using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TcpIpCanCtrl.Event;
using TcpIpCanCtrl.Interface;
using TcpIpCanCtrl.Model;
using TcpIpCanCtrl.Transport;
using TcpIpCanCtrl.Util;

namespace TcpIpCanCtrl.Service
{
    internal class CommunicationOrchestrator : IDisposable, IFrameSource<JbFrame>
    {
        private readonly struct SendItem
        {
            internal readonly IMemoryOwner<byte> CommandBytes;
            internal readonly TaskCompletionSource<bool> Tcs;

            internal SendItem(IMemoryOwner<byte> commandBytes, TaskCompletionSource<bool> tcs)
            {
                CommandBytes = commandBytes;
                Tcs = tcs;
            }
        }

        private readonly PipelineTransport _transport;
        private readonly Channel<SendItem> _sendQueue =
            Channel.CreateUnbounded<SendItem>(new UnboundedChannelOptions { SingleReader = true });
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _sendLoop;
        private bool _started;


        // → 2단계: Orchestrator 레벨에서 재발행할 이벤트
        public event EventHandler<FrameReceivedEventArgs<JbFrame>> FrameReceived;
        public event EventHandler<OnErrorEventArgs> OnError;

        internal CommunicationOrchestrator(PipelineTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));

            // → transport(FrameReceived1) 구독    → FrameReceived2로 재발행
            _transport.FrameReceived += (sender, args) => FrameReceived?.Invoke(this, args);
            //_transport.OnError += (sender, args) => OnError?.Invoke(this, args);
            _sendLoop = Task.Run(SendLoopAsync);
        }

        internal void Start()
        {
            if (_started) throw new InvalidOperationException("Already started");
            _started = true;
            _ = _transport.StartReceiveLoopAsync(_cts.Token);
        }


        #region 송신 API

        /// <summary>
        /// Fire-and-forget 방식으로 명령을 큐에 등록합니다.
        /// (실제 전송 완료를 기다리지 않습니다.)
        /// </summary>
        internal void EnqueueCommand(IMemoryOwner<byte> commandBytes)
              => _ = EnqueueCommandAsync(commandBytes, CancellationToken.None);

        /// <summary>
        /// 완성된 바이트 데이터를 큐에 등록합니다.
        /// </summary>
        private Task EnqueueCommandAsync(IMemoryOwner<byte> commandBytes, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // 큐에 명령을 추가
            if (!_sendQueue.Writer.TryWrite(new SendItem(commandBytes, tcs)))
            {
                throw new InvalidOperationException("Send queue closed");
            }

            // CancellationToken이 취소되면 Task도 취소 상태로 만듭니다.
            ct.Register(() => tcs.TrySetCanceled());

            return tcs.Task;
        }


        #endregion


        #region 루프
        
        private async Task SendLoopAsync()
        {
            var reader = _sendQueue.Reader;
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                while (reader.TryRead(out var item))
                {
                    try
                    {
                        // 전송 직전에 Build를 호출하여 IMemoryOwner를 얻음
                        using (item.CommandBytes)
                        {
                            //Console.WriteLine($"[COMM ▶] TX 시작 {Encodings.Ascii.GetString(payloadOwner.Memory.ToArray())}");
                            Console.WriteLine($"[COMM ▶] TX 시작 {Encodings.Ksc5601.GetString(item.CommandBytes.Memory.ToArray())}");
                            await _transport.SendAsync(item.CommandBytes.Memory, _cts.Token);
                            Console.WriteLine($"[COMM ▶] TX 완료");
                            item.Tcs.TrySetResult(true);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Task를 Canceled 상태로 설정
                        item.Tcs.TrySetCanceled();
                    }
                    catch (Exception ex)
                    {
                        // Task를 예외 상태로 설정
                        item.Tcs.TrySetException(ex);
                        OnError?.Invoke(this, new OnErrorEventArgs(ex)); // 에러 처리
                    }

                    // 메시지 전송 후 80ms 대기하여 메시지가 묶이는 것과 빠른 송신을 방지
                    await Task.Delay(80, _cts.Token);
                }
            }
        }

        #endregion


        #region 정리 (Dispose)

        private void Stop()
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                _sendQueue.Writer.Complete();
            }
        }

        public void Dispose()
        {
            Stop();
            try { _sendLoop.Wait(); } catch { }
            _cts.Dispose();
        }

        #endregion
    }
}
