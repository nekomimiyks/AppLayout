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
  public static string Enabled { get { return L.T("配置を有効"); } } public static string AutoStart { get { return L.T("ログイン時に自動起動"); } } public static string LaunchMissing { get { return L.T("アプリ自動起動"); } } public static string MinimizeToTrayOnClose { get { return L.T("アプリ終了時自動縮小"); } }
  public static string EnabledTip { get { return L.T("オンの間、保存したウィンドウを約2秒ごとに保存位置へ戻します。オフにするとウィンドウを自由に動かせます。"); } }
  public static string AutoStartTip { get { return L.T("Windowsにサインインしたとき、画面を出さずにタスクトレイに常駐した状態で起動します。"); } }
  public static string LaunchMissingTip { get { return L.T("ログイン時の自動起動のとき、保存済みのアプリが起動していなければ起動します。"); } }
  public static string MinimizeToTrayOnCloseTip { get { return L.T("×ボタンで閉じたとき、終了せずにタスクトレイに入ります。オフのときは×で終了します。"); } }
  public static string Save { get { return L.T("今の位置で配置"); } } public static string Unsave { get { return L.T("すべての配置を解除"); } } public static string Restore { get { return L.T("配置を実行"); } } public static string Edit { get { return L.T("編集"); } }
  public static string Name { get { return L.T("名前"); } } public static string Process { get { return L.T("アプリ（プロセス名）"); } } public static string Position { get { return L.T("位置(X, Y)"); } } public static string Size { get { return L.T("サイズ(幅×高さ)"); } } public static string Monitor { get { return L.T("モニタ"); } } public static string TitleRule { get { return L.T("ウィンドウの見分け方"); } } public static string Maximized { get { return L.T("最大化"); } } public static string Status { get { return L.T("状態"); } }
 }
 public class AppController {
  readonly ConfigStore store; readonly Keeper keeper; readonly SynchronizationContext context;
  Dictionary<AppEntry, EntryStatus> statuses = new Dictionary<AppEntry, EntryStatus>();
  bool restorePending;
  volatile bool stopped;
  public event EventHandler RowsChanged;
  public string LastMessage { get; private set; }
  public AppController(ConfigStore store, bool startupMode) {
   this.store = store; context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
   keeper = new Keeper(store, startupMode); LastMessage = "";
   keeper.StatusUpdated += delegate(Dictionary<AppEntry, EntryStatus> s) { context.Post(delegate { statuses = s; Changed(); }, null); };
  }
  void Changed() { var h = RowsChanged; if (h != null) h(this, EventArgs.Empty); }
  internal void ClearMessage() {LastMessage="";}
  public List<WindowRow> GetRows() {
   var all = WindowScanner.ReadAll(); var mons = WindowScanner.GetMonitors(); var result = new List<WindowRow>();
   AppEntry[] entries; lock(store.Sync) entries = store.Config.Apps.ToArray();
   Dictionary<IntPtr,int> assignments;
   var matchesByEntry=WindowScanner.MatchLayouts(entries,all,out assignments);
   foreach (var e in entries) {
    var matches=matchesByEntry[e];
    bool unique=matches.Count==1 && assignments[matches[0].Handle]==1;
    var row = new WindowRow { Key = "e:" + e.Id, Entry = e, Saved = true, Window = unique ? matches[0] : null, Name = e.Name, Process = e.Process, Position = e.X + ", " + e.Y, Size = e.Width + " × " + e.Height, Monitor = WindowScanner.MonitorLabel(e.X,e.Y,e.Width,e.Height,mons), TitleRule = e.TitleRuleText, Maximized = e.Maximized ? "はい" : "いいえ" };
    EntryStatus st; if (matches.Count == 0) { row.Status = WindowScanner.HasCloakedMatch(all,e) ? "非表示中" : "未起動"; row.StatusKind = 2; }
    else if (!unique) { row.Status = "該当が複数"; row.StatusKind = 1; }
    else if (!Enabled) { row.Status = "一時停止中"; row.StatusKind = 2; }
    else if (row.Monitor == "なし") { row.Status = "モニタなし"; row.StatusKind = 1; }
    else if (row.Window.Minimized) { row.Status = "最小化中"; row.StatusKind = 0; }
    else if (statuses.TryGetValue(e, out st) && (st.Text == "位置を戻しました" || st.Text == "配置を変更できませんでした")) { row.Status = st.Text; row.StatusKind = st.Kind; }
    else { row.Status = "配置中"; row.StatusKind = 0; }
    result.Add(row);
   }
   foreach (var w in all) if (!assignments.ContainsKey(w.Handle) && WindowScanner.IsPickable(w)) {
    result.Add(new WindowRow { Key = "w:" + w.Handle.ToInt64(), Window = w, Saved = false, Name = w.Title, Process = w.ProcessName, Position = w.Minimized ? "" : w.Bounds.Left + ", " + w.Bounds.Top, Size = w.Minimized ? "" : w.Width + " × " + w.Height, Monitor = w.Minimized ? "なし" : WindowScanner.MonitorLabel(w.Bounds.Left,w.Bounds.Top,w.Width,w.Height,mons), TitleRule = "アプリで判定", Maximized = "いいえ", Status = "未保存", StatusKind = 3 });
   }
   return result;
  }
  AppEntry NewEntry(WinInfo w) { var e = AppEntry.FromWindow(w); int count = 0; foreach (var other in WindowScanner.ReadAll()) if (!WindowScanner.IsOwn(other) && e.Matches(other)) count++; if (count > 1) { e.TitleMode = "exact"; e.Title = w.Title; } return e; }
  public bool SetTarget(WindowRow row, bool enabled) {
   if(row==null)return false;
   lock(store.Sync) {
    var previous=store.Config.Apps;
    var next=new List<AppEntry>(previous);
    int index=row.Entry==null?-1:next.FindIndex(delegate(AppEntry item){return item.Id==row.Entry.Id;});
    if(!enabled) {
     if(index<0)return true;
     next.RemoveAt(index);
    } else {
     if(index>=0)return true;
     var window=WindowScanner.ReadCurrent(row.Window);
     if(window==null) {
      LastMessage=L.T("現在位置を取得できません。ウィンドウを開いて最小化を解除してからONにしてください。");Changed();return false;
     }
     // 更新待ちの古い未登録行から同じウィンドウを二重に登録しない。
     foreach(var item in next)if(item.Matches(window))return true;
     next.Add(NewEntry(window));
    }
    store.Config.Apps=next;
    try{store.Save();}catch{store.Config.Apps=previous;throw;}
   }
   LastMessage=L.F(enabled?"{0}をONにしました。現在の配置を保存しました。":"{0}をOFFにしました。配置の登録を解除しました。",row.Name);
   keeper.Kick();Changed();return true;
  }
  public void SaveCurrent(IList<WindowRow> rows, IWin32Window owner) {
   int saved = 0, kept = 0, removed = 0;
   var skipped = new List<string>();
   var selectedIds = new HashSet<string>();
   foreach (var row in rows) if (row.Saved && row.Entry != null) selectedIds.Add(row.Entry.Id);
   lock(store.Sync) {
    var previous = store.Config.Apps;
    var next = new List<AppEntry>();
    foreach (var entry in previous) { if (selectedIds.Contains(entry.Id)) next.Add(entry.Clone()); else removed++; }
    var handles = new HashSet<IntPtr>();
    foreach (var row in rows) {
     AppEntry entry = null;
     if (row.Saved && row.Entry != null) entry = next.Find(delegate(AppEntry e) { return e.Id == row.Entry.Id; });
     var w = WindowScanner.ReadCurrent(row.Window);
     if (entry != null && w != null && !entry.Matches(w)) w = null;
     if (w == null) {
      if (entry != null) kept++;
      else skipped.Add(row.Name);
      continue;
     }
     if (!handles.Add(w.Handle)) continue;
     if (entry != null) entry.SetPosition(w);
     else if (!row.Saved) next.Add(NewEntry(w));
     else continue;
     saved++;
    }
    store.Config.Apps = next;
    try { store.Save(); } catch { store.Config.Apps = previous; throw; }
   }
   LastMessage = L.F("現在位置を登録 {0}件・登録を解除 {1}件",saved,removed) + (kept > 0 ? L.F("・既存の配置を保持 {0}件（未起動・最小化中など）",kept) : "") + (skipped.Count > 0 ? L.F("。登録できませんでした: {0}",string.Join(", ", skipped.ToArray())) : "");
   keeper.Kick(); Changed();
  }
  public void UnsaveAll() {
   int count;
   lock (store.Sync) {
    var previous = store.Config.Apps; count = previous.Count;
    store.Config.Apps = new List<AppEntry>();
    try { store.Save(); } catch { store.Config.Apps = previous; throw; }
   }
   LastMessage = L.F("すべての配置を解除しました（{0}件）",count); keeper.Kick(); Changed();
  }
  public void Unsave(IList<WindowRow> rows) {
   var ids=new HashSet<string>();
   foreach(var row in rows)if(row.Saved && row.Entry!=null)ids.Add(row.Entry.Id);
   int removed;
   lock(store.Sync) {
    var previous=store.Config.Apps;
    var next=new List<AppEntry>(previous);
    removed=next.RemoveAll(delegate(AppEntry e){return ids.Contains(e.Id);});
    if(removed>0) {
     store.Config.Apps=next;
     try{store.Save();}catch{store.Config.Apps=previous;throw;}
    }
   }
   LastMessage=L.F("{0}件の保存を解除しました",removed);keeper.Kick();Changed();
  }
  public void RestoreNow(IList<WindowRow> rows) {
   if(restorePending || stopped)return;
   var ids=new HashSet<string>();
   foreach(var row in rows)if(row.Saved && row.Entry!=null)ids.Add(row.Entry.Id);
   bool all=rows.Count==0;
   List<AppEntry> entries;
   lock(store.Sync)entries=store.Config.Apps;
   restorePending=true;
   ThreadPool.QueueUserWorkItem(delegate {
    int restored=0,skipped=0;
    Exception error=null;
    try {
     Dictionary<IntPtr,int> assignments;
     var matches=WindowScanner.MatchLayouts(entries,WindowScanner.ReadAll(),out assignments);
     foreach(var entry in entries) {
      if(stopped)return;
      if(!all && !ids.Contains(entry.Id))continue;
      var candidates=matches[entry];
      if(candidates.Count==1 && assignments[candidates[0].Handle]==1 &&
       keeper.Apply(candidates[0],entry,entries,false)>=0)restored++;
      else skipped++;
     }
    } catch(Exception ex){error=ex;}
    if(stopped)return;
    try {
     context.Post(delegate {
      restorePending=false;
      if(stopped)return;
      LastMessage=error==null
       ? L.F("{0}件の配置を実行しました",restored)+(skipped>0?L.F("（未起動・最小化中など {0}件はそのまま）",skipped):"")
       : L.T("配置を変更できませんでした")+"\n"+error.Message;
      Changed();
     },null);
    } catch(InvalidOperationException) {}
   });
  }
  public bool Edit(WindowRow row, IWin32Window owner) {
   AppEntry e;
   if (row.Saved) e = row.Entry.Clone();
   else {
    var window = WindowScanner.ReadCurrent(row.Window);
    if (window == null) {
     LastMessage = L.T("ウィンドウが閉じられたか最小化されています。ウィンドウを開いてから編集してください。"); Changed(); return false;
    }
    e = NewEntry(window);
   }
   using (var d = new EntryEditDialog(e, !row.Saved)) { if (d.ShowDialog(owner) != DialogResult.OK) return false; }
   lock(store.Sync) {
    var previous = store.Config.Apps;
    var next = new List<AppEntry>(previous);
    if (row.Saved) { int i = next.FindIndex(delegate(AppEntry item) { return item.Id == e.Id; }); if (i < 0) { LastMessage = L.T("この配置はすでに解除されています。"); Changed(); return false; } next[i] = e; }
    else next.Add(e);
    store.Config.Apps = next;
    try { store.Save(); } catch { store.Config.Apps = previous; throw; }
   }
   LastMessage = L.F("{0}の配置を登録しました",e.Name); keeper.Kick(); Changed(); return true;
  }
  internal void ExportSettings(string destination) { store.ExportSettings(destination); }

  internal void RestoreSettings(LayoutConfig settings) {
   store.RestoreBackup(settings);
   L.SetLanguage(settings.Language);
   keeper.Kick();
   Changed();
  }

  void SaveSetting(ref bool setting,bool value) {
   lock(store.Sync) {
    bool previous=setting;
    if(previous==value)return;
    setting=value;
    try{store.Save();}catch{setting=previous;throw;}
   }
  }
  public bool Enabled { get { return store.Config.Enabled; } set { SaveSetting(ref store.Config.Enabled,value);keeper.Kick();Changed(); } }
  public bool LaunchMissing { get { return store.Config.LaunchMissingAtStartup; } set { SaveSetting(ref store.Config.LaunchMissingAtStartup,value);Changed(); } }
  public bool MinimizeToTrayOnClose { get { return store.Config.MinimizeToTrayOnClose; } set { SaveSetting(ref store.Config.MinimizeToTrayOnClose,value);Changed(); } }
  internal event EventHandler UpdateCheckingChanged;
  internal event EventHandler UpdateStatusChanged, UpdateRequested;
  internal Version AvailableUpdate { get; private set; }
  internal bool Updating { get; private set; }
  internal void SetUpdateStatus(Version available, bool updating) {
   AvailableUpdate=available;Updating=updating;
   var handler=UpdateStatusChanged;
   if(handler!=null)handler(this,EventArgs.Empty);
  }
  internal void RequestUpdate() {
   var handler=UpdateRequested;
   if(handler!=null)handler(this,EventArgs.Empty);
  }
  internal bool CheckForUpdates {
   get { return store.Config.CheckForUpdates; }
   set {
    if(value==store.Config.CheckForUpdates)return;
    SaveSetting(ref store.Config.CheckForUpdates,value);
    var handler=UpdateCheckingChanged;
    if(handler!=null)handler(this,EventArgs.Empty);
   }
  }
  public bool AutoStart {
#if STORE_BUILD
   get {
    try { return IsStartupEnabled(ReadStartupResult(Windows.ApplicationModel.StartupTask.GetAsync("AppLayoutStartup")).State); }
    catch(System.Runtime.InteropServices.COMException){return false;}
    catch(TimeoutException){return false;}
   }
   set {
    var task=ReadStartupResult(Windows.ApplicationModel.StartupTask.GetAsync("AppLayoutStartup"));
    if(value)ReadStartupResult(task.RequestEnableAsync());
    else task.Disable();
    Changed();
   }
#else
   get {
    try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return k != null && k.GetValue("AppLayout") != null; }
    catch (System.Security.SecurityException) { return false; }
    catch (UnauthorizedAccessException) { return false; }
   }
   set { using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (value) k.SetValue("AppLayout", "\"" + Application.ExecutablePath + "\" /startup"); else k.DeleteValue("AppLayout", false); } Changed(); }
#endif
  }
#if STORE_BUILD
  static bool IsStartupEnabled(Windows.ApplicationModel.StartupTaskState state) {
   return state==Windows.ApplicationModel.StartupTaskState.Enabled ||
          state==Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
  }
  static T ReadStartupResult<T>(Windows.Foundation.IAsyncOperation<T> operation) {
   var watch=System.Diagnostics.Stopwatch.StartNew();
   while(operation.Status==Windows.Foundation.AsyncStatus.Started) {
    if(watch.ElapsedMilliseconds>=1500)throw new TimeoutException();
    Thread.Sleep(10);
   }
   return operation.GetResults();
  }
#endif
  public void Start() { keeper.Start(); } public void Stop() { stopped=true;keeper.Stop(); }
  public string Language {
   get {return L.NormalizeSelection(store.Config.Language);}
   set {
    string next=L.NormalizeSelection(value);
    lock(store.Sync) {
     string previous=store.Config.Language;store.Config.Language=next;
     try{store.Save();}catch{store.Config.Language=previous;throw;}
    }
    L.SetLanguage(next);LastMessage="";Changed();
   }
  }
 }
}
