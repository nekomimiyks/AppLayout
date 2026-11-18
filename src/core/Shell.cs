using System;
using System.Threading;
using System.Windows.Forms;
namespace AppLayout {
 public static class Shell {
  public static void Run(Form form, AppController controller, bool startupMode) {
   bool exiting = false;
   int languageRefreshPending = 0;
   var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
   TrayTransition transition = null;
   using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName))
   using (var stopEvent = new ManualResetEvent(false))
   using (var icon = AppIcon.Create())
   using (var tray = new NotifyIcon())
   using (var menu = new TrayMenu()) {
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
    menu.Items.Add(L.T("アプリを終了する"), null, delegate { exiting = true; form.Close(); });
    EventHandler languageChanged=delegate {
     menu.Items[0].Text=L.T("アプリを開く");menu.Items[1].Text=Texts.Restore;menu.Items[3].Text=L.T("アプリを終了する");
     menu.AccessibleName=L.T("AppLayout のメニュー");
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
    Microsoft.Win32.SystemEvents.UserPreferenceChanged+=windowsLanguageChanged;
    thread.Start();
    try {
     controller.Start();
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
     stopEvent.Set();thread.Join();
     if(transition!=null && !transition.IsDisposed)transition.Close();
     controller.Stop();tray.Visible=false;
    }
   }
  }
 }
}
