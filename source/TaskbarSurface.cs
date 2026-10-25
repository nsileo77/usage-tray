using System;
using System.Drawing;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// An app-owned layered child HWND. No Explorer subclass, DLL injection or shell patches.
// The native taskbar relationship is undocumented as an extension point; fail back to floating.
public sealed class TaskbarSurface : Control {
 [StructLayout(LayoutKind.Sequential)] struct NativePoint {public int X,Y;public NativePoint(int x,int y){X=x;Y=y;}}
 [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
 [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr window,ref NativePoint point);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetParent(IntPtr window,IntPtr parent);
 [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
 [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window,int index,int value);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
 [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
 readonly IntPtr taskbar;
 public bool NeedsFrame=true;
 public IntPtr Taskbar {get{return taskbar;}}
 public bool IsAttached {get{return !IsDisposed&&IsHandleCreated&&IsWindow(taskbar)&&GetParent(Handle)==taskbar;}}
 public bool NativeVisible {get{return IsAttached&&IsWindowVisible(Handle);}}
 public int NativeDpi {get{return IsAttached?(int)GetDpiForWindow(Handle):96;}}
 public bool FirstSibling {get{return IsAttached&&GetWindow(Handle,3)==IntPtr.Zero;}}
 public Rectangle ScreenBounds {get{Rect r;if(IsAttached&&GetWindowRect(Handle,out r))return Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);return Rectangle.Empty;}}
 public TaskbarSurface(IntPtr parent){
  taskbar=parent;SetStyle(ControlStyles.UserPaint,true);SetStyle(ControlStyles.Selectable,false);
  var previous=SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(parent));
  try{CreateHandle();SetWindowLong(Handle,-16,(GetWindowLong(Handle,-16)&~unchecked((int)0x80000000))|0x40000000);SetParent(Handle,parent);int error=Marshal.GetLastWin32Error();if(!IsAttached){Dispose();throw new Win32Exception(error,"Cannot attach taskbar surface");}}
  finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
 }
 protected override CreateParams CreateParams {get{var p=base.CreateParams;p.Caption="Usage Tray taskbar surface";p.Parent=IntPtr.Zero;p.Style=unchecked((int)0x90000000);p.ExStyle=0x08080080;p.ClassStyle|=8;p.X=0;p.Y=0;p.Width=1;p.Height=1;return p;}}
 public void Place(Rectangle screen){
  if(!IsAttached)return;if(NativeVisible&&FirstSibling&&ScreenBounds==screen)return;
  if(!Visible)Show();var point=new NativePoint(screen.X,screen.Y);ScreenToClient(taskbar,ref point);
  var flags=(uint)0x0050; // SHOWWINDOW | NOACTIVATE. HWND_TOP orders only siblings.
  if(FirstSibling)flags|=0x0004; // NOZORDER unless another taskbar child is above us.
  SetWindowPos(Handle,IntPtr.Zero,point.X,point.Y,screen.Width,screen.Height,flags);
 }
 protected override void OnPaintBackground(PaintEventArgs e){}
 protected override void OnPaint(PaintEventArgs e){}
 protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
}
