using System;

namespace TcpIpCanCtrl.Event
{
    public class OnRcvBarEventArgs : EventArgs
    {
        public string Address { get; }
        public int Status { get; }
        public string Value { get; }

        public OnRcvBarEventArgs(string pAddr, string pBarcode)
        {
            Address = pAddr;
            Status = 0;
            Value = pBarcode;          
        }

        public static OnRcvBarEventArgs Parse(string pAddr, string pBarcode)
     => new OnRcvBarEventArgs(pAddr, pBarcode);
    }
}
