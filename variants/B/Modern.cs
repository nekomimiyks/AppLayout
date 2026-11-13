using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppLayout {
 public static class UiFactory { public static Form CreateMainForm(AppController c) { return new ModernForm(c); } }

 class SwitchBox : CheckBox {
  public SwitchBox() { AutoSize = true; MinimumSize = Ui.Sz(0, 30); SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
  public override Size GetPreferredSize(Size proposed) { Size s = TextRenderer.MeasureText(Text, Font); return new Size(s.Width + Ui.S(48), Math.Max(s.Height, Ui.S(30))); }
  protected override void OnPaint(PaintEventArgs e) {
   e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
   int x = Ui.S(1), y = (Height - Ui.S(18)) / 2, w = Ui.S(32), h = Ui.S(18);
   using (var p = new GraphicsPath()) { p.AddArc(x,y,h,h,90,180); p.AddArc(x+w-h,y,h,h,270,180); p.CloseFigure(); using (var b = new SolidBrush(Checked ? Color.FromArgb(37,99,235) : Color.FromArgb(185,194,207))) e.Graphics.FillPath(b,p); }
   using (var b = new SolidBrush(Color.White)) e.Graphics.FillEllipse(b, x + (Checked ? w-h : 0) + Ui.S(2), y + Ui.S(2), h-Ui.S(4), h-Ui.S(4));
   TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Ui.S(40),0,Width-Ui.S(40),Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
   if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
  }
 }
 class ModernForm : Form {
  static readonly Color Blue = Color.FromArgb(37,99,235), Ink = Color.FromArgb(35,48,68), Line = Color.FromArgb(230,234,240), SelectedColor = Color.FromArgb(235,242,255);
  readonly AppController controller; readonly DataGridView grid; readonly Label message; readonly HashSet<string> selected = new HashSet<string>();
  bool resetting = true, refreshing; string pressedKey;
  public ModernForm(AppController c) {
   controller = c; Text = "AppLayout"; Font = new Font("Yu Gothic UI",9f); ForeColor = Ink; BackColor = Color.FromArgb(245,247,250); AutoScaleMode = AutoScaleMode.None;
   ClientSize = Ui.Sz(1500,700); MinimumSize = Ui.Sz(850,450); StartPosition = FormStartPosition.CenterScreen;
   var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.S(20)) };
   root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
   var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new Padding(0,0,0,Ui.S(16)) };
   header.Controls.Add(new Label { Text = "AppLayout", Font = new Font("Segoe UI",18f,FontStyle.Bold), ForeColor = Ink, AutoSize = true, Margin = new Padding(0,0,0,Ui.S(8)) },0,0);
   var settings = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) }; header.Controls.Add(settings,0,1); root.Controls.Add(header,0,0);
   var tips = new ToolTip();
   var enabled = new SwitchBox { Text = Texts.Enabled, Checked = c.Enabled }; var auto = new SwitchBox { Text = Texts.AutoStart, Checked = c.AutoStart }; var launch = new SwitchBox { Text = Texts.LaunchMissing, Checked = c.LaunchMissing }; var close = new SwitchBox { Text = Texts.MinimizeToTrayOnClose, Checked = c.MinimizeToTrayOnClose };
   foreach (Control item in new Control[] { enabled,auto,launch,close }) { item.BackColor = Color.FromArgb(245,247,250); item.Margin = new Padding(0,0,Ui.S(20),Ui.S(4)); settings.Controls.Add(item); }
   tips.SetToolTip(enabled,Texts.EnabledTip); tips.SetToolTip(auto,Texts.AutoStartTip); tips.SetToolTip(launch,Texts.LaunchMissingTip); tips.SetToolTip(close,Texts.MinimizeToTrayOnCloseTip);
   enabled.CheckedChanged += delegate { c.Enabled = enabled.Checked; }; auto.CheckedChanged += delegate { c.AutoStart = auto.Checked; }; launch.CheckedChanged += delegate { c.LaunchMissing = launch.Checked; }; close.CheckedChanged += delegate { c.MinimizeToTrayOnClose = close.Checked; };
   var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) }; body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Ui.S(205))); root.Controls.Add(body,0,1);
   grid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, ReadOnly = true, RowHeadersVisible = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Line, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, ColumnHeadersHeight = Ui.S(42), RowTemplate = { Height = Ui.S(52) }, Margin = new Padding(0), AutoGenerateColumns = false };
   grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248,250,253); grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(100,116,139); grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font,FontStyle.Bold); grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(Ui.S(6),0,Ui.S(6),0);
   grid.DefaultCellStyle.BackColor = Color.White; grid.DefaultCellStyle.ForeColor = Ink; grid.DefaultCellStyle.SelectionBackColor = SelectedColor; grid.DefaultCellStyle.SelectionForeColor = Ink; grid.DefaultCellStyle.Padding = new Padding(Ui.S(6),0,Ui.S(6),0);
   string[] names = { Texts.Name,Texts.Process,Texts.Position,Texts.Size,Texts.Monitor,Texts.TitleRule,Texts.Maximized,Texts.Status,Texts.Edit }; int[] widths = { 180,145,105,115,75,165,75,130,70 };
   for (int i=0; i<names.Length; i++) { var col = new DataGridViewTextBoxColumn { HeaderText = names[i], Width = Ui.S(widths[i]), SortMode = DataGridViewColumnSortMode.NotSortable }; grid.Columns.Add(col); }
   body.Controls.Add(grid,0,0);
   var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Ui.S(16),0,0,0), Margin = new Padding(0) }; body.Controls.Add(actions,1,0);
   var save = ActionButton(Texts.Save,true); var unsave = ActionButton(Texts.Unsave,false); var restore = ActionButton(Texts.Restore,false); actions.Controls.AddRange(new Control[] { save,unsave,restore });
   save.Click += delegate { controller.SaveCurrent(SelectedRows(),this); resetting = true; RefreshRows(); }; unsave.Click += delegate { controller.Unsave(SelectedRows()); resetting = true; RefreshRows(); }; restore.Click += delegate { controller.RestoreNow(SelectedRows()); resetting = true; RefreshRows(); };
   message = new Label { AutoSize = false, Dock = DockStyle.Fill, Height = Ui.S(34), ForeColor = Color.FromArgb(100,116,139), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }; root.Controls.Add(message,0,2);
   grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e) { pressedKey = e.RowIndex >= 0 ? ((WindowRow)grid.Rows[e.RowIndex].Tag).Key : null; };
   grid.CellMouseUp += delegate(object sender, DataGridViewCellMouseEventArgs e) {
    if (e.RowIndex < 0 || pressedKey == null) return;
    var row = (WindowRow)grid.Rows[e.RowIndex].Tag; if (row.Key != pressedKey) return;
    if (e.ColumnIndex == 8) { ApplySelection(); if (controller.Edit(row,this)) { resetting = true; RefreshRows(); } }
    else { if (!selected.Remove(row.Key)) selected.Add(row.Key); ApplySelection(); }
    pressedKey = null;
   };
   grid.CellPainting += PaintCell;
   c.RowsChanged += delegate { RefreshRows(); };
   RefreshRows();
  }
  Button ActionButton(string text, bool primary) { var b = new Button { Text = text, Size = Ui.Sz(185,48), FlatStyle = FlatStyle.Flat, BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Blue, Margin = new Padding(0,0,0,Ui.S(12)), Cursor = Cursors.Hand }; b.FlatAppearance.BorderSize = primary ? 0 : 1; b.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(170,190,220); return b; }
  List<WindowRow> SelectedRows() { var result = new List<WindowRow>(); foreach (DataGridViewRow r in grid.Rows) { var row = (WindowRow)r.Tag; if (selected.Contains(row.Key)) result.Add(row); } return result; }
  void ApplySelection() { grid.ClearSelection(); foreach (DataGridViewRow r in grid.Rows) r.Selected = selected.Contains(((WindowRow)r.Tag).Key); grid.Invalidate(); }
  void RefreshRows() {
   if (refreshing) return; refreshing = true;
   var rows = controller.GetRows(); var incoming = new Dictionary<string,WindowRow>(); foreach (var row in rows) incoming[row.Key] = row;
   int first = grid.FirstDisplayedScrollingRowIndex; string firstKey = first >= 0 ? ((WindowRow)grid.Rows[first].Tag).Key : null; int offset = grid.HorizontalScrollingOffset;
   for (int i=grid.Rows.Count-1; i>=0; i--) { var old = (WindowRow)grid.Rows[i].Tag; if (!incoming.ContainsKey(old.Key)) { grid.Rows.RemoveAt(i); selected.Remove(old.Key); } }
   var existing = new Dictionary<string,DataGridViewRow>(); foreach (DataGridViewRow r in grid.Rows) existing[((WindowRow)r.Tag).Key] = r;
   if (resetting) selected.Clear();
   foreach (var row in rows) {
    DataGridViewRow r; if (!existing.TryGetValue(row.Key,out r)) { int index = grid.Rows.Add(); r = grid.Rows[index]; }
    r.Tag = row; string[] values = { row.Name,row.Process,row.Position,row.Size,row.Monitor,row.TitleRule,row.Maximized,row.Status,Texts.Edit };
    for (int i=0; i<values.Length; i++) if (!object.Equals(r.Cells[i].Value,values[i])) r.Cells[i].Value = values[i];
    if (resetting && row.Saved) selected.Add(row.Key);
   }
   resetting = false; ApplySelection();
   if (firstKey != null) foreach (DataGridViewRow r in grid.Rows) if (((WindowRow)r.Tag).Key == firstKey) { grid.FirstDisplayedScrollingRowIndex = r.Index; break; }
   grid.HorizontalScrollingOffset = offset; message.Text = controller.LastMessage; refreshing = false;
  }
  void PaintCell(object sender, DataGridViewCellPaintingEventArgs e) {
   if (e.RowIndex < 0) return;
   var row = (WindowRow)grid.Rows[e.RowIndex].Tag; bool isSelected = selected.Contains(row.Key);
   if (e.ColumnIndex == 7 || e.ColumnIndex == 8) {
    e.PaintBackground(e.CellBounds, true); e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
    if (e.ColumnIndex == 7) {
     Color bg = row.StatusKind == 0 ? Color.FromArgb(225,246,235) : row.StatusKind == 1 ? Color.FromArgb(255,239,216) : row.StatusKind == 2 ? Color.FromArgb(235,238,242) : Color.FromArgb(245,247,249);
     Color fg = row.StatusKind == 0 ? Color.FromArgb(25,130,75) : row.StatusKind == 1 ? Color.FromArgb(170,95,10) : Color.FromArgb(100,110,125);
     var rect = new Rectangle(e.CellBounds.X+Ui.S(5),e.CellBounds.Y+(e.CellBounds.Height-Ui.S(26))/2,e.CellBounds.Width-Ui.S(10),Ui.S(26));
     using (var path = new GraphicsPath()) { int d=rect.Height; path.AddArc(rect.X,rect.Y,d,d,90,180); path.AddArc(rect.Right-d,rect.Y,d,d,270,180); path.CloseFigure(); using (var brush = new SolidBrush(bg)) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.FillPath(brush,path); } }
     TextRenderer.DrawText(e.Graphics,row.Status,Font,rect,fg,TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    } else {
     var rect = new Rectangle(e.CellBounds.X+Ui.S(5),e.CellBounds.Y+(e.CellBounds.Height-Ui.S(28))/2,e.CellBounds.Width-Ui.S(10),Ui.S(28)); using (var pen = new Pen(Color.FromArgb(190,207,232))) e.Graphics.DrawRectangle(pen,rect); TextRenderer.DrawText(e.Graphics,Texts.Edit,Font,rect,Blue,TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    e.Handled = true;
   } else if (e.ColumnIndex == 0 && isSelected) { e.Paint(e.CellBounds, DataGridViewPaintParts.All); using (var brush = new SolidBrush(Blue)) e.Graphics.FillRectangle(brush,e.CellBounds.X,e.CellBounds.Y,Ui.S(4),e.CellBounds.Height-Ui.S(1)); e.Handled = true; }
  }
 }
}


