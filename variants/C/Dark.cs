using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppLayout {
 public static class UiFactory { public static Form CreateMainForm(AppController c) { return new DarkForm(c); } }
 class DarkForm : Form {
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
  [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);
  readonly AppController controller;
  readonly DataGridView grid;
  readonly Label message;
  readonly HashSet<string> selection = new HashSet<string>();
  readonly Color background = Color.FromArgb(27, 30, 36), panel = Color.FromArgb(35, 39, 47), foreground = Color.FromArgb(220, 225, 232), accent = Color.FromArgb(81, 210, 192);
  bool updating;
  public DarkForm(AppController c) {
   controller = c; Text = "AppLayout"; BackColor = background; ForeColor = foreground;
   AutoScaleMode = AutoScaleMode.None; ClientSize = Ui.Sz(1350, 560); MinimumSize = Ui.Sz(850, 360);
   Font = new Font("Yu Gothic UI", 9F);
   var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(Ui.S(12)), BackColor = background };
   root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(172)));
   root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(30))); Controls.Add(root);
   grid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = panel, BorderStyle = BorderStyle.None, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, ReadOnly = true, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, EnableHeadersVisualStyles = false, GridColor = Color.FromArgb(51, 57, 67), CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single, ScrollBars = ScrollBars.Both };
   grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; grid.ColumnHeadersHeight = Ui.S(32); grid.RowTemplate.Height = Ui.S(28);
   grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(43, 48, 58); grid.ColumnHeadersDefaultCellStyle.ForeColor = foreground; grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(43, 48, 58); grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
   grid.DefaultCellStyle.BackColor = panel; grid.DefaultCellStyle.ForeColor = foreground; grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(48, 62, 68); grid.DefaultCellStyle.SelectionForeColor = foreground; grid.DefaultCellStyle.Padding = new Padding(Ui.S(5), 0, Ui.S(3), 0);
   string[] headers = { Texts.Name, Texts.Process, Texts.Position, Texts.Size, Texts.Monitor, Texts.TitleRule, Texts.Maximized, Texts.Status };
   int[] widths = { 185, 135, 105, 115, 65, 145, 70, 110 };
   for (int i = 0; i < headers.Length; i++) { int n = grid.Columns.Add("col" + i, headers[i]); grid.Columns[n].Width = Ui.S(widths[i]); grid.Columns[n].SortMode = DataGridViewColumnSortMode.NotSortable; }
   grid.Columns[2].DefaultCellStyle.Font = new Font("Consolas", 9F); grid.Columns[3].DefaultCellStyle.Font = new Font("Consolas", 9F); grid.Columns[4].DefaultCellStyle.Font = new Font("Consolas", 9F);
   var edit = new DataGridViewButtonColumn { HeaderText = Texts.Edit, Text = "✎", UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat, Width = Ui.S(55), SortMode = DataGridViewColumnSortMode.NotSortable }; grid.Columns.Add(edit);
   root.Controls.Add(grid, 0, 0);
   grid.CellMouseDown += delegate(object s, DataGridViewCellMouseEventArgs e) { if (e.RowIndex < 0) return; if (e.ColumnIndex != 8) { string key = ((WindowRow)grid.Rows[e.RowIndex].Tag).Key; if (!selection.Remove(key)) selection.Add(key); } };
   grid.CellMouseUp += delegate(object s, DataGridViewCellMouseEventArgs e) { ApplySelection(); };
   grid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex == 8) { if (controller.Edit((WindowRow)grid.Rows[e.RowIndex].Tag, this)) { ResetSelection(); } else ApplySelection(); } };
   grid.CellPainting += delegate(object s, DataGridViewCellPaintingEventArgs e) { if (e.RowIndex < 0 || e.ColumnIndex != 0) return; e.Paint(e.CellBounds, DataGridViewPaintParts.All); if (selection.Contains(((WindowRow)grid.Rows[e.RowIndex].Tag).Key)) using (var brush = new SolidBrush(accent)) e.Graphics.FillRectangle(brush, e.CellBounds.Left, e.CellBounds.Top, Ui.S(3), e.CellBounds.Height); e.Handled = true; };
   var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Ui.S(12), 0, 0, 0) }; root.Controls.Add(actions, 1, 0);
   string[] labels = { Texts.Save, Texts.Unsave, Texts.Restore }; var buttons = new Button[3];
   for (int i = 0; i < buttons.Length; i++) { var b = new Button { Text = labels[i], Size = Ui.Sz(150, 40), FlatStyle = FlatStyle.Flat, BackColor = i == 0 ? Color.FromArgb(38, 100, 95) : Color.FromArgb(48, 54, 64), ForeColor = foreground, Margin = new Padding(0, 0, 0, Ui.S(8)), UseVisualStyleBackColor = false }; b.FlatAppearance.BorderColor = Color.FromArgb(65, 75, 84); buttons[i] = b; actions.Controls.Add(b); }
   buttons[0].Click += delegate { controller.SaveCurrent(Selected(), this); ResetSelection(); }; buttons[1].Click += delegate { controller.Unsave(Selected()); ResetSelection(); }; buttons[2].Click += delegate { controller.RestoreNow(Selected()); ResetSelection(); };
   var checks = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, Ui.S(12), 0, Ui.S(3)) }; root.Controls.Add(checks, 0, 1); root.SetColumnSpan(checks, 2);
   var tips = new ToolTip(); var enabled = new CheckBox { Text = Texts.Enabled, Checked = c.Enabled, AutoSize = true }; var auto = new CheckBox { Text = Texts.AutoStart, Checked = c.AutoStart, AutoSize = true }; var launch = new CheckBox { Text = Texts.LaunchMissing, Checked = c.LaunchMissing, AutoSize = true }; var close = new CheckBox { Text = Texts.MinimizeToTrayOnClose, Checked = c.MinimizeToTrayOnClose, AutoSize = true };
   foreach (CheckBox check in new CheckBox[] { enabled, auto, launch, close }) { check.Margin = new Padding(0, 0, Ui.S(20), Ui.S(5)); check.ForeColor = foreground; checks.Controls.Add(check); }
   tips.SetToolTip(enabled, Texts.EnabledTip); tips.SetToolTip(auto, Texts.AutoStartTip); tips.SetToolTip(launch, Texts.LaunchMissingTip); tips.SetToolTip(close, Texts.MinimizeToTrayOnCloseTip);
   enabled.CheckedChanged += delegate { c.Enabled = enabled.Checked; }; auto.CheckedChanged += delegate { c.AutoStart = auto.Checked; }; launch.CheckedChanged += delegate { c.LaunchMissing = launch.Checked; }; close.CheckedChanged += delegate { c.MinimizeToTrayOnClose = close.Checked; };
   message = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(150, 164, 175), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }; root.Controls.Add(message, 0, 2); root.SetColumnSpan(message, 2);
   grid.HandleCreated += delegate { try { SetWindowTheme(grid.Handle, "DarkMode_Explorer", null); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } };
   HandleCreated += delegate { try { int value = 1; DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int)); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } };
   controller.RowsChanged += delegate { RefreshRows(); }; ResetSelection();
  }
  List<WindowRow> Selected() { var rows = new List<WindowRow>(); foreach (DataGridViewRow r in grid.Rows) if (selection.Contains(((WindowRow)r.Tag).Key)) rows.Add((WindowRow)r.Tag); return rows; }
  void ApplySelection() { if (updating) return; updating = true; foreach (DataGridViewRow r in grid.Rows) r.Selected = selection.Contains(((WindowRow)r.Tag).Key); grid.Invalidate(); updating = false; }
  void ResetSelection() { selection.Clear(); foreach (WindowRow row in controller.GetRows()) if (row.Saved) selection.Add(row.Key); RefreshRows(); }
  void RefreshRows() {
   if (updating) return; updating = true;
   var rows = controller.GetRows(); var incoming = new Dictionary<string, WindowRow>(); foreach (var row in rows) incoming[row.Key] = row;
   for (int i = grid.Rows.Count - 1; i >= 0; i--) { var old = (WindowRow)grid.Rows[i].Tag; if (!incoming.ContainsKey(old.Key)) { grid.Rows.RemoveAt(i); selection.Remove(old.Key); } }
   var existing = new HashSet<string>(); foreach (DataGridViewRow r in grid.Rows) existing.Add(((WindowRow)r.Tag).Key);
   foreach (var row in rows) if (!existing.Contains(row.Key)) { int i = grid.Rows.Add(); grid.Rows[i].Tag = row; }
   foreach (DataGridViewRow r in grid.Rows) {
    var row = incoming[((WindowRow)r.Tag).Key]; r.Tag = row;
    string[] values = { row.Name, row.Process, row.Position, row.Size, row.Monitor == "なし" ? "" : row.Monitor, row.TitleRule, row.Maximized, row.Status };
    for (int i = 0; i < values.Length; i++) if (!object.Equals(r.Cells[i].Value, values[i])) r.Cells[i].Value = values[i];
    Color status = row.StatusKind == 0 ? Color.FromArgb(111, 213, 164) : row.StatusKind == 1 ? Color.FromArgb(235, 195, 96) : row.StatusKind == 2 ? Color.FromArgb(160, 169, 179) : Color.FromArgb(113, 124, 135);
    r.Cells[7].Style.ForeColor = status; r.Cells[7].Style.SelectionForeColor = status; r.Cells[8].ToolTipText = Texts.Edit; r.Selected = selection.Contains(row.Key);
   }
   message.Text = controller.LastMessage; updating = false; grid.Invalidate();
  }
 }
}

