using System;
using System.Windows.Forms;

namespace ModernMenuManager
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Elevated helper mode: ModernMenuManager.exe --machine block|unblock {GUID},{GUID}
            if (args.Length == 3 && args[0] == "--machine")
                return BlockList.RunMachineCommand(args[1], args[2]);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
