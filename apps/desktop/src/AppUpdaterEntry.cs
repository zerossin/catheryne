using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

internal static class AppUpdaterEntry {
 static int Main(string[] args){
  if(args.Length!=4||args[0]!="--apply")return 2;
  string root;try{root=Path.GetFullPath(args[1]);}catch{return 2;}int pid;long started;
  if(!int.TryParse(args[2],out pid)||!long.TryParse(args[3],out started)||pid<=0||started<=0)return 2;
  AppUpdatePlan plan=null;bool exited=false,locked=false;
  try{
   plan=AppUpdateProtocol.ReadPlan(root);
   // Validate before waiting, and again after the old GUI has released its files.
   AppUpdateProtocol.ValidatePlan(plan,plan.AppDirectory,root);
   Process caller=null;try{caller=Process.GetProcessById(pid);}catch(ArgumentException){exited=true;}
   if(caller!=null)using(var parent=caller){
    if(!parent.HasExited)try{
    if(parent.StartTime.ToUniversalTime().Ticks!=started||!string.Equals(parent.MainModule.FileName,Path.Combine(plan.AppDirectory,"GenshinLauncher.exe"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unexpected app update caller");
    if(!parent.WaitForExit(60000))throw new IOException("App did not finish closing");
    }catch(InvalidOperationException){if(!parent.HasExited)throw;}catch(System.ComponentModel.Win32Exception){if(!parent.HasExited)throw;}
   }
   exited=true;
   using(var mutex=new Mutex(false,AppUpdateProtocol.LockName(plan.AppDirectory))){
    try{locked=mutex.WaitOne(0);}catch(AbandonedMutexException){locked=true;}
    if(!locked)throw new IOException("Another app update is in progress");
    try{using(var lifecycle=PrivateIpc.Mutex(AppUpdateProtocol.EnvironmentLifecycleMutex)){
     bool held=false;try{
      try{held=lifecycle.WaitOne(15000);}catch(AbandonedMutexException){held=true;}
      if(!held)throw new IOException("Game environment request is in progress");
      using(var input=GameInputLease.Acquire(root)){
       AppUpdateProtocol.ValidatePlan(plan,plan.AppDirectory,root);
       if(AppUpdateProtocol.EnvironmentActive()||ProcessGuard.Busy())throw new IOException("Game environment or collection is active");
       using(var package=new FileStream(plan.Installer,FileMode.Open,FileAccess.Read,FileShare.Read)){
        AppUpdateProtocol.Verify(package,plan.Release);
        using(var installer=Process.Start(new ProcessStartInfo(plan.Installer,AppUpdateProtocol.InstallerArguments(plan)){UseShellExecute=false,WorkingDirectory=plan.AppDirectory,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){
         installer.WaitForExit();
         if(installer.ExitCode!=0||!AppUpdateProtocol.InstallationMatches(plan))throw new IOException("App installation did not complete");
        }
       }
       AppUpdateProtocol.Write(Path.Combine(AppUpdateProtocol.Cache(root),"result.json"),new{state="installed",version=plan.Release.Version,at=DateTime.UtcNow.ToString("o")});
       File.Delete(AppUpdateProtocol.Pending(root));
      }
     }finally{if(held)lifecycle.ReleaseMutex();}
    }}finally{if(locked)mutex.ReleaseMutex();}
   }
   return 0;
  }catch(Exception){
   try{AppUpdateProtocol.Write(Path.Combine(AppUpdateProtocol.Cache(root),"result.json"),new{state="failed",version=plan!=null&&plan.Release!=null?plan.Release.Version:"",packageHash=plan!=null&&plan.Release!=null?plan.Release.Sha256:"",at=DateTime.UtcNow.ToString("o")});}catch{}
   return 1;
  }finally{
   // Start in the home screen, preserving the previous game launch preference.
   if(exited&&plan!=null)try{Process.Start(new ProcessStartInfo(Path.Combine(plan.AppDirectory,"GenshinLauncher.exe"),"--preview"){UseShellExecute=true,WorkingDirectory=plan.AppDirectory});}catch{}
  }
 }
}
