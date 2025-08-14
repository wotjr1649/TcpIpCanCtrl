using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TcpIpCanCtrl.Event;
using TcpIpCanCtrl.Interface;
using TcpIpCanCtrl.Model;
using TcpIpCanCtrl.Parser;
using TcpIpCanCtrl.Util;

namespace TcpIpCanCtrl.Transport
{
    internal class PipelineTransport : IDisposable, IFrameSource<JbFrame>
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _networkStream;
        private readonly Pipe _pipe;
        private readonly JbFrameParser _parser;
        private bool _isReading;
        private bool _disposed;

        internal bool IsConnected => _tcpClient?.Connected ?? false;

        // → 1단계: 파이프라인에서 파싱된 JbFrame을 처음으로 발행
        public event EventHandler<FrameReceivedEventArgs<JbFrame>> FrameReceived;

        private PipelineTransport(TcpClient client)
        {
            _tcpClient = client ?? throw new ArgumentNullException(nameof(client));
            _networkStream = client.GetStream();
            _parser = new JbFrameParser();
            _pipe = new Pipe();

            Console.WriteLine($"[DEBUG] 주입된 Parser = {_parser.GetType().FullName}");

        }

        /// <summary>
        /// 지정된 호스트/포트에 연결하고, 새 PipelineTransport 인스턴스를 반환합니다.
        /// </summary>
        internal static async Task<PipelineTransport> CreateAsync(
            string host,
            int port,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("host는 비어 있을 수 없습니다.", nameof(host));

            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(host, port).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[{host}:{port}] 연결에 실패했습니다.", ex);
            }

            return new PipelineTransport(client);
        }

        /// <summary>
        /// 페이로드를 네트워크로 비동기 송신합니다.
        /// </summary>
        internal async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken token)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PipelineTransport));
            if (payload.IsEmpty) throw new ArgumentException("payload가 비어 있습니다.", nameof(payload));

            await _networkStream.WriteAsync(payload.ToArray(), 0, payload.Length, token);
            await _networkStream.FlushAsync(token);
        }

        /// <summary>
        /// 수신 루프를 시작합니다. 취소될 때까지 데이터를 파싱해 이벤트로 전달합니다.
        /// </summary>
        internal async Task StartReceiveLoopAsync(CancellationToken token) // 2 초 동안 응답 없을시  ME2000 처리 기능 추가
        {
            if (_isReading) throw new InvalidOperationException("이미 수신 루프가 실행 중입니다.");
            _isReading = true;

            Console.WriteLine("[PT] ▷ 수신 루프 시작");

            // 1) 네트워크 스트림 → PipeWriter 복사
            var writer = _pipe.Writer;
            var copyTask = _networkStream.CopyToAsync(writer.AsStream(), 8192, token);


            var reader = _pipe.Reader;
            while (!token.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(token);


                var buffer = result.Buffer;
                var debugBuf = buffer;  // 복사본 (ReadOnlySequence는 값 타입)
                if (buffer.Length > 0)
                    Console.WriteLine($"[PT]   ▽ 버퍼 {buffer.Length} bytes");



                SequencePosition consumed = buffer.Start;
                Console.WriteLine($"[PT]   ▽ 버퍼 {Encodings.Ksc5601.GetString(buffer.ToArray())} bytes");
                // 버퍼에서 프레임을 파싱할 때마다
                while (_parser.TryParse(ref buffer, out var frame))
                {
                    Console.WriteLine($"[PT]   ✔ 프레임 파싱 성공 MAIN={frame.pMain} SUB={frame.pSub} DataLen={frame.pData.Length}");
                    // → FrameReceived 이벤트 발행
                    FrameReceived?.Invoke(this, new FrameReceivedEventArgs<JbFrame>(frame));
                    consumed = buffer.Start;

                }
                reader.AdvanceTo(consumed, buffer.End);

                if (result.IsCompleted) break;
            }

            await reader.CompleteAsync();
            await writer.CompleteAsync();
            await copyTask;
            Console.WriteLine("[PT] ◁ 수신 루프 종료");

        }


        public void Dispose()
        {
            if (_disposed) return;
            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
            try { _networkStream?.Dispose(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _disposed = true;
        }
    }
}
