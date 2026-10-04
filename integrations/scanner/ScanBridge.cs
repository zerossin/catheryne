using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
namespace CatheryneScanning {
 internal static class ScanBridge {
  internal static string Folder; static readonly object Gate=new object(); static int LastCount; static int? LastTotal;
  internal static bool Active {get{return Folder!=null;}}
  internal static bool Stopped {get{return Active&&File.Exists(Path.Combine(Folder,"stop"));}}
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
  internal static void RequireSession(){
   string value=Environment.GetEnvironmentVariable("CATHERYNE_GAME_SESSION");if(value==null)return;
   int child,parent;using(var process=Process.GetCurrentProcess()){
    if(!int.TryParse(value,out child)||!int.TryParse(Environment.GetEnvironmentVariable("CATHERYNE_GAME_PARENT"),out parent)||child<0||parent<0||child==parent||child!=process.SessionId)throw new InvalidOperationException("Invalid isolated scan session");
   }
  }
  internal static Process FindProcess(string name){
   RequireSession();int session;using(var current=Process.GetCurrentProcess())session=current.SessionId;
   Process found=null;foreach(var process in Process.GetProcessesByName(name)){if(found==null&&process.SessionId==session)found=process;else process.Dispose();}return found;
  }
  internal static bool GameRunning(){foreach(string name in new[]{"GenshinImpact","YuanShen"})using(var process=FindProcess(name))if(process!=null)return true;return false;}
  internal static void Check(bool foreground=false){RequireSession();if(Stopped)throw new OperationCanceledException();if(Active&&File.Exists(Path.Combine(Folder,"owner"))){int owner;if(!int.TryParse(File.ReadAllText(Path.Combine(Folder,"owner")),out owner))throw new OperationCanceledException();try{using(var process=Process.GetProcessById(owner)){if(process.HasExited)throw new OperationCanceledException();}}catch(ArgumentException){throw new OperationCanceledException();}}if(foreground&&Active){uint id;GetWindowThreadProcessId(GetForegroundWindow(),out id);string name;try{name=Process.GetProcessById((int)id).ProcessName;}catch{throw new OperationCanceledException();}if(name!="GenshinImpact"&&name!="YuanShen")throw new OperationCanceledException("게임이 전경에서 벗어났습니다.");}}
  internal static void Write(string state,string phase,int? count=null,int? total=null,string error=null){if(!Active)return;lock(Gate){if(count.HasValue)LastCount=count.Value;if(total.HasValue)LastTotal=total;Publish(Path.Combine(Folder,"status.json"),JsonConvert.SerializeObject(new{state=state,phase=phase,count=LastCount,total=LastTotal,error=error,updatedAt=DateTime.UtcNow.ToString("o")}));}}
  // Readers and virus scanners can briefly hold the destination. Keep the old
  // complete snapshot until the atomic replacement succeeds; never delete it first.
  internal static void Publish(string target,string text){
   string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{
    File.WriteAllText(temp,text);
    for(int attempt=0;;attempt++){
     try{if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);return;}
     catch(IOException error){int code=error.HResult&0xffff;if(attempt>=8||(code!=32&&code!=33&&code!=1175&&code!=5))throw;System.Threading.Thread.Sleep(25*(attempt+1));}
    }
   }finally{if(File.Exists(temp))File.Delete(temp);}
  }
  internal static void Result(object value){Check();string temp=Path.Combine(Folder,"result.tmp");File.WriteAllText(temp,JsonConvert.SerializeObject(value));File.Move(temp,Path.Combine(Folder,"result.json"));}
  internal static bool Enter(string[] args){if(args.Length!=2||args[0]!="--catheryne-scan")return false;System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;Folder=Path.GetFullPath(args[1]);if(!Directory.Exists(Folder))throw new DirectoryNotFoundException();return true;}
 }
}
