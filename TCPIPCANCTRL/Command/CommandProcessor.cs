using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using TcpIpCanCtrl.Util;

namespace TcpIpCanCtrl.Command
{
    internal static class CommandProcessor
    {

        /// <summary>
        /// 명령 시퀀스, 페이로드, 인코딩 정보를 바탕으로 바이트 배열을 조립합니다.
        /// 이 메서드는 MemoryPool을 사용하여 메모리 재사용을 최적화합니다.
        /// </summary>
        /// <param name="_seq">4자리 시퀀스 번호</param>
        /// <param name="_payload">페이로드 문자열</param>
        /// <param name="_encoding">사용할 인코딩 타입</param>
        /// <returns>빌려온 메모리 영역을 관리하는 IMemoryOwner</returns>
        internal static IMemoryOwner<byte> Build(int _seq, string _payload, Encoding _encoding)
        {
            //var payloadLen = _payload.Length;
            var payloadLen = _encoding.GetByteCount(_payload);
            var totalLength = Constants.FIXED_HEADER_LENGTH + payloadLen;

            // using 문을 사용하여 MemoryOwner의 Dispose()가 보장되도록 합니다.
            // 하지만 이 메서드의 반환 타입이 IMemoryOwner이므로, 호출하는 쪽에서 using을 사용해야 합니다.
            // 여기서는 빌린 메모리를 최종 반환하기 때문에 using을 직접 사용할 수 없습니다.
            // CommandHandler.cs에서 builtCommand를 using으로 감싸는 것이 올바른 패턴입니다.
            IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(totalLength);
            if (!MemoryMarshal.TryGetArray(owner.Memory, out ArraySegment<byte> segment))
            {
                owner.Dispose();
                throw new InvalidOperationException("Failed to get underlying array from MemoryPool.");
            }

            byte[] bytes = segment.Array;
            int offset = segment.Offset; // 배열의 시작 오프셋을 사용합니다.

            try
            {
                // seq (D4)
                offset += _encoding.GetBytes(_seq.ToString("D4"), 0, Constants.SEQLEN, bytes, offset);

                // payload.Length (D3)
                offset += _encoding.GetBytes(payloadLen.ToString("D3"), 0, Constants.PAYLOADLEN, bytes, offset);

                // payload내용 (addr + command + data)
                offset += _encoding.GetBytes(_payload, 0, _payload.Length, bytes, offset);
            }
            catch (EncoderFallbackException ex)
            {
                owner.Dispose();
                throw new InvalidOperationException($"명령에 사용할 수 없는 문자 포함", ex);
            }
            catch (Exception)
            {
                owner.Dispose();
                throw;
            }

            return new MemoryOwner(owner, totalLength);
        }
    }
}
