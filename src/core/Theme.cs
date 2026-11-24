using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppLayout {
 public static class AppTheme {
  sealed class Palette {
   public readonly bool Dark;
   public readonly Color Background,Surface,ActiveRow,Band,Foreground,Muted,Border,Accent,Primary,OnPrimary,Warning;
   public Palette(bool dark) {
    Dark=dark;
    Background=dark?Color.FromArgb(32,36,36):Color.FromArgb(244,246,245);
    Surface=dark?Color.FromArgb(40,45,45):Color.White;
    ActiveRow=dark?Color.FromArgb(33,65,57):Color.FromArgb(224,242,233);
    Band=dark?Color.FromArgb(48,57,55):Color.FromArgb(233,239,237);
    Foreground=dark?Color.FromArgb(238,243,240):Color.FromArgb(32,40,37);
    Muted=dark?Color.FromArgb(178,192,185):Color.FromArgb(81,96,90);
    Border=dark?Color.FromArgb(66,75,71):Color.FromArgb(212,220,216);
    Accent=dark?Color.FromArgb(138,213,193):Color.FromArgb(7,103,90);
    Primary=dark?Color.FromArgb(160,222,202):Color.FromArgb(19,94,81);
    OnPrimary=dark?Color.FromArgb(18,44,35):Color.White;
    Warning=dark?Color.FromArgb(244,201,141):Color.FromArgb(137,87,25);
   }
  }
  static volatile Palette current=new Palette(ReadDarkMode("AppsUseLightTheme",false));
  static volatile bool systemDark=ReadDarkMode("SystemUsesLightTheme",false);
  static int monitoring;
  static readonly object sync=new object();
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
  public static event EventHandler Changed;
  public static bool IsDark {get{return current.Dark;}}
  public static bool IsSystemDark {get{return systemDark;}}
  public static Color Background {get{return current.Background;}}
  public static Color Surface {get{return current.Surface;}}
  public static Color ActiveRow {get{return current.ActiveRow;}}
  public static Color Band {get{return current.Band;}}
  public static Color Foreground {get{return current.Foreground;}}
  public static Color Muted {get{return current.Muted;}}
  public static Color Border {get{return current.Border;}}
  public static Color Accent {get{return current.Accent;}}
  public static Color Primary {get{return current.Primary;}}
  public static Color OnPrimary {get{return current.OnPrimary;}}
  public static Color Warning {get{return current.Warning;}}
  public static Color ScrollTrack {get{return IsDark?Color.FromArgb(32,36,36):Color.FromArgb(240,244,242);}}
  public static Color ScrollThumb {get{return IsDark?Color.FromArgb(101,119,110):Color.FromArgb(145,163,153);}}
  public static Color ScrollHover {get{return IsDark?Color.FromArgb(137,157,146):Color.FromArgb(106,132,117);}}
  public static Color ScrollPressed {get{return IsDark?Color.FromArgb(166,190,176):Color.FromArgb(74,108,88);}}
  public static Color MenuSurface {get{return systemDark?Color.FromArgb(32,32,32):Color.FromArgb(249,249,249);}}
  public static Color MenuForeground {get{return systemDark?Color.FromArgb(245,245,245):Color.FromArgb(28,28,28);}}
  public static Color MenuMuted {get{return systemDark?Color.FromArgb(170,170,170):Color.FromArgb(102,102,102);}}
  public static Color MenuHover {get{return systemDark?Color.FromArgb(53,53,53):Color.FromArgb(232,232,232);}}
  public static Color MenuBorder {get{return systemDark?Color.FromArgb(66,66,66):Color.FromArgb(211,211,211);}}

  static bool ReadDarkMode(string name,bool fallback) {
   try {
    using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
     object value=key==null?null:key.GetValue(name,null);
     return value==null?fallback:Convert.ToInt32(value)==0;
    }
   } catch{return fallback;}
  }
  static void MonitorWindows() {
   if(Interlocked.CompareExchange(ref monitoring,1,0)!=0)return;
   try {SystemEvents.UserPreferenceChanged+=delegate {RefreshFromWindows();};}
   catch(InvalidOperationException){Interlocked.Exchange(ref monitoring,0);}
   catch(System.Security.SecurityException){Interlocked.Exchange(ref monitoring,0);}
  }
  public static void RefreshFromWindows() {
   MonitorWindows();
   UiTypography.RefreshFromWindows();
   UpdateModes(ReadDarkMode("AppsUseLightTheme",IsDark),ReadDarkMode("SystemUsesLightTheme",IsSystemDark));
  }
  static void UpdateModes(bool appDark,bool windowsDark) {
   lock(sync) {
    if(current.Dark==appDark && systemDark==windowsDark)return;
    current=new Palette(appDark);systemDark=windowsDark;
   }
   var changed=Changed;if(changed!=null)changed(null,EventArgs.Empty);
  }
  public static void Apply(Form form) {
   MonitorWindows();
   UiTypography.Attach(form);form.AutoScaleMode=AutoScaleMode.None;
   form.BackColor=Background;form.ForeColor=Foreground;
   Palette applied=current;
   Action update=delegate {
    if(form.IsDisposed || form.Disposing)return;
    Palette next=current;
    if(applied!=next) {
     form.SuspendLayout();
     try {Recolor(form,applied,next);applied=next;}
     finally{form.ResumeLayout(false);}
     form.Invalidate(true);
    }
    ApplyTitleBar(form);
   };
   EventHandler changed=delegate {
    if(form.IsDisposed || form.Disposing || !form.IsHandleCreated)return;
    if(form.InvokeRequired) {
     try{form.BeginInvoke(update);}catch(InvalidOperationException){}
    } else update();
   };
   Changed+=changed;
   form.HandleCreated+=delegate {update();};
   form.Activated+=delegate {RefreshFromWindows();};
   form.Disposed+=delegate {Changed-=changed;};
  }
  static void ApplyTitleBar(Form form) {
   if(!form.IsHandleCreated)return;
   try {
    int value=IsDark?1:0;
    if(DwmSetWindowAttribute(form.Handle,20,ref value,sizeof(int))!=0)DwmSetWindowAttribute(form.Handle,19,ref value,sizeof(int));
   } catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
  }
  static Color Map(Color color,Color[] before,Color[] after) {
   if(color.IsEmpty || color.A==0)return color;
   for(int i=0;i<before.Length;i++)if(color.ToArgb()==before[i].ToArgb())return after[i];
   return color;
  }
  static Color Back(Color color,Palette a,Palette b) {
   return Map(color,new[]{a.Background,a.Surface,a.ActiveRow,a.Band,a.Primary},new[]{b.Background,b.Surface,b.ActiveRow,b.Band,b.Primary});
  }
  static Color Fore(Color color,Palette a,Palette b) {
   return Map(color,new[]{a.Foreground,a.Muted,a.Accent,a.OnPrimary,a.Warning},new[]{b.Foreground,b.Muted,b.Accent,b.OnPrimary,b.Warning});
  }
  static Color Line(Color color,Palette a,Palette b) {
   return Map(color,new[]{a.Border,a.Primary,a.Muted,a.Accent},new[]{b.Border,b.Primary,b.Muted,b.Accent});
  }
  static void RecolorStyle(DataGridViewCellStyle style,Palette a,Palette b) {
   style.BackColor=Back(style.BackColor,a,b);style.SelectionBackColor=Back(style.SelectionBackColor,a,b);
   style.ForeColor=Fore(style.ForeColor,a,b);style.SelectionForeColor=Fore(style.SelectionForeColor,a,b);
  }
  static void Recolor(Control control,Palette a,Palette b) {
   // 子を先に更新し、親から継承していた色も旧パレットで判定する。
   foreach(Control child in control.Controls)Recolor(child,a,b);
   control.BackColor=Back(control.BackColor,a,b);control.ForeColor=Fore(control.ForeColor,a,b);
   var link=control as LinkLabel;
   if(link!=null) {
    link.LinkColor=Fore(link.LinkColor,a,b);link.ActiveLinkColor=Fore(link.ActiveLinkColor,a,b);
   }
   var button=control as Button;
   if(button!=null) {
    button.FlatAppearance.BorderColor=Line(button.FlatAppearance.BorderColor,a,b);
    button.FlatAppearance.MouseOverBackColor=Back(button.FlatAppearance.MouseOverBackColor,a,b);
    button.FlatAppearance.MouseDownBackColor=Back(button.FlatAppearance.MouseDownBackColor,a,b);
   }
   var grid=control as DataGridView;
   if(grid!=null) {
    grid.BackgroundColor=Back(grid.BackgroundColor,a,b);grid.GridColor=Line(grid.GridColor,a,b);
    RecolorStyle(grid.DefaultCellStyle,a,b);RecolorStyle(grid.ColumnHeadersDefaultCellStyle,a,b);
    RecolorStyle(grid.RowHeadersDefaultCellStyle,a,b);RecolorStyle(grid.RowsDefaultCellStyle,a,b);
    RecolorStyle(grid.AlternatingRowsDefaultCellStyle,a,b);
    foreach(DataGridViewColumn col in grid.Columns)RecolorStyle(col.DefaultCellStyle,a,b);
    foreach(DataGridViewRow row in grid.Rows) {
     if(row.HasDefaultCellStyle)RecolorStyle(row.DefaultCellStyle,a,b);
     foreach(DataGridViewCell cell in row.Cells)if(cell.HasStyle)RecolorStyle(cell.Style,a,b);
    }
   }
  }
  internal static LinkLabel Link(string text,Color color) {
   return new LinkLabel { Text=text, AutoSize=true, TabStop=true, LinkColor=color,
    ActiveLinkColor=Foreground, LinkBehavior=LinkBehavior.HoverUnderline, UseMnemonic=false };
  }
  public static Button Button(string text,bool primary) {
   var button=new Button {
    Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
    MinimumSize=Ui.Sz(96,36),FlatStyle=FlatStyle.Flat,
    BackColor=primary?Primary:Surface,ForeColor=primary?OnPrimary:Foreground,
    Padding=new Padding(Ui.S(12),Ui.S(5),Ui.S(12),Ui.S(5)),Margin=new Padding(Ui.S(8),0,0,0),UseVisualStyleBackColor=false
   };
   button.FlatAppearance.BorderColor=primary?Primary:Border;button.FlatAppearance.BorderSize=1;
   button.FlatAppearance.MouseOverBackColor=primary?Primary:Band;
   button.FlatAppearance.MouseDownBackColor=primary?Primary:Band;
   return button;
  }
  public static void StyleInput(Control input) {
   input.BackColor=Surface;input.ForeColor=Foreground;
   var combo=input as ComboBox;if(combo!=null)combo.FlatStyle=FlatStyle.Flat;
  }
 }
}
