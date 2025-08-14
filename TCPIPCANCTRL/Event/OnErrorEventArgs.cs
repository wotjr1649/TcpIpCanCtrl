using System;

namespace TcpIpCanCtrl.Event
{
    /// <summary>
    /// 송·수신 또는 파싱 중 오류가 발생했을 때 전달되는 이벤트 인자입니다.
    /// </summary>
    public class OnErrorEventArgs : EventArgs
    {
        /// <summary>
        /// 발생한 예외 정보
        /// </summary>
        public Exception Exception { get; }

        public OnErrorEventArgs(Exception exception)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }
    }
}