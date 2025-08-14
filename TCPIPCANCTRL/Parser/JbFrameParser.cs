using System;
using System.Buffers;
using System.Text;
using TcpIpCanCtrl.Model;
using TcpIpCanCtrl.Util;

namespace TcpIpCanCtrl.Parser
{
    internal sealed class JbFrameParser
    {

        /// <summary>
        /// ReadOnlySequence에서 JbFrame을 파싱합니다.
        /// </summary>
        /// <param name="buf">파싱할 바이트 시퀀스</param>
        /// <param name="frame">파싱된 JbFrame (성공 시)</param>
        /// <returns>파싱 성공 여부</returns>
        internal bool TryParse(ref ReadOnlySequence<byte> buf, out JbFrame frame)
        {
            frame = default;

            // 1. 버퍼가 고정된 주소값 + 커맨드 길이보다 짧으면 즉시 종료
            if (buf.Length < Constants.FIXED_FRAME_LENGTH) return false;

            // 2. 메모리 할당을 최소화하기 위해 스택에 메모리 할당
            Span<byte> headerBytes = stackalloc byte[Constants.FIXED_FRAME_LENGTH];
            buf.Slice(0, Constants.FIXED_FRAME_LENGTH).CopyTo(headerBytes);

            // 3. 헤더 파싱
            string addr = headerBytes.Slice(0, 4).ToString();
            char mainCommand = (char)headerBytes[4];
            char subCommand = (char)headerBytes[5];

            // 4. 페이로드(데이터) 파싱
            ReadOnlySequence<byte> dataSequence = buf.Slice(Constants.FIXED_FRAME_LENGTH);
            string value = string.Empty;

            if (dataSequence.Length > 0)
                value = (dataSequence.IsSingleSegment 
                    ? dataSequence.First.Span.ToString() 
                    : Encodings.Ascii.GetString(dataSequence.ToArray()))
                    .TrimEnd(Constants.PAD_CHAR_FOR_VALUE);

            // 5. JbFrame 객체 생성
            frame = new JbFrame(addr, mainCommand, subCommand, value);

            // 6.  버퍼 소모 (Consume)
            buf = buf.Slice(buf.Length);

            return true;
        }


    }
}
