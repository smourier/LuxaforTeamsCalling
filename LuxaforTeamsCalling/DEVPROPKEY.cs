using System;
using System.Runtime.InteropServices;

namespace LuxaforTeamsCalling
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public int pid;

        public DEVPROPKEY(Guid fmtid, int pid)
        {
            this.fmtid = fmtid;
            this.pid = pid;
        }

        public override string ToString() => fmtid.ToString("B") + " " + pid;
    }
}
