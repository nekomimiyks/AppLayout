using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AppLayout {
 public static class UiFactory {
  public static Form CreateMainForm(AppController c) { return new StandardForm(c); }
 }
 class StandardForm : Form {
  readonly AppController controller;
  readonly DataGridView grid;
  readonly ToolStripStatusLabel message;
  readonly ToolTip tips;
  HashSet<string> beforeClick = new HashSet<string>();
  bool initial = true;

  public StandardForm(AppController c) {
   controller = c;
   Text = "AppLayout";
   AutoScaleMode = AutoScaleMode.None;
   ClientSize = Ui.Sz(1250, 600);
   MinimumSize = Ui.Sz(900, 350);
   var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(Ui.S(8)) };
   root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
   root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
   root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
   root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   Controls.Add(root);
   var checks = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
   root.Controls.Add(checks, 0, 0); root.SetColumnSpan(checks, 2);
   tips = new ToolTip();
   var enabled = new CheckBox { Text = Texts.Enabled, AutoSize = true, Checked = c.Enabled };
   var auto = new CheckBox { Text = Texts.AutoStart, AutoSize = true, Checked = c.AutoStart };
   var launch = new CheckBox { Text = Texts.LaunchMissing, AutoSize = true, Checked = c.LaunchMissing };
   var close = new CheckBox { Text = Texts.MinimizeToTrayOnClose, AutoSize = true, Checked = c.MinimizeToTrayOnClose };
   checks.Controls.AddRange(new Control[] { enabled, auto, launch, close });
   tips.SetToolTip(enabled, Texts.EnabledTip); tips.SetToolTip(auto, Texts.AutoStartTip);
   tips.SetToolTip(launch, Texts.LaunchMissingTip); tips.SetToolTip(close, Texts.MinimizeToTrayOnCloseTip);
   enabled.CheckedChanged += delegate { c.Enabled = enabled.Checked; };
   auto.CheckedChanged += delegate { c.AutoStart = auto.Checked; };
   launch.CheckedChanged += delegate { c.LaunchMissing = launch.Checked; };
   close.CheckedChanged += delegate { c.MinimizeToTrayOnClose = close.Checked; };
   grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false, ReadOnly = true, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None };
   string[] headers = { Texts.Name, Texts.Process, Texts.Position, Texts.Size, Texts.Monitor, Texts.TitleRule, Texts.Maximized, Texts.Status };
   int[] widths = { 170, 125, 95, 110, 60, 140, 60, 100 };
   for (int i = 0; i < headers.Length; i++) {
    var col = new DataGridViewTextBoxColumn { HeaderText = headers[i], Width = Ui.S(widths[i]), SortMode = DataGridViewColumnSortMode.NotSortable };
    grid.Columns.Add(col);
   }
   grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = Texts.Edit, Text = Texts.Edit, UseColumnTextForButtonValue = true, Width = Ui.S(65), SortMode = DataGridViewColumnSortMode.NotSortable });
   root.Controls.Add(grid, 0, 1);
   grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e) {
    beforeClick.Clear();
    foreach (DataGridViewRow r in grid.SelectedRows) beforeClick.Add(((WindowRow)r.Tag).Key);
   };
   grid.CellMouseUp += delegate(object sender, DataGridViewCellMouseEventArgs e) {
    if (e.RowIndex < 0 || e.Button != MouseButtons.Left) return;
    string key = ((WindowRow)grid.Rows[e.RowIndex].Tag).Key;
    if (e.ColumnIndex != 8) { if (!beforeClick.Remove(key)) beforeClick.Add(key); }
    grid.ClearSelection();
    foreach (DataGridViewRow r in grid.Rows) r.Selected = beforeClick.Contains(((WindowRow)r.Tag).Key);
   };
   grid.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e) {
    if (e.RowIndex >= 0 && e.ColumnIndex == 8) controller.Edit((WindowRow)grid.Rows[e.RowIndex].Tag, this);
   };
   var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(Ui.S(6), 0, 0, 0) };
   root.Controls.Add(buttons, 1, 1);
   var save = new Button { Text = Texts.Save, Size = Ui.Sz(150, 32) };
   var unsave = new Button { Text = Texts.Unsave, Size = Ui.Sz(150, 32) };
   var restore = new Button { Text = Texts.Restore, Size = Ui.Sz(150, 32) };
   buttons.Controls.AddRange(new Control[] { save, unsave, restore });
   save.Click += delegate { controller.SaveCurrent(Selected(), this); SelectSaved(); };
   unsave.Click += delegate { controller.Unsave(Selected()); SelectSaved(); };
   restore.Click += delegate { controller.RestoreNow(Selected()); SelectSaved(); };
   var status = new StatusStrip(); message = new ToolStripStatusLabel(); status.Items.Add(message);
   root.Controls.Add(status, 0, 2); root.SetColumnSpan(status, 2);
   controller.RowsChanged += delegate { RefreshRows(); };
   RefreshRows();
  }
  List<WindowRow> Selected() {
   var result = new List<WindowRow>();
   foreach (DataGridViewRow r in grid.SelectedRows) result.Add((WindowRow)r.Tag);
   return result;
  }
  void SelectSaved() {
   grid.ClearSelection();
   foreach (DataGridViewRow r in grid.Rows) r.Selected = ((WindowRow)r.Tag).Saved;
  }
  void RefreshRows() {
   var selected = new HashSet<string>();
   foreach (DataGridViewRow r in grid.SelectedRows) selected.Add(((WindowRow)r.Tag).Key);
   int first = grid.FirstDisplayedScrollingRowIndex;
   int horizontal = grid.HorizontalScrollingOffset;
   var incoming = controller.GetRows();
   var remaining = new Dictionary<string, WindowRow>();
   foreach (var row in incoming) remaining[row.Key] = row;
   for (int i = grid.Rows.Count - 1; i >= 0; i--) {
    var key = ((WindowRow)grid.Rows[i].Tag).Key;
    if (!remaining.ContainsKey(key)) grid.Rows.RemoveAt(i);
   }
   var existing = new Dictionary<string, DataGridViewRow>();
   foreach (DataGridViewRow r in grid.Rows) existing[((WindowRow)r.Tag).Key] = r;
   foreach (var row in incoming) {
    DataGridViewRow r;
    if (!existing.TryGetValue(row.Key, out r)) { int i = grid.Rows.Add(); r = grid.Rows[i]; }
    r.Tag = row;
    string[] values = { row.Name, row.Process, row.Position, row.Size, row.Monitor, row.TitleRule, row.Maximized, row.Status };
    for (int i = 0; i < values.Length; i++) if (!object.Equals(r.Cells[i].Value, values[i])) r.Cells[i].Value = values[i];
    r.Cells[7].Style.ForeColor = row.StatusKind == 0 ? Color.ForestGreen : row.StatusKind == 1 ? Color.DarkOrange : row.StatusKind == 2 ? SystemColors.GrayText : SystemColors.ControlText;
   }
   grid.ClearSelection();
   foreach (DataGridViewRow r in grid.Rows) { var row = (WindowRow)r.Tag; r.Selected = initial ? row.Saved : selected.Contains(row.Key); }
   initial = false;
   if (first >= 0 && grid.Rows.Count > 0) grid.FirstDisplayedScrollingRowIndex = Math.Min(first, grid.Rows.Count - 1);
   grid.HorizontalScrollingOffset = horizontal;
   message.Text = controller.LastMessage;
  }
  protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
 }
}
