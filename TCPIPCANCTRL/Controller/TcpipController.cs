using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TcpIpCanCtrl.Command;
using TcpIpCanCtrl.Event;
using TcpIpCanCtrl.Model;
using TcpIpCanCtrl.Service;
using TcpIpCanCtrl.Transport;
using TcpIpCanCtrl.Util;

namespace TcpIpCanCtrl.Controller
{
    /// <summary>
    /// PC→JB 명령 송신 컨트롤러
    /// JB 연결 컨트롤러
    /// </summary>
    public class TcpIpController : IDisposable
    {
        private PipelineTransport _transport;
        private CommunicationOrchestrator _orchestrator;
        private JbCommandProcessor _processor;
        private CommandHandler _handler;
        private CancellationTokenSource _cts;
        private bool _disposed;

        private readonly object _lcdLock = new object(); // 전용 락 오브젝트
        private readonly string _localIp; // 로컬 IP 주소
        private readonly string _jbAddress; // JB 박스 주소
        private readonly int _jbPort; // JB 박스 포트 번호
        private bool hDevice = false; // 장치 핸들 (연결 상태 표시)

        public readonly int _jbIndex; // JB 인덱스

        //private readonly EventHandler<FrameReceivedEventArgs<JbFrame>> _FrameReceived;
        private readonly EventHandler<OnErrorEventArgs> _OnError;
        private readonly EventHandler<OnRcvUnitEventArgs> _OnRcvUnit;
        private readonly EventHandler<OnRcvBarEventArgs> _OnRcvBar;        

        // → 4단계: Controller 레벨에서 재발행할 이벤트
        //private event EventHandler<FrameReceivedEventArgs<JbFrame>> FrameReceived;
        public event EventHandler<OnErrorEventArgs> OnError;
        public event EventHandler<OnRcvUnitEventArgs> OnRcvUnit;
        public event EventHandler<OnRcvBarEventArgs> OnRcvBar;

        /// <summary>
        /// ITransport 인스턴스를 주입받아 초기화합니다.
        /// 반드시 StartAsync 호출 전까지 Dispose하지 마십시오.
        /// </summary>
        public TcpIpController(int Jb_Index, string JB_Address, string Local_Address)
        {
            _jbIndex = Jb_Index;
            _localIp = Local_Address;

            var parts = JB_Address.Split(':');
            if (parts.Length is 2 && short.TryParse(parts[1], out short port))
            {
                _jbAddress = parts[0];
                _jbPort = port;
            }
            else throw new ArgumentException("JB_Address는 'IP:Port' 형식이어야 합니다.");

            _OnError = (sender, args) => OnError?.Invoke(this, args);
            _OnRcvBar = (sender, args) => OnRcvBar?.Invoke(this, args);
            _OnRcvUnit = (sender, args) => OnRcvUnit?.Invoke(this, args);
        }

        #region JB_BOX OPEN/CLOSE

        /// <summary>
        /// JB 박스와 통신을 시작 합니다.
        /// 수신 루프를 시작합니다. 반드시 호출해야 수신 이벤트가 발생합니다.
        /// </summary>
        public bool JB_OPEN()
        {
            if (hDevice)
                return false;

            try
            {
                _cts = new CancellationTokenSource();
                _transport = PipelineTransport.CreateAsync(_jbAddress, _jbPort, _cts.Token).GetAwaiter().GetResult();

                _orchestrator = new CommunicationOrchestrator(_transport, this._jbIndex);
                _handler = new CommandHandler(_orchestrator, this);
                _processor = new JbCommandProcessor(_orchestrator);

                _orchestrator.OnError += _OnError;
                _processor.OnRcvBar += _OnRcvBar;
                _processor.OnRcvUnit += _OnRcvUnit;

                _orchestrator.Start();

                (hDevice, _disposed) = (true, false);
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, new OnErrorEventArgs(ex));
                Console.WriteLine($"연결 실패 {ex}");
                return false;
            }
        }

        public async Task<bool> JB_OPENAsync()
        {
            if (hDevice) return false;

            _cts = new CancellationTokenSource();

            try
            {
                _transport = await PipelineTransport.CreateAsync(_jbAddress, _jbPort, _cts.Token).ConfigureAwait(false);

                _orchestrator = new CommunicationOrchestrator(_transport, this._jbIndex);
                _handler = new CommandHandler(_orchestrator, this);
                _processor = new JbCommandProcessor(_orchestrator);

                _orchestrator.OnError += _OnError;
                _processor.OnRcvBar += _OnRcvBar;
                _processor.OnRcvUnit += _OnRcvUnit;

                await _orchestrator.StartAsync().ConfigureAwait(false);
                (hDevice, _disposed) = (true, false);
                return true;
            }
            catch
            {
                OnError?.Invoke(this, new OnErrorEventArgs(new Exception($"{_jbIndex}번 JB {_jbAddress}:{_jbPort} IP 연결 실패")));
                Dispose();
                return false;
            }
        }

        /// <summary>
        /// JB 박스와 통신을 종료 합니다.
        /// 수신 루프를 종료합니다. 반드시 호출해야 수신 이벤트가 종료됩니다.
        /// </summary>
        public void JB_CLOSE() => Dispose();

        public bool JB_CONNECT() => hDevice && _transport != null && _transport.IsConnected;

        #endregion

        #region Command_Func

        /// <summary>PORT_ALL_ON</summary>
        public void PortAllOn(int canId)
            => _handler.Handle(new PortAllOnCommand(canId));

        /// <summary>PORT_ALL_OFF</summary>
        public void PortAllOff(int canId)
            => _handler.Handle(new PortAllOffCommand(canId));

        /// <summary>UNIT_ONE_ON</summary>
        public void UnitOneOn(string payload)
            => _handler.Handle(new UnitOneOnCommand(payload));

        /// <summary>UNIT_ONE_OFF</summary>
        public void UnitOneOff(string address)
            => _handler.Handle(new UnitOneOffCommand(address));

        /// <summary>UNIT_SET</summary>
        public void UnitSet(string address)
            => _handler.Handle(new UnitSetCommand(address));

        /// <summary>BCRI_ON</summary>
        public void BcriOn(string address)
            => _handler.Handle(new BcriOnCommand(address));

        /// <summary>BCRI_OFF</summary>
        public void BcriOff(string address)
            => _handler.Handle(new BcriOffCommand(address));

        /// <summary>LCD_DISP</summary>
        public void LcdDisp(string value)
        {
            lock (_lcdLock)
            {
                _handler.Handle(new LcdAcCommand(value.Substring(0, 4)), false);
                _handler.Handle(new LcdAlCommand(value));
            }
        }

        /// <summary>DISP_5SND</summary>
        public void DISP_5SND(string value)
            => _handler.Handle(new Disp5SndCommand(value));

        /// <summary>DISP_10SND</summary>
        public void Disp10Snd(string value)
            => _handler.Handle(new Disp10SndCommand(value));

        /// <summary>DISP_16SND</summary>
        public void Disp16Snd(string value)
            => _handler.Handle(new Disp16SndCommand(value));

        internal void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TcpIpController));
            if (_transport == null || !_transport.IsConnected)
                throw new InvalidOperationException($"제어 PC가 {_jbIndex}번 JB BOX와 연결되지 않았습니다.");
        }

        #endregion


        /* ---------- 정리 ---------- */
        public void Dispose()
        {
            if (_disposed || !hDevice) return;

            // 이벤트 핸들러 해제
            if (_orchestrator != null)
            {
                //_orchestrator.FrameReceived -= _FrameReceived;
                _orchestrator.OnError -= _OnError;
            }
            if (_processor != null)
            {
                _processor.OnRcvBar -= _OnRcvBar;
                _processor.OnRcvUnit -= _OnRcvUnit;
            }

            // 객체 Dispose
            _processor?.Dispose();
            _orchestrator?.Dispose();
            _transport?.Dispose();

            // CancellationTokenSource 처리
            _cts?.Cancel();
            _cts?.Dispose(); // null-conditional로 변경하여 안정성 확보

            (hDevice, _disposed) = (false, true); // 장치 핸들 초기화
            Console.WriteLine($"[DEBUG] {nameof(TcpIpCanCtrl)} disposed.");
        }
    }
}
