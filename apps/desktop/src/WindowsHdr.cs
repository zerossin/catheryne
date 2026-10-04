using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal sealed class WindowsHdrState {
 public int Monitor;public string Identity="",Error="";public bool Supported,Enabled,Blocked;
 internal bool CanSet {get{return Supported&&!Blocked&&Error.Length==0;}}
}
internal sealed class WindowsHdr {
 internal static Func<int,bool?,string,WindowsHdrState> TestBackend;
 readonly Func<int,bool?,string,WindowsHdrState> backend;
 internal WindowsHdr(Func<int,bool?,string,WindowsHdrState> backend=null){this.backend=backend??TestBackend??Native;}
 [DllImport("Catheryne.Capture.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]static extern int CatheryneDisplayHdr(string display,string expected,int desired,out int flags,StringBuilder identity,int capacity);
 static WindowsHdrState Native(int number,bool? enabled,string expected){
  var screens=System.Windows.Forms.Screen.AllScreens.OrderByDescending(s=>s.Primary).ToArray();
  if(number<1||number>screens.Length)throw new InvalidOperationException(Locale.T("선택한 모니터가 연결되어 있지 않습니다."));
  var identity=new StringBuilder(256);int flags;int code=CatheryneDisplayHdr(screens[number-1].DeviceName,expected,enabled.HasValue?(enabled.Value?1:0):-1,out flags,identity,identity.Capacity);
  if(code<0)throw new InvalidOperationException(Locale.Format("Windows HDR을 확인하거나 변경하지 못했습니다. {0}",new Win32Exception(code&65535).Message));
  return new WindowsHdrState{Monitor=number,Identity=identity.ToString(),Supported=(flags&1)!=0,Enabled=(flags&2)!=0,Blocked=(flags&4)!=0};
 }
 internal WindowsHdrState Read(int monitor){try{return backend(monitor,null,null);}catch(Exception error){return new WindowsHdrState{Monitor=monitor,Error=error.Message};}}
 internal WindowsHdrState Set(int monitor,bool enabled,string expected=null){
  // Serialize read/write/readback across GUI, presets and AI processes.
  using(var mutex=new Mutex(false,"Local\\Catheryne.WindowsHdr")){bool held=false;try{try{held=mutex.WaitOne(5000);}catch(AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException(Locale.T("Windows HDR을 변경하는 중입니다."));
   var before=backend(monitor,null,expected);if(!before.CanSet)throw new InvalidOperationException(Locale.T("선택한 모니터에서 Windows HDR을 변경할 수 없습니다."));if(before.Enabled==enabled)return before;
   var result=backend(monitor,enabled,expected??before.Identity);for(int i=0;result.Enabled!=enabled&&i<10;i++){Thread.Sleep(100);result=backend(monitor,null,before.Identity);}
   if(result.Enabled!=enabled)throw new InvalidOperationException(Locale.T("Windows HDR 변경을 확인하지 못했습니다."));return result;
  }finally{if(held)mutex.ReleaseMutex();}}
 }
}
