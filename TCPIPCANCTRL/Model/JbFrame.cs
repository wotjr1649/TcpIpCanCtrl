namespace TcpIpCanCtrl.Model
{
    public readonly struct JbFrame
    {
        internal string pAddr { get; }
        internal char pMain { get; }
        internal char pSub { get; }
        internal string pData { get; }

        internal JbFrame(string addr, char main, char sub, string value)
        {
            pAddr = addr;
            pMain = main;
            pSub = sub;
            pData = value;
        }

        public override string ToString()
            => $"[{pAddr} {pMain}{pSub} \"{pData}\"]";
    }
}
