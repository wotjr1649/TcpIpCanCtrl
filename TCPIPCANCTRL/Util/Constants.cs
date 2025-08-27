using System;
using System.Collections.Generic;
using System.Text;

namespace TcpIpCanCtrl.Util
{
    internal enum Encoding_Type
    {
        ASCII,
        KSC949,
        kSC5601
    }

    internal static class Constants
    {
        internal const byte SEQLEN = 4;
        internal const byte PAYLOADLEN = 3;
        // 헤더의 고정 길이 부분 (SEQ + PAYLOADLEN)
        internal const byte FIXED_HEADER_LENGTH = 7;
        // 스페이스 문자       
        internal const char PAD_CHAR_FOR_VALUE = (char)0x20; 
    }
}
