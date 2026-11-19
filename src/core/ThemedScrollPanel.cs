using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AppLayout {
 // 標準スクロールバーを表示せず、専用部品でコンテンツの表示位置を管理する。
 public class ThemedScrollPanel : UserControl {
  readonly Panel viewport=new Panel {AutoScroll=false,Margin=Padding.Empty};
  readonly Panel content=new Panel {AutoScroll=false,Margin=Padding.Empty};
  readonly ThemedScrollBar vertical=new ThemedScrollBar {Orientation=Orientation.Vertical,AccessibleName=L.T("縦スクロール")};
  readonly ThemedScrollBar horizontal=new ThemedScrollBar {Orientation=Orientation.Horizontal,AccessibleName=L.T("横スクロール")};
  readonly HashSet<Control> watched=new HashSet<Control>();
  bool arranging;
  public Panel Content {get{return content;}}
  public Point ScrollPosition {get{return new Point(horizontal.Value,vertical.Value);}set{horizontal.Value=value.X;vertical.Value=value.Y;}}
  public ThemedScrollPanel() {
   AutoScroll=false;DoubleBuffered=true;Margin=Padding.Empty;
   BackColor=AppTheme.ScrollTrack;viewport.BackColor=content.BackColor=AppTheme.Surface;
   viewport.Controls.Add(content);Controls.Add(viewport);Controls.Add(vertical);Controls.Add(horizontal);
   vertical.ValueChanged+=delegate{MoveContent();};horizontal.ValueChanged+=delegate{MoveContent();};
   content.ControlAdded+=delegate(object sender,ControlEventArgs e){Watch(e.Control);PerformLayout();};
   content.ControlRemoved+=delegate(object sender,ControlEventArgs e){Unwatch(e.Control);PerformLayout();};
   content.Layout+=delegate{if(!arranging)PerformLayout();};
   UiTypography.Changed+=TypographyChanged;
   AppTheme.Changed+=ThemeChanged;
  }
  void ThemeChanged(object sender,EventArgs e) {
   if(IsDisposed||!IsHandleCreated)return;
   Action apply=delegate {if(IsDisposed)return;BackColor=AppTheme.ScrollTrack;viewport.BackColor=content.BackColor=AppTheme.Surface;Invalidate();};
   if(InvokeRequired){try{BeginInvoke(apply);}catch(InvalidOperationException){}}else apply();
  }
  void TypographyChanged(object sender,EventArgs e) {
   if(IsDisposed||!IsHandleCreated)return;
   if(InvokeRequired){try{BeginInvoke((Action)PerformLayout);}catch(InvalidOperationException){}}
   else PerformLayout();
  }
  protected override void OnLayout(LayoutEventArgs e) {
   if(arranging||viewport==null||vertical==null||horizontal==null)return;
   arranging=true;
   try {
    base.OnLayout(e);
    int thickness=vertical.PreferredThickness;
    bool v=false,h=false;Size needed=Size.Empty;int width=ClientSize.Width,height=ClientSize.Height;
    for(int pass=0;pass<4;pass++) {
     width=Math.Max(1,ClientSize.Width-(v?thickness:0));height=Math.Max(1,ClientSize.Height-(h?thickness:0));
     content.Width=width;needed=MeasureContent(width,height);
     bool nextV=needed.Height>height,nextH=needed.Width>width;
     if(v==nextV&&h==nextH)break;v=nextV;h=nextH;
    }
    width=Math.Max(1,ClientSize.Width-(v?thickness:0));height=Math.Max(1,ClientSize.Height-(h?thickness:0));
    viewport.Bounds=new Rectangle(0,0,width,height);content.Size=new Size(Math.Max(width,needed.Width),Math.Max(height,needed.Height));
    vertical.Visible=v;horizontal.Visible=h;
    vertical.Bounds=new Rectangle(width,0,thickness,height);horizontal.Bounds=new Rectangle(0,height,width,thickness);
    vertical.SetRange(0,Math.Max(0,needed.Height-height),height,Math.Max(12,Font.Height));
    horizontal.SetRange(0,Math.Max(0,needed.Width-width),width,Math.Max(12,Font.Height));
    MoveContent();
   } finally{arranging=false;}
  }
  Size MeasureContent(int width,int height) {
   int right=width,bottom=height;
   foreach(Control child in content.Controls) {
    if(!child.Visible)continue;
    Size preferred=child.GetPreferredSize(new Size(width,0));
    var table=child as TableLayoutPanel;
    if(child.Dock==DockStyle.Fill && table!=null)preferred.Height=TableMinimumHeight(table,width);
    int w=child.Dock==DockStyle.Top||child.Dock==DockStyle.Fill?Math.Max(width,child.MinimumSize.Width):child.Right;
    int h=child.Dock==DockStyle.Fill?Math.Max(height,preferred.Height):child.Dock==DockStyle.Top?preferred.Height:child.Bottom;
    right=Math.Max(right,w);bottom=Math.Max(bottom,h+child.Margin.Bottom);
   }
   return new Size(right,bottom);
  }
  static int TableMinimumHeight(TableLayoutPanel table,int width) {
   int height=table.Padding.Vertical;
   for(int row=0;row<table.RowCount;row++) {
    RowStyle style=row<table.RowStyles.Count?table.RowStyles[row]:new RowStyle(SizeType.AutoSize);
    int required=style.SizeType==SizeType.Absolute?(int)Math.Ceiling(style.Height):0;
    foreach(Control child in table.Controls) {
     if(!child.Visible || table.GetRow(child)!=row)continue;
     int h=style.SizeType==SizeType.Percent?child.MinimumSize.Height:child.GetPreferredSize(new Size(Math.Max(1,width-table.Padding.Horizontal-child.Margin.Horizontal),0)).Height;
     required=Math.Max(required,h+child.Margin.Vertical);
    }
    height+=required;
   }
   return height;
  }
  void MoveContent(){if(!IsDisposed)content.Location=new Point(-horizontal.Value,-vertical.Value);}
  protected override void OnMouseWheel(MouseEventArgs e) {
   base.OnMouseWheel(e);if(!vertical.Visible)return;
   vertical.ScrollWheel(e.Delta);var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;
  }
  void Watch(Control control) {
   if(!watched.Add(control))return;control.Enter+=EnsureVisible;control.ControlAdded+=ChildAdded;control.ControlRemoved+=ChildRemoved;
   foreach(Control child in control.Controls)Watch(child);
  }
  void Unwatch(Control control) {
   if(!watched.Remove(control))return;control.Enter-=EnsureVisible;control.ControlAdded-=ChildAdded;control.ControlRemoved-=ChildRemoved;
   foreach(Control child in control.Controls)Unwatch(child);
  }
  void ChildAdded(object sender,ControlEventArgs e){Watch(e.Control);if(!arranging)PerformLayout();}
  void ChildRemoved(object sender,ControlEventArgs e){Unwatch(e.Control);if(!arranging)PerformLayout();}
  void EnsureVisible(object sender,EventArgs e) {
   var control=(Control)sender;if(!control.CanSelect||control.HasChildren)return;
   Rectangle bounds=viewport.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
   if(bounds.Top<0)vertical.Value+=bounds.Top;else if(bounds.Bottom>viewport.Height)vertical.Value+=bounds.Bottom-viewport.Height;
   if(bounds.Left<0)horizontal.Value+=bounds.Left;else if(bounds.Right>viewport.Width)horizontal.Value+=bounds.Right-viewport.Width;
  }
  protected override void Dispose(bool disposing) {
   if(disposing){UiTypography.Changed-=TypographyChanged;AppTheme.Changed-=ThemeChanged;foreach(var control in new List<Control>(watched))Unwatch(control);}
   base.Dispose(disposing);
  }
 }
}
