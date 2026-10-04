using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
internal static class DailyAgent {
 const string EventName="Local\\Catheryne.Daily.Stop";
 internal static void Stop(){try{using(var stop=EventWaitHandle.OpenExisting(EventName))stop.Set();}catch(WaitHandleCannotBeOpenedException){}}
 internal static int Run(){bool first;using(var mutex=new Mutex(true,"Local\\Catheryne.Daily",out first)){
  if(!first)return 0;using(var stop=new EventWaitHandle(false,EventResetMode.ManualReset,EventName)){
   var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var owner=new Window();var daily=new DailyPanel(owner);
   var tray=new System.Windows.Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Information,Visible=true,Text="Catheryne · Daily"};
   tray.ContextMenuStrip=new System.Windows.Forms.ContextMenuStrip();tray.ContextMenuStrip.Items.Add("Open Catheryne",null,(s,e)=>Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GenshinLauncher.exe"),"--preview"){UseShellExecute=true}));tray.ContextMenuStrip.Items.Add("Exit",null,(s,e)=>app.Shutdown());
   var registration=ThreadPool.RegisterWaitForSingleObject(stop,(s,t)=>app.Dispatcher.BeginInvoke(new Action(()=>app.Shutdown())),null,-1,true);
   try{app.Run();}finally{registration.Unregister(null);daily.Dispose();tray.Dispose();}return 0;
  }
 }}
 internal static void Configure(bool enabled){
  using(var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){
   if(enabled)run.SetValue("CatheryneDaily",StoryClient.Quote(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GenshinLauncher.exe"))+" --daily-agent");else run.DeleteValue("CatheryneDaily",false);
  }
  AppPreferences.Set("backgroundDaily",enabled);if(!enabled)Stop();
 }
 internal static void StartIfEnabled(){if(AppPreferences.Flag("backgroundDaily"))Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GenshinLauncher.exe"),"--daily-agent"){UseShellExecute=false,CreateNoWindow=true});}
}
