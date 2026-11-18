using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppLayout {
 // 元画面から非アクティブな写しに切れ目なく切り替え、写しだけを短時間トレイへ移動する。
 internal sealed class TrayTransition : Form {
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string title);
  [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action,uint parameter,out bool value,uint flags);
  [StructLayout(LayoutKind.Sequential)] struct AnimationInfo {public uint Size;public int Minimize;}
  [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")] static extern bool GetAnimation(uint action,uint parameter,ref AnimationInfo value,uint flags);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out Native.RECT rect,int size);
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
  [DllImport("dwmapi.dll")] static extern int DwmFlush();
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window,IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
  [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr item);
  [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc,int mode);
  [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dest,int x,int y,int width,int height,
   IntPtr source,int sx,int sy,int sw,int sh,uint operation);
  [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr window,IntPtr screen,
   ref Native.POINT destination,ref Native.POINT size,IntPtr source,ref Native.POINT origin,
   uint key,IntPtr blend,uint flags);
  readonly Bitmap snapshot;
  readonly Rectangle from,to;
  readonly Timer timer=new Timer {Interval=15};
  readonly Stopwatch elapsed=new Stopwatch();
  IntPtr sourceDc,frameDc,sourceBitmap,frameBitmap,oldSource,oldFrame;
  Rectangle displayed;
  const int Duration=220;
  const int Halftone=4;
  const uint SourceCopy=0x00CC0020;
  const uint LayeredOpaque=4;
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
   StartPosition=FormStartPosition.Manual;Bounds=start;
   timer.Tick+=delegate {
    double t=Math.Min(1,elapsed.Elapsed.TotalMilliseconds/Duration);
    if(t>=1 || !Present(Frame(from,to,t)))Close();
   };
  }
  protected override bool ShowWithoutActivation {get{return true;}}
  protected override void OnHandleCreated(EventArgs e) {
   base.OnHandleCreated(e);
   // 演出用ウィンドウにWindows側の表示・非表示アニメーションを重ねない。
   SetWindowsTransitions(Handle,false);
  }
  internal static void SetWindowsTransitions(IntPtr window,bool enabled) {
   const int transitionsForcedDisabled=3;
   int disable=enabled?0:1;
   DwmSetWindowAttribute(window,transitionsForcedDisabled,ref disable,sizeof(int));
  }
  protected override CreateParams CreateParams {
   get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x00000020|0x00000080|0x00080000;return p;}
  }
  internal bool ShowFirstFrame() {
   IntPtr screen=GetDC(IntPtr.Zero);
   if(screen==IntPtr.Zero)return false;
   try {
    sourceDc=CreateCompatibleDC(screen);
    frameDc=CreateCompatibleDC(screen);
    sourceBitmap=snapshot.GetHbitmap();
    frameBitmap=CreateCompatibleBitmap(screen,from.Width,from.Height);
    if(sourceDc==IntPtr.Zero || frameDc==IntPtr.Zero || sourceBitmap==IntPtr.Zero || frameBitmap==IntPtr.Zero)return false;
    oldSource=SelectObject(sourceDc,sourceBitmap);
    oldFrame=SelectObject(frameDc,frameBitmap);
    if(oldSource==IntPtr.Zero || oldFrame==IntPtr.Zero)return false;
    SetStretchBltMode(frameDc,Halftone);
    // 枠を出す前に画像を入れる。空の描画面が表示される時間を作らない。
    if(!Present(from))return false;
    Show();
    return DwmFlush()==0;
   }
   finally {ReleaseDC(IntPtr.Zero,screen);}
  }
  bool Present(Rectangle bounds) {
   if(bounds==displayed)return true;
   if(!StretchBlt(frameDc,0,0,bounds.Width,bounds.Height,sourceDc,0,0,snapshot.Width,snapshot.Height,SourceCopy))
    return false;
   var point=new Native.POINT{X=bounds.X,Y=bounds.Y};
   var size=new Native.POINT{X=bounds.Width,Y=bounds.Height};
   var origin=new Native.POINT();
   // 画像とその位置・寸法を一度に更新する。
   if(!UpdateLayeredWindow(Handle,IntPtr.Zero,ref point,ref size,frameDc,ref origin,0,IntPtr.Zero,LayeredOpaque))return false;
   displayed=bounds;
   return true;
  }
  internal void StartAnimation() {
   elapsed.Start();
   timer.Start();
  }
  protected override void Dispose(bool disposing) {
   if(disposing) {
    timer.Dispose();
    ReleaseSurface(ref frameDc,ref frameBitmap,oldFrame);
    ReleaseSurface(ref sourceDc,ref sourceBitmap,oldSource);
    snapshot.Dispose();
   }
   base.Dispose(disposing);
  }
  static void ReleaseSurface(ref IntPtr dc,ref IntPtr bitmap,IntPtr previous) {
   if(dc!=IntPtr.Zero) {
    if(previous!=IntPtr.Zero)SelectObject(dc,previous);
    DeleteDC(dc);
    dc=IntPtr.Zero;
   }
   if(bitmap!=IntPtr.Zero){DeleteObject(bitmap);bitmap=IntPtr.Zero;}
  }
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
     Rectangle bounds;
     if(TryGetCaptureBounds(source,out bounds)) {
      image=new Bitmap(bounds.Width,bounds.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb);
      using(var graphics=Graphics.FromImage(image))
       graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);
      var tray=Destination();
      var end=new Rectangle(tray.X+tray.Width/2-4,tray.Y+tray.Height/2-4,8,8);
      transition=new TrayTransition(image,bounds,end);
      image=null;
      if(!transition.ShowFirstFrame()) {
       transition.Dispose();
       transition=null;
      }
     }
    } catch(ExternalException){if(transition!=null)transition.Dispose();transition=null;}
      catch(ArgumentException){if(transition!=null)transition.Dispose();transition=null;}
      finally{if(image!=null)image.Dispose();}
   }
   if(source.Visible) {
    // 元画面がフェードして写しと重なるのを防ぎ、即時収納でも消え際の色を変えない。
    SetWindowsTransitions(source.Handle,false);
   }
   // 非表示にすればタスクバーからも消える。ここで ShowInTaskbar を変更すると HWND が再作成される。
   source.Hide();
   if(transition!=null)transition.StartAnimation();
   return transition;
  }
  static bool TryGetCaptureBounds(Form source,out Rectangle bounds) {
   const int extendedFrameBounds=9;
   const int cloakedAttribute=14;
   const uint windowAbove=3;
   var window=source.Handle;
   bounds=Rectangle.Empty;
   Native.RECT frame;
   if(DwmGetWindowAttribute(window,extendedFrameBounds,out frame,Marshal.SizeOf(typeof(Native.RECT)))!=0)return false;
   bounds=Rectangle.FromLTRB(frame.Left,frame.Top,frame.Right,frame.Bottom);
   if(bounds.Width<=0 || bounds.Height<=0)return false;

   // 画面外や他の窓に重なる場合は、その画素を演出に持ち込まない。
   using(var outside=new Region(bounds))
   using(var graphics=source.CreateGraphics()) {
    foreach(var screen in Screen.AllScreens)outside.Exclude(screen.Bounds);
    if(!outside.IsEmpty(graphics))return false;
   }
   IntPtr other=GetWindow(window,windowAbove);
   for(int i=0;other!=IntPtr.Zero && i<1024;i++,other=GetWindow(other,windowAbove)) {
    if(!Native.IsWindowVisible(other) || Native.IsIconic(other))continue;
    int cloaked;
    if(Native.DwmGetWindowAttribute(other,cloakedAttribute,out cloaked,sizeof(int))==0 && cloaked!=0)continue;
    Native.RECT rect;
    if(Native.GetWindowRect(other,out rect) &&
       bounds.IntersectsWith(Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom)))return false;
   }
   return other==IntPtr.Zero;
  }
 }
}
