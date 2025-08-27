using System;
using System.Buffers;
using System.Text;
using TcpIpCanCtrl.Model;
using TcpIpCanCtrl.Util;

namespace TcpIpCanCtrl.Parser
{

    /// <summary>
    /// 헤더(ADDR4+CMD2)를 기준으로:
    /// - 고정 20B(ME/MA/…): HEADER(6) + PAYLOAD(14)
    /// - 가변 RB: HEADER(6) + [다음 헤더 시작 전] (없으면 개행 전)
    ///    → 개행을 만나면 CRLF(2) 또는 단일 개행(1) + 그 뒤 연속 공백/탭/개행 전체 스킵
    /// 여러 프레임 연속 버퍼에서 한 프레임씩 정확히 소비.
    /// </summary>
    internal sealed class JbFrameParser
    {
        private const int HEADER_LEN = 6;   // 4(ADDR) + 2(CMD)
        private const int PAYLOAD_LEN = 14;  // 고정 프레임 payload
        internal const int FRAME_LEN = HEADER_LEN + PAYLOAD_LEN;

        private static bool IsDigit(byte b) { return b >= (byte)'0' && b <= (byte)'9'; }
        private static byte ToUpperAscii(byte b) { return (b >= (byte)'a' && b <= (byte)'z') ? (byte)(b - 32) : b; }
        private static bool IsUpper(byte b) { return b >= (byte)'A' && b <= (byte)'Z'; }
        private static bool LooksLikeHeader(ReadOnlySpan<byte> span)
        {
            if (span.Length < HEADER_LEN) return false;
            // ADDR 4자리 숫자
            if (!IsDigit(span[0]) || !IsDigit(span[1]) || !IsDigit(span[2]) || !IsDigit(span[3])) return false;
            // CMD 2자리 대문자
            var m = ToUpperAscii(span[4]);
            var s = ToUpperAscii(span[5]);
            return IsUpper(m) && IsUpper(s);
        }
        private static bool TryGetByteAt(ReadOnlySequence<byte> seq, long offset, out byte b)
        {
            if ((ulong)seq.Length <= (ulong)offset) { b = 0; return false; }
            var pos = seq.GetPosition(offset);
            var one = seq.Slice(pos, 1);
            Span<byte> tmp = stackalloc byte[1];
            one.CopyTo(tmp);
            b = tmp[0];
            return true;
        }
        /// <summary>헤더 정렬이 안 되었을 때 다음 정상 헤더 시작으로 리싱크(일부 소비 시 true).</summary>
        private static bool ResyncToNextHeaderLinear(ref ReadOnlySequence<byte> buffer)
        {
            Span<byte> win = stackalloc byte[HEADER_LEN];
            int winCount = 0;
            long globalIndex = 0;

            var seq = buffer;
            var pos = seq.Start;
            ReadOnlyMemory<byte> mem;
            while (seq.TryGet(ref pos, out mem, true))
            {
                var span = mem.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    byte b = span[i];

                    if (winCount < HEADER_LEN)
                    {
                        win[winCount++] = b;
                        if (winCount == HEADER_LEN && LooksLikeHeader(win))
                        {
                            long startOff = globalIndex - (HEADER_LEN - 1);
                            buffer = buffer.Slice(buffer.GetPosition(startOff));
                            return true;
                        }
                    }
                    else
                    {
                        // 6바이트 롤링 윈도우
                        win[0] = win[1]; win[1] = win[2]; win[2] = win[3];
                        win[3] = win[4]; win[4] = win[5]; win[5] = b;

                        if (LooksLikeHeader(win))
                        {
                            long startOff = globalIndex - (HEADER_LEN - 1);
                            buffer = buffer.Slice(buffer.GetPosition(startOff));
                            return true;
                        }
                    }

                    globalIndex++;
                }
            }
            return false;
        }
        /// <summary>afterHeader에서 다음 헤더 시작 오프셋을 찾기(없으면 -1).</summary>
        private static long FindNextHeaderOffset(ReadOnlySequence<byte> afterHeader)
        {
            Span<byte> win = stackalloc byte[HEADER_LEN];
            int winCount = 0;
            long idx = 0;

            var seq = afterHeader;
            var pos = seq.Start;
            ReadOnlyMemory<byte> mem;
            while (seq.TryGet(ref pos, out mem, true))
            {
                var span = mem.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    byte b = span[i];

                    if (winCount < HEADER_LEN)
                    {
                        win[winCount++] = b;
                        if (winCount == HEADER_LEN && LooksLikeHeader(win))
                            return idx - (HEADER_LEN - 1);
                    }
                    else
                    {
                        win[0] = win[1]; win[1] = win[2]; win[2] = win[3];
                        win[3] = win[4]; win[4] = win[5]; win[5] = b;

                        if (LooksLikeHeader(win))
                            return idx - (HEADER_LEN - 1);
                    }
                    idx++;
                }
            }
            return -1;
        }
        /// <summary>
        /// afterHeader에서 첫 개행의 오프셋과 폭(1 or 2; CRLF)을 계산.
        /// 개행이 없으면 -1, 0 반환.
        /// </summary>
        private static long FindFirstNewlineOffset(ReadOnlySequence<byte> seq, out int sepWidth)
        {
            sepWidth = 0;
            long offset = 0;

            var s = seq;
            var pos = s.Start;
            ReadOnlyMemory<byte> mem;
            while (s.TryGet(ref pos, out mem, true))
            {
                var span = mem.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    byte b = span[i];
                    if (b == (byte)'\r' || b == (byte)'\n')
                    {
                        // CRLF 처리
                        if (b == (byte)'\r')
                        {
                            byte next;
                            if (TryGetByteAt(seq, offset + i + 1, out next) && next == (byte)'\n')
                                sepWidth = 2;
                            else
                                sepWidth = 1;
                        }
                        else
                        {
                            sepWidth = 1;
                        }
                        return offset + i;
                    }
                }
                offset += span.Length;
            }
            return -1;
        }
        /// <summary>시작부터 스킵 가능한 공백류(스페이스/탭/CR/LF)를 모두 센다.</summary>
        private static int CountLeadingSkippables(ReadOnlySequence<byte> seq)
        {
            int count = 0;
            var s = seq;
            var pos = s.Start;
            ReadOnlyMemory<byte> mem;
            while (s.TryGet(ref pos, out mem, true))
            {
                var span = mem.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    byte b = span[i];
                    if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n')
                        count++;
                    else
                        return count;
                }
            }
            return count;
        }
        /// <summary>
        /// 메인 파서: 성공 시 frame 채우고 buffer를 딱 그 프레임 뒤로 이동.
        /// 실패 시: 소비가 없거나(더 필요) 또는 리싱크로 일부 소비 후 false 반환.
        /// </summary>
        internal bool TryParse(int jbIndex, ref ReadOnlySequence<byte> buffer, out JbFrame frame)
        {
#if DEBUG
            Console.WriteLine($"파싱 예정 기다려주세요. {jbIndex}번 JB 에서 수신됨 {Encodings.Ascii.GetString(buffer.ToArray())}/{Encodings.Ascii.GetString(buffer.ToArray()).Length} ");
#endif
            frame = default(JbFrame);

            // 최소 헤더 검사
            if (buffer.Length < HEADER_LEN) return false;

            // 현재가 헤더인지 확인(아니면 리싱크)
            Span<byte> head6 = stackalloc byte[HEADER_LEN];
            buffer.Slice(0, HEADER_LEN).CopyTo(head6);

            if (!LooksLikeHeader(head6))
            {
                if (ResyncToNextHeaderLinear(ref buffer))
                    return false; // 일부 소비됨 → 상위 루프에서 재시도
                return false;     // 소비 없음 → 더 필요
            }

            // 헤더 파싱
            string addr = Encodings.Ascii.GetString(head6.Slice(0, 4).ToArray());
            byte bMain = ToUpperAscii(head6[4]);
            byte bSub = ToUpperAscii(head6[5]);
            char main = (char)bMain;
            char sub = (char)bSub;

            // ── RB: 다음 헤더가 보이면 그 전까지(최우선), 없으면 개행(+공백류)까지 ──
            if (main == 'R' && sub == 'B')
            {
                var afterHeader = buffer.Slice(HEADER_LEN);

                // 1) 다음 헤더가 보이면 거기까지
                long offHeader = FindNextHeaderOffset(afterHeader);
                if (offHeader >= 0)
                {
                    var payloadSeq = afterHeader.Slice(0, offHeader);
                    string value = Encodings.Ascii.GetString(payloadSeq.ToArray()).TrimEnd('\r', '\n', Constants.PAD_CHAR_FOR_VALUE);
                    frame = new JbFrame(jbIndex, addr, main, sub, value);
                    buffer = buffer.Slice(HEADER_LEN + offHeader); // 헤더에 정렬
                    return true;
                }

                // 2) 개행을 경계로 자르고, CRLF(2) or 단일(1) + 그 뒤 공백/탭/추가 개행 전부 스킵
                int sepWidth;
                long offNewline = FindFirstNewlineOffset(afterHeader, out sepWidth);
                if (offNewline >= 0)
                {
                    var payloadSeq = afterHeader.Slice(0, offNewline);
                    string value = Encodings.Ascii.GetString(payloadSeq.ToArray()).TrimEnd('\r', '\n', Constants.PAD_CHAR_FOR_VALUE);

                    // 개행 폭 + 공백류 스킵
                    var rest = afterHeader.Slice(offNewline + sepWidth);
                    int skippables = CountLeadingSkippables(rest);

                    frame = new JbFrame(jbIndex, addr, main, sub, value);
                    buffer = buffer.Slice(HEADER_LEN + offNewline + sepWidth + skippables);
                    return true;
                }

                // 경계 없음 → 더 필요
                return false;
            }

            // ── 고정 20B (ME/MA/…) ──
            if (buffer.Length < FRAME_LEN) return false;

            Span<byte> payload = stackalloc byte[PAYLOAD_LEN];
            buffer.Slice(HEADER_LEN, PAYLOAD_LEN).CopyTo(payload);

            string data = Encodings.Ascii.GetString(payload.ToArray()).TrimEnd('\r', '\n', Constants.PAD_CHAR_FOR_VALUE);

            frame = new JbFrame(jbIndex, addr, main, sub, data);
            buffer = buffer.Slice(FRAME_LEN);
            return true;
        }
    }
}
