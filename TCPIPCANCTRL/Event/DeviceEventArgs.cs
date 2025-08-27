using System;
using System.Collections.Generic;
using System.Text;
using TcpIpCanCtrl.Model;

namespace TcpIpCanCtrl.Event
{
    #region 상위로 재발행할 이벤트 (디바이스 인덱스 포함)
    public sealed class DeviceErrorEventArgs : EventArgs
    {
        public int JbIndex { get; private set; }
        public OnErrorEventArgs Original { get; private set; }
        public DeviceErrorEventArgs(int jbIndex, OnErrorEventArgs original) { JbIndex = jbIndex; Original = original; }
    }

    public sealed class DeviceRcvUnitEventArgs : EventArgs
    {
        public int JbIndex { get; private set; }
        public OnRcvUnitEventArgs Original { get; private set; }
        public DeviceRcvUnitEventArgs(int jbIndex, OnRcvUnitEventArgs original) { JbIndex = jbIndex; Original = original; }
    }

    public sealed class DeviceRcvBarEventArgs : EventArgs
    {
        public int JbIndex { get; private set; }
        public OnRcvBarEventArgs Original { get; private set; }
        public DeviceRcvBarEventArgs(int jbIndex, OnRcvBarEventArgs original) { JbIndex = jbIndex; Original = original; }
    }

    public sealed class DeviceFrameEventArgs : EventArgs
    {
        public int JbIndex { get; private set; }
        public JbFrame Frame { get; private set; }
        public DeviceFrameEventArgs(int jbIndex, JbFrame frame) { JbIndex = jbIndex; Frame = frame; }
    }
    #endregion
}
