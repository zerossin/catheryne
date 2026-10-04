using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal enum DiagnosticEvent { Started, Stopped, UnhandledFailure, RuntimeFailure, AiConnectionFailure, AiTransportFailure, TaskReadFailure, ToolFailure, ImageFailure, CollectionFailure, DataRefreshFailure, DiagnosticExportFailure, ComponentCleanupFailure, NotificationFailure }

// Support reports contain a fixed schema, never exception messages or runtime payloads.
internal sealed class AppDiagnostics {
 const int RetainedFiles=4,ReportEntries=64;
 readonly string root;readonly int maxBytes;
 static readonly Lazy<string> build=new Lazy<string>(()=>{try{using(var sha=SHA256.Create())using(var file=File.OpenRead(Assembly.GetExecutingAssembly().Location))return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}catch{return "unavailable";}});
 internal AppDiagnostics(string root,int maxBytes=256*1024){if(maxBytes<1024)throw new ArgumentOutOfRangeException("maxBytes");this.root=root;this.maxBytes=maxBytes;}
 static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=2*1024*1024};}
 static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").Substring(0,24);}
 string FileName(int index){return Path.Combine(root,"diagnostics",index==0?"app.jsonl":"app."+index+".jsonl");}
 Mutex Gate(){return PrivateIpc.Mutex("Global\\Catheryne.Diagnostics."+Hash(Path.GetFullPath(root).ToUpperInvariant()));}
 static bool Enter(Mutex gate,int wait=100){try{return gate.WaitOne(wait);}catch(AbandonedMutexException){return true;}}
 static object[] Faults(Exception error){
  var faults=new List<object>();for(int depth=0;error!=null&&depth<4;depth++,error=error.InnerException){
   var frames=new StackTrace(error,false).GetFrames()??new StackFrame[0];
   var methods=frames.Select(f=>f.GetMethod()).Where(m=>m!=null&&m.DeclaringType!=null&&m.DeclaringType.Assembly==typeof(AppDiagnostics).Assembly).Take(8).Select(m=>m.DeclaringType.FullName+"."+m.Name).ToArray();
   var fault=new Dictionary<string,object>{{"type",error.GetType().FullName},{"hresult","0x"+error.HResult.ToString("X8",CultureInfo.InvariantCulture)},{"methods",methods}};
   var web=error as WebException;if(web!=null){fault["network_status"]=web.Status.ToString();var response=web.Response as HttpWebResponse;if(response!=null)fault["http_status"]=(int)response.StatusCode;}
   faults.Add(fault);
  }return faults.ToArray();
 }
 internal void Write(DiagnosticEvent kind,Exception error=null){
  if(!Enum.IsDefined(typeof(DiagnosticEvent),kind))throw new ArgumentOutOfRangeException("kind");
  try{
   var line=Encoding.UTF8.GetBytes(Json().Serialize(new{schema=1,at=DateTime.UtcNow.ToString("o"),@event=kind.ToString(),build=build.Value,faults=Faults(error)})+"\n");
   if(line.Length>maxBytes)return;
   using(var gate=Gate()){if(!Enter(gate,0))return;try{
    Directory.CreateDirectory(Path.GetDirectoryName(FileName(0)));
    if(File.Exists(FileName(0))&&new FileInfo(FileName(0)).Length+line.Length>maxBytes){if(File.Exists(FileName(RetainedFiles-1)))File.Delete(FileName(RetainedFiles-1));for(int i=RetainedFiles-2;i>=0;i--)if(File.Exists(FileName(i)))File.Move(FileName(i),FileName(i+1));}
    using(var output=new FileStream(FileName(0),FileMode.Append,FileAccess.Write,FileShare.Read)){output.Write(line,0,line.Length);}
   }finally{gate.ReleaseMutex();}}
  }catch{ /* Diagnostics must never interrupt the original operation. */ }
 }
 // Import only our own bounded log schema; unrelated diagnostic files are never read.
 static Dictionary<string,object> SafeRecord(string line){
  try{
   var data=CodexChat.Map(Json().DeserializeObject(line));DiagnosticEvent kind;DateTime at;string name=CodexChat.S(data,"event"),stamp=CodexChat.S(data,"at"),id=CodexChat.S(data,"build");
   if(!Enum.TryParse(name,out kind)||!Enum.IsDefined(typeof(DiagnosticEvent),kind)||!DateTime.TryParse(stamp,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out at)||!System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-f0-9]{64}$|^unavailable$"))return null;
   var faults=new List<object>();object raw;if(data.TryGetValue("faults",out raw))foreach(var item in CodexChat.Items(raw).Take(4)){
    string type=CodexChat.S(item,"type"),hr=CodexChat.S(item,"hresult");if(!System.Text.RegularExpressions.Regex.IsMatch(type,"^[A-Za-z_][A-Za-z0-9_.+`]{0,180}$")||!System.Text.RegularExpressions.Regex.IsMatch(hr,"^0x[A-F0-9]{8}$"))continue;
    var fault=new Dictionary<string,object>{{"type",type},{"hresult",hr}};
    object methods;if(item.TryGetValue("methods",out methods)){var list=methods as System.Collections.IEnumerable;fault["methods"]=list==null?new string[0]:list.Cast<object>().Select(Convert.ToString).Where(s=>System.Text.RegularExpressions.Regex.IsMatch(s,"^[A-Za-z_][A-Za-z0-9_.+<>`]{0,220}$")).Take(8).ToArray();}
    WebExceptionStatus status;if(Enum.TryParse(CodexChat.S(item,"network_status"),out status)&&Enum.IsDefined(typeof(WebExceptionStatus),status))fault["network_status"]=status.ToString();int code;if(int.TryParse(CodexChat.S(item,"http_status"),out code)&&code>=100&&code<=599)fault["http_status"]=code;faults.Add(fault);
   }
   return new Dictionary<string,object>{{"schema",1},{"at",at.ToUniversalTime().ToString("o")},{"event",kind.ToString()},{"build",id},{"faults",faults.ToArray()}};
  }catch{return null;}
 }
 internal string Export(){
  var entries=new List<Dictionary<string,object>>();bool busy=false;
  try{using(var gate=Gate()){if(!Enter(gate))busy=true;else try{for(int i=RetainedFiles-1;i>=0;i--){string file=FileName(i);if(!File.Exists(file)||new FileInfo(file).Length>maxBytes)continue;foreach(string line in File.ReadLines(file)){if(line.Length>8192)continue;var record=SafeRecord(line);if(record!=null)entries.Add(record);}}}finally{gate.ReleaseMutex();}}}catch{busy=true;}
  return Json().Serialize(new{schema=1,product="Catheryne",build=build.Value,assembly_version=Assembly.GetExecutingAssembly().GetName().Version.ToString(),os=Environment.OSVersion.Version.ToString(),clr=Environment.Version.ToString(),process_bits=Environment.Is64BitProcess?64:32,read_incomplete=busy,omitted_entries=Math.Max(0,entries.Count-ReportEntries),events=entries.Skip(Math.Max(0,entries.Count-ReportEntries)).ToArray()});
 }
 internal static void Record(DiagnosticEvent kind,Exception error=null,string dataRoot=null){new AppDiagnostics(dataRoot??Setup.DataFolder).Write(kind,error);}
}
