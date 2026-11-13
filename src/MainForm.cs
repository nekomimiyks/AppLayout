using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppLayout
{
    public class MainForm : Form
    {
        readonly ConfigStore store;
        readonly Keeper keeper;
        readonly bool startupMode;
        readonly string importedFrom;

        ListView list;
        CheckBox chkEnabled, chkAutoStart, chkLaunchMissing;
        Button btnAddList, btnPick, btnUpdate, btnEdit, btnDelete, btnApplyNow, btnExit;
        Label lblStatus, lblHint;
        Panel oldBanner; Label oldBannerText; string oldKitPath;
        NotifyIcon tray;
        ToolStripMenuItem trayToggle;
        System.Windows.Forms.Timer pickTimer;
        bool picking, pickPrevDown, reallyExit, balloonShown;
        Dictionary<AppEntry, EntryStatus> lastStatus = new Dictionary<AppEntry, EntryStatus>();
        EventWaitHandle showEvent;

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "AppLayout";

        public MainForm(ConfigStore store, bool startupMode, string importedFrom)
        {
            this.store = store; this.startupMode = startupMode; this.importedFrom = importedFrom;
            BuildUi();
            keeper = new Keeper(store, startupMode) { LogPath = Path.Combine(Path.GetDirectoryName(store.Path), "error.log") };
            keeper.StatusUpdated += OnStatus;
            RefreshList();
            SyncOptionControls();
            keeper.Start();

            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            var t = new Thread(delegate ()
            {
                while (true)
                {
                    showEvent.WaitOne();
                    try { if (IsHandleCreated) BeginInvoke(new Action(ShowFromTray)); } catch { }
                }
            }) { IsBackground = true };
            t.Start();
        }

        // ---------------- 画面構築 ----------------
        void BuildUi()
        {
            Text = "AppLayout - ウィンドウ配置キープ";
            Font = new Font("Yu Gothic UI", 9.5f);
            ClientSize = Ui.Sz(1080, 540);
            MinimumSize = Ui.Sz(760, 400);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = AppIcon.Create();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(10) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // 上段: オプション
            var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 6) };
            chkEnabled = new CheckBox { Text = "配置キープを有効にする", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 4, 24, 4) };
            chkEnabled.CheckedChanged += delegate { if (syncing) return; SetEnabled(chkEnabled.Checked); };
            chkAutoStart = new CheckBox { Text = "Windowsログイン時に自動起動", AutoSize = true, Margin = new Padding(0, 4, 24, 4) };
            chkAutoStart.CheckedChanged += delegate { if (syncing) return; SetAutoStart(chkAutoStart.Checked); };
            chkLaunchMissing = new CheckBox { Text = "自動起動時、記憶済みアプリが起動していなければ起動する", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
            chkLaunchMissing.CheckedChanged += delegate
            {
                if (syncing) return;
                lock (store.Sync) store.Config.LaunchMissingAtStartup = chkLaunchMissing.Checked;
                SaveConfig();
            };
            top.Controls.AddRange(new Control[] { chkEnabled, chkAutoStart, chkLaunchMissing });
            root.Controls.Add(top, 0, 0); root.SetColumnSpan(top, 2);

            // 旧版が動いているときのお知らせ
            oldBanner = new Panel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Color.FromArgb(255, 244, 206), Padding = new Padding(8), Margin = new Padding(0, 0, 0, 6), Visible = false };
            var bannerFlow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            oldBannerText = new Label { AutoSize = true, Margin = new Padding(0, 6, 12, 0), Text = "旧版（PowerShell版）の配置固定がまだ動いています。両方動いていると、ここで削除・変更した配置を旧版が元に戻してしまいます。" , MaximumSize = Ui.Sz(640, 0)};
            var bannerBtn = new Button { Text = "旧版を停止してこちらに切り替える", AutoSize = true };
            bannerBtn.Click += delegate { StopOldVersion(); };
            bannerFlow.Controls.Add(oldBannerText); bannerFlow.Controls.Add(bannerBtn);
            oldBanner.Controls.Add(bannerFlow);
            root.Controls.Add(oldBanner, 0, 1); root.SetColumnSpan(oldBanner, 2);

            // 一覧
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, GridLines = true };
            list.Columns.Add("名前", Ui.S(110));
            list.Columns.Add("アプリ（プロセス）", Ui.S(100));
            list.Columns.Add("位置 X, Y", Ui.S(95));
            list.Columns.Add("サイズ 幅×高さ", Ui.S(105));
            list.Columns.Add("モニタ", Ui.S(215));
            list.Columns.Add("最大化", Ui.S(50));
            list.Columns.Add("状態", Ui.S(160));
            list.DoubleClick += delegate { EditSelected(); };
            list.SelectedIndexChanged += delegate { UpdateButtons(); };
            list.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) DeleteSelected(); };
            root.Controls.Add(list, 0, 2);

            // 右側ボタン
            var side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
            btnAddList = MakeButton("起動中のウィンドウから追加…", delegate { AddFromList(); });
            btnPick = MakeButton("クリックしたウィンドウを追加", delegate { StartPick(); });
            btnUpdate = MakeButton("今の位置で記憶し直す", delegate { UpdateSelectedFromCurrent(); });
            btnEdit = MakeButton("編集…", delegate { EditSelected(); });
            btnDelete = MakeButton("記憶を解除（削除）", delegate { DeleteSelected(); });
            btnApplyNow = MakeButton("今すぐ配置を戻す", delegate { keeper.Kick(); });
            btnExit = MakeButton("終了（キープも止まる）", delegate { reallyExit = true; Close(); });
            side.Controls.AddRange(new Control[] { btnAddList, btnPick, Spacer(), btnUpdate, btnEdit, btnDelete, Spacer(), btnApplyNow, Spacer(), btnExit });
            root.Controls.Add(side, 1, 2);

            // 下段
            var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            lblStatus = new Label { AutoSize = true, ForeColor = Color.FromArgb(0, 90, 160), Font = new Font(Font, FontStyle.Bold) };
            lblHint = new Label
            {
                AutoSize = true, ForeColor = SystemColors.GrayText,
                Text = "配置を変えたいとき: 「配置キープを有効にする」を外す → ウィンドウを動かす → 一覧で選んで「今の位置で記憶し直す」 → キープを有効に戻す。\n×ボタンで閉じてもタスクトレイで動き続けます。"
            };
            bottom.Controls.Add(lblStatus); bottom.Controls.Add(lblHint);
            root.Controls.Add(bottom, 0, 3); root.SetColumnSpan(bottom, 2);

            // タスクトレイ
            var menu = new ContextMenuStrip();
            menu.Items.Add("画面を開く", null, delegate { ShowFromTray(); });
            trayToggle = new ToolStripMenuItem("配置キープを有効にする", null, delegate { SetEnabled(!trayToggle.Checked); });
            menu.Items.Add(trayToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("終了", null, delegate { reallyExit = true; Close(); });
            tray = new NotifyIcon { Icon = Icon, Text = "AppLayout - ウィンドウ配置キープ", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowFromTray(); };

            pickTimer = new System.Windows.Forms.Timer { Interval = 30 };
            pickTimer.Tick += PickTick;
        }

        Button MakeButton(string text, EventHandler h)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = Ui.Sz(200, 30), Margin = new Padding(0, 0, 0, 6) };
            b.Click += h;
            return b;
        }
        Control Spacer() { return new Label { Height = Ui.S(8), Width = Ui.S(10), Margin = new Padding(0) }; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (importedFrom != null)
                SetStatus("旧版の設定を取り込みました: " + importedFrom);
            CheckOldVersionAsync();
        }

        protected override void SetVisibleCore(bool value)
        {
            // ログイン時の自動起動ではウィンドウを出さずトレイに常駐
            if (startupMode && !IsHandleCreated) { CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!balloonShown)
                {
                    balloonShown = true;
                    tray.ShowBalloonTip(3000, "AppLayout", "タスクトレイで配置キープを続けています。終了するにはトレイアイコンを右クリック →「終了」。", ToolTipIcon.Info);
                }
                return;
            }
            keeper.Stop();
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        // ---------------- 一覧 ----------------
        void RefreshList()
        {
            var mons = WindowScanner.GetMonitors();
            var selected = new HashSet<AppEntry>();
            foreach (ListViewItem it in list.SelectedItems) selected.Add((AppEntry)it.Tag);
            list.BeginUpdate();
            list.Items.Clear();
            List<AppEntry> apps;
            lock (store.Sync) apps = new List<AppEntry>(store.Config.Apps);
            foreach (var a in apps)
            {
                var it = new ListViewItem(a.Name ?? "");
                it.SubItems.Add(a.Process ?? "");
                it.SubItems.Add(a.X + ", " + a.Y);
                it.SubItems.Add(a.Width + " × " + a.Height);
                it.SubItems.Add(WindowScanner.MonitorLabel(a.X, a.Y, a.Width, a.Height, mons));
                it.SubItems.Add(a.Maximized ? "はい" : "");
                EntryStatus st;
                it.SubItems.Add(lastStatus.TryGetValue(a, out st) ? st.Text : "確認中…");
                it.Tag = a;
                if (selected.Contains(a)) it.Selected = true;
                ApplyStatusColor(it, st);
                list.Items.Add(it);
            }
            list.EndUpdate();
            UpdateButtons();
        }

        void ApplyStatusColor(ListViewItem it, EntryStatus st)
        {
            it.UseItemStyleForSubItems = false;
            var c = SystemColors.WindowText;
            if (st != null) c = st.Kind == 0 ? Color.FromArgb(0, 120, 60) : st.Kind == 1 ? Color.FromArgb(190, 100, 0) : SystemColors.GrayText;
            it.SubItems[6].ForeColor = c;
        }

        void OnStatus(Dictionary<AppEntry, EntryStatus> st)
        {
            try { BeginInvoke(new Action(delegate { ApplyStatus(st); })); } catch { }
        }

        void ApplyStatus(Dictionary<AppEntry, EntryStatus> st)
        {
            lastStatus = st;
            foreach (ListViewItem it in list.Items)
            {
                EntryStatus s;
                if (!st.TryGetValue((AppEntry)it.Tag, out s)) continue;
                if (it.SubItems[6].Text != s.Text) it.SubItems[6].Text = s.Text;
                ApplyStatusColor(it, s);
            }
        }

        void UpdateButtons()
        {
            int n = list.SelectedItems.Count;
            btnUpdate.Enabled = n == 1; btnEdit.Enabled = n == 1; btnDelete.Enabled = n >= 1;
        }

        AppEntry SelectedEntry { get { return list.SelectedItems.Count == 1 ? (AppEntry)list.SelectedItems[0].Tag : null; } }

        void SetStatus(string s) { lblStatus.Text = s; }

        void SaveConfig()
        {
            try { store.Save(); }
            catch (Exception ex) { MessageBox.Show(this, "設定を保存できませんでした。\n" + ex.Message, "AppLayout", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            keeper.Kick();
        }

        // ---------------- オプション ----------------
        bool syncing;
        void SyncOptionControls()
        {
            syncing = true;
            bool en;
            lock (store.Sync) { en = store.Config.Enabled; chkLaunchMissing.Checked = store.Config.LaunchMissingAtStartup; }
            chkEnabled.Checked = en; trayToggle.Checked = en;
            chkAutoStart.Checked = IsAutoStart();
            tray.Text = en ? "AppLayout - 配置キープ中" : "AppLayout - 一時停止中";
            syncing = false;
        }

        void SetEnabled(bool en)
        {
            lock (store.Sync) store.Config.Enabled = en;
            SaveConfig();
            SyncOptionControls();
            SetStatus(en ? "配置キープを有効にしました。" : "配置キープを一時停止しました（ウィンドウを自由に動かせます）。");
        }

        string RunCommand { get { return "\"" + Application.ExecutablePath + "\" /startup"; } }

        bool IsAutoStart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                var v = k == null ? null : k.GetValue(RunName) as string;
                return v != null && string.Equals(v, RunCommand, StringComparison.OrdinalIgnoreCase);
            }
        }

        void SetAutoStart(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunName, RunCommand);
                    else if (k.GetValue(RunName) != null) k.DeleteValue(RunName);
                }
                SetStatus(on ? "ログイン時に自動起動するよう設定しました。" : "ログイン時の自動起動を解除しました。");
            }
            catch (Exception ex) { MessageBox.Show(this, "自動起動の設定に失敗しました。\n" + ex.Message, "AppLayout"); }
            SyncOptionControls();
        }

        // ---------------- 追加・更新・削除 ----------------
        void AddFromList()
        {
            using (var dlg = new WindowPickerDialog())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Selected == null) return;
                AddWindow(dlg.Selected);
            }
        }

        void AddWindow(WinInfo w)
        {
            w = WindowScanner.Describe(w.Handle, null) ?? w;
            if (w.Minimized)
            {
                MessageBox.Show(this, "このウィンドウは最小化されています。元のサイズに戻してから追加してください。", "AppLayout", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // 既に記憶済みなら位置の更新として扱う
            List<AppEntry> apps;
            lock (store.Sync) apps = new List<AppEntry>(store.Config.Apps);
            foreach (var a in apps)
            {
                if (!a.Matches(w)) continue;
                var r = MessageBox.Show(this, "このウィンドウは「" + a.Name + "」として記憶済みです。\n今の位置・サイズで記憶し直しますか？", "AppLayout", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes)
                {
                    lock (store.Sync) a.SetPosition(w);
                    SaveConfig(); RefreshList();
                    SetStatus("「" + a.Name + "」の配置を記憶し直しました。");
                }
                return;
            }

            var e = AppEntry.FromWindow(w);
            // 同じアプリのウィンドウが複数あるならタイトルで区別する
            int same = 0;
            foreach (var o in WindowScanner.ReadAll()) if (!WindowScanner.IsOwn(o) && e.Matches(o)) same++;
            if (same > 1) { e.TitleMode = "exact"; e.Title = w.Title; }

            using (var dlg = new EntryEditDialog(e, true))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                lock (store.Sync) store.Config.Apps.Add(e);
                SaveConfig(); RefreshList();
                SetStatus("「" + e.Name + "」の配置を記憶しました。");
            }
        }

        void UpdateSelectedFromCurrent()
        {
            var a = SelectedEntry; if (a == null) return;
            var m = new List<WinInfo>();
            foreach (var w in WindowScanner.ReadAll()) if (!WindowScanner.IsOwn(w) && a.Matches(w)) m.Add(w);
            if (m.Count == 0) { MessageBox.Show(this, "「" + a.Name + "」のウィンドウが見つかりません。アプリを起動してから実行してください。", "AppLayout"); return; }
            if (m.Count > 1) { MessageBox.Show(this, "「" + a.Name + "」に当てはまるウィンドウが" + m.Count + "個あります。「編集」でタイトル条件を指定してください。", "AppLayout"); return; }
            if (m[0].Minimized) { MessageBox.Show(this, "ウィンドウが最小化されています。元のサイズに戻してから実行してください。", "AppLayout"); return; }
            bool en; lock (store.Sync) en = store.Config.Enabled;
            if (en)
            {
                var r = MessageBox.Show(this, "配置キープが有効なため、ウィンドウは保存済みの位置に戻されている可能性があります。\nこのまま現在の位置で記憶し直しますか？\n\n（先にキープを一時停止してウィンドウを動かすのがおすすめです）", "AppLayout", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }
            lock (store.Sync) a.SetPosition(m[0]);
            SaveConfig(); RefreshList();
            SetStatus("「" + a.Name + "」の配置を記憶し直しました。");
        }

        void EditSelected()
        {
            var a = SelectedEntry; if (a == null) return;
            var copy = a.Clone();
            using (var dlg = new EntryEditDialog(copy, false))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                lock (store.Sync)
                {
                    int i = store.Config.Apps.IndexOf(a);
                    if (i >= 0) store.Config.Apps[i] = copy;
                }
                SaveConfig(); RefreshList();
                SetStatus("「" + copy.Name + "」を変更しました。");
            }
        }

        void DeleteSelected()
        {
            if (list.SelectedItems.Count == 0) return;
            var targets = new List<AppEntry>();
            var names = new List<string>();
            foreach (ListViewItem it in list.SelectedItems) { targets.Add((AppEntry)it.Tag); names.Add("・" + ((AppEntry)it.Tag).Name); }
            var r = MessageBox.Show(this, "次の記憶を解除します。よろしいですか？\n\n" + string.Join("\n", names.ToArray()), "記憶の解除", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;
            lock (store.Sync) foreach (var t in targets) store.Config.Apps.Remove(t);
            SaveConfig(); RefreshList();
            SetStatus(targets.Count + "件の記憶を解除しました。");
        }

        // ---------------- クリックで指定 ----------------
        void StartPick()
        {
            if (picking) { EndPick("キャンセルしました。"); return; }
            picking = true;
            pickPrevDown = Native.GetAsyncKeyState(1) < 0;
            btnPick.Text = "指定をキャンセル（Esc）";
            SetStatus("記憶したいウィンドウをクリックしてください。（Escキーでキャンセル）");
            pickTimer.Start();
        }

        void EndPick(string msg)
        {
            picking = false; pickTimer.Stop();
            btnPick.Text = "クリックしたウィンドウを追加";
            if (msg != null) SetStatus(msg);
        }

        void PickTick(object sender, EventArgs e)
        {
            if (Native.GetAsyncKeyState(0x1B) < 0) { EndPick("キャンセルしました。"); return; }
            bool down = Native.GetAsyncKeyState(1) < 0;
            bool pressed = down && !pickPrevDown;
            pickPrevDown = down;
            if (!pressed) return;
            Native.POINT p; Native.GetCursorPos(out p);
            IntPtr h = Native.WindowFromPoint(p);
            if (h == IntPtr.Zero) return;
            IntPtr root = Native.GetAncestor(h, 2);
            if (root != IntPtr.Zero) h = root;
            var w = WindowScanner.Describe(h, null);
            if (w == null || WindowScanner.IsOwn(w)) return; // 自分の画面のクリックは無視
            if (w.Class == "Progman" || w.Class == "WorkerW" || w.Class == "Shell_TrayWnd")
            {
                SetStatus("デスクトップやタスクバーは記憶できません。アプリのウィンドウをクリックしてください。");
                return;
            }
            EndPick(null);
            SetStatus("選んだウィンドウ: " + (w.Title.Length > 0 ? w.Title : w.ProcessName));
            // クリックが終わってから前面に出す
            var t = new System.Windows.Forms.Timer { Interval = 250 };
            t.Tick += delegate
            {
                t.Stop(); t.Dispose();
                ShowFromTray();
                if (w.Title.Length == 0)
                {
                    MessageBox.Show(this, "タイトルのないウィンドウは記憶できません。", "AppLayout");
                    return;
                }
                AddWindow(w);
            };
            t.Start();
        }

        // ---------------- 旧版の検出と停止 ----------------
        void CheckOldVersionAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string kit = null;
                try
                {
                    using (var s = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name='powershell.exe' OR Name='pwsh.exe'"))
                    foreach (ManagementObject o in s.Get())
                    {
                        var cl = o["CommandLine"] as string;
                        if (cl == null) continue;
                        var m = Regex.Match(cl, "-File\\s+\"?([^\"]*?)\\\\scripts\\\\Keep-AppLayout\\.ps1", RegexOptions.IgnoreCase);
                        if (m.Success) { kit = m.Groups[1].Value; break; }
                    }
                }
                catch { }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        oldKitPath = kit;
                        oldBanner.Visible = kit != null;
                        if (kit != null) oldBannerText.Text = "旧版（PowerShell版、" + kit + "）の配置固定がまだ動いています。両方動いていると、ここで削除・変更した配置を旧版が元に戻してしまいます。";
                    }));
                }
                catch { }
            });
        }

        void StopOldVersion()
        {
            if (oldKitPath == null) return;
            var r = MessageBox.Show(this,
                "旧版の配置固定を停止し、次回ログインからはこのアプリが自動起動するようにします。\n\n" +
                "・旧版は「一時停止」の状態になるだけで、ファイルは消しません\n" +
                "  （戻したいときは " + oldKitPath + "\\Resume-AppLayout.cmd）\n" +
                "・このアプリの「Windowsログイン時に自動起動」をオンにします\n\nよろしいですか？",
                "旧版からの切り替え", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;
            try
            {
                string marker = Path.Combine(oldKitPath, @"state\fixed-app-layout.stop");
                File.WriteAllText(marker, DateTime.Now.ToString("o"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "旧版を停止できませんでした。\n" + ex.Message, "AppLayout", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            SetAutoStart(true);
            SetStatus("旧版を停止しました（数秒で止まります）。今後はこのアプリが配置をキープします。");
            oldBanner.Visible = false;
            var t = new System.Windows.Forms.Timer { Interval = 5000 };
            t.Tick += delegate { t.Stop(); t.Dispose(); CheckOldVersionAsync(); };
            t.Start();
        }
    }
}
