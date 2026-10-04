using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class RuntimePerformanceTests {
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Pump(Task task){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(5)};var deadline=DateTime.UtcNow.AddSeconds(8);timer.Tick+=(s,e)=>{if(task.IsCompleted||DateTime.UtcNow>deadline)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();Check(task.IsCompleted,"Background lifecycle operation timed out");task.GetAwaiter().GetResult();}
 internal static void Run(){var prior=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());try{RunCore();}finally{SynchronizationContext.SetSynchronizationContext(prior);}}
 static void RunCore(){
  Check(!ProcessGuard.FromNames(new[]{"unrelated"}).Busy&&ProcessGuard.FromNames(new[]{"GENSHINIMPACT"}).Game&&ProcessGuard.FromNames(new[]{"YuanShen"}).Busy&&ProcessGuard.FromNames(new[]{"unlockfps_nc_signed"}).Unlocker,"Fresh process samples must preserve both game channels and unlocker busy guards");
  using(var released=new ManualResetEventSlim())using(var shell=new Launcher(null,false,true,false,false,connectAi:false)){
   int reads=0,beats=0;shell.processState=()=>{Interlocked.Increment(ref reads);released.Wait(5000);return ProcessGuard.FromNames(new string[0]);};
   var heartbeat=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};heartbeat.Tick+=(s,e)=>{beats++;if(beats>=3)released.Set();};heartbeat.Start();
   try{var pending=shell.TickAsync();Check(!pending.IsCompleted,"A blocked process scan must run outside the UI thread");Check(shell.TickAsync().IsCompleted,"Overlapping periodic scans must coalesce");Pump(pending);Check(beats>=3&&reads==1&&Equals(((Button)shell.Window.FindName("Play")).Content,Locale.T("게임 시작")),"Slow process scans must keep the UI heartbeat and use one fresh sample");}finally{released.Set();heartbeat.Stop();}
   released.Reset();shell.processState=()=>{released.Wait(5000);return ProcessGuard.FromNames(new[]{"GenshinImpact"});};var late=shell.TickAsync();var play=(Button)shell.Window.FindName("Play");play.Content="fixture-after-close";shell.Dispose();released.Set();Pump(late);Check(Equals(play.Content,"fixture-after-close"),"Late process results must not mutate a disposed shell");
  }
  string root=Path.Combine(Path.GetTempPath(),"catheryne-transport-stop-"+Guid.NewGuid().ToString("N"));
  try{using(var chat=new CodexChat(root)){
   var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -Command Start-Sleep -Seconds 20"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true};var child=Process.Start(info);try{
    typeof(CodexChat).GetField("process",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(chat,child);typeof(CodexChat).GetField("input",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(chat,child.StandardInput);
    int beats=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};timer.Tick+=(s,e)=>beats++;timer.Start();try{var pending=(Task)typeof(CodexChat).GetMethod("CloseTransport",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(chat,null);Check(!pending.IsCompleted,"Transport shutdown must yield while the child exits");Pump(pending);Check(beats>0,"AI reconnect shutdown must keep the UI responsive");}finally{timer.Stop();}
   }finally{try{if(!child.HasExited)child.Kill();}catch(InvalidOperationException){}child.Dispose();}
  }}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
