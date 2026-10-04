using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

// Desktop and MCP read the same private binding and call the same StoryService.
internal sealed class StoryClient {
 readonly string configPath;
 internal StoryClient(string data){configPath=Path.Combine(data,"ai-connection.json");}
 internal Dictionary<string,object> Config(){return File.Exists(configPath)?Read(configPath):new Dictionary<string,object>();}
 internal static Dictionary<string,object> Read(string path){using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream))return new JavaScriptSerializer {MaxJsonLength=16000000}.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());}
 internal void SetPlan(string path){
  var plan=Read(path);if(!plan.ContainsKey("id")||!plan.ContainsKey("steps"))throw new InvalidDataException("임무 계획 형식을 확인해 주세요.");
  var config=Config();config["story_plan"]=Path.GetFullPath(path);
  // The host fingerprints the complete plan and refuses an incompatible journal.
  string id;using(var sha=System.Security.Cryptography.SHA256.Create())id=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
  config["story_state_dir"]=Path.Combine(Path.GetDirectoryName(configPath),"tasks",id);
  Save(config);
 }
 internal void Save(Dictionary<string,object> config){Directory.CreateDirectory(Path.GetDirectoryName(configPath));AtomicFile.Write(configPath,new JavaScriptSerializer().Serialize(config));}
 internal static Uri Endpoint(string url){Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="http"||uri.Host!="127.0.0.1"||uri.AbsolutePath!="/rpc"||uri.UserInfo.Length!=0||uri.Query.Length!=0||uri.Fragment.Length!=0)throw new InvalidDataException("유효한 로컬 관제 연결이 아닙니다.");return uri;}
 internal bool HasConnection(){
  var config=Config();object directory;if(!config.TryGetValue("story_state_dir",out directory))return false;
  string file=Path.Combine(Convert.ToString(directory),"connection.json");if(!File.Exists(file))return false;
  var binding=Read(file);object id;if(binding.TryGetValue("pid",out id)){try{using(var process=Process.GetProcessById(Convert.ToInt32(id)))return !process.HasExited;}catch(ArgumentException){return false;}}
  try{Call("status",new{after_sequence=0},true);return true;}catch(InvalidOperationException){return false;}
 }
 internal Dictionary<string,object> Call(string method,object parameters,bool compact=false){
  string root=Path.GetDirectoryName(configPath);if(GameEnvironment.Remote(root))return CodexChat.Map(GameEnvironment.Invoke(root,"story.call",new Dictionary<string,object>{{"method",method},{"parameters",parameters},{"compact",compact}},false));
  var config=Config();if(!config.ContainsKey("story_state_dir"))throw new InvalidOperationException("임무 계획을 선택해 주세요.");
  string file=Path.Combine(Convert.ToString(config["story_state_dir"]),"connection.json");
  if(!File.Exists(file))throw new InvalidOperationException("관제가 꺼져 있습니다. 관제 시작을 눌러 주세요.");
  var binding=Read(file);if(GameEnvironment.InSession(root)&&(!binding.ContainsKey("pid")||!GameEnvironment.OwnsProcess(Convert.ToInt32(binding["pid"]))))throw new InvalidOperationException("관제가 현재 게임 실행 환경에 속하지 않습니다.");var request=(HttpWebRequest)WebRequest.Create(Endpoint(Convert.ToString(binding["url"])));
  request.Method="POST";request.AllowAutoRedirect=false;request.Timeout=5000;request.ReadWriteTimeout=5000;request.Proxy=null;
  request.ContentType="application/json";request.Headers["Authorization"]="Bearer "+Convert.ToString(binding["token"]);
  var json=new JavaScriptSerializer {MaxJsonLength=16000000};byte[] body=Encoding.UTF8.GetBytes(json.Serialize(new{method=method,@params=parameters,view=compact?"compact":"full"}));
  request.ContentLength=body.Length;using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);
  WebResponse response;try{response=request.GetResponse();}catch(WebException e){if(e.Response==null)throw new InvalidOperationException("관제에 연결하지 못했습니다. 관제 상태를 확인해 주세요.");response=e.Response;}
  using(response)using(var reader=new StreamReader(response.GetResponseStream())){
   var data=json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
   if(!data.ContainsKey("ok")||!(bool)data["ok"])throw new InvalidOperationException("게임 작업을 처리하지 못했습니다: "+(data.ContainsKey("error")?Convert.ToString(data["error"]):"현재 조작권과 미확인 결과를 확인해 주세요."));
   return (Dictionary<string,object>)data["result"];
  }
 }
 internal void Shutdown(){
  var status=Call("status",new{after_sequence=0},true);Process host;
  try{host=Process.GetProcessById(Convert.ToInt32(status["host_pid"]));}catch(ArgumentException){return;}
  using(host){try{Call("shutdown",new{});}catch(InvalidOperationException){if(!host.HasExited)throw;}if(!host.WaitForExit(5000))throw new InvalidOperationException("게임 관제의 종료를 기다리고 있습니다.");}
 }
 internal void Start(bool input){
  string root=Path.GetDirectoryName(configPath);if(GameEnvironment.Remote(root)){GameEnvironment.Invoke(root,"story.start",new Dictionary<string,object>{{"input",input}},true);return;}
  if(input)GameInputLease.CheckAvailable(Path.GetDirectoryName(configPath));
  var config=Config();
  foreach(string key in new[]{"story_plan","story_state_dir","story_python"})if(!config.ContainsKey(key)||string.IsNullOrWhiteSpace(Convert.ToString(config[key])))throw new InvalidOperationException("관제 구성 또는 임무 계획이 없습니다. 설치 상태를 확인해 주세요.");
  try{Call("status",new{after_sequence=0});throw new AlreadyRunningException();}catch(AlreadyRunningException){throw new InvalidOperationException("이미 실행 중인 관제에 연결했습니다. 입력 권한 변경은 종료 후 다시 시작할 때 적용됩니다.");}catch(InvalidOperationException){}
  string python=Convert.ToString(config["story_python"]), plan=Convert.ToString(config["story_plan"]),state=Convert.ToString(config["story_state_dir"]);
  string source=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"core","story-control","src");
  if(!File.Exists(python)||!File.Exists(Path.Combine(source,"story_control","__main__.py")))throw new InvalidOperationException("관제 실행 구성요소가 설치되지 않았습니다.");
  python=NativePaths.Resolve(python);plan=NativePaths.Resolve(plan);state=NativePaths.Resolve(state);source=NativePaths.Resolve(source);
  var start=new ProcessStartInfo(python){UseShellExecute=true,Verb=input&&!AppRuntime.Elevated()?"runas":"",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=NativePaths.Resolve(AppDomain.CurrentDomain.BaseDirectory)};
  string bootstrap="import sys,runpy;sys.path.insert(0,'"+source.Replace("\\","\\\\").Replace("'","\\'")+"');runpy.run_module('story_control',run_name='__main__')";
  start.Arguments="-B -c "+Quote(bootstrap)+" serve --plan "+Quote(plan)+" --state-dir "+Quote(state);
  if(input){var installation=new LauncherOperations(Path.GetDirectoryName(configPath)).Installation();if(!Setup.Ready(installation))throw new InvalidOperationException("게임 설치 경로를 확인해 주세요.");string game=Convert.ToString(Read(installation.Config)["GamePath"]);start.Arguments+=" --enable-input --target-exe "+Quote(game)+" --input-lock "+Quote(GameInputLease.LockPath(Path.GetDirectoryName(configPath)));}
  start.Arguments+=" --assist-profile "+Quote(Path.Combine(Path.GetDirectoryName(configPath),"assist","hud-profile.json"));
  using(var process=Process.Start(start)){
   for(int i=0;i<25;i++){
    if(process.HasExited)throw new InvalidOperationException("관제 시작에 실패했습니다. 임무 계획과 저장 기록의 호환성을 확인해 주세요.");
    try{Call("status",new{after_sequence=0});return;}catch(InvalidOperationException){}
    System.Threading.Thread.Sleep(200);
   }
   throw new InvalidOperationException("관제 시작 확인이 지연되고 있습니다. 중복 실행하지 말고 상태 갱신을 기다려 주세요.");
  }
 }
 sealed class AlreadyRunningException:Exception{}
 internal static string Quote(string value){if(value.Contains("\"")||value.Contains("\r")||value.Contains("\n"))throw new ArgumentException("잘못된 경로입니다.");return "\""+value.TrimEnd('\\')+"\"";}
}
