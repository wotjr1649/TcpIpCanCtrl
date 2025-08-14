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
        // 프레임의 고정 길이 부분 (CAN PORT + ID + MAIN + SUB)
        internal const byte FIXED_FRAME_LENGTH = 6;

        internal const string BCRI_OFF = "BC";
        internal const string BCRI_ON = "BO";

        internal const string DISP_SND_1 = "LF1";
        internal const string DISP_SND_2 = "LF2";
        internal const string DISP_SND_3 = "LF3";
        internal const string DISP_SND_4 = "LF4";

        internal const string LCD_AC = "AC111111";
        internal const string LCD_AL = "AL";

        internal const string UNIT_SET = "000MS";
        internal const string UNIT_ONE_OFF = "LD      ";

        internal const string PORT_ALL_ON = "000MA      ";
        internal const string PORT_ALL_OFF = "000MZ      ";        

        internal const char PAD_CHAR_FOR_VALUE = (char)0x20; // 스페이스 문자       
    }
}
