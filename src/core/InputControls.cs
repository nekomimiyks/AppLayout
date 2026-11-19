using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppLayout
{
    internal static class InputLook
    {
        internal const TextFormatFlags TextFlags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        internal static int Pixels(Control control, int value)
        {
            var form = control.FindForm() as DpiAwareForm;
            return (int)Math.Round(value * (form == null ? Ui.K : form.UiScale));
        }

        internal static void Frame(Graphics graphics, Rectangle bounds, Color fill, Color border, int radius)
        {
            bounds.Width--; bounds.Height--;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            int size = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                if (size < 2)
                {
                    graphics.FillRectangle(brush, bounds); graphics.DrawRectangle(pen, bounds);
                    return;
                }
                using (var path = new GraphicsPath())
                {
                    path.AddArc(bounds.Left, bounds.Top, size, size, 180, 90);
                    path.AddArc(bounds.Right - size, bounds.Top, size, size, 270, 90);
                    path.AddArc(bounds.Right - size, bounds.Bottom - size, size, size, 0, 90);
                    path.AddArc(bounds.Left, bounds.Bottom - size, size, size, 90, 90);
                    path.CloseFigure();
                    var state = graphics.Save();
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.FillPath(brush, path); graphics.DrawPath(pen, path);
                    graphics.Restore(state);
                }
            }
        }

        internal static void Chevron(Graphics graphics, Point center, int size, bool up, Color color)
        {
            int direction = up ? -1 : 1;
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, Math.Max(1f, size / 2.5f)))
                graphics.DrawLines(pen, new[] { new Point(center.X - size, center.Y - direction * size / 2),
                    new Point(center.X, center.Y + direction * size / 2),
                    new Point(center.X + size, center.Y - direction * size / 2) });
            graphics.Restore(state);
        }
    }

    internal sealed class InputFrame : Panel
    {
        readonly Control input;
        readonly NumericUpDown number;
        readonly UpDownStep up, down;
        Size visibleNumberSize;
        public InputFrame(Control input)
        {
            this.input = input;
            number = input as NumericUpDown;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = false;
            BackColor = AppTheme.Surface;
            input.Dock = DockStyle.None;
            var text = input as TextBox;
            if (text != null) text.BorderStyle = BorderStyle.None;
            if (number != null)
            {
                number.BorderStyle = BorderStyle.None;
                up = new UpDownStep(true);
                down = new UpDownStep(false);
                up.Click += delegate { number.UpButton(); };
                down.Click += delegate { number.DownButton(); };
                up.MouseUp += delegate { number.Focus(); };
                down.MouseUp += delegate { number.Focus(); };
            }
            AppTheme.StyleInput(input);
            Controls.Add(input);
            if (number != null) { Controls.Add(up); Controls.Add(down); }
            input.FontChanged += delegate { UpdateHeight(); };
            UpdateHeight();
            Enter += delegate { Invalidate(); };
            Leave += delegate { Invalidate(); };
        }
        int S(int value) { return InputLook.Pixels(this, value); }
        public override Size GetPreferredSize(Size proposedSize)
        {
            return new Size(0, Math.Max(S(32), input.PreferredSize.Height + S(10)));
        }
        internal void UpdateHeight()
        {
            int height = GetPreferredSize(Size.Empty).Height;
            if (Height != height) Height = height;
            PerformLayout();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (input == null) return;
            int left = S(10), right = number == null ? left : S(29);
            int height = input.PreferredSize.Height;
            int width = Math.Max(1, Width - left - right);
            int top = (Height - height) / 2;
            if (number == null)
            {
                input.SetBounds(left, top, width, height);
                return;
            }
            // WinForms repositions the original spinner when its handle or font changes.
            // Keep that part outside the visible window region and let the editor fill the field.
            int nativeButtonsWidth = 0;
            foreach (Control child in number.Controls)
            {
                if (child is TextBox) continue;
                nativeButtonsWidth = Math.Max(nativeButtonsWidth, child.Width);
                child.Hide();
            }
            var visibleSize = new Size(width, height);
            if (visibleNumberSize != visibleSize)
            {
                visibleNumberSize = visibleSize;
                var oldRegion = number.Region;
                number.Region = new Region(new Rectangle(Point.Empty, visibleSize));
                if (oldRegion != null) oldRegion.Dispose();
            }
            number.SetBounds(left, top, width + nativeButtonsWidth, height);
            int half = Math.Max(1, (Height - S(4)) / 2);
            int stepWidth = Math.Max(1, right - S(5));
            up.Bounds = new Rectangle(Width - right + S(2), S(2), stepWidth, half);
            down.Bounds = new Rectangle(up.Left, up.Bottom, stepWidth, half);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            InputLook.Frame(e.Graphics, ClientRectangle, AppTheme.Surface, ContainsFocus ? AppTheme.Accent : AppTheme.Border, S(4));
            if (number != null)
            {
                using (var pen = new Pen(AppTheme.Border))
                    e.Graphics.DrawLine(pen, Width - S(29), S(6), Width - S(29), Height - S(6));
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) input.Focus();
        }
        sealed class UpDownStep : Button
        {
            readonly bool up;
            readonly Timer repeat = new Timer();
            bool hover, repeated;
            public UpDownStep(bool up)
            {
                this.up = up;
                SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                TabStop = false;
                Cursor = Cursors.Hand;
                AccessibleRole = AccessibleRole.PushButton;
                AccessibleName = up ? "+" : "−";
                repeat.Tick += delegate
                {
                    if (!Capture) { repeat.Stop(); return; }
                    if (!ClientRectangle.Contains(PointToClient(MousePosition))) return;
                    repeated = true;
                    base.OnClick(EventArgs.Empty);
                    repeat.Interval = 65;
                };
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                repeated = false;
                repeat.Interval = 420;
                repeat.Start();
            }
            protected override void OnMouseUp(MouseEventArgs e) { repeat.Stop(); base.OnMouseUp(e); }
            protected override void OnMouseCaptureChanged(EventArgs e) { repeat.Stop(); base.OnMouseCaptureChanged(e); }
            protected override void OnClick(EventArgs e) { if (!repeated) base.OnClick(e); repeated = false; }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(hover ? AppTheme.Band : AppTheme.Surface);
                InputLook.Chevron(e.Graphics, new Point(Width / 2, Height / 2),
                    Math.Max(2, InputLook.Pixels(this, 3)), up, AppTheme.Muted);
            }
            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing) repeat.Dispose();
            }
        }
    }

    internal sealed class ThemedSelect : Control
    {
        readonly List<string> items = new List<string>();
        ContextMenuStrip popup;
        bool ListOpen { get { return popup != null && popup.Visible; } }
        int selectedIndex = -1;
        bool hover;
        public event EventHandler SelectedIndexChanged;
        public IList<string> Items { get { return items.AsReadOnly(); } }
        public string SelectedItem { get { return selectedIndex < 0 ? null : items[selectedIndex]; } }
        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                if (value < -1 || value >= items.Count) throw new ArgumentOutOfRangeException("value");
                if (selectedIndex == value) return;
                selectedIndex = value;
                UpdateText();
                var changed = SelectedIndexChanged;
                if (changed != null) changed(this, EventArgs.Empty);
            }
        }
        public ThemedSelect()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.ComboBox;
            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.Foreground;
        }
        public void SetItems(IEnumerable<string> labels)
        {
            if (ListOpen) popup.Close();
            items.Clear();
            items.AddRange(labels);
            if (selectedIndex >= items.Count) selectedIndex = -1;
            UpdateText();
        }
        void UpdateText()
        {
            Text = SelectedItem ?? "";
            Invalidate();
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }
        int S(int value) { return InputLook.Pixels(this, value); }
        public override Size GetPreferredSize(Size size)
        {
            int width = 0;
            foreach (string label in items) width = Math.Max(width, TextRenderer.MeasureText(label, Font).Width);
            return new Size(width + S(38), Math.Max(S(32), Font.Height + S(12)));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var fill = hover || ListOpen ? AppTheme.Band : AppTheme.Surface;
            InputLook.Frame(e.Graphics, ClientRectangle, fill, ContainsFocus || ListOpen ? AppTheme.Accent : AppTheme.Border, S(4));
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(S(10), 0, Math.Max(1, Width - S(36)), Height),
                Enabled ? AppTheme.Foreground : AppTheme.Muted, InputLook.TextFlags);
            InputLook.Chevron(e.Graphics, new Point(Width - S(15), Height / 2), Math.Max(2, S(4)), ListOpen, AppTheme.Muted);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) { Focus(); OpenList(); }
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Space: case Keys.Enter: case Keys.F4: case Keys.Alt | Keys.Down:
                    OpenList(); return true;
                case Keys.Up: case Keys.Down: case Keys.Home: case Keys.End:
                    if (items.Count > 0)
                    {
                        int next = keyData == Keys.Home ? 0 : keyData == Keys.End ? items.Count - 1 :
                            selectedIndex + (keyData == Keys.Up ? -1 : 1);
                        SelectedIndex = Math.Max(0, Math.Min(items.Count - 1, next));
                    }
                    return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }
        internal void OpenList()
        {
            if (!Enabled || items.Count == 0 || ListOpen || !IsHandleCreated) return;
            if (popup != null) popup.Dispose();
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false,
                Font = Font, Padding = new Padding(S(4)), Renderer = new SelectRenderer() };
            popup = menu;
            var area = Screen.FromControl(this).WorkingArea;
            int width = Math.Max(1, Math.Min(area.Width, Math.Max(Width, GetPreferredSize(Size.Empty).Width + menu.Padding.Horizontal)));
            menu.MinimumSize = new Size(width, 0);
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem(items[i]) { AutoSize = false, Checked = i == selectedIndex,
                    Size = new Size(Math.Max(1, width - menu.Padding.Horizontal), Math.Max(S(29), Font.Height + S(10))),
                    Margin = Padding.Empty, Padding = new Padding(S(9), 0, S(24), 0), TextAlign = ContentAlignment.MiddleLeft };
                item.Click += delegate { menu.Close(); SelectedIndex = index; };
                menu.Items.Add(item);
            }
            menu.Closed += delegate { Invalidate(); };
            menu.Show(this, new Point(0, Height + S(3)), ToolStripDropDownDirection.BelowRight);
            if (selectedIndex >= 0) menu.Items[selectedIndex].Select();
            Invalidate();
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SelectAccessible(this); }
        sealed class SelectAccessible : ControlAccessibleObject
        {
            readonly ThemedSelect owner;
            public SelectAccessible(ThemedSelect owner) : base(owner) { this.owner = owner; }
            public override string Value { get { return owner.Text; } }
            public override string DefaultAction { get { return owner.AccessibleName; } }
            public override AccessibleStates State { get { return base.State | (owner.ListOpen ? AccessibleStates.Expanded : AccessibleStates.Collapsed); } }
            public override void DoDefaultAction() { owner.Focus(); owner.OpenList(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && popup != null) popup.Dispose();
            base.Dispose(disposing);
        }
        sealed class SelectRenderer : ToolStripRenderer
        {
            static Rectangle VisibleRow(ToolStripItem item)
            {
                var owner = item.Owner;
                var row = new Rectangle(Point.Empty, item.Size);
                if (owner == null) return row;
                // ToolStrip は行の幅や表示位置を調整する。見えている範囲を行内の座標に変換する。
                var viewport = Rectangle.Inflate(owner.ClientRectangle, -1, 0);
                viewport.Offset(-item.Bounds.Left, -item.Bounds.Top);
                return Rectangle.Intersect(row, viewport);
            }
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(AppTheme.Surface))
                    e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var pen = new Pen(AppTheme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                bool current = ((ToolStripMenuItem)e.Item).Checked;
                var row = VisibleRow(e.Item);
                if (row.Width < 1 || row.Height < 1) return;
                Color background = current ? AppTheme.ActiveRow : e.Item.Selected ? AppTheme.Band : AppTheme.Surface;
                using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, row);
                if (e.Item.Selected)
                    using (var pen = new Pen(AppTheme.Accent)) e.Graphics.DrawRectangle(pen, row.X, row.Y, row.Width - 1, row.Height - 1);
                if (current)
                {
                    int x = row.Right - e.Item.Padding.Right / 2, y = e.Item.Height / 2;
                    using (var pen = new Pen(AppTheme.Accent, 2))
                        e.Graphics.DrawLines(pen, new[] { new Point(x - 5, y), new Point(x - 1, y + 4), new Point(x + 6, y - 4) });
                }
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                var row = VisibleRow(e.Item);
                int left = row.Left + e.Item.Padding.Left;
                int right = row.Right - e.Item.Padding.Right;
                if (right <= left || row.Height < 1) return;
                var bounds = new Rectangle(left, 0, right - left, e.Item.Height);
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, bounds, AppTheme.Foreground,
                    InputLook.TextFlags | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
            }
        }
    }
}
