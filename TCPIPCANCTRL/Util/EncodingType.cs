using System;
using System.Collections.Generic;
using System.Text;

namespace TcpIpCanCtrl.Util
{
    public static class Encodings
    {
        // KSC (CP949) 인코딩 객체. 예외 폴백 사용.
        // Encoding.GetEncoding(949)는 기본적으로 대체 폴백을 사용하지만, 명시적으로 지정하여 예상치 못한 문제를 방지합니다.
        public static readonly Encoding Ksc949 = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        // ASCII 인코딩 객체 (Framework가 제공하는 캐시된 인스턴스 사용)
        public static readonly Encoding Ascii = Encoding.ASCII;

        // EUC-KR (ksc_5601)이 필요한 경우 추가
        public static readonly Encoding Ksc5601 = Encoding.GetEncoding("ksc_5601", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
