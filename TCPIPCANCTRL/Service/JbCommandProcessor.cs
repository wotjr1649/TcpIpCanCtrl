using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TcpIpCanCtrl.Event;
using TcpIpCanCtrl.Interface;
using TcpIpCanCtrl.Model;

namespace TcpIpCanCtrl.Service
{
    /// <summary>
    /// 수신 프레임 디스패처.
    /// - RB(바코드) 누적/완성 → OnRcvBar(주소, 바코드)
    /// - RF/RC/RS/ME → OnRcvUnit(JbFrame)
    /// </summary>
    internal class JbCommandProcessor : IDisposable
    {
        private readonly Channel<JbFrame> _channel;
        private readonly CancellationTokenSource _cts;
        private readonly Task _worker;

        // 구독 해제용 보관
        private readonly IFrameSource<JbFrame> _source;
        private readonly EventHandler<FrameReceivedEventArgs<JbFrame>> _rxHandler;

        // --- 외부 이벤트
        internal event EventHandler<OnRcvBarEventArgs> OnRcvBar;
        internal event EventHandler<OnRcvUnitEventArgs> OnRcvUnit;

        // --- Main/Sub별 디스패치 맵
        private readonly Dictionary<(char Main, char Sub), Action<JbFrame>> _dispatch;
        // --- RB 전용 버퍼 (주소별 StringBuilder)
        private readonly Dictionary<string, StringBuilder> _rbBuffers = new Dictionary<string, StringBuilder>();

        internal JbCommandProcessor(IFrameSource<JbFrame> frameSource, int queueCapacity = 1024)
        {
            // 0) 초기화
            _cts = new CancellationTokenSource();

            // 1) 채널 설정
            _channel = Channel.CreateBounded<JbFrame>(new BoundedChannelOptions(capacity: queueCapacity)
            {
                SingleReader = true, // 단일 SendLoop Task만 읽을 것임을 명시 (성능 최적화)
                SingleWriter = false, // 여러 SendCommandAsync 호출이 가능하므로 false
                FullMode = BoundedChannelFullMode.Wait
            });

            // 2) 외부 프레임 수신 → 채널로 쓰기
            _source = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
            _source.FrameReceived += _rxHandler = async (s, e) // → Orchestrator(FrameReceived2) 구독
                => await _channel.Writer.WriteAsync(e.Frame, _cts.Token).ConfigureAwait(false); // → 내부 채널에 JbFrame 저장 && 포화 시 대기(백프레셔)

            // 3) dispatchMap 초기화
            _dispatch = new Dictionary<(char, char), Action<JbFrame>>
            {
                {('R','B'), HandleRb },
                {('R','F'), HandleUnit },
                {('R','C'), HandleUnit },
                {('R','S'), HandleUnit },
                {('M','E'), HandleUnit },
            };

            // 4) 워커 시작
            _worker = Task.Run(WorkerLoopAsync);
        }

        private async Task WorkerLoopAsync()
        {
            var reader = _channel.Reader;
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                while (reader.TryRead(out var frame))
                    try { if (_dispatch.TryGetValue((frame.pMain, frame.pSub), out var action)) action(frame); } catch { /* 개별 프레임 오류는 상위로 전파하지 않음 */ }
        }

        // --- RB 전용 핸들러
        private void HandleRb(JbFrame f)
        {
            //안전성 체크
            if (string.IsNullOrEmpty(f.pAddr) || string.IsNullOrEmpty(f.pData)) return;

            // 주소는 프레임 헤더에서 받는다
            var address = f.pAddr;

            // 서브타입은 pData[0]
            var subType = f.pData[0] - '0';
            var segment = subType == 1 ? string.Empty : f.pData.Substring(1);

            if (!_rbBuffers.TryGetValue(address, out var sb))
            {
                sb = new StringBuilder();
                _rbBuffers[address] = sb;
            }
            if (segment.Length > 0)
                sb.Append(segment);

            // 길이 불일치 시 종료
            if (subType != segment.Length)
            {
                // 완료
                var barcode = sb.ToString();
                _rbBuffers.Remove(address);
                OnRcvBar?.Invoke(this, new OnRcvBarEventArgs(address, barcode));
            }
        }

        private void HandleUnit(JbFrame f) => OnRcvUnit?.Invoke(this, new OnRcvUnitEventArgs(f));

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _channel.Writer.TryComplete(); } catch { }
            try { _worker.Wait(200); } catch { }

            // 구독 해제
            if (_source != null && _rxHandler != null)
                _source.FrameReceived -= _rxHandler;

            _cts.Dispose();
        }
    }
}
