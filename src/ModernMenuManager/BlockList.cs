using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ModernMenuManager
{
    /// <summary>
    /// The shell's extension block list. A CLSID listed as a value name under this key is not loaded by
    /// Explorer, which hides that command from the context menu. Deleting the value brings it back.
    /// </summary>
    static class BlockList
    {
        public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

        public static string Format(Guid clsid) => clsid.ToString("B").ToUpperInvariant();

        static RegistryKey OpenBase(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

        public static HashSet<Guid> Read(RegistryHive hive)
        {
            var result = new HashSet<Guid>();
            try
            {
                using (var root = OpenBase(hive))
                using (var key = root.OpenSubKey(KeyPath))
                {
                    if (key == null)
                        return result;
                    foreach (var name in key.GetValueNames())
                        if (Guid.TryParse(name, out var g))
                            result.Add(g);
                }
            }
            catch (Exception ex)
            {
                Log.Write("Read block list " + hive + " failed: " + ex.Message);
            }
            return result;
        }

        static void Block(RegistryKey key, Guid clsid, string note) =>
            key.SetValue(Format(clsid), note ?? "", RegistryValueKind.String);

        static void Unblock(RegistryKey key, Guid clsid)
        {
            foreach (var name in key.GetValueNames())
                if (Guid.TryParse(name, out var g) && g == clsid)
                    key.DeleteValue(name, false);
        }

        public static void BlockUser(Guid clsid, string note)
        {
            using (var root = OpenBase(RegistryHive.CurrentUser))
            using (var key = root.CreateSubKey(KeyPath))
                Block(key, clsid, note);
        }

        public static void UnblockUser(Guid clsid)
        {
            using (var root = OpenBase(RegistryHive.CurrentUser))
            using (var key = root.OpenSubKey(KeyPath, true))
                if (key != null)
                    Unblock(key, clsid);
        }

        /// <summary>Relaunches this exe elevated to change the machine-wide list. Returns false if cancelled or failed.</summary>
        public static bool RunMachineElevated(string op, IEnumerable<Guid> clsids)
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath,
                                           "--machine " + op + " " + string.Join(",", clsids.Select(Format)))
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (Win32Exception)
            {
                // The user declined the UAC prompt.
                return false;
            }
        }

        public static int RunMachineCommand(string op, string list)
        {
            try
            {
                using (var root = OpenBase(RegistryHive.LocalMachine))
                using (var key = root.CreateSubKey(KeyPath))
                {
                    foreach (var part in list.Split(','))
                    {
                        if (!Guid.TryParse(part, out var g))
                            continue;
                        if (op == "block")
                            Block(key, g, "ModernMenuManager");
                        else if (op == "unblock")
                            Unblock(key, g);
                        else
                            return 2;
                    }
                }
                return 0;
            }
            catch
            {
                return 1;
            }
        }
    }
}
