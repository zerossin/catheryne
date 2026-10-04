using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Forms=System.Windows.Forms;

// Transport and lifetime only. Gameplay remains in the canonical tools and story host.
internal static class GameEnvironment {
 const string Prefix="Catheryne.GameSession.";
 internal static readonly TimeSpan ConnectionTimeout=TimeSpan.FromSeconds(60);
 internal static readonly TimeSpan WorkerTimeout=TimeSpan.FromSeconds(30);
 static readonly Encoding Utf8=new UTF8Encoding(false);
 static readonly string loadedImage=NativePaths.FileIdentity(Executable);
 static readonly System.Collections.Concurrent.ConcurrentDictionary<int,Tuple<long,string>> processImages=new System.Collections.Concurrent.ConcurrentDictionary<int,Tuple<long,string>>();
 static int workerSession=-1;
 internal static bool IsWorker {get{return workerSession>=0;}}
 // Activation is explicit until the complete background workflow is validated and exposed in settings.
 internal static bool Selected(string root){return CodexChat.S(AppPreferences.Read(root),"gameExecution")=="isolated";}
 internal static bool InSession(string root){
  if(IsWorker){if(WindowsChildSession.Current!=workerSession)throw new InvalidOperationException("게임 실행 세션이 변경되었습니다.");return true;}
  string inherited=Environment.GetEnvironmentVariable("CATHERYNE_GAME_SESSION");if(string.IsNullOrEmpty(inherited))return false;
  int child,parent;if(!int.TryParse(inherited,out child)||!int.TryParse(Environment.GetEnvironmentVariable("CATHERYNE_GAME_PARENT"),out parent))throw new InvalidOperationException("게임 실행 환경 정보가 올바르지 않습니다.");
  WindowsChildSession.RequireTarget(child,parent,WindowsChildSession.Current);
  var endpoint=StoryClient.Read(Endpoint(root,parent));
  if(Number(endpoint,"child_session")!=child||Number(endpoint,"parent_session")!=parent||!SameProcess(Number(endpoint,"host_pid"),Ticks(endpoint,"host_started"),parent,Image(endpoint,"host")))throw new InvalidOperationException("게임 실행 환경의 소유자가 변경되었습니다.");return true;
 }
 internal static bool Remote(string root){return Selected(root)&&!InSession(root);}
 internal static bool OwnsProcess(int pid){int session;return ProcessIdToSessionId(pid,out session)&&session==WindowsChildSession.Current;}
 internal static bool ProcessAlive(int pid,string ownerStarted){
  DateTime expected;if(!DateTime.TryParse(ownerStarted,null,System.Globalization.DateTimeStyles.RoundtripKind,out expected))return false;
  IntPtr process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)return false;
  try{long created,exited,kernel,user;return GetProcessTimes(process,out created,out exited,out kernel,out user)&&exited==0&&DateTime.FromFileTimeUtc(created).Ticks==expected.ToUniversalTime().Ticks;}finally{CloseHandle(process);}
 }
 internal static bool OwnsProcess(int pid,string started){return OwnsProcess(pid)&&ProcessAlive(pid,started);}
 internal static string Endpoint(string root,int parent){return Path.Combine(root,"game-environment","session-"+parent+".json");}
 static string Executable {get{return NativePaths.Resolve(Assembly.GetExecutingAssembly().Location);}}
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr process,out long created,out long exited,out long kernel,out long user);
 [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(int pid,out int session);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int length);
 internal static bool SameProcess(int id,long started,int session,string expectedImage=null){
  IntPtr process=OpenProcess(0x1000,false,id);if(process==IntPtr.Zero)return false;
  try{
   long created,exited,kernel,user;int actual;
   if(!GetProcessTimes(process,out created,out exited,out kernel,out user)||exited!=0||!ProcessIdToSessionId(id,out actual)||actual!=session)return false;
   long stamp=DateTime.FromFileTimeUtc(created).Ticks;if(started!=0&&stamp!=started)return false;
   Tuple<long,string> image;
   if(!processImages.TryGetValue(id,out image)||image.Item1!=stamp){
    int length=32768;var path=new StringBuilder(length);if(!QueryFullProcessImageName(process,0,path,ref length))return false;
    image=Tuple.Create(stamp,NativePaths.FileIdentity(path.ToString()));
    if(processImages.Count>=128)processImages.Clear();processImages[id]=image;
   }
   // Bind the loaded image once. Renaming an update's frozen file does not change the running process.
   if(expectedImage!=null)return image.Item2==expectedImage;
   return image.Item2==loadedImage||image.Item2==NativePaths.FileIdentity(Executable);
  }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}finally{CloseHandle(process);}
 }
 static string Image(Dictionary<string,object> endpoint,string owner){string image=CodexChat.S(endpoint,owner+"_image");return string.IsNullOrEmpty(image)?null:image;}
 static int Number(Dictionary<string,object> value,string key){return Convert.ToInt32(value[key]);}
 static long Ticks(Dictionary<string,object> value,string key){return Convert.ToInt64(value[key]);}
 internal static Dictionary<string,object> Status(string root){
  string file=Endpoint(root,WindowsChildSession.Current);if(!File.Exists(file))return new Dictionary<string,object>{{"state","stopped"}};
  var endpoint=StoryClient.Read(file);
  if(CodexChat.S(endpoint,"state")=="failed"){
   // Windows reuses session numbers. A previous logon's failure must not block a new logon.
   long logon=WindowsChildSession.LogonTime(WindowsChildSession.Current);
   if(logon>0&&endpoint.ContainsKey("host_started")&&Ticks(endpoint,"host_started")>0&&Ticks(endpoint,"host_started")<logon)return new Dictionary<string,object>{{"state","stopped"}};
   if(CodexChat.S(endpoint,"failure_kind")=="authentication")endpoint["error"]=ChildSessionView.DisconnectMessage(2055,null);
   return endpoint;
  }
  if(!endpoint.ContainsKey("host_pid")||!SameProcess(Number(endpoint,"host_pid"),Ticks(endpoint,"host_started"),WindowsChildSession.Current,Image(endpoint,"host")))return new Dictionary<string,object>{{"state","stopped"}};
  // During native reconnection retain the same owner/receipt, but do not dispatch game input until Windows is connected.
  if(CodexChat.S(endpoint,"state")=="ready"&&!WindowsChildSession.Active(Number(endpoint,"child_session"))){endpoint["state"]="starting";endpoint["connection_phase"]="reconnecting";}
  return endpoint;
 }
 internal static bool StartupMatches(Dictionary<string,object> endpoint,int child,int parent,string pipe,int host,long started){
  return endpoint.ContainsKey("host_pid")&&endpoint.ContainsKey("host_started")&&endpoint.ContainsKey("child_session")&&endpoint.ContainsKey("parent_session")&&Number(endpoint,"host_pid")==host&&Ticks(endpoint,"host_started")==started&&Number(endpoint,"child_session")==child&&Number(endpoint,"parent_session")==parent&&CodexChat.S(endpoint,"pipe")==pipe;
 }
 static bool RetainedSession(Dictionary<string,object> endpoint){
  int parent=WindowsChildSession.Current;
  return endpoint.ContainsKey("child_session")&&Number(endpoint,"parent_session")==parent&&SameProcess(Number(endpoint,"host_pid"),Ticks(endpoint,"host_started"),parent,Image(endpoint,"host"))&&Number(endpoint,"child_session")>=0&&Number(endpoint,"child_session")==WindowsChildSession.Child&&Number(endpoint,"child_session")!=parent&&WindowsChildSession.LoggedOn(Number(endpoint,"child_session"));
 }
 internal static T WithLifecycleLock<T>(Func<T> action,Action check=null){
  using(var mutex=PrivateIpc.Mutex(AppUpdateProtocol.EnvironmentLifecycleMutex)){
   bool owned=false;var wait=Stopwatch.StartNew();
   try{
    while(!owned){if(check!=null)check();try{owned=mutex.WaitOne(100);}catch(AbandonedMutexException){owned=true;}if(!owned&&wait.Elapsed.TotalSeconds>=15)throw new IOException(Locale.T("게임 실행 환경의 다른 요청을 기다리고 있습니다."));}
    if(check!=null)check();return action();
   }finally{if(owned)mutex.ReleaseMutex();}
  }
 }
 internal static void RequireConnectionRequest(Dictionary<string,object> endpoint,bool interactiveAuthentication,bool retained){
  // Only an explicit connection can retry a failed Windows logon. Gameplay never opens a credential dialog.
  if(!interactiveAuthentication&&!retained&&((CodexChat.S(endpoint,"state")=="failed"&&CodexChat.S(endpoint,"failure_kind")!="connection")||WaitingForSignIn(endpoint)))throw new InvalidOperationException(Locale.T("설정의 자동화 실행에서 연결해 주세요."));
 }
 internal static void CheckHostStartup(Process process){
  if(process==null||process.HasExited)throw new IOException(Locale.T("게임 실행 환경을 시작하지 못했습니다. 설정의 자동화 실행에서 연결을 확인해 주세요."));
 }
 static Dictionary<string,object> Ensure(string root,Action check=null,bool interactiveAuthentication=false){
  if(check!=null)check();root=NativePaths.Resolve(root);int parent=WindowsChildSession.Current;
  Process launchedHost=null;
  try{
   var current=WithLifecycleLock(()=>{
    if(AppUpdateProtocol.Installing(Path.GetDirectoryName(Executable)))throw new IOException(Locale.T("앱 업데이트가 진행 중입니다."));
    var state=Status(root);bool retained=CodexChat.S(state,"state")=="failed"&&RetainedSession(state);
    RequireConnectionRequest(state,interactiveAuthentication,retained);
    if(retained)PostView(state,4);
    else if(new[]{"stopped","failed"}.Contains(CodexChat.S(state,"state"))){
    if(CodexChat.S(state,"state")=="failed")Stop(root);
    if(WindowsChildSession.Child>=0)throw new InvalidOperationException("다른 게임 실행 환경이 열려 있습니다. 해당 환경을 종료한 뒤 다시 시도해 주세요.");
    if(File.Exists(Endpoint(root,parent)))File.Delete(Endpoint(root,parent));
    launchedHost=Process.Start(new ProcessStartInfo(Executable,"--game-environment-host "+StoryClient.Quote(root)+" "+parent+" "+(interactiveAuthentication?"interactive":"automatic")){UseShellExecute=true,Verb=AppRuntime.Elevated()?"":"runas",WindowStyle=ProcessWindowStyle.Hidden});
    CheckHostStartup(launchedHost);
    // Publish ownership before releasing the gate; simultaneous callers join this host instead of deleting its receipt.
    var published=Stopwatch.StartNew();
    while(true){state=Status(root);if(state.ContainsKey("host_pid")&&Number(state,"host_pid")==launchedHost.Id)break;CheckHostStartup(launchedHost);if(published.Elapsed.TotalSeconds>=15)throw new IOException(Locale.T("게임 실행 환경을 시작하지 못했습니다. 설정의 자동화 실행에서 연결을 확인해 주세요."));Thread.Sleep(100);}
    }
    return state;
   },check);
   if(CodexChat.S(current,"state")=="ready")return current;
   return WaitForConnection(()=>Status(root),check,()=>{if(launchedHost!=null)CheckHostStartup(launchedHost);},ConnectionTimeout,interactiveAuthentication);
  }finally{if(launchedHost!=null)launchedHost.Dispose();}
 }
 internal static bool WaitingForSignIn(Dictionary<string,object> endpoint){return CodexChat.S(endpoint,"state")=="starting"&&CodexChat.S(endpoint,"connection_phase")=="authenticating";}
 internal static Dictionary<string,object> WaitForConnection(Func<Dictionary<string,object>> read,Action check,Action inspect,TimeSpan timeout,bool interactiveAuthentication){
  var watch=Stopwatch.StartNew();bool observed=false,waiting=false;string phase=null;
  while(true){
   if(check!=null)check();var current=read();string state=CodexChat.S(current,"state");
   if(state=="ready")return current;if(state=="failed")throw new InvalidOperationException(CodexChat.S(current,"error"));
   if(state=="starting")observed=true;else if(observed&&state=="stopped")throw new OperationCanceledException("게임 실행 환경 연결이 종료되었습니다.");
   RequireConnectionRequest(current,interactiveAuthentication,false);
   if(inspect!=null)inspect();
   bool signIn=interactiveAuthentication&&WaitingForSignIn(current);
   // User-owned authentication has no automatic deadline. Active connection/worker startup remains bounded.
   string next=CodexChat.S(current,"connection_phase");if(signIn||waiting||(phase!=null&&phase!=next))watch.Restart();waiting=signIn;phase=next;
   if(!waiting&&watch.Elapsed>=timeout)throw new IOException("게임 실행 환경 연결이 지연되고 있습니다. 실행 환경에서 로그인 상태를 확인해 주세요.");
   Thread.Sleep(200);
  }
 }
 static Dictionary<string,object> Ready(string root,bool start,Action check=null,bool interactiveAuthentication=false){
  var endpoint=start?Ensure(root,check,interactiveAuthentication):Status(root);
  if(CodexChat.S(endpoint,"state")!="ready")throw new InvalidOperationException("게임 실행 환경이 연결되지 않았습니다.");
  WindowsChildSession.RequireTarget(Number(endpoint,"child_session"),Number(endpoint,"parent_session"),WindowsChildSession.Child);return endpoint;
 }
 internal static object Invoke(string root,string command,Dictionary<string,object> parameters,bool start=false,Action check=null,bool interactiveAuthentication=false,TimeSpan? responseTimeout=null){
  if(InSession(root))throw new InvalidOperationException("분리 실행 환경을 중복 호출할 수 없습니다.");
  var endpoint=Ready(root,start,check,interactiveAuthentication);
  int child=Number(endpoint,"child_session"),parent=Number(endpoint,"parent_session");
  string pipe=CodexChat.S(endpoint,"pipe");ValidatePipe(pipe);
  using(var client=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous)){
   client.Connect(5000);uint owner;if(!GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(),out owner)||!SameProcess(checked((int)owner),Ticks(endpoint,"worker_started"),child,Image(endpoint,"worker")))throw new IOException("게임 실행 환경의 연결 주체가 변경되었습니다.");
   Write(client,new Dictionary<string,object>{{"command",command},{"parameters",parameters??new Dictionary<string,object>()}});
   var pending=Task.Run(()=>Read(client));if(!pending.Wait(responseTimeout??TimeSpan.FromMinutes(11)))throw new IOException("게임 실행 환경의 응답 시간이 초과되었습니다.");
   var response=pending.Result;if(!Equals(response["ok"],true))throw new InvalidOperationException(CodexChat.S(response,"error"));return response.ContainsKey("result")?response["result"]:null;
  }
 }
 static void ValidatePipe(string pipe){Guid id;if(string.IsNullOrEmpty(pipe)||!pipe.StartsWith(Prefix,StringComparison.Ordinal)||!Guid.TryParseExact(pipe.Substring(Prefix.Length),"N",out id))throw new InvalidDataException("게임 실행 환경 연결 주소가 올바르지 않습니다.");}
 internal static void Write(Stream stream,object value){byte[] data=Utf8.GetBytes(CatheryneTools.Json().Serialize(value));if(data.Length>16000000)throw new InvalidDataException("실행 요청이 너무 큽니다.");var writer=new BinaryWriter(stream,Utf8,true);writer.Write(data.Length);writer.Write(data);writer.Flush();}
 internal static Dictionary<string,object> Read(Stream stream){var reader=new BinaryReader(stream,Utf8,true);int length=reader.ReadInt32();if(length<2||length>16000000)throw new InvalidDataException("실행 요청 크기가 올바르지 않습니다.");byte[] data=reader.ReadBytes(length);if(data.Length!=length)throw new EndOfStreamException();return CodexChat.Map(CatheryneTools.Json().DeserializeObject(Utf8.GetString(data)));}
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint id);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool GetNamedPipeClientProcessId(IntPtr pipe,out uint id);
 [DllImport("wtsapi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool WTSLogoffSession(IntPtr server,int session,[MarshalAs(UnmanagedType.Bool)] bool wait);
 internal static int Worker(string root,int child,int parent,string pipe,int host,long hostStarted){
  WindowsChildSession.RequireTarget(child,parent,WindowsChildSession.Current);
  using(var mutex=PrivateIpc.Mutex("Local\\Catheryne.GameEnvironment.Worker")){
   bool owned=false;try{try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}if(!owned)return 0;return RunWorker(root,child,parent,pipe,host,hostStarted);}finally{if(owned)mutex.ReleaseMutex();}
  }
 }
 static int RunWorker(string root,int child,int parent,string pipe,int host,long hostStarted){
  root=Path.GetFullPath(root);ValidatePipe(pipe);WindowsChildSession.RequireTarget(child,parent,WindowsChildSession.Current);
  var endpoint=StoryClient.Read(Endpoint(root,parent));
  if(!SameProcess(host,hostStarted,parent,Image(endpoint,"host")))throw new InvalidOperationException("게임 실행 환경 소유자가 일치하지 않습니다.");
  Environment.SetEnvironmentVariable("CATHERYNE_TOOL_DATA",root);Environment.SetEnvironmentVariable("CATHERYNE_GAME_SESSION",child.ToString());Environment.SetEnvironmentVariable("CATHERYNE_GAME_PARENT",parent.ToString());workerSession=child;
  using(var me=Process.GetCurrentProcess()){
   if(CodexChat.S(endpoint,"state")!="starting"||!StartupMatches(endpoint,child,parent,pipe,host,hostStarted))throw new InvalidOperationException("게임 실행 환경의 시작 요청이 변경되었습니다.");
   endpoint["child_session"]=child;endpoint["worker_pid"]=me.Id;endpoint["worker_started"]=me.StartTime.ToUniversalTime().Ticks;endpoint["worker_image"]=loadedImage;
   var stopping=new CancellationTokenSource();
   Task.Run(()=>{while(!stopping.Token.WaitHandle.WaitOne(500)){if(SameProcess(host,hostStarted,parent,Image(endpoint,"host")))continue;try{GameEnvironmentOperations.Stop(root);}finally{WTSLogoffSession(IntPtr.Zero,child,false);Environment.Exit(1);}}});
   var slots=new SemaphoreSlim(8);bool acknowledged=false;
   try{
    while(!stopping.IsCancellationRequested){
     slots.Wait(stopping.Token);NamedPipeServerStream server=null;
     try{
      server=PrivateIpc.Server(pipe,8,65536);
      if(!acknowledged){endpoint["state"]="ready";AtomicFile.Write(Endpoint(root,parent),CatheryneTools.Json().Serialize(endpoint));acknowledged=true;}
      if(!PrivateIpc.Accept(server,stopping.Token)){server.Dispose();slots.Release();break;}
      var connected=server;
      Task.Run(()=>{bool shutdown=false;try{
       try{
        uint caller;if(!GetNamedPipeClientProcessId(connected.SafePipeHandle.DangerousGetHandle(),out caller)||!SameProcess(checked((int)caller),0,parent))throw new IOException("게임 실행 환경 요청 주체가 일치하지 않습니다.");
        var request=Read(connected);string command=CodexChat.S(request,"command");var parameters=request.ContainsKey("parameters")?CodexChat.Map(request["parameters"]):new Dictionary<string,object>();object result;
        if(command=="status")result=new{state="ready",parent_session=parent,child_session=child,worker_pid=me.Id,elevated=AppRuntime.Elevated(),foreground_owner=ForegroundOwner()};
        else if(command=="shutdown"){GameEnvironmentOperations.Stop(root);result=new{stopped=true};shutdown=true;}
        else result=GameEnvironmentOperations.Run(root,command,parameters);
        Write(connected,new{ok=true,result=result});
       }catch(Exception error){try{Write(connected,new{ok=false,error=error.GetBaseException().Message});}catch(IOException){}}
      }finally{connected.Dispose();slots.Release();if(shutdown)stopping.Cancel();}});
      server=null; // The request now owns the pipe and its capacity slot.
     }catch{if(server!=null)server.Dispose();slots.Release();throw;}
    }
    return 0;
   }finally{stopping.Cancel();}
  }
 }
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 static object ForegroundOwner(){uint id;GetWindowThreadProcessId(GetForegroundWindow(),out id);if(id==0)return null;using(var process=Process.GetProcessById(checked((int)id)))return new{pid=process.Id,session=process.SessionId,name=process.ProcessName};}
 internal static int Host(string root,int parent,bool interactiveAuthentication){
  if(parent!=WindowsChildSession.Current)throw new InvalidOperationException("게임 실행 환경의 부모 세션이 일치하지 않습니다.");
  root=Path.GetFullPath(root);Directory.CreateDirectory(Path.GetDirectoryName(Endpoint(root,parent)));
  using(var mutex=PrivateIpc.Mutex(AppUpdateProtocol.EnvironmentMutex)){
   bool locked=false;try{try{locked=mutex.WaitOne(0);}catch(AbandonedMutexException){locked=true;}if(!locked)return 0;
    if(WindowsChildSession.Child>=0)throw new InvalidOperationException("다른 게임 실행 환경이 열려 있습니다.");
    WindowsChildSession.Enable();
    using(var form=new EnvironmentWindow(root,parent,interactiveAuthentication)){
     Forms.Application.Run(form);return form.StartupFailed?1:0;
    }
   }finally{if(locked)mutex.ReleaseMutex();}
  }
 }
 internal static void Stop(string root){
  WithLifecycleLock(()=>{StopConnected(root);return 0;});
 }
 static void StopConnected(string root){
  var endpoint=Status(root);string state=CodexChat.S(endpoint,"state");if(state=="stopped")return;
  int host=Number(endpoint,"host_pid");long started=Ticks(endpoint,"host_started");int parent=WindowsChildSession.Current;int ownedChild=WindowsChildSession.Child;
  if(!SameProcess(host,started,parent,Image(endpoint,"host"))){if(ownedChild<0)AtomicFile.Write(Endpoint(root,parent),CatheryneTools.Json().Serialize(new{state="stopped"}));return;}
  // A nonresponsive worker must not prevent an explicit shutdown of the owned environment.
  if(state=="ready")try{Invoke(root,"shutdown",new Dictionary<string,object>(),false,responseTimeout:TimeSpan.FromSeconds(5));}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.RuntimeFailure,error,root);}
  if(SameProcess(host,started,parent,Image(endpoint,"host")))PostView(endpoint,3);
  var wait=Stopwatch.StartNew();while((SameProcess(host,started,parent,Image(endpoint,"host"))||(ownedChild>=0&&WindowsChildSession.Child==ownedChild))&&wait.Elapsed.TotalSeconds<15)Thread.Sleep(100);
  if(SameProcess(host,started,parent,Image(endpoint,"host"))||(ownedChild>=0&&WindowsChildSession.Child==ownedChild))throw new IOException("게임 실행 환경 종료를 기다리고 있습니다.");
 }
 internal static void HideView(string root){var endpoint=Status(root);if(CodexChat.S(endpoint,"state")=="ready")PostView(endpoint,0);}
 static void PostView(Dictionary<string,object> endpoint,int action){
  string pipe=CodexChat.S(endpoint,"host_pipe");ValidatePipe(pipe);
  using(var client=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous)){
   client.Connect(5000);uint owner;if(!GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(),out owner)||!SameProcess((int)owner,Ticks(endpoint,"host_started"),WindowsChildSession.Current,Image(endpoint,"host")))throw new IOException("실행 환경의 화면 연결이 변경되었습니다.");
   Write(client,new{action=action});var pending=Task.Run(()=>Read(client));if(!pending.Wait(5000))throw new IOException("게임 실행 환경의 화면 응답을 기다리고 있습니다.");var reply=pending.Result;if(!Equals(reply["ok"],true))throw new InvalidOperationException(CodexChat.S(reply,"error"));
  }
 }
 internal static void ShowView(string root,bool manual){
  var endpoint=Ready(root,false);
  if(manual)Invoke(root,"environment.manual",new Dictionary<string,object>(),false);
  PostView(endpoint,manual?2:1);
 }
 internal static int Command(string root,string command){
  if(!new[]{"start","status","shutdown"}.Contains(command))throw new ArgumentException("지원하지 않는 실행 환경 명령입니다.");
  object result;if(command=="shutdown"){Stop(root);result=Status(root);}else result=command=="status"?(object)Status(root):Invoke(root,"status",new Dictionary<string,object>(),true);
  using(var output=new StreamWriter(Console.OpenStandardOutput(),Utf8))output.Write(CatheryneTools.Json().Serialize(result));return 0;
 }
 sealed class EnvironmentWindow:Forms.Form {
  readonly string root;readonly int parent;readonly TimeSpan connectionTimeout;readonly bool interactiveAuthentication;readonly ChildSessionView view;readonly Forms.Timer timer;readonly Stopwatch watch=Stopwatch.StartNew();readonly Dictionary<string,object> endpoint;readonly CancellationTokenSource controlStop=new CancellationTokenSource();readonly string controlPipe;bool launched,ready,closing,resourcesReleased,prompting,recovered,recoveringConnection;Task workerStart;int child=-1;
  internal bool StartupFailed {get{return CodexChat.S(endpoint,"state")=="failed";}}
  protected override bool ShowWithoutActivation {get{return true;}}
  internal EnvironmentWindow(string root,int parent,bool interactiveAuthentication){
   this.root=root;this.parent=parent;this.interactiveAuthentication=interactiveAuthentication;connectionTimeout=TimeSpan.FromSeconds(30);Text="Catheryne";ShowInTaskbar=false;ClientSize=new System.Drawing.Size(1280,720);StartPosition=Forms.FormStartPosition.Manual;Location=new System.Drawing.Point(100,100);
   view=new ChildSessionView();((ISupportInitialize)view).BeginInit();Controls.Add(view);((ISupportInitialize)view).EndInit();
   using(var me=Process.GetCurrentProcess())endpoint=new Dictionary<string,object>{{"state","starting"},{"connection_phase","connecting"},{"parent_session",parent},{"host_pid",me.Id},{"host_started",me.StartTime.ToUniversalTime().Ticks},{"host_image",loadedImage},{"host_pipe",Prefix+Guid.NewGuid().ToString("N")},{"pipe",Prefix+Guid.NewGuid().ToString("N")}};
   view.Trace=Trace;
   controlPipe=CodexChat.S(endpoint,"host_pipe");AtomicFile.Write(Endpoint(root,parent),CatheryneTools.Json().Serialize(endpoint));
   IntPtr createdHandle=Handle;Task.Run(()=>ControlLoop());
   timer=new Forms.Timer{Interval=250};timer.Tick+=(s,e)=>Poll();Shown+=(s,e)=>{try{view.Connect(DisplayPresets.AiWidth,DisplayPresets.AiHeight);Hide();timer.Start();}catch(Exception error){Fail(error);}};
   FormClosing+=(s,e)=>{if(!closing){e.Cancel=true;Hide();}};
  }
  void Trace(string phase,int? code=null){try{using(var db=new LocalDataService(root))db.Observe("default","game-environment",new{phase=phase,code=code,parent=parent,child=child,host_pid=Number(endpoint,"host_pid"),host_started=Ticks(endpoint,"host_started"),interactive_authentication=prompting,elapsed_ms=watch.ElapsedMilliseconds});}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.RuntimeFailure,error,root);}}
  void ControlLoop(){
   while(!controlStop.IsCancellationRequested){
    try{using(var server=PrivateIpc.Server(controlPipe,1,4096)){
     if(!PrivateIpc.Accept(server,controlStop.Token))return;
     try{uint caller;if(!GetNamedPipeClientProcessId(server.SafePipeHandle.DangerousGetHandle(),out caller)||!SameProcess((int)caller,0,parent))throw new IOException("게임 실행 환경 요청 주체가 일치하지 않습니다.");
      int action=Number(Read(server),"action");if(action<0||action>4)throw new ArgumentException("Invalid view action");
      if(action!=3)Invoke((Action)(()=>{if(action==4)RetryWorker();else if(action==0){view.Enabled=false;Hide();}else{view.Enabled=action==2;ShowInTaskbar=true;Show();WindowState=Forms.FormWindowState.Normal;Activate();view.Focus();}}));
      Write(server,new{ok=true});if(action==3)BeginInvoke((Action)(()=>{endpoint["state"]="stopped";AtomicFile.Write(Endpoint(root,parent),CatheryneTools.Json().Serialize(endpoint));closing=true;Close();}));
     }catch(Exception error){Write(server,new{ok=false,error=error.GetBaseException().Message});}
    }}catch(Exception error){if(!controlStop.IsCancellationRequested)AppDiagnostics.Record(DiagnosticEvent.RuntimeFailure,error,root);}
   }
  }
  void Poll(){try{
   if(view.Failure!=null){
    if(interactiveAuthentication&&!prompting&&!view.LoggedIn&&view.FailureKind=="authentication"){
     if(view.Connection!=0){if(watch.Elapsed<connectionTimeout)return;throw new IOException(view.Failure);}
     // Try Windows' existing authentication first. Only the user's Connect action may request a dialog once.
     prompting=true;endpoint["connection_phase"]="authenticating";AtomicFile.Write(Endpoint(root,parent),CatheryneTools.Json().Serialize(endpoint));watch.Restart();Trace("authentication-required");view.Connect(DisplayPresets.AiWidth,DisplayPresets.AiHeight,true);return;
    }
    throw new IOException(view.Failure);
   }
   if(view.Reconnecting||recoveringConnection){
    if(!recoveringConnection){recoveringConnection=true;watch.Restart();}
    int actual=WindowsChildSession.Child;
    if(view.Reconnecting||!view.LoggedIn||actual<0||!WindowsChildSession.LoggedOn(actual)){
     if(watch.Elapsed>connectionTimeout)throw new IOException("게임 실행 환경 연결이 끊어졌습니다. 사용자 화면에서는 조작하지 않습니다.");
     return;
    }
    if(child>=0&&child!=actual)throw new IOException("게임 실행 환경의 시작 요청이 변경되었습니다.");
    recoveringConnection=false;watch.Restart();
   }
   // The WTS token appears before the interactive logon has finished. Wait for the client completion event too.
   if(!StartupFailed&&!launched&&view.LoggedIn&&(child=WindowsChildSession.Child)>=0&&WindowsChildSession.LoggedOn(child)){
    WindowsChildSession.RequireTarget(child,parent,child);launched=true;watch.Restart();
    endpoint["child_session"]=child;endpoint["connection_phase"]="starting-worker";AtomicFile.Write(Endpoint(root,parent),CatheryneTools.Json().Serialize(endpoint));
    string pipe=CodexChat.S(endpoint,"pipe");int host=Number(endpoint,"host_pid");long started=Ticks(endpoint,"host_started");
    workerStart=Task.Run(()=>WindowsChildSession.StartWorker(child,parent,root,pipe,host,started,controlStop.Token));
   }
   if(workerStart!=null&&workerStart.IsFaulted)WorkerFailed(workerStart.Exception.Flatten().InnerExceptions[0].GetBaseException());
   var status=Status(root);if(CodexChat.S(status,"state")=="ready"){
    if(!ready){ready=true;foreach(var item in status)endpoint[item.Key]=item.Value;view.Enabled=false;Hide();}
    if(!SameProcess(Number(status,"worker_pid"),Ticks(status,"worker_started"),child,Image(status,"worker"))){WorkerFailed(new IOException("게임 실행기를 시작하지 못했습니다."));if(!recovered){recovered=true;RetryWorker(true);}}
   }else if(!prompting&&!launched&&!StartupFailed&&watch.Elapsed>connectionTimeout)throw new IOException(view.LoginError??"게임 실행 환경 로그인 또는 연결을 확인하지 못했습니다.");
   if((ready||StartupFailed)&&(WindowsChildSession.Child!=child||!WindowsChildSession.Active(child)))throw new IOException("게임 실행 환경 연결이 끊어졌습니다. 사용자 화면에서는 조작하지 않습니다.");
  }catch(Exception error){Fail(error);}}
  void WorkerFailed(Exception error){
   // A worker failure does not discard a still-connected Windows logon.
   AppDiagnostics.Record(DiagnosticEvent.RuntimeFailure,error,root);Trace("worker-failed");workerStart=null;ready=false;endpoint["state"]="failed";endpoint["failure_kind"]="worker";endpoint["connection_phase"]="worker-failed";endpoint["error"]=error.GetBaseException().Message;
   AtomicFile.Write(Endpoint(root,parent),CatheryneTools.Json().Serialize(endpoint));view.Enabled=false;Hide();
  }
  void RetryWorker(bool automatic=false){
   if(!StartupFailed)return;if(!RetainedSession(endpoint))throw new IOException("게임 실행 환경 연결이 종료되었습니다.");
   if(!automatic)recovered=false;Trace("worker-recover");launched=false;ready=false;endpoint["state"]="starting";endpoint["connection_phase"]="starting-worker";endpoint.Remove("error");endpoint.Remove("failure_kind");endpoint.Remove("worker_pid");endpoint.Remove("worker_started");endpoint.Remove("worker_image");endpoint["pipe"]=Prefix+Guid.NewGuid().ToString("N");
   AtomicFile.Write(Endpoint(root,parent),CatheryneTools.Json().Serialize(endpoint));watch.Restart();
  }
  void Fail(Exception error){var cause=error.GetBaseException();AppDiagnostics.Record(DiagnosticEvent.RuntimeFailure,cause,root);Trace("connection-failed",cause.HResult);endpoint["state"]="failed";endpoint["failure_kind"]=view.FailureKind??"connection";endpoint["error"]=cause.Message;AtomicFile.Write(Endpoint(root,parent),CatheryneTools.Json().Serialize(endpoint));closing=true;Close();}
  protected override void Dispose(bool disposing){
   if(disposing&&!resourcesReleased){
    resourcesReleased=true;controlStop.Cancel();timer.Stop();timer.Dispose();
    // Let the canceled startup remove its owned one-time task before exiting.
    if(workerStart!=null)try{if(!workerStart.Wait(5000))AppDiagnostics.Record(DiagnosticEvent.RuntimeFailure,new IOException("Worker startup cleanup timed out"),root);}catch(AggregateException){}
    try{view.Disconnect();}finally{if(child>=0&&WindowsChildSession.Child==child&&!WTSLogoffSession(IntPtr.Zero,child,false))AppDiagnostics.Record(DiagnosticEvent.RuntimeFailure,new Win32Exception(Marshal.GetLastWin32Error()),root);view.Dispose();}
   }
   base.Dispose(disposing);
  }
 }
}
