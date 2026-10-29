using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Win32;

namespace ModernMenuManager
{
    /// <summary>
    /// Finds the commands shown in the Windows 11 context menu. Apps add them through the
    /// windows.fileExplorerContextMenus extension in their package manifest, so we read every
    /// installed package's AppxManifest.xml and collect the command CLSIDs.
    /// </summary>
    static class MenuScanner
    {
        const string PackageRepositoryKey =
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

        // Windows commands in the modern menu that are not declared by any package manifest.
        static readonly (Guid Clsid, string Title)[] BuiltIns =
        {
            (new Guid("e2bf9676-5f8f-435c-97eb-11607a5bedf7"), "分享"),
        };

        public static List<MenuEntry> Scan()
        {
            Log.Reset();
            Log.Write("Scan started. OS " + Environment.OSVersion.VersionString);

            var packages = FindPackages();
            Log.Write("Packages found: " + packages.Count);

            var entries = new Dictionary<Guid, MenuEntry>();
            int manifestsRead = 0;
            foreach (var pkg in packages)
            {
                var manifest = FindManifest(pkg.Key, pkg.Value);
                if (manifest == null)
                    continue;
                manifestsRead++;
                try
                {
                    ReadManifest(pkg.Key, manifest, entries);
                }
                catch (Exception ex)
                {
                    Log.Write("Manifest error " + manifest + ": " + ex.Message);
                }
            }
            Log.Write("Manifests read: " + manifestsRead + ", menu commands: " + entries.Count);

            foreach (var b in BuiltIns)
                if (!entries.ContainsKey(b.Clsid))
                    entries[b.Clsid] = new MenuEntry { Clsid = b.Clsid, Source = EntrySource.BuiltIn, FixedTitle = b.Title, AppName = "Windows" };

            var userBlocked = BlockList.Read(RegistryHive.CurrentUser);
            var machineBlocked = BlockList.Read(RegistryHive.LocalMachine);
            foreach (var g in userBlocked.Concat(machineBlocked))
                if (!entries.ContainsKey(g))
                    entries[g] = new MenuEntry { Clsid = g, Source = EntrySource.Other, FixedTitle = ClassicName(g), AppName = "" };

            foreach (var e in entries.Values)
            {
                e.BlockedUser = userBlocked.Contains(e.Clsid);
                e.BlockedMachine = machineBlocked.Contains(e.Clsid);
            }

            var list = entries.Values.ToList();
            ProbeTitles(list.Where(e => e.Source == EntrySource.Package).ToList());

            foreach (var e in list)
                Log.Write(string.Format("{0} [{1}] {2} | {3} | {4} | user={5} machine={6}",
                    BlockList.Format(e.Clsid), e.Source, e.Title, e.AppName, e.PackageFullName, e.BlockedUser, e.BlockedMachine));
            Log.Save();
            return list;
        }

        /// <summary>Package full name -> install location (may be null).</summary>
        static Dictionary<string, string> FindPackages()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PackageRepositoryKey))
                {
                    if (key != null)
                    {
                        foreach (var name in key.GetSubKeyNames())
                        {
                            string root = null;
                            using (var sub = key.OpenSubKey(name))
                                root = sub?.GetValue("PackageRootFolder") as string;
                            result[name] = root;
                        }
                    }
                }
                Log.Write("Packages from registry: " + result.Count);
            }
            catch (Exception ex)
            {
                Log.Write("Registry package list failed: " + ex.Message);
            }

            // Also ask PowerShell; it covers sparse packages (e.g. VS Code, 7-Zip) and anything the registry missed.
            try
            {
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
                    "Get-AppxPackage | ForEach-Object { $_.PackageFullName + '|' + $_.InstallLocation }\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                int added = 0;
                using (var p = Process.Start(psi))
                {
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        var parts = line.Split(new[] { '|' }, 2);
                        if (parts[0].Length == 0)
                            continue;
                        var location = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
                        if (!result.TryGetValue(parts[0], out var existing))
                        {
                            result[parts[0]] = location;
                            added++;
                        }
                        else if (existing == null)
                        {
                            result[parts[0]] = location;
                        }
                    }
                    p.WaitForExit(30000);
                }
                Log.Write("Packages added by Get-AppxPackage: " + added);
            }
            catch (Exception ex)
            {
                Log.Write("Get-AppxPackage failed: " + ex.Message);
            }
            return result;
        }

        static string FindManifest(string fullName, string location)
        {
            var candidates = new List<string>();
            var staged = PackagePath(fullName, true);
            if (staged != null) candidates.Add(staged);
            var path = PackagePath(fullName, false);
            if (path != null) candidates.Add(path);
            if (location != null) candidates.Add(location);
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps", fullName));

            foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var file = Path.Combine(dir, "AppxManifest.xml");
                    if (File.Exists(file))
                        return file;
                }
                catch
                {
                    // Bad path or access denied; try the next one.
                }
            }
            return null;
        }

        static string PackagePath(string fullName, bool staged)
        {
            try
            {
                uint len = 1024;
                var sb = new StringBuilder((int)len);
                int rc = staged
                    ? NativeMethods.GetStagedPackagePathByFullName(fullName, ref len, sb)
                    : NativeMethods.GetPackagePathByFullName(fullName, ref len, sb);
                return rc == 0 ? sb.ToString() : null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }

        static void ReadManifest(string fullName, string manifestPath, Dictionary<Guid, MenuEntry> entries)
        {
            var text = File.ReadAllText(manifestPath);
            if (text.IndexOf("fileExplorerContextMenus", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            var doc = XDocument.Parse(text);
            var root = doc.Root;
            var packageName = (string)root.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity")?.Attribute("Name") ?? "";
            var displayRaw = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Properties")?
                                 .Elements().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value;
            var appName = Resolve(fullName, packageName, displayRaw) ?? packageName;

            // CLSID -> display name of its COM server registration.
            var comNames = new Dictionary<Guid, string>();
            foreach (var cls in doc.Descendants().Where(e => e.Name.LocalName == "Class"))
            {
                if (!Guid.TryParse((string)cls.Attribute("Id") ?? "", out var id))
                    continue;
                var name = (string)cls.Attribute("DisplayName") ?? (string)cls.Parent?.Attribute("DisplayName");
                if (name != null)
                    comNames[id] = Resolve(fullName, packageName, name) ?? name;
            }

            var menus = doc.Descendants().Where(e => e.Name.LocalName == "Extension" &&
                                                     (string)e.Attribute("Category") == "windows.fileExplorerContextMenus");
            foreach (var ext in menus)
            {
                foreach (var itemType in ext.Descendants().Where(e => e.Name.LocalName == "ItemType"))
                {
                    var type = (string)itemType.Attribute("Type") ?? "";
                    foreach (var verb in itemType.Elements().Where(e => e.Name.LocalName == "Verb"))
                    {
                        if (!Guid.TryParse((string)verb.Attribute("Clsid") ?? "", out var clsid))
                            continue;
                        if (!entries.TryGetValue(clsid, out var entry))
                        {
                            entry = new MenuEntry
                            {
                                Clsid = clsid,
                                Source = EntrySource.Package,
                                AppName = appName,
                                PackageFullName = fullName,
                                VerbId = (string)verb.Attribute("Id"),
                            };
                            comNames.TryGetValue(clsid, out entry.ComDisplayName);
                            entries[clsid] = entry;
                        }
                        if (type.Length > 0)
                            entry.ItemTypes.Add(type);
                    }
                }
            }
        }

        /// <summary>Turns an "ms-resource:" reference into the localized string for that package.</summary>
        static string Resolve(string fullName, string packageName, string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                return value;

            var res = value.Substring("ms-resource:".Length);
            var candidates = new List<string>();
            if (res.StartsWith("//"))
            {
                candidates.Add(value);
            }
            else if (res.StartsWith("/"))
            {
                candidates.Add("ms-resource://" + packageName + res);
            }
            else
            {
                candidates.Add("ms-resource://" + packageName + "/Resources/" + res);
                candidates.Add("ms-resource://" + packageName + "/" + res);
            }

            foreach (var c in candidates)
            {
                var sb = new StringBuilder(1024);
                if (NativeMethods.SHLoadIndirectString("@{" + fullName + "? " + c + "}", sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0)
                    return sb.ToString();
            }
            return null;
        }

        static string ClassicName(Guid clsid)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64))
                using (var key = root.OpenSubKey(@"CLSID\" + BlockList.Format(clsid)))
                {
                    var name = key?.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }
            catch
            {
                // Fall through to the generic label.
            }
            return "（不明項目）";
        }

        /// <summary>
        /// Asks each command for the exact text it shows in the menu. Commands run out of process
        /// (dllhost or the app's exe), so a misbehaving one cannot crash us; slow ones are abandoned.
        /// </summary>
        static void ProbeTitles(List<MenuEntry> entries)
        {
            var threads = new List<Thread>();
            foreach (var entry in entries)
            {
                var e = entry;
                var t = new Thread(() =>
                {
                    try
                    {
                        e.ProbedTitle = GetTitle(e.Clsid);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Title probe failed " + BlockList.Format(e.Clsid) + ": " + ex.Message);
                    }
                }) { IsBackground = true };
                t.SetApartmentState(ApartmentState.MTA);
                t.Start();
                threads.Add(t);
            }

            var deadline = DateTime.UtcNow.AddSeconds(8);
            foreach (var t in threads)
            {
                var left = deadline - DateTime.UtcNow;
                t.Join(left > TimeSpan.Zero ? left : TimeSpan.Zero);
            }
        }

        static string GetTitle(Guid clsid)
        {
            NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_MULTITHREADED);
            var iidUnknown = new Guid("00000000-0000-0000-C000-000000000046");
            int hr = NativeMethods.CoCreateInstance(ref clsid, IntPtr.Zero, NativeMethods.CLSCTX_LOCAL_SERVER, ref iidUnknown, out object obj);
            if (hr < 0 || obj == null)
            {
                Log.Write("CoCreateInstance " + BlockList.Format(clsid) + " hr=0x" + hr.ToString("X8"));
                return null;
            }
            try
            {
                if (!(obj is IExplorerCommand cmd))
                    return null;
                hr = cmd.GetTitle(IntPtr.Zero, out var p);
                if (hr < 0 || p == IntPtr.Zero)
                    return null;
                try
                {
                    var title = System.Runtime.InteropServices.Marshal.PtrToStringUni(p);
                    return string.IsNullOrWhiteSpace(title) ? null : title;
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeCoTaskMem(p);
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(obj);
            }
        }
    }
}
