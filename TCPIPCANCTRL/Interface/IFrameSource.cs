using System;
using TcpIpCanCtrl.Event;

namespace TcpIpCanCtrl.Interface
{
    // 1) 공통 인터페이스 정의
    internal interface IFrameSource<T>
    {
        /// <summary>
        /// 파싱된 프레임이 준비될 때마다 발생합니다.
        /// 구독자는 이 이벤트를 통해 JbFrame을 받아 처리할 수 있습니다.
        /// </summary>
        event EventHandler<FrameReceivedEventArgs<T>> FrameReceived;
    }
}
