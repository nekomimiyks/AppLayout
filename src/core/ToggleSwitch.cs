using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppLayout {
 public static class SwitchRenderer {
  public static Size SizeFor(float dpiScale) {
   float scale=dpiScale*UiTypography.TextScale;
   return new Size((int)Math.Round(56*scale),(int)Math.Round(22*scale));
  }
  public static void Draw(Graphics graphics, Rectangle bounds, bool on, bool enabled, Font font) {
   if(bounds.Width<4 || bounds.Height<4)return;
   Color track=enabled?(on?Color.FromArgb(17,121,86):Color.FromArgb(91,103,113)):Color.FromArgb(110,114,117);
   var state=graphics.Save();
   try {
    graphics.SmoothingMode=SmoothingMode.AntiAlias;
    float d=bounds.Height-1;
    using(var shape=new GraphicsPath()) {
     shape.AddArc(bounds.X,bounds.Y,d,d,90,180);shape.AddArc(bounds.Right-d-1,bounds.Y,d,d,270,180);shape.CloseFigure();
     using(var brush=new SolidBrush(track))graphics.FillPath(brush,shape);
    }
    int inset=Math.Max(2,bounds.Height/9),diameter=bounds.Height-2*inset;
    int knob=on?bounds.Right-inset-diameter:bounds.Left+inset;
    using(var brush=new SolidBrush(Color.White))graphics.FillEllipse(brush,knob,bounds.Top+inset,diameter,diameter);
    var text=new Rectangle(on?bounds.Left+inset:bounds.Left+diameter+inset,bounds.Top,
     bounds.Width-diameter-3*inset,bounds.Height);
    using(var labelFont=new Font(font.FontFamily,(font.Unit==GraphicsUnit.Pixel?font.Size:font.SizeInPoints*96f/72f*Ui.K)*0.84f,FontStyle.Bold,GraphicsUnit.Pixel))
     TextRenderer.DrawText(graphics,on?"ON":"OFF",labelFont,text,Color.White,
      TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
   } finally {graphics.Restore(state);}
  }
 }
 public class ToggleSwitch : CheckBox {
  float DpiScale {get{var form=FindForm() as DpiAwareForm;return form==null?Ui.K:form.UiScale;}}
  int Pixel(int logical){return (int)Math.Round(logical*DpiScale);}
  public ToggleSwitch() {
   SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
   AutoSize=true;Cursor=Cursors.Hand;MinimumSize=Ui.Sz(60,28);
  }
  public override Size GetPreferredSize(Size proposedSize) {
   Size text=TextRenderer.MeasureText(Text,Font);
   Size track=SwitchRenderer.SizeFor(DpiScale);
   return new Size(track.Width+Pixel(4)+(Text.Length>0?Pixel(9)+text.Width:0),Math.Max(track.Height+Pixel(6),text.Height+Pixel(4)));
  }
  protected override void OnPaint(PaintEventArgs e) {
   e.Graphics.Clear(BackColor);
   Size track=SwitchRenderer.SizeFor(DpiScale);int width=track.Width,height=track.Height;
   SwitchRenderer.Draw(e.Graphics,new Rectangle(Pixel(1),(Height-height)/2,width,height),Checked,Enabled,Font);
   int textLeft=width+Pixel(13);
   if(Text.Length>0)TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(textLeft,0,Math.Max(0,Width-textLeft),Height),Enabled?ForeColor:SystemColors.GrayText,
    TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
   if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle,ForeColor,BackColor);
  }
 }
}
