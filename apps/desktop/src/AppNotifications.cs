using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

internal enum NotificationPriority { Normal, Attention, Urgent }
internal sealed class NotificationTarget {
 internal readonly string Page,Thread;
 internal NotificationTarget(string page,string thread=null){if(!new[]{"Capture","Daily","Resin","Story","Chat","Tasks"}.Contains(page))throw new ArgumentException("Unknown notification destination");if(thread!=null&&!System.Text.RegularExpressions.Regex.IsMatch(thread,@"^[A-Za-z0-9_-]{1,80}$"))throw new ArgumentException("Invalid conversation identity");Page=page;Thread=thread;}
 internal string Link {get{return "catheryne-notification://open/?page="+Page+(Thread==null?"":"&thread="+Uri.EscapeDataString(Thread));}}
 internal static NotificationTarget Parse(string value){
  Uri uri;if(value==null||value.Length>1024||!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="catheryne-notification"||uri.Host!="open"||uri.AbsolutePath!="/"||uri.UserInfo.Length!=0||uri.Port!=-1||uri.Fragment.Length!=0)throw new ArgumentException("Invalid notification activation");
  var fields=new Dictionary<string,string>();foreach(string pair in uri.Query.TrimStart('?').Split('&')){var parts=pair.Split(new[]{'='},2);if(parts.Length!=2||!new[]{"page","thread"}.Contains(parts[0])||fields.ContainsKey(parts[0]))throw new ArgumentException("Invalid notification arguments");fields.Add(parts[0],Uri.UnescapeDataString(parts[1]));}
  string page,thread;fields.TryGetValue("page",out page);fields.TryGetValue("thread",out thread);return new NotificationTarget(page,thread);
 }
}
internal sealed class AppNotification {
 internal readonly string Key,Title,Message;internal readonly NotificationTarget Target;internal readonly NotificationPriority Priority;
 internal AppNotification(string key,string title,string message,NotificationTarget target,NotificationPriority priority=NotificationPriority.Normal){if(string.IsNullOrWhiteSpace(key)||key.Length>512)throw new ArgumentException("Invalid notification key");Key=key;Title=title;Message=message;Target=target;Priority=priority;}
 internal string Tag {get{return AppNotifications.Identity(Key);}}
 internal string Xml(bool urgentSupported,bool silent=false){
  string scenario=Priority==NotificationPriority.Normal?"":Priority==NotificationPriority.Urgent&&urgentSupported?"urgent":"reminder";
  return "<toast launch='"+Escape(Target.Link)+"'"+(scenario.Length==0?"":" scenario='"+scenario+"'")+"><visual><binding template='ToastGeneric'><text>"+Escape(Title)+"</text><text>"+Escape(Message)+"</text></binding></visual><actions><action content='"+Escape(Locale.T("열기"))+"' arguments='"+Escape(Target.Link)+"' activationType='foreground'/></actions>"+(silent?"<audio silent='true'/>":"<audio src='ms-winsoundevent:Notification.Default'/>")+"</toast>";
 }
 static string Escape(string value){return SecurityElement.Escape(new string((value??"").Where(c=>!char.IsControl(c)||c=='\n'||c=='\t').Take(512).ToArray()));}
 internal static string QuestionKey(AiUserInput request){return "question:"+request.Thread+":"+request.Turn+":"+request.Key;}
 internal static AppNotification Question(AiUserInput request){return new AppNotification(QuestionKey(request),Locale.T("응답 필요"),Locale.T("AI 작업에 응답이 필요합니다."),new NotificationTarget("Chat",request.Thread),NotificationPriority.Attention);}
 internal static AppNotification Controller(Dictionary<string,object> report){
  var settings=CodexChat.Map(report.ContainsKey("settings")?report["settings"]:null);string alert=CodexChat.S(report,"alert");
  if(!Equals(settings.ContainsKey("notifications")?settings["notifications"]:false,true)||!Equals(report.ContainsKey("stopped")?report["stopped"]:false,true)||!StoryAlerts.RequiresAttention(alert))return null;
  bool urgent=Equals(report.ContainsKey("urgent")?report["urgent"]:false,true)&&!Equals(report.ContainsKey("paused")?report["paused"]:false,true);
  return new AppNotification("controller:"+CodexChat.S(report,"plan_id")+":"+alert+":"+CodexChat.S(report,"stage")+":"+CodexChat.S(report,"cursor"),Locale.T(urgent?"긴급":"확인 필요"),StoryAlerts.Text(alert),new NotificationTarget("Story"),urgent?NotificationPriority.Urgent:NotificationPriority.Attention);
 }
}
internal sealed class AppNotifications {
 internal const string AppId="Catheryne.Desktop",Activator="{CE0A4BCD-DB1B-4B55-8B77-964DAFC41A73}";
 static readonly Lazy<AppNotifications> current=new Lazy<AppNotifications>(()=>new AppNotifications(Setup.DataFolder));internal static AppNotifications Current {get{return current.Value;}}
 readonly string root;readonly Func<AppNotification,int> send;readonly Action<string> remove;readonly object sequence=new object();Task pending=Task.FromResult(true);string controllerSignature,controllerKey;
 internal AppNotifications(string root,Func<AppNotification,int> send=null,Action<string> remove=null){this.root=root;this.send=send??NativeSend;this.remove=remove??NativeRemove;}
 internal static string Identity(string key){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","").Substring(0,16);}
 Task<bool> Queue(Func<bool> action){lock(sequence){var result=pending.ContinueWith(_=>{try{return action();}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.NotificationFailure,error);return false;}},TaskScheduler.Default);pending=result;return result;}}
 internal Task<bool> Send(AppNotification notification){return Queue(()=>{
  using(var gate=PrivateIpc.Mutex("Global\\Catheryne.Notification."+Identity(Path.GetFullPath(root).ToUpperInvariant()))){bool held=false;try{try{held=gate.WaitOne(5000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Notification delivery is busy");
   using(var db=new LocalDataService(root)){if(db.Query("SELECT event_key FROM notification_receipts WHERE event_key="+LocalDataService.Sql(notification.Key)).Count!=0)return false;
    int result=send(notification);if(result<0)Marshal.ThrowExceptionForHR(result);db.Execute("INSERT OR IGNORE INTO notification_receipts VALUES("+LocalDataService.Sql(notification.Key)+","+LocalDataService.Sql(DateTime.UtcNow.ToString("o"))+")");return true;}
  }finally{if(held)gate.ReleaseMutex();}}
 });}
 internal void ObserveController(Dictionary<string,object> report){
  if(!report.ContainsKey("available"))return;
  var notice=Equals(report["available"],true)?AppNotification.Controller(report):null;string signature=notice==null?null:CodexChat.S(report,"plan_id")+":"+CodexChat.S(report,"stage")+":"+CodexChat.S(report,"alert");
  if(signature==controllerSignature)return;if(controllerKey!=null){var clear=Clear(controllerKey);}controllerSignature=signature;controllerKey=notice==null?null:notice.Key;if(notice!=null){var delivery=Send(notice);}
 }
 internal Task<bool> Clear(string key){return Queue(()=>{remove(Identity(key));return true;});}
 static readonly object registration=new object();static bool registered;
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
 internal static void Register(){lock(registration){if(registered)return;string exe=NativePaths.Resolve(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GenshinLauncher.exe"));
  Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));
  using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\"+AppId)){key.SetValue("DisplayName","Catheryne");key.SetValue("IconUri",Path.Combine(Path.GetDirectoryName(exe),"branding","launcher.png"));key.SetValue("CustomActivator",Activator);}
  using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Classes\CLSID\"+Activator+@"\LocalServer32"))key.SetValue("",StoryClient.Quote(exe)+" --notification-server");registered=true;
 }}
 static int NativeSend(AppNotification value){Register();return NotificationShow(AppId,value.Xml(NotificationUrgentSupported()!=0),value.Tag,value.Priority==NotificationPriority.Normal?3600:86400,0);}
 static void NativeRemove(string tag){int result=NotificationRemove(AppId,tag);if(result<0)Marshal.ThrowExceptionForHR(result);}
 internal static int Probe(){
  string id="Catheryne.NotificationTest."+Guid.NewGuid().ToString("N"),clsid=Guid.NewGuid().ToString("B"),tag="native-test";int shown=-1,count=0;bool activation=false;
  try{
   using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\"+id)){key.SetValue("DisplayName","Catheryne notification check");key.SetValue("CustomActivator",clsid);}
   var notice=new AppNotification("native-test","Notification check","Synthetic, silent verification",new NotificationTarget("Tasks"));shown=NotificationShow(id,notice.Xml(false,true),tag,60,1);if(shown<0)Marshal.ThrowExceptionForHR(shown);
   if(shown==0){for(int i=0;i<30&&count==0;i++){count=NotificationCount(id);if(count<0)Marshal.ThrowExceptionForHR(count);if(count==0)Thread.Sleep(30);}if(count!=1)throw new IOException("Windows notification history did not accept the fixture");}
   var output=new StringBuilder(1025);var awaiting=Task.Run(()=>NotificationAwait(id,clsid,5000,output,output.Capacity));Guid classId=new Guid(clsid),interfaceId=new Guid("53E31837-6600-4A81-9395-75CFFE746F94");IntPtr instance=IntPtr.Zero;int created=unchecked((int)0x80040154);
   for(int i=0;i<100&&created<0;i++){created=CoCreateInstance(ref classId,IntPtr.Zero,4,ref interfaceId,out instance);if(created<0)Thread.Sleep(20);}
   if(created<0)Marshal.ThrowExceptionForHR(created);var callback=(NotificationCallback)Marshal.GetObjectForIUnknown(instance);Marshal.Release(instance);
   try{if(callback.Activate("wrong-app",notice.Target.Link,IntPtr.Zero,0)>=0)throw new IOException("Activation identity check failed");Marshal.ThrowExceptionForHR(callback.Activate(id,notice.Target.Link,IntPtr.Zero,0));}finally{Marshal.ReleaseComObject(callback);}
   activation=awaiting.Result==0&&output.ToString()==notice.Target.Link;if(!activation)throw new IOException("Native COM activation failed");
   NotificationRemove(id,tag);if(NotificationCount(id)!=0)throw new IOException("Resolved notification was retained");
   using(var writer=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)))writer.Write(CatheryneTools.Json().Serialize(new{nativeAccepted=shown==0,nativeStatus=shown,disabledByWindows=shown>0,historyCount=count,removed=true,comActivation=activation,urgentSupported=NotificationUrgentSupported()!=0}));return 0;
  }catch(Exception error){using(var writer=new StreamWriter(Console.OpenStandardError(),new UTF8Encoding(false)))writer.Write(error.GetBaseException().Message);return 1;}
  finally{try{NotificationRemove(id,tag);}catch{}Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\"+id,false);}
 }
 [ComImport,Guid("53E31837-6600-4A81-9395-75CFFE746F94"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface NotificationCallback {[PreserveSig]int Activate([MarshalAs(UnmanagedType.LPWStr)]string app,[MarshalAs(UnmanagedType.LPWStr)]string arguments,IntPtr data,uint count);}
 [DllImport("ole32.dll")]static extern int CoCreateInstance(ref Guid clsid,IntPtr outer,uint context,ref Guid iid,out IntPtr result);
 [DllImport("Catheryne.Notifications.dll",CharSet=CharSet.Unicode,CallingConvention=CallingConvention.Cdecl)]static extern int NotificationCount(string id);
 internal static bool IsActivationServer(string[] args){return args.Length>=1&&args[0]=="--notification-server"&&(args.Length==1||args.Length==2&&string.Equals(args[1],"-Embedding",StringComparison.OrdinalIgnoreCase));}
 internal static string AwaitActivation(){var text=new StringBuilder(1025);int result=Task.Run(()=>NotificationAwait(AppId,Activator,15000,text,text.Capacity)).GetAwaiter().GetResult();if(result<0)Marshal.ThrowExceptionForHR(result);return result==0?text.ToString():null;}
 [DllImport("Catheryne.Notifications.dll",CharSet=CharSet.Unicode,CallingConvention=CallingConvention.Cdecl)] static extern int NotificationShow(string id,string xml,string tag,int seconds,int silent);
 [DllImport("Catheryne.Notifications.dll",CharSet=CharSet.Unicode,CallingConvention=CallingConvention.Cdecl)] static extern int NotificationRemove(string id,string tag);
 [DllImport("Catheryne.Notifications.dll",CallingConvention=CallingConvention.Cdecl)] static extern int NotificationUrgentSupported();
 [DllImport("Catheryne.Notifications.dll",CharSet=CharSet.Unicode,CallingConvention=CallingConvention.Cdecl)] static extern int NotificationAwait(string app,string clsid,int milliseconds,StringBuilder result,int capacity);
}
internal static class NotificationActivation {
 static string PathFor(string root){return Path.Combine(root,"runtime","notification-activation.json");}
 static T Locked<T>(string root,Func<T> action){using(var gate=PrivateIpc.Mutex("Global\\Catheryne.Activation."+AppNotifications.Identity(Path.GetFullPath(root).ToUpperInvariant()))){bool held=false;try{try{held=gate.WaitOne(5000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Notification activation is busy");return action();}finally{if(held)gate.ReleaseMutex();}}}
 internal static void Save(string root,NotificationTarget target){Locked(root,()=>{Directory.CreateDirectory(Path.GetDirectoryName(PathFor(root)));AtomicFile.Write(PathFor(root),CatheryneTools.Json().Serialize(new{link=target.Link,at=DateTime.UtcNow.ToString("o")}));return true;});}
 internal static NotificationTarget Take(string root){return Locked(root,()=>{
  string path=PathFor(root);if(!File.Exists(path))return null;var data=StoryClient.Read(path);File.Delete(path);DateTime at;
  if(!DateTime.TryParse(CodexChat.S(data,"at"),null,System.Globalization.DateTimeStyles.RoundtripKind,out at))return null;var age=DateTime.UtcNow-at.ToUniversalTime();if(age<TimeSpan.FromSeconds(-30)||age>TimeSpan.FromMinutes(5))return null;return NotificationTarget.Parse(CodexChat.S(data,"link"));
 });}
}
