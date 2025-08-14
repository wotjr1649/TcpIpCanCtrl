using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TcpIpCanCtrl.Event;
using TcpIpCanCtrl.Interface;
using TcpIpCanCtrl.Model;

namespace TcpIpCanCtrl.Service
{
    internal class JbCommandProcessor : IDisposable
    {
        private readonly Channel<JbFrame> _channel;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _worker;


        // --- RB 전용 버퍼 (주소별 StringBuilder)
        private readonly Dictionary<string, StringBuilder> _rbBuffers
            = new Dictionary<string, StringBuilder>();

        // --- 외부 이벤트
        internal event EventHandler<OnRcvBarEventArgs> OnRcvBar;
        internal event EventHandler<OnRcvUnitEventArgs> OnRcvUnit;

        // --- Main/Sub별 디스패치 맵
        private readonly Dictionary<(char Main, char Sub), Action<JbFrame>> _dispatchMap;

        internal JbCommandProcessor(IFrameSource<JbFrame> frameSource)
        {
            // 1) 채널 설정
            _channel = Channel.CreateBounded<JbFrame>(new BoundedChannelOptions(capacity: 300)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true, // 단일 SendLoop Task만 읽을 것임을 명시 (성능 최적화)
                SingleWriter = false // 여러 SendCommandAsync 호출이 가능하므로 false
            });

            // 2) 외부 프레임 수신 → 채널로 쓰기
            frameSource.FrameReceived += (s, e) => // → Orchestrator(FrameReceived2) 구독
                _channel.Writer.TryWrite(e.Frame); // → 내부 채널에 JbFrame 저장

            // 3) dispatchMap 초기화
            _dispatchMap = new Dictionary<(char, char), Action<JbFrame>>
            {
                {('R','B'), f=> HandleRbFrame(f)},
                {('R','F'), f => HandleUnitFrame(f) },
                {('R','C'), f => HandleUnitFrame(f) },
                {('R','S'), f => HandleUnitFrame(f) },
                {('M','E'), f => HandleUnitFrame(f) },
            };

            // 4) 워커 시작
            _worker = Task.Run(WorkerLoop);
        }

        private async Task WorkerLoop()
        {
            var reader = _channel.Reader;
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                while (reader.TryRead(out var frame))
                {
                    var key = (frame.pMain, frame.pSub);
                    if (_dispatchMap.TryGetValue(key, out var action))
                        action(frame);
                }

            }
        }

        // --- RB 전용 핸들러
        private void HandleRbFrame(JbFrame f)
        {
            var raw = f.pData?.Trim();
            if (string.IsNullOrEmpty(raw) || raw.Length <= 2) return;

            var payload = raw.Substring(2);
            if (payload.Length < 6) return;

            var body = payload.Substring(6);
            if (!int.TryParse(body.Substring(0, 1), out int lenField)) return;

            var dataPart = body.Substring(1);
            var address = payload.Substring(0, 4);

            if (!_rbBuffers.TryGetValue(address, out var sb))
            {
                sb = new StringBuilder();
                _rbBuffers[address] = sb;
            }
            sb.Append(dataPart);

            // 길이 불일치 시 종료
            if (dataPart.Length != lenField)
            {
                var barcode = sb.ToString();
                _rbBuffers.Remove(address);
                OnRcvBar?.Invoke(this, new OnRcvBarEventArgs(address, barcode));
            }
        }

        private void HandleUnitFrame(JbFrame f)
        {
            Console.WriteLine($"JB 프로세싱 진입 : {f.pMain}{f.pSub}/{f.pData}");
            OnRcvUnit?.Invoke(this, new OnRcvUnitEventArgs(f));
        }
        //=> OnRcvUnit?.Invoke(this, new OnRcvUnitEventArgs(f));


        public void Dispose() { _cts.Cancel(); try { _worker.Wait(); } catch { } _channel.Writer.Complete(); }

    }
}
