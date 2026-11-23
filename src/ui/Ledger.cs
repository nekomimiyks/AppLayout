using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppLayout
{
    public static class UiFactory
    {
        public static Form CreateMainForm(AppController controller) { return new LedgerForm(controller); }
    }

    internal sealed class LedgerGrid : DataGridView
    {
        public LedgerGrid() { DoubleBuffered = true; }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var host = Parent as GridScrollHost;
            if (host != null) host.ScrollWheel(e); else base.OnMouseWheel(e);
        }
    }

    internal sealed class LedgerForm : DpiAwareForm
    {
        [DllImport("user32.dll")] static extern bool GetPhysicalCursorPos(out Point point);
        const int Target = 0, NameColumn = 1, Geometry = 2, Rule = 3, Max = 4, State = 5, EditColumn = 6;
        readonly AppController controller;
        readonly LedgerGrid grid = new LedgerGrid();
        readonly GridScrollHost gridHost;
        readonly Dictionary<string, long> arrival = new Dictionary<string, long>();
        readonly Dictionary<string, DataGridViewRow> views = new Dictionary<string, DataGridViewRow>();
        readonly ToolTip tips = new ToolTip();
        readonly Icon windowIcon;
        Font heading, bold, secondary;
        int rowHeight, bandHeight, bodyLineHeight, secondaryLineHeight;
        readonly TableLayoutPanel command, options;
        readonly FlowLayoutPanel title, commands, automatic;
        readonly Button settings;
        bool arranging;
        readonly Label savedCount, autoStatus, message;
        readonly Button execute, clear;
        readonly FlowLayoutPanel preferences;
        readonly CheckBox enabled, login, launch, tray;
        long nextOrder;
        int savedTotal;
        bool busy, readingSettings;
        DataGridViewRow hoverEdit;
        bool initialSizeApplied;
        readonly ThemedSelect language;
        bool readingLanguage;

        public LedgerForm(AppController controller)
        {
            this.controller = controller;
            AppTheme.Apply(this);
            windowIcon = AppIcon.Create();
            Icon = windowIcon;
            Text = "AppLayout";
            StartPosition = FormStartPosition.Manual;
            var work = Screen.FromPoint(StartupPoint()).WorkingArea;
            Size initial = FitStartupSize(Ui.Sz(860, 440), Size - ClientSize, work, Ui.S(14));
            MinimumSize = new Size(Math.Min(initial.Width, Ui.S(610)), Math.Min(initial.Height, Ui.S(390)));
            Bounds = Centered(initial, work);
            DoubleBuffered = true;
            CreateFonts();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
                Margin = Padding.Empty, Padding = Padding.Empty, BackColor = AppTheme.Surface, AutoScroll = false };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++)
                root.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
            var page = new ThemedScrollPanel { Dock = DockStyle.Fill };
            page.Content.Controls.Add(root);
            Controls.Add(page);

            command = Strip(AppTheme.Surface, 13, 3);
            title = Flow();
            title.Controls.Add(Caption(L.T("登録済みの配置"), heading, AppTheme.Foreground));
            savedCount = Caption("", bold, AppTheme.Accent);
            title.Controls.Add(savedCount);
            commands = Flow();
            execute = AppTheme.Button(Texts.Restore, true);
            clear = QuietButton(Texts.Unsave);
            commands.Controls.AddRange(new Control[] { execute, clear });
            command.Controls.Add(title, 0, 0); command.Controls.Add(commands, 1, 0);
            root.Controls.Add(command, 0, 0);

            options = Strip(AppTheme.Surface, 2, 6);
            automatic = Flow();
            enabled = Check(Texts.Enabled, Texts.EnabledTip);
            autoStatus = Caption("", secondary, AppTheme.Muted);
            automatic.Controls.AddRange(new Control[] { enabled, autoStatus });
            settings = AppTheme.Button(L.T("設定を開く"), false);
            settings.Name = "SettingsButton";
            settings.AccessibleName = settings.Text;
            settings.MinimumSize = Ui.Sz(120, 36);
            settings.BackColor = AppTheme.Background;
            settings.FlatAppearance.BorderColor = AppTheme.Muted;
            settings.Anchor = AnchorStyles.Right;
            options.Controls.Add(automatic, 0, 0); options.Controls.Add(settings, 1, 0);
            root.Controls.Add(options, 0, 1);

            preferences = Flow(); preferences.Dock = DockStyle.Fill; preferences.WrapContents = true;
            preferences.Padding = Pad(19, 7, 19, 7); preferences.BackColor = AppTheme.Background;
            login = Check(Texts.AutoStart, Texts.AutoStartTip); launch = Check(Texts.LaunchMissing, Texts.LaunchMissingTip);
            tray = Check(Texts.MinimizeToTrayOnClose, Texts.MinimizeToTrayOnCloseTip);
            preferences.Controls.AddRange(new Control[] { login, launch, tray });
            var languageLabel=Caption(L.T("言語"),secondary,AppTheme.Muted);
            languageLabel.Margin=Pad(0,6,8,4);
            language=new ThemedSelect { Name="LanguageSelector",Width=Ui.S(240),Margin=Pad(0,3,0,3),AccessibleName=L.T("言語") };
            var languageGroup=Flow();languageGroup.Controls.Add(languageLabel);languageGroup.Controls.Add(language);
            preferences.Controls.Add(languageGroup);
            var backup = AppTheme.Button(L.T("設定をバックアップ"), false);
            var restoreBackup = AppTheme.Button(L.T("バックアップから復元"), false);
            L.BindText(backup, "設定をバックアップ");
            L.BindText(restoreBackup, "バックアップから復元");
            preferences.Controls.AddRange(new Control[] { backup, restoreBackup });
            backup.Click += delegate { ManageBackup(false); };
            restoreBackup.Click += delegate { ManageBackup(true); };
            language.SelectedIndexChanged+=delegate {
                if(readingLanguage || language.SelectedItem==null)return;
                try{controller.Language=L.Languages[language.SelectedIndex].Code;}
                catch(Exception ex){MessageBox.Show(this,ex.Message,L.T("設定を変更できませんでした"),MessageBoxButtons.OK,MessageBoxIcon.Error);ReadLanguage();}
            };
            preferences.Visible = false; root.Controls.Add(preferences, 0, 2);
            settings.Click += delegate
            {
                bool open = !preferences.Visible;
                preferences.Visible = open;
                settings.Text = open ? L.T("設定を閉じる") : L.T("設定を開く");
                settings.AccessibleName = settings.Text;
                settings.BackColor = open ? AppTheme.Band : AppTheme.Background;
                settings.FlatAppearance.BorderColor = open ? AppTheme.Accent : AppTheme.Muted;
                ArrangeCommands();
            };

            ConfigureGrid();
            gridHost = new GridScrollHost(grid) { Dock = DockStyle.Fill, MinimumSize = Ui.Sz(0, 84) };
            root.Controls.Add(gridHost, 0, 3);
            var footer = Strip(AppTheme.Background, 10, 10);
            var hint = Caption(L.T("対象を ON にすると現在位置を登録し、OFF にすると登録を解除します。"), secondary, AppTheme.Muted);
            hint.Dock = DockStyle.Fill;
            footer.Controls.Add(hint, 0, 0); footer.SetColumnSpan(hint, 2);
            root.Controls.Add(footer, 0, 4);
            message = new Label { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty,
                Padding = Pad(19, 3, 12, 3), Height = Ui.S(27), MinimumSize = Ui.Sz(0, 27), Font = secondary,
                ForeColor = AppTheme.Muted, BackColor = AppTheme.Background, Visible = false };
            root.Controls.Add(message, 0, 5);

            execute.Click += delegate { Perform(delegate { controller.RestoreNow(new List<WindowRow>()); }); };
            clear.Click += delegate
            {
                Perform(delegate
                {
                    if (MessageBox.Show(this, L.F("登録済みの {0} 件をすべて解除しますか？",savedTotal),
                        Texts.Unsave, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                        controller.UnsaveAll();
                });
            };
            BindSetting(enabled, delegate(bool value) { controller.Enabled = value; });
            BindSetting(login, delegate(bool value) { controller.AutoStart = value; });
            BindSetting(launch, delegate(bool value) { controller.LaunchMissing = value; });
            BindSetting(tray, delegate(bool value) { controller.MinimizeToTrayOnClose = value; });
            ReadSettings();
            MeasureGrid();
            command.SizeChanged += delegate { ArrangeCommands(); };
            options.SizeChanged += delegate { ArrangeCommands(); };
            ArrangeCommands();
            controller.RowsChanged += RowsChanged;
            bool firstShow = true;
            VisibleChanged += delegate {
                if (!Visible || IsDisposed) return;
                if (firstShow) firstShow = false;
                else RefreshLedger();
            };
            L.Attach(this,delegate {
                controller.ClearMessage();
                ReadLanguage();
                settings.Text=L.T(preferences.Visible?"設定を閉じる":"設定を開く");
                settings.AccessibleName=settings.Text;
                tips.SetToolTip(enabled,Texts.EnabledTip);tips.SetToolTip(login,Texts.AutoStartTip);
                tips.SetToolTip(launch,Texts.LaunchMissingTip);tips.SetToolTip(tray,Texts.MinimizeToTrayOnCloseTip);
                ReadSettings();RefreshLedger();MeasureGrid();ArrangeCommands();
            });
        }

        void ReadLanguage() {
            readingLanguage=true;
            try {
                language.SetItems(L.Languages.Select(choice => choice.ToString()));
                for(int i=0;i<language.Items.Count;i++)if(L.Languages[i].Code==controller.Language){language.SelectedIndex=i;break;}
                MeasureLanguage();
            }finally{readingLanguage=false;}
        }
        void MeasureLanguage() {
            if(language==null)return;
            Size preferred = language.GetPreferredSize(Size.Empty);
            language.Size = new Size(Math.Max(Pixel(240), preferred.Width), preferred.Height);
            language.Invalidate();
        }

        static Point StartupPoint()
        {
            Point point;
            try { if (GetPhysicalCursorPos(out point)) return point; }
            catch (EntryPointNotFoundException) { }
            return Cursor.Position;
        }
        static Rectangle Centered(Size size, Rectangle work)
        {
            return new Rectangle(work.Left + (work.Width - size.Width) / 2,
                work.Top + (work.Height - size.Height) / 2, size.Width, size.Height);
        }
        internal static Size FitStartupSize(Size desiredClient, Size frame, Rectangle work, int margin)
        {
            int inset = Math.Max(0, margin);
            int width = Math.Max(1, work.Width - Math.Min(work.Width - 1, 2 * inset));
            int height = Math.Max(1, work.Height - Math.Min(work.Height - 1, 2 * inset));
            return new Size((int)Math.Min(width, Math.Max(1L, (long)desiredClient.Width + frame.Width)),
                (int)Math.Min(height, Math.Max(1L, (long)desiredClient.Height + frame.Height)));
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshLedger();
            FitInitialWindow();
        }
        internal void FitInitialWindow()
        {
            if (initialSizeApplied || IsDisposed || Disposing) return;
            initialSizeApplied = true;
            Rectangle work = Screen.FromHandle(Handle).WorkingArea;
            MeasureGrid();
            int width = PreferredGridWidth();
            int commandsWidth = title.PreferredSize.Width + commands.PreferredSize.Width + command.Padding.Horizontal;
            int optionsWidth = automatic.PreferredSize.Width + settings.PreferredSize.Width + settings.Margin.Horizontal + options.Padding.Horizontal;
            width = Math.Max(width, Math.Max(commandsWidth, optionsWidth));
            Size frame = Size - ClientSize;
            Size fit = FitStartupSize(new Size(width, Pixel(420)), frame, work, Pixel(14));
            MinimumSize = new Size(Math.Min(Pixel(610), fit.Width), Math.Min(Pixel(390), fit.Height));
            Bounds = Centered(fit, work);
            ArrangeCommands();
            PerformLayout();
            var root = (TableLayoutPanel)gridHost.Parent;
            root.PerformLayout();
            // 初回表示前は親フォームが非表示なので、Visible に頼らず常設の行を測る。
            int chromeHeight = root.Padding.Vertical;
            foreach (int row in new[] { 0, 1, 4 })
            {
                Control strip = root.GetControlFromPosition(0, row);
                chromeHeight += strip.GetPreferredSize(new Size(root.ClientSize.Width, 0)).Height + strip.Margin.Vertical;
            }
            long rowsHeight = grid.Rows.Cast<DataGridViewRow>().Sum(r => (long)r.Height);
            gridHost.SyncScrollBars();
            int scrollbar = gridHost.HorizontalScrollNeeded ? gridHost.ScrollBarThickness : 0;
            int height = (int)Math.Min(int.MaxValue, Math.Max(Pixel(420),
                rowsHeight + grid.ColumnHeadersHeight + chromeHeight + scrollbar + Pixel(4)));
            fit = FitStartupSize(new Size(width, height), frame, work, Pixel(14));
            Bounds = Centered(fit, work);
        }
        int PreferredGridWidth()
        {
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            int total = Pixel(22);
            using (var body = PixelFont(Font))
            using (var small = PixelFont(secondary))
            {
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    int width = column.MinimumWidth;
                    foreach (DataGridViewRow view in grid.Rows)
                    {
                        if (!(view.Tag is WindowRow) || column.Index == Target) continue;
                        string[] lines = Convert.ToString(view.Cells[column.Index].Value).Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                            width = Math.Max(width, TextRenderer.MeasureText(lines[i], i == 0 ? body : small, Size.Empty, flags).Width + Pixel(18));
                    }
                    if (column.Index == NameColumn || column.Index == Rule)
                        width = Math.Max(column.MinimumWidth, Math.Min(width, Pixel(column.Index == NameColumn ? 300 : 260)));
                    if (column.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill) column.FillWeight = width;
                    total += width;
                }
            }
            return total;
        }
        static Padding Pad(int l, int t, int r, int b) { return new Padding(Ui.S(l), Ui.S(t), Ui.S(r), Ui.S(b)); }
        void CreateFonts()
        {
            heading = UiTypography.BodyFont(1.3f, FontStyle.Bold, UiScale);
            bold = UiTypography.BodyFont(1f, FontStyle.Bold, UiScale);
            secondary = UiTypography.BodyFont(.95f, FontStyle.Regular, UiScale);
        }
        void ArrangeCommands()
        {
            if (arranging || settings == null || autoStatus == null) return;
            arranging = true;
            try
            {
                ArrangeStrip(command, title, commands);
                ArrangeStrip(options, automatic, settings);
            }
            finally { arranging = false; }
        }
        static void ArrangeStrip(TableLayoutPanel strip, FlowLayoutPanel left, Control right)
        {
            int available = strip.ClientSize.Width - strip.Padding.Horizontal;
            if (available <= 0) return;
            var rightFlow = right as FlowLayoutPanel;
            int leftWidth = left.Controls.Cast<Control>().Sum(c => c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal);
            int rightWidth = rightFlow == null ? right.GetPreferredSize(Size.Empty).Width :
                rightFlow.Controls.Cast<Control>().Sum(c => c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal);
            bool stacked = leftWidth + rightWidth + right.Margin.Horizontal > available;
            left.MaximumSize = new Size(available, 0); left.WrapContents = leftWidth > available;
            if (rightFlow != null) { rightFlow.MaximumSize = new Size(available, 0); rightFlow.WrapContents = rightWidth > available; }
            if (stacked == (strip.GetRow(right) == 1)) return;
            strip.SuspendLayout();
            try
            {
                if (stacked) strip.RowCount = 2;
                strip.SetCellPosition(right, new TableLayoutPanelCellPosition(stacked ? 0 : 1, stacked ? 1 : 0));
                strip.SetColumnSpan(left, stacked ? 2 : 1); strip.SetColumnSpan(right, stacked ? 2 : 1);
                right.Anchor = stacked ? AnchorStyles.Left : AnchorStyles.Right;
                if (!stacked) strip.RowCount = 1;
            }
            finally { strip.ResumeLayout(true); }
        }
        static FlowLayoutPanel Flow()
        {
            return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Margin = Padding.Empty, Anchor = AnchorStyles.Left };
        }
        static TableLayoutPanel Strip(Color color, int top, int bottom)
        {
            var panel = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, BackColor = color, Padding = Pad(18, top, 18, bottom), Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return panel;
        }
        static Label Caption(string value, Font font, Color color)
        {
            return new Label { AutoSize = true, Text = value, Font = font, ForeColor = color, Anchor = AnchorStyles.Left,
                Margin = Pad(0, 0, 10, 0), TextAlign = ContentAlignment.MiddleLeft };
        }
        static Button QuietButton(string text)
        {
            var b = AppTheme.Button(text, false); b.MinimumSize = Ui.Sz(55, 30); b.FlatAppearance.BorderSize = 0;
            b.ForeColor = AppTheme.Muted; b.Padding = Pad(6, 3, 6, 3); return b;
        }
        CheckBox Check(string text, string tip)
        {
            var check = new ToggleSwitch { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Pad(0, 4, 13, 4) };
            tips.SetToolTip(check, tip); return check;
        }
        void ReadSettings()
        {
            readingSettings = true;
            try
            {
                enabled.Checked = controller.Enabled; login.Checked = controller.AutoStart; launch.Checked = controller.LaunchMissing;
                tray.Checked = controller.MinimizeToTrayOnClose;
                autoStatus.Text = enabled.Checked ? L.T("約2秒ごとに自動復元") : L.T("自動復元を一時停止中");
                ArrangeCommands();
            }
            finally { readingSettings = false; }
        }
        void BindSetting(CheckBox check, Action<bool> write)
        {
            check.CheckedChanged += delegate
            {
                if (readingSettings) return;
                try { write(check.Checked); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("設定を変更できませんでした"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
                ReadSettings();
            };
        }
        void ConfigureGrid()
        {
            grid.Name = "LedgerGrid"; grid.AccessibleName = L.T("モニタ別の配置台帳");
            grid.Dock = DockStyle.Fill; grid.Margin = Padding.Empty; grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = AppTheme.Surface; grid.GridColor = AppTheme.Border; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.RowHeadersVisible = false; grid.ReadOnly = true; grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.AllowUserToOrderColumns = false; grid.AllowUserToResizeColumns = false;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle = Style(AppTheme.Background, AppTheme.Muted);
            grid.DefaultCellStyle = Style(AppTheme.Surface, AppTheme.Foreground);
            grid.DefaultCellStyle.Padding = Pad(7, 0, 6, 0);
            grid.ColumnHeadersDefaultCellStyle.Padding = Pad(7, 0, 4, 0);
            var check = new DataGridViewCheckBoxColumn { HeaderText = L.T("対象"), Name = "Target", Width = Ui.S(82),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None, FlatStyle = FlatStyle.Standard };
            check.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            check.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            check.HeaderCell.Style.Padding = check.DefaultCellStyle.Padding = Padding.Empty;
            grid.Columns.Add(check);
            AddColumn(L.T("名前 ／ プロセス"), 24, 115); AddColumn(L.T("位置 ／ サイズ"), 19, 111); AddColumn(L.T("見分け方"), 18, 100);
            AddColumn(L.T("最大化"), 8, 61); AddColumn(L.T("登録 ／ 状態"), 20, 127);
            var edit = new DataGridViewButtonColumn { HeaderText = L.T("編集"), Width = Ui.S(62), Name = "Edit",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None, FlatStyle = FlatStyle.Flat };
            edit.DefaultCellStyle.Padding = Pad(3, 9, 6, 9); grid.Columns.Add(edit);
            foreach (DataGridViewColumn col in grid.Columns) col.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.CellPainting += PaintCell;
            grid.CellMouseEnter += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == EditColumn && grid.Rows[e.RowIndex].Tag is WindowRow)
                    SetHoverEdit(grid.Rows[e.RowIndex]);
            };
            grid.CellMouseLeave += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.ColumnIndex == EditColumn) SetHoverEdit(null);
            };
            grid.CellMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex != EditColumn) Toggle(grid.Rows[e.RowIndex]);
            };
            grid.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == EditColumn) Edit(grid.Rows[e.RowIndex]);
            };
            grid.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (grid.CurrentCell == null || (e.KeyCode != Keys.Space && e.KeyCode != Keys.Enter)) return;
                var row = grid.CurrentRow;
                if (grid.CurrentCell.ColumnIndex == EditColumn) Edit(row);
                else if (e.KeyCode == Keys.Space) Toggle(row); else return;
                e.Handled = true; e.SuppressKeyPress = true;
            };
        }
        int Pixel(int logical) { return (int)Math.Round(logical * UiScale); }
        Padding GridPadding(int l, int t, int r, int b) { return new Padding(Pixel(l), Pixel(t), Pixel(r), Pixel(b)); }
        protected override void OnUiScaleChanged(float factor)
        {
            base.OnUiScaleChanged(factor);
            if (grid == null || grid.Columns.Count <= EditColumn || message == null) return;
            MeasureGrid(); MeasureLanguage(); ArrangeCommands();
        }
        protected override void OnTextMetricsChanged()
        {
            base.OnTextMetricsChanged();
            if (message == null || IsDisposed || Disposing) return;
            var priorHeading = heading; var priorBold = bold; var priorSecondary = secondary;
            CreateFonts();
            priorHeading.Dispose(); priorBold.Dispose(); priorSecondary.Dispose();
            MeasureGrid(); MeasureLanguage(); ArrangeCommands();
        }
        void MeasureGrid()
        {
            // GDI のピクセルフォントで、移動先モニタの DPI と現在の文字サイズを各一度だけ反映する。
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            using (var bodyPixels = PixelFont(Font))
            using (var secondaryPixels = PixelFont(secondary))
            using (var boldPixels = PixelFont(bold))
            {
                bodyLineHeight = TextRenderer.MeasureText("配置 Ag", bodyPixels, Size.Empty, flags).Height;
                secondaryLineHeight = TextRenderer.MeasureText("配置 Ag", secondaryPixels, Size.Empty, flags).Height;
                bandHeight = Math.Max(Pixel(25), TextRenderer.MeasureText("モニタ 1", boldPixels, Size.Empty, flags).Height + Pixel(6));
                Size targetSize = SwitchRenderer.SizeFor(UiScale);
                rowHeight = Math.Max(Math.Max(Pixel(44), targetSize.Height + Pixel(6)),
                    bodyLineHeight + secondaryLineHeight + Pixel(6));
                grid.ColumnHeadersHeight = Math.Max(Pixel(30), bodyLineHeight + Pixel(8));
                grid.RowTemplate.Height = rowHeight;
                int[] minima = { 82, 115, 111, 100, 61, 105, 62 };
                string[] samples = { "", "", "-2887, -1673", L.T("アプリで判定"), L.T("いいえ"), L.T("位置を戻しました"), Texts.Edit };
                for (int i = 0; i < minima.Length; i++)
                {
                    var column = grid.Columns[i];
                    int label = TextRenderer.MeasureText(column.HeaderText, bodyPixels, Size.Empty, flags).Width;
                    int value = TextRenderer.MeasureText(samples[i], bodyPixels, Size.Empty, flags).Width;
                    int width = Math.Max(Pixel(minima[i]), Math.Max(label, value) + Pixel(16));
                    if (i == Target) width = Math.Max(width, targetSize.Width + Pixel(16));
                    column.MinimumWidth = width;
                    if (column.AutoSizeMode == DataGridViewAutoSizeColumnMode.None) column.Width = width;
                }
            }
            message.MinimumSize = new Size(0, secondaryLineHeight + message.Padding.Vertical + Pixel(3));
            if (gridHost != null) gridHost.MinimumSize = new Size(0, grid.ColumnHeadersHeight + rowHeight + gridHost.ScrollBarThickness);
            grid.DefaultCellStyle.Padding = GridPadding(7, 0, 6, 0);
            grid.ColumnHeadersDefaultCellStyle.Padding = GridPadding(7, 0, 4, 0);
            grid.Columns[EditColumn].DefaultCellStyle.Padding = GridPadding(3, 9, 6, 9);
            foreach (DataGridViewRow row in grid.Rows) row.Height = row.Tag is WindowRow ? rowHeight : bandHeight;
            if (gridHost != null) gridHost.SyncScrollBars();
        }
        Font PixelFont(Font source)
        {
            if (source.Unit == GraphicsUnit.Pixel) return (Font)source.Clone();
            return new Font(source.FontFamily, source.SizeInPoints * 96f * UiScale / 72f, source.Style, GraphicsUnit.Pixel);
        }
        static DataGridViewCellStyle Style(Color background, Color foreground)
        {
            return new DataGridViewCellStyle { BackColor = background, ForeColor = foreground,
                SelectionBackColor = background, SelectionForeColor = foreground };
        }
        void AddColumn(string name, int weight, int minimum)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = weight, MinimumWidth = Ui.S(minimum) });
        }
        void SetHoverEdit(DataGridViewRow next)
        {
            if (hoverEdit == next) return;
            var previous = hoverEdit; hoverEdit = next;
            if (previous != null && previous.DataGridView == grid) grid.InvalidateCell(previous.Cells[EditColumn]);
            if (next != null) grid.InvalidateCell(next.Cells[EditColumn]);
        }
        void PaintCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var row = grid.Rows[e.RowIndex];
            if (row.Tag is string)
            {
                DrawBand(e, row); e.Handled = true; return;
            }
            var data = row.Tag as WindowRow;
            if (data == null) return;
            if (e.ColumnIndex == Target)
            {
                e.PaintBackground(e.ClipBounds, false);
                e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);
                Size size = SwitchRenderer.SizeFor(UiScale);
                var track = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - size.Width) / 2,
                    e.CellBounds.Y + (e.CellBounds.Height - size.Height) / 2, size.Width, size.Height);
                SwitchRenderer.Draw(e.Graphics, track, data.Saved, grid.Enabled, Font);
                if (grid.Focused && grid.CurrentCell == row.Cells[Target])
                    ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(track, Pixel(2), Pixel(2)),
                        AppTheme.Foreground, e.CellStyle.BackColor);
                e.Handled = true; return;
            }
            if (e.ColumnIndex == EditColumn)
            {
                e.PaintBackground(e.ClipBounds, false);
                e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);
                var area = Rectangle.Inflate(e.CellBounds, -Pixel(4), -Pixel(5));
                if (row == hoverEdit)
                    using (var brush = new SolidBrush(AppTheme.Band)) e.Graphics.FillRectangle(brush, area);
                TextRenderer.DrawText(e.Graphics, Texts.Edit, Font, area, AppTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                if (grid.Focused && grid.CurrentCell == row.Cells[EditColumn])
                    ControlPaint.DrawFocusRectangle(e.Graphics, area, AppTheme.Foreground, row == hoverEdit ? AppTheme.Band : e.CellStyle.BackColor);
                e.Handled = true; return;
            }
            if (e.ColumnIndex != NameColumn && e.ColumnIndex != Geometry && e.ColumnIndex != State) return;
            var value = row.Cells[e.ColumnIndex].Value as string;
            if (value == null) return;
            var lines = value.Split(new[] { '\n' }, 2);
            e.PaintBackground(e.ClipBounds, false);
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);
            var bounds = Rectangle.Inflate(e.CellBounds, -Pixel(7), 0);
            var first = new Rectangle(bounds.X, bounds.Y + (bounds.Height - bodyLineHeight - secondaryLineHeight) / 2,
                bounds.Width, bodyLineHeight);
            var second = new Rectangle(first.X, first.Bottom, first.Width, secondaryLineHeight);
            Color color = e.ColumnIndex != State ? AppTheme.Foreground : data.Saved ? AppTheme.Accent : AppTheme.Muted;
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(e.Graphics, lines[0], Font, first, color, flags);
            if (lines.Length > 1) TextRenderer.DrawText(e.Graphics, lines[1], secondary, second, AppTheme.Muted, flags);
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Focus); e.Handled = true;
        }
        void DrawBand(DataGridViewCellPaintingEventArgs e, DataGridViewRow row)
        {
            using (var brush = new SolidBrush(AppTheme.Band)) e.Graphics.FillRectangle(brush, e.CellBounds);
            // 二つのセルにまたがるよう、同じ起点で描き、各セルの領域で切り取る。
            if (e.ColumnIndex > NameColumn) return;
            var full = grid.GetCellDisplayRectangle(Target, e.RowIndex, false);
            full.Width = grid.Columns[Target].Width + grid.Columns[NameColumn].Width;
            full.X += Pixel(16); full.Width -= Pixel(16);
            var saved = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(e.CellBounds, System.Drawing.Drawing2D.CombineMode.Intersect);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(row.Cells[NameColumn].Value), bold, full, AppTheme.Foreground,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.NoPadding);
            }
            finally { e.Graphics.Restore(saved); }
        }
        void Toggle(DataGridViewRow view)
        {
            var data = view.Tag as WindowRow;
            if (data == null || busy) return;
            Perform(delegate { controller.SetTarget(data, !data.Saved); });
        }
        void Edit(DataGridViewRow view)
        {
            var data = view.Tag as WindowRow;
            if (data == null || busy) return;
            Perform(delegate { controller.Edit(data, this); });
        }
        void ManageBackup(bool restore)
        {
            string title = L.T(restore ? "バックアップから復元" : "設定をバックアップ");
            const string filter = "AppLayout backup (*.json)|*.json";
            try
            {
                if (!restore)
                {
                    using (var dialog = new SaveFileDialog { Title = title, Filter = filter, DefaultExt = "json", OverwritePrompt = false,
                        FileName = "AppLayout-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", RestoreDirectory = true })
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        controller.ExportSettings(dialog.FileName);
                    }
                }
                else
                {
                    using (var dialog = new OpenFileDialog { Title = title, Filter = filter, RestoreDirectory = true })
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        var replacement = ConfigStore.ReadBackup(dialog.FileName);
                        if (MessageBox.Show(this, L.F("バックアップの配置 {0} 件で現在の設定を置き換えますか？\n自動配置とアプリの自動起動は一時停止します。復元後にモニタと配置を確認してください。",
                            replacement.Apps.Count), title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                        controller.RestoreSettings(replacement);
                        ReadSettings();
                        RefreshLedger();
                    }
                }
                MessageBox.Show(this, L.T(restore ? "設定を復元しました。" : "バックアップを保存しました。"),
                    title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void Perform(Action action)
        {
            if (busy) return;
            busy = true;
            try { action(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("配置を変更できませんでした"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { busy = false; RefreshLedger(); }
        }
        void RowsChanged(object sender, EventArgs e) { if (Visible) RefreshLedger(); }
        void RefreshLedger()
        {
            if (busy || IsDisposed || Disposing) return;
            busy = true;
            try
            {
                SetRows(controller.GetRows(), false);
                var text = controller.LastMessage ?? "";
                if (message.Text != text) { message.Text = text; tips.SetToolTip(message, text); message.Visible = text.Length > 0; }
            }
            finally { busy = false; }
        }
        static string GroupFor(WindowRow row)
        {
            int number;
            if (int.TryParse(row.Monitor, out number) && number > 0) return number.ToString();
            return row.Saved && row.Monitor == "なし" ? "未接続" : "不明";
        }
        static int GroupOrder(string group)
        {
            int order; return int.TryParse(group, out order) ? order : group == "未接続" ? int.MaxValue - 1 : int.MaxValue;
        }
        static string RowKey(DataGridViewRow view)
        {
            if (view == null) return null;
            var data = view.Tag as WindowRow;
            return data != null ? data.Key : view.Tag as string;
        }
        // 登録と解除でキーが e:ID ⇔ w:HWND に変わっても、同じウィンドウの表示行と順番を引き継ぐ。
        void ReuseWindowViews(Dictionary<string, WindowRow> incoming)
        {
            var oldByHandle = views.Values.Select(v => v.Tag as WindowRow)
                .Where(r => r != null && !incoming.ContainsKey(r.Key) && r.Window != null && r.Window.Handle != IntPtr.Zero)
                .ToLookup(r => r.Window.Handle);
            var newByHandle = incoming.Values.Where(r => !views.ContainsKey(r.Key) && r.Window != null && r.Window.Handle != IntPtr.Zero)
                .ToLookup(r => r.Window.Handle);
            foreach (var group in newByHandle)
            {
                var prior = oldByHandle[group.Key];
                if (group.Count() != 1 || prior.Count() != 1) continue;
                var next = group.First(); var old = prior.First();
                if (next.Saved == old.Saved || next.Window.Pid != old.Window.Pid ||
                    !string.Equals(next.Window.ProcessName, old.Window.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                    AppEntry.ClassKey(next.Window.Class) != AppEntry.ClassKey(old.Window.Class)) continue;
                var view = views[old.Key]; views.Remove(old.Key); views.Add(next.Key, view);
                arrival.Add(next.Key, arrival[old.Key]); arrival.Remove(old.Key);
                view.Tag = next;
            }
        }
        internal void SetRows(IList<WindowRow> rows, bool resetChecks)
        {
            gridHost.UpdateGrid(delegate { UpdateRows(rows); });
        }
        void UpdateRows(IList<WindowRow> rows)
        {
            var incoming = new Dictionary<string, WindowRow>();
            foreach (var row in rows) incoming[row.Key] = row;
            ReuseWindowViews(incoming);
            foreach (var key in arrival.Keys.Where(k => !incoming.ContainsKey(k)).ToList()) arrival.Remove(key);
            foreach (var row in incoming.Values)
            {
                if (!arrival.ContainsKey(row.Key)) arrival.Add(row.Key, nextOrder++);
            }

            var order = new List<string>();
            var counts = new Dictionary<string, int>();
            foreach (var group in incoming.Values.GroupBy(GroupFor).OrderBy(g => GroupOrder(g.Key)))
            {
                string key = "group:" + group.Key;
                order.Add(key); counts.Add(key, group.Count());
                order.AddRange(group.OrderBy(r => arrival[r.Key]).Select(r => r.Key));
            }
            string anchor = null, current = null; int col = 0;
            if (grid.FirstDisplayedScrollingRowIndex >= 0) anchor = RowKey(grid.Rows[grid.FirstDisplayedScrollingRowIndex]);
            if (grid.CurrentCell != null) { current = RowKey(grid.CurrentRow); col = grid.CurrentCell.ColumnIndex; }
            bool structural = false;
            var desired = new HashSet<string>(order);
            foreach (var key in views.Keys.Where(k => !desired.Contains(k)).ToList())
            {
                grid.Rows.Remove(views[key]); views.Remove(key); structural = true;
            }
            for (int i = 0; i < order.Count; i++)
            {
                string key = order[i]; DataGridViewRow view;
                if (!views.TryGetValue(key, out view))
                {
                    view = new DataGridViewRow(); view.CreateCells(grid);
                    view.Tag = incoming.ContainsKey(key) ? (object)incoming[key] : key;
                    view.Height = incoming.ContainsKey(key) ? rowHeight : bandHeight;
                    grid.Rows.Insert(i, view);
                    // 共有行の Index は -1 のため、添字から個別の行を取得してセルを更新する。
                    view = grid.Rows[i]; views.Add(key, view); structural = true;
                }
                else if (view.Index != i)
                {
                    grid.Rows.Remove(view); grid.Rows.Insert(i, view);
                    view = grid.Rows[i]; views[key] = view; structural = true;
                }
                WindowRow row;
                if (incoming.TryGetValue(key, out row))
                {
                    view.Tag = row;
                    UpdateTarget(view, row);
                    SetCell(view, NameColumn, row.Name + "\n" + row.Process);
                    SetCell(view, Geometry, (string.IsNullOrEmpty(row.Position) ? "—" : row.Position) + "\n" +
                        (string.IsNullOrEmpty(row.Size) ? "—" : row.Size));
                    SetCell(view, Rule, L.Rule(row.TitleRule)); SetCell(view, Max, L.T(row.Maximized)); SetCell(view, EditColumn, Texts.Edit);
                }
                else
                {
                    string group = key.Substring(6);
                    string caption = GroupOrder(group) < int.MaxValue - 1 ? L.F("モニタ {0}",group) : L.F("モニタ{0}",L.T(group));
                    if (SetCell(view, NameColumn, caption + "    " + L.F("{0} 件",counts[key])))
                        grid.InvalidateCell(view.Cells[Target]);
                }
            }
            if (structural)
            {
                DataGridViewRow restore;
                if (current != null && views.TryGetValue(current, out restore) && grid.CurrentCell != restore.Cells[col])
                    grid.CurrentCell = restore.Cells[col];
                if (anchor != null && views.TryGetValue(anchor, out restore) && grid.FirstDisplayedScrollingRowIndex != restore.Index)
                    grid.FirstDisplayedScrollingRowIndex = restore.Index;
            }
            UpdateSummary();
        }
        void UpdateTarget(DataGridViewRow view, WindowRow row)
        {
            SetCell(view, Target, row.Saved);
            SetCell(view, State, (row.Saved ? L.T("登録済み") : L.T("未登録")) + "\n" + L.T(row.Status));
            Color background = row.Saved ? AppTheme.ActiveRow : AppTheme.Surface;
            if (view.DefaultCellStyle.BackColor != background)
                view.DefaultCellStyle = Style(background, AppTheme.Foreground);
        }
        static bool SetCell(DataGridViewRow row, int column, object value)
        {
            var cell = row.Cells[column];
            if (object.Equals(cell.Value, value)) return false;
            cell.Value = value;
            if (value is string) cell.ToolTipText = (string)value;
            return true;
        }
        void UpdateSummary()
        {
            savedTotal = views.Values.Select(v => v.Tag as WindowRow).Count(r => r != null && r.Saved);
            savedCount.Text = L.F("{0} 件",savedTotal);
            execute.Enabled = clear.Enabled = savedTotal > 0;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { controller.RowsChanged -= RowsChanged; tips.Dispose(); }
            base.Dispose(disposing);
            if (disposing) { heading.Dispose(); bold.Dispose(); secondary.Dispose(); windowIcon.Dispose(); }
        }
    }
}
