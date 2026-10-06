using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

internal static class ServiceTests {
 static void Assert(bool condition,string name){if(!condition)throw new Exception(name);}
 static System.Diagnostics.Process Sleeper(){
  return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"ping.exe"),"-n 30 127.0.0.1"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true});
 }
 static void CheckUnlockerOwnership(){
  using(var unrelated=Sleeper()){
   try{
    using(var owned=Sleeper()){
     Assert(UnlockerSession.Monitor(owned,()=>null,TimeSpan.Zero,()=>{})=="game_start_timeout"&&owned.HasExited,"failed game start closes owned unlocker");
     Assert(!unrelated.HasExited,"other process remains running");
    }
    using(var owned=Sleeper()){
     var exited=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"),"/c exit 0"){UseShellExecute=false,CreateNoWindow=true});exited.WaitForExit();
     bool observed=false;
     Assert(UnlockerSession.Monitor(owned,()=>exited,TimeSpan.FromSeconds(2),()=>observed=true)=="game_exited"&&observed&&owned.HasExited,"game exit closes owned unlocker");
    }
    using(var owned=Sleeper()){
     Reject(()=>UnlockerSession.Monitor(owned,()=>{throw new IOException("observation failed");},TimeSpan.FromSeconds(2),()=>{}),"observation error propagated");
     Assert(owned.HasExited,"observation error does not leak owned unlocker");
    }
   }finally{if(!unrelated.HasExited){unrelated.Kill();unrelated.WaitForExit();}}
  }
 }
 static void Reject(Action action,string name){bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected,name);}
 static void CheckInputLeaseInterop(){
  string python=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime-build","python.exe");
  if(!File.Exists(python))return; // Build-time protocol check; installed self-test has no build runtime.
  string source=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","core","story-control","src"));
  string root=Path.Combine(Path.GetTempPath(),"catheryne-lease-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string script=Path.Combine(root,"lease_test.py");
  File.WriteAllText(script,"import sys\nsys.path.insert(0,sys.argv[1])\nfrom story_control.service import HostLock\ntry:\n with HostLock(sys.argv[2]):\n  print('owned',flush=True)\n  sys.stdin.readline()\nexcept RuntimeError:\n print('blocked',flush=True)\n");
  Func<System.Diagnostics.Process> start=()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(python,StoryClient.Quote(script)+" "+StoryClient.Quote(source)+" "+StoryClient.Quote(GameInputLease.LockPath(root))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true});
  try{
   using(GameInputLease.Acquire(root,false))using(var child=start()){Assert(child.StandardOutput.ReadLine()=="blocked","Python host is blocked by desktop input owner");Assert(child.WaitForExit(5000),"Blocked host exits");}
   using(var child=start()){try{Assert(child.StandardOutput.ReadLine()=="owned","Python host acquires released input lease");Reject(()=>{using(GameInputLease.Acquire(root,false)){}},"Desktop executor is blocked by Python owner");}finally{if(!child.HasExited){child.Kill();child.WaitForExit();}}}
   using(GameInputLease.Acquire(root,false)){} // Process death releases the same lease.
  }finally{Directory.Delete(root,true);}
 }
 static void CheckPrivateIpc(){
  string name="Catheryne.PipeTest."+Guid.NewGuid().ToString("N");
  using(var first=PrivateIpc.Server(name,8,4096)){
   var waiting=System.Threading.Tasks.Task.Run(()=>PrivateIpc.Accept(first,System.Threading.CancellationToken.None));
   using(var client=new System.IO.Pipes.NamedPipeClientStream(".",name,System.IO.Pipes.PipeDirection.InOut)){
    client.Connect(2000);Assert(waiting.Wait(2000)&&waiting.Result,"private pipe accepts its client");
    // Keep a connected request open while the worker creates its next listener.
    using(var second=PrivateIpc.Server(name,8,4096)){
     var next=System.Threading.Tasks.Task.Run(()=>PrivateIpc.Accept(second,System.Threading.CancellationToken.None));
     using(var other=new System.IO.Pipes.NamedPipeClientStream(".",name,System.IO.Pipes.PipeDirection.InOut)){
      other.Connect(2000);Assert(next.Wait(2000)&&next.Result,"overlapping request reaches the next listener");
      GameEnvironment.Write(first,new{value=1});GameEnvironment.Write(second,new{value=2});
      Assert(Convert.ToInt32(GameEnvironment.Read(client)["value"])==1&&Convert.ToInt32(GameEnvironment.Read(other)["value"])==2,"overlapping private connections retain independent replies");
     }
    }
   }
   var acl=first.GetAccessControl();var rules=acl.GetAccessRules(true,false,typeof(System.Security.Principal.SecurityIdentifier));
   using(var identity=System.Security.Principal.WindowsIdentity.GetCurrent())Assert(acl.AreAccessRulesProtected&&rules.Count==1&&rules[0].IdentityReference.Equals(identity.User),"private pipe keeps its protected current-user ACL");
  }
  using(var recreated=PrivateIpc.Server(name,8,4096))Assert(recreated.CanRead&&recreated.CanWrite,"private listener can be recreated after every instance closes");
  for(int i=0;i<16;i++)using(var cancel=new System.Threading.CancellationTokenSource())using(var server=PrivateIpc.Server("Catheryne.CancelTest."+Guid.NewGuid().ToString("N"),1,4096)){
   var waiting=System.Threading.Tasks.Task.Run(()=>PrivateIpc.Accept(server,cancel.Token));if(i%2==0)System.Threading.Thread.Sleep(5);cancel.Cancel();
   Assert(waiting.Wait(2000)&&!waiting.Result,"canceling a pending private connection completes without disposing its callback handle early");
  }
 }
 static void CheckConnectionConfiguration(){
  Assert(!string.IsNullOrWhiteSpace(ChildSessionView.DisconnectMessage(2055,null)),"authentication failure remains actionable when Windows returns no error description");
  Assert(ChildSessionView.DisconnectMessage(1028,"Socket failure")=="Socket failure"&&!string.IsNullOrWhiteSpace(ChildSessionView.DisconnectMessage(1028," ")),"connection errors preserve Windows detail and have a nonempty fallback");
  Assert(!ChildSessionView.SupportsRelativeMouse(new Version(10,0,19045,0))&&!ChildSessionView.SupportsRelativeMouse(new Version(10,0,20348,0))&&!ChildSessionView.SupportsRelativeMouse(new Version(10,0,26099,9999)),"Windows 10, Server 2022 and pre-24H2 RDP clients never receive the unsupported relative mouse setting");
  Assert(ChildSessionView.SupportsRelativeMouse(new Version(10,0,26100,0))&&ChildSessionView.SupportsRelativeMouse(new Version(10,0,26200,0)),"relative mouse configuration starts at the actual RDP client 24H2 version and supports later clients");
  // Exercise the actual COM property ABI without connecting or prompting for credentials.
  using(var form=new System.Windows.Forms.Form())using(var view=new ChildSessionView()){
   ((System.ComponentModel.ISupportInitialize)view).BeginInit();form.Controls.Add(view);((System.ComponentModel.ISupportInitialize)view).EndInit();
   IntPtr handle=form.Handle;view.CreateControl();int configured=0;
   view.Trace=(phase,code)=>{Assert(phase=="configured"&&!code.HasValue,"configuration diagnostics contain no login or credential payload");configured++;};
   view.Configure(DisplayPresets.AiWidth,DisplayPresets.AiHeight);
   Assert(configured==1&&view.Connection==0&&!view.LoggedIn,"child connection and pointer settings roundtrip without authentication or input");
   view.Configure(DisplayPresets.AiWidth,DisplayPresets.AiHeight,true);view.Configure(DisplayPresets.AiWidth,DisplayPresets.AiHeight);
   Assert(configured==3&&view.Connection==0,"credential prompting and the Windows remember choice are explicit and reset for an automatic attempt");
   var client=typeof(System.Windows.Forms.AxHost).GetMethod("GetOcx").Invoke(view,null);var advanced=WindowsChildSession.Get(client,"AdvancedSettings7");
   Assert(Convert.ToBoolean(WindowsChildSession.Get(advanced,"EnableAutoReconnect"))&&Convert.ToInt32(WindowsChildSession.Get(advanced,"MaxReconnectAttempts"))==ChildSessionView.ReconnectAttempts,"actual Windows reconnect configuration is enabled and bounded");
   var extendedType=typeof(ChildSessionView).GetNestedType("ExtendedSettings",System.Reflection.BindingFlags.NonPublic);
   if(ChildSessionView.SupportsRelativeMouse(ChildSessionView.ClientVersion))Assert(Equals(extendedType.GetMethod("GetProperty").Invoke(client,new object[]{"AllowRelativeMouseMode"}),true),"supported native RDP client actually enables relative mouse input");
   int nativeError=0;
   try{extendedType.GetMethod("SetProperty").Invoke(client,new object[]{"CatheryneUnsupportedPropertyProbe",true});}
   catch(System.Reflection.TargetInvocationException error){nativeError=error.GetBaseException().HResult;}
   Assert(nativeError!=0,"actual native RDP client rejects an unknown property");
   try{
    typeof(ChildSessionView).GetMethod("SetExtendedFlag",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{client,"CatheryneUnsupportedPropertyProbe","not applied"});
    throw new Exception("Unknown native RDP property was accepted");
   }catch(System.Reflection.TargetInvocationException error){
    var cause=error.GetBaseException();Assert(cause is System.Runtime.InteropServices.COMException&&cause.HResult==nativeError&&cause.Message.Contains("CatheryneUnsupportedPropertyProbe"),"unsupported native settings remain failures with their exact property and original HRESULT");
   }
   var eventsType=typeof(ChildSessionView).GetNestedType("SessionEvents",System.Reflection.BindingFlags.NonPublic);
   var events=Activator.CreateInstance(eventsType,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{view},null);
   var traces=new System.Collections.Generic.List<string>();view.Trace=(phase,code)=>traces.Add(phase);
   Func<int,int,int> reconnect=(reason,attempt)=>{var args=new object[]{reason,attempt,null};eventsType.GetMethod("OnAutoReconnecting").Invoke(events,args);return Convert.ToInt32(args[2]);};
   Assert(reconnect(1028,1)==1&&!view.Reconnecting&&view.Failure!=null,"an unauthenticated connection does not retry or request credentials");
   eventsType.GetMethod("OnLoginComplete").Invoke(events,null);
   Assert(view.LoggedIn&&view.Failure==null,"successful logon clears only its prior connection failure");
   for(int attempt=1;attempt<=ChildSessionView.ReconnectAttempts;attempt++)Assert(reconnect(1028,attempt)==0&&view.Reconnecting&&view.Failure==null,"a transient loss preserves the authenticated connection during native retry");
   eventsType.GetMethod("OnAutoReconnected").Invoke(events,null);
   Assert(view.LoggedIn&&!view.Reconnecting&&view.Failure==null&&traces.Contains("reconnected"),"native reconnection completion restores the same control and records recovery");
   view.Configure(DisplayPresets.AiWidth,DisplayPresets.AiHeight,true);eventsType.GetMethod("OnLoginComplete").Invoke(events,null);
   var authenticationType=typeof(ChildSessionView).GetNestedType("ClientAuthentication",System.Reflection.BindingFlags.NonPublic);
   Assert(!Convert.ToBoolean(authenticationType.GetMethod("GetAllowPromptingForCredentials").Invoke(client,null)),"a completed explicit logon cannot show another credential prompt during recovery");
   Assert(reconnect(2055,1)==1&&!view.Reconnecting&&view.FailureKind=="authentication","authentication failure stops native retry even after a prior logon");
   eventsType.GetMethod("OnLoginComplete").Invoke(events,null);
   Assert(reconnect(1028,ChildSessionView.ReconnectAttempts+1)==1&&!view.Reconnecting&&view.FailureKind=="connection","native retry exhaustion becomes a terminal connection failure");
   Assert(view.Connection==0&&!form.Visible,"reconnection event checks send no actual connection, credentials, input or shown UI");
  }
 }
 static void CheckConnectionWait(){
  Func<string,string,System.Collections.Generic.Dictionary<string,object>> endpoint=(state,phase)=>new System.Collections.Generic.Dictionary<string,object>{{"state",state},{"connection_phase",phase},{"error","authentication canceled"}};
  int reads=0;
  var connected=GameEnvironment.WaitForConnection(()=>++reads<3?endpoint("starting","authenticating"):endpoint("ready","starting-worker"),null,null,TimeSpan.Zero,true);
  Assert(reads==3&&CodexChat.S(connected,"state")=="ready","user sign-in can outlast the connection deadline without replacing the connection");
  reads=0;Reject(()=>GameEnvironment.WaitForConnection(()=>++reads<2?endpoint("starting","authenticating"):endpoint("starting","starting-worker"),null,null,TimeSpan.Zero,true),"worker startup deadline still applies after sign-in");
  Assert(reads==2,"signed-in worker phase receives its own finite startup deadline");
  Reject(()=>GameEnvironment.WaitForConnection(()=>endpoint("starting","authenticating"),null,null,TimeSpan.Zero,false),"automatic connection cannot wait indefinitely for sign-in");
  reads=0;Reject(()=>GameEnvironment.WaitForConnection(()=>++reads<2?endpoint("starting","authenticating"):endpoint("stopped",""),null,null,TimeSpan.Zero,true),"explicit environment stop ends the sign-in wait");
  Assert(reads==2,"stopped authentication connection is not restarted");
  Reject(()=>GameEnvironment.WaitForConnection(()=>endpoint("failed","authenticating"),null,null,TimeSpan.Zero,true),"authentication failure exits rather than retaining a failed prompt");
  reads=0;Reject(()=>GameEnvironment.WaitForConnection(()=>{reads++;return endpoint("ready","");},()=>{throw new OperationCanceledException();},null,TimeSpan.Zero,true),"cancellation wins over a late authentication acknowledgement");
  Assert(reads==0,"canceled request does not inspect a connection");
  Reject(()=>GameEnvironment.WaitForConnection(()=>endpoint("starting","authenticating"),null,()=>{throw new IOException("host exited");},TimeSpan.Zero,true),"host exit interrupts the unbounded user sign-in wait");
  reads=0;Reject(()=>GameEnvironment.WaitForConnection(()=>{reads++;return endpoint("starting","authenticating");},null,null,TimeSpan.FromMinutes(1),false),"a gameplay request does not wait for a user's pending sign-in");
  Assert(reads==1,"pending user authentication fails immediately without replacing or retrying the connection");
 }
 static void CheckConnectionFailure(){
  string root=Path.Combine(Path.GetTempPath(),"catheryne-connection-failure-"+Guid.NewGuid().ToString("N"));int priorChild=WindowsChildSession.Child;
  var type=typeof(GameEnvironment).GetNestedType("EnvironmentWindow",System.Reflection.BindingFlags.NonPublic);
  try{
   Directory.CreateDirectory(Path.GetDirectoryName(GameEnvironment.Endpoint(root,WindowsChildSession.Current)));
   using(var form=(System.Windows.Forms.Form)Activator.CreateInstance(type,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{root,WindowsChildSession.Current,false},null)){
    var view=(ChildSessionView)type.GetField("view",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(form);view.LoggedIn=true;view.Reconnecting=true;
    var poll=type.GetMethod("Poll",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);poll.Invoke(form,null);
    Assert(!form.IsDisposed&&CodexChat.S(StoryClient.Read(GameEnvironment.Endpoint(root,WindowsChildSession.Current)),"state")=="starting","host retains its same hidden control while native reconnection is in progress");
    type.GetField("connectionTimeout",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(form,TimeSpan.Zero);poll.Invoke(form,null);
    Assert((bool)type.GetProperty("StartupFailed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(form,null),"native reconnection deadline cannot leave an indefinitely waiting host");
    Assert(WindowsChildSession.Child==priorChild&&!form.Visible,"timed-out unbound reconnection never logs off another session or opens a window");
   }
   using(var form=(System.Windows.Forms.Form)Activator.CreateInstance(type,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{root,WindowsChildSession.Current,false},null)){
    Assert(!form.Visible,"connection failure check never opens a window or connects");
    type.GetMethod("Fail",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(form,new object[]{new System.Runtime.InteropServices.COMException("private-child-session-failure",unchecked((int)0x80070057))});
    Assert((bool)type.GetProperty("StartupFailed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(form,null),"failed host cannot report successful completion");
   }
   Assert(WindowsChildSession.Child==priorChild,"unconnected host cleanup preserves an existing child session");
   Assert(CodexChat.S(GameEnvironment.Status(root),"state")=="failed","connection failure leaves an authoritative failed receipt");
   using(var db=new LocalDataService(root)){
    var trace=CodexChat.Map(CatheryneTools.Json().DeserializeObject(db.Recent("default","game-environment",1)[0]["payload"]));
    Assert(CodexChat.S(trace,"phase")=="connection-failed"&&Convert.ToInt32(trace["code"])==unchecked((int)0x80070057),"connection journal retains the original failure code");
   }
   string report=new AppDiagnostics(root).Export();Assert(report.Contains("0x80070057")&&!report.Contains("private-child-session-failure"),"support diagnostics retain failure type and code without private exception text");
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 static void CheckEnvironmentLifecycle(){
  var failed=new System.Collections.Generic.Dictionary<string,object>{{"state","failed"},{"failure_kind","authentication"}};
  Reject(()=>GameEnvironment.RequireConnectionRequest(failed,false,false),"gameplay cannot reopen failed authentication");
  GameEnvironment.RequireConnectionRequest(failed,true,false);GameEnvironment.RequireConnectionRequest(failed,false,true);
  GameEnvironment.RequireConnectionRequest(new System.Collections.Generic.Dictionary<string,object>{{"state","failed"},{"failure_kind","connection"}},false,false);
  var jobs=new System.Threading.Tasks.Task[8];int inside=0,completed=0;
  for(int i=0;i<jobs.Length;i++)jobs[i]=System.Threading.Tasks.Task.Run(()=>GameEnvironment.WithLifecycleLock(()=>{
   Assert(System.Threading.Interlocked.Increment(ref inside)==1,"concurrent lifecycle operations never replace each other's ownership");
   try{System.Threading.Thread.Sleep(10);System.Threading.Interlocked.Increment(ref completed);}finally{System.Threading.Interlocked.Decrement(ref inside);}return 0;
  }));
  Assert(System.Threading.Tasks.Task.WaitAll(jobs,5000)&&completed==jobs.Length,"concurrent starts serialize and all callers finish");
  bool called=false;Reject(()=>GameEnvironment.WithLifecycleLock(()=>{called=true;return 0;},()=>{throw new OperationCanceledException();}),"a canceled request never enters the shared lifecycle operation");
  Assert(!called,"cancellation cannot stop or replace another caller's connection");
  Reject(()=>GameEnvironment.WithLifecycleLock<int>(()=>{throw new IOException("failed action");}),"lifecycle error propagates");
  Assert(GameEnvironment.WithLifecycleLock(()=>42)==42,"failed operation releases the lifecycle gate");
  using(var host=PrivateIpc.Mutex(AppUpdateProtocol.EnvironmentMutex)){
   host.WaitOne();try{Assert(System.Threading.Tasks.Task.Run(()=>AppUpdateService.Busy(Path.GetTempPath())).Result,"full installers defer while an authenticated connection owner is alive without a game");}finally{host.ReleaseMutex();}
   Assert(!AppUpdateProtocol.EnvironmentActive(),"finished environment releases the installation boundary");
  }
  string root=Path.Combine(Path.GetTempPath(),"catheryne-auth-gate-"+Guid.NewGuid().ToString("N"));
  try{
   string receipt=GameEnvironment.Endpoint(root,WindowsChildSession.Current);Directory.CreateDirectory(Path.GetDirectoryName(receipt));AtomicFile.Write(receipt,CatheryneTools.Json().Serialize(failed));string before=File.ReadAllText(receipt);
   Reject(()=>GameEnvironment.Invoke(root,"status",new System.Collections.Generic.Dictionary<string,object>(),true),"actual remote start rejects cached authentication failure before launching any host");
   Assert(File.ReadAllText(receipt)==before,"failed authentication receipt is preserved for explicit recovery");
   long logon=WindowsChildSession.LogonTime(WindowsChildSession.Current);Assert(logon>0,"current Windows logon has a real timestamp");
   failed["host_started"]=logon-1;AtomicFile.Write(receipt,CatheryneTools.Json().Serialize(failed));
   Assert(CodexChat.S(GameEnvironment.Status(root),"state")=="stopped","previous Windows logon's failure cannot block a new automatic connection");
   failed["host_started"]=logon;AtomicFile.Write(receipt,CatheryneTools.Json().Serialize(failed));
   Assert(CodexChat.S(GameEnvironment.Status(root),"state")=="failed","current logon's authentication failure remains latched without another login prompt");
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 static void CheckHostStartup(){
  using(var current=System.Diagnostics.Process.GetCurrentProcess())GameEnvironment.CheckHostStartup(current);
  Reject(()=>GameEnvironment.CheckHostStartup(null),"missing host process fails immediately");
  // A malformed private host request must exit before any GUI, logon or worker starts.
  var info=new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--game-environment-host"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,StandardErrorEncoding=Encoding.UTF8};
  string fixture=Path.Combine(Path.GetTempPath(),"catheryne-host-language-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);fixture=NativePaths.Resolve(fixture);File.WriteAllText(Path.Combine(fixture,"settings.json"),CatheryneTools.Json().Serialize(new{language=Locale.LanguageCode}));info.EnvironmentVariables["CATHERYNE_TOOL_DATA"]=fixture;
  try{using(var failed=System.Diagnostics.Process.Start(info)){
   Assert(failed.WaitForExit(5000)&&failed.ExitCode!=0,"host startup failure exits without leaving a login request");
   Assert(failed.StandardError.ReadToEnd().Contains(Locale.T("게임 실행 환경 요청이 올바르지 않습니다.")),"invalid host request retains its actionable error");
   Reject(()=>GameEnvironment.CheckHostStartup(failed),"early host exit does not wait for a connection deadline");
  }}finally{Directory.Delete(fixture,true);}
 }
 static void CheckProcessImageOwnership(){
  string root=Path.Combine(Path.GetTempPath(),"catheryne-image-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);root=NativePaths.Resolve(root);
  string original=Path.Combine(root,"Catheryne.exe"),frozen=Path.Combine(root,"Catheryne.active.exe");
  File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location,original);
  // The tool command waits for EOF; this fixture performs no authentication, GUI or game input.
  var info=new System.Diagnostics.ProcessStartInfo(original,"--ai-tool"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
  info.EnvironmentVariables["CATHERYNE_TOOL_DATA"]=Path.Combine(root,"data");
  try{
   using(var child=System.Diagnostics.Process.Start(info)){
    try{
     Assert(!child.WaitForExit(100),"input-free ownership fixture remains live");
     long started=child.StartTime.ToUniversalTime().Ticks;int session=child.SessionId;string image=NativePaths.FileIdentity(original);
     Assert(GameEnvironment.SameProcess(child.Id,started,session,image),"running process matches its file identity");
     File.Move(original,frozen);File.Copy(frozen,original);string replacement=NativePaths.FileIdentity(original);
     Assert(NativePaths.FileIdentity(frozen)==image&&replacement!=image,"frozen image keeps its identity while replacement is distinct");
     Assert(GameEnvironment.SameProcess(child.Id,started,session,image),"app update preserves the running owner after its image is renamed");
     Assert(!GameEnvironment.SameProcess(child.Id,started,session,replacement),"replacement image cannot claim the old running process");
     Assert(!GameEnvironment.SameProcess(child.Id,started+1,session,image)&&!GameEnvironment.SameProcess(child.Id,started,session+1,image),"cached image cannot bypass creation time or session ownership");
     child.StandardInput.Close();Assert(child.WaitForExit(5000),"input-free ownership fixture exits on EOF");
     Assert(!GameEnvironment.SameProcess(child.Id,started,session,image),"exited owner is rejected even with a cached file identity");
    }finally{if(!child.HasExited){child.Kill();child.WaitForExit();}}
   }
  }finally{Directory.Delete(root,true);}
 }
 static void CheckWorkerStartup(){
  Action queued=()=>{throw new System.Reflection.TargetInvocationException(new System.Runtime.InteropServices.COMException("queued",unchecked((int)0x8004130B)));};
  foreach(int result in new[]{0x41303,0x41325,0x41301})Assert(!WindowsChildSession.RefreshWorkerTask(queued,()=>result),"queued interactive worker retains its launch request");
  Assert(WindowsChildSession.RefreshWorkerTask(()=>{},()=>{throw new Exception("unexpected result query");}),"running worker refresh does not query a stale task result");
  foreach(int result in new[]{0,1,unchecked((int)0x80041320)})Reject(()=>WindowsChildSession.RefreshWorkerTask(queued,()=>result),"exited or failed worker cannot wait as a queued launch");
  Reject(()=>WindowsChildSession.RefreshWorkerTask(()=>{throw new System.Runtime.InteropServices.COMException("denied",unchecked((int)0x80070005));},()=>0x41325),"scheduler access failure cannot masquerade as a queued worker");
  var logon=new WindowsChildSession.SessionInfo{Session=2,State=0,User="test",Logon=1};
  Assert(WindowsChildSession.HasLogon(2,logon),"active authenticated session can start its worker");
  logon.Logon=0;Assert(!WindowsChildSession.HasLogon(2,logon),"active connection before logon cannot queue an interactive worker");
  logon.Logon=1;logon.User="";Assert(!WindowsChildSession.HasLogon(2,logon),"session without a logged-on user cannot start its worker");
  logon.User="test";Assert(!WindowsChildSession.HasLogon(3,logon),"another session cannot satisfy logon readiness");
  int observations=0,reads=0;
  WindowsChildSession.WaitForWorker(()=>++reads<3?"starting":"ready",()=>observations++,System.Threading.CancellationToken.None,TimeSpan.FromSeconds(2));
  Assert(reads==3&&observations==2,"queued worker waits for acknowledgement without accepting launch request");
  using(var canceled=new System.Threading.CancellationTokenSource()){
   canceled.Cancel();reads=0;
   Reject(()=>WindowsChildSession.WaitForWorker(()=>{reads++;return "ready";},null,canceled.Token,TimeSpan.FromSeconds(2)),"canceled connection rejects even a late ready acknowledgement");
   Assert(reads==0,"canceled startup does not inspect or restart the connection");
  }
  Reject(()=>WindowsChildSession.WaitForWorker(()=>"failed",null,System.Threading.CancellationToken.None,TimeSpan.FromSeconds(2)),"failed connection ends worker startup wait");
  Reject(()=>WindowsChildSession.WaitForWorker(()=>"starting",null,System.Threading.CancellationToken.None,TimeSpan.Zero),"missing worker acknowledgement has a finite deadline");
  var endpoint=new System.Collections.Generic.Dictionary<string,object>{{"host_pid",10},{"host_started",20L},{"child_session",2},{"parent_session",1},{"pipe","attempt-a"}};
  Assert(GameEnvironment.StartupMatches(endpoint,2,1,"attempt-a",10,20L),"worker acknowledgement belongs to the requested session owner");
  Assert(!GameEnvironment.StartupMatches(endpoint,2,1,"attempt-b",10,20L),"old worker cannot acknowledge a retried launch");
  Assert(!GameEnvironment.StartupMatches(endpoint,3,1,"attempt-a",10,20L),"worker cannot acknowledge another child session");
  Assert(!GameEnvironment.StartupMatches(endpoint,2,1,"attempt-a",10,21L),"reused host PID cannot acknowledge an old launch");
 }
 static void CheckLaunchPurpose(){
  string root=Path.Combine(Path.GetTempPath(),"catheryne-launch-purpose-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   string file=GameEnvironment.Endpoint(root,WindowsChildSession.Current),failed=CatheryneTools.Json().Serialize(new{state="failed",failure_kind="authentication",host_started=DateTime.UtcNow.Ticks,error="fixture authentication failure"});
   Directory.CreateDirectory(Path.GetDirectoryName(file));AtomicFile.Write(file,failed);
   var installation=new Installation{Data=root,Engine=Path.Combine(root,"missing-engine.exe")};
   var config=new System.Collections.Generic.Dictionary<string,object>{{"GamePath",Path.Combine(root,"missing-game.exe")}};
   Assert(!GameEnvironment.Selected(root),"new settings default to ordinary desktop execution");
   foreach(var purpose in new[]{GameLaunchPurpose.Player,GameLaunchPurpose.Automation}){
    bool normal=false;try{LauncherOperations.Start(installation,config,false,purpose);}
    catch(FileNotFoundException){normal=true;}
    catch(InvalidOperationException error){if(error.Message!="게임 또는 언락커가 이미 실행 중입니다.")throw;normal=true;}
    Assert(normal&&File.ReadAllText(file)==failed,"default player and AI launches stay local despite stale child-session authentication errors");
   }
   AppPreferences.Set("gameExecution","isolated",root);
   bool local=false;try{LauncherOperations.Start(installation,config,false,GameLaunchPurpose.Player);}
   catch(FileNotFoundException){local=true;}
   catch(InvalidOperationException error){if(error.Message!="게임 또는 언락커가 이미 실행 중입니다.")throw;local=true;}
   Assert(local,"player launch validates the local game even when isolated automation authentication failed");
   bool blocked=false;try{LauncherOperations.Start(installation,config,false,GameLaunchPurpose.Automation);}
   catch(InvalidOperationException error){if(error.Message!=Locale.T("설정의 자동화 실행에서 연결해 주세요."))throw;blocked=true;}
   Assert(blocked&&File.ReadAllText(file)==failed,"automation still fails closed without resetting authentication or launching on the player desktop");
   Assert(Convert.ToString(GameEnvironment.Status(root)["error"])==ChildSessionView.DisconnectMessage(2055,null)&&File.ReadAllText(file)==failed,"cached authentication errors use the current settings name and locale without rewriting stored diagnostics");
  }finally{Directory.Delete(root,true);}
 }
 static void CheckDisconnectedEnvironment(){
  string root=Path.Combine(Path.GetTempPath(),"catheryne-disconnected-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   AppPreferences.Set("gameExecution","isolated",root);var tools=new GameTools(root);
   Reject(()=>GameEnvironment.Invoke(root,"status",new System.Collections.Generic.Dictionary<string,object>()),"transport defaults to an existing connection");Assert(!tools.Available(),"availability query does not start a disconnected environment");
   foreach(string command in GameTools.Commands)Reject(()=>tools.Run(command,new System.Collections.Generic.Dictionary<string,object>(),"test",null),"disconnected "+command+" cannot create an authenticated environment");
   Reject(()=>tools.ChangeControl("missing","user","test"),"manual handoff requires the existing environment");
   Reject(()=>tools.RequireAspect(16,9),"aspect query cannot create an environment");
   Reject(()=>GameEnvironment.ShowView(root,false),"preview cannot create an environment");
   Reject(()=>GameEnvironment.ShowView(root,true),"manual view cannot create an environment");
   var store=new AiTaskStore(root);var task=store.Begin("fake game task","test","disconnected-stop","test");task.Action="game_control";store.Save(task);
   tools.Run("stop",new System.Collections.Generic.Dictionary<string,object>{{"task_id",task.Id}},"test",null);
   Assert(store.Find(task.Id).State=="cancelled","disconnected game task can be canceled through its existing journal");
   Assert(!File.Exists(GameEnvironment.Endpoint(root,WindowsChildSession.Current)),"disconnected control never creates a connection receipt");
  }finally{Directory.Delete(root,true);}
 }
 internal static void Run(){
  CheckPrivateIpc();CheckConnectionConfiguration();CheckConnectionWait();CheckConnectionFailure();CheckEnvironmentLifecycle();CheckHostStartup();CheckProcessImageOwnership();CheckWorkerStartup();CheckLaunchPurpose();CheckDisconnectedEnvironment();
  using(var current=System.Diagnostics.Process.GetCurrentProcess()){
   Assert(GameEnvironment.OwnsProcess(current.Id,current.StartTime.ToUniversalTime().ToString("o")),"current session owner identity verified");
   Assert(!GameEnvironment.OwnsProcess(current.Id,new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).ToString("o")),"reused PID cannot own an old session task");
  }
  WindowsChildSession.RequireTarget(2,1,2);
  Reject(()=>WindowsChildSession.RequireTarget(1,1,1),"child session cannot be the user desktop");
  Reject(()=>WindowsChildSession.RequireTarget(2,1,3),"changed child session is rejected without fallback");
  Reject(()=>WindowsChildSession.RequireTarget(-1,1,-1),"missing child session cannot receive execution");
  using(var packet=new MemoryStream()){
   new BinaryWriter(packet).Write(int.MaxValue);packet.Position=0;
   Reject(()=>GameEnvironment.Read(packet),"oversized environment request rejected before allocation");
  }
  using(var packet=new MemoryStream()){
   GameEnvironment.Write(packet,new{command="status",parameters=new{}});packet.Position=0;
   Assert(Convert.ToString(GameEnvironment.Read(packet)["command"])=="status","bounded environment request roundtrip");
  }
  CheckInputLeaseInterop();
  CheckUnlockerOwnership();
  string dir=Path.Combine(Path.GetTempPath(),"genshin-services-test-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(dir);
  try {
   string launchId=Guid.NewGuid().ToString("N");
   using(var worker=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--unlocker-session "+StoryClient.Quote(NativePaths.Resolve(dir))+" "+launchId){UseShellExecute=false,CreateNoWindow=true})){
    Assert(worker.WaitForExit(10000)&&worker.ExitCode==1,"launch helper rejects missing installation without opening UI");
   }
   var receipt=StoryClient.Read(Path.Combine(dir,"cache","launch",launchId+".json"));
   Assert(Equals(receipt["started"],false),"launch helper reports failure to caller");
   // A fresh CLI process must establish HTTPS policy without opening a login panel.
   var request=HttpTransport.Create("https://example.invalid/");
   Assert((System.Net.ServicePointManager.SecurityProtocol&System.Net.SecurityProtocolType.Tls12)!=0,"cold HTTP caller enables TLS 1.2");request.Abort();
   string resolved=NativePaths.Resolve(dir);
   Assert(Directory.Exists(resolved)&&NativePaths.Resolve(Path.Combine(dir,"new","state.json"))==Path.Combine(resolved,"new","state.json"),"elevated job resolves existing and future data paths consistently");
   string optionsPath=Path.Combine(dir,"options.json");
   var optionsJson=new JavaScriptSerializer();
   File.WriteAllText(optionsPath,"{\"GamePath\":\"unchanged\",\"LastVersionNotify\":123,\"UnknownFutureField\":42}");
   var optionValues=UnlockerOptions.Defaults();
   optionValues["Fullscreen"]=false;optionValues["PopupWindow"]=true;optionValues["UseCustomRes"]=true;
   optionValues["CustomResX"]=2560;optionValues["CustomResY"]=1440;optionValues["MonitorNum"]=2;optionValues["Priority"]=1;
   optionValues["UseHDR"]=true;optionValues["UsePowerSave"]=true;optionValues["UseMobileUI"]=true;optionValues["StartMinimized"]=true;optionValues["SuspendLoad"]=true;
   optionValues["AdditionalCommandLine"]="-example \"한글 path\"";optionValues["DllList"]=new[]{"second.dll","first.dll"};
   var optionStore=new ConfigStore(optionsPath);optionStore.Save(optionValues);
   var savedOptions=optionStore.Read();
   foreach(var pair in optionValues)Assert(optionsJson.Serialize(pair.Value)==optionsJson.Serialize(savedOptions[pair.Key]),"option roundtrip: "+pair.Key);
   Assert((int)savedOptions["LastVersionNotify"]==123&&(int)savedOptions["UnknownFutureField"]==42&&(string)savedOptions["GamePath"]=="unchanged","option metadata preserved");
   string validOptions=File.ReadAllText(optionsPath);
   Reject(()=>optionStore.Save(new System.Collections.Generic.Dictionary<string,object>{{"Fullscreen",true}}),"conflicting window modes rejected");
   Reject(()=>optionStore.Save(new System.Collections.Generic.Dictionary<string,object>{{"CustomResX",0}}),"invalid resolution rejected");
   Reject(()=>optionStore.Save(new System.Collections.Generic.Dictionary<string,object>{{"Priority",6}}),"invalid priority rejected");
   Reject(()=>optionStore.Save(new System.Collections.Generic.Dictionary<string,object>{{"AdditionalCommandLine","line\nbreak"}}),"multiline arguments rejected");
   Assert(File.ReadAllText(optionsPath)==validOptions,"invalid options do not change config");
   var modes=UnlockerOptions.Defaults();modes["CustomResX"]=1280;modes["CustomResY"]=720;
   for(int mode=0;mode<4;mode++){UnlockerOptions.SetWindowMode(modes,mode);Assert(UnlockerOptions.WindowMode(modes)==mode,"Canonical window mode roundtrip");string args=UnlockerOptions.GameArguments(modes);Assert(!args.Contains("-screen-width")&&!args.Contains("-screen-height"),"Game resolution not overridden by stale custom dimensions");Assert(args.Contains("-popupwindow")== (mode==1),"Only borderless window requests popup");Assert(args.Contains("-screen-fullscreen 1")== (mode>=2),"Fullscreen modes actually request fullscreen");Assert(args.Contains("exclusive")== (mode==3),"Exclusive mode isolated");}
   modes["UseCustomRes"]=true;Assert(UnlockerOptions.GameArguments(modes).Contains("-screen-width 1280 -screen-height 720"),"Explicit custom resolution is passed");
   string nativeDll=Path.Combine(dir,"native.dll");var pe=new byte[512];
   using(var stream=new MemoryStream(pe))using(var writer=new BinaryWriter(stream)) {
    writer.Write((ushort)0x5a4d);stream.Position=0x3c;writer.Write(64);stream.Position=64;writer.Write(0x4550);writer.Write((ushort)0x8664);
    stream.Position=84;writer.Write((ushort)240);writer.Write((ushort)0x2000);writer.Write((ushort)0x20b);stream.Position=196;writer.Write(16u);
   }
   File.WriteAllBytes(nativeDll,pe);UnlockerOptions.ValidateDll(nativeDll);
   pe[64+24+112+14*8]=1;File.WriteAllBytes(nativeDll,pe);Reject(()=>UnlockerOptions.ValidateDll(nativeDll),"managed DLL rejected");
   pe[64+24+112+14*8]=0;pe[68]=0x4c;pe[69]=0x01;File.WriteAllBytes(nativeDll,pe);Reject(()=>UnlockerOptions.ValidateDll(nativeDll),"x86 DLL rejected");
   File.WriteAllText(nativeDll,"invalid");Reject(()=>UnlockerOptions.ValidateDll(nativeDll),"invalid DLL rejected");
   string gameDir=Path.Combine(dir,"custom game");Directory.CreateDirectory(gameDir);
   string gameExe=Path.Combine(gameDir,"GenshinImpact.exe");File.WriteAllText(gameExe,"fixture");
   Assert(!Setup.ValidGame(gameExe),"reject executable without game data");
   Directory.CreateDirectory(Path.Combine(gameDir,"GenshinImpact_Data"));
   Assert(Setup.ValidGame(gameExe),"custom game install accepted");
   Assert(System.Linq.Enumerable.Contains(Setup.Candidates(dir),Path.Combine(dir,"games","Genshin Impact game","GenshinImpact.exe")),"standard install discovery");
   Assert((int)Setup.Defaults(gameExe)["FPSTarget"]==120,"first-run 120 FPS");
   using(var publicIcon=new System.Drawing.Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launcher.ico")))Assert(publicIcon.Width>0,"public icon readable");
   string setupDir=Path.Combine(dir,"setup");Directory.CreateDirectory(setupDir);
   string setupEngine=Path.Combine(setupDir,"unlockfps_nc.exe"),binding=Path.Combine(setupDir,"installation.json");
   var installed=Setup.SaveInstallation(setupEngine,setupDir,binding,gameExe);
   var setupJson=new JavaScriptSerializer();
   var setupConfig=setupJson.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(installed.Config));
   Assert((int)setupConfig["FPSTarget"]==120&&(string)setupConfig["GamePath"]==gameExe,"first-run config persisted");
   string prefs=Path.Combine(setupDir,"launcher-settings.json");
   Assert(!(bool)setupJson.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(prefs))["LaunchOnOpen"],"new users do not auto-launch");
   setupConfig["UnknownField"]=42;setupConfig["FPSTarget"]=90;File.WriteAllText(installed.Config,setupJson.Serialize(setupConfig));
   File.WriteAllText(prefs,"{\"LaunchOnOpen\":true}");
   Setup.SaveInstallation(setupEngine,setupDir,binding,gameExe);
   setupConfig=setupJson.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(installed.Config));
   Assert((int)setupConfig["UnknownField"]==42&&(int)setupConfig["FPSTarget"]==90,"import preserves FPS and unknown fields");
   Assert(File.ReadAllText(prefs)=="{\"LaunchOnOpen\":true}","existing launch preference preserved");
   string original="[general]\r\ngame_version=7.0.0\r\nchannel=1\r\nsub_channel=0\r\ncps=hoyoverse\r\nwpf_version=7.0.0.47194594\r\nplugin_sdk_version=2.46.0\r\nuapc=unchanged\r\n[other]\r\nsub_channel=99\r\n";
   string google="[general]\nchannel=1\nsub_channel=6\ncps=mihoyo\nwpf_version=old\nplugin_sdk_version=old\n";
   File.WriteAllText(Path.Combine(dir,"config.ini"),original);
   File.WriteAllText(Path.Combine(dir,"config - 원본.ini"),original);
   File.WriteAllText(Path.Combine(dir,"config - 구글용.ini"),google);
   var channel=new ChannelService(dir);
   Assert(channel.Current()=="원본","initial channel");
   Reject(()=>channel.Switch(true,()=>true),"busy channel rejected");
   Assert(File.ReadAllText(channel.ConfigPath)==original,"busy channel unchanged");
   channel.Switch(true,()=>false);
   string switched=File.ReadAllText(channel.ConfigPath);
   Assert(switched==original.Replace("sub_channel=0","sub_channel=6").Replace("cps=hoyoverse","cps=mihoyo"),"only channel fields changed; all versions preserved");
   Assert(File.ReadAllText(channel.ConfigPath+".launcher-backup")==original,"channel backup");
   Assert(channel.Current()=="Google Play","google detection");
   channel.Switch(false,()=>false);
   Assert(File.ReadAllText(channel.ConfigPath)==original,"channel roundtrip");
   Reject(()=>ChannelService.Merge(original,google+"sub_channel=7\n"),"duplicate rejected");
   Reject(()=>ChannelService.Merge("[general]\nchannel=1\n",google),"missing fields rejected");

   string exe=Path.Combine(dir,"unlockfps_nc.exe");
   string state=Path.Combine(dir,"version.json");
   string next=Path.Combine(dir,"new.bin");
   File.WriteAllBytes(exe,Encoding.UTF8.GetBytes(new string('a',2048)));
   File.WriteAllBytes(next,Encoding.UTF8.GetBytes(new string('b',2048)));
   string prior=UpdateService.Hash(exe),hash=UpdateService.Hash(next);
   var json=new JavaScriptSerializer();
   File.WriteAllText(state,json.Serialize(new{Version="v3.5.0",Hash=prior}));
   string releaseJson=json.Serialize(new{tag_name="v3.6.0",draft=false,prerelease=false,assets=new[]{new{name="unlockfps_nc.exe",digest="sha256:"+hash,size=2048,browser_download_url="https://github.com/34736384/genshin-fps-unlock/releases/download/v3.6.0/unlockfps_nc.exe"}}});
   var release=ReleaseInfo.Parse(releaseJson);
   string signedJson=releaseJson.Replace("unlockfps_nc.exe","unlockfps_nc_signed.exe");
   var signedRelease=ReleaseInfo.Parse(signedJson,"unlockfps_nc_signed.exe");
   Assert(signedRelease.Url.EndsWith("/unlockfps_nc_signed.exe")&&signedRelease.Hash==hash,"signed release keeps official asset and digest");
   Reject(()=>ReleaseInfo.Parse(releaseJson,"unlockfps_nc_signed.exe"),"signed selection never falls back to unsigned");
   Reject(()=>ReleaseInfo.Parse(signedJson.Replace("/unlockfps_nc_signed.exe","/unlockfps_nc.exe"),"unlockfps_nc_signed.exe"),"signed asset URL mismatch rejected");
   Reject(()=>ReleaseInfo.Parse(signedJson,"other.exe"),"unknown release variant rejected");
   string freshEngine=Path.Combine(dir,"fresh.exe");
   var freshUpdater=new UpdateService(freshEngine,Path.Combine(dir,"fresh-state.json"),()=>false);
   Reject(()=>freshUpdater.CheckRelease(release,true,(url,path,size)=>File.WriteAllText(path,"invalid")),"fresh corrupt install rejected");
   Assert(!File.Exists(freshEngine),"failed bootstrap leaves no engine");
   freshUpdater.CheckRelease(release,true,(url,path,size)=>File.Copy(next,path));
   Assert(UpdateService.Hash(freshEngine)==hash,"fresh verified install");
   Reject(()=>ReleaseInfo.Parse(releaseJson.Replace("sha256:","md5:")),"missing SHA256 rejected");
   Reject(()=>ReleaseInfo.Parse(releaseJson.Replace("github.com/34736384","evil.example/34736384")),"untrusted URL rejected");
   var updater=new UpdateService(exe,state,()=>false);
   bool downloaded=false;
   updater.CheckRelease(release,false,(url,path,size)=>{downloaded=true;});
   Assert(!downloaded&&UpdateService.Hash(exe)==prior,"check-only does not install");
   var blocked=new UpdateService(exe,state,()=>true);
   blocked.CheckRelease(release,true,(url,path,size)=>{downloaded=true;});
   Assert(!downloaded&&UpdateService.Hash(exe)==prior,"busy skips update");
   Reject(()=>updater.CheckRelease(release,true,(url,path,size)=>File.WriteAllText(path,"corrupt")),"corrupt download rejected");
   Assert(UpdateService.Hash(exe)==prior&&!File.Exists(exe+".download"),"corrupt cleanup retains engine");
   using(var locked=new FileStream(exe,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    Reject(()=>updater.CheckRelease(release,true,(url,path,size)=>File.Copy(next,path,true)),"locked executable cannot replace");
    Assert(UpdateService.Hash(exe)==prior,"locked engine preserved");
   }
   updater.CheckRelease(release,true,(url,path,size)=>File.Copy(next,path,true));
   Assert(UpdateService.Hash(exe)==hash&&UpdateService.Hash(exe+".previous")==prior,"verified update and backup");
   Assert(File.ReadAllText(channel.ConfigPath)==original,"update preserves game config");
   var older=ReleaseInfo.Parse(releaseJson.Replace("v3.6.0","v3.4.0").Replace(hash,prior));
   updater.CheckRelease(older,true,(url,path,size)=>{downloaded=true;});
   Assert(!downloaded&&UpdateService.Hash(exe)==hash,"downgrade rejected");
  } finally {
   if(Directory.Exists(dir))Directory.Delete(dir,true);
  }
 }
}
