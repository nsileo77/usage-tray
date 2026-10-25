using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// Out-of-process window notifications only; no mouse/keyboard hooks or injection.
public sealed class TopmostRepair : IDisposable {
 delegate void WinEventProc(IntPtr hook,uint eventId,IntPtr hwnd,int objectId,int childId,uint thread,uint time);
 [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventProc callback,uint process,uint thread,uint flags);
 [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
 [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
 [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int max);
 readonly Form target; readonly Action repair; readonly Func<bool> active;
 readonly Timer burst=new Timer {Interval=50},watchdog=new Timer {Interval=250};
 readonly WinEventProc callback; GCHandle callbackRoot; IntPtr foreground,reorder,menus,visibility,cloaking; DateTime until; bool disposed,repairing;
 public bool HooksInstalled {get{return foreground!=IntPtr.Zero&&reorder!=IntPtr.Zero&&menus!=IntPtr.Zero&&visibility!=IntPtr.Zero&&cloaking!=IntPtr.Zero;}}
 public int EventsDelivered {get;private set;}
 public TopmostRepair(Form window,Action action,Func<bool> enabled){target=window;repair=action;active=enabled;callback=OnEvent;callbackRoot=GCHandle.Alloc(callback);
  foreground=SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0);
  reorder=SetWinEventHook(0x8004,0x8005,IntPtr.Zero,callback,0,0,2);
  menus=SetWinEventHook(4,7,IntPtr.Zero,callback,0,0,2);
  visibility=SetWinEventHook(0x8002,0x8003,IntPtr.Zero,callback,0,0,2);
  cloaking=SetWinEventHook(0x8017,0x8018,IntPtr.Zero,callback,0,0,2);
  burst.Tick+=(s,e)=>{if(disposed||!active()||DateTime.UtcNow>=until){burst.Stop();return;}Repair();};
  watchdog.Tick+=(s,e)=>{if(!disposed&&active())Repair();};watchdog.Start();
 }
 static bool IsShell(IntPtr hwnd){if(hwnd==IntPtr.Zero||hwnd==GetDesktopWindow())return true;var root=GetAncestor(hwnd,2);var name=new StringBuilder(96);GetClassName(root,name,name.Capacity);return name.ToString()=="Shell_TrayWnd"||name.ToString()=="Shell_SecondaryTrayWnd";}
 void OnEvent(IntPtr hook,uint eventId,IntPtr hwnd,int obj,int child,uint thread,uint time){
  if(disposed||!active()||target.IsDisposed)return;
  bool topLevel=obj==0&&child==0&&hwnd!=IntPtr.Zero&&GetAncestor(hwnd,2)==hwnd;
  if(eventId<=7||IsShell(hwnd)||(topLevel&&(eventId==0x8002||eventId==0x8003||eventId==0x8017||eventId==0x8018))){EventsDelivered++;Repair();until=DateTime.UtcNow.AddMilliseconds(1500);burst.Start();}
 }
 void Repair(){if(repairing||disposed)return;try{repairing=true;repair();}finally{repairing=false;}}
 public void Dispose(){if(disposed)return;disposed=true;burst.Dispose();watchdog.Dispose();foreach(var hook in new[]{foreground,reorder,menus,visibility,cloaking})if(hook!=IntPtr.Zero)UnhookWinEvent(hook);if(callbackRoot.IsAllocated)callbackRoot.Free();}
}
