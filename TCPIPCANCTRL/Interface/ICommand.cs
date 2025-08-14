using TcpIpCanCtrl.Util;

namespace TcpIpCanCtrl.Interface
{
    internal interface ICommand {

        /// <summary>
        /// 커맨드 객체 내부 데이터의 유효성을 검사합니다.
        /// </summary>
        void Validate();

        /// <summary>
        /// 완성된 페이로드 문자열을 반환합니다.
        /// </summary>
        string GetPayload();

        /// <summary>
        /// 페이로드에 사용될 인코딩 타입을 반환합니다.
        /// </summary>
        Encoding_Type GetEncodingType();
    }
}
