using TcpIpCanCtrl.Model;

namespace TcpIpCanCtrl.Event
{
    public class OnRcvUnitEventArgs
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

}
