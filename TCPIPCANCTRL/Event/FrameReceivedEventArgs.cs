using System;

namespace TcpIpCanCtrl.Event
{
    public class FrameReceivedEventArgs<T> : EventArgs
    {
        public T Frame { get; }
        public FrameReceivedEventArgs(T frame) => Frame = frame;
    }
}
