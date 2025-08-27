using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TcpIpCanCtrl.Event;
using TcpIpCanCtrl.Interface;
using TcpIpCanCtrl.Model;
using TcpIpCanCtrl.Parser;
using TcpIpCanCtrl.Util;

namespace TcpIpCanCtrl.Transport
{
    /// <summary>
    /// TcpClient + System.IO.Pipelines 기반 전송/수신.
    /// </summary>
    internal sealed class PipelineTransport : IDisposable, IFrameSource<JbFrame>
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly Pipe _pipe = new Pipe();
        private readonly JbFrameParser _parser = new JbFrameParser();

        private volatile bool _disposed;
        private volatile bool _reading;

        // Complete 중복 보호용 플래그
        private int _readerCompleted, _writerCompleted;

        // 1회 Read에서 너무 많은 프레임을 뽑는 폭주 방지
        private const int MAX_FRAMES_PER_READ = 1024;
        // FillPipe용 최소 읽기 버퍼(바이트) - 백프레셔/성능 균형
        private const int MIN_READ = MAX_FRAMES_PER_READ * 8; 

        public bool IsConnected => _client?.Connected == true;

        // → 1단계: 파이프라인에서 파싱된 JbFrame을 처음으로 발행
        public event EventHandler<FrameReceivedEventArgs<JbFrame>> FrameReceived;
        public event EventHandler<OnErrorEventArgs> OnError;

        private PipelineTransport(TcpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _client.NoDelay = true; // Nagle 알고리즘 비활성화 (지연 최소화)
            _stream = client.GetStream();
        }

        /// <summary>호스트/포트 연결 후 인스턴스 생성</summary>
        internal static async Task<PipelineTransport> CreateAsync(string host,int port,CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("host");
            if (port <= 0) throw new ArgumentOutOfRangeException(nameof(port));

            var client = new TcpClient();
            try
            {
                // 취소 반영: 토큰 취소 시 소켓 dispose로 ConnectAsync 탈출
                using (ct.Register(() => { try { client.Dispose(); } catch { } }))
                    await client.ConnectAsync(host, port).ConfigureAwait(false);
                return new PipelineTransport(client);
            }
            catch (Exception ex)
            {
                try { client.Dispose(); } catch { }
                throw new IOException($"Connect {host}:{port} 실패", ex);
            }
        }

        /// <summary>페이로드 송신</summary>
        internal async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PipelineTransport));
            if (payload.IsEmpty) throw new ArgumentException("empty payload");

#if NETSTANDARD2_0
            // 배열 기반이면 추가 복사 없이 전송
            if (MemoryMarshal.TryGetArray(payload, out var seg) && seg.Array != null)
                await _stream.WriteAsync(seg.Array, seg.Offset, seg.Count, ct).ConfigureAwait(false);
            else
            {
                // 배열 기반이 아니면 풀에서 빌려 복사 후 전송
                var rented = ArrayPool<byte>.Shared.Rent(payload.Length);
                try
                {
                    payload.Span.CopyTo(rented.AsSpan());
                    await _stream.WriteAsync(rented, 0, payload.Length, ct).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
#else
            await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
#endif
            // NetworkStream Flush는 일반적으로 무의미(소켓). 필요 시만 활성화
            //await _stream.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <summary>수신 루프 시작(취소 시까지)</summary>
        internal async Task StartReceiveLoopAsync(int jbIndex, CancellationToken ct)
        {
            if (_reading) throw new InvalidOperationException("already reading");
            _reading = true;

#if DEBUG
            Console.WriteLine("[PT] ▷ 수신 루프 시작");
#endif
            // 1) 네트워크 스트림 → PipeWriter 복사
            var writer = _pipe.Writer;
            var reader = _pipe.Reader;
            var fillTask = FillPipeAsync(writer, ct);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var result = await reader.ReadAsync(ct).ConfigureAwait(false);
                    var buffer = result.Buffer; // 값 타입 복사

                    SequencePosition consumed = buffer.Start;
                    SequencePosition examined = buffer.End;

                    int frames = 0;
                    try
                    {
                        while (frames < MAX_FRAMES_PER_READ)
                        {
                            var work = buffer;
                            var beforeLen = work.Length;

                            JbFrame frame;
                            if (!_parser.TryParse(jbIndex, ref work, out frame))
                            {
                                // 일부 소비(리싱크 등) 되었는가?
                                if (work.Length < buffer.Length)
                                {
                                    buffer = work;
                                    consumed = buffer.Start;
                                    examined = consumed;
                                    continue; // 이어서 파싱
                                }

                                // 소비 없음 → 더 필요
                                examined = buffer.End;
                                break;
                            }

                            // 파싱 성공 → 소비/검사 갱신
                            buffer = work;
                            consumed = buffer.Start;
                            examined = consumed;
                            frames++;

                            // 프레임당 1회만 이벤트 발행
                            try
                            {
                                var h = FrameReceived;
                                if (h != null) h(this, new FrameReceivedEventArgs<JbFrame>(frame));
                            }
                            catch (Exception ex)
                            {
                                var eh = OnError;
                                if (eh != null) eh(this, new OnErrorEventArgs(ex));
                            }
                        }

                        // 과도 추출 방지(남은 데이터가 있음을 시그널)
                        if (frames >= MAX_FRAMES_PER_READ)
                            examined = buffer.Start;
                    }
                    finally
                    {
                        reader.AdvanceTo(consumed, examined);
                    }
                    if (result.IsCompleted) break;

                }
            }
            catch (IOException ex) { OnError?.Invoke(this, new OnErrorEventArgs(ex)); }
            catch (SocketException ex) { OnError?.Invoke(this, new OnErrorEventArgs(ex)); }
            catch (OperationCanceledException) { /* 정상 종료 */ }
            catch (Exception ex) { OnError?.Invoke(this, new OnErrorEventArgs(ex)); }
            finally
            {
                // Complete 중복 보호: Interlocked로 가드
                await SafeCompleteReaderAsync().ConfigureAwait(false);
                await SafeCompleteWriterAsync().ConfigureAwait(false);

                try { await fillTask.ConfigureAwait(false); }
                catch { /* 종료 중 예외 무시 */ }
            }
#if DEBUG
            Console.WriteLine("[PT] ◁ 수신 루프 종료");
#endif
        }

        /// <summary>NetworkStream → PipeWriter로 채우는 루프(백프레셔/예외 제어 용이).</summary>
        private async Task FillPipeAsync(PipeWriter writer, CancellationToken ct)
        {
            byte[] buffer = null;
            try
            {
                buffer = ArrayPool<byte>.Shared.Rent(MIN_READ);

                while (!ct.IsCancellationRequested)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    if (bytesRead == 0) break; // 소켓 종료

                    // 파이프에 쓸 메모리를 정확한 크기로 확보
                    var memory = writer.GetMemory(bytesRead);
                    // System.Memory: 배열 → 파이프 메모리(Span) 복사
                    buffer.AsSpan(0, bytesRead).CopyTo(memory.Span);

                    writer.Advance(bytesRead);

                    var flush = await writer.FlushAsync(ct).ConfigureAwait(false);
                    if (flush.IsCompleted) break;
                }
            }
            catch (IOException ex) { OnError?.Invoke(this, new OnErrorEventArgs(ex)); }
            catch (SocketException ex) { OnError?.Invoke(this, new OnErrorEventArgs(ex)); }
            catch (OperationCanceledException) { }
            finally
            {
                if (buffer != null) ArrayPool<byte>.Shared.Return(buffer);
                await SafeCompleteWriterAsync().ConfigureAwait(false);
            }
        }

        // Complete 중복 보호 헬퍼들
        private Task SafeCompleteReaderAsync()
            => Interlocked.Exchange(ref _readerCompleted, 1) == 0 ? _pipe.Reader.CompleteAsync().AsTask() : Task.CompletedTask;

        private Task SafeCompleteWriterAsync()
            => Interlocked.Exchange(ref _writerCompleted, 1) == 0 ? _pipe.Writer.CompleteAsync().AsTask() : Task.CompletedTask;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _stream?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }

            try { _pipe.Reader.Complete(); } catch { }
            try { _pipe.Writer.Complete(); } catch { }
        }
    }
}
