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
                Text = "打勾 = 顯示在新式右鍵選單，取消打勾 = 隱藏。改完按「套用變更」，再按「重新啟動檔案總管」就會生效。\n" +
                       "隱藏只是加進 Windows 的封鎖清單，隨時可以打勾恢復。「顯示其他選項」裡的舊式項目請用 ContextMenuManager 管理。",
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
            restartButton = MakeButton("重新啟動檔案總管", (s, e) => RestartExplorer());
            logButton = MakeButton("開啟記錄檔", (s, e) => OpenLog());
            statusLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding((int)(12 * scale), (int)(8 * scale), 0, 0) };
            buttons.Controls.AddRange(new Control[] { refreshButton, applyButton, restartButton, logButton, statusLabel });

            layout.Controls.Add(help, 0, 0);
            layout.Controls.Add(list, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);

            Shown += async (s, e) => await RefreshListAsync();
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
                return e.BlockedMachine ? "已隱藏（所有使用者）" : e.BlockedUser ? "已隱藏" : "顯示中";
            return wantVisible ? "待套用：恢復顯示" : "待套用：隱藏";
        }

        void OnItemChecked(object sender, ItemCheckedEventArgs args)
        {
            if (populating || !(args.Item.Tag is MenuEntry e))
                return;
            args.Item.SubItems[3].Text = StatusText(e, args.Item.Checked);
            args.Item.Font = new Font(list.Font, args.Item.Checked == !e.IsHidden ? FontStyle.Regular : FontStyle.Bold);
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

            try
            {
                foreach (var e in toHide)
                    BlockList.BlockUser(e.Clsid, e.Title);
                foreach (var e in toShow)
                    BlockList.UnblockUser(e.Clsid);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "寫入登錄檔失敗：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            var machine = toShow.Where(e => e.BlockedMachine).Select(e => e.Clsid).ToList();
            if (machine.Count > 0 && !BlockList.RunMachineElevated("unblock", machine))
                MessageBox.Show(this, "有項目是「所有使用者」層級的隱藏，需要管理員權限才能恢復，這次沒有完成。", Text,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);

            await RefreshListAsync();
            AskRestartExplorer();
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
            if (BlockList.RunMachineElevated("block", selected.Select(e => e.Clsid)))
            {
                await RefreshListAsync();
                AskRestartExplorer();
            }
        }

        void CopyClsid()
        {
            var selected = list.SelectedItems.Cast<ListViewItem>().Select(i => BlockList.Format(((MenuEntry)i.Tag).Clsid)).ToList();
            if (selected.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, selected));
        }

        void AskRestartExplorer()
        {
            var r = MessageBox.Show(this, "已套用。要現在重新啟動檔案總管讓變更生效嗎？\n（桌面和工作列會閃一下，開著的資料夾視窗會關閉）",
                                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes)
                RestartExplorer();
        }

        void RestartExplorer()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    try { p.Kill(); p.WaitForExit(5000); } catch { }
                }
                // Windows usually restarts the shell on its own; start it ourselves if it did not.
                System.Threading.Thread.Sleep(2000);
                if (Process.GetProcessesByName("explorer").Length == 0)
                    Process.Start("explorer.exe");
                statusLabel.Text = "檔案總管已重新啟動。";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "重新啟動檔案總管失敗：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OpenLog()
        {
            if (File.Exists(Log.FilePath))
                Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
            else
                statusLabel.Text = "還沒有記錄檔。";
        }
    }
}
