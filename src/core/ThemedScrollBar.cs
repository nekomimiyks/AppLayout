using System;
using System.Drawing;
using System.Windows.Forms;

namespace AppLayout
{
    // HWND の SCROLLBAR を使わず、描画は OnPaint の中だけで完結する。
    public sealed class ThemedScrollBar : Control
    {
        enum Part { None, Back, Forward, Thumb, Before, After }
        Orientation orientation = Orientation.Vertical;
        int minimum, maximum, value, largeChange = 10, smallChange = 1;
        int dragOffset, wheelRemainder;
        Part hover, pressed;
        readonly Timer repeat = new Timer();
        public event EventHandler ValueChanged;

        public ThemedScrollBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable | ControlStyles.Opaque, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.ScrollBar;
            AccessibleName = "縦スクロール";
            repeat.Tick += RepeatTick;
            AppTheme.Changed += ThemeChanged;
        }
        float ScaleFactor { get { var form = FindForm() as DpiAwareForm; return form == null ? Ui.K : form.UiScale; } }
        public int PreferredThickness { get { return Math.Max(12, (int)Math.Round(17 * ScaleFactor)); } }
        public Orientation Orientation
        {
            get { return orientation; }
            set { if (orientation == value) return; orientation = value; AccessibleName = value == Orientation.Vertical ? "縦スクロール" : "横スクロール"; Invalidate(); }
        }
        public int Minimum { get { return minimum; } set { SetRange(value, Math.Max(value, maximum), largeChange, smallChange); } }
        public int Maximum { get { return maximum; } set { SetRange(minimum, value, largeChange, smallChange); } }
        public int LargeChange { get { return largeChange; } set { SetRange(minimum, maximum, value, smallChange); } }
        public int SmallChange { get { return smallChange; } set { smallChange = Math.Max(1, value); } }
        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(minimum, Math.Min(maximum, value));
                if (this.value == next) return;
                this.value = next; Invalidate();
                var handler = ValueChanged; if (handler != null) handler(this, EventArgs.Empty);
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
        }
        public void SetRange(int minimum, int maximum, int largeChange, int smallChange)
        {
            minimum = Math.Max(0, minimum); maximum = Math.Max(minimum, maximum);
            largeChange = Math.Max(1, largeChange); smallChange = Math.Max(1, smallChange);
            bool repaint = this.minimum != minimum || this.maximum != maximum || this.largeChange != largeChange;
            this.minimum = minimum; this.maximum = maximum; this.largeChange = largeChange; this.smallChange = smallChange;
            Value = value;
            if (repaint) Invalidate();
        }
        int Length { get { return orientation == Orientation.Vertical ? Height : Width; } }
        int Breadth { get { return orientation == Orientation.Vertical ? Width : Height; } }
        int ArrowSize { get { return Math.Min(PreferredThickness, Math.Max(0, Length / 2)); } }
        Rectangle AxisRectangle(int start, int length)
        {
            return orientation == Orientation.Vertical ? new Rectangle(0, start, Width, length) : new Rectangle(start, 0, length, Height);
        }
        Rectangle ThumbBounds()
        {
            int track = Math.Max(0, Length - 2 * ArrowSize);
            if (track == 0) return Rectangle.Empty;
            double range = (double)maximum - minimum;
            int length = Math.Min(track, Math.Max(PreferredThickness, (int)Math.Round(track * largeChange / (range + largeChange))));
            int position = range <= 0 ? 0 : (int)Math.Round((track - length) * (value - (double)minimum) / range);
            return AxisRectangle(ArrowSize + position, length);
        }
        int Axis(Point point) { return orientation == Orientation.Vertical ? point.Y : point.X; }
        int Start(Rectangle rectangle) { return orientation == Orientation.Vertical ? rectangle.Top : rectangle.Left; }
        int Extent(Rectangle rectangle) { return orientation == Orientation.Vertical ? rectangle.Height : rectangle.Width; }
        Part Hit(Point point)
        {
            if (!ClientRectangle.Contains(point) || !Enabled || maximum == minimum) return Part.None;
            int position = Axis(point);
            if (position < ArrowSize) return Part.Back;
            if (position >= Length - ArrowSize) return Part.Forward;
            var thumb = ThumbBounds();
            if (thumb.Contains(point)) return Part.Thumb;
            return position < Start(thumb) ? Part.Before : Part.After;
        }
        void SetHover(Part next) { if (hover != next) { hover = next; Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(AppTheme.ScrollTrack);
            var thumb = ThumbBounds();
            if (maximum > minimum && !thumb.IsEmpty)
            {
                int inset = Math.Max(2, (int)Math.Round(3 * ScaleFactor));
                if (orientation == Orientation.Vertical) thumb.Inflate(-Math.Min(inset, Math.Max(0, Width / 2 - 1)), -1);
                else thumb.Inflate(-1, -Math.Min(inset, Math.Max(0, Height / 2 - 1)));
                Color color = pressed == Part.Thumb ? AppTheme.ScrollPressed : hover == Part.Thumb ? AppTheme.ScrollHover : AppTheme.ScrollThumb;
                using (var brush = new SolidBrush(color)) e.Graphics.FillRectangle(brush, thumb);
            }
            DrawArrow(e.Graphics, AxisRectangle(0, ArrowSize), false, Part.Back);
            DrawArrow(e.Graphics, AxisRectangle(Length - ArrowSize, ArrowSize), true, Part.Forward);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), AppTheme.Foreground, AppTheme.ScrollTrack);
            base.OnPaint(e);
        }
        void DrawArrow(Graphics graphics, Rectangle bounds, bool forward, Part part)
        {
            if (bounds.Width < 3 || bounds.Height < 3) return;
            if (hover == part || pressed == part)
                using (var brush = new SolidBrush(pressed == part ? AppTheme.ScrollPressed : AppTheme.ScrollHover))
                    graphics.FillRectangle(brush, bounds);
            int x = bounds.X + bounds.Width / 2, y = bounds.Y + bounds.Height / 2;
            int radius = Math.Max(2, Math.Min(bounds.Width, bounds.Height) / 5), direction = forward ? 1 : -1;
            Point[] points = orientation == Orientation.Vertical ?
                new[] { new Point(x - radius, y - direction * radius / 2), new Point(x, y + direction * radius / 2), new Point(x + radius, y - direction * radius / 2) } :
                new[] { new Point(x - direction * radius / 2, y - radius), new Point(x + direction * radius / 2, y), new Point(x - direction * radius / 2, y + radius) };
            using (var pen = new Pen(Enabled && maximum > minimum ? AppTheme.Foreground : AppTheme.Muted, Math.Max(1, ScaleFactor)))
                graphics.DrawLines(pen, points);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressed == Part.Thumb && Capture)
            {
                Rectangle thumb = ThumbBounds();
                int travel = Length - 2 * ArrowSize - Extent(thumb);
                if (travel > 0)
                {
                    double fraction = Math.Max(0, Math.Min(1, (Axis(e.Location) - dragOffset - ArrowSize) / (double)travel));
                    Value = (int)Math.Round(minimum + fraction * ((double)maximum - minimum));
                }
            }
            SetHover(Hit(e.Location));
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); SetHover(Part.None); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            pressed = Hit(e.Location);
            if (pressed == Part.None) return;
            Focus(); Capture = true; SetHover(pressed); Invalidate();
            if (pressed == Part.Thumb) dragOffset = Axis(e.Location) - Start(ThumbBounds());
            else { Step(pressed); repeat.Interval = 400; repeat.Start(); }
        }
        void Step(Part part)
        {
            long delta = part == Part.Back ? -smallChange : part == Part.Forward ? smallChange : part == Part.Before ? -largeChange : largeChange;
            Value = (int)Math.Max(minimum, Math.Min(maximum, value + delta));
        }
        void RepeatTick(object sender, EventArgs e)
        {
            repeat.Interval = 60;
            if (Capture && Hit(PointToClient(MousePosition)) == pressed) Step(pressed);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            EndPress(); Capture = false; SetHover(Hit(e.Location));
        }
        void EndPress() { repeat.Stop(); if (pressed != Part.None) { pressed = Part.None; Invalidate(); } }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) EndPress(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled) { EndPress(); Capture = false; } Invalidate(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) { EndPress(); Capture = false; } }
        public void ScrollWheel(int delta)
        {
            wheelRemainder += delta;
            int steps = wheelRemainder / SystemInformation.MouseWheelScrollDelta;
            wheelRemainder %= SystemInformation.MouseWheelScrollDelta;
            int lines = SystemInformation.MouseWheelScrollLines;
            long amount = lines < 0 ? largeChange : (long)smallChange * lines;
            Value = (int)Math.Max(minimum, Math.Min(maximum, value - steps * amount));
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollWheel(e.Delta);
            var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true;
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right ||
                key == Keys.PageUp || key == Keys.PageDown || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Up: case Keys.Left: Step(Part.Back); break;
                case Keys.Down: case Keys.Right: Step(Part.Forward); break;
                case Keys.PageUp: Step(Part.Before); break;
                case Keys.PageDown: Step(Part.After); break;
                case Keys.Home: Value = minimum; break;
                case Keys.End: Value = maximum; break;
                default: return;
            }
            e.Handled = true; e.SuppressKeyPress = true;
        }
        void ThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { try { BeginInvoke((Action)Invalidate); } catch (InvalidOperationException) { } }
            else Invalidate();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { repeat.Dispose(); AppTheme.Changed -= ThemeChanged; }
            base.Dispose(disposing);
        }
    }
}
