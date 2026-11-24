using System;
using System.Threading;
using System.Windows.Forms;
namespace AppLayout {
 public static class Shell {
  public static void Run(Form form, AppController controller, bool startupMode) {
   bool exiting = false;
   int languageRefreshPending = 0;
   var ui = SynchronizationContext.Current as WindowsFormsSynchronizationContext ?? new WindowsFormsSynchronizationContext();
   TrayTransition transition = null;
   using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName))
   using (var stopEvent = new ManualResetEvent(false))
   using (var icon = AppIcon.Create())
   using (var tray = new NotifyIcon())
   using (var menu = new TrayMenu())
   using (var updateTimer = new System.Windows.Forms.Timer { Interval = 24 * 60 * 60 * 1000 }) {
    Action refreshLanguage = delegate {
     if(!form.IsDisposed && !form.Disposing && L.Selection=="auto")L.SetLanguage("auto");
    };
    Action show = delegate {
     if(exiting || form.IsDisposed || form.Disposing)return;
     if(transition!=null && !transition.IsDisposed)transition.Close();
     transition=null;
     form.ShowInTaskbar=true;
     if(!form.Visible)TrayTransition.SetWindowsTransitions(form.Handle,true);
     if(form.WindowState==FormWindowState.Minimized)form.WindowState=FormWindowState.Normal;
     form.Show();
     Form target=form;
     foreach(Form owned in form.OwnedForms)if(owned.Modal && owned.Visible)target=owned;
     target.Activate();target.BringToFront();Native.SetForegroundWindow(target.Handle);
    };
    menu.Items.Add(L.T("アプリを開く"), null, delegate { show(); });
    menu.Items.Add(Texts.Restore, null, delegate {
     try{controller.RestoreNow(new System.Collections.Generic.List<WindowRow>());}
     catch(Exception ex){MessageBox.Show(ex.Message,L.T("配置を変更できませんでした"),MessageBoxButtons.OK,MessageBoxIcon.Error);}
    });
    menu.Items.Add(new ToolStripSeparator());
    var exitItem = menu.Items.Add(L.T("アプリを終了する"), null, delegate { exiting = true; form.Close(); });
    ReleaseCheck.Update available = null;
    Version notified = null;
    bool checking = false, installing = false;
    var updateItem = new ToolStripMenuItem { Visible = false };
    menu.Items.Insert(2,updateItem);
    tray.BalloonTipClicked += delegate { show(); };
    Action refreshUpdate = delegate {
     updateItem.Visible = controller.CheckForUpdates && available!=null;
     updateItem.Enabled = !installing;
     var version=controller.CheckForUpdates && available!=null ? available.Version : null;
     if(version!=null)updateItem.Text=L.F("v{0} に更新",version);
     controller.SetUpdateStatus(version,installing);
    };
    EventHandler installRelease = delegate {
     if(exiting || installing || available==null || !controller.CheckForUpdates)return;
     var selected=available;
     installing=true;
     refreshUpdate();
     ThreadPool.QueueUserWorkItem(delegate {
      string path=null;
      try { path=ReleaseCheck.Download(selected); }
      catch(System.Net.WebException) { }
      catch(System.IO.IOException) { }
      catch(UnauthorizedAccessException) { }
      try { ui.Post(delegate {
       if(exiting || !controller.CheckForUpdates) {
        installing=false;
        if(path!=null)ReleaseCheck.RemoveDownload(path);
        return;
       }
       if(path!=null) {
        try {
         var setup=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
         setup.Exited+=delegate {
          setup.Dispose();
          ReleaseCheck.RemoveDownload(path);
          try { ui.Post(delegate { if(!exiting){installing=false;refreshUpdate();} },null); }
          catch(InvalidOperationException) { }
         };
         setup.EnableRaisingEvents=true;
         return;
        } catch(System.ComponentModel.Win32Exception) { ReleaseCheck.RemoveDownload(path); }
       }
       installing=false;
       refreshUpdate();
       MessageBox.Show(form,L.T("アップデートを開始できませんでした。時間をおいて再試行してください。"),
        "AppLayout",MessageBoxButtons.OK,MessageBoxIcon.Error);
      },null); } catch(InvalidOperationException) { if(path!=null)ReleaseCheck.RemoveDownload(path); }
     });
    };
    updateItem.Click += installRelease;
    Action checkRelease = delegate {
     if(exiting || checking || ReleaseCheck.InstalledVersion==null || !controller.CheckForUpdates)return;
     checking=true;
     ThreadPool.QueueUserWorkItem(delegate {
      ReleaseCheck.Update latest=null;
      try { latest=ReleaseCheck.CheckLatest(); }
      catch(System.Net.WebException) { }
      catch(System.IO.IOException) { }
      catch(System.Runtime.Serialization.SerializationException) { }
      try { ui.Post(delegate {
       checking=false;
       if(exiting || !controller.CheckForUpdates || latest==null)return;
       available=latest;
       refreshUpdate();
       if(latest.Version==notified)return;
       notified=latest.Version;
       tray.ShowBalloonTip(6500,"AppLayout",L.F("新しいバージョン v{0} があります。設定から更新できます。",latest.Version),ToolTipIcon.Info);
      },null); } catch(InvalidOperationException) { }
     });
    };
    EventHandler updateSettingChanged = delegate {
     refreshUpdate();
     if(ReleaseCheck.InstalledVersion!=null && controller.CheckForUpdates) {
      updateTimer.Start();
      checkRelease();
     } else updateTimer.Stop();
    };
    updateTimer.Tick += delegate { checkRelease(); };
    EventHandler languageChanged=delegate {
     menu.Items[0].Text=L.T("アプリを開く");menu.Items[1].Text=Texts.Restore;exitItem.Text=L.T("アプリを終了する");
     menu.AccessibleName=L.T("AppLayout のメニュー");
     refreshUpdate();
    };
    Microsoft.Win32.UserPreferenceChangedEventHandler windowsLanguageChanged=delegate {
     if(Interlocked.Exchange(ref languageRefreshPending,1)!=0)return;
     try{ui.Post(delegate {Interlocked.Exchange(ref languageRefreshPending,0);refreshLanguage();},null);}
     catch(InvalidOperationException){Interlocked.Exchange(ref languageRefreshPending,0);}
    };
    menu.Opening+=delegate {refreshLanguage();};
    form.Activated+=delegate {refreshLanguage();};
    tray.Icon = icon; tray.Text = "AppLayout"; tray.ContextMenuStrip = menu; tray.Visible = true; tray.DoubleClick += delegate { show(); };
    form.Resize += delegate { if (form.WindowState == FormWindowState.Minimized) { form.Hide(); form.ShowInTaskbar = false; } };
    form.FormClosing += delegate(object sender, FormClosingEventArgs e) {
     if (exiting || e.CloseReason != CloseReason.UserClosing || !controller.MinimizeToTrayOnClose) return;
     e.Cancel=true;
     if (!form.Visible) return;
     if (transition!=null && !transition.IsDisposed) transition.Close();
     transition=TrayTransition.HideToTray(form);
    };
    var thread=new Thread(delegate() {
     var events=new WaitHandle[]{stopEvent,showEvent};
     try{while(WaitHandle.WaitAny(events)==1)ui.Post(delegate {show();},null);}
     catch(InvalidOperationException){} // UIスレッドの終了中
    }){IsBackground=true};
    L.Changed+=languageChanged;
    controller.UpdateCheckingChanged+=updateSettingChanged;
    controller.UpdateRequested+=installRelease;
    Microsoft.Win32.SystemEvents.UserPreferenceChanged+=windowsLanguageChanged;
    thread.Start();
    try {
     controller.Start();
     updateSettingChanged(null,EventArgs.Empty);
     if (!startupMode) Application.Run(form);
     else {
      form.ShowInTaskbar=false;
      // MainForm に渡すとメッセージループ開始時に表示されるため、通知領域からの要求まで隠しておく。
      using(var context = new ApplicationContext()) {
       form.FormClosed+=delegate {context.ExitThread();};
       var handle=form.Handle;
       Application.Run(context);
      }
     }
    }
    finally {
     exiting=true;
     Microsoft.Win32.SystemEvents.UserPreferenceChanged-=windowsLanguageChanged;
     L.Changed-=languageChanged;
     controller.UpdateCheckingChanged-=updateSettingChanged;
     controller.UpdateRequested-=installRelease;
     updateTimer.Stop();
     stopEvent.Set();thread.Join();
     if(transition!=null && !transition.IsDisposed)transition.Close();
     controller.Stop();tray.Visible=false;
    }
   }
  }
 }
}
