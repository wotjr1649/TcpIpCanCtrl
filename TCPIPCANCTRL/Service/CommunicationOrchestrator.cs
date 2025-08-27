using System;
using System.Buffers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TcpIpCanCtrl.Event;
using TcpIpCanCtrl.Interface;
using TcpIpCanCtrl.Model;
using TcpIpCanCtrl.Transport;

namespace TcpIpCanCtrl.Service
{
    /// <summary>
    /// 송수신 오케스트레이션 담당.
    /// - 송신: Channel 직렬화, PreRegister → Send → Arm(2s)
    /// - 수신: RB(스캐너)면 그냥 재발행, 그 외 응답은 TimeoutManager.Complete
    /// - 타임아웃: TimeoutManager.RequestTimedOut 시 가상 프레임 재발행(해당 JB만)
    /// </summary>
    internal class CommunicationOrchestrator : IDisposable, IFrameSource<JbFrame>
    {
        private readonly struct SendItem
        {
            internal readonly IMemoryOwner<byte> Bytes;
            internal readonly TaskCompletionSource<bool> Tcs;
            internal readonly bool RequireResponse;
            internal readonly JbFrame OriginalFrame;

            internal SendItem(IMemoryOwner<byte> bytes, TaskCompletionSource<bool> tcs, bool reqResponse, JbFrame frame)
            {
                Bytes = bytes; Tcs = tcs; RequireResponse = reqResponse; OriginalFrame = frame;
            }
        }

        private readonly PipelineTransport _transport;
        private readonly Channel<SendItem> _sendQueue;
        private readonly CancellationTokenSource _cts;
        private readonly TimeoutManager _timeout = TimeoutManager.Instance;


        private readonly Task _sendLoop;
        private readonly int _jbIndex;
        private readonly TimeSpan _sendInterval = TimeSpan.FromMilliseconds(100);

        private bool _started;

        // → 2단계: Orchestrator 레벨에서 재발행할 이벤트
        public event EventHandler<FrameReceivedEventArgs<JbFrame>> FrameReceived;
        public event EventHandler<OnErrorEventArgs> OnError;

        // 이벤트 핸들러 참조(구독 해제용)
        private EventHandler<RequestTimedOutEventArgs> _toHandler;
        private EventHandler<FrameReceivedEventArgs<JbFrame>> _rxHandler;
        private EventHandler<OnErrorEventArgs> _errHandler;

        internal CommunicationOrchestrator(PipelineTransport transport, int jbIndex, int queueCapacity = 1024)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));

            _jbIndex = jbIndex;
            _cts = new CancellationTokenSource();

            _sendQueue = Channel.CreateBounded<SendItem>(new BoundedChannelOptions(queueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait   // ★ 백프레셔
            });

            // 수신 → 재발행 + 타임아웃 완료
            // → transport(FrameReceived1) 구독    → FrameReceived2로 재발행
            _transport.FrameReceived += _rxHandler = (sender, args) =>
            {
                if (IsScannerAuto(args.Frame)) // RB(스캐너)면 그냥 재발행
                {
                    FrameReceived?.Invoke(this, args); // Orchestrator 레벨에서 재발행
                    return;
                }

                if (_timeout.TryComplete(args.Frame)) // 응답 프레임이면 타임아웃 매니저에서 헤드 제거
                {
                    FrameReceived?.Invoke(this, args); // Orchestrator 레벨에서 재발행
                    return;
                }
                FrameReceived?.Invoke(this, args);
            };

            // Transport 오류 발생 시 Orchestrator 레벨로 전달
            _transport.OnError += _errHandler = (sender, args) => OnError?.Invoke(this, args);

            // 타임아웃 발생 시, 가상 프레임을 주입하여 재발행
            _timeout.RequestTimedOut += _toHandler = (sender, args) =>
            {
                // ★ 핵심 필터: 내 jbIndex가 아니면 무시
                if (args.OriginalFrame.pIndex != _jbIndex) return;                

                // 필요하면 DeviceKey(예: "1-1000")까지 비교 가능:
                // if (args.DeviceKey != $"{_jbIndex}-{args.OriginalFrame.pAddr}") return;

                FrameReceived?.Invoke(this, new FrameReceivedEventArgs<JbFrame>(new JbFrame(args.OriginalFrame.pIndex, args.OriginalFrame.pAddr, 'M', 'E', "2000")));
            };

            _sendLoop = Task.Run(SendLoopAsync);
        }

        internal void Start()
        {
            if (_started) throw new InvalidOperationException("Already started");
            _ = _transport.StartReceiveLoopAsync(_jbIndex, _cts.Token);
            _started = true;
        }

        /// <summary>수신 루프 시작(Task 반환으로 수명/예외 전파)</summary>
        internal Task StartAsync()
        {
            if (_started) throw new InvalidOperationException("Already started");
            _started = true;

            return _transport.StartReceiveLoopAsync(_jbIndex, _cts.Token);
        }


        #region 송신 API

        /// /// <summary>Fire-and-forget 큐 등록</summary>
        internal void EnqueueCommand(IMemoryOwner<byte> Bytes, JbFrame jbFrame, bool requireResponse = false)
              => _ = EnqueueCommandAsync(Bytes, jbFrame, requireResponse, CancellationToken.None);

        /// <summary>큐 등록 + 송신 완료/실패 대기</summary>
        internal async Task EnqueueCommandAsync(IMemoryOwner<byte> bytes, JbFrame frame, bool requireResponse, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var item = new SendItem(bytes, tcs, requireResponse, frame);

            CancellationTokenRegistration reg = default;
            if (ct.CanBeCanceled) reg = ct.Register(() => tcs.TrySetCanceled(ct), useSynchronizationContext: true);

            try
            {
                await _sendQueue.Writer.WriteAsync(item, ct).ConfigureAwait(false);
                await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                reg.Dispose();
            }
        }
        #endregion


        #region 송신 루프

        private async Task SendLoopAsync()
        {
            var reader = _sendQueue.Reader;

            try
            {
                while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var item))
                    {
                        try
                        {
#if DEBUG
                                // 디버깅용 간략 로깅 (할당 최소화)
                                var len = item.Bytes.Memory.Length;
                                var previewLen = len < 64 ? len : 64;
                                Console.WriteLine("[COMM ▶] TX 시작 len=" + len + ", preview=" + previewLen);
#endif

                            if (item.RequireResponse) _timeout.PreRegister(item.OriginalFrame);

                            // 네트워크 송신
                            try
                            {
                                await _transport.SendAsync(item.Bytes.Memory, _cts.Token).ConfigureAwait(false);
                            }
                            finally { try { item.Bytes.Dispose(); } catch { } }  // 항상 메모리 반납

                            if (item.RequireResponse) _timeout.Arm(item.OriginalFrame); // 실제 송신 직후 카운트다운

                            item.Tcs.TrySetResult(true);
#if DEBUG
                            Console.WriteLine("[COMM ▶] TX 완료");
#endif
                        }
                        catch (OperationCanceledException oce)
                        {
                            // 취소(종료 플로우 포함)
                            if (item.RequireResponse) _timeout.Cancel(item.OriginalFrame);
                            item.Tcs.TrySetCanceled(oce.CancellationToken);
                        }
                        catch (Exception ex)
                        {
                            if (item.RequireResponse) _timeout.Cancel(item.OriginalFrame);
                            item.Tcs.TrySetException(ex);
                            OnError?.Invoke(this, new OnErrorEventArgs(ex));
                        }

                        // 스로틀링(비동기 지연) — 빠른 연속 송신 방지
                        if (!_cts.IsCancellationRequested)
                            await Task.Delay(_sendInterval, _cts.Token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                // 종료 시 잔여 항목 드레인 & 취소
                while (reader.TryRead(out var rest))
                {
                    try { rest.Bytes.Dispose(); } catch { }
                    if (rest.RequireResponse) _timeout.Cancel(rest.OriginalFrame);
                    rest.Tcs.TrySetCanceled();
                }
            }
        }
        #endregion

        #region Helpers (RB/응답 판별)

        // 수신시 RB(스캐너)인지 판별
        private static bool IsScannerAuto(JbFrame f)
            => f.pMain == 'R' && f.pSub == 'B';

        // 프로젝트 규약상 “ME”가 명령 응답이면 이렇게 한정
        private static bool IsResponseForTimeout(JbFrame f)
            => (f.pMain == 'M' || f.pMain == 'm') && (f.pSub == 'E' || f.pSub == 'e');

        #endregion

        #region 정리 (Dispose)

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }

            // 이벤트 해제
            if (_toHandler != null) _timeout.RequestTimedOut -= _toHandler;
            if (_rxHandler != null) _transport.FrameReceived -= _rxHandler;
            if (_errHandler != null) _transport.OnError -= _errHandler;

            try { _sendQueue.Writer.TryComplete(); } catch { }
            try { _sendLoop?.Wait(200); } catch { }

            _started = false;
            _cts.Dispose();
            // TimeoutManager는 싱글톤 → Dispose하지 않음
        }

        #endregion
    }
}
