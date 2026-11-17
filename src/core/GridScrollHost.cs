using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AppLayout
{
    public sealed class GridScrollHost : UserControl
    {
        public readonly DataGridView Grid;
        readonly ThemedScrollBar vertical = new ThemedScrollBar();
        readonly ThemedScrollBar horizontal = new ThemedScrollBar { Orientation = Orientation.Horizontal };
        readonly List<int> scrollRows = new List<int>();
        bool syncing, verticalNeeded, horizontalNeeded, syncPending;
        int updateDepth;
        public bool HorizontalScrollVisible { get { return horizontal.Visible; } }
        public int ScrollBarThickness { get { return horizontal.PreferredThickness; } }
        public GridScrollHost(DataGridView grid)
        {
            if (grid == null) throw new ArgumentNullException("grid");
            Grid = grid; Name = "GridScrollHost"; Margin = Padding.Empty;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = AppTheme.ScrollTrack;
            Grid.ScrollBars = ScrollBars.None; Grid.Dock = DockStyle.None;
            Grid.MinimumSize = Size.Empty;
            vertical.Name = "GridVerticalScroll"; horizontal.Name = "GridHorizontalScroll";
            Controls.Add(Grid); Controls.Add(vertical); Controls.Add(horizontal);
            vertical.ValueChanged += VerticalChanged; horizontal.ValueChanged += HorizontalChanged;
            Grid.RowsAdded += RowsAdded; Grid.RowsRemoved += RowsRemoved;
            Grid.RowHeightChanged += RowHeightChanged; Grid.RowStateChanged += RowStateChanged;
            Grid.ColumnWidthChanged += ColumnChanged; Grid.ColumnAdded += ColumnChanged; Grid.ColumnRemoved += ColumnChanged;
            Grid.ColumnStateChanged += ColumnStateChanged;
            Grid.ColumnHeadersHeightChanged += GridChanged; Grid.RowHeadersWidthChanged += GridChanged;
            Grid.Scroll += GridScrolled; Grid.SizeChanged += GridChanged;
            Grid.MouseWheel += GridWheel;
            AppTheme.Changed += ThemeChanged;
        }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); SyncScrollBars(); }
        public override Size GetPreferredSize(Size proposedSize)
        {
            // 表の現在のBoundsを外側のAutoScrollへ必要高さとして戻すと、領域が自己拡大する。
            return MinimumSize;
        }
        internal void UpdateGrid(Action update)
        {
            updateDepth++;
            try { update(); }
            finally
            {
                if (--updateDepth == 0 && syncPending) SyncScrollBars();
            }
        }
        public void SyncScrollBars()
        {
            if (syncing || IsDisposed || Grid == null || Grid.IsDisposed) return;
            if (updateDepth > 0) { syncPending = true; return; }
            syncPending = false;
            syncing = true;
            try
            {
                Grid.ScrollBars = ScrollBars.None;
                int thickness = vertical.PreferredThickness;
                bool needVertical = verticalNeeded, needHorizontal = horizontalNeeded;
                int contentHeight = Grid.Rows.GetRowsHeight(DataGridViewElementStates.Visible);
                // 一方のバーで表示領域が減ることを含めて、レイアウトを収束させる。
                for (int pass = 0; pass < 3; pass++)
                {
                    int width = Math.Max(0, ClientSize.Width - (needVertical ? thickness : 0));
                    int height = Math.Max(0, ClientSize.Height - (needHorizontal ? thickness : 0));
                    Grid.Bounds = new Rectangle(0, 0, width, height);
                    bool nextHorizontal = Grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) > ViewWidth();
                    bool nextVertical = contentHeight > ViewHeight();
                    if (nextHorizontal == needHorizontal && nextVertical == needVertical) break;
                    needHorizontal = nextHorizontal; needVertical = nextVertical;
                }
                Grid.Bounds = new Rectangle(0, 0, Math.Max(0, ClientSize.Width - (needVertical ? thickness : 0)),
                    Math.Max(0, ClientSize.Height - (needHorizontal ? thickness : 0)));
                vertical.Bounds = new Rectangle(Grid.Right, 0, thickness, Grid.Height);
                horizontal.Bounds = new Rectangle(0, Grid.Bottom, Grid.Width, thickness);
                verticalNeeded = needVertical; horizontalNeeded = needHorizontal;
                vertical.Visible = needVertical; horizontal.Visible = needHorizontal;

                scrollRows.Clear();
                int available = ViewHeight();
                foreach (DataGridViewRow row in Grid.Rows)
                {
                    if (!row.Visible) continue;
                    if (row.Frozen) available -= row.Height; else scrollRows.Add(row.Index);
                }
                available = Math.Max(1, available);
                int lastPage = LastPageStart(Grid, scrollRows, available);
                int first = Grid.FirstDisplayedScrollingRowIndex;
                int position = Math.Max(0, scrollRows.IndexOf(first));
                int pageRows = 0, used = 0;
                for (int i = position; i < scrollRows.Count; i++)
                {
                    used += Grid.Rows[scrollRows[i]].Height;
                    if (used > available) break;
                    pageRows++;
                }
                vertical.SetRange(0, lastPage, Math.Max(1, pageRows), 1);
                vertical.Value = position;
                int widthTotal = Grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible);
                horizontal.SetRange(0, Math.Max(0, widthTotal - ViewWidth()), Math.Max(1, ViewWidth()), Math.Max(1, thickness));
                horizontal.Value = Grid.HorizontalScrollingOffset;
            }
            finally { syncing = false; }
        }
        internal static int LastPageStart(DataGridView grid, IList<int> visibleRows, int availableHeight)
        {
            if (visibleRows.Count == 0) return 0;
            int start = visibleRows.Count - 1;
            long used = grid.Rows[visibleRows[start]].Height;
            while (start > 0 && used + grid.Rows[visibleRows[start - 1]].Height <= availableHeight)
                used += grid.Rows[visibleRows[--start]].Height;
            return start;
        }
        int ViewWidth()
        {
            int border = Grid.BorderStyle == BorderStyle.None ? 0 : 2;
            return Math.Max(1, Grid.ClientSize.Width - (Grid.RowHeadersVisible ? Grid.RowHeadersWidth : 0) - border);
        }
        int ViewHeight()
        {
            int border = Grid.BorderStyle == BorderStyle.None ? 0 : 2;
            return Math.Max(1, Grid.ClientSize.Height - (Grid.ColumnHeadersVisible ? Grid.ColumnHeadersHeight : 0) - border);
        }
        void VerticalChanged(object sender, EventArgs e)
        {
            if (syncing || scrollRows.Count == 0) return;
            int index = scrollRows[Math.Min(scrollRows.Count - 1, vertical.Value)];
            if (Grid.FirstDisplayedScrollingRowIndex != index) Grid.FirstDisplayedScrollingRowIndex = index;
            SyncScrollBars();
        }
        void HorizontalChanged(object sender, EventArgs e)
        {
            if (syncing) return;
            Grid.HorizontalScrollingOffset = horizontal.Value;
            SyncScrollBars();
        }
        public void ScrollWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Shift) == Keys.Shift || !vertical.Visible) horizontal.ScrollWheel(e.Delta);
            else vertical.ScrollWheel(e.Delta);
            var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true;
        }
        void GridWheel(object sender, MouseEventArgs e) { ScrollWheel(e); }
        void GridChanged(object sender, EventArgs e) { SyncScrollBars(); }
        void GridScrolled(object sender, ScrollEventArgs e) { SyncScrollBars(); }
        void RowsAdded(object sender, DataGridViewRowsAddedEventArgs e) { SyncScrollBars(); }
        void RowsRemoved(object sender, DataGridViewRowsRemovedEventArgs e) { SyncScrollBars(); }
        void RowHeightChanged(object sender, DataGridViewRowEventArgs e) { SyncScrollBars(); }
        void RowStateChanged(object sender, DataGridViewRowStateChangedEventArgs e) { if ((e.StateChanged & (DataGridViewElementStates.Visible | DataGridViewElementStates.Frozen)) != 0) SyncScrollBars(); }
        void ColumnChanged(object sender, DataGridViewColumnEventArgs e) { SyncScrollBars(); }
        void ColumnStateChanged(object sender, DataGridViewColumnStateChangedEventArgs e) { if ((e.StateChanged & (DataGridViewElementStates.Visible | DataGridViewElementStates.Frozen)) != 0) SyncScrollBars(); }
        void ThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { try { BeginInvoke((Action)ApplyTheme); } catch (InvalidOperationException) { } }
            else ApplyTheme();
        }
        void ApplyTheme() { BackColor = AppTheme.ScrollTrack; Invalidate(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AppTheme.Changed -= ThemeChanged;
                Grid.RowsAdded -= RowsAdded; Grid.RowsRemoved -= RowsRemoved; Grid.RowHeightChanged -= RowHeightChanged;
                Grid.RowStateChanged -= RowStateChanged; Grid.ColumnWidthChanged -= ColumnChanged;
                Grid.ColumnAdded -= ColumnChanged; Grid.ColumnRemoved -= ColumnChanged; Grid.ColumnStateChanged -= ColumnStateChanged;
                Grid.ColumnHeadersHeightChanged -= GridChanged; Grid.RowHeadersWidthChanged -= GridChanged;
                Grid.Scroll -= GridScrolled; Grid.SizeChanged -= GridChanged; Grid.MouseWheel -= GridWheel;
            }
            base.Dispose(disposing);
        }
    }
}
