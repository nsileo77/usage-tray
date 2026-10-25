using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// Independent layered window. The real taskbar remains visible underneath.
public sealed class TaskbarStrip : Form {
 [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] struct NativePoint {public int X,Y; public NativePoint(int x,int y){X=x;Y=y;}}
 [StructLayout(LayoutKind.Sequential)] struct NativeSize {public int Width,Height; public NativeSize(int w,int h){Width=w;Height=h;}}
 [StructLayout(LayoutKind.Sequential,Pack=1)] struct Blend {public byte Operation,Flags,Alpha,Format;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
 [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dc,ref NativePoint destination,ref NativeSize size,IntPtr source,ref NativePoint origin,uint key,ref Blend blend,uint flags);
 [DllImport("user32.dll",EntryPoint="UpdateLayeredWindow",SetLastError=true)] static extern bool UpdateChildLayer(IntPtr hwnd,IntPtr dc,IntPtr destination,ref NativeSize size,IntPtr source,ref NativePoint origin,uint key,ref Blend blend,uint flags);
 [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
 [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
 [DllImport("user32.dll")] static extern void NotifyWinEvent(uint eventId,IntPtr hwnd,int objectId,int childId);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
 [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
 [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
 [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
 [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
 readonly ToolTip help=new ToolTip(); readonly GraphicsPath logo; readonly SmoothText textRenderer=new SmoothText();
 WeeklyView reading=new WeeklyView {Amount="…",Countdown="Connecting…",ResetDate="Weekly account usage"};
 string state=""; bool dragging; Point dragStart,windowStart; float scale=1; TopmostRepair visibilityGuard;
 Bitmap cachedFrame; DateTime lastPresented,lastRaised; bool requestedVisible,recovering;
 TaskbarSurface surface; IntPtr failedTaskbar; DateTime retrySurfaceAfter; string surfaceError=""; bool updatingSurface;
 public bool Hosted {get{return surface!=null&&surface.IsAttached&&!dragging;}}
 public string HostingStatus {get{return Hosted?"Taskbar attached":surfaceError.Length>0?"Floating fallback: "+surfaceError:"Floating display";}}
 public int PresentCount {get;private set;} public int RenderCount {get;private set;}
 public bool WantsVisible {get{return requestedVisible&&!IsDisposed&&IsHandleCreated;}}
 UsageLayout dashboard;
 string dashboardSignature="";
 public string CurrentLayout {get{return dashboard==null?"legacy":dashboard.Layout;}}
 public string CurrentWindow {get{return dashboard==null?"weekly":dashboard.Selected.Label;}}
 public event Action PositionSaved;
 public event Action LayoutToggleRequested;
 public TaskbarStrip(Action details,Action refresh,Action quit) {
  Text="Usage Tray taskbar strip";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;
  StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;Font=new Font("Segoe UI",12f,FontStyle.Regular,GraphicsUnit.Pixel);ForeColor=Color.White;TopMost=true;
  using(var g=CreateGraphics())ApplyDpi((int)g.DpiX);
  logo=LoadLogo();SetTip();var menu=new ContextMenuStrip();
  menu.Items.Add("Open usage details",null,(s,e)=>details());menu.Items.Add("Refresh now",null,(s,e)=>refresh());menu.Items.Add(new ToolStripSeparator());
  menu.Items.Add("Switch standard / compact",null,(s,e)=>{if(LayoutToggleRequested!=null)LayoutToggleRequested();});
  menu.Items.Add("Place over taskbar (left)",null,(s,e)=>{DockToTaskbar(false);SavePosition();});
  menu.Items.Add("Place just above taskbar",null,(s,e)=>{DockToTaskbar(true);SavePosition();});
  menu.Items.Add("Hide strip (restore from tray icon)",null,(s,e)=>Hide());menu.Items.Add("Quit Usage Tray",null,(s,e)=>quit());ContextMenuStrip=menu;
  MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){dragging=true;dragStart=Cursor.Position;windowStart=Location;Capture=true;}};
  MouseMove+=(s,e)=>{if(dragging){Point now=Cursor.Position;Location=new Point(windowStart.X+now.X-dragStart.X,windowStart.Y+now.Y-dragStart.Y);}};
  MouseUp+=(s,e)=>{if(dragging){dragging=false;Capture=false;EnsureOnScreen();KeepVisible();Present();SavePosition();}};
  MouseDoubleClick+=(s,e)=>{if(e.Button==MouseButtons.Left)details();};
 }
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x00000080|0x00080000;return p;}}
 protected override void SetVisibleCore(bool value){requestedVisible=value;if(!value&&surface!=null)surface.Hide();base.SetVisibleCore(value);if(value&&logo!=null&&IsHandleCreated)KeepVisible();}
 protected override void OnShown(EventArgs e){base.OnShown(e);if(visibilityGuard==null)visibilityGuard=new TopmostRepair(this,KeepVisible,()=>WantsVisible);Present();}
 protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Visible&&logo!=null)Present();}
 protected override void OnPaintBackground(PaintEventArgs e){}
 protected override void OnPaint(PaintEventArgs e){}
 void SetTip(){if(dashboard!=null){help.ShowAlways=true;help.SetToolTip(this,dashboard.Detail);if(surface!=null)help.SetToolTip(surface,dashboard.Detail);return;}string tip="Weekly account usage (Codex feed)"+(state.Length>0?" — "+state:"")+"\nRed needle: actual allowance remaining.\nWhite pointer: even-use pace"+(reading.PaceRemaining.HasValue?" ("+reading.PaceRemaining.Value.ToString("0.000",CultureInfo.InvariantCulture)+"% remaining)":" unavailable")+".\n"+HostingStatus+". Refreshes every 60 seconds. Drag to move.\nDouble-click for details. Right-click for options.\nIndependent utility. Not affiliated with or endorsed by OpenAI.";help.ShowAlways=true;help.SetToolTip(this,tip);if(surface!=null)help.SetToolTip(surface,tip);}
 void SavePosition(){if(PositionSaved!=null)PositionSaved();}
 public void Restore(int? x,int? y){if(x.HasValue&&y.HasValue){Location=new Point(x.Value,y.Value);EnsureOnScreen();}else DockToTaskbar(false);}
 public void DockToTaskbar(bool above){Rect r;var taskbar=FindWindow("Shell_TrayWnd",null);
  if(taskbar!=IntPtr.Zero&&GetWindowRect(taskbar,out r)&&r.Right-r.Left>r.Bottom-r.Top&&r.Bottom-r.Top>=20)Location=new Point(r.Left+8,above?r.Top-Height-4:r.Top+(r.Bottom-r.Top-Height)/2);
  else {var area=Screen.PrimaryScreen.WorkingArea;Location=new Point(area.Left+8,area.Bottom-Height-4);}EnsureOnScreen();
 }
 public void EnsureOnScreen(){Location=Clamp(Location,Size,Screen.FromRectangle(Bounds).Bounds);Rect r;var taskbar=FindWindow("Shell_TrayWnd",null);if(taskbar!=IntPtr.Zero&&GetWindowRect(taskbar,out r))Location=FitTaskbar(Location,Size,new Rectangle(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top));}
 public static Point FitTaskbar(Point location,Size size,Rectangle taskbar){var bounds=new Rectangle(location,size);if(taskbar.Width>taskbar.Height&&size.Height<=taskbar.Height&&bounds.IntersectsWith(taskbar))return new Point(location.X,taskbar.Top+(taskbar.Height-size.Height)/2);return location;}
 public static Point Clamp(Point p,Size size,Rectangle b){return new Point(Math.Max(b.Left,Math.Min(p.X,b.Right-size.Width)),Math.Max(b.Top,Math.Min(p.Y,b.Bottom-size.Height)));}
 public sealed class WeeklyView {public string Amount,Countdown,ResetDate;public double? Remaining,PaceRemaining;}
 public static double? PaceAt(DateTime? reset,DateTime now){
  if(!reset.HasValue)return null;double left=(reset.Value.ToUniversalTime()-now.ToUniversalTime()).TotalSeconds;
  if(left<=0||left>604800)return null;return Math.Ceiling(left/60)/10080.0*100;
 }
 public static WeeklyView FormatWeekly(double? remaining,DateTime? reset,DateTime now,string status){
  if(remaining.HasValue&&(double.IsNaN(remaining.Value)||double.IsInfinity(remaining.Value)))remaining=null;
  var view=new WeeklyView {Amount=remaining.HasValue?Math.Max(0,Math.Min(100,remaining.Value)).ToString("0.#",CultureInfo.InvariantCulture)+"%":"unknown",Remaining=remaining.HasValue?(double?)Math.Max(0,Math.Min(100,remaining.Value)):null,PaceRemaining=PaceAt(reset,now),Countdown="Reset time unknown",ResetDate="Double-click for details"};
  if(reset.HasValue){view.ResetDate=reset.Value.ToLocalTime().ToString("ddd, MMM d · h:mm tt",CultureInfo.InvariantCulture);var left=reset.Value.ToUniversalTime()-now.ToUniversalTime();if(left<=TimeSpan.Zero){view.Amount="verify";view.Remaining=null;view.Countdown="Reset passed — refresh";}else{long minutes=(long)Math.Ceiling(left.TotalMinutes);view.Countdown=minutes/1440+"d "+(minutes%1440)/60+"h "+minutes%60+"m until reset";}}
  else if(!remaining.HasValue)view.Countdown="Weekly usage unknown";
  if(!string.IsNullOrEmpty(status))view.Countdown=status+" · "+view.Countdown.Replace(" until reset","");
  return view;
 }
 public void SetWeekly(double? remaining,DateTime? reset,DateTime now,string status){
  if(dashboard!=null){dashboard=null;InvalidateFrame();}
  var next=FormatWeekly(remaining,reset,now,status);string nextState=status??"";
  bool changed=reading.Amount!=next.Amount||reading.Countdown!=next.Countdown||reading.ResetDate!=next.ResetDate||reading.Remaining!=next.Remaining||reading.PaceRemaining!=next.PaceRemaining||state!=nextState;
  reading=next;state=nextState;if(changed){InvalidateFrame();SetTip();if(WantsVisible)Present();}
 }
 public void SetWindows(System.Collections.Generic.List<Quota> rows,string layout,string selected,DateTime now,string status,DisplayOptions options=null){
  var next=UsageLayout.Create(rows,layout,selected,now,status,options);string signature=next.Signature;bool changed=dashboard==null||dashboardSignature!=signature;dashboardSignature=signature;dashboard=next;state=status??"";if(Width!=(int)Math.Round(next.Width*scale)){ApplyDpi((int)Math.Round(scale*96));if(WantsVisible)EnsureOnScreen();}
  if(changed){InvalidateFrame();SetTip();if(WantsVisible)Present();}
 }
 void MiniGauge(Graphics g,float x,float y,float zoom,WindowView v){var saved=g.Save();g.TranslateTransform(x,y);g.ScaleTransform(zoom,zoom);DrawGauge(g,new PointF(0,0),v.Remaining.HasValue?(double?)(v.Remaining.Value/100):null,dashboard.Options.ShowPace&&v.Pace.HasValue?(double?)(v.Pace.Value/100):null);g.Restore(saved);}
 string ValueLabel(WindowView v){return (dashboard.Options.ShowLabels&&dashboard.Visible.Count>1?v.Label+"  ":"")+(dashboard.Options.ShowAmounts||!v.Remaining.HasValue?v.Amount:"");}
 string TimeLabel(WindowView v){return dashboard.Options.ShowReset?v.ResetText:dashboard.Options.ShowCountdown?v.Countdown:"";}
 void DrawDashboard(Graphics g){
  var options=dashboard.Options;float brand=options.ShowIcon||dashboard.Status.Length>0?46:8;
  if(options.ShowIcon){using(var custom=LocalLogo.Load(options.LogoPath)){if(custom!=null)g.DrawImage(custom,new RectangleF(5,2,28,28));else {var saved=g.Save();g.TranslateTransform(5,2);g.ScaleTransform(28f/41,28f/41);g.FillPath(Brushes.White,logo);g.Restore(saved);using(var ai=new Font("Segoe UI",10*scale,FontStyle.Bold,GraphicsUnit.Pixel))DrawCell(g,"AI",new RectangleF(9,7,21,17),ai,false,true);}}}
  if(brand>8)using(var divider=new Pen(Color.FromArgb(140,255,255,255),1))g.DrawLine(divider,43,3,43,41);
  using(var small=new Font("Segoe UI",11*scale,FontStyle.Regular,GraphicsUnit.Pixel))using(var dateFont=new Font("Segoe UI",10*scale,FontStyle.Regular,GraphicsUnit.Pixel)){
   if(dashboard.NoViews){DrawCell(g,"Usage views hidden",new RectangleF(brand+6,2,300,20),Font,false);DrawCell(g,"Enable sections in Display preferences",new RectangleF(brand+6,23,350,18),small,false);}
   else if(dashboard.Layout=="double"){
    for(int i=0;i<dashboard.Visible.Count;i++){var v=dashboard.Visible[i];float y=dashboard.Visible.Count==1?11:i*22;
     DrawCell(g,ValueLabel(v),new RectangleF(brand+6,y,145,19),small,false);
     DrawCell(g,TimeLabel(v),new RectangleF(brand+184,y,dashboard.Width-brand-189,19),dateFont,true);
     float start=brand+6,length=dashboard.Width-start-5;
     if(options.ShowLines){using(var track=new Pen(Color.FromArgb(65,255,255,255),1))g.DrawLine(track,start,y+21,dashboard.Width-5,y+21);if(v.Remaining.HasValue)using(var fill=new Pen(Color.FromArgb(190,220,233,205),1))g.DrawLine(fill,start,y+21,start+(float)(length*v.Remaining.Value/100),y+21);
      if(options.ShowPace&&v.Pace.HasValue)using(var notch=new Pen(Color.White,2)){float x=start+(float)(length*v.Pace.Value/100);g.DrawLine(notch,x,y+17,x,y+23);}}
    }
   }else{
    float cell=(dashboard.Width-brand)/dashboard.Visible.Count;
    for(int i=0;i<dashboard.Visible.Count;i++){var v=dashboard.Visible[i];float x=brand+i*cell;MiniGauge(g,x+28,25,1,v);
     if(options.ShowAmounts||!v.Remaining.HasValue)DrawCell(g,v.Remaining.HasValue?v.Remaining.Value.ToString("0.#")+"%":"--",new RectangleF(x,26,56,17),small,false,true);
     string upper=(options.ShowLabels&&dashboard.Visible.Count>1?v.Label+"  ":"")+(options.ShowCountdown?v.Countdown+ (v.Reported&&v.Countdown!="Reset passed"&&v.Countdown!="Reset unknown"?" until reset":""):"");
     DrawCell(g,upper,new RectangleF(x+60,1,cell-65,20),small,false);
     DrawCell(g,options.ShowReset?v.ResetText:"",new RectangleF(x+60,23,cell-65,19),dateFont,false);
    }
   }
   using(var badge=new Font("Segoe UI",9*scale,FontStyle.Regular,GraphicsUnit.Pixel))DrawCell(g,dashboard.Status=="refreshing"?"WAIT":dashboard.Status.ToUpperInvariant(),new RectangleF(1,31,43,13),badge,false,true);
  }
 }
 bool UpdateSurface(){
  if(updatingSurface)return Hosted;updatingSurface=true;
  try{
   var parent=FindWindow("Shell_TrayWnd",null);Rect r;bool fits=!dragging&&parent!=IntPtr.Zero&&GetWindowRect(parent,out r)&&new Rectangle(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top).Contains(Bounds);
   if(surface!=null&&(!surface.IsAttached||surface.Taskbar!=parent||!fits)){surface.Dispose();surface=null;}
   if(!fits)return false;
   if(surface==null&&(failedTaskbar!=parent||DateTime.UtcNow>=retrySurfaceAfter)){
    try{surface=new TaskbarSurface(parent);surface.ContextMenuStrip=ContextMenuStrip;
     surface.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){dragging=true;dragStart=Cursor.Position;windowStart=Location;surface.Hide();ShowWindow(Handle,4);Capture=true;Present();}};
     surface.MouseDoubleClick+=(s,e)=>{if(e.Button==MouseButtons.Left){var item=ContextMenuStrip.Items[0] as ToolStripMenuItem;if(item!=null)item.PerformClick();}};
     if(surface.NativeDpi>0&&Math.Abs(scale-surface.NativeDpi/96f)>.001)ApplyDpi(surface.NativeDpi);
     surfaceError="";SetTip();
    }catch(Exception ex){if(surface!=null){surface.Dispose();surface=null;}failedTaskbar=parent;retrySurfaceAfter=DateTime.UtcNow.AddSeconds(3);surfaceError=ex.Message+(ex is Win32Exception?" (Win32 "+((Win32Exception)ex).NativeErrorCode+")":"");SetTip();}
   }
   if(surface!=null){surface.Place(Bounds);if(IsWindowVisible(Handle))ShowWindow(Handle,0);return true;}return false;
  }finally{updatingSurface=false;}
 }
 public void KeepVisible(){
  if(!WantsVisible||recovering||dragging||ContextMenuStrip.Visible)return;
  try{recovering=true;EnsureOnScreen();
   // Windows retains the layered child's pixels. Parenting solves the Start band
   // change without repeatedly resubmitting its image to the taskbar compositor.
   if(UpdateSurface()){if(surface.NeedsFrame)Present();return;}
   if(!IsWindowVisible(Handle)||IsIconic(Handle))ShowWindow(Handle,4); // SW_SHOWNOACTIVATE
   // Avoid repeatedly changing shell z-order when we are already above the taskbar.
   // A needless raise can make Explorer reorder again and feed the event hook.
   bool raise=(GetWindowLong(Handle,-20)&8)==0;var taskbar=FindWindow("Shell_TrayWnd",null);
   for(var w=GetWindow(Handle,3);w!=IntPtr.Zero&&!raise;w=GetWindow(w,3)){
    Rect r;bool overlappingTopmost=IsWindowVisible(w)&&(GetWindowLong(w,-20)&8)!=0&&GetWindowRect(w,out r)&&Bounds.IntersectsWith(new Rectangle(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top));
    if(w==taskbar||overlappingTopmost)raise=true;
   }
   if(raise&&DateTime.UtcNow-lastRaised>=TimeSpan.FromMilliseconds(100)){lastRaised=DateTime.UtcNow;SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0053);}
   // Re-submit the cached surface even when quota/countdown values have not changed.
   // Throttle animation bursts; the independent 250ms watchdog also calls this.
   if(DateTime.UtcNow-lastPresented>=TimeSpan.FromMilliseconds(100))Present();
  }finally{recovering=false;}
 }
 void InvalidateFrame(){if(cachedFrame!=null){cachedFrame.Dispose();cachedFrame=null;}}
 public void ApplyDpi(int dpi){InvalidateFrame();scale=dpi/96f;ClientSize=new Size((int)Math.Round((dashboard==null?330:dashboard.Width)*scale),(int)Math.Round(44*scale));var old=Font;var next=new Font("Segoe UI",(float)Math.Round(12*scale),FontStyle.Regular,GraphicsUnit.Pixel);if(!next.Equals(old)){Font=next;if(old!=null)old.Dispose();}else next.Dispose();}
 protected override void WndProc(ref Message m){if(m.Msg==0x02E0){base.WndProc(ref m);ApplyDpi((int)(m.WParam.ToInt64()&0xffff));EnsureOnScreen();if(Visible&&logo!=null)Present();return;}base.WndProc(ref m);}
 void DrawCell(Graphics g,string text,RectangleF rect,Font font,bool right,bool center=false){
  var saved=g.Save();g.ResetTransform();
  int x=(int)Math.Round(rect.X*scale),y=(int)Math.Round(rect.Y*scale),width=(int)Math.Round(rect.Width*scale),height=(int)Math.Round(rect.Height*scale);
  var glyphs=textRenderer.Get(text,width,height,font.Size,right,center);
  g.DrawImageUnscaled(glyphs,x,y);g.Restore(saved);
 }
 public static PointF NeedleTip(double fraction,PointF center,float length){double angle=Math.PI*(1-Math.Max(0,Math.Min(1,fraction)));return new PointF(center.X+length*(float)Math.Cos(angle),center.Y-length*(float)Math.Sin(angle));}
 public static PointF[] PacePointer(double fraction,PointF center){
  double angle=Math.PI*(1-Math.Max(0,Math.Min(1,fraction)));
  var tip=NeedleTip(fraction,center,18f);
  var a=new PointF(center.X+23.7f*(float)Math.Cos(angle+.09),center.Y-23.7f*(float)Math.Sin(angle+.09));
  var b=new PointF(center.X+23.7f*(float)Math.Cos(angle-.09),center.Y-23.7f*(float)Math.Sin(angle-.09));
  return new[]{tip,a,b};
 }
 static void DrawGauge(Graphics g,PointF center,double? remaining,double? pace){
  const float radius=20.5f;
  using(var track=new Pen(Color.FromArgb(110,255,255,255),1))g.DrawArc(track,center.X-radius,center.Y-radius,radius*2,radius*2,180,180);
  using(var ticks=new Pen(Color.FromArgb(195,255,255,255),1))foreach(double value in new[]{0,0.25,0.5,0.75,1}){var a=NeedleTip(value,center,radius);var b=NeedleTip(value,center,radius-4.5f);g.DrawLine(ticks,a,b);}
  if(pace.HasValue)g.FillPolygon(Brushes.White,PacePointer(pace.Value,center));
  if(remaining.HasValue){
   double angle=Math.PI*(1-Math.Max(0,Math.Min(1,remaining.Value)));float nx=(float)Math.Sin(angle),ny=(float)Math.Cos(angle);
   using(var red=new SolidBrush(Color.FromArgb(255,107,102))){g.FillPolygon(red,new[]{new PointF(center.X-nx,center.Y-ny),NeedleTip(remaining.Value,center,17.5f),new PointF(center.X+nx,center.Y+ny)});g.FillEllipse(red,center.X-1.6f,center.Y-1.6f,3.2f,3.2f);}
  }else using(var mark=new Pen(Color.White,1.6f))g.DrawLine(mark,center.X-3,center.Y-4,center.X+3,center.Y-4);
 }
 public Bitmap RenderLayer(){var bitmap=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(bitmap)){
  // Alpha 1/255 keeps the full rectangle draggable with negligible tint.
  g.Clear(Color.FromArgb(1,0,0,0));g.ScaleTransform(scale,scale);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
  if(dashboard!=null){DrawDashboard(g);return bitmap;}
  var saved=g.Save();g.TranslateTransform(6,4);g.ScaleTransform(36f/41f,36f/41f);g.FillPath(Brushes.White,logo);g.Restore(saved);using(var line=new Pen(Color.White,1))g.DrawLine(line,58,3,58,41);DrawGauge(g,new PointF(103,24),reading.Remaining.HasValue?(double?)(reading.Remaining.Value/100):null,reading.PaceRemaining.HasValue?(double?)(reading.PaceRemaining.Value/100):null);DrawCell(g,reading.Amount,new RectangleF(75,25,56,18),Font,false,true);DrawCell(g,reading.Countdown,new RectangleF(145,4,180,18),Font,false);DrawCell(g,reading.ResetDate,new RectangleF(145,24,180,18),Font,false);
 }return bitmap;}
 public void Present(){if(!IsHandleCreated||IsDisposed)return;if(requestedVisible&&!dragging)UpdateSurface();if(cachedFrame==null){cachedFrame=RenderLayer();RenderCount++;}var bitmap=cachedFrame;
  IntPtr screen=GetDC(IntPtr.Zero),dc=CreateCompatibleDC(screen),image=IntPtr.Zero,old=IntPtr.Zero;
  try{image=bitmap.GetHbitmap(Color.FromArgb(0));old=SelectObject(dc,image);var position=new NativePoint(Left,Top);var origin=new NativePoint(0,0);var size=new NativeSize(Width,Height);var blend=new Blend{Operation=0,Flags=0,Alpha=255,Format=1};bool ok=Hosted?UpdateChildLayer(surface.Handle,screen,IntPtr.Zero,ref size,dc,ref origin,0,ref blend,2):UpdateLayeredWindow(Handle,screen,ref position,ref size,dc,ref origin,0,ref blend,2);if(!ok)throw new Win32Exception(Marshal.GetLastWin32Error());if(Hosted)surface.NeedsFrame=false;lastPresented=DateTime.UtcNow;PresentCount++;}
  finally{if(old!=IntPtr.Zero)SelectObject(dc,old);if(image!=IntPtr.Zero)DeleteObject(image);if(dc!=IntPtr.Zero)DeleteDC(dc);if(screen!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screen);}
 }
 static GraphicsPath LoadLogo(){var p=new GraphicsPath(FillMode.Alternate);p.AddRectangle(new RectangleF(6,6,29,29));p.AddRectangle(new RectangleF(9,9,23,23));foreach(float n in new[]{12f,20f,28f}){p.AddRectangle(new RectangleF(n,1,2,5));p.AddRectangle(new RectangleF(n,35,2,5));p.AddRectangle(new RectangleF(1,n,5,2));p.AddRectangle(new RectangleF(35,n,5,2));}return p;}
 protected override void Dispose(bool disposing){if(disposing){if(visibilityGuard!=null)visibilityGuard.Dispose();if(surface!=null)surface.Dispose();InvalidateFrame();textRenderer.Dispose();help.Dispose();if(logo!=null)logo.Dispose();if(ContextMenuStrip!=null)ContextMenuStrip.Dispose();}base.Dispose(disposing);}
 public static void Test(){
  DateTime now=new DateTime(2026,10,23,20,0,0,DateTimeKind.Utc);var reset=now.AddDays(6).AddHours(18).AddMinutes(7);
  var view=FormatWeekly(100,reset,now,"");if(view.Amount!="100%"||view.Countdown!="6d 18h 7m until reset"||view.ResetDate!=reset.ToLocalTime().ToString("ddd, MMM d · h:mm tt",CultureInfo.InvariantCulture))throw new Exception("Weekly formatting failed");
  var expired=FormatWeekly(90,now,now,"");if(expired.Remaining.HasValue||expired.Amount!="verify")throw new Exception("Expired value failed");
  if(FormatWeekly(null,null,now,"").Remaining.HasValue||FormatWeekly(null,null,now,"").Amount!="unknown"||FormatWeekly(double.NaN,null,now,"").Remaining.HasValue)throw new Exception("Unknown value failed");
  if(!FormatWeekly(55,reset,now,"STALE").Countdown.StartsWith("STALE · "))throw new Exception("Stale state failed");
  if(Math.Abs(PaceAt(now.AddDays(6),now).Value-600.0/7)>1e-8||Math.Abs(PaceAt(now.AddDays(4.5),now).Value-450.0/7)>1e-8||PaceAt(now.AddDays(3.5),now)!=50||PaceAt(now.AddDays(7),now)!=100)throw new Exception("Even-use pace failed");
  if(PaceAt(null,now).HasValue||PaceAt(now,now).HasValue||PaceAt(now.AddSeconds(-1),now).HasValue||PaceAt(now.AddDays(8),now).HasValue)throw new Exception("Invalid pace deadline accepted");
  if(FormatWeekly(90,now,now,"").PaceRemaining.HasValue)throw new Exception("Expired pace marker not hidden");
  var deadline=now.AddDays(6);double p0=PaceAt(deadline,now).Value,p1=PaceAt(deadline,now.AddMinutes(1)).Value;
  if(Math.Abs((p0-p1)-100.0/10080)>1e-8||PaceAt(deadline,now.AddSeconds(30))!=p0||PaceAt(deadline,now.AddSeconds(59))!=p0||PaceAt(deadline,now.AddSeconds(60))!=p1)throw new Exception("Minute-resolution pace failed");
  var step0=FormatWeekly(97,deadline,now,"");var step1=FormatWeekly(97,deadline,now.AddMinutes(1),"");if(step0.PaceRemaining==step1.PaceRemaining||Math.Abs(step0.PaceRemaining.Value-step1.PaceRemaining.Value-100.0/10080)>1e-8)throw new Exception("Pace lost minute precision before drawing");
  var center=new PointF(20,20);var empty=NeedleTip(0,center,10);var half=NeedleTip(0.5,center,10);var full=NeedleTip(1,center,10);
  if(Math.Abs(empty.X-10)>0.01||Math.Abs(half.Y-10)>0.01||Math.Abs(full.X-30)>0.01)throw new Exception("Needle direction failed");
  if(Clamp(new Point(9000,9000),new Size(330,44),new Rectangle(-1920,0,1920,1080))!=new Point(-330,1036))throw new Exception("Monitor clamp failed");
  var taskbar=new Rectangle(0,1032,1920,48);var fitted=FitTaskbar(new Point(170,1024),new Size(330,44),taskbar);if(fitted.Y!=1034||fitted.Y+44>taskbar.Bottom)throw new Exception("Taskbar fit failed");if(FitTaskbar(new Point(170,984),new Size(330,44),taskbar).Y!=984)throw new Exception("Above-taskbar placement moved");
  using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{}))foreach(int dpi in new[]{96,120,144,192}){
   strip.ApplyDpi(dpi);strip.SetWeekly(97,now.AddDays(4.5),now,"");
   using(var layer=strip.RenderLayer())using(var g=Graphics.FromImage(layer))using(var format=new StringFormat(StringFormat.GenericTypographic)){
    string[] samples={"100%","unknown","6d 23h 59m until reset","Wed, Sep 30 · 12:59 PM"};float[] widths={56,56,180,180};
    for(int i=0;i<samples.Length;i++)if(SmoothText.MeasureWidth(samples[i],strip.Font.Size)>Math.Round(widths[i]*strip.scale)+0.5||SmoothText.MeasureHeight(samples[i],strip.Font.Size)>Math.Round(18*strip.scale))throw new Exception("Text clipping at DPI "+dpi+": "+samples[i]);
    if(layer.GetPixel(layer.Width-2,layer.Height-2).A>1||layer.GetPixel((int)Math.Round(58*strip.scale),(int)Math.Round(28*strip.scale)).A<100)throw new Exception("Transparency or divider failed");
    int redPixels=0;for(int y=0;y<(int)Math.Round(27*strip.scale);y++)for(int x=(int)Math.Round(75*strip.scale);x<(int)Math.Round(131*strip.scale);x++){var p=layer.GetPixel(x,y);if(p.A>100&&p.R>p.G+60&&p.R>p.B+60)redPixels++;}if(redPixels<5)throw new Exception("Red needle missing at DPI "+dpi);
    var points=PacePointer(FormatWeekly(97,now.AddDays(4.5),now,"").PaceRemaining.Value/100,new PointF(103,24));
    float cx=(points[0].X+points[1].X+points[2].X)/3,cy=(points[0].Y+points[1].Y+points[2].Y)/3;
    var white=layer.GetPixel((int)Math.Round(cx*strip.scale),(int)Math.Round(cy*strip.scale));if(white.A<100||white.R<230||white.G<230||white.B<230)throw new Exception("White pace pointer missing at DPI "+dpi);
    int smoothed=0,solid=0;for(int y=(int)Math.Round(5*strip.scale);y<(int)Math.Round(41*strip.scale);y++)for(int x=(int)Math.Round(145*strip.scale);x<(int)Math.Round(325*strip.scale);x++){var pixel=layer.GetPixel(x,y);if(pixel.A>1&&pixel.A<255)smoothed++;if(pixel.A>=240)solid++;if(Math.Abs(pixel.R-pixel.G)>1||Math.Abs(pixel.G-pixel.B)>1)throw new Exception("Colored text fringe at DPI "+dpi);}if(smoothed<30||solid<10)throw new Exception("Smooth text edge/core pixels missing at DPI "+dpi+" (edge="+smoothed+", core="+solid+", scale="+strip.scale+", image="+layer.Size+", "+SmoothText.Metrics("Sample",strip.Font.Size)+")");
   }
  }
 }
 public static void Preview(string path){using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{})){var now=new DateTime(2026,10,24,12,0,0,DateTimeKind.Utc);strip.SetWeekly(68,now.AddDays(4).AddHours(12),now,"SYNTHETIC DEMO");using(var layer=strip.RenderLayer()){
  layer.Save(Path.ChangeExtension(path,"transparent.png"),ImageFormat.Png);using(var preview=new Bitmap(layer.Width,layer.Height))using(var g=Graphics.FromImage(preview)){using(var background=new LinearGradientBrush(new Rectangle(0,0,preview.Width,preview.Height),Color.FromArgb(28,42,51),Color.FromArgb(66,57,27),0f))g.FillRectangle(background,0,0,preview.Width,preview.Height);g.DrawImageUnscaled(layer,0,0);preview.Save(path,ImageFormat.Png);}
 }}}
 public static void SmokeTest(string path){using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{})){
  var now=new DateTime(2026,10,24,12,0,0,DateTimeKind.Utc);strip.SetWeekly(68,now.AddDays(4).AddHours(12),now,"SYNTHETIC DEMO");strip.Location=new Point(24,24);strip.Show();Application.DoEvents();strip.Present();
  if(!AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(strip.Handle),new IntPtr(-4)))throw new Exception("Per-monitor V2 awareness missing");
  if(strip.visibilityGuard==null||!strip.visibilityGuard.HooksInstalled)throw new Exception("Visibility hooks not installed");
  using(var cover=new Form {Text="Usage Tray visibility test",FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=strip.Bounds,TopMost=true}){
   cover.Show();cover.BringToFront();int before=strip.visibilityGuard.EventsDelivered;NotifyWinEvent(3,cover.Handle,0,0);
   var wait=System.Diagnostics.Stopwatch.StartNew();while(wait.ElapsedMilliseconds<250){Application.DoEvents();System.Threading.Thread.Sleep(5);}
   if(strip.visibilityGuard.EventsDelivered<=before)throw new Exception("Window event not delivered");
   for(var above=GetWindow(strip.Handle,3);above!=IntPtr.Zero;above=GetWindow(above,3))if(above==cover.Handle)throw new Exception("Overlay not restored above test cover");
   cover.Close();
  }
  int rendered=strip.RenderCount,presented=strip.PresentCount;
  var elapsed=System.Diagnostics.Stopwatch.StartNew();while(elapsed.ElapsedMilliseconds<1100){Application.DoEvents();System.Threading.Thread.Sleep(5);}
  if(strip.PresentCount<presented+3||strip.RenderCount!=rendered)throw new Exception("Cached watchdog redraw failed with unchanged display values");
  ShowWindow(strip.Handle,0); // Simulate a native external hide without a user Hide command.
  elapsed.Restart();while(elapsed.ElapsedMilliseconds<600){Application.DoEvents();System.Threading.Thread.Sleep(5);}
  if(!IsWindowVisible(strip.Handle))throw new Exception("Native hidden window not restored");
  strip.Hide();presented=strip.PresentCount;
  elapsed.Restart();while(elapsed.ElapsedMilliseconds<600){Application.DoEvents();System.Threading.Thread.Sleep(5);}
  if(IsWindowVisible(strip.Handle)||strip.PresentCount!=presented)throw new Exception("Intentional Hide was not respected");
  strip.Show();Application.DoEvents();strip.KeepVisible();if(!IsWindowVisible(strip.Handle))throw new Exception("Show after intentional Hide failed");strip.Hide();
  File.WriteAllText(path,"PASS: layered window, per-monitor V2, shell hooks, foreground recovery above cover, repeated cached repaint without changed values, native hide recovery within 600ms, intentional Hide preserved and Show restored.");
 }}
 public static void VisibilityTest(string path){using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{}))using(var timer=new Timer {Interval=100})using(var log=new StreamWriter(path)){
  var now=DateTime.UtcNow;strip.Text="Usage Tray Start recovery QA";strip.SetWeekly(97,now.AddDays(4).AddHours(12),now,"");strip.DockToTaskbar(false);strip.Left=1250;
  var elapsed=System.Diagnostics.Stopwatch.StartNew();var cpu=System.Diagnostics.Process.GetCurrentProcess();var cpuStart=cpu.TotalProcessorTime;int count=0;
  log.WriteLine("utc,elapsed_ms,native_visible,above_taskbar,presents,renders,last_present_age_ms,events,hosted,host_bounds,foreground_process");log.AutoFlush=true;
  timer.Tick+=(s,e)=>{bool above=true;var taskbar=FindWindow("Shell_TrayWnd",null);for(var w=GetWindow(strip.Handle,3);w!=IntPtr.Zero;w=GetWindow(w,3))if(w==taskbar)above=false;
   uint foregroundProcess;GetWindowThreadProcessId(GetForegroundWindow(),out foregroundProcess);string processName="";try{processName=System.Diagnostics.Process.GetProcessById((int)foregroundProcess).ProcessName;}catch{}
   log.WriteLine(DateTime.UtcNow.ToString("o")+","+elapsed.ElapsedMilliseconds+","+(strip.Hosted?strip.surface.NativeVisible:IsWindowVisible(strip.Handle))+","+(strip.Hosted?strip.surface.FirstSibling:above)+","+strip.PresentCount+","+strip.RenderCount+","+(DateTime.UtcNow-strip.lastPresented).TotalMilliseconds.ToString("0",CultureInfo.InvariantCulture)+","+(strip.visibilityGuard==null?0:strip.visibilityGuard.EventsDelivered)+","+strip.Hosted+","+(strip.Hosted?strip.surface.ScreenBounds.ToString().Replace(',',';'):strip.HostingStatus.Replace(',',';'))+","+processName);
   if(++count>=600){log.WriteLine("CPU seconds: "+(cpu.TotalProcessorTime-cpuStart).TotalSeconds.ToString(CultureInfo.InvariantCulture));timer.Stop();strip.Close();}
  };timer.Start();Application.Run(strip);
 }}
 static void Pump(int milliseconds){var clock=System.Diagnostics.Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds){Application.DoEvents();System.Threading.Thread.Sleep(5);}}
 public static void HostedTest(string path){using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{})){
  var now=DateTime.UtcNow;strip.SetWeekly(97,now.AddDays(4),now,"");strip.DockToTaskbar(false);strip.Left=1250;var original=strip.Location;strip.Show();Pump(350);strip.KeepVisible();
  if(!strip.Hosted||!strip.surface.NativeVisible||strip.surface.ScreenBounds!=strip.Bounds)throw new Exception("Attach/bounds failed: "+strip.HostingStatus);
  if(IsWindowVisible(strip.Handle))throw new Exception("Floating duplicate was not hidden");
  if(!AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(strip.surface.Handle),GetWindowDpiAwarenessContext(strip.surface.Taskbar)))throw new Exception("Taskbar/child DPI context mismatch");
  strip.Hide();Pump(350);if(strip.surface.NativeVisible||strip.WantsVisible)throw new Exception("Intentional hosted Hide failed");
  strip.Show();Pump(350);if(!strip.Hosted||!strip.surface.NativeVisible)throw new Exception("Hosted Show failed");
  strip.Left+=12;strip.KeepVisible();strip.Present();if(strip.surface.ScreenBounds!=strip.Bounds)throw new Exception("Hosted position synchronization failed");
  strip.DockToTaskbar(true);strip.KeepVisible();strip.Present();Pump(200);if(strip.Hosted||!IsWindowVisible(strip.Handle)||strip.ContextMenuStrip.IsDisposed)throw new Exception("Floating placement/menu fallback failed");
  strip.Location=original;strip.KeepVisible();strip.Present();Pump(200);if(!strip.Hosted||!strip.surface.NativeVisible)throw new Exception("Reattach after floating placement failed");
  strip.dragging=true;strip.surface.Hide();ShowWindow(strip.Handle,4);strip.Present();if(strip.Hosted||!IsWindowVisible(strip.Handle))throw new Exception("Drag surface handoff failed");strip.dragging=false;strip.KeepVisible();Pump(150);if(!strip.Hosted||!strip.surface.NativeVisible)throw new Exception("Reattach after drag failed");
  strip.surface.Dispose();strip.KeepVisible();Pump(150);if(!strip.Hosted||!strip.surface.NativeVisible)throw new Exception("Destroyed surface recovery failed");
  strip.Hide();File.WriteAllText(path,"PASS: taskbar child attachment, exact screen bounds, matching DPI context, hidden floating duplicate, intentional Hide/Show, position changes, above-taskbar floating fallback and menu retention, reattachment, drag handoff, destroyed-surface recovery.");
 }}
}



















