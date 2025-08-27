using System;
using System.Collections.Generic;
using System.Text;
using TcpIpCanCtrl.Model;

namespace TcpIpCanCtrl.Event
{
    #region 내부에서 발행해서 처리하는 이벤트

    public sealed class OnRcvUnitEventArgs
    {
        public string Address { get; }
        public int Status { get; }
        public string Data { get; }


        public OnRcvUnitEventArgs(JbFrame frame)
        {

            Address = frame.pAddr;
            Data = frame.pData;

            var command = $"{frame.pMain}{frame.pSub}";
            Status = command is "ME" ? 1
                   : command is "RF" ? 3
                   : command is "RC" ? (Data is "-----" ? 2 : 0)
                   : 0;

        }

        public static OnRcvUnitEventArgs Parse(JbFrame frame)
            => new OnRcvUnitEventArgs(frame);
    }

    public sealed class OnRcvBarEventArgs : EventArgs
    {
        public string Address { get; }
        public int Status { get; }
        public string Data { get; }

        public OnRcvBarEventArgs(string pAddr, string pBarcode)
        {
            Address = pAddr;
            Status = 0;
            Data = pBarcode;
        }

        public static OnRcvBarEventArgs Parse(string pAddr, string pBarcode)
     => new OnRcvBarEventArgs(pAddr, pBarcode);
    }

    public sealed class OnErrorEventArgs : EventArgs
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

    public sealed class FrameReceivedEventArgs<T> : EventArgs
    {
        public T Frame { get; }
        public FrameReceivedEventArgs(T frame) => Frame = frame;
    }

    /// <summary>
    /// 타임아웃된 요청 정보를 담는 이벤트 인수
    /// </summary>
    public sealed class RequestTimedOutEventArgs : EventArgs
    {
        public string DeviceKey { get; }
        public JbFrame OriginalFrame { get; }

        public RequestTimedOutEventArgs(string deviceKey, JbFrame originalFrame)
        {
            DeviceKey = deviceKey;
            OriginalFrame = originalFrame;
        }
    }

    #endregion
}
