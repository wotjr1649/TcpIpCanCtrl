using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using TcpIpCanCtrl.Interface;
using TcpIpCanCtrl.Util;

namespace TcpIpCanCtrl.Controller
{
    internal sealed class PortAllOnCommand : ICommand
    {
        private readonly int _canId;
        private const string CMD_PREFIX = "000MA";

        /// <param name="canId">can 포트 값</param>
        internal PortAllOnCommand(int canId) => _canId = canId;

        public void Validate()
        {
            if (_canId < 1 || _canId > 3)
                throw new ArgumentException($"CanID는 1~3이어야 합니다.  (현재: {_canId})", nameof(_canId));
        }

        public string GetPayload()
        {
            var sb = new StringBuilder();
            sb.Append(_canId.ToString()).Append(CMD_PREFIX);
            return sb.ToString();
        }

        public Encoding_Type GetEncodingType() => Encoding_Type.ASCII;
    }
    internal sealed class PortAllOffCommand : ICommand
    {
        private readonly int _canId;
        private const string CMD_PREFIX = "000MZ";

        /// <param name="canId">can 포트 값</param>
        internal PortAllOffCommand(int canId) => _canId = canId;

        public void Validate()
        {
            if (_canId < 1 || _canId > 3)
                throw new ArgumentException($"CanID는 1~3이어야 합니다.  (현재: {_canId})", nameof(_canId));
        }

        public string GetPayload()
        {
            var sb = new StringBuilder();
            sb.Append(_canId.ToString()).Append(CMD_PREFIX);
            return sb.ToString();
        }

        public Encoding_Type GetEncodingType() => Encoding_Type.ASCII;
    }

    internal sealed class UnitOneOnCommand : ICommand
    {
        private readonly string _payload;

        /// <param name="payload">address + 커맨드 + 명령값</param>
        internal UnitOneOnCommand(string payload) => _payload = payload;

        public void Validate()
        {
            if (string.IsNullOrEmpty(_payload) || _payload.Length < 4)
                throw new ArgumentException("payload 길이가 4보다 작거나 공백이면 안됩니다.", nameof(_payload));
        }

        public string GetPayload() => _payload;

        public Encoding_Type GetEncodingType() => Encoding_Type.ASCII;
    }
    internal sealed class UnitOneOffCommand : ICommand
    {
        private readonly string _address;
        private const string CMD_PREFIX = "LD";

        /// <param name="address">표시기 주소값</param>
        internal UnitOneOffCommand(string address) => _address = address;

        public void Validate()
        {
            if (string.IsNullOrEmpty(_address) || _address.Length != 4)
                throw new ArgumentException("주소에는 값이 비어있거나 공백이 포함될 수 없습니다.", nameof(_address));
        }

        public string GetPayload()
        {
            var sb = new StringBuilder();
            sb.Append(_address).Append(CMD_PREFIX);
            return sb.ToString();
        }

        public Encoding_Type GetEncodingType() => Encoding_Type.ASCII;
    }
    internal sealed class UnitSetCommand : ICommand
    {
        private readonly string _address;
        private const string CMD_PREFIX = "000MS";

        /// <param name="address">표시기에 주소값</param>
        internal UnitSetCommand(string address) => _address = address;

        public void Validate()
        {
            if (string.IsNullOrEmpty(_address) || _address.Length != 5)
                throw new ArgumentException("주소에는 값이 비어있거나 공백이 포함될 수 없고 JB박스 인덱스를 포함한 5자 이어야 합니다.", nameof(_address));
        }

        public string GetPayload()
        {
            var sb = new StringBuilder();
            sb.Append(_address[1]).Append(CMD_PREFIX).Append(_address);
            return sb.ToString();
        }

        public Encoding_Type GetEncodingType() => Encoding_Type.ASCII;
    }

    internal sealed class BcriOnCommand : ICommand
    {
        private readonly string _address;
        private const string CMD_PREFIX = "BO";

        /// <param name="address">바코드 주소값</param>
        internal BcriOnCommand(string address) => _address = address;

        public void Validate()
        {
            if (string.IsNullOrEmpty(_address) || _address.Length < 4)
                throw new ArgumentException("주소에는 값이 비어있거나 공백이 포함될 수 없습니다.", nameof(_address));
        }

        public string GetPayload()
        {
            var sb = new StringBuilder();
            sb.Append(_address).Append(CMD_PREFIX);
            return sb.ToString();
        }

        public Encoding_Type GetEncodingType() => Encoding_Type.ASCII;
    }
    internal sealed class BcriOffCommand : ICommand
    {
        private readonly string _address;
        private const string CMD_PREFIX = "BO";

        /// <param name="address">바코드 주소값</param>
        internal BcriOffCommand(string address) => _address = address;

        public void Validate()
        {
            if (string.IsNullOrEmpty(_address) || _address.Length < 4)
                throw new ArgumentException("주소에는 값이 비어있거나 공백이 포함될 수 없습니다.", nameof(_address));
        }

        public string GetPayload()
        {
            var sb = new StringBuilder();
            sb.Append(_address).Append(CMD_PREFIX);
            return sb.ToString();
        }

        public Encoding_Type GetEncodingType() => Encoding_Type.ASCII;
    }

    internal sealed class Disp5SndCommand : ICommand
    {
        private readonly string _value;
        private const string CMD_PREFIX = "LF";

        /// <param name="value">5행 표시기에 보낼 명령어</param>
        internal Disp5SndCommand(string value) => _value = value;

        public void Validate()
        {
            if (string.IsNullOrEmpty(_value))
                throw new ArgumentException("표시할 메시지는 비어 있거나 Null값이 포함될 수 없습니다.", nameof(_value));
            if (_value.Length < 4 || _value.Length > 9)
                throw new ArgumentException($"메시지 길이는 4~9자여야 합니다. (현재: {_value.Length}글자)", nameof(_value));
        }

        public string GetPayload()
        {
            var address = _value.Substring(0, 4);
            var payloadData = _value.Substring(4).PadRight(5, Constants.PAD_CHAR_FOR_VALUE);
            var sb = new StringBuilder();
            sb.Append(address).Append(CMD_PREFIX).Append(payloadData);
            return sb.ToString();
        }

        public Encoding_Type GetEncodingType() => Encoding_Type.ASCII;
    }
    internal sealed class Disp10SndCommand : ICommand
    {
        private readonly string _value;
        private const string CMD_PREFIX_1 = "LF1";
        private const string CMD_PREFIX_2 = "LF2";

        /// <param name="value">10행 표시기에 보낼 명령어</param>
        internal Disp10SndCommand(string value) => _value = value;

        public void Validate()
        {
            if (string.IsNullOrEmpty(_value))
                throw new ArgumentException("표시할 메시지는 비어 있거나 Null값이 포함될 수 없습니다.", nameof(_value));
            if (_value.Length < 4 || _value.Length > 14)
                throw new ArgumentException($"메시지 길이는 4~14자여야 합니다. (현재: {_value.Length}글자)", nameof(_value));
        }

        public string GetPayload()
        {
            var address = _value.Substring(0, 4);
            var payloadData = _value.Substring(4).PadRight(10, Constants.PAD_CHAR_FOR_VALUE);
            var sb = new StringBuilder();
            sb.Append(address).Append(CMD_PREFIX_1).Append(payloadData.Substring(0, 5));
            sb.Append(address).Append(CMD_PREFIX_2).Append(payloadData.Substring(5, 5));
            return sb.ToString();
        }

        public Encoding_Type GetEncodingType() => Encoding_Type.ASCII;
    }
    internal sealed class Disp16SndCommand : ICommand
    {
        private readonly string _value;
        private const string CMD_PREFIX_1 = "LF1";
        private const string CMD_PREFIX_2 = "LF2";
        private const string CMD_PREFIX_3 = "LF3";
        private const string CMD_PREFIX_4 = "LF4";

        /// <param name="value">16행 표시기에 보낼 명령어</param>
        internal Disp16SndCommand(string value) => _value = value;


        //16행 표시기 문자열 길이 제한 검사(4글자 ~ 20글자)
        public void Validate()
        {
            if (string.IsNullOrEmpty(_value))
                throw new ArgumentException("표시할 메시지는 비어 있거나 Null값이 포함될 수 없습니다.", nameof(_value));
            if (_value.Length < 4 || _value.Length > 20)
                throw new ArgumentException($"메시지 길이는 4~20자여야 합니다. (현재: {_value.Length}글자)", nameof(_value));
        }

        public string GetPayload()
        {
            var address = _value.Substring(0, 4);
            var payloadData = _value.Substring(4).PadRight(16, Constants.PAD_CHAR_FOR_VALUE);
            var sb = new StringBuilder();
            sb.Append(address).Append(CMD_PREFIX_1).Append(payloadData.Substring(0, 5));
            sb.Append(address).Append(CMD_PREFIX_2).Append(payloadData.Substring(5, 5));
            sb.Append(address).Append(CMD_PREFIX_3).Append(payloadData.Substring(10, 5));
            sb.Append(address).Append(CMD_PREFIX_4).Append(payloadData.Substring(15));
            return sb.ToString();
        }

        public Encoding_Type GetEncodingType() => Encoding_Type.ASCII;
    }

    internal sealed class LcdAcCommand : ICommand
    {
        private readonly string _address;
        private const string CMD_PREFIX = "AC111111";

        /// <param name="address">한글 표시기 주소값</param>
        internal LcdAcCommand(string address) => _address = address;

        public void Validate()
        {
            if (string.IsNullOrEmpty(_address) || _address.Length != 4)
                throw new ArgumentException("주소에는 값이 비어있거나 4글자이어야 합니다.", nameof(_address));
        }

        public string GetPayload()
        {
            var sb = new StringBuilder();
            sb.Append(_address).Append(CMD_PREFIX);
            return sb.ToString();
        }

        public Encoding_Type GetEncodingType() => Encoding_Type.ASCII;
    }
    internal sealed class LcdAlCommand : ICommand 
    {
        private readonly string _address;
        private readonly string _value;
        private const int FIXED_FRAME_LENGTH = 6; // CAN PORT + ID + MAIN + SUB
        private const string CMD_PREFIX = "AL";

        /// <param name="value">표시기에 적용될 주소값 + 문자열 데이터</param>
        internal LcdAlCommand(string value) { _address = value.Substring(0, 4);  _value = value.Substring(4); }
        public void Validate()
        {
            // 0. 'value' 파라미터 기본 유효성 검사
            if (string.IsNullOrEmpty(_value))
                throw new ArgumentException("표시할 메시지는 비어 있거나 Null값이 포함될 수 없습니다.", nameof(_value));

            (int charCount, int koreanCount) = CountCharacters(_value);
            // 1. 전체 문자열 길이 제한 검사 (1글자 ~ 9글자)
            if (charCount < 1 || charCount > 9)
                throw new ArgumentException($"표시할 메시지는 최소 1글자, 최대 9글자여야 합니다. (현재: {charCount}글자)", nameof(_value));

            // 2. 순수 한글 문자 개수 제한 검사 (최대 7개)
            if (koreanCount > 7)
                throw new ArgumentException($"표시할 메시지 '{_value}'에 포함된 한글 문자의 개수가 최대 7개를 초과합니다. (현재 한글: {koreanCount}글자)", nameof(_value));
        }

        public string GetPayload()
        {
            var sb = new StringBuilder();
            foreach (var chunk in SplitAndPadByByteLimit(_value))
                sb.Append(_address).Append(CMD_PREFIX).Append(chunk);

            return sb.ToString();
        }

        public Encoding_Type GetEncodingType() => Encoding_Type.KSC949;

        private static IEnumerable<string> SplitAndPadByByteLimit(string inputString)
        {
            var currentChunk = new StringBuilder();
            int currentByteLength = 0;

            foreach (char c in inputString)
            {
                // 'IsKorean' 메서드가 더 빠르므로 유지하고, KscEncoding 객체를 전역으로 사용합니다.
                int charByteLength = IsKorean(c) ? 2 : 1;

                if (currentByteLength + charByteLength > 6)
                {
                    yield return PadToSixBytes(currentChunk.ToString(), FIXED_FRAME_LENGTH - currentByteLength);
                    currentChunk.Clear();
                    currentByteLength = 0;
                }

                currentChunk.Append(c);
                currentByteLength += charByteLength;
            }

            if (currentChunk.Length > 0)
            {
                yield return PadToSixBytes(currentChunk.ToString(), FIXED_FRAME_LENGTH - currentByteLength);
            }
        }

        private static string PadToSixBytes(string chunk, int padLength)
            => padLength > 0 ? chunk.PadRight(chunk.Length + padLength, Constants.PAD_CHAR_FOR_VALUE) : chunk;

        // 한글(AC00–D7A3) 유니코드 범위
        private static bool IsKorean(char c) => c >= '\uAC00' && c <= '\uD7A3';

        private static (int charCount, int koreanCount) CountCharacters(string value)
        {
            int charCount = 0;
            int koreanCount = 0;
            foreach (var c in value)
            {
                charCount++;
                if (IsKorean(c))
                {
                    koreanCount++;
                }
            }
            return (charCount, koreanCount);
        }


    }


}
