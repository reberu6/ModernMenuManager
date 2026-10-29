using System;
using System.IO;
using System.Text;

namespace ModernMenuManager
{
    /// <summary>Plain-text scan log under %LOCALAPPDATA%\ModernMenuManager, for troubleshooting.</summary>
    static class Log
    {
        static readonly object Gate = new object();
        static readonly StringBuilder Buffer = new StringBuilder();

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "ModernMenuManager", "log.txt");

        public static void Write(string message)
        {
            lock (Gate)
                Buffer.AppendLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message);
        }

        public static void Reset()
        {
            lock (Gate)
                Buffer.Clear();
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string text;
                lock (Gate)
                    text = Buffer.ToString();
                File.WriteAllText(FilePath, text, Encoding.UTF8);
            }
            catch
            {
                // Logging must never break the app.
            }
        }
    }
}
