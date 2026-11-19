using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppLayout {
 // .NET Framework 4にはPer-Monitor DPIの自動レイアウトがないため明示的に拡縮する。
 public class DpiAwareForm : Form {
  [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
  float uiScale = Ui.K;
  bool scaling;
  public float UiScale { get { return uiScale; } }
  protected override void OnLoad(EventArgs e) {
   try { uint dpi = GetDpiForWindow(Handle); if (dpi > 0) UpdateDpi((int)dpi); }
   catch (EntryPointNotFoundException) { }
   ConstrainToScreen(Bounds);
   base.OnLoad(e);
  }
  protected override void WndProc(ref Message message) {
   if (message.Msg == 0x02E0 && message.LParam != IntPtr.Zero) {
    int dpi = (int)(message.WParam.ToInt64() & 0xffff);
    var bounds = (Native.RECT)Marshal.PtrToStructure(message.LParam, typeof(Native.RECT));
    UpdateDpi(dpi);
    ConstrainToScreen(Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
    message.Result = IntPtr.Zero; return;
   }
   base.WndProc(ref message);
   if(message.Msg==0x001A || message.Msg==0x031A || message.Msg==0x0015)AppTheme.RefreshFromWindows();
  }
  void UpdateDpi(int dpi) {
   if (dpi < 48 || dpi > 768 || scaling) return;
   float next = dpi / 96f;
   Ui.SetDpi(dpi);
   if (Math.Abs(next - uiScale) < 0.001f) return;
   float factor = next / uiScale;
   scaling = true; SuspendLayout();
   try { uiScale = next; Scale(new SizeF(factor, factor)); UiTypography.ApplyDpi(this); OnUiScaleChanged(factor); }
   finally { ResumeLayout(true); scaling = false; }
  }
  protected virtual void OnUiScaleChanged(float factor) { }
  void ConstrainToScreen(Rectangle requested) {
   Rectangle fit=FitBounds(requested,Screen.FromRectangle(requested).WorkingArea);
   MinimumSize=new Size(Math.Min(MinimumSize.Width,fit.Width),Math.Min(MinimumSize.Height,fit.Height));
   Bounds=fit;
  }
  internal static Rectangle FitBounds(Rectangle requested,Rectangle area) {
   int width=Math.Min(requested.Width,area.Width),height=Math.Min(requested.Height,area.Height);
   return new Rectangle(Math.Max(area.Left,Math.Min(requested.Left,area.Right-width)),Math.Max(area.Top,Math.Min(requested.Top,area.Bottom-height)),width,height);
  }
  internal void NotifyTextMetricsChanged(){OnTextMetricsChanged();}
  protected virtual void OnTextMetricsChanged() { }
 }
}
