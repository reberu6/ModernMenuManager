using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModernMenuManager
{
    sealed class MainForm : Form
    {
        readonly ListView list;
        readonly Label statusLabel;
        readonly Button refreshButton, applyButton, restartButton, logButton;
        readonly ListViewGroup packageGroup, builtInGroup, otherGroup;
        readonly Font regularFont, boldFont;
        bool populating;

        public MainForm()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Text = "新式右鍵選單管理 v" + version.ToString(3);
            Font = new Font("Microsoft JhengHei UI", 9F);
            float scale = DeviceDpi / 96f;
            ClientSize = new Size((int)(960 * scale), (int)(600 * scale));
            MinimumSize = new Size((int)(600 * scale), (int)(400 * scale));
            StartPosition = FormStartPosition.CenterScreen;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding((int)(8 * scale)) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var help = new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 0, 0, (int)(6 * scale)),
                Text = "打勾 = 允許顯示，取消打勾 = 隱藏。每個項目只在特定地方出現（看「出現在」欄，有些 App 也會自己決定何時出現）。\n" +
                       "改完按「套用變更」，再按「重新啟動檔案總管」就會生效。" +
                       "隱藏只是加進 Windows 的封鎖清單，隨時可以打勾恢復。\n「顯示其他選項」裡的舊式項目請用 ContextMenuManager 管理。",
            };

            list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false,
                ShowItemToolTips = true,
            };
            list.Columns.Add("選單項目", (int)(260 * scale));
            list.Columns.Add("來源 App", (int)(220 * scale));
            list.Columns.Add("出現在", (int)(220 * scale));
            list.Columns.Add("狀態", (int)(160 * scale));
            packageGroup = new ListViewGroup("App 加入的項目");
            builtInGroup = new ListViewGroup("Windows 內建");
            otherGroup = new ListViewGroup("其他已隱藏的項目");
            list.Groups.AddRange(new[] { packageGroup, builtInGroup, otherGroup });
            list.ItemChecked += OnItemChecked;

            var menu = new ContextMenuStrip();
            menu.Items.Add("隱藏給所有使用者（需要管理員）", null, async (s, e) => await HideForAllUsersAsync());
            menu.Items.Add("複製 CLSID", null, (s, e) => CopyClsid());
            list.ContextMenuStrip = menu;

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, (int)(6 * scale), 0, 0) };
            refreshButton = MakeButton("重新整理", async (s, e) => await RefreshListAsync());
            applyButton = MakeButton("套用變更", async (s, e) => await ApplyAsync());
            restartButton = MakeButton("重新啟動檔案總管", async (s, e) => await RestartExplorerAsync());
            logButton = MakeButton("開啟記錄檔", (s, e) => OpenLog());
            statusLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding((int)(12 * scale), (int)(8 * scale), 0, 0) };
            buttons.Controls.AddRange(new Control[] { refreshButton, applyButton, restartButton, logButton, statusLabel });

            layout.Controls.Add(help, 0, 0);
            layout.Controls.Add(list, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);

            regularFont = new Font(list.Font, FontStyle.Regular);
            boldFont = new Font(list.Font, FontStyle.Bold);
            Shown += async (s, e) => await RefreshListAsync();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                regularFont?.Dispose();
                boldFont?.Dispose();
            }
        }

        static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 2, 8, 2) };
            b.Click += onClick;
            return b;
        }

        void SetBusy(bool busy, string message)
        {
            refreshButton.Enabled = applyButton.Enabled = restartButton.Enabled = !busy;
            list.Enabled = !busy;
            UseWaitCursor = busy;
            statusLabel.Text = message;
        }

        async Task RefreshListAsync()
        {
            SetBusy(true, "掃描中，請稍候…");
            List<MenuEntry> entries;
            try
            {
                entries = await Task.Run(() => MenuScanner.Scan());
            }
            catch (Exception ex)
            {
                Log.Write("Scan failed: " + ex);
                Log.Save();
                SetBusy(false, "掃描失敗，請按「開啟記錄檔」查看原因。");
                return;
            }

            populating = true;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var e in entries.OrderBy(x => x.Source).ThenBy(x => x.AppName, StringComparer.CurrentCulture).ThenBy(x => x.Title, StringComparer.CurrentCulture))
            {
                var item = new ListViewItem(e.Title)
                {
                    Tag = e,
                    Checked = !e.IsHidden,
                    Group = e.Source == EntrySource.Package ? packageGroup : e.Source == EntrySource.BuiltIn ? builtInGroup : otherGroup,
                    ToolTipText = "CLSID " + BlockList.Format(e.Clsid) + (e.PackageFullName != null ? "\n" + e.PackageFullName : ""),
                };
                item.SubItems.Add(e.AppName ?? "");
                item.SubItems.Add(e.ItemTypesText);
                item.SubItems.Add(StatusText(e, !e.IsHidden));
                list.Items.Add(item);
            }
            list.EndUpdate();
            populating = false;

            int hidden = entries.Count(x => x.IsHidden);
            SetBusy(false, string.Format("共 {0} 個項目，其中 {1} 個已隱藏。", entries.Count, hidden));
        }

        static string StatusText(MenuEntry e, bool wantVisible)
        {
            if (wantVisible == !e.IsHidden)
                return e.BlockedMachine ? "已隱藏（所有使用者）" : e.BlockedUser ? "已隱藏" : "允許顯示";
            return wantVisible ? "待套用：恢復顯示" : "待套用：隱藏";
        }

        void OnItemChecked(object sender, ItemCheckedEventArgs args)
        {
            if (populating || !(args.Item.Tag is MenuEntry e))
                return;
            args.Item.SubItems[3].Text = StatusText(e, args.Item.Checked);
            args.Item.Font = args.Item.Checked == !e.IsHidden ? regularFont : boldFont;
        }

        async Task ApplyAsync()
        {
            var toHide = new List<MenuEntry>();
            var toShow = new List<MenuEntry>();
            foreach (ListViewItem item in list.Items)
            {
                var e = (MenuEntry)item.Tag;
                if (!item.Checked && !e.IsHidden) toHide.Add(e);
                else if (item.Checked && e.IsHidden) toShow.Add(e);
            }
            if (toHide.Count == 0 && toShow.Count == 0)
            {
                statusLabel.Text = "沒有需要套用的變更。";
                return;
            }

            var failedNames = new List<string>();
            var failedLogs = new List<string>();
            var machineToShow = new List<MenuEntry>();
            int success = 0;

            foreach (var e in toHide)
            {
                try
                {
                    BlockList.BlockUser(e.Clsid, e.Title);
                    success++;
                }
                catch (Exception ex)
                {
                    failedNames.Add(e.Title);
                    failedLogs.Add("Hide failed " + BlockList.Format(e.Clsid) + " (" + e.Title + "): " + ex);
                }
            }

            foreach (var e in toShow)
            {
                try
                {
                    BlockList.UnblockUser(e.Clsid);
                    if (e.BlockedMachine)
                        machineToShow.Add(e);
                    else
                        success++;
                }
                catch (Exception ex)
                {
                    failedNames.Add(e.Title);
                    failedLogs.Add("Show failed " + BlockList.Format(e.Clsid) + " (" + e.Title + "): " + ex);
                }
            }

            if (machineToShow.Count > 0)
            {
                bool elevatedSuccess = false;
                try
                {
                    elevatedSuccess = await BlockList.RunMachineElevatedAsync(
                        "unblock", machineToShow.Select(e => e.Clsid));
                }
                catch (Exception ex)
                {
                    failedLogs.Add("Machine unblock failed: " + ex);
                }
                foreach (var e in machineToShow)
                {
                    if (elevatedSuccess)
                        success++;
                    else
                    {
                        failedNames.Add(e.Title);
                        failedLogs.Add("Machine unblock did not complete for " + BlockList.Format(e.Clsid) +
                                       " (" + e.Title + "): cancelled or insufficient permissions.");
                    }
                }
            }

            // RefreshListAsync resets the scan log, so record apply failures after refreshing.
            await RefreshListAsync();
            foreach (var error in failedLogs)
                Log.Write(error);
            if (failedLogs.Count > 0)
                Log.Save();

            string summary = string.Format("成功 {0} 項，失敗 {1} 項", success, failedNames.Count);
            if (failedNames.Count > 0)
                summary += "：" + string.Join("、", failedNames);
            statusLabel.Text = summary;
            if (success > 0)
            {
                await AskRestartExplorerAsync();
                if (statusLabel.Text != summary)
                    statusLabel.Text = summary + "；" + statusLabel.Text;
            }
        }

        async Task HideForAllUsersAsync()
        {
            var selected = list.SelectedItems.Cast<ListViewItem>().Select(i => (MenuEntry)i.Tag).ToList();
            if (selected.Count == 0)
                return;
            var r = MessageBox.Show(this,
                "如果一般的隱藏沒有效果，可以改成隱藏給這台電腦的所有使用者。\n需要管理員權限，之後打勾恢復時也會再問一次。\n\n要繼續嗎？",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK)
                return;
            if (await BlockList.RunMachineElevatedAsync("block", selected.Select(e => e.Clsid)))
            {
                await RefreshListAsync();
                await AskRestartExplorerAsync();
            }
            else
            {
                statusLabel.Text = "沒有完成（已取消或權限不足）";
            }
        }

        void CopyClsid()
        {
            var selected = list.SelectedItems.Cast<ListViewItem>().Select(i => BlockList.Format(((MenuEntry)i.Tag).Clsid)).ToList();
            if (selected.Count == 0)
                return;
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, selected));
            }
            catch (Exception ex)
            {
                Log.Write("Copy CLSID failed: " + ex);
                Log.Save();
                statusLabel.Text = "複製失敗，剪貼簿被其他程式占用";
            }
        }

        async Task AskRestartExplorerAsync()
        {
            var r = MessageBox.Show(this, "已套用。要現在重新啟動檔案總管讓變更生效嗎？\n（桌面和工作列會閃一下，開著的資料夾視窗會關閉）",
                                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes)
                await RestartExplorerAsync();
        }

        async Task RestartExplorerAsync()
        {
            SetBusy(true, "正在重新啟動檔案總管…");
            try
            {
                string result = await Task.Run(() => RestartShell());
                SetBusy(false, result);
            }
            catch (Exception ex)
            {
                Log.Write("Restart Explorer failed: " + ex);
                Log.Save();
                SetBusy(false, "重新啟動檔案總管失敗。");
                MessageBox.Show(this, "重新啟動檔案總管失敗：" + ex.Message, Text,
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static string RestartShell()
        {
            int session;
            using (var self = Process.GetCurrentProcess())
                session = self.SessionId;

            var shellWindow = NativeMethods.GetShellWindow();
            uint shellPid = 0;
            bool foundShell = false;
            if (shellWindow != IntPtr.Zero &&
                NativeMethods.GetWindowThreadProcessId(shellWindow, out shellPid) != 0 && shellPid != 0)
            {
                try
                {
                    using (var shell = Process.GetProcessById((int)shellPid))
                    {
                        if (shell.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) &&
                            shell.SessionId == session)
                        {
                            foundShell = true;
                            shell.Kill();
                            if (!shell.WaitForExit(5000))
                                throw new TimeoutException("殼層行程未能在 5 秒內結束。");
                        }
                    }
                }
                catch (ArgumentException)
                {
                    // The shell exited before we could open it; use the session-only fallback.
                }
            }

            if (!foundShell)
            {
                // Only fall back to all Explorer processes in this session if the shell PID is unavailable.
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    using (p)
                    {
                        try
                        {
                            if (p.SessionId != session)
                                continue;
                            p.Kill();
                            if (!p.WaitForExit(5000))
                                Log.Write("Explorer process " + p.Id + " did not exit within 5 seconds.");
                        }
                        catch (Exception ex)
                        {
                            Log.Write("Explorer fallback failed: " + ex.Message);
                        }
                    }
                }
            }

            for (int i = 0; i < 20; i++)
            {
                System.Threading.Thread.Sleep(500);
                if (NativeMethods.GetShellWindow() != IntPtr.Zero)
                    return "檔案總管已重新啟動。";
            }

            // The shell did not come back automatically. Launch it only once, without opening a folder.
            Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            return "檔案總管未在 10 秒內自動恢復，已嘗試啟動。";
        }

        void OpenLog()
        {
            if (File.Exists(Log.FilePath))
                Process.Start(Path.Combine(Environment.SystemDirectory, "notepad.exe"), "\"" + Log.FilePath + "\"");
            else
                statusLabel.Text = "還沒有記錄檔。";
        }
    }
}
