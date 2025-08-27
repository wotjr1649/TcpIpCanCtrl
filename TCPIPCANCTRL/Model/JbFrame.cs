namespace TcpIpCanCtrl.Model
{
    public readonly struct JbFrame
    {
        internal int pIndex { get; }
        internal string pAddr { get; }
        internal char pMain { get; }
        internal char pSub { get; }
        internal string pData { get; }

        internal JbFrame(int index, string addr, char main, char sub, string value)
        {
            pIndex = index;
            pAddr = addr;
            pMain = main;
            pSub = sub;
            pData = value;
        }

        public override string ToString()
            => $"[{pIndex} : {pAddr} {pMain}{pSub} \"{pData}\"]";
    }
}
