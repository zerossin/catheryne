using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Forms=System.Windows.Forms;

// Documented Windows child-session APIs. No RDP patches, credentials or input injection.
internal static class WindowsChildSession {
 [DllImport("wtsapi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool WTSEnableChildSessions([MarshalAs(UnmanagedType.Bool)] bool enabled);
 [DllImport("wtsapi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool WTSIsChildSessionsEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
 [DllImport("wtsapi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool WTSGetChildSessionId(out uint session);
 [DllImport("wtsapi32.dll",SetLastError=true,CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool WTSQuerySessionInformation(IntPtr server,int session,int info,out IntPtr buffer,out int bytes);
 [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr buffer);
 internal static bool Active(int session){IntPtr buffer;int bytes;if(!WTSQuerySessionInformation(IntPtr.Zero,session,8,out buffer,out bytes))throw new Win32Exception(Marshal.GetLastWin32Error());try{return bytes>=4&&Marshal.ReadInt32(buffer)==0;}finally{WTSFreeMemory(buffer);}}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct SessionInfo {
  internal int State,Session;internal uint IncomingBytes,OutgoingBytes,IncomingFrames,OutgoingFrames,IncomingCompressedBytes,OutgoingCompressedBytes;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] internal string Station;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=17)] internal string Domain;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=21)] internal string User;
  internal long Connect,Disconnect,LastInput,Logon,Current;
 }
 internal static bool HasLogon(int session,SessionInfo info){return info.Session==session&&info.State==0&&info.Logon>0&&!string.IsNullOrWhiteSpace(info.User);}
 static SessionInfo Information(int session){
  IntPtr buffer;int bytes;if(!WTSQuerySessionInformation(IntPtr.Zero,session,24,out buffer,out bytes))throw new Win32Exception(Marshal.GetLastWin32Error());
  try{if(bytes<Marshal.SizeOf(typeof(SessionInfo)))throw new InvalidDataException("Invalid Windows session information");return (SessionInfo)Marshal.PtrToStructure(buffer,typeof(SessionInfo));}finally{WTSFreeMemory(buffer);}
 }
 internal static bool LoggedOn(int session){return HasLogon(session,Information(session));}
 internal static long LogonTime(int session){var info=Information(session);return HasLogon(session,info)?DateTime.FromFileTimeUtc(info.Logon).Ticks:0;}
 internal static int Current {get{using(var process=Process.GetCurrentProcess())return process.SessionId;}}
 internal static int Child {get{uint id;if(!WTSGetChildSessionId(out id)){int error=Marshal.GetLastWin32Error();if(error==1168)return -1;throw new Win32Exception(error);}return id==uint.MaxValue?-1:checked((int)id);}}
 internal static void Enable(){bool enabled;if(!WTSIsChildSessionsEnabled(out enabled))throw new Win32Exception(Marshal.GetLastWin32Error());if(!enabled&&!WTSEnableChildSessions(true))throw new Win32Exception(Marshal.GetLastWin32Error());if(!WTSIsChildSessionsEnabled(out enabled))throw new Win32Exception(Marshal.GetLastWin32Error());if(!enabled)throw new InvalidOperationException(Locale.T("Windows 분리 실행 환경을 활성화하지 못했습니다."));}
 internal static void RequireTarget(int child,int parent,int actual){if(child<0||parent<0||child==parent||actual!=child)throw new InvalidOperationException("게임 실행 환경이 일치하지 않습니다. 사용자 화면에서는 조작하지 않습니다.");}
 internal static object Get(object target,string name){return target.GetType().InvokeMember(name,BindingFlags.GetProperty,null,target,null);}
 internal static void Set(object target,string name,object value){target.GetType().InvokeMember(name,BindingFlags.SetProperty,null,target,new[]{value});}
 internal static object Call(object target,string name,params object[] args){return target.GetType().InvokeMember(name,BindingFlags.InvokeMethod,null,target,args);}
 static void Release(object target){if(target!=null&&Marshal.IsComObject(target))Marshal.ReleaseComObject(target);}
 internal static void WaitForWorker(Func<string> state,Action inspect,CancellationToken cancel,TimeSpan timeout){
  var wait=Stopwatch.StartNew();
  while(wait.Elapsed<timeout){
   cancel.ThrowIfCancellationRequested();string value=state();if(value=="ready")return;
   if(value!="starting")throw new OperationCanceledException("게임 실행 환경 연결이 종료되었습니다.");
   if(inspect!=null)inspect();cancel.WaitHandle.WaitOne(100);
  }
  throw new IOException("게임 실행기를 시작하지 못했습니다.");
 }
 internal static bool RefreshWorkerTask(Action refresh,Func<int> lastResult){
  try{refresh();return true;}catch(Exception error){
   var cause=error.GetBaseException();
   if(!(cause is COMException)||cause.HResult!=unchecked((int)0x8004130B))throw;
   int result=lastResult();
   // RunEx can return while its interactive instance is still queued for logon.
   if(result==0x41303||result==0x41325||result==0x41301)return false;
   throw new IOException(Locale.T("게임 실행기를 시작하지 못했습니다."),cause);
  }
 }
 internal static void StartWorker(int child,int parent,string root,string pipe,int host,long started,CancellationToken cancel){
  cancel.ThrowIfCancellationRequested();RequireTarget(child,parent,Child);if(!LoggedOn(child))throw new InvalidOperationException("게임 실행 환경 로그인이 완료되지 않았습니다.");
  string executable=NativePaths.Resolve(Assembly.GetExecutingAssembly().Location);
  string arguments="--game-environment-worker "+StoryClient.Quote(root)+" "+child+" "+parent+" "+pipe+" "+host+" "+started;
  string name="Catheryne.ChildSession."+Guid.NewGuid().ToString("N");
  object scheduler=null,folder=null,definition=null,settings=null,principal=null,actions=null,action=null,registered=null,running=null;bool created=false,ready=false;
  try{
   scheduler=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));Call(scheduler,"Connect");folder=Call(scheduler,"GetFolder","\\");definition=Call(scheduler,"NewTask",0);
   settings=Get(definition,"Settings");Set(settings,"Enabled",true);Set(settings,"Hidden",true);Set(settings,"AllowDemandStart",true);Set(settings,"DisallowStartIfOnBatteries",false);Set(settings,"StopIfGoingOnBatteries",false);Set(settings,"ExecutionTimeLimit","PT0S");
   string user;using(var identity=WindowsIdentity.GetCurrent())user=identity.Name;
   principal=Get(definition,"Principal");Set(principal,"UserId",user);Set(principal,"LogonType",3);Set(principal,"RunLevel",1);
   actions=Get(definition,"Actions");action=Call(actions,"Create",0);Set(action,"Path",executable);Set(action,"Arguments",arguments);Set(action,"WorkingDirectory",Path.GetDirectoryName(executable));
   cancel.ThrowIfCancellationRequested();registered=Call(folder,"RegisterTaskDefinition",name,definition,2,user,null,3,null);created=true;
   cancel.ThrowIfCancellationRequested();running=Call(registered,"RunEx",null,4,child,null);if(running==null)throw new IOException("게임 실행 환경을 시작하지 못했습니다.");
   // Keep the one-time registration until its worker has actually started.
   // Removing a queued task immediately can cancel startup during user logon.
   string last=null;bool retried=false;var launchWait=Stopwatch.StartNew();
   WaitForWorker(()=>{
    var endpoint=GameEnvironment.Status(root);string state=CodexChat.S(endpoint,"state");
    if((state=="starting"||state=="ready")&&!GameEnvironment.StartupMatches(endpoint,child,parent,pipe,host,started))throw new IOException("게임 실행 환경의 시작 요청이 변경되었습니다.");
    return state;
   },()=>{
    bool active=RefreshWorkerTask(()=>Call(running,"Refresh"),()=>Convert.ToInt32(Get(registered,"LastTaskResult")));string state=active?Convert.ToString(Get(running,"State")):"queued",result=Convert.ToString(Get(registered,"LastTaskResult"));
    if(last!=state+":"+result){using(var db=new LocalDataService(root))db.Observe("default","game-environment",new{phase="worker-start",parent=parent,child=child,task_state=state,task_result=result});last=state+":"+result;}
    // A task queued during shell initialization may never get an instance. Reissue once after actual logon.
    if(!active&&!retried&&launchWait.Elapsed.TotalSeconds>=5&&LoggedOn(child)){
     retried=true;try{Call(running,"Stop");}catch(Exception error){var cause=error.GetBaseException();if(!(cause is COMException)||cause.HResult!=unchecked((int)0x8004130B))throw;}
     Release(running);running=null;cancel.ThrowIfCancellationRequested();running=Call(registered,"RunEx",null,4,child,null);if(running==null)throw new IOException(Locale.T("게임 실행기를 시작하지 못했습니다."));
     using(var db=new LocalDataService(root))db.Observe("default","game-environment",new{phase="worker-reissue",parent=parent,child=child,elapsed_ms=launchWait.ElapsedMilliseconds});
    }
   },cancel,GameEnvironment.WorkerTimeout);ready=true;
  }finally{
   try{
    // Stop only this unacknowledged instance, including a queued logon launch.
    if(!ready&&running!=null)try{Call(running,"Stop");}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.RuntimeFailure,error,root);}
    if(created)Call(folder,"DeleteTask",name,0);
   }finally{foreach(var value in new[]{running,registered,action,actions,principal,settings,definition,folder,scheduler})Release(value);}
  }
 }
}

internal sealed class ChildSessionView:Forms.AxHost {
 internal ChildSessionView():base("A0C63C30-F08D-4AB4-907C-34905D770C7D"){Dock=Forms.DockStyle.Fill;}
 ConnectionPointCookie events;
 internal string Failure;
 internal string FailureKind;
 internal bool LoggedIn;
 internal bool Reconnecting;
 internal const int ReconnectAttempts=3;
 internal string LoginError;
 internal Action<string,int?> Trace;
 void Record(string phase,int? code=null){if(Trace!=null)Trace(phase,code);}
 protected override void CreateSink(){base.CreateSink();events=new ConnectionPointCookie(GetOcx(),new SessionEvents(this),typeof(RdpEvents));}
 protected override void DetachSink(){try{if(events!=null){events.Disconnect();events=null;}}finally{base.DetachSink();}}
 [ComImport,Guid("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6"),InterfaceType(ComInterfaceType.InterfaceIsIDispatch)] interface RdpEvents {
  [DispId(1)] void OnConnecting();
  [DispId(2)] void OnConnected();
  [DispId(3)] void OnLoginComplete();
  [DispId(4)] void OnDisconnected(int reason);
  [DispId(10)] void OnFatalError(int error);
  [DispId(15)] void OnConfirmClose(out bool allowed);
  [DispId(17)] void OnAutoReconnecting(int reason,int attempt,out int decision);
  [DispId(22)] void OnLogonError(int error);
  [DispId(33)] void OnAutoReconnected();
 }
 [ComVisible(true),ClassInterface(ClassInterfaceType.None)] sealed class SessionEvents:RdpEvents {
  readonly ChildSessionView owner;
  internal SessionEvents(ChildSessionView owner){this.owner=owner;}
  public void OnConnecting(){owner.Record("connecting");}
  public void OnConnected(){owner.Record("connected");}
  public void OnLoginComplete(){owner.Restored("logged-on");}
  public void OnDisconnected(int reason){owner.Reconnecting=false;owner.Record("disconnected",reason);string description=null;try{int extended=Convert.ToInt32(WindowsChildSession.Get(owner.GetOcx(),"ExtendedDisconnectReason"));if(extended!=0)owner.Record("disconnect-extended",extended);description=Convert.ToString(WindowsChildSession.Call(owner.GetOcx(),"GetErrorDescription",reason,extended));}catch{}owner.FailureKind=reason==2055?"authentication":"connection";owner.Failure=DisconnectMessage(reason,description);}
  public void OnFatalError(int error){owner.Reconnecting=false;owner.Record("fatal-error",error);owner.FailureKind="connection";owner.Failure="게임 실행 환경 연결 오류 ("+error+").";}
  public void OnConfirmClose(out bool allowed){allowed=true;}
  public void OnAutoReconnecting(int reason,int attempt,out int decision){
   // The Windows event value 1 stops reconnection. Reuse an authenticated session only; never retry logon.
   decision=owner.LoggedIn&&owner.Failure==null&&reason!=2055&&attempt>=1&&attempt<=ReconnectAttempts?0:1;owner.Reconnecting=decision==0;
   owner.Record("reconnecting",reason);owner.Record("reconnect-attempt",attempt);
   if(decision==1){owner.FailureKind=reason==2055?"authentication":"connection";owner.Failure=DisconnectMessage(reason,null);}
  }
  public void OnAutoReconnected(){owner.Restored("reconnected");}
  public void OnLogonError(int error){owner.Record("logon-error",error);if(error!=-2)owner.LoginError="게임 실행 환경 로그인 오류 ("+error+").";}
 }
 void Restored(string phase){
  try{
   // A completed explicit logon does not authorize another credential prompt on connection recovery.
   var authentication=(ClientAuthentication)GetOcx();authentication.SetAllowPromptingForCredentials(false);
   if(authentication.GetAllowPromptingForCredentials())throw new InvalidOperationException(Locale.T("Windows 인증 방식을 설정하지 못했습니다."));
   Reconnecting=false;LoggedIn=true;LoginError=null;Failure=null;FailureKind=null;Record(phase);
  }catch(Exception error){Reconnecting=false;FailureKind="connection";Failure=error.GetBaseException().Message;Record("reconnect-guard-failed",error.GetBaseException().HResult);}
 }
 internal static string DisconnectMessage(int reason,string description){
  if(reason==2055)return Locale.T("Windows 로그인을 확인하지 못했습니다. 설정의 자동화 실행에서 연결해 주세요.");
  return string.IsNullOrWhiteSpace(description)?Locale.T("게임 실행 환경 연결이 끊어졌습니다."):description;
 }
 internal int Connection {get{return IsHandleCreated?Convert.ToInt32(WindowsChildSession.Get(GetOcx(),"Connected")):0;}}
 internal void Configure(int width,int height,bool interactiveAuthentication=false){
  if(Connection!=0)throw new InvalidOperationException(Locale.T("연결된 게임 실행 환경은 다시 설정할 수 없습니다."));
  object client=GetOcx();var authentication=(ClientAuthentication)client;
  // Windows owns the optional remember choice; the launcher never reads or stores a password.
  authentication.SetAllowCredentialSaving(interactiveAuthentication);
  if(authentication.GetAllowCredentialSaving()!=interactiveAuthentication)throw new InvalidOperationException(Locale.T("Windows 인증 방식을 설정하지 못했습니다."));
  // Use the client-owned credential dialog for the explicit connection, with its optional saving choice.
  authentication.SetPromptForCredsOnClient(interactiveAuthentication);
  if(authentication.GetPromptForCredsOnClient()!=interactiveAuthentication)throw new InvalidOperationException(Locale.T("Windows 인증 방식을 설정하지 못했습니다."));
  authentication.SetAllowPromptingForCredentials(interactiveAuthentication);
  if(authentication.GetAllowPromptingForCredentials()!=interactiveAuthentication)throw new InvalidOperationException(Locale.T("Windows 인증 방식을 설정하지 못했습니다."));
  string user;using(var identity=WindowsIdentity.GetCurrent())user=identity.Name;
  WindowsChildSession.Set(client,"UserName",user);
  if(!string.Equals(Convert.ToString(WindowsChildSession.Get(client,"UserName")),user,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(Locale.T("Windows 로그인 대상을 설정하지 못했습니다."));
  WindowsChildSession.Set(client,"Server","localhost");WindowsChildSession.Set(client,"DesktopWidth",width);WindowsChildSession.Set(client,"DesktopHeight",height);WindowsChildSession.Set(client,"ColorDepth",32);
  object secured=WindowsChildSession.Get(client,"SecuredSettings2"),advanced=WindowsChildSession.Get(client,"AdvancedSettings7");
  WindowsChildSession.Set(secured,"KeyboardHookMode",0);WindowsChildSession.Set(secured,"AudioRedirectionMode",2);WindowsChildSession.Set(advanced,"EnableCredSspSupport",true);WindowsChildSession.Set(advanced,"SmartSizing",true);
  WindowsChildSession.Set(advanced,"EnableAutoReconnect",true);WindowsChildSession.Set(advanced,"MaxReconnectAttempts",ReconnectAttempts);
  // Do not forward host drives, printers, devices or the clipboard into the game session.
  WindowsChildSession.Set(advanced,"RedirectDrives",false);WindowsChildSession.Set(advanced,"RedirectPrinters",false);WindowsChildSession.Set(advanced,"RedirectPorts",false);WindowsChildSession.Set(advanced,"RedirectClipboard",false);
  var extended=(ExtendedSettings)client;
  SetExtendedFlag(extended,"ConnectToChildSession",Locale.T("Windows 분리 연결을 설정하지 못했습니다."));
  // Remote pointer-position updates must never warp the user's desktop cursor.
  SetExtendedFlag(extended,"IgnoreServerGeneratedMouseMoves",Locale.T("게임 입력 격리를 설정하지 못했습니다."));
  // Microsoft introduced this optional property in the 24H2 RDP client (build 26100).
  // Older clients return E_UNEXPECTED for unknown properties; do not call them.
  if(SupportsRelativeMouse(ClientVersion))SetExtendedFlag(extended,"AllowRelativeMouseMode",Locale.T("게임 입력 격리를 설정하지 못했습니다."));
  Record("configured");
 }
 internal static Version ClientVersion {get{
  var info=FileVersionInfo.GetVersionInfo(Path.Combine(Environment.SystemDirectory,"mstscax.dll"));
  return new Version(info.FileMajorPart,info.FileMinorPart,info.FileBuildPart,info.FilePrivatePart);
 }}
 internal static bool SupportsRelativeMouse(Version clientVersion){return clientVersion>=new Version(10,0,26100,0);}
 static void SetExtendedFlag(ExtendedSettings extended,string name,string failureMessage){
  try{
   object enabled=true;extended.SetProperty(name,ref enabled);
   if(!Equals(extended.GetProperty(name),true))throw new InvalidOperationException(failureMessage);
  }catch(COMException error){throw new COMException("Windows RDP setting failed: "+name+" (0x"+error.ErrorCode.ToString("X8")+")",error.ErrorCode);}
 }
 internal void Connect(int width,int height,bool interactiveAuthentication=false){
  if(Connection!=0)return;
  Failure=null;FailureKind=null;LoginError=null;LoggedIn=false;Reconnecting=false;
  Configure(width,height,interactiveAuthentication);WindowsChildSession.Call(GetOcx(),"Connect");
 }
 internal void Disconnect(){if(Connection!=0)WindowsChildSession.Call(GetOcx(),"Disconnect");}
 // IMsRdpClientNonScriptable5 includes the documented saving-choice property from version 4.
 // Gaps preserve the installed type library's inherited native ABI.
 [ComImport,Guid("4F6996D5-D7B1-412C-B0FF-063718566907"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ClientAuthentication {
  void _VtblGap1_42();
  void SetAllowCredentialSaving([MarshalAs(UnmanagedType.VariantBool)] bool allowed);
  [return:MarshalAs(UnmanagedType.VariantBool)] bool GetAllowCredentialSaving();
  void SetPromptForCredsOnClient([MarshalAs(UnmanagedType.VariantBool)] bool prompt);
  [return:MarshalAs(UnmanagedType.VariantBool)] bool GetPromptForCredsOnClient();
  void _VtblGap2_14();
  void SetAllowPromptingForCredentials([MarshalAs(UnmanagedType.VariantBool)] bool allowed);
  [return:MarshalAs(UnmanagedType.VariantBool)] bool GetAllowPromptingForCredentials();
 }
 [ComImport,Guid("302D8188-0052-4807-806A-362B628F9AC5"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ExtendedSettings {
  void SetProperty([MarshalAs(UnmanagedType.BStr)] string name,[MarshalAs(UnmanagedType.Struct)] ref object value);
  [return:MarshalAs(UnmanagedType.Struct)] object GetProperty([MarshalAs(UnmanagedType.BStr)] string name);
 }
}
