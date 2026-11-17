using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppLayout
{
    /// 起動中のウィンドウ一覧から1つ選ぶ
    public class WindowPickerDialog : Form
    {
        ListView list;
        public WinInfo Selected;

        public WindowPickerDialog()
        {
            Text = L.T("起動中のウィンドウから追加");
            Font = UiTypography.BodyFont();
            ClientSize = Ui.Sz(860, 460); MinimumSize = Ui.Sz(600, 300);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            root.Controls.Add(new Label { AutoSize = true, Text = L.T("記憶したいウィンドウを選んで「追加」を押してください（ダブルクリックでも追加できます）。"), Margin = new Padding(0, 0, 0, 6) }, 0, 0);

            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true };
            list.Columns.Add(L.T("ウィンドウのタイトル"), Ui.S(330));
            list.Columns.Add(L.T("アプリ（プロセス）"), Ui.S(130));
            list.Columns.Add(L.T("位置 X, Y"), Ui.S(100));
            list.Columns.Add(L.T("サイズ"), Ui.S(100));
            list.Columns.Add(L.T("モニタ"), Ui.S(170));
            list.DoubleClick += delegate { Accept(); };
            root.Controls.Add(list, 0, 1);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };
            var cancel = new Button { Text = L.T("キャンセル"), AutoSize = true, MinimumSize = Ui.Sz(100, 32), DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = L.T("追加"), AutoSize = true, MinimumSize = Ui.Sz(100, 32) };
            ok.Click += delegate { Accept(); };
            var refresh = new Button { Text = L.T("一覧を更新"), AutoSize = true, MinimumSize = Ui.Sz(100, 32) };
            refresh.Click += delegate { Reload(); };
            buttons.Controls.AddRange(new Control[] { cancel, ok, refresh });
            root.Controls.Add(buttons, 0, 2);
            AcceptButton = ok; CancelButton = cancel;
            Reload();
            L.Attach(this,Reload);
        }

        void Reload()
        {
            var mons = WindowScanner.GetMonitors();
            list.BeginUpdate(); list.Items.Clear();
            foreach (var w in WindowScanner.ReadPickable())
            {
                var it = new ListViewItem(w.Title);
                it.SubItems.Add(w.ProcessName);
                if (w.Minimized) { it.SubItems.Add(L.T("（最小化中）")); it.SubItems.Add(""); it.SubItems.Add(""); it.ForeColor = SystemColors.GrayText; }
                else
                {
                    it.SubItems.Add(w.Bounds.Left + ", " + w.Bounds.Top);
                    it.SubItems.Add(w.Width + " × " + w.Height);
                    it.SubItems.Add(WindowScanner.MonitorLabel(w.Bounds.Left, w.Bounds.Top, w.Width, w.Height, mons));
                }
                it.Tag = w;
                list.Items.Add(it);
            }
            list.EndUpdate();
        }

        void Accept()
        {
            if (list.SelectedItems.Count != 1) { MessageBox.Show(this, L.T("ウィンドウを1つ選んでください。"), "AppLayout"); return; }
            Selected = (WinInfo)list.SelectedItems[0].Tag;
            DialogResult = DialogResult.OK;
        }
    }

    /// 登録した配置とウィンドウの判定条件を編集する。
    public class EntryEditDialog : DpiAwareForm
    {
        readonly AppEntry entry;
        TextBox txtName, txtTitle;
        ThemedSelect cmbMode;
        NumericUpDown numX, numY, numW, numH;
        CheckBox chkMax;
        Label lblMatch, lblMonitor;
        readonly System.Windows.Forms.Timer matchTimer;
        readonly TableLayoutPanel content;
        readonly ThemedScrollPanel scroll;
        readonly TableLayoutPanel header;
        bool wrapping;
        readonly Label titleHint;
        readonly Control titleField;
        readonly TextBox details;
        readonly Button detailsButton;
        static readonly string[] ModeKeys = { "none", "exact", "contains", "regex" };
        static readonly string[] ModeLabels = { "アプリで判定", "タイトルが完全に一致", "タイトルに含む", "正規表現" };

        public EntryEditDialog(AppEntry e, bool isNew)
        {
            entry = e;
            AppTheme.Apply(this);
            Text = isNew ? L.T("配置を登録") : L.T("配置を編集");
            L.BindText(this,isNew?"配置を登録":"配置を編集");
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = Ui.Sz(590, 600); MinimumSize = Ui.Sz(390, 330);
            Icon = AppIcon.Create();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            header = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, BackColor = AppTheme.Surface, Padding = new Padding(Ui.S(24), Ui.S(16), Ui.S(24), Ui.S(10)), Margin = Padding.Empty };
            header.Controls.Add(new Label { Text = Text, AutoSize = true, Font = UiTypography.BodyFont(1.25f, FontStyle.Bold), Margin = Padding.Empty });
            L.BindText(header.Controls[0],isNew?"配置を登録":"配置を編集");
            header.Controls.Add(new Label { Text = L.T("対象アプリ  /  ") + (e.Process ?? ""), ForeColor = AppTheme.Muted, AutoSize = true, Margin = new Padding(0, Ui.S(4), 0, 0) });
            root.Controls.Add(header, 0, 0);

            scroll = new ThemedScrollPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = AppTheme.Surface };
            content = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Top, Padding = new Padding(Ui.S(24), Ui.S(2), Ui.S(24), Ui.S(14)), Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scroll.Content.Controls.Add(content); root.Controls.Add(scroll, 0, 1);

            txtName = new TextBox { Name = "EntryName", Text = e.Name ?? "", MaxLength = 200, Dock = DockStyle.Fill };
            AddBlock(Field(L.T("名前"), txtName));

            AddSection(L.T("配置"));
            var geometry = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            geometry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); geometry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            geometry.RowStyles.Add(new RowStyle(SizeType.AutoSize)); geometry.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            numX = Num(e.X); numY = Num(e.Y);
            numX.Name = "PositionX"; numY.Name = "PositionY";
            numW = Num(Math.Max(1, e.Width)); numH = Num(Math.Max(1, e.Height)); numW.Minimum = 1; numH.Minimum = 1;
            numW.Name = "WindowWidth"; numH.Name = "WindowHeight";
            var fields = new Control[] { Field(L.T("X 座標（px）"), numX), Field(L.T("Y 座標（px）"), numY), Field(L.T("幅（px）"), numW), Field(L.T("高さ（px）"), numH) };
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i].Margin = new Padding(i % 2 == 0 ? 0 : Ui.S(8), 0, i % 2 == 0 ? Ui.S(8) : 0, Ui.S(7));
                geometry.Controls.Add(fields[i], i % 2, i / 2);
            }
            AddBlock(geometry);
            lblMonitor = new Label { AutoSize = true, ForeColor = AppTheme.Muted, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Ui.S(7)) };
            AddBlock(lblMonitor);
            chkMax = new ToggleSwitch { Name = "Maximized", Text = L.T("最大化して配置"), Checked = e.Maximized, AutoSize = true, Margin = new Padding(0, 0, 0, Ui.S(4)) };
            AddBlock(chkMax);

            AddSection(L.T("ウィンドウの見分け方"));
            cmbMode = new ThemedSelect { Name = "MatchMode", Dock = DockStyle.Fill, AccessibleName = L.T("ウィンドウの見分け方") };
            cmbMode.SetItems(Array.ConvertAll(ModeLabels, L.T));
            cmbMode.SelectedIndex = Math.Max(0, Array.IndexOf(ModeKeys, e.TitleModeOrNone));
            cmbMode.Margin = new Padding(0, 0, 0, Ui.S(7)); AddBlock(cmbMode);
            txtTitle = new TextBox { Name = "MatchTitle", Text = e.Title ?? "", MaxLength = 2000, Dock = DockStyle.Fill };
            titleField = Field(L.T("タイトル条件"), txtTitle); AddBlock(titleField);
            titleHint = new Label { AutoSize = true, ForeColor = AppTheme.Muted, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Ui.S(6)) };
            AddBlock(titleHint);
            lblMatch = new Label { Name = "MatchStatus", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Ui.S(2)) };
            AddBlock(lblMatch);

            detailsButton = AppTheme.Button(L.T("識別情報を表示"), false);
            detailsButton.FlatAppearance.BorderSize = 0; detailsButton.ForeColor = AppTheme.Muted;
            detailsButton.Margin = new Padding(0, Ui.S(6), 0, 0); detailsButton.MinimumSize = Size.Empty;
            detailsButton.Padding = new Padding(0, Ui.S(4), Ui.S(8), Ui.S(4));
            detailsButton.Anchor = AnchorStyles.Left;
            AddBlock(detailsButton);
            details = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.None, Height = Ui.S(76), Dock = DockStyle.Fill, Visible = false,
                Text = L.T("プロセス: ") + e.Process + L.T("\r\nクラス: ") + e.Class + L.T("\r\nアプリID: ") + (string.IsNullOrEmpty(e.AppId) ? "なし" : e.AppId), Margin = new Padding(0, Ui.S(4), 0, 0) };
            AppTheme.StyleInput(details); AddBlock(details);
            detailsButton.Click += delegate { details.Visible = !details.Visible; if(details.Visible)ResizeDetails();detailsButton.Text = details.Visible ? L.T("識別情報を閉じる") : L.T("識別情報を表示"); };
            details.SizeChanged+=delegate {if(details.Visible)ResizeDetails();};

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, BackColor = AppTheme.Background, Padding = new Padding(Ui.S(16), Ui.S(12), Ui.S(24), Ui.S(12)), Margin = Padding.Empty, WrapContents = true };
            var cancel = AppTheme.Button(L.T("キャンセル"), false); cancel.DialogResult = DialogResult.Cancel;
            var ok = AppTheme.Button(L.T("登録を確定"), true); ok.Name = "ConfirmEntry";
            L.BindText(ok,"登録を確定");
            ok.Click += delegate { Accept(); };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 2);
            AcceptButton = ok; CancelButton = cancel;

            matchTimer = new System.Windows.Forms.Timer { Interval = 300 };
            matchTimer.Tick += delegate { matchTimer.Stop(); UpdateMatch(); };
            cmbMode.SelectedIndexChanged += delegate { QueueMatch(); };
            txtTitle.TextChanged += delegate { QueueMatch(); };
            foreach (var n in new NumericUpDown[] { numX, numY, numW, numH }) n.ValueChanged += delegate { UpdateMonitor(); };
            content.SizeChanged += delegate { UpdateWrapWidths(); };
            header.SizeChanged += delegate { UpdateWrapWidths(); };
            QueueMatch(); UpdateMonitor();
            UpdateInputSizes();
            L.Attach(this,delegate {
                cmbMode.SetItems(Array.ConvertAll(ModeLabels, L.T));
                ((Label)header.Controls[1]).Text=L.F("対象アプリ  /  {0}",entry.Process??"");
                detailsButton.Text=L.T(details.Visible?"識別情報を閉じる":"識別情報を表示");
                details.Text=L.T("プロセス: ")+entry.Process+L.T("\r\nクラス: ")+entry.Class+L.T("\r\nアプリID: ")+(string.IsNullOrEmpty(entry.AppId)?L.T("なし"):entry.AppId);
                if (details.Visible) ResizeDetails();
                UpdateMonitor();QueueMatch();UpdateWrapWidths();
            });
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle area = Screen.FromControl(Owner ?? this).WorkingArea;
            int width = Math.Min(Width, Math.Max(280, area.Width - Ui.S(24)));
            int height = Math.Min(Height, Math.Max(260, area.Height - Ui.S(24)));
            MinimumSize = new Size(Math.Min(MinimumSize.Width, width), Math.Min(MinimumSize.Height, height));
            Size = new Size(width, height);
            if (Left < area.Left || Right > area.Right) Left = area.Left + (area.Width - Width) / 2;
            if (Top < area.Top || Bottom > area.Bottom) Top = area.Top + (area.Height - Height) / 2;
            UpdateWrapWidths();
            txtName.Focus(); txtName.SelectAll();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && matchTimer != null) matchTimer.Dispose();
            base.Dispose(disposing);
        }

        void AddSection(string text)
        {
            var heading = new Label { Text = text, ForeColor = AppTheme.Accent, Font = new Font(Font, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, Ui.S(10), 0, Ui.S(7)) };
            AddBlock(heading);
        }
        void AddBlock(Control c)
        {
            int row = content.RowCount++;
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(c, 0, row);
        }
        static TableLayoutPanel Field(string label, Control input)
        {
            var p = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, Ui.S(7)) };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.RowStyles.Add(new RowStyle(SizeType.AutoSize)); p.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            p.Controls.Add(new Label { Text = label, ForeColor = AppTheme.Muted, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Ui.S(3)) }, 0, 0);
            input.AccessibleName = label;
            p.Controls.Add(new InputFrame(input) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
            return p;
        }
        protected override void OnUiScaleChanged(float factor)
        {
            base.OnUiScaleChanged(factor);
            UpdateInputSizes();
            if (lblMatch != null && titleHint != null && lblMonitor != null) UpdateWrapWidths();
        }
        protected override void OnTextMetricsChanged()
        {
            base.OnTextMetricsChanged();
            UpdateInputSizes();
            if (lblMatch != null && titleHint != null && lblMonitor != null) UpdateWrapWidths();
        }
        static NumericUpDown Num(int v)
        {
            var n = new NumericUpDown { Minimum = -100000, Maximum = 100000, Dock = DockStyle.Fill, ThousandsSeparator = false, Margin = new Padding(0, Ui.S(3), 0, Ui.S(6)) };
            n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v));
            AppTheme.StyleInput(n);
            return n;
        }

        void UpdateInputSizes()
        {
            if (txtTitle == null || txtTitle.Parent == null) return;
            foreach (var input in new Control[] { txtName, numX, numY, numW, numH, txtTitle })
                ((InputFrame)input.Parent).UpdateHeight();
            cmbMode.Height = cmbMode.GetPreferredSize(Size.Empty).Height;
        }

        void UpdateWrapWidths()
        {
            if(wrapping || lblMatch==null || titleHint==null || lblMonitor==null)return;
            wrapping=true;
            try {ConstrainLabels(header);ConstrainLabels(content);if(details!=null&&details.Visible)ResizeDetails();}
            finally {wrapping=false;}
        }
        static void ConstrainLabels(Control parent)
        {
            foreach(Control child in parent.Controls)
            {
                var label=child as Label;
                if(label!=null)label.MaximumSize=new Size(Math.Max(40,parent.ClientSize.Width-parent.Padding.Horizontal-label.Margin.Horizontal),0);
                else if(child.HasChildren)ConstrainLabels(child);
            }
        }
        void ResizeDetails()
        {
            int height=Math.Max(Ui.S(76),TextRenderer.MeasureText(details.Text,details.Font,new Size(Math.Max(40,details.ClientSize.Width-8),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height+12);
            if(details.Height!=height)details.Height=height;
        }

        void QueueMatch()
        {
            txtTitle.Enabled = cmbMode.SelectedIndex > 0;
            titleField.Visible = cmbMode.SelectedIndex > 0;
            titleHint.Text = cmbMode.SelectedIndex == 0 ? L.T("同じアプリのウィンドウが複数ある場合は、タイトル条件で絞り込みます。")
                : cmbMode.SelectedIndex == 3 ? L.T("例: Chat$ は、タイトルの末尾が Chat のウィンドウです。")
                : cmbMode.SelectedIndex == 2 ? L.T("タイトルに含まれる文字を指定します。大文字・小文字は区別しません。") : L.T("タイトル全体を指定します。大文字・小文字も区別します。");
            lblMatch.Text = L.T("条件を確認しています…"); lblMatch.ForeColor = AppTheme.Muted;
            matchTimer.Stop(); matchTimer.Start();
        }

        void UpdateMonitor()
        {
            string monitor = WindowScanner.MonitorLabel((int)numX.Value, (int)numY.Value, (int)numW.Value, (int)numH.Value, WindowScanner.GetMonitors());
            lblMonitor.Text = monitor == "なし" || monitor == "?" ? L.T("配置先: 該当するモニタがありません") : L.F("配置先: モニタ {0}",monitor);
        }

        AppEntry Preview()
        {
            var p = entry.Clone();
            p.TitleMode = ModeKeys[Math.Max(0, cmbMode.SelectedIndex)];
            p.Title = txtTitle.Text;
            return p;
        }

        void UpdateMatch()
        {
            var p = Preview();
            System.Text.RegularExpressions.Regex pattern = null;
            if (p.TitleMode == "regex")
            {
                try { pattern = new System.Text.RegularExpressions.Regex(p.Title, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
                catch (ArgumentException) { lblMatch.Text = L.T("正規表現の書き方が正しくありません"); lblMatch.ForeColor = AppTheme.Warning; return; }
            }
            int n = 0;
            try
            {
                // 正規表現は1件につき100ms、プレビュー全体も約300msで打ち切る。
                var watch = System.Diagnostics.Stopwatch.StartNew();
                foreach (var w in WindowScanner.ReadAll())
                {
                    if (WindowScanner.IsOwn(w)) continue;
                    if (pattern == null) { if (p.Matches(w)) n++; }
                    else
                    {
                        string mode = p.TitleMode; p.TitleMode = "none";
                        bool sameApp = p.Matches(w); p.TitleMode = mode;
                        if (sameApp && pattern.IsMatch(w.Title ?? "")) n++;
                    }
                    if (watch.ElapsedMilliseconds > 300) { lblMatch.Text = L.T("条件の確認に時間がかかっています。条件を短くしてください。"); lblMatch.ForeColor = AppTheme.Warning; return; }
                }
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { lblMatch.Text = L.T("正規表現の確認を中断しました。条件を簡単にしてください。"); lblMatch.ForeColor = AppTheme.Warning; return; }
            if (n == 1) { lblMatch.Text = L.T("1件に一致。この条件で配置できます。"); lblMatch.ForeColor = AppTheme.Accent; }
            else if (n == 0) { lblMatch.Text = L.T("一致するウィンドウはありません。未起動でも登録できます。"); lblMatch.ForeColor = AppTheme.Muted; }
            else { lblMatch.Text = L.F("{0}件に一致。1件に絞り込むまで配置は実行しません。",n); lblMatch.ForeColor = AppTheme.Warning; }
        }

        void Accept()
        {
            if (txtName.Text.Trim().Length == 0) { MessageBox.Show(this, L.T("名前を入力してください。"), "AppLayout"); return; }
            var p = Preview();
            if (p.TitleMode != "none" && p.Title.Length == 0) { MessageBox.Show(this, L.T("タイトル条件を入力するか、「アプリで判定」を選んでください。"), "AppLayout"); return; }
            if (p.TitleMode == "regex")
            {
                try { new System.Text.RegularExpressions.Regex(p.Title, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
                catch (ArgumentException) { MessageBox.Show(this, L.T("正規表現の書き方が正しくありません。"), "AppLayout", MessageBoxButtons.OK, MessageBoxIcon.Warning); txtTitle.Focus(); return; }
            }
            entry.Name = txtName.Text.Trim();
            entry.TitleMode = p.TitleMode; entry.Title = p.TitleMode == "none" ? "" : p.Title;
            entry.X = (int)numX.Value; entry.Y = (int)numY.Value; entry.Width = (int)numW.Value; entry.Height = (int)numH.Value;
            entry.Maximized = chkMax.Checked;
            DialogResult = DialogResult.OK;
        }
    }

    /// EXEに埋め込んだ採用アイコンをウィンドウとトレイで共用する。
    public static class AppIcon
    {
        public static Icon Create()
        {
            using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("AppLayout.AppIcon.ico"))
            {
                if (stream != null) using (var icon = new Icon(stream, new Size(32, 32))) return (Icon)icon.Clone();
            }
            using (var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)) return (Icon)icon.Clone();
        }

        public static Bitmap Draw(int s)
        {
            using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("AppLayout.AppIcon.ico"))
            {
                if (stream != null) using (var icon = new Icon(stream, new Size(s, s))) return icon.ToBitmap();
            }
            using (var icon = Create()) using (var bitmap = icon.ToBitmap()) return new Bitmap(bitmap, s, s);
        }
    }
}
