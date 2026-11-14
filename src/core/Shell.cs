using System;
using System.Threading;
using System.Windows.Forms;
namespace AppLayout {
 public static class Shell {
  public static void Run(Form form, AppController controller, bool startupMode) {
   bool exiting = false;
   int stopping = 0;
   TrayTransition transition = null;
   using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName))
   using (var tray = new NotifyIcon())
   using (var menu = new TrayMenu()) {
    Action show = delegate { if(transition!=null && !transition.IsDisposed)transition.Close();transition=null;form.ShowInTaskbar = true; form.Show(); if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal; form.Activate(); form.BringToFront(); Native.SetForegroundWindow(form.Handle); };
    menu.Items.Add("アプリを開く", null, delegate { show(); });
    menu.Items.Add(Texts.Restore, null, delegate { controller.RestoreNow(new System.Collections.Generic.List<WindowRow>()); });
    menu.Items.Add(new ToolStripSeparator());
    menu.Items.Add("アプリを終了する", null, delegate { exiting = true; form.Close(); });
    tray.Icon = AppIcon.Create(); tray.Text = "AppLayout"; tray.ContextMenuStrip = menu; tray.Visible = true; tray.DoubleClick += delegate { show(); };
    form.Resize += delegate { if (form.WindowState == FormWindowState.Minimized) { form.Hide(); form.ShowInTaskbar = false; } };
    form.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!exiting && e.CloseReason == CloseReason.UserClosing && controller.MinimizeToTrayOnClose) { e.Cancel = true; if(transition!=null && !transition.IsDisposed)transition.Close();transition=TrayTransition.HideToTray(form); } };
    form.HandleCreated += delegate { };
    IntPtr handle = form.Handle;
    var thread = new Thread(delegate() { while (showEvent.WaitOne()) { if (Thread.VolatileRead(ref stopping) != 0 || form.IsDisposed) break; try { form.BeginInvoke(show); } catch (InvalidOperationException) { break; } } }); thread.IsBackground = true; thread.Start();
    controller.Start();
    if (startupMode) { form.ShowInTaskbar = false; form.Load += delegate { form.BeginInvoke((Action)delegate { form.Hide(); }); }; form.Opacity = 0; form.Shown += delegate { form.Hide(); form.Opacity = 1; }; }
    try { Application.Run(form); } finally { Interlocked.Exchange(ref stopping,1);if(transition!=null && !transition.IsDisposed)transition.Close();controller.Stop(); tray.Visible = false; showEvent.Set(); thread.Join(); }
   }
  }
 }
}
