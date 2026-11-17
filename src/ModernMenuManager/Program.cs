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

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => ReportUnhandledException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                ReportUnhandledException(e.ExceptionObject as Exception ??
                                         new Exception(e.ExceptionObject?.ToString() ?? "Unknown error"));

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        static void ReportUnhandledException(Exception ex)
        {
            try
            {
                Log.Write("Unhandled exception: " + ex);
                Log.Save();
            }
            catch { /* A logging failure must not trigger another unhandled exception. */ }

            try
            {
                MessageBox.Show("發生錯誤，詳情請按『開啟記錄檔』",
                                "新式右鍵選單管理", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { /* The process may already be terminating. */ }
        }
    }
}
