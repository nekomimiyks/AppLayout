using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppLayout
{
    /// Windows のシェル配色に追従するトレイメニュー。
    public class TrayMenu : ContextMenuStrip
    {
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint x, out uint y);
        [DllImport("user32.dll")] static extern bool GetPhysicalCursorPos(out Point point);

        float menuScale = 1f;
        Font menuFont;
        bool updatingMetrics;
        int itemWidth, rowHeight;
        Point openingPoint;
        Point? requestedPoint;
        bool anchored;

        internal float DpiScale { get { return menuScale; } }

        public TrayMenu()
        {
            AccessibleName = L.T("AppLayout のメニュー");
            ShowImageMargin = false;
            ShowCheckMargin = false;
            DropShadowEnabled = true;
            AutoSize = true;
            Renderer = new TrayMenuRenderer();
            AppTheme.Changed += ThemeChanged;
            UiTypography.Changed += TypographyChanged;
            ApplyTheme();
            ApplyMetrics(CursorScale());
        }

        protected override void OnOpening(CancelEventArgs e)
        {
            // NotifyIcon側の初期位置や前回のBoundsからモニタを推測しない。
            // マルチDPI環境でも右クリック地点の物理座標を開く時点で固定する。
            openingPoint = requestedPoint ?? PhysicalCursorPosition();
            requestedPoint = null;
            anchored = true;
            AppTheme.RefreshFromWindows();
            UiTypography.RefreshFromWindows();
            ApplyTheme();
            ApplyMetrics(ScaleAt(openingPoint));
            base.OnOpening(e);
        }

        protected override void OnOpened(EventArgs e)
        {
            // 標準の配置処理に加え、作業領域の端に置かれたタスクバーでも収める。
            ClampToWorkingArea();
            UpdateRegion();
            base.OnOpened(e);
        }

        void ClampToWorkingArea()
        {
            Rectangle area = WorkingArea();
            Location = Placement(openingPoint, Size, area).Location;
        }
        public void ShowAt(Point screenPoint)
        {
            requestedPoint = screenPoint;
            Show(screenPoint);
        }
        static Point PhysicalCursorPosition()
        {
            Point point;
            return GetPhysicalCursorPos(out point) ? point : Cursor.Position;
        }
        internal static Rectangle Placement(Point anchor, Size size, Rectangle area)
        {
            int width=Math.Min(size.Width,area.Width),height=Math.Min(size.Height,area.Height);
            int left=anchor.X,top=anchor.Y;
            if(left+width>area.Right)left=anchor.X-width;
            if(top+height>area.Bottom)top=anchor.Y-height;
            left=Math.Max(area.Left,Math.Min(left,area.Right-width));
            top=Math.Max(area.Top,Math.Min(top,area.Bottom-height));
            return new Rectangle(left,top,width,height);
        }

        protected override void OnItemAdded(ToolStripItemEventArgs e)
        {
            base.OnItemAdded(e);
            ApplyMetrics(menuScale);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
        }

        void ThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)ApplyTheme); }
                catch (InvalidOperationException) { }
                return;
            }
            ApplyTheme();
        }

        void ApplyTheme()
        {
            if (IsDisposed || Disposing) return;
            BackColor = AppTheme.MenuSurface;
            ForeColor = AppTheme.MenuForeground;
            foreach (ToolStripItem item in Items)
            {
                item.BackColor = BackColor;
                item.ForeColor = ForeColor;
            }
            Invalidate(true);
        }

        void TypographyChanged(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            Action update = delegate
            {
                if (IsDisposed || Disposing) return;
                ApplyMetrics(anchored ? ScaleAt(openingPoint) : CursorScale());
                if (Visible) ClampToWorkingArea();
                Invalidate(true);
            };
            if (InvokeRequired)
            {
                try { BeginInvoke(update); }
                catch (InvalidOperationException) { }
            }
            else update();
        }

        void ApplyMetrics(float scale)
        {
            if (updatingMetrics) return;
            updatingMetrics = true;
            SuspendLayout();
            try
            {
                menuScale = scale;
                Padding = new Padding(S(5));
                // Windowsのメニューフォントには文字拡大が含まれる。DPIはここで1回だけ適用する。
                var candidate = UiTypography.MenuFont(scale);
                if (menuFont != null && menuFont.Equals(candidate)) candidate.Dispose();
                else
                {
                    var oldFont = menuFont;
                    menuFont = candidate;
                    Font = menuFont;
                    if (oldFont != null) oldFont.Dispose();
                }

                Rectangle area = WorkingArea();
                int minWidth = Math.Min(S(210), area.Width);
                MinimumSize = Size.Empty;
                MaximumSize = area.Size;
                MinimumSize = new Size(minWidth, 0);
                itemWidth = Math.Max(1, minWidth - Padding.Horizontal);
                rowHeight = S(28);
                foreach (ToolStripItem item in Items)
                {
                    if (item is ToolStripSeparator) continue;
                    Size textSize = TextRenderer.MeasureText(item.Text, menuFont, Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    itemWidth = Math.Max(itemWidth, textSize.Width + S(24));
                    rowHeight = Math.Max(rowHeight, textSize.Height + S(8));
                }
                itemWidth = Math.Min(itemWidth, Math.Max(1, area.Width - Padding.Horizontal - 2));
                foreach (ToolStripItem item in Items) StyleItem(item);
            }
            finally { ResumeLayout(true); updatingMetrics = false; }
            UpdateRegion();
        }

        void StyleItem(ToolStripItem item)
        {
            item.AutoSize = false;
            item.Margin = Padding.Empty;
            item.Padding = new Padding(S(12), 0, S(12), 0);
            item.Size = new Size(itemWidth, item is ToolStripSeparator ? S(8) : rowHeight);
            item.BackColor = AppTheme.MenuSurface;
            item.ForeColor = AppTheme.MenuForeground;
            item.TextAlign = ContentAlignment.MiddleLeft;
        }

        int S(int value) { return (int)Math.Round(value * menuScale); }

        static float CursorScale()
        {
            return ScaleAt(PhysicalCursorPosition());
        }

        Rectangle WorkingArea()
        {
            return Screen.FromPoint(anchored ? openingPoint : PhysicalCursorPosition()).WorkingArea;
        }

        static float ScaleAt(Point point)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out x, out y) == 0 && x >= 48 && x <= 768)
                    return x / 96f;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return Ui.K;
        }

        void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (var path = RoundedRectangle(new RectangleF(0, 0, Width, Height), S(8)))
            {
                var old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0) return path;
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AppTheme.Changed -= ThemeChanged;
                UiTypography.Changed -= TypographyChanged;
                if (menuFont != null) { menuFont.Dispose(); menuFont = null; }
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(AppTheme.MenuSurface))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            float scale = ScaleOf(e.ToolStrip);
            var state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = TrayMenu.RoundedRectangle(new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 8 * scale))
                using (var pen = new Pen(AppTheme.MenuBorder)) e.Graphics.DrawPath(pen, path);
            }
            finally { e.Graphics.Restore(state); }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            float scale = ScaleOf(e.ToolStrip);
            var rect = new RectangleF(4 * scale, 2 * scale, Math.Max(0, e.Item.Width - 8 * scale), Math.Max(0, e.Item.Height - 4 * scale));
            var state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = TrayMenu.RoundedRectangle(rect, 5 * scale))
                using (var brush = new SolidBrush(AppTheme.MenuHover)) e.Graphics.FillPath(brush, path);
            }
            finally { e.Graphics.Restore(state); }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            float scale = ScaleOf(e.ToolStrip);
            int inset = (int)Math.Round(12 * scale);
            var bounds = new Rectangle(inset, 0, Math.Max(0, e.Item.Width - 2 * inset), e.Item.Height);
            Color foreground = e.Item.Enabled ? AppTheme.MenuForeground : AppTheme.MenuMuted;
            // .NET Frameworkのメニューは標準TextRectangleが上寄せになるため行全体で位置を決める。
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, bounds, foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int inset = e.Item.Padding.Left;
            int y = e.Item.Height / 2;
            using (var pen = new Pen(AppTheme.MenuBorder)) e.Graphics.DrawLine(pen, inset, y, Math.Max(inset, e.Item.Width - inset), y);
        }

        static float ScaleOf(ToolStrip owner)
        {
            var menu = owner as TrayMenu;
            return menu == null ? 1f : menu.DpiScale;
        }
    }
}
