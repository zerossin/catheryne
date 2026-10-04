using System;
using System.Diagnostics;
using System.IO;

// Same byte-range lock as story_control.service.HostLock. OS closes it on process exit.
internal sealed class GameInputLease : IDisposable {
 FileStream stream;
 internal static string LockPath(string root){return NativePaths.Resolve(Path.Combine(root,"runtime","input.lock"));}
 internal static GameInputLease Acquire(string root,bool checkProcesses=true){
  var lease=new GameInputLease();string path=LockPath(root);Directory.CreateDirectory(Path.GetDirectoryName(path));
  try{
   lease.stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);
   if(lease.stream.Length==0){lease.stream.WriteByte(0);lease.stream.Flush();}
   lease.stream.Lock(0,1);
   if(checkProcesses)foreach(string name in new[]{"BetterGI","AkashaScanner","InventoryKamera"}){
    var processes=Process.GetProcessesByName(name);bool running=processes.Length>0;foreach(var process in processes)process.Dispose();
    if(running)throw new InvalidOperationException("다른 게임 작업이 실행 중입니다. 해당 작업을 종료한 뒤 시작해 주세요.");
   }
   return lease;
  }catch(IOException){lease.Dispose();throw new InvalidOperationException("다른 게임 작업이 입력을 사용하고 있습니다.");}catch{lease.Dispose();throw;}
 }
 internal static void CheckAvailable(string root){using(Acquire(root)){} }
 public void Dispose(){if(stream!=null){stream.Dispose();stream=null;}}
}
