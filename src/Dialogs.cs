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
            Text = "起動中のウィンドウから追加";
            Font = new Font("Yu Gothic UI", 9.5f);
            ClientSize = Ui.Sz(860, 460); MinimumSize = Ui.Sz(600, 300);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            root.Controls.Add(new Label { AutoSize = true, Text = "記憶したいウィンドウを選んで「追加」を押してください（ダブルクリックでも追加できます）。", Margin = new Padding(0, 0, 0, 6) }, 0, 0);

            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true };
            list.Columns.Add("ウィンドウのタイトル", Ui.S(330));
            list.Columns.Add("アプリ（プロセス）", Ui.S(130));
            list.Columns.Add("位置 X, Y", Ui.S(100));
            list.Columns.Add("サイズ", Ui.S(100));
            list.Columns.Add("モニタ", Ui.S(170));
            list.DoubleClick += delegate { Accept(); };
            root.Controls.Add(list, 0, 1);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };
            var cancel = new Button { Text = "キャンセル", AutoSize = true, MinimumSize = Ui.Sz(100, 32), DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "追加", AutoSize = true, MinimumSize = Ui.Sz(100, 32) };
            ok.Click += delegate { Accept(); };
            var refresh = new Button { Text = "一覧を更新", AutoSize = true, MinimumSize = Ui.Sz(100, 32) };
            refresh.Click += delegate { Reload(); };
            buttons.Controls.AddRange(new Control[] { cancel, ok, refresh });
            root.Controls.Add(buttons, 0, 2);
            AcceptButton = ok; CancelButton = cancel;
            Reload();
        }

        void Reload()
        {
            var mons = WindowScanner.GetMonitors();
            list.BeginUpdate(); list.Items.Clear();
            foreach (var w in WindowScanner.ReadPickable())
            {
                var it = new ListViewItem(w.Title);
                it.SubItems.Add(w.ProcessName);
                if (w.Minimized) { it.SubItems.Add("（最小化中）"); it.SubItems.Add(""); it.SubItems.Add(""); it.ForeColor = SystemColors.GrayText; }
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
            if (list.SelectedItems.Count != 1) { MessageBox.Show(this, "ウィンドウを1つ選んでください。", "AppLayout"); return; }
            Selected = (WinInfo)list.SelectedItems[0].Tag;
            DialogResult = DialogResult.OK;
        }
    }

    /// 記憶内容の確認・編集
    public class EntryEditDialog : Form
    {
        readonly AppEntry entry;
        TextBox txtName, txtTitle;
        ComboBox cmbMode;
        NumericUpDown numX, numY, numW, numH;
        CheckBox chkMax;
        Label lblMatch;
        static readonly string[] ModeKeys = { "none", "exact", "contains", "regex" };
        static readonly string[] ModeLabels = { "指定しない（アプリだけで判定）", "タイトルが完全に一致", "タイトルに含む", "正規表現（例: Discord$ ＝末尾がDiscord）" };

        public EntryEditDialog(AppEntry e, bool isNew)
        {
            entry = e;
            Text = isNew ? "このウィンドウの配置を記憶" : "記憶内容の編集";
            Font = new Font("Yu Gothic UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; TopMost = true;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var g = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(12), Dock = DockStyle.Fill };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(g);

            txtName = new TextBox { Text = e.Name, Width = Ui.S(380) };
            AddRow(g, "名前", txtName);
            string target = e.Process + "（" + e.Class + "）" + (string.IsNullOrEmpty(e.AppId) ? "" : "\nアプリID: " + e.AppId);
            AddRow(g, "対象アプリ", new Label { Text = target, AutoSize = true, MaximumSize = Ui.Sz(420, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 6) });

            cmbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.S(380) };
            cmbMode.Items.AddRange(ModeLabels);
            cmbMode.SelectedIndex = Math.Max(0, Array.IndexOf(ModeKeys, e.TitleModeOrNone));
            AddRow(g, "ウィンドウの見分け方", cmbMode);
            txtTitle = new TextBox { Text = e.Title ?? "", Width = Ui.S(380) };
            AddRow(g, "タイトル条件", txtTitle);
            lblMatch = new Label { AutoSize = true, Margin = new Padding(3, 2, 3, 8) };
            AddRow(g, "", lblMatch);

            var pos = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            numX = Num(e.X); numY = Num(e.Y);
            pos.Controls.AddRange(new Control[] { SmallLabel("X"), numX, SmallLabel("Y"), numY });
            AddRow(g, "位置", pos);
            var size = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            numW = Num(e.Width); numH = Num(e.Height); numW.Minimum = 1; numH.Minimum = 1;
            size.Controls.AddRange(new Control[] { SmallLabel("幅"), numW, SmallLabel("高さ"), numH });
            AddRow(g, "サイズ", size);
            chkMax = new CheckBox { Text = "最大化した状態で保つ", Checked = e.Maximized, AutoSize = true };
            AddRow(g, "", chkMax);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            var cancel = new Button { Text = "キャンセル", AutoSize = true, MinimumSize = Ui.Sz(100, 32), DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = isNew ? "記憶する" : "保存", AutoSize = true, MinimumSize = Ui.Sz(100, 32) };
            ok.Click += delegate { Accept(); };
            buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
            g.Controls.Add(buttons); g.SetColumnSpan(buttons, 2);
            AcceptButton = ok; CancelButton = cancel;

            cmbMode.SelectedIndexChanged += delegate { UpdateMatch(); };
            txtTitle.TextChanged += delegate { UpdateMatch(); };
            UpdateMatch();
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); txtName.Focus(); }

        static void AddRow(TableLayoutPanel g, string label, Control c)
        {
            g.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 10, 3) });
            g.Controls.Add(c);
        }
        static Label SmallLabel(string t) { return new Label { Text = t, AutoSize = true, Margin = new Padding(3, 7, 3, 3) }; }
        static NumericUpDown Num(int v)
        {
            var n = new NumericUpDown { Minimum = -100000, Maximum = 100000, Width = Ui.S(100) };
            n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v));
            return n;
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
            txtTitle.Enabled = cmbMode.SelectedIndex > 0;
            var p = Preview();
            if (p.TitleMode == "regex")
            {
                try { new System.Text.RegularExpressions.Regex(p.Title); }
                catch { lblMatch.Text = "正規表現の書き方が正しくありません"; lblMatch.ForeColor = Color.Firebrick; return; }
            }
            int n = 0;
            foreach (var w in WindowScanner.ReadAll()) if (!WindowScanner.IsOwn(w) && p.Matches(w)) n++;
            if (n == 1) { lblMatch.Text = "いま当てはまるウィンドウ: 1個（OK）"; lblMatch.ForeColor = Color.FromArgb(0, 120, 60); }
            else if (n == 0) { lblMatch.Text = "いま当てはまるウィンドウ: なし（起動していないか、条件が厳しすぎます）"; lblMatch.ForeColor = Color.FromArgb(190, 100, 0); }
            else { lblMatch.Text = "いま当てはまるウィンドウ: " + n + "個（1個に絞らないと配置を戻しません）"; lblMatch.ForeColor = Color.Firebrick; }
        }

        void Accept()
        {
            if (txtName.Text.Trim().Length == 0) { MessageBox.Show(this, "名前を入力してください。", "AppLayout"); return; }
            var p = Preview();
            if (p.TitleMode != "none" && p.Title.Length == 0) { MessageBox.Show(this, "タイトル条件を入力するか、「指定しない」を選んでください。", "AppLayout"); return; }
            if (p.TitleMode == "regex")
            {
                try { new System.Text.RegularExpressions.Regex(p.Title); }
                catch { MessageBox.Show(this, "正規表現の書き方が正しくありません。", "AppLayout"); return; }
            }
            entry.Name = txtName.Text.Trim();
            entry.TitleMode = p.TitleMode; entry.Title = p.TitleMode == "none" ? "" : p.Title;
            entry.X = (int)numX.Value; entry.Y = (int)numY.Value; entry.Width = (int)numW.Value; entry.Height = (int)numH.Value;
            entry.Maximized = chkMax.Checked;
            DialogResult = DialogResult.OK;
        }
    }

    /// アプリアイコン（4分割のウィンドウ配置を表す図）
    public static class AppIcon
    {
        public static Icon Create()
        {
            using (var bmp = Draw(32)) return Icon.FromHandle(bmp.GetHicon());
        }

        public static Bitmap Draw(int s)
        {
            var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float m = s * 0.06f, gap = s * 0.06f, half = (s - 2 * m - gap) / 2;
                var colors = new[] { Color.FromArgb(37, 99, 235), Color.FromArgb(16, 185, 129), Color.FromArgb(245, 158, 11), Color.FromArgb(236, 72, 153) };
                for (int i = 0; i < 4; i++)
                {
                    float x = m + (i % 2) * (half + gap), y = m + (i / 2) * (half + gap);
                    using (var b = new SolidBrush(colors[i])) g.FillRectangle(b, x, y, half, half);
                    using (var b = new SolidBrush(Color.FromArgb(90, 255, 255, 255))) g.FillRectangle(b, x, y, half, half * 0.22f);
                }
            }
            return bmp;
        }
    }
}
