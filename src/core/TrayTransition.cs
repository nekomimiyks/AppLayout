using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppLayout {
 // 操作対象のフォームは即時非表示にし、非アクティブな写しだけを短時間トレイへ移動する。
 internal sealed class TrayTransition : Form {
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string title);
  [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action,uint parameter,out bool value,uint flags);
  [StructLayout(LayoutKind.Sequential)] struct AnimationInfo {public uint Size;public int Minimize;}
  [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")] static extern bool GetAnimation(uint action,uint parameter,ref AnimationInfo value,uint flags);
  readonly Bitmap snapshot;
  readonly Rectangle from,to;
  readonly Timer timer=new Timer {Interval=15};
  readonly Stopwatch elapsed=new Stopwatch();
  const int Duration=220;
  internal static bool MotionEnabled {
   get {
    bool value;
    var animation=new AnimationInfo {Size=(uint)Marshal.SizeOf(typeof(AnimationInfo))};
    if(GetAnimation(0x0048,animation.Size,ref animation,0)&&animation.Minimize==0)return false;
    return !SystemInformation.TerminalServerSession &&
     (!SystemParametersInfo(0x1042,0,out value,0) || value);
   }
  }
  TrayTransition(Bitmap image,Rectangle start,Rectangle end) {
   snapshot=image;from=start;to=end;
   FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;AutoScaleMode=AutoScaleMode.None;
   StartPosition=FormStartPosition.Manual;Bounds=start;Opacity=0.94;
   DoubleBuffered=true;BackColor=AppTheme.Surface;
   timer.Tick+=delegate {
    double t=Math.Min(1,elapsed.Elapsed.TotalMilliseconds/Duration);
    if(t>=1){Close();return;}
    Bounds=Frame(from,to,t);Opacity=0.94*(1-t);Invalidate();
   };
  }
  protected override bool ShowWithoutActivation {get{return true;}}
  protected override CreateParams CreateParams {
   get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x00000020|0x00000080;return p;}
  }
  protected override void OnPaint(PaintEventArgs e) {
   e.Graphics.InterpolationMode=InterpolationMode.HighQualityBilinear;
   e.Graphics.DrawImage(snapshot,ClientRectangle);
  }
  protected override void OnShown(EventArgs e){base.OnShown(e);elapsed.Start();timer.Start();}
  protected override void Dispose(bool disposing){if(disposing){timer.Dispose();snapshot.Dispose();}base.Dispose(disposing);}
  internal static Rectangle Frame(Rectangle start,Rectangle end,double progress) {
   double t=Math.Max(0,Math.Min(1,progress));t=t*t*(3-2*t);
   return new Rectangle((int)Math.Round(start.X+(end.X-start.X)*t),(int)Math.Round(start.Y+(end.Y-start.Y)*t),
    Math.Max(1,(int)Math.Round(start.Width+(end.Width-start.Width)*t)),Math.Max(1,(int)Math.Round(start.Height+(end.Height-start.Height)*t)));
  }
  internal static Rectangle Destination() {
   IntPtr taskbar=FindWindow("Shell_TrayWnd",null),area=FindWindowEx(taskbar,IntPtr.Zero,"TrayNotifyWnd",null);
   Native.RECT rect;
   if(area!=IntPtr.Zero && Native.GetWindowRect(area,out rect) && rect.Right>rect.Left && rect.Bottom>rect.Top)
    return new Rectangle((rect.Left+rect.Right)/2-12,(rect.Top+rect.Bottom)/2-12,24,24);
   var screen=Screen.PrimaryScreen;var bounds=screen.Bounds;var work=screen.WorkingArea;
   if(taskbar!=IntPtr.Zero && Native.GetWindowRect(taskbar,out rect)) {
    if(rect.Right-rect.Left>rect.Bottom-rect.Top)return new Rectangle(rect.Right-48,(rect.Top+rect.Bottom)/2-12,24,24);
    return new Rectangle((rect.Left+rect.Right)/2-12,rect.Bottom-48,24,24);
   }
   return new Rectangle(Math.Min(bounds.Right-24,work.Right-24),Math.Min(bounds.Bottom-24,work.Bottom-24),24,24);
  }
  internal static TrayTransition HideToTray(Form source) {
   TrayTransition transition=null;
   if(MotionEnabled && source.Visible && source.WindowState!=FormWindowState.Minimized) {
    Bitmap image=null;
    try {
     image=new Bitmap(source.Width,source.Height);source.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
     transition=new TrayTransition(image,source.Bounds,Destination());transition.Show();image=null;
    } catch(ExternalException){if(image!=null)image.Dispose();if(transition!=null)transition.Dispose();transition=null;}
      catch(ArgumentException){if(image!=null)image.Dispose();if(transition!=null)transition.Dispose();transition=null;}
   }
   source.Hide();source.ShowInTaskbar=false;
   return transition;
  }
 }
}
