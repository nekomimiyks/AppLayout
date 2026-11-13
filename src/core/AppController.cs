using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppLayout {
 public class WindowRow {
  public string Key; public AppEntry Entry; public WinInfo Window; public bool Saved;
  public string Name, Process, Position, Size, Monitor, TitleRule, Maximized, Status; public int StatusKind;
 }
 public static class Texts {
  public const string Enabled = "配置を有効", AutoStart = "ログイン時に自動起動", LaunchMissing = "アプリ自動起動", MinimizeToTrayOnClose = "アプリ終了時自動縮小";
  public const string EnabledTip = "オンの間、保存したウィンドウを約2秒ごとに保存位置へ戻します。オフにするとウィンドウを自由に動かせます。";
  public const string AutoStartTip = "Windowsにサインインしたとき、画面を出さずにタスクトレイに常駐した状態で起動します。";
  public const string LaunchMissingTip = "ログイン時の自動起動のとき、保存済みのアプリが起動していなければ起動します。";
  public const string MinimizeToTrayOnCloseTip = "×ボタンで閉じたとき、終了せずにタスクトレイに入ります。オフのときは×で終了します。";
  public const string Save = "今の位置で保存", Unsave = "保存を解除する", Restore = "配置を戻す", Edit = "編集";
  public const string Name = "名前", Process = "アプリ（プロセス名）", Position = "位置(X, Y)", Size = "サイズ(幅×高さ)", Monitor = "モニタ", TitleRule = "ウィンドウの見分け方", Maximized = "最大化", Status = "状態";
 }
 public class AppController {
  readonly ConfigStore store; readonly Keeper keeper; readonly SynchronizationContext context;
  Dictionary<AppEntry, EntryStatus> statuses = new Dictionary<AppEntry, EntryStatus>();
  public event EventHandler RowsChanged;
  public string LastMessage { get; private set; }
  public AppController(ConfigStore store, bool startupMode) {
   this.store = store; context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
   keeper = new Keeper(store, startupMode); LastMessage = "";
   keeper.StatusUpdated += delegate(Dictionary<AppEntry, EntryStatus> s) { context.Post(delegate { statuses = s; Changed(); }, null); };
  }
  void Changed() { var h = RowsChanged; if (h != null) h(this, EventArgs.Empty); }
  public List<WindowRow> GetRows() {
   var all = WindowScanner.ReadAll(); var mons = WindowScanner.GetMonitors(); var result = new List<WindowRow>(); var matched = new HashSet<IntPtr>();
   AppEntry[] entries; lock(store.Sync) entries = store.Config.Apps.ToArray();
   foreach (var e in entries) {
    var matches = new List<WinInfo>(); foreach (var w in all) if (!WindowScanner.IsOwn(w) && e.Matches(w)) { matches.Add(w); matched.Add(w.Handle); }
    var row = new WindowRow { Key = "e:" + e.Id, Entry = e, Saved = true, Window = matches.Count == 1 ? matches[0] : null, Name = e.Name, Process = e.Process, Position = e.X + ", " + e.Y, Size = e.Width + " × " + e.Height, Monitor = WindowScanner.MonitorLabel(e.X,e.Y,e.Width,e.Height,mons), TitleRule = e.TitleRuleText, Maximized = e.Maximized ? "はい" : "いいえ" };
    EntryStatus st; if (matches.Count == 0) { row.Status = "未起動"; row.StatusKind = 2; }
    else if (matches.Count > 1) { row.Status = "該当が複数"; row.StatusKind = 1; }
    else if (!Enabled) { row.Status = "一時停止中"; row.StatusKind = 2; }
    else if (row.Monitor == "なし") { row.Status = "モニタなし"; row.StatusKind = 1; }
    else if (row.Window.Minimized) { row.Status = "最小化中"; row.StatusKind = 0; }
    else { row.Status = statuses.TryGetValue(e, out st) && st.Text == "位置を戻しました" ? st.Text : "配置中"; row.StatusKind = 0; }
    result.Add(row);
   }
   foreach (var w in all) if (!matched.Contains(w.Handle) && WindowScanner.IsPickable(w)) {
    result.Add(new WindowRow { Key = "w:" + w.Handle.ToInt64(), Window = w, Saved = false, Name = w.Title, Process = w.ProcessName, Position = w.Minimized ? "" : w.Bounds.Left + ", " + w.Bounds.Top, Size = w.Minimized ? "" : w.Width + " × " + w.Height, Monitor = w.Minimized ? "なし" : WindowScanner.MonitorLabel(w.Bounds.Left,w.Bounds.Top,w.Width,w.Height,mons), TitleRule = "アプリで判定", Maximized = "いいえ", Status = "未保存", StatusKind = 3 });
   }
   return result;
  }
  AppEntry NewEntry(WinInfo w) { var e = AppEntry.FromWindow(w); int count = 0; foreach (var other in WindowScanner.ReadAll()) if (!WindowScanner.IsOwn(other) && e.Matches(other)) count++; if (count > 1) { e.TitleMode = "exact"; e.Title = w.Title; } return e; }
  public void SaveCurrent(IList<WindowRow> rows, IWin32Window owner) {
   int saved = 0; var skipped = new List<string>();
   lock(store.Sync) { foreach (var row in rows) { var w = row.Window == null ? null : WindowScanner.Describe(row.Window.Handle, null); if (w == null || w.Minimized) { skipped.Add(row.Name + (w == null ? "（未起動または複数該当）" : "（最小化中）")); continue; } if (row.Saved) row.Entry.SetPosition(w); else store.Config.Apps.Add(NewEntry(w)); saved++; } if (saved > 0) store.Save(); }
   LastMessage = saved + "件を保存しました" + (skipped.Count > 0 ? "。スキップ: " + string.Join("、", skipped.ToArray()) : ""); keeper.Kick(); Changed();
  }
  public void Unsave(IList<WindowRow> rows) { int n = 0; lock(store.Sync) { foreach (var row in rows) if (row.Saved && store.Config.Apps.Remove(row.Entry)) n++; if (n > 0) store.Save(); } LastMessage = n + "件の保存を解除しました"; keeper.Kick(); Changed(); }
  public void RestoreNow(IList<WindowRow> rows) { var targets = rows.Count == 0 ? GetRows() : rows; int n = 0; foreach (var row in targets) if (row.Saved && row.Window != null) { var e = row.Entry; if (WindowScanner.Apply(row.Window.Handle,e.X,e.Y,e.Width,e.Height,e.Maximized) >= 0) n++; } LastMessage = n + "件の配置を戻しました"; Changed(); }
  public bool Edit(WindowRow row, IWin32Window owner) { var e = row.Saved ? row.Entry.Clone() : NewEntry(row.Window); using (var d = new EntryEditDialog(e, !row.Saved)) { if (d.ShowDialog(owner) != DialogResult.OK) return false; } lock(store.Sync) { if (row.Saved) { int i = store.Config.Apps.IndexOf(row.Entry); if (i >= 0) store.Config.Apps[i] = e; } else store.Config.Apps.Add(e); store.Save(); } LastMessage = e.Name + "を保存しました"; keeper.Kick(); Changed(); return true; }
  public bool Enabled { get { return store.Config.Enabled; } set { lock(store.Sync) { store.Config.Enabled = value; store.Save(); } keeper.Kick(); Changed(); } }
  public bool LaunchMissing { get { return store.Config.LaunchMissingAtStartup; } set { lock(store.Sync) { store.Config.LaunchMissingAtStartup = value; store.Save(); } Changed(); } }
  public bool MinimizeToTrayOnClose { get { return store.Config.MinimizeToTrayOnClose; } set { lock(store.Sync) { store.Config.MinimizeToTrayOnClose = value; store.Save(); } Changed(); } }
  public bool AutoStart { get { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return k != null && k.GetValue("AppLayout") != null; } set { using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (value) k.SetValue("AppLayout", "\"" + Application.ExecutablePath + "\" /startup"); else k.DeleteValue("AppLayout", false); } Changed(); } }
  public void Start() { keeper.Start(); } public void Stop() { keeper.Stop(); }
 }
}
