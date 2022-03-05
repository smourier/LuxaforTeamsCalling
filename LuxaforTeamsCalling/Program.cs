using System;
using System.Windows.Forms;

namespace LuxaforTeamsCalling
{
    static class Program
    {
        internal static readonly SingleInstance Singleton = new SingleInstance(typeof(Program).FullName);

        [STAThread]
        private static void Main()
        {
            // NOTE: if this always return false, close & restart Visual Studio
            // this is probably due to the vshost.exe thing
            Singleton.RunFirstInstance(SingleInstanceMain);
        }

        private static void SingleInstanceMain()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Main());
        }
    }
}
