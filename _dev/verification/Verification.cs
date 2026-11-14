using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AppLayout {
 internal static class Verification {
  static int checks;
  static string output;
  static bool screenshotsOnly;
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr handle);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr handle,IntPtr dc);
  [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc,int x,int y);
  [DllImport("uxtheme.dll")] static extern IntPtr GetWindowTheme(IntPtr handle);
  static void Pump(int milliseconds){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(10);}}
  static Color LivePixel(Control control,int x,int y){IntPtr dc=GetWindowDC(control.Handle);try{return ColorTranslator.FromWin32((int)GetPixel(dc,x,y));}finally{ReleaseDC(control.Handle,dc);}}
  static void Assert(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); }
  static AppEntry Entry(string name) { return new AppEntry { Id = Guid.NewGuid().ToString("N"), Name = name, Process = "applayout-verification-missing", Class = "missing", Width = 800, Height = 600 }; }
  [STAThread] static int Main(string[] args) {
   try {
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
    Native.MakeDpiAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    output = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    if (args.Length == 1 && args[0] == "screenshots") {
     screenshotsOnly = true;
     TestEditor(); TestLedger();
     Console.WriteLine("Completed " + checks + " checks."); return 0;
    }
    TestController(); TestRealtime(); TestInstance(args); TestEditor(); TestTrayTransition(); TestCloseToTray(); TestTrayPlacement(); TestScrollbars(); TestStartupSizing();
    if (args.Length > 1 && args[1] == "ledger") TestLedger();
    if (args.Length > 0 && File.Exists(args[0])) TestShell(args[0]);
    Console.WriteLine("Completed " + checks + " checks."); return 0;
   } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
  }
  static void TestController() {
   string path = Path.Combine(output,"test-layout.json");
   var store = new ConfigStore(path); store.Config = new LayoutConfig { Enabled = false };
   AppEntry keep = Entry("未起動・保持"), remove = Entry("選択解除"), mini = Entry("最小化・保持");
   keep.X = 42; store.Config.Apps.AddRange(new[] { keep,remove,mini }); store.Save();
   var c = new AppController(store,false);
   using(var target = new Form { Text = "AppLayout isolated verification", Bounds = new Rectangle(200,150,420,280), ShowInTaskbar = false }) {
    target.Show(); Application.DoEvents();
    var window = WindowScanner.Describe(target.Handle,null);
    Assert(window != null,"隔離したテストウィンドウを取得");
    mini.Process=window.ProcessName;mini.Class=window.Class;mini.AppId=window.AppId;
    var existing = AppEntry.FromWindow(window); existing.Name = "現在位置を更新"; existing.X = -1234; store.Config.Apps.Add(existing);
    target.WindowState = FormWindowState.Minimized; Application.DoEvents();
    var minimized = WindowScanner.Describe(target.Handle,null);
    Assert(minimized != null && minimized.Minimized,"最小化したテストウィンドウを取得");
    c.SaveCurrent(new List<WindowRow> {
     new WindowRow { Saved=true, Entry=keep, Name=keep.Name },
     new WindowRow { Saved=true, Entry=mini, Name=mini.Name, Window=minimized }
    },null);
    Assert(store.Config.Apps.Count==2,"非選択の登録のみ解除");
    Assert(store.Config.Apps.Exists(delegate(AppEntry e){return e.Id==keep.Id && e.X==42;}),"未起動の選択行の座標・登録を保持");
    Assert(store.Config.Apps.Exists(delegate(AppEntry e){return e.Id==mini.Id;}),"最小化中の選択行の登録を保持");
    target.WindowState=FormWindowState.Normal; Application.DoEvents(); window=WindowScanner.Describe(target.Handle,null);
    c.SaveCurrent(new List<WindowRow> { new WindowRow { Window=window, Name="新規ウィンドウ" } },null);
    Assert(store.Config.Apps.Count==1,"チェック中の新規ウィンドウを登録し非選択は解除");
    var saved=store.Config.Apps[0]; saved.X=-1234;
    c.SaveCurrent(new List<WindowRow> { new WindowRow { Saved=true,Entry=saved,Window=window,Name=saved.Name } },null);
    Assert(store.Config.Apps.Count==1 && store.Config.Apps[0].X==window.Bounds.Left,"既存登録を現在位置に更新");
    var reloaded=new ConfigStore(path);reloaded.Load();Assert(reloaded.Config.Apps.Count==1,"変更が設定ファイルに永続化");
    c.SaveCurrent(new List<WindowRow>(),null);Assert(store.Config.Apps.Count==0,"すべてのチェックを外して確定すると全登録解除");
    store.Config.Apps.Add(Entry("全解除用"));c.UnsaveAll();Assert(store.Config.Apps.Count==0,"全解除は選択に関係なく全登録解除");
    target.Close();
   }
   // 永続化失敗時にもメモリ上の登録を失わない。
   var invalid=new ConfigStore(Path.Combine(output,"Verification.exe","layout.json"));invalid.Config=new LayoutConfig();invalid.Config.Apps.Add(Entry("失敗時保持"));
   var broken=new AppController(invalid,false);bool failed=false;try{broken.UnsaveAll();}catch(IOException){failed=true;}
   Assert(failed && invalid.Config.Apps.Count==1,"保存失敗時は登録一覧をロールバック");
   Assert(Texts.Save=="今の位置で配置" && Texts.Restore=="配置を実行" && Texts.Unsave=="すべての配置を解除する","指定されたボタン文言");
   var pathological=Entry("正規表現");pathological.TitleMode="regex";pathological.Title="^(a+)+$";
   var regexWindow=new WinInfo { ProcessName=pathological.Process,Class=pathological.Class,Title=new string('a',10000)+"!" };
   var timer=Stopwatch.StartNew();Assert(!pathological.Matches(regexWindow),"複雑な正規表現はtimeoutで不一致");Assert(timer.ElapsedMilliseconds<3000,"正規表現が長時間UIを止めない");
  }
  static void TestInstance(string[] args) {
   bool created;
   using(var mutex=new Mutex(true,Program.InstanceMutexName,out created)) {
    Assert(created,"単一起動テスト用の排他を確保");
    using(var show=new EventWaitHandle(false,EventResetMode.AutoReset,Program.ShowEventName)) {
     typeof(Program).GetMethod("Main",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{new string[0]});
     Assert(show.WaitOne(1000),"二重起動は既存画面を開くイベントを通知して終了");
     if(args.Length>0 && File.Exists(args[0])) {
      foreach(string name in new[]{"AppLayout.Renamed1.exe","AppLayout.Renamed2.exe"}) {
       string dest=Path.Combine(output,name);File.Copy(args[0],dest,true);
       using(var process=Process.Start(new ProcessStartInfo(dest){UseShellExecute=false,CreateNoWindow=true})) {
        if(!process.WaitForExit(5000)) { process.Kill();throw new Exception("duplicate child did not exit"); }
        Assert(process.ExitCode==0 && show.WaitOne(1000),"exe名が異なっても多重起動しない: "+name);
       }
      }
     }
    }
    mutex.ReleaseMutex();
   }
  }
  static void TestRealtime() {
   var store=new ConfigStore(Path.Combine(output,"realtime-layout.json"));store.Config=new LayoutConfig {Enabled=false};
   var other=Entry("別の登録");other.X=987;store.Config.Apps.Add(other);store.Save();
   var c=new AppController(store,false);
   using(var target=new Form {Text="Realtime isolated target",Bounds=new Rectangle(220,180,460,320),ShowInTaskbar=false}) {
    target.Show();Application.DoEvents();var window=WindowScanner.Describe(target.Handle,null);
    var row=new WindowRow {Key="w:"+window.Handle,Name="即時登録",Window=window};
    Assert(c.SetTarget(row,true) && store.Config.Apps.Count==2,"ONクリックで対象1件を即時登録");
    var reload=new ConfigStore(store.Path);reload.Load();Assert(reload.Config.Apps.Count==2,"ON操作を直ちにファイルへ保存");
    Assert(store.Config.Apps[0].Id==other.Id && store.Config.Apps[0].X==987,"ON操作で他行の設定を変更しない");
    var active=store.Config.Apps[1];int x=active.X;target.Left+=100;Application.DoEvents();
    Assert(c.SetTarget(row,true) && store.Config.Apps.Count==2 && store.Config.Apps[1].X==x,"重複したON通知で二重登録・座標変更しない");
    var savedRow=new WindowRow {Key="e:"+active.Id,Saved=true,Entry=active,Window=window,Name=active.Name};
    Assert(c.SetTarget(savedRow,false) && store.Config.Apps.Count==1 && store.Config.Apps[0].Id==other.Id,"OFF操作は対象1件だけ即時解除");
    reload.Load();Assert(reload.Config.Apps.Count==1,"OFF操作を直ちにファイルへ保存");
    target.WindowState=FormWindowState.Minimized;Application.DoEvents();
    row.Window=WindowScanner.Describe(target.Handle,null);
    Assert(!c.SetTarget(row,true) && store.Config.Apps.Count==1,"未登録の最小化ウィンドウは誤登録しない");
    target.WindowState=FormWindowState.Normal;Application.DoEvents();
    row.Window=WindowScanner.Describe(target.Handle,null);
    var bad=new ConfigStore(Path.Combine(output,"Verification.exe","layout.json"));bad.Config=new LayoutConfig();bad.Config.Apps.Add(other);
    bool failed=false;try{new AppController(bad,false).SetTarget(row,true);}catch(IOException){failed=true;}
    Assert(failed && bad.Config.Apps.Count==1,"即時保存が失敗したらメモリ上の登録も元に戻す");
    target.Close();Assert(!c.SetTarget(row,true),"閉じたウィンドウの古い行は登録しない");
   }
  }
  static void Capture(Form form,string name) {
   if(!form.Visible)form.Show();Application.DoEvents();
   if(screenshotsOnly && (name=="main-theme-light.png" || name=="main-theme-dark.png" || name=="edit-dialog.png")) FitScreenshot(form);
   var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<350){Application.DoEvents();Thread.Sleep(10);}
   using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(Path.Combine(output,name)); }
  }
  // 公開画像は実際のウィンドウをリサイズし、一覧や入力欄の下の空白を減らす。
  static void FitScreenshot(Form form) {
   var grids=form.Controls.Find("LedgerGrid",true);
   if(grids.Length>0) {
    var grid=(DataGridView)grids[0];
    int needed=grid.ColumnHeadersHeight+8;
    foreach(DataGridViewRow row in grid.Rows)needed+=row.Height;
    form.Height-=Math.Max(0,grid.Height-needed);
   } else if(form is EntryEditDialog) {
    var flags=BindingFlags.Instance|BindingFlags.NonPublic;
    var content=(TableLayoutPanel)typeof(EntryEditDialog).GetField("content",flags).GetValue(form);
    var scroll=(ThemedScrollPanel)typeof(EntryEditDialog).GetField("scroll",flags).GetValue(form);
    form.Height-=Math.Max(0,scroll.Height-content.Height-8);
   }
   form.PerformLayout();Application.DoEvents();
  }
  static IntPtr AppWindow(int pid) {
   IntPtr result=IntPtr.Zero;
   Native.EnumWindows(delegate(IntPtr handle,IntPtr ignored) {
    uint id;Native.GetWindowThreadProcessId(handle,out id);
    if(id==(uint)pid && (Native.GetWindowLong(handle,-16)&0x00C00000)==0x00C00000){var title=new System.Text.StringBuilder(256);Native.GetWindowText(handle,title,title.Capacity);if(title.ToString()=="AppLayout"){result=handle;return false;}}
    return true;
   },IntPtr.Zero);return result;
  }
  static void TestShell(string app) {
   string dir=Path.Combine(output,"shell-test");Directory.CreateDirectory(dir);
   string firstPath=Path.Combine(dir,"AppLayout.One.exe"),secondPath=Path.Combine(dir,"AppLayout.Two.exe");File.Copy(app,firstPath,true);File.Copy(app,secondPath,true);
   var config=new ConfigStore(Path.Combine(dir,"layout.json"));config.Config=new LayoutConfig{Enabled=false,LaunchMissingAtStartup=false,MinimizeToTrayOnClose=false};config.Save();
   byte[] before=File.ReadAllBytes(config.Path);
   using(var first=Process.Start(new ProcessStartInfo(firstPath,"/startup"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=dir})) {
    try {
     IntPtr window=IntPtr.Zero;var wait=Stopwatch.StartNew();
     while(wait.ElapsedMilliseconds<6000){window=AppWindow(first.Id);if(window!=IntPtr.Zero)break;Thread.Sleep(50);}
     Assert(window!=IntPtr.Zero,"完成版が隔離設定で起動しトレイ待機");
     Thread.Sleep(200);Assert(!Native.IsWindowVisible(window),"ログイン起動ではメイン画面を表示しない");
     using(var second=Process.Start(new ProcessStartInfo(secondPath){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=dir})) {
      if(!second.WaitForExit(5000)){second.Kill();throw new Exception("duplicate app did not exit");}
      Assert(second.ExitCode==0,"別名の完成版は二重常駐せず終了");
     }
     wait.Restart();while(wait.ElapsedMilliseconds<3000){window=AppWindow(first.Id);if(window!=IntPtr.Zero && Native.IsWindowVisible(window))break;Thread.Sleep(50);}
     Assert(Native.IsWindowVisible(window),"二重起動でトレイの既存画面を表示");
     PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero);
     Assert(first.WaitForExit(5000),"終了時に配置スレッドとトレイを解放");
     Assert(Convert.ToBase64String(before)==Convert.ToBase64String(File.ReadAllBytes(config.Path)),"起動と終了で配置設定を書き換えない");
    } finally {if(!first.HasExited)first.Kill();}
   }
  }
  static void TestEditor() {
   var entry=Entry("チャット");entry.Process="chat-app";entry.Class="SampleWindow";entry.X=-1440;entry.Y=1252;entry.Width=1440;entry.Height=900;
   using(var dialog=new EntryEditDialog(entry,false)) {
    Capture(dialog,"edit-dialog.png");Assert(dialog.Text=="配置を編集","編集画面の名称");
    var x=(NumericUpDown)dialog.Controls.Find("PositionX",true)[0];
    var y=(NumericUpDown)dialog.Controls.Find("PositionY",true)[0];
    Assert(x.Value==-1440 && y.Value==1252,"編集画面に登録済みの座標を表示");
    Assert(dialog.Controls.Find("Maximized",true)[0] is ToggleSwitch,"最大化はON/OFFスイッチ");
    var mode=(ComboBox)dialog.Controls.Find("MatchMode",true)[0];
    Assert(!dialog.Controls.Find("MatchTitle",true)[0].Visible,"アプリ判定では不要なタイトル入力を非表示");
    mode.SelectedIndex=1;Application.DoEvents();
    Assert(dialog.Controls.Find("MatchTitle",true)[0].Visible,"タイトル判定に切り替えると条件入力を表示");
    Capture(dialog,"edit-dialog-title.png");
    dialog.Close();
   }
   Assert(entry.X==-1440 && entry.Name=="チャット","編集キャンセルでは元の登録を変更しない");
   using(var dialog=new EntryEditDialog(entry,false)) {
    ((TextBox)dialog.Controls.Find("EntryName",true)[0]).Text="変更後の名前";
    ((NumericUpDown)dialog.Controls.Find("PositionX",true)[0]).Value=-1200;
    dialog.GetType().GetMethod("Accept",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,null);
    Assert(dialog.DialogResult==DialogResult.OK && entry.Name=="変更後の名前" && entry.X==-1200,"編集確定で名前と座標を反映");
   }
  }
  static void TestLedger() {
   var store=new ConfigStore(Path.Combine(output,"ui-layout.json"));store.Config=new LayoutConfig{Enabled=true};
   var controller=new AppController(store,false);
   Console.WriteLine("Creating ledger...");
   using(var form=UiFactory.CreateMainForm(controller)) {
    var rows=new List<WindowRow>();
    string[] names={"コードエディター","作業フォルダー","テキストエディター","チャット","ブラウザー","音楽プレーヤー"};
    string[] processes={"code-editor","file-manager","text-editor","chat-app","browser","music-player"};
    for(int i=0;i<names.Length;i++){var entry=Entry(names[i]);entry.Process=processes[i];bool saved=i!=2;if(saved)store.Config.Apps.Add(entry);rows.Add(new WindowRow{Key=(saved?"e:":"w:")+entry.Id,Entry=saved?entry:null,Saved=saved,Name=names[i],Process=entry.Process,Position=i<3?"0, 0":"-1440, 0",Size="1440 × 900",Monitor=i<3?"1":"2",TitleRule="アプリで判定",Maximized="いいえ",Status=i==4?"最小化中":i==5?"未起動":saved?"配置中":"未保存",StatusKind=i==5?2:saved?0:3});}
    Console.WriteLine("Showing ledger..."); form.Show();Application.DoEvents();
    var set=form.GetType().GetMethod("SetRows",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
    Assert(set!=null,"台帳のUI単体テストフック");set.Invoke(form,new object[]{rows,true});
    Capture(form,"main-ledger.png");Assert(form.Width>500 && form.Height>300,"台帳画面を構築");
    var grid=(DataGridView)form.Controls.Find("LedgerGrid",true)[0];
    var chosen=new List<DataGridViewRow>();foreach(DataGridViewRow view in grid.Rows)if(view.Tag is WindowRow)chosen.Add(view);
    Assert(chosen.Count==6 && (bool)chosen[0].Cells[0].Value && !(bool)chosen[2].Cells[0].Value,"登録済みだけONスイッチ");
    Assert(chosen[0].DefaultCellStyle.BackColor==AppTheme.ActiveRow && chosen[2].DefaultCellStyle.BackColor==AppTheme.Surface,"ONは行全体を色付けしOFFと区別");
    Assert(!HasButton(form,Texts.Save),"今の位置で配置ボタンは不要なので撤廃");
    TestLiveTheme(form,grid,chosen);
    TestTypography(form,grid,chosen);
    int valueChanges=0;grid.CellValueChanged+=delegate{valueChanges++;};set.Invoke(form,new object[]{rows,false});
    Assert(valueChanges==0,"同一データの定期更新ではセル値を書き直さない");
    int anchor=grid.FirstDisplayedScrollingRowIndex;set.Invoke(form,new object[]{rows,false});
    Assert(grid.FirstDisplayedScrollingRowIndex==anchor,"定期更新でもスクロール位置を保持");
    var toggle=form.GetType().GetMethod("Toggle",BindingFlags.Instance|BindingFlags.NonPublic);
    string removedId=rows[0].Entry.Id,otherId=rows[1].Entry.Id;
    toggle.Invoke(form,new object[]{chosen[0]});
    Assert(!store.Config.Apps.Exists(delegate(AppEntry e){return e.Id==removedId;}) && store.Config.Apps.Exists(delegate(AppEntry e){return e.Id==otherId;}),"行クリックが他行を変えず即時解除APIへ接続");
    rows[0].Saved=false;rows[0].Status="未保存";
    set.Invoke(form,new object[]{rows,false});
    chosen.Clear();foreach(DataGridViewRow view in grid.Rows)if(view.Tag is WindowRow)chosen.Add(view);
    var offView=ViewFor(grid,rows[0].Key);var onView=ViewFor(grid,rows[1].Key);
    Assert(!(bool)offView.Cells[0].Value && offView.DefaultCellStyle.BackColor==AppTheme.Surface,"OFFにすると行色も通常色へ戻る");
    Capture(form,"main-live.png");
    // モニタの移動で受信する倍率変更を模擬して寸法と選択維持を確認。
    var dpiForm=form as DpiAwareForm;Assert(dpiForm!=null,"台帳は動的DPIに対応");
    var dpi=typeof(DpiAwareForm).GetMethod("UpdateDpi",BindingFlags.Instance|BindingFlags.NonPublic);
    int originalDpi=(int)Math.Round(dpiForm.UiScale*96);
    float originalLogicalPixels=form.Font.Size/((DpiAwareForm)form).UiScale;
    foreach(int targetDpi in new[]{144,192,96}) {
     dpi.Invoke(form,new object[]{targetDpi});Application.DoEvents();
     Assert(grid.ColumnHeadersHeight>=(int)Math.Round(30*targetDpi/96f),"DPI "+targetDpi+" で表の寸法を更新");
     Assert(form.Font.Unit==GraphicsUnit.Pixel && Math.Abs(form.Font.Size-originalLogicalPixels*targetDpi/96f)<0.02f,"DPI変更で本文の大きさを1回だけ拡大");
     Assert(!(bool)offView.Cells[0].Value && (bool)onView.Cells[0].Value,"倍率変更後も対象のON/OFFを保持");
    }
    dpi.Invoke(form,new object[]{originalDpi});
    form.ClientSize=new Size(640,420);Application.DoEvents();Capture(form,"main-compact.png");
    form.Close();
   }
  }
  static bool HasButton(Control parent,string text){foreach(Control child in parent.Controls){if(child is Button&&child.Text==text)return true;if(HasButton(child,text))return true;}return false;}
  static void SetTheme(bool appDark,bool systemDark) {
   typeof(AppTheme).GetMethod("UpdateModes",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{appDark,systemDark});
  }
  static void CaptureMenu(TrayMenu menu,string file) {
   Application.DoEvents();using(var bitmap=new Bitmap(menu.Width,menu.Height)){menu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,file));}
  }
  static int ThemeListeners() {var callbacks=typeof(AppTheme).GetField("Changed",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null) as Delegate;return callbacks==null?0:callbacks.GetInvocationList().Length;}
  static void SetTypography(object original,float ratio) {
   var type=original.GetType();
   object value=Activator.CreateInstance(type,new object[]{
    type.GetField("BodyName").GetValue(original),(float)type.GetField("BodyPoints").GetValue(original)*ratio,type.GetField("BodyStyle").GetValue(original),
    type.GetField("MenuName").GetValue(original),(float)type.GetField("MenuPoints").GetValue(original)*ratio,type.GetField("MenuStyle").GetValue(original)});
   typeof(UiTypography).GetMethod("Update",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{value});
  }
  static void TestTypography(Form form,DataGridView grid,List<DataGridViewRow> rows) {
   object original=typeof(UiTypography).GetField("current",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
   float bodyPoints=UiTypography.BodyPoints;
   Console.WriteLine("Windows body font: "+form.Font.Name+", "+bodyPoints+"pt");
   Assert(form.Font.Unit==GraphicsUnit.Pixel && Math.Abs(form.Font.Size-bodyPoints*96f/72f*((DpiAwareForm)form).UiScale)<0.02f,"本文はWindows標準値に表示先DPIを1回だけ反映");
   using(var menu100=UiTypography.MenuFont(1))using(var menu200=UiTypography.MenuFont(2))
    Assert(menu100.Unit==GraphicsUnit.Pixel && Math.Abs(menu200.Size-menu100.Size*2)<0.01f,"メニューフォントは表示先DPIを1回だけ反映");
   var dpi=typeof(DpiAwareForm).GetMethod("UpdateDpi",BindingFlags.Instance|BindingFlags.NonPublic);
   int originalDpi=(int)Math.Round(((DpiAwareForm)form).UiScale*96);
   var cell=grid.CurrentCell;int scroll=grid.FirstDisplayedScrollingRowIndex;int total=grid.Rows.Count;
   try {
    using(var editor=new EntryEditDialog(Entry("文字サイズ検証"),false))
    using(var menu=new TrayMenu()) {
     editor.Show(form);Application.DoEvents();var name=(TextBox)editor.Controls.Find("EntryName",true)[0];name.Text="変更前の入力を保持";
     menu.Items.Add("アプリを開く");menu.Items.Add(Texts.Restore);menu.Items.Add(new ToolStripSeparator());menu.Items.Add("アプリを終了する");
     menu.Show(new Point(200,200));Application.DoEvents();
     foreach(float textScale in new[]{1f,1.5f,2.25f}) {
      SetTypography(original,textScale);Application.DoEvents();
      foreach(int dpiValue in new[]{96,144,192}) {
       dpi.Invoke(form,new object[]{dpiValue});Application.DoEvents();
       float scale=dpiValue/96f;
       int required=(int)Math.Ceiling(form.Font.GetHeight(dpiValue)+form.Font.GetHeight(dpiValue)*0.95f)+ (int)Math.Round(4*scale);
       Assert(Math.Abs(form.Font.Size-bodyPoints*textScale*96f/72f*scale)<0.03f,"文字 "+textScale+" × DPI "+dpiValue+" で二重拡大なし");
       Assert(rows[0].Height>=required && grid.ColumnHeadersHeight>=Math.Ceiling(form.Font.GetHeight(dpiValue)),"文字 "+textScale+" × DPI "+dpiValue+" の行高と見出しを確保");
       Assert(grid.ColumnHeadersDefaultCellStyle.Font==null || Math.Abs(grid.ColumnHeadersDefaultCellStyle.Font.Size-form.Font.Size)<0.03f,"一覧見出しに文字倍率が重ならない");
       Assert(Math.Abs(editor.Font.Size-bodyPoints*textScale*96f/72f*((DpiAwareForm)editor).UiScale)<0.03f,"別モニタの編集画面に他画面のDPIを混ぜない");
      }
      Assert(name.Text=="変更前の入力を保持" && grid.Rows.Count==total,"文字設定変更でも編集内容と一覧を保持");
      Assert(menu.Items[0].Height>=TextRenderer.MeasureText(menu.Items[0].Text,menu.Font).Height,"メニューは文字サイズに応じて行高を拡張");
      if(textScale==2.25f){dpi.Invoke(form,new object[]{originalDpi});Capture(form,"main-text-225.png");Capture(editor,"edit-text-225.png");CaptureMenu(menu,"tray-text-225.png");}
     }
     Assert(grid.CurrentCell==cell && grid.FirstDisplayedScrollingRowIndex==scroll,"文字サイズとDPIの変更後もフォーカスとスクロールを保持");
     menu.Close();editor.Close();
    }
   } finally {
    UiTypography.RefreshFromWindows();dpi.Invoke(form,new object[]{originalDpi});Application.DoEvents();
   }
   Capture(form,"main-typography.png");
   Rectangle work=new Rectangle(-1920,0,1920,1040),request=new Rectangle(-2000,-80,2400,1400);
   Assert(work.Contains(DpiAwareForm.FitBounds(request,work)),"高DPIで画面より大きくなった窓を作業領域内へ収める");
   using(var menu=new TrayMenu()) {
    menu.Items.Add("アプリを開く");menu.Items.Add(Texts.Restore);menu.Items.Add(new ToolStripSeparator());menu.Items.Add("アプリを終了する");
    menu.Show(new Point(200,200));SetTheme(true,true);Application.DoEvents();CaptureMenu(menu,"tray-typography.png");menu.Close();AppTheme.RefreshFromWindows();
   }
  }
  static void TestLiveTheme(Form form,DataGridView grid,List<DataGridViewRow> rows) {
   var settings=(Button)form.Controls.Find("SettingsButton",true)[0];
   Assert(settings.Text=="設定を開く" && settings.FlatAppearance.BorderSize>0 && settings.Width>=Ui.S(120),"設定は背景と枠のある明確なボタン");
   settings.PerformClick();Assert(settings.Text=="設定を閉じる" && settings.BackColor==AppTheme.Band,"設定を開くとボタンの名前と背景も変更");
   int before=ThemeListeners(),clicked=0;grid.CurrentCell=rows[1].Cells[1];
   var currentCell=grid.CurrentCell;int scroll=grid.FirstDisplayedScrollingRowIndex;
   var sample=Entry("編集中の名前");sample.Process="chat-app";
   try {
    using(var editor=new EntryEditDialog(sample,false))
    using(var menu=new TrayMenu()) {
     menu.Items.Add("アプリを開く",null,delegate{clicked++;});menu.Items.Add(Texts.Restore);menu.Items.Add(new ToolStripSeparator());menu.Items.Add("アプリを終了する");
     editor.Show(form);Application.DoEvents();
     var name=(TextBox)editor.Controls.Find("EntryName",true)[0];name.Text="保存前の編集テキスト";
     menu.Show(new Point(200,200));Application.DoEvents();
     SetTheme(false,true);Application.DoEvents();
     Assert(!AppTheme.IsDark && AppTheme.IsSystemDark,"アプリとWindows側で異なる配色モードを扱える");
     Assert(form.BackColor==AppTheme.Background && grid.BackgroundColor==AppTheme.Surface,"起動中にアプリ本体をライトへ変更");
     Assert(rows[0].DefaultCellStyle.BackColor==AppTheme.ActiveRow && rows[2].DefaultCellStyle.BackColor==AppTheme.Surface,"ライトへ変更してもONとOFFの行色を維持");
     Assert(menu.BackColor==AppTheme.MenuSurface && menu.BackColor.R<80,"トレイメニューはWindows側のダークに追従");
     Assert(editor.BackColor==AppTheme.Background && name.BackColor==AppTheme.Surface,"開いている編集画面も同時にライトへ追従");
     Assert(settings.BackColor==AppTheme.Band && settings.FlatAppearance.BorderColor==AppTheme.Accent,"設定の展開状態もテーマ変更後に維持");
     Capture(form,"main-theme-light.png");Capture(editor,"edit-theme-light.png");menu.Items[0].Select();CaptureMenu(menu,"tray-menu-dark.png");
     Exception backgroundFailure=null;var thread=new Thread(delegate(){try{SetTheme(true,false);}catch(Exception e){backgroundFailure=e;}});thread.Start();thread.Join();
     if(backgroundFailure!=null)throw backgroundFailure;Application.DoEvents();
     Assert(AppTheme.IsDark && !AppTheme.IsSystemDark && form.BackColor==AppTheme.Background,"別スレッドからの変更通知をUIスレッドで反映");
     Assert(menu.BackColor==AppTheme.MenuSurface && menu.BackColor.R>230,"トレイメニューは表示中もライトへ追従");
     Assert(name.Text=="保存前の編集テキスト" && sample.Name=="編集中の名前","配色切替で未保存の入力や元データを変更しない");
     Assert(grid.CurrentCell==currentCell && grid.FirstDisplayedScrollingRowIndex==scroll,"配色切替で行のフォーカスとスクロールを維持");
     Assert((bool)rows[0].Cells[0].Value && !(bool)rows[2].Cells[0].Value,"配色切替で対象のON/OFFを変えない");
     Capture(form,"main-theme-dark.png");Capture(editor,"edit-theme-dark.png");CaptureMenu(menu,"tray-menu-light.png");
     menu.Items[0].PerformClick();Assert(clicked==1 && menu.Items[2] is ToolStripSeparator,"トレイメニューはクリック動作と終了前の区切りを保持");
     menu.Close();editor.Close();
    }
    Assert(ThemeListeners()==before,"編集画面とメニューを閉じたらテーマ購読を解除");
   } finally {AppTheme.RefreshFromWindows();}
   settings.PerformClick();Assert(settings.Text=="設定を開く","設定を閉じた状態へ戻せる");
  }
  static DataGridViewRow ViewFor(DataGridView grid,string key){foreach(DataGridViewRow view in grid.Rows){var row=view.Tag as WindowRow;if(row!=null&&row.Key==key)return view;}throw new Exception("Missing row: "+key);}
  static void TestTrayTransition() {
   Rectangle start=new Rectangle(100,100,800,600),end=new Rectangle(1800,1000,24,24);
   Assert(TrayTransition.Frame(start,end,0)==start && TrayTransition.Frame(start,end,1)==end,"トレイ演出は画面位置から通知領域へ収束");
   var middle=TrayTransition.Frame(start,end,0.5);Assert(middle.Width<start.Width&&middle.Width>end.Width&&middle.X>start.X,"トレイ演出は移動しながら縮小");
   Assert(TrayTransition.Destination().Width>0,"システムトレイの移動先を特定");
   using(var source=new Form {Text="Tray animation isolated test",Bounds=start,ShowInTaskbar=false}) {
    source.Show();Application.DoEvents();var effect=TrayTransition.HideToTray(source);
    Assert(!source.Visible && !source.ShowInTaskbar,"×の演出開始時に元画面をトレイへ収納");
    var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<700 && effect!=null&&!effect.IsDisposed){Application.DoEvents();Thread.Sleep(10);}
    Assert(effect==null || effect.IsDisposed,"短時間の演出後に写し画面を破棄");
    var image=new Bitmap(160,100);using(var g=Graphics.FromImage(image))g.Clear(AppTheme.ActiveRow);
    using(var preview=(TrayTransition)Activator.CreateInstance(typeof(TrayTransition),BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{image,start,end},null)) {
     preview.Show();Application.DoEvents();Assert(!preview.ShowInTaskbar,"演出用の写しはタスクバーに追加しない");
     watch.Restart();while(watch.ElapsedMilliseconds<700&&!preview.IsDisposed){Application.DoEvents();Thread.Sleep(10);}
     Assert(preview.IsDisposed,"アニメーション用タイマーが完了して自動終了");
    }
   }
  }
  static void TestCloseToTray() {
   var store=new ConfigStore(Path.Combine(output,"tray-layout.json"));store.Config=new LayoutConfig{Enabled=false,LaunchMissingAtStartup=false,MinimizeToTrayOnClose=true};
   var controller=new AppController(store,false);Exception failure=null;int phase=0;
   using(var form=new Form {Text="Isolated close-to-tray scenario",Bounds=new Rectangle(200,180,520,340)})
   using(var timer=new System.Windows.Forms.Timer {Interval=350}) {
    timer.Tick+=delegate {
     try {
      if(phase++==0){form.Close();Assert(!form.Visible&&!form.IsDisposed,"×では終了せず演出付きのトレイ収納へ進む");return;}
      Assert(!form.Visible&&!form.IsDisposed,"演出終了後もトレイで常駐を継続");
      timer.Stop();controller.MinimizeToTrayOnClose=false;form.Close();
     } catch(Exception e){failure=e;timer.Stop();store.Config.MinimizeToTrayOnClose=false;form.Close();}
    };
    form.Shown+=delegate{timer.Start();};Shell.Run(form,controller,false);
    if(failure!=null)throw failure;
    Assert(form.IsDisposed,"自動縮小をOFFにした場合は×で通常終了");
   }
  }
  static void TestTrayPlacement() {
   var size=new Size(310,150);
   Rectangle[] areas={new Rectangle(0,0,1920,1040),new Rectangle(-1920,-1080,1920,1040),new Rectangle(1920,200,2560,1400)};
   foreach(var area in areas) {
    foreach(var anchor in new[]{new Point(area.Left+4,area.Top+4),new Point(area.Right-4,area.Top+4),new Point(area.Left+4,area.Bottom+25),new Point(area.Right-4,area.Bottom+25)})
     Assert(area.Contains(TrayMenu.Placement(anchor,size,area)),"正負のモニタ座標・端のトレイでも指定モニタ内へ配置");
   }
   using(var menu=new TrayMenu()) {
    menu.Items.Add("アプリを開く");menu.Items.Add(Texts.Restore);menu.Items.Add(new ToolStripSeparator());menu.Items.Add("アプリを終了する");
    foreach(var screen in Screen.AllScreens) {
     var point=new Point(screen.Bounds.Right-20,screen.Bounds.Bottom-12);
     menu.ShowAt(point);Application.DoEvents();
     Assert(screen.WorkingArea.Contains(menu.Bounds),"表示要求のモニタにメニューを固定: "+screen.DeviceName);
     Assert(Screen.FromRectangle(menu.Bounds).DeviceName==screen.DeviceName,"別モニタの初期Boundsで移動先を誤判定しない");
     menu.Close();Application.DoEvents();
    }
    Native.POINT cursor;Native.GetCursorPos(out cursor);var expected=Screen.FromPoint(new Point(cursor.X,cursor.Y)).WorkingArea;
    // NotifyIconが別モニタの初期座標を渡した場合も、実際の右クリック地点を優先する。
    menu.Show(new Point(areas[2].Left+40,areas[2].Top+40));Application.DoEvents();
    Assert(expected.Contains(menu.Bounds),"通常のトレイ表示は右クリック地点のモニタを優先");
    menu.Close();
   }
  }
  static void TestScrollbars() {
   using(var form=new Form {Text="専用スクロールバー検証",ClientSize=new Size(730,470),StartPosition=FormStartPosition.CenterScreen}) {
    AppTheme.Apply(form);
    var grid=new DataGridView {AllowUserToAddRows=false,RowHeadersVisible=false,BackgroundColor=AppTheme.Surface};
    for(int i=0;i<6;i++)grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="列 "+i,Width=145});
    for(int i=0;i<70;i++)grid.Rows.Add("ウィンドウ "+i,"位置","サイズ","モニタ","状態","編集");
    var host=new GridScrollHost(grid){Bounds=new Rectangle(12,12,490,245)};
    var panel=new ThemedScrollPanel {Bounds=new Rectangle(520,12,190,430)};
    panel.Content.Controls.Add(new Label {Text="編集画面のスクロール領域",Location=new Point(10,10),Size=new Size(380,900)});
    form.Controls.Add(host);form.Controls.Add(panel);form.Show();Application.DoEvents();host.SyncScrollBars();
    var vertical=(ThemedScrollBar)host.Controls.Find("GridVerticalScroll",true)[0];
    var horizontal=(ThemedScrollBar)host.Controls.Find("GridHorizontalScroll",true)[0];
    Assert(horizontal.Visible&&vertical.Visible,"専用の縦横スクロールバーを表示");
    Assert(grid.ScrollBars==ScrollBars.None,"標準スクロールバーの描画を無効にして競合を排除");
    foreach(Control native in grid.Controls)if(native is ScrollBar)Assert(!native.Visible,"ネイティブのスクロールバーは表示しない");
    vertical.Value=12;horizontal.Value=140;panel.ScrollPosition=new Point(100,220);Application.DoEvents();
    Console.WriteLine("Custom scroll row="+grid.FirstDisplayedScrollingRowIndex+" vertical="+vertical.Value+" offset="+grid.HorizontalScrollingOffset+" horizontal="+horizontal.Value);
    Assert(grid.FirstDisplayedScrollingRowIndex==12&&grid.HorizontalScrollingOffset==140,"専用バーの操作が実際の表示位置へ反映");
    int row=grid.FirstDisplayedScrollingRowIndex,offset=grid.HorizontalScrollingOffset;Point position=panel.ScrollPosition;
    foreach(bool dark in new[]{true,false}) {
     SetTheme(dark,dark);Application.DoEvents();
     foreach(var bar in new[]{horizontal,vertical}) {
      Rectangle thumb=(Rectangle)typeof(ThemedScrollBar).GetMethod("ThumbBounds",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bar,null);
      var point=new Point(thumb.Left+thumb.Width/2,thumb.Top+thumb.Height/2);
      var move=typeof(ThemedScrollBar).GetMethod("OnMouseMove",BindingFlags.Instance|BindingFlags.NonPublic);
      move.Invoke(bar,new object[]{new MouseEventArgs(MouseButtons.None,0,point.X,point.Y,0)});Pump(80);
      Assert(LivePixel(bar,point.X,point.Y).ToArgb()==AppTheme.ScrollHover.ToArgb(),"ホバー中のつまみは各配色のホバー色");
      int paints=0;PaintEventHandler handler=delegate{paints++;};bar.Paint+=handler;
      for(int i=0;i<12;i++)move.Invoke(bar,new object[]{new MouseEventArgs(MouseButtons.None,0,point.X,point.Y,0)});
      Pump(350);bar.Paint-=handler;
      Assert(paints<=2,"同じ場所のホバーでは再描画を連発せずチカチカしない");
      Assert(LivePixel(bar,point.X,point.Y).ToArgb()==AppTheme.ScrollHover.ToArgb(),"時間経過後もホバー色を維持");
      typeof(ThemedScrollBar).GetMethod("OnMouseLeave",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bar,new object[]{EventArgs.Empty});Pump(30);
      Assert(LivePixel(bar,point.X,point.Y).ToArgb()==AppTheme.ScrollThumb.ToArgb(),"カーソルが離れたら通常つまみ色へ戻る");
     }
     Assert(grid.FirstDisplayedScrollingRowIndex==row&&grid.HorizontalScrollingOffset==offset,"バーの配色切替で一覧のスクロール位置を維持");
     Assert(panel.ScrollPosition==position,"編集領域のスクロール位置も配色切替で維持");
     Capture(form,dark?"scrollbars-dark.png":"scrollbars-light.png");
    }
    vertical.Value=0;
    typeof(ThemedScrollBar).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(vertical,new object[]{new KeyEventArgs(Keys.End)});
    Assert(vertical.Value==vertical.Maximum&&grid.Rows[grid.Rows.Count-1].Displayed,"Endキーで最後の行まで到達");
    int prior=vertical.Value;vertical.ScrollWheel(120);Assert(vertical.Value<prior,"ホイールでスクロール");
    horizontal.Value=horizontal.Maximum;Assert(grid.HorizontalScrollingOffset>0,"横方向も末尾までスクロール");
    AppTheme.RefreshFromWindows();form.Close();
   }
  }
  static void TestStartupSizing() {
   foreach(var work in new[]{new Rectangle(0,0,640,480),new Rectangle(-1920,0,1920,1040),new Rectangle(0,-900,320,200)})
    foreach(float scale in new[]{1f,1.5f,2f}) {
     var desired=new Size((int)(1100*scale),(int)(700*scale));var frame=new Size((int)(16*scale),(int)(38*scale));int margin=(int)(14*scale);
     Size fit=LedgerForm.FitStartupSize(desired,frame,work,margin);
     Assert(fit.Width<=work.Width&&fit.Height<=work.Height&&fit.Width>0&&fit.Height>0,"初期サイズは小画面・負座標・DPI "+scale+" でも作業領域内");
     if(desired.Width+frame.Width<=work.Width-2*margin&&desired.Height+frame.Height<=work.Height-2*margin)
      Assert(fit==desired+frame,"収まる画面なら必要なサイズを維持");
    }
   var store=new ConfigStore(Path.Combine(output,"startup-size.json"));store.Config=new LayoutConfig{Enabled=false};
   using(var form=(LedgerForm)UiFactory.CreateMainForm(new AppController(store,false))) {
    form.Show();Application.DoEvents();Assert(Screen.FromHandle(form.Handle).WorkingArea.Contains(form.Bounds),"初回表示でタイトルバーを含む全体が表示先画面内");
    var requested=new Size(Math.Max(form.MinimumSize.Width,650),Math.Max(form.MinimumSize.Height,430));form.Size=requested;
    form.FitInitialWindow();Assert(form.Size==requested,"起動後の再表示・更新ではユーザーのサイズを上書きしない");
    form.Close();
   }
  }
 }
}
