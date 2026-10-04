using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.IO.Compression;

internal static class AtomicFile {
 internal static void Write(string path,string text,string backup=null){Write(path,new UTF8Encoding(false).GetBytes(text),backup);}
 internal static void Write(string path,byte[] bytes,string backup=null) {
  string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  FileAttributes attrs=File.Exists(path)?File.GetAttributes(path):FileAttributes.Normal;
  try {
   File.WriteAllBytes(temp,bytes);
   if(File.Exists(path)) {
    if(backup!=null)File.Copy(path,backup,true);
    File.SetAttributes(path,attrs & ~FileAttributes.Hidden & ~FileAttributes.ReadOnly);
    File.Replace(temp,path,null);
   } else File.Move(temp,path);
  } finally {
   if(File.Exists(path))File.SetAttributes(path,attrs);
   if(File.Exists(temp))File.Delete(temp);
  }
 }
}

internal sealed class ChannelService {
 static readonly string[] Keys={"channel","sub_channel","cps"};
 readonly string folder;
 internal ChannelService(string gameFolder){folder=gameFolder;}
 internal string ConfigPath {get{return Path.Combine(folder,"config.ini");}}
 string Profile(bool google){return Path.Combine(folder,google?"config - 구글용.ini":"config - 원본.ini");}
 internal bool Available {get{return File.Exists(ConfigPath)&&File.Exists(Profile(false))&&File.Exists(Profile(true));}}
 internal static Dictionary<string,string> ReadFields(string text) {
  var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  bool general=false;
  foreach(var line in Regex.Split(text,"\r?\n")) {
   string trimmed=line.Trim().TrimStart('\uFEFF');
   if(trimmed.StartsWith("[")){general=trimmed.Equals("[general]",StringComparison.OrdinalIgnoreCase);continue;}
   if(!general)continue;
   var match=Regex.Match(line,@"^\s*(channel|sub_channel|cps)\s*=\s*([^\r\n;#]*)(?:[;#].*)?$",RegexOptions.IgnoreCase);
   if(match.Success) {
    string key=match.Groups[1].Value;
    if(values.ContainsKey(key))throw new InvalidDataException("채널 항목이 중복되어 전환하지 않았습니다.");
    values[key]=match.Groups[2].Value.Trim();
   }
  }
  if(Keys.Any(k=>!values.ContainsKey(k)))throw new InvalidDataException("채널 항목을 확인할 수 없습니다.");
  return values;
 }
 internal static string Merge(string current,string profile) {
  ReadFields(current);
  var desired=ReadFields(profile);
  bool general=false;
  var parts=Regex.Split(current,"(\r\n|\n)");
  for(int i=0;i<parts.Length;i+=2) {
   string trimmed=parts[i].Trim().TrimStart('\uFEFF');
   if(trimmed.StartsWith("[")){general=trimmed.Equals("[general]",StringComparison.OrdinalIgnoreCase);continue;}
   if(!general)continue;
   var m=Regex.Match(parts[i],@"^(\s*)(channel|sub_channel|cps)(\s*=\s*)([^;#]*)(.*)$",RegexOptions.IgnoreCase);
   if(m.Success)parts[i]=m.Groups[1].Value+m.Groups[2].Value+m.Groups[3].Value+desired[m.Groups[2].Value]+m.Groups[5].Value;
  }
  return string.Concat(parts);
 }
 internal string Current() {
  try {
   var current=ReadFields(File.ReadAllText(ConfigPath));
   foreach(bool google in new[]{false,true}) {
    var target=ReadFields(File.ReadAllText(Profile(google)));
    if(Keys.All(k=>target[k]==current[k]))return google?"Google Play":"원본";
   }
   return "사용자 설정";
  } catch {return "확인 불가";}
 }
 internal void Switch(bool google,Func<bool> busy) {
  if(busy())throw new InvalidOperationException("게임과 언락커를 종료한 뒤 전환해 주세요.");
  string before=File.ReadAllText(ConfigPath);
  string after=Merge(before,File.ReadAllText(Profile(google)));
  if(before==after)return;
  if(busy() || File.ReadAllText(ConfigPath)!=before)throw new IOException("설정이 변경되었습니다. 다시 시도해 주세요.");
  AtomicFile.Write(ConfigPath,after,ConfigPath+".launcher-backup");
 }
}

internal sealed class ReleaseInfo {
 internal string Tag,Url,Hash;
 internal long Size;
 internal Version Version;
 internal static ReleaseInfo Parse(string text,string assetName="unlockfps_nc.exe") {
  if(assetName!="unlockfps_nc.exe"&&assetName!="unlockfps_nc_signed.exe")throw new ArgumentException("호환되는 언락커 파일이 아닙니다.");
  var json=new JavaScriptSerializer();
  var data=json.Deserialize<Dictionary<string,object>>(text);
  if(Convert.ToBoolean(data["draft"])||Convert.ToBoolean(data["prerelease"]))throw new InvalidDataException("정식 릴리스가 아닙니다.");
  string tag=Convert.ToString(data["tag_name"]);
  Version version;
  if(!System.Version.TryParse(tag.TrimStart('v'),out version))throw new InvalidDataException("버전 정보가 잘못되었습니다.");
  var assets=(IEnumerable)data["assets"];
  foreach(Dictionary<string,object> asset in assets) {
   if(Convert.ToString(asset["name"])!=assetName)continue;
   string digest=asset.ContainsKey("digest")?Convert.ToString(asset["digest"]):"";
   if(!Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))throw new InvalidDataException("검증용 해시가 없어 자동 업데이트하지 않습니다.");
   string url=Convert.ToString(asset["browser_download_url"]);
   Uri uri;
   if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/34736384/genshin-fps-unlock/releases/download/",StringComparison.Ordinal)||!uri.AbsolutePath.EndsWith("/"+assetName,StringComparison.Ordinal))throw new InvalidDataException("공식 배포 주소가 아닙니다.");
   long size=Convert.ToInt64(asset["size"]);
   if(size<1024||size>64*1024*1024)throw new InvalidDataException("배포 파일 크기를 확인할 수 없습니다.");
   return new ReleaseInfo{Tag=tag,Version=version,Url=url,Hash=digest.Substring(7).ToLowerInvariant(),Size=size};
  }
  throw new InvalidDataException("호환되는 언락커 파일이 없습니다.");
 }
}

internal sealed class UpdateService {
 readonly string engine,statePath;
 readonly Func<bool> busy;
 internal UpdateService(string executable,string state,Func<bool> isBusy){engine=executable;statePath=state;busy=isBusy;}
 internal static string Hash(string path){using(var sha=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
 static HttpWebRequest Request(string url){var req=HttpTransport.Create(url);req.UserAgent="GenshinPersonalLauncher/1.1";req.Timeout=8000;req.ReadWriteTimeout=15000;return req;}
 internal static ReleaseInfo Latest(string assetName="unlockfps_nc.exe"){using(var response=Request("https://api.github.com/repos/34736384/genshin-fps-unlock/releases/latest").GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))return ReleaseInfo.Parse(reader.ReadToEnd(),assetName);}
 void Record(ReleaseInfo release){AtomicFile.Write(statePath,new JavaScriptSerializer().Serialize(new{Version=release.Tag,Hash=release.Hash}));}
 internal string Check(bool install) {
  return CheckRelease(Latest(Path.GetFileName(engine).Equals("unlockfps_nc_signed.exe",StringComparison.OrdinalIgnoreCase)?"unlockfps_nc_signed.exe":"unlockfps_nc.exe"),install,Download);
 }
 static void Download(string url,string staged,long size) {
  using(var response=Request(url).GetResponse())using(var input=response.GetResponseStream())using(var output=new FileStream(staged,FileMode.Create,FileAccess.Write,FileShare.None)) {
   var buffer=new byte[65536];int count;long total=0;
   while((count=input.Read(buffer,0,buffer.Length))>0){total+=count;if(total>size)throw new InvalidDataException("파일 크기가 일치하지 않습니다.");output.Write(buffer,0,count);}
  }
 }
 internal string CheckRelease(ReleaseInfo release,bool install,Action<string,string,long> download) {
  if(!File.Exists(engine)) {
   if(!install)return "언락커 설치 필요";
   if(busy())throw new InvalidOperationException("게임과 언락커를 종료한 뒤 설치해 주세요.");
   string fresh=engine+"."+Guid.NewGuid().ToString("N")+".download";
   try {
    download(release.Url,fresh,release.Size);
    if(new FileInfo(fresh).Length!=release.Size||Hash(fresh)!=release.Hash)throw new InvalidDataException("설치 파일 검증 실패");
    if(busy())throw new InvalidOperationException("게임 실행 중에는 설치하지 않습니다.");
    File.Move(fresh,engine);Record(release);return release.Tag+" · 설치 완료";
   }finally{if(File.Exists(fresh))File.Delete(fresh);}
  }
  string localHash=Hash(engine);
  if(localHash==release.Hash){Record(release);return release.Tag+" · 최신";}
  if(!install)return release.Tag+" 업데이트 가능";
  if(busy())return release.Tag+" · 종료 후 업데이트";
  if(!File.Exists(statePath))throw new InvalidDataException("설치 버전을 확인할 수 없어 자동 교체하지 않았습니다.");
  var state=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(statePath));
  Version installed;
  if(Convert.ToString(state["Hash"])!=localHash || !Version.TryParse(Convert.ToString(state["Version"]).TrimStart('v'),out installed))throw new InvalidDataException("설치 파일이 변경되어 자동 교체하지 않았습니다.");
  if(release.Version<=installed)return "설치 버전 유지";
  string staged=engine+".download";
  try {
   download(release.Url,staged,release.Size);
   InstallVerified(engine,staged,release.Hash,release.Size,localHash,busy);
   Record(release);
   return release.Tag+" · 업데이트 완료";
  }finally{if(File.Exists(staged))File.Delete(staged);}
 }
 internal static void InstallVerified(string engine,string staged,string expected,long size,string prior,Func<bool> busy) {
  if(new FileInfo(staged).Length!=size||Hash(staged)!=expected)throw new InvalidDataException("업데이트 파일 검증 실패");
  if(busy())throw new InvalidOperationException("게임 실행 중에는 업데이트하지 않습니다.");
  if(Hash(engine)!=prior)throw new IOException("설치 파일이 변경되었습니다.");
  string backup=engine+".previous";
  var attributes=File.GetAttributes(engine);
  File.Copy(engine,backup,true);
  try {
   File.SetAttributes(engine,attributes & ~FileAttributes.Hidden & ~FileAttributes.ReadOnly);
   File.Replace(staged,engine,null);
   if(Hash(engine)!=expected)throw new IOException("교체 후 검증 실패");
  }catch {
   if(Hash(engine)!=prior)File.Copy(backup,engine,true);
   throw;
  }finally{File.SetAttributes(engine,attributes);}
 }
}

internal enum GameLaunchPurpose { Player, Automation }

// Launcher UI and AI share validated configuration, channel and launch primitives.
internal sealed class LauncherOperations {
 readonly string root;readonly Func<bool> busy;
 static Dictionary<string,object> Preferences(string path){return File.Exists(path)?StoryClient.Read(path):new Dictionary<string,object>();}
 internal LauncherOperations(string root,Func<bool> busy=null){this.root=root;this.busy=busy??ProcessGuard.Busy;}
 internal Installation Installation(){string file=Path.Combine(root,"installation.json");if(!File.Exists(file))return null;var d=StoryClient.Read(file);string engine=CodexChat.S(d,"Engine");if(string.IsNullOrEmpty(engine))return null;return new Installation{Engine=engine,Data=string.IsNullOrEmpty(CodexChat.S(d,"Data"))?root:CodexChat.S(d,"Data")};}
 internal object Read(){var i=Installation();if(i==null||!File.Exists(i.Config))return new{configured=false,game_or_unlocker_running=busy(),windows_hdr=(object)null};var c=new ConfigStore(i.Config).Read();var hdr=new WindowsHdr().Read(Convert.ToInt32(UnlockerOptions.WithDefaults(c)["MonitorNum"]));var autoHdr=new WindowsAutoHdr().Read();var p=Preferences(Path.Combine(i.Data,"launcher-settings.json"));var channel=new ChannelService(Path.GetDirectoryName(Convert.ToString(c["GamePath"])));return new{configured=true,game_or_unlocker_running=busy(),fps=c["FPSTarget"],use_unlocker=!p.ContainsKey("UseUnlockerLaunch")||Equals(p["UseUnlockerLaunch"],true),channel=channel.Available?channel.Current():null,channel_switch_available=channel.Available,unlocker_hdr=c.ContainsKey("UseHDR")?c["UseHDR"]:null,windows_hdr=hdr,windows_hdr_control=true,windows_auto_hdr=autoHdr,engine_settings=c.Where(x=>UnlockerOptions.Defaults().ContainsKey(x.Key)).ToDictionary(x=>x.Key,x=>x.Value),preferences=p,display_presets=new DisplayPresets(root).Snapshot(UnlockerOptions.WithDefaults(c),hdr,autoHdr)};}
 internal static Dictionary<string,object> SaveSettings(Installation installation,Dictionary<string,object> edits,Dictionary<string,object> preferenceEdits){
  string file=Path.Combine(installation.Data,"launcher-settings.json");var prefs=Preferences(file);foreach(var entry in preferenceEdits)prefs[entry.Key]=entry.Value;
  if(edits.Count>0)new ConfigStore(installation.Config).Save(edits);
  if(preferenceEdits.Count>0)AtomicFile.Write(file,CatheryneTools.Json().Serialize(prefs));return prefs;
 }
 internal object Apply(string operation,int fps,bool enabled){
  var i=Installation();if(i==null||!File.Exists(i.Config))throw new InvalidOperationException("게임 설치 설정이 필요합니다.");
  if(busy())throw new InvalidOperationException("게임과 언락커를 종료한 뒤 변경해 주세요.");
  var store=new ConfigStore(i.Config);var c=store.Read();
  if(operation=="fps")SaveSettings(i,new Dictionary<string,object>{{"FPSTarget",fps}},new Dictionary<string,object>());
  else if(operation=="unlocker")SaveSettings(i,new Dictionary<string,object>(),new Dictionary<string,object>{{"UseUnlockerLaunch",enabled}});
  else if(operation=="google_channel"||operation=="original_channel")new ChannelService(Path.GetDirectoryName(Convert.ToString(c["GamePath"]))).Switch(operation=="google_channel",busy);
  else if(operation=="launch"){c=new DisplayPresets(root).Apply(UnlockerOptions.WithDefaults(c),true,next=>{SaveSettings(i,next.Where(x=>DisplayPresets.Fields.Contains(x.Key)).ToDictionary(x=>x.Key,x=>x.Value),new Dictionary<string,object>());return true;});var p=Preferences(Path.Combine(i.Data,"launcher-settings.json"));Start(i,c,!p.ContainsKey("UseUnlockerLaunch")||Equals(p["UseUnlockerLaunch"],true),GameLaunchPurpose.Automation);return new{launch_requested=true,display_preset="ai",game_completion_verified=false};}
  else throw new ArgumentException("지원하지 않는 런처 작업입니다.");
  return Read();
 }
 internal static void Start(Installation installation,Dictionary<string,object> config,bool useUnlocker,GameLaunchPurpose purpose,Action<ModProgress> progress=null){
  if(purpose==GameLaunchPurpose.Automation&&GameEnvironment.Remote(installation.Data)){GameEnvironment.Invoke(installation.Data,"game.launch",new Dictionary<string,object>{{"config",config},{"unlocker",useUnlocker}},true);return;}
  if(ProcessGuard.Busy())throw new InvalidOperationException("게임 또는 언락커가 이미 실행 중입니다.");

  string game=Convert.ToString(config["GamePath"]);if(!Setup.ValidGame(game))throw new FileNotFoundException("원신 실행 파일을 확인할 수 없습니다.");
  if(ModIntegration.TryStart(installation.Data,config,useUnlocker,progress:progress))return;
  if(useUnlocker&&!Setup.HasDesktopRuntime())throw new InvalidOperationException("언락커 실행에 .NET 8 Desktop Runtime이 필요합니다.");
  string executable=useUnlocker?installation.Engine:game;if(!File.Exists(executable))throw new FileNotFoundException("실행 구성요소를 설치해 주세요.");
  if(useUnlocker){UnlockerSession.Start(installation.Data);return;}
  var start=new ProcessStartInfo(executable,UnlockerOptions.GameArguments(UnlockerOptions.WithDefaults(config))){WorkingDirectory=Path.GetDirectoryName(executable),UseShellExecute=true};using(var process=Process.Start(start)){if(process==null)throw new InvalidOperationException("실행 요청이 전달되지 않았습니다.");}
 }
}

// All external HTTP callers establish TLS before their first request, including CLI jobs.
internal static class HttpTransport {
 static HttpTransport(){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;}
 internal static HttpWebRequest Create(string url){return Create(new Uri(url));}
 internal static HttpWebRequest Create(Uri url){return (HttpWebRequest)WebRequest.Create(url);}
}

// The elevated owner outlives the launcher UI and only closes the process it created.
internal static class UnlockerSession {
 static string Receipt(string root,string id){return Path.Combine(root,"cache","launch",id+".json");}
 internal static void Start(string root){
  root=NativePaths.Resolve(root);string id=Guid.NewGuid().ToString("N"),receipt=Receipt(root,id);
  Directory.CreateDirectory(Path.GetDirectoryName(receipt));
  try {
   using(var worker=Process.Start(new ProcessStartInfo(NativePaths.Resolve(System.Reflection.Assembly.GetExecutingAssembly().Location),"--unlocker-session "+StoryClient.Quote(root)+" "+id){UseShellExecute=true,Verb=AppRuntime.Elevated()?"":"runas",WindowStyle=ProcessWindowStyle.Hidden})){
    if(worker==null)throw new InvalidOperationException("실행 요청이 전달되지 않았습니다.");
    var wait=Stopwatch.StartNew();
    while(!File.Exists(receipt)&&!worker.HasExited&&wait.Elapsed<TimeSpan.FromSeconds(15))System.Threading.Thread.Sleep(50);
    if(!File.Exists(receipt))throw new IOException("언락커 실행 결과를 확인하지 못했습니다.");
    var result=StoryClient.Read(receipt);
    if(!Equals(result["started"],true))throw new InvalidOperationException(CodexChat.S(result,"error"));
   }
  }finally{if(File.Exists(receipt))File.Delete(receipt);}
 }
 internal static int Run(string root,string id){
  Guid parsed;if(!Guid.TryParseExact(id,"N",out parsed))return 1;
  string receipt=Receipt(root,id),journal=Path.Combine(root,"launch-session.json");bool acknowledged=false,ownsSession=false;
  Action<string,string> record=(state,error)=>AtomicFile.Write(journal,CatheryneTools.Json().Serialize(new{session_id=id,state=state,error=error,updated_at=DateTime.UtcNow.ToString("o"),fps_verified=false}));
  try{
   Directory.CreateDirectory(Path.GetDirectoryName(receipt));
   bool first;
   using(var gate=new System.Threading.Mutex(true,"Local\\Catheryne.UnlockerSession",out first)){
    if(!first||ProcessGuard.Busy())throw new InvalidOperationException("게임 또는 언락커가 이미 실행 중입니다.");
    ownsSession=true;
    var installation=new LauncherOperations(root).Installation();
    if(installation==null)throw new InvalidOperationException("게임 설치 설정이 필요합니다.");
    string engine=NativePaths.Resolve(installation.Engine);
    string game=NativePaths.Resolve(Convert.ToString(new ConfigStore(installation.Config).Read()["GamePath"]));
    if(!Setup.ValidGame(game)||!File.Exists(engine))throw new FileNotFoundException("게임과 언락커 설치 경로를 확인해 주세요.");
    string name=Path.GetFileName(engine);
    if(!name.Equals("unlockfps_nc.exe",StringComparison.OrdinalIgnoreCase)&&!name.Equals("unlockfps_nc_signed.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("등록된 언락커 실행 파일을 확인해 주세요.");
    DateTime began=DateTime.UtcNow;
    using(var owned=Process.Start(new ProcessStartInfo(engine){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(engine)})){
     if(owned==null)throw new IOException("언락커가 실행되지 않았습니다.");
     try{record("launch_requested","");
     AtomicFile.Write(receipt,CatheryneTools.Json().Serialize(new{started=true}));acknowledged=true;}catch{StopOwned(owned);throw;}
     string outcome=Monitor(owned,()=>FindGame(game,began),TimeSpan.FromMinutes(5),()=>record("game_running",""));
     record(outcome,"");return outcome=="game_start_timeout"?1:0;
    }
   }
  }catch(Exception error){
   try{if(ownsSession)record("failed",error.Message);if(!acknowledged)AtomicFile.Write(receipt,CatheryneTools.Json().Serialize(new{started=false,error=error.Message}));}catch(Exception logError){Trace.TraceError(logError.ToString());}
   return 1;
  }
 }
 static Process FindGame(string executable,DateTime began){
  var candidates=Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));Process selected=null;
  try{
   foreach(var process in candidates){
    try{if(GameEnvironment.OwnsProcess(process.Id)&&process.StartTime.ToUniversalTime()>=began.AddSeconds(-1)&&string.Equals(process.MainModule.FileName,executable,StringComparison.OrdinalIgnoreCase)){selected=process;break;}}
    catch(InvalidOperationException){}
   }
   return selected;
  }finally{foreach(var process in candidates)if(process!=selected)process.Dispose();}
 }
 static void StopOwned(Process owned){if(!owned.HasExited){owned.Kill();if(!owned.WaitForExit(5000))throw new IOException("남은 언락커의 종료를 확인하지 못했습니다.");}}
 internal static string Monitor(Process owned,Func<Process> findGame,TimeSpan timeout,Action observed){
  var wait=Stopwatch.StartNew();Process game=null;
  try{
   while(!owned.HasExited){
    if(game==null){
     game=findGame();
     if(game!=null)observed();
     else if(wait.Elapsed>=timeout)return "game_start_timeout";
    }
    if(game!=null&&game.HasExited)return "game_exited";
    owned.WaitForExit(250);
   }
   return "unlocker_exited";
  }finally{
   if(game!=null)game.Dispose();
   // Keep the original Process handle: never re-open or terminate by PID/name.
   StopOwned(owned);
  }
 }
}