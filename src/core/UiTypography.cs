using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppLayout {
 // DPI=96のWindows標準フォントを基準にする。文字拡大は既にLOGFONTに含まれる。
 // 古いWinFormsのシステムDPI依存を避け、本文もPixelで表示先DPIを1回掛ける。
 public static class UiTypography {
  [StructLayout(LayoutKind.Sequential,CharSet=System.Runtime.InteropServices.CharSet.Unicode)] struct LogFont {
   public int Height,Width,Escapement,Orientation,Weight;
   public byte Italic,Underline,StrikeOut,CharSet,OutPrecision,ClipPrecision,Quality,PitchAndFamily;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string FaceName;
  }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Metrics {
   public uint Size;public int BorderWidth,ScrollWidth,ScrollHeight,CaptionWidth,CaptionHeight;
   public LogFont Caption;public int SmallCaptionWidth,SmallCaptionHeight;public LogFont SmallCaption;
   public int MenuWidth,MenuHeight;public LogFont Menu,Status,Message;public int PaddedBorder;
  }
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SystemParametersInfoForDpi(uint action,uint parameter,ref Metrics metrics,uint flags,uint dpi);
  sealed class Definition {
   public readonly string BodyName,MenuName;public readonly float BodyPoints,MenuPoints;public readonly FontStyle BodyStyle,MenuStyle;
   public Definition(string body,float bodyPoints,FontStyle bodyStyle,string menu,float menuPoints,FontStyle menuStyle) {
    BodyName=body;BodyPoints=bodyPoints;BodyStyle=bodyStyle;MenuName=menu;MenuPoints=menuPoints;MenuStyle=menuStyle;
   }
   public bool Same(Definition other){return BodyName==other.BodyName&&MenuName==other.MenuName&&Math.Abs(BodyPoints-other.BodyPoints)<0.01f&&Math.Abs(MenuPoints-other.MenuPoints)<0.01f&&BodyStyle==other.BodyStyle&&MenuStyle==other.MenuStyle;}
  }
  static volatile Definition current=Read();
  static readonly Dictionary<Form,Binding> bindings=new Dictionary<Form,Binding>();
  public static event EventHandler Changed;
  public static float BodyPoints {get{return current.BodyPoints;}}
  public static float TextScale {get{return Math.Max(1,current.BodyPoints/9f);}}
  public static Font BodyFont(){return BodyFont(1,FontStyle.Regular);}
  public static Font BodyFont(float relativeSize,FontStyle style) {
   return BodyFont(relativeSize,style,Ui.K);
  }
  public static Font BodyFont(float relativeSize,FontStyle style,float dpiScale) {
   var value=current;return new Font(value.BodyName,Math.Max(6,value.BodyPoints*relativeSize)*96f/72f*dpiScale,style|value.BodyStyle,GraphicsUnit.Pixel);
  }
  public static Font MenuFont(float dpiScale) {
   var value=current;return new Font(value.MenuName,value.MenuPoints*96f/72f*dpiScale,value.MenuStyle,GraphicsUnit.Pixel);
  }
  public static void RefreshFromWindows(){Update(Read());}
  static void Update(Definition value){if(current.Same(value))return;current=value;var handler=Changed;if(handler!=null)handler(null,EventArgs.Empty);}
  static FontStyle Style(LogFont font){return(font.Weight>=700?FontStyle.Bold:FontStyle.Regular)|(font.Italic!=0?FontStyle.Italic:FontStyle.Regular);}
  static Definition Read() {
   try {
    var metrics=new Metrics{Size=(uint)Marshal.SizeOf(typeof(Metrics))};
    if(SystemParametersInfoForDpi(0x0029,metrics.Size,ref metrics,0,96) && metrics.Message.Height!=0 && metrics.Menu.Height!=0)
     return new Definition(metrics.Message.FaceName,Math.Abs(metrics.Message.Height)*72f/96f,Style(metrics.Message),metrics.Menu.FaceName,Math.Abs(metrics.Menu.Height)*72f/96f,Style(metrics.Menu));
   } catch(EntryPointNotFoundException){}catch(DllNotFoundException){}
   // 古いWindowsでも、DPIをポイント値に二重掛けしない。
   var body=SystemFonts.MessageBoxFont;var menu=SystemFonts.MenuFont;
   return new Definition(body.Name,body.SizeInPoints,body.Style,menu.Name,menu.SizeInPoints,menu.Style);
  }
  public static void Attach(Form form){var binding=new Binding(form);bindings.Add(form,binding);}
  internal static void ApplyDpi(Form form){Binding binding;if(bindings.TryGetValue(form,out binding))binding.Apply();}
  sealed class Binding {
   readonly Form form;Definition applied;float appliedDpi;
   List<Font> owned=new List<Font>();
   public Binding(Form owner) {
    form=owner;applied=current;appliedDpi=Ui.K;
    Font body=BodyFont();owned.Add(body);form.Font=body;
    Changed+=OnChanged;
    form.HandleCreated+=delegate{Apply();};
    form.Disposed+=delegate {Changed-=OnChanged;bindings.Remove(form);foreach(var font in owned)font.Dispose();owned.Clear();};
   }
   void OnChanged(object sender,EventArgs e) {
    if(form.IsDisposed||form.Disposing||!form.IsHandleCreated)return;
    if(form.InvokeRequired){try{form.BeginInvoke((Action)Apply);}catch(InvalidOperationException){}}
    else Apply();
   }
   public void Apply() {
    if(form.IsDisposed||form.Disposing)return;
    var next=current;var dpiForm=form as DpiAwareForm;float nextDpi=dpiForm==null?Ui.K:dpiForm.UiScale;
    bool sameDpi=Math.Abs(appliedDpi-nextDpi)<0.001f;
    if(applied.Same(next)&&sameDpi)return;
    if(sameDpi&&applied.BodyName==next.BodyName && Math.Abs(applied.BodyPoints-next.BodyPoints)<0.01f && applied.BodyStyle==next.BodyStyle){applied=next;return;}
    var created=new Dictionary<string,Font>();var previous=owned;owned=new List<Font>();
    Func<Font,Font> replace=delegate(Font font) {
     float oldPixels=font.Unit==GraphicsUnit.Pixel?font.Size:font.SizeInPoints*96f/72f*appliedDpi;
     float pixels=oldPixels/applied.BodyPoints*next.BodyPoints/appliedDpi*nextDpi;
     string key=pixels.ToString("R",CultureInfo.InvariantCulture)+":"+(int)font.Style;
     Font result;if(!created.TryGetValue(key,out result)){
      result=new Font(next.BodyName,Math.Max(1,pixels),font.Style,GraphicsUnit.Pixel);
      if(result.Equals(font)){result.Dispose();result=font;if(previous.Remove(font))owned.Add(font);}
      else owned.Add(result);
      created.Add(key,result);
     }return result;
    };
    form.SuspendLayout();
    try {
     ReplaceFonts(form,replace);applied=next;appliedDpi=nextDpi;
     if(dpiForm!=null)dpiForm.NotifyTextMetricsChanged();
    } finally {form.ResumeLayout(true);foreach(var font in previous)font.Dispose();}
    form.Invalidate(true);
   }
  }
  static void QueueStyle(DataGridViewCellStyle style,Func<Font,Font> replace,List<Action> changes){var font=style.Font;if(font!=null)changes.Add(delegate{style.Font=replace(font);});}
  static void ReplaceFonts(Control control,Func<Font,Font> replace) {
   var changes=new List<Action>();QueueFonts(control,replace,changes);foreach(var change in changes)change();
  }
  static void QueueFonts(Control control,Func<Font,Font> replace,List<Action> changes) {
   foreach(Control child in control.Controls)QueueFonts(child,replace,changes);
   var original=control.Font;changes.Add(delegate{control.Font=replace(original);});
   var grid=control as DataGridView;if(grid==null)return;
   // Control.Fontの変更がDataGridViewのスタイルへ伝播する前の値を保存し、倍率を再適用しない。
   QueueStyle(grid.DefaultCellStyle,replace,changes);QueueStyle(grid.ColumnHeadersDefaultCellStyle,replace,changes);QueueStyle(grid.RowHeadersDefaultCellStyle,replace,changes);
   foreach(DataGridViewColumn column in grid.Columns)QueueStyle(column.DefaultCellStyle,replace,changes);
   foreach(DataGridViewRow row in grid.Rows){if(row.HasDefaultCellStyle)QueueStyle(row.DefaultCellStyle,replace,changes);foreach(DataGridViewCell cell in row.Cells)if(cell.HasStyle)QueueStyle(cell.Style,replace,changes);}
  }
 }
}
