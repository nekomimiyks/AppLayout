using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
namespace AppLayout {
 public static class UiFactory { public static Form CreateMainForm(AppController c) { return new BasicForm(c); } }
 class BasicForm : Form {
  readonly AppController controller; readonly DataGridView grid; readonly Label message; bool refreshing; bool resetSelection = true; HashSet<string> beforeClick = new HashSet<string>();
  public BasicForm(AppController c) {
   controller = c; Text = "AppLayout - 動作確認"; ClientSize = Ui.Sz(1200,600); MinimumSize = Ui.Sz(700,350);
   var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(Ui.S(8)) }; root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
   var checks = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; root.Controls.Add(checks,0,0); var tips = new ToolTip();
   var enabled = new CheckBox { Text = Texts.Enabled, AutoSize = true, Checked = c.Enabled }; var auto = new CheckBox { Text = Texts.AutoStart, AutoSize = true, Checked = c.AutoStart }; var launch = new CheckBox { Text = Texts.LaunchMissing, AutoSize = true, Checked = c.LaunchMissing }; var close = new CheckBox { Text = Texts.MinimizeToTrayOnClose, AutoSize = true, Checked = c.MinimizeToTrayOnClose };
   checks.Controls.AddRange(new Control[] { enabled, auto, launch, close }); tips.SetToolTip(enabled,Texts.EnabledTip); tips.SetToolTip(auto,Texts.AutoStartTip); tips.SetToolTip(launch,Texts.LaunchMissingTip); tips.SetToolTip(close,Texts.MinimizeToTrayOnCloseTip);
   enabled.CheckedChanged += delegate { c.Enabled = enabled.Checked; }; auto.CheckedChanged += delegate { c.AutoStart = auto.Checked; }; launch.CheckedChanged += delegate { c.LaunchMissing = launch.Checked; }; close.CheckedChanged += delegate { c.MinimizeToTrayOnClose = close.Checked; };
   grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells };
   foreach (string s in new string[] { Texts.Name,Texts.Process,Texts.Position,Texts.Size,Texts.Monitor,Texts.TitleRule,Texts.Maximized,Texts.Status }) grid.Columns.Add(s,s);
   grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = Texts.Edit, Text = Texts.Edit, UseColumnTextForButtonValue = true }); root.Controls.Add(grid,0,1);
   grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e) { beforeClick.Clear(); foreach (DataGridViewRow r in grid.SelectedRows) beforeClick.Add(((WindowRow)r.Tag).Key); };
   grid.CellMouseUp += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.RowIndex < 0 || e.ColumnIndex == 8) return; string key = ((WindowRow)grid.Rows[e.RowIndex].Tag).Key; if (!beforeClick.Remove(key)) beforeClick.Add(key); grid.ClearSelection(); foreach (DataGridViewRow r in grid.Rows) r.Selected = beforeClick.Contains(((WindowRow)r.Tag).Key); };
   grid.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex == 8) { if (controller.Edit((WindowRow)grid.Rows[e.RowIndex].Tag,this)) { resetSelection = true; RefreshRows(); } } };
   var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; root.Controls.Add(buttons,0,2);
   var save = new Button { Text = Texts.Save, AutoSize = true }; var unsave = new Button { Text = Texts.Unsave, AutoSize = true }; var restore = new Button { Text = Texts.Restore, AutoSize = true }; buttons.Controls.AddRange(new Control[] { save,unsave,restore });
   save.Click += delegate { controller.SaveCurrent(Selected(),this); resetSelection = true; RefreshRows(); }; unsave.Click += delegate { controller.Unsave(Selected()); resetSelection = true; RefreshRows(); }; restore.Click += delegate { controller.RestoreNow(Selected()); resetSelection = true; RefreshRows(); };
   message = new Label { AutoSize = true }; root.Controls.Add(message,0,3); controller.RowsChanged += delegate { RefreshRows(); }; RefreshRows();
  }
  List<WindowRow> Selected() { var rows = new List<WindowRow>(); foreach (DataGridViewRow r in grid.SelectedRows) rows.Add((WindowRow)r.Tag); return rows; }
  void RefreshRows() { if (refreshing) return; refreshing = true; var selected = new HashSet<string>(); foreach (DataGridViewRow r in grid.SelectedRows) if (r.Tag != null) selected.Add(((WindowRow)r.Tag).Key); var rows = controller.GetRows(); grid.Rows.Clear(); foreach (var row in rows) { int i = grid.Rows.Add(row.Name,row.Process,row.Position,row.Size,row.Monitor,row.TitleRule,row.Maximized,row.Status); grid.Rows[i].Tag = row; } grid.ClearSelection(); foreach (DataGridViewRow r in grid.Rows) { var row = (WindowRow)r.Tag; r.Selected = resetSelection ? row.Saved && row.StatusKind == 0 : selected.Contains(row.Key); } resetSelection = false; message.Text = controller.LastMessage; refreshing = false; }
 }
}
