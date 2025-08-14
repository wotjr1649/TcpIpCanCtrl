using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
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
    public class TCPIPCTRL : IDisposable
    {
        private PipelineTransport _transport;
        private CommunicationOrchestrator _orchestrator;
        private JbCommandProcessor _processor;
        private GenericCommandHandler _handler;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private bool _disposed;
        private readonly object _syncRoot = new object();
        private string mvarLocalIP = string.Empty; // 로컬 IP 주소 (외부 입력용)
        private string mvarPortNo = "1"; // 포트 번호 (초기값 "1") (외부 입력용)
        private int mvarJBIndex; // JB 인덱스
        private bool mvarReturnEvents = true; // 이벤트 반환 여부
        private bool mvarAutoSndRcv = true; // 자동 송수신 여부
        private bool hDevice = false; // 장치 핸들 (연결 상태 표시)
        private readonly EventHandler<FrameReceivedEventArgs<JbFrame>> _FrameReceived;
        private readonly EventHandler<OnErrorEventArgs> _OnError;
        private readonly EventHandler<OnRcvUnitEventArgs> _OnRcvUnit;
        private readonly EventHandler<OnRcvBarEventArgs> _OnRcvBar;

        // 외부 설정용 변수
        public string JB_PORT { get => mvarPortNo; set => mvarPortNo = value; }
        public int JB_INDEX { get => mvarJBIndex; set => mvarJBIndex = value; }
        public string PC_IP { get => mvarLocalIP; set => mvarLocalIP = value; }
        public bool ReturnEvents { get => mvarReturnEvents; set => mvarReturnEvents = value; }
        public bool AutoRcv { get => mvarAutoSndRcv; set => mvarAutoSndRcv = value; }
        public bool JB_CONNECT => hDevice != false; // hDevice가 0이 아니면 연결된 것으로 간주

        // → 4단계: Controller 레벨에서 재발행할 이벤트
        private event EventHandler<FrameReceivedEventArgs<JbFrame>> FrameReceived;
        public event EventHandler<OnErrorEventArgs> OnError;
        public event EventHandler<OnRcvUnitEventArgs> OnRcvUnit;
        public event EventHandler<OnRcvBarEventArgs> OnRcvBar;

        /// <summary>
        /// ITransport 인스턴스를 주입받아 초기화합니다.
        /// 반드시 StartAsync 호출 전까지 Dispose하지 마십시오.
        /// </summary>
        public TCPIPCTRL()
        {
            //Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _FrameReceived = (sender, args) => FrameReceived?.Invoke(this, args);
            _OnError += (sender, args) => OnError?.Invoke(this, args);
            _OnRcvBar += (sender, args) => OnRcvBar?.Invoke(this, args);
            _OnRcvUnit += (sender, args) => OnRcvUnit?.Invoke(this, args);
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


            var parts = mvarPortNo.Split(':');
            if (parts.Length != 2 || !short.TryParse(parts[1], out short port))
                return false;

            try
            {
                _transport = PipelineTransport.CreateAsync(parts[0], port, cts.Token).GetAwaiter().GetResult();
                _orchestrator = new CommunicationOrchestrator(_transport);
                _handler = new GenericCommandHandler(_orchestrator, this);
                _processor = new JbCommandProcessor(_orchestrator);

                _orchestrator.FrameReceived += _FrameReceived;
                _orchestrator.OnError += _OnError;
                _processor.OnRcvBar += _OnRcvBar;
                _processor.OnRcvUnit += _OnRcvUnit;

                hDevice = true;
                if (AutoRcv) _orchestrator.Start();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"연결 실패 {ex}");
                return false;
            }
        }

        /// <summary>
        /// JB 박스와 통신을 종료 합니다.
        /// 수신 루프를 종료합니다. 반드시 호출해야 수신 이벤트가 종료됩니다.
        /// </summary>
        public bool JB_CLOSE()
        {
            if (!hDevice) return false;

            Dispose();

            hDevice = false;

            return true;
        }

        #endregion

        #region Command_Func

        /// <summary>PORT_ALL_ON</summary>
        public void PortAllOn(int canId)
            => _handler.Handle(new PortAllOnCommand(canId));

        /// <summary>PORT_ALL_OFF</summary>
        public void PortAllOff(int canId)
            => _handler.Handle(new PortAllOffCommand(canId));

        /// <summary>UNIT_ONE_ON</summary>
        public void UnitOneOn(string payload, string cap = "")
            => _handler.Handle(new UnitOneOnCommand(payload));

        /// <summary>UNIT_ONE_OFF</summary>
        public void UnitOneOff(string address)
            => _handler.Handle(new UnitOneOffCommand(address));

        /// <summary>BCRI_ON</summary>
        public void BcriOn(string address)
            => _handler.Handle(new BcriOnCommand(address));

        /// <summary>BCRI_OFF</summary>
        public void BcriOff(string address)
            => _handler.Handle(new BcriOffCommand(address));

        /// <summary>LCD_DISP</summary>
        public void LcdDisp(string value)
        {
            lock (_syncRoot)
            {
                _handler.Handle(new LcdAcCommand(value.Substring(0, 4)));
                _handler.Handle(new LcdAlCommand(value));
            }
        }

        /// <summary>DISP_10SND</summary>
        public void Disp10Snd(string value)
            => _handler.Handle(new Disp10SndCommand(value));

        /// <summary>DISP_16SND</summary>
        public void Disp16Snd(string value)
            => _handler.Handle(new Disp16SndCommand(value));

        /// <summary>UNIT_SET</summary>
        public void UnitSet(string address)
            => _handler.Handle(new UnitSetCommand(address));

        internal void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TCPIPCTRL));
            if (!_transport.IsConnected)
                throw new Exception($"제어 PC가 {JB_INDEX}번 JB BOX와 연결되지 않았습니다.");
        }

        #endregion


        /* ---------- 정리 ---------- */
        public void Dispose()
        {
            if (_disposed) return;

            _orchestrator.FrameReceived -= _FrameReceived;
            _orchestrator.OnError -= _OnError;
            _processor.OnRcvBar -= _OnRcvBar;
            _processor.OnRcvUnit -= _OnRcvUnit;

            cts.Cancel();

            _processor?.Dispose();
            _orchestrator?.Dispose();
            _transport?.Dispose();

            cts.Dispose();
            _disposed = true;

        }
    }
}
