using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TcpIpCanCtrl.Event;
using TcpIpCanCtrl.Model;

// ★★★ 외부로 노출될 Controller 네임스페이스
namespace TcpIpCanCtrl.Controller
{
    /// <summary>
    /// 여러 개의 TCPIPCTRL(JB 박스) 인스턴스를 총괄 관리하는 클래스.
    /// 이 라이브러리의 주 사용 진입점입니다.
    /// </summary>
    public sealed class DeviceManager : IDisposable // ★ sealed 키워드로 상속 방지
    {
        private sealed class Entry
        {
            internal TcpIpController Controller;
            // 구독 핸들러 보관
            internal EventHandler<OnErrorEventArgs> OnErrorHandler;
            internal EventHandler<OnRcvUnitEventArgs> OnRcvUnitHandler;
            internal EventHandler<OnRcvBarEventArgs> OnRcvBarHandler;
        }

        private readonly ConcurrentDictionary<int, Entry> _devices =
            new ConcurrentDictionary<int, Entry>();

        private volatile bool _disposed;

        // ★ 상위 앱으로 재발행(디바이스 인덱스 포함)
        public event EventHandler<DeviceErrorEventArgs> DeviceError;
        public event EventHandler<DeviceRcvUnitEventArgs> DeviceRcvUnit;
        public event EventHandler<DeviceRcvBarEventArgs> DeviceRcvBar;

        #region Add / Remove Device

        /// <summary>
        /// 새로운 JB 박스 장치를 시스템에 추가하고 연결을 시작합니다.
        /// </summary>
        /// <param name="jbIndex">고유한 JB 박스 인덱스</param>
        /// <param name="jbAddress">JB 박스의 IP 주소</param>
        /// <param name="localAddress">PC의 IP 주소</param>
        /// <returns>성공적으로 생성 및 연결된 TCPIPCTRL 인스턴스, 실패 시 null</returns>
        public TcpIpController AddDevice(int jbIndex, string jbAddress, string localAddress)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DeviceManager));

            if (_devices.TryGetValue(jbIndex, out var existing))
                return existing.Controller; // 이미 존재하면 그대로 반환

            var ctrl = new TcpIpController(jbIndex, jbAddress, localAddress);
            var e = new Entry { Controller = ctrl };

            // 1) 구독 핸들러 구성(디바이스 인덱스 포함 재발행)
            ctrl.OnError += e.OnErrorHandler = (s, args) => DeviceError?.Invoke(this, new DeviceErrorEventArgs(jbIndex, args));
            ctrl.OnRcvUnit += e.OnRcvUnitHandler = (s, args) => DeviceRcvUnit?.Invoke(this, new DeviceRcvUnitEventArgs(jbIndex, args));
            ctrl.OnRcvBar += e.OnRcvBarHandler = (s, args) => DeviceRcvBar?.Invoke(this, new DeviceRcvBarEventArgs(jbIndex, args));


            // 2) 연결 시도
            if (!ctrl.JB_OPEN())
            {
                // 실패 시 구독 해제 후 Dispose
                if (e.OnErrorHandler != null) ctrl.OnError -= e.OnErrorHandler;
                if (e.OnRcvUnitHandler != null) ctrl.OnRcvUnit -= e.OnRcvUnitHandler;
                if (e.OnRcvBarHandler != null) ctrl.OnRcvBar -= e.OnRcvBarHandler;

                ctrl.Dispose();
                return null;
            }

            // 딕셔너리 등록
            if (!_devices.TryAdd(jbIndex, e))
            {
                // 경쟁으로 실패하면 롤백
                ctrl.OnError -= e.OnErrorHandler;
                ctrl.OnRcvUnit -= e.OnRcvUnitHandler;
                ctrl.OnRcvBar -= e.OnRcvBarHandler;
                ctrl.Dispose();
                Console.WriteLine($"디바이스 매니저로 jb 박스 추가");
                return _devices.TryGetValue(jbIndex, out var winner) ? winner.Controller : null;
            }

            return ctrl;
        }

        public async Task<TcpIpController> AddDeviceAsync(int jbIndex, string jbAddress, string localAddress)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DeviceManager));
            if (_devices.TryGetValue(jbIndex, out var ex)) return ex.Controller;


            var ctrl = new TcpIpController(jbIndex, jbAddress, localAddress);
            var e = new Entry { Controller = ctrl };

            // 1) 구독 핸들러 구성(디바이스 인덱스 포함 재발행)
            ctrl.OnError += e.OnErrorHandler = (s, args) => DeviceError?.Invoke(this, new DeviceErrorEventArgs(jbIndex, args));
            ctrl.OnRcvUnit += e.OnRcvUnitHandler = (s, args) => DeviceRcvUnit?.Invoke(this, new DeviceRcvUnitEventArgs(jbIndex, args));
            ctrl.OnRcvBar += e.OnRcvBarHandler = (s, args) => DeviceRcvBar?.Invoke(this, new DeviceRcvBarEventArgs(jbIndex, args));

            // 2) 연결 시도
            if (!await ctrl.JB_OPENAsync().ConfigureAwait(false))
            {
                // 실패 시 구독 해제 후 Dispose
                if (e.OnErrorHandler != null) ctrl.OnError -= e.OnErrorHandler;
                if (e.OnRcvUnitHandler != null) ctrl.OnRcvUnit -= e.OnRcvUnitHandler;
                if (e.OnRcvBarHandler != null) ctrl.OnRcvBar -= e.OnRcvBarHandler;
                ctrl.Dispose();
                return null;
            }

            // 딕셔너리 등록
            if (!_devices.TryAdd(jbIndex, e))
            {
                // 경쟁으로 실패하면 롤백
                ctrl.OnError -= e.OnErrorHandler;
                ctrl.OnRcvUnit -= e.OnRcvUnitHandler;
                ctrl.OnRcvBar -= e.OnRcvBarHandler;
                ctrl.Dispose();
                return _devices.TryGetValue(jbIndex, out var winner) ? winner.Controller : null;
            }
            return ctrl;
        }

        public bool RemoveDevice(int jbIndex)
        {
            if (_devices.TryRemove(jbIndex, out var e))
            {
                try
                {
                    if (e.Controller != null)
                    {
                        // 구독 해제
                        if (e.OnErrorHandler != null) e.Controller.OnError -= e.OnErrorHandler;
                        if (e.OnRcvUnitHandler != null) e.Controller.OnRcvUnit -= e.OnRcvUnitHandler;
                        if (e.OnRcvBarHandler != null) e.Controller.OnRcvBar -= e.OnRcvBarHandler;

                        e.Controller.JB_CLOSE(); // JB 박스 연결 종료
#if DEBUG
                        Console.WriteLine($"디바이스 매니저로 jb 박스 제거완료 {jbIndex}");
#endif

                    }
                }
                catch { /* 로깅 권장 */ }
                return true;
            }
            return false;
        }

        #endregion

        #region Execute Command

        /// <summary>
        /// 장치가 연결된 상태일 때만 지정된 동작을 수행합니다.
        /// </summary>
        /// <param name="jbIndex">명령을 내릴 장치의 인덱스</param>
        /// <param name="commandAction">장치 컨트롤러를 받아 실제 명령을 실행할 Action 델리게이트</param>
        public void ExecuteIfConnected(int jbIndex, Action<TcpIpController> commandAction)
        {
            if (commandAction == null) throw new ArgumentNullException(nameof(commandAction));

            if (_devices.TryGetValue(jbIndex, out var e) && e.Controller != null && e.Controller.JB_CONNECT())
            {
                commandAction(e.Controller);
            }
            else
            {
                // 로깅 권장
                // Console.WriteLine($"Device {jbIndex} not connected.");
            }
        }

        public Task ExecuteIfConnectedAsync(int jbIndex, Action<TcpIpController> action, CancellationToken ct = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (_devices.TryGetValue(jbIndex, out var e) && e.Controller != null && e.Controller.JB_CONNECT())
                action(e.Controller); 
            return Task.CompletedTask;
        }
        public Task ExecuteIfConnectedAsync(int jbIndex, Func<TcpIpController, Task> actionAsync, CancellationToken ct = default)
        {
            if (actionAsync == null) throw new ArgumentNullException(nameof(actionAsync));

            if (_devices.TryGetValue(jbIndex, out var e) && e.Controller != null && e.Controller.JB_CONNECT())
                return actionAsync(e.Controller);
            return Task.CompletedTask;
        }
        //public async Task ExecuteIfConnectedAsync(int jbIndex, Func<TcpIpController, CancellationToken, Task> commandActionAsync, CancellationToken ct = default)
        //{
        //    if (commandActionAsync == null) throw new ArgumentNullException(nameof(commandActionAsync));

        //    if (_devices.TryGetValue(jbIndex, out var e) && e.Controller != null && e.Controller.JB_CONNECT())
        //        await commandActionAsync(e.Controller, ct).ConfigureAwait(false);
        //}

        #endregion


        #region Helpers

        /// <summary>
        /// 인덱스로 특정 장치 제어기를 가져옵니다.
        /// </summary>
        /// <param name="jbIndex">가져올 장치의 인덱스</param>
        /// <param name="controller">해당 인덱스의 TCPIPCTRL 인스턴스, 없으면 null</param>
        /// <returns>해당 인덱스의 TCPIPCTRL 인스턴스, 없으면 null</returns>
        public bool TryGetDevice(int jbIndex, out TcpIpController controller)
        {
            if (_devices.TryGetValue(jbIndex, out var e) && e.Controller != null)
            {
                controller = e.Controller;
                return true;
            }
            controller = null;
            return false;
        }

        public int[] GetConnectedIndices()
        {
            return _devices
                .Where(kv => kv.Value.Controller != null && kv.Value.Controller.JB_CONNECT())
                .Select(kv => kv.Key)
                .ToArray();
        }


        /// <summary>
        /// 관리 중인 모든 장치와의 연결을 종료하고 리소스를 해제합니다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kv in _devices.ToArray())
                RemoveDevice(kv.Key);

            _devices.Clear();
        }

        #endregion
    }
}