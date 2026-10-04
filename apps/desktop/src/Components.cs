using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Collections;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

internal sealed class ComponentSpec {
 internal readonly string Id,Title,Description;
 internal readonly Func<bool> Available;
 internal readonly Action Prepare;
 internal string Website {get{if(ExternalTools.Catalog.Any(x=>x.Id==Id))return ExternalTools.Spec(Id).Website;return Id=="xxmi"?"https://github.com/SpectrumQT/XXMI-Launcher":Id=="kamera"?"https://github.com/taiwenlee/Inventory_Kamera":Id=="scanner"?"https://github.com/akrios-d/AkashaScanner":Id=="ai"?"https://github.com/modelcontextprotocol/python-sdk":"https://developer.microsoft.com/microsoft-edge/webview2/";}}
 internal string Version {get{if(ExternalTools.Catalog.Any(x=>x.Id==Id))return ExternalTools.Spec(Id).Version;return Id=="xxmi"?"XXMI 2.2.1":Id=="kamera"?Components.KameraVersion:Id=="scanner"?Components.ScannerVersion:Id=="ai"?"Python 3.13.15 / "+Components.McpRequirement():Components.WebViewVersion();}}
 internal ComponentSpec(string id,string title,string description,Func<bool> available,Action prepare){Id=id;Title=title;Description=description;Available=available;Prepare=prepare;}
}
internal static class Components {
 const string KameraRevision="9", ScannerRevision="10";
 internal static string KameraVersion {get{return "Kamera · Catheryne "+KameraRevision;}}
 internal static string Kamera {get{return Path.Combine(Setup.DataFolder,"components","kamera","catheryne-"+KameraRevision,"InventoryKamera.exe");}}
 internal static void PrepareKamera(){if(File.Exists(Kamera))return;string source=Path.Combine(Root,"integrations","kamera");if(!File.Exists(Path.Combine(source,"InventoryKamera.exe")))throw new InvalidOperationException("Kamera 구성요소가 없습니다.");string destination=Path.GetDirectoryName(Kamera);WithStage(destination,stage=>{CopyScannerBundle(source,stage);Directory.Move(stage,destination);});}
 internal static string Root {get{return AppDomain.CurrentDomain.BaseDirectory;}}
 internal static Dictionary<string,object> Manifest {get{var manifest=StoryClient.Read(Path.Combine(Root,"components.json"));object selected;if(AppPreferences.Read().TryGetValue("scannerApprovedRelease",out selected)&&selected is Dictionary<string,object>)manifest=(Dictionary<string,object>)selected;ValidateRelease(manifest);return manifest;}}
 internal static void ValidateRelease(Dictionary<string,object> release){
  string version=Convert.ToString(release["scannerVersion"]),hash=Convert.ToString(release["scannerSha256"]);Uri url;
  if(!Regex.IsMatch(version,@"^\d+\.\d+\.\d+$")||!Regex.IsMatch(hash,@"^[a-fA-F0-9]{64}$")||!Uri.TryCreate(Convert.ToString(release["scannerUrl"]),UriKind.Absolute,out url)||url.Scheme!="https"||url.Host!="github.com"||url.UserInfo.Length!=0||!url.AbsolutePath.StartsWith("/akrios-d/AkashaScanner/releases/download/"))throw new InvalidDataException("스캐너 버전 정보가 올바르지 않습니다.");
 }
 internal static Dictionary<string,object> LatestScanner(){
  var request=HttpTransport.Create("https://api.github.com/repos/akrios-d/AkashaScanner/releases/latest");request.UserAgent="Catheryne";request.Timeout=15000;
  using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){
   var release=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
   foreach(Dictionary<string,object> asset in (IEnumerable)release["assets"]){if(!Convert.ToString(asset["name"]).EndsWith(".zip",StringComparison.OrdinalIgnoreCase))continue;string digest=asset.ContainsKey("digest")?Convert.ToString(asset["digest"]):"";if(!digest.StartsWith("sha256:"))continue;var selected=new Dictionary<string,object>{{"scannerVersion",Convert.ToString(release["tag_name"]).TrimStart('v')},{"scannerUrl",asset["browser_download_url"]},{"scannerSha256",digest.Substring(7)}};ValidateRelease(selected);return selected;}
   throw new InvalidDataException("검증 가능한 최신 배포 파일이 없습니다.");
  }
 }
 internal static string BundledScanner {get{return Path.Combine(Root,"integrations","scanner","AkashaScanner.exe");}}
 internal static bool UseBundledScanner {get{object selected;return File.Exists(BundledScanner)&&(!AppPreferences.Read().TryGetValue("scannerApprovedRelease",out selected)||selected==null);}}
 internal static string ScannerVersion {get{return UseBundledScanner?"Akasha 0.6.4 · Catheryne "+ScannerRevision:"Akasha "+Convert.ToString(Manifest["scannerVersion"]);}}
 internal static string Scanner {get{return Path.Combine(Setup.DataFolder,"components","scanner",UseBundledScanner?"catheryne-"+ScannerRevision:Convert.ToString(Manifest["scannerVersion"]),"AkashaScanner.exe");}}
 internal static void CopyScannerBundle(string source,string destination){
  Directory.CreateDirectory(destination);
  foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){
   string relative=file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
   string[] parts=relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
   if(parts.Any(p=>new[]{"ScannedData","GenshinDatabase","logs","webview","x86","win-x86"}.Contains(p,StringComparer.OrdinalIgnoreCase))||new[]{"config.json","scan-diagnostic.txt"}.Contains(Path.GetFileName(file),StringComparer.OrdinalIgnoreCase)||new[]{".log",".tmp"}.Contains(Path.GetExtension(file),StringComparer.OrdinalIgnoreCase))continue;
   string target=SafeArchivePath(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target,false);
  }
 }

 // A staging directory belongs to this preparation only; never sweep version
 // folders, which can contain user settings and scan exports.
 internal static void WithStage(string destination,Action<string> prepare){
  string stage=Path.GetFullPath(destination)+"-stage-"+Guid.NewGuid().ToString("N");
  Directory.CreateDirectory(stage);
  try{prepare(stage);}finally{
   try{if(Directory.Exists(stage))Directory.Delete(stage,true);}
   catch(IOException error){AppDiagnostics.Record(DiagnosticEvent.ComponentCleanupFailure,error);}
   catch(UnauthorizedAccessException error){AppDiagnostics.Record(DiagnosticEvent.ComponentCleanupFailure,error);}
  }
 }

 internal static string SafeArchivePath(string root,string entry){string path=Path.GetFullPath(Path.Combine(root,entry));if(!path.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("압축 경로가 올바르지 않습니다.");return path;}
 internal static bool Busy(){return ProcessGuard.Busy()||CollectionScanner.Busy();}
 internal static void PrepareScanner(){
  if(File.Exists(Scanner)&&Setup.HasDesktopRuntime(ScannerRuntimeMajor()))return;
  if(CollectionScanner.Busy()||(ProcessGuard.Busy()&&(!UseBundledScanner||!Setup.HasDesktopRuntime(ScannerRuntimeMajor()))))throw new InvalidOperationException("게임과 스캐너를 종료한 뒤 준비해 주세요.");
  if(UseBundledScanner){
   string target=Path.GetDirectoryName(Scanner);
   if(!File.Exists(Scanner)){
    WithStage(target,stage=>{CopyScannerBundle(Path.GetDirectoryName(BundledScanner),stage);
     if(CollectionScanner.Busy())throw new InvalidOperationException("작업이 시작되어 교체를 중단했습니다.");Directory.Move(stage,target);
    });
   }
   int required=ScannerRuntimeMajor();if(!Setup.HasDesktopRuntime(required))Install("Microsoft.DotNet.DesktopRuntime."+required);return;
  }
  var manifest=Manifest;string destination=Path.Combine(Setup.DataFolder,"components","scanner",Convert.ToString(manifest["scannerVersion"]),"AkashaScanner.exe");
  if(!File.Exists(destination)){
   string version=Convert.ToString(manifest["scannerVersion"]),url=Convert.ToString(manifest["scannerUrl"]);Uri uri=new Uri(url);if(uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/akrios-d/AkashaScanner/releases/download/"))throw new InvalidDataException("공식 스캐너 주소가 아닙니다.");
   string targetFolder=Path.GetDirectoryName(destination);
   WithStage(targetFolder,stage=>{
   string zip=Path.Combine(stage,"package.zip"),unpack=Path.Combine(stage,"unpack");

   var request=HttpTransport.Create(uri);request.Timeout=30000;request.ReadWriteTimeout=30000;using(var response=request.GetResponse())using(var input=response.GetResponseStream())using(var output=File.Create(zip))input.CopyTo(output);
   if(UpdateService.Hash(zip)!=Convert.ToString(manifest["scannerSha256"]).ToLowerInvariant())throw new InvalidDataException("스캐너 검증 해시가 일치하지 않습니다.");
   Directory.CreateDirectory(unpack);using(var archive=ZipFile.OpenRead(zip))foreach(var entry in archive.Entries){string target=SafeArchivePath(unpack,entry.FullName);if(string.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(target);continue;}Directory.CreateDirectory(Path.GetDirectoryName(target));entry.ExtractToFile(target);}
   string executable=Directory.GetFiles(unpack,"AkashaScanner.exe",SearchOption.AllDirectories).Single();
   if(Directory.Exists(targetFolder))throw new IOException("미완료된 동일 버전 폴더가 있습니다. 기존 기록을 보존하기 위해 교체를 중단했습니다.");
   if(Busy())throw new InvalidOperationException("작업이 시작되어 교체를 중단했습니다.");Directory.Move(Path.GetDirectoryName(executable),targetFolder);
   });
   // Version directories are retained, including any exports and user settings.
  }
  int runtime=ScannerRuntimeMajor(destination);if(!Setup.HasDesktopRuntime(runtime))Install("Microsoft.DotNet.DesktopRuntime."+runtime);
 }
 internal static int ScannerRuntimeMajor(string scanner=null){
  string path=Path.Combine(Path.GetDirectoryName(scanner??Scanner),"AkashaScanner.runtimeconfig.json");if(!File.Exists(path))return 6;
  var config=StoryClient.Read(path);var options=(Dictionary<string,object>)config["runtimeOptions"];foreach(Dictionary<string,object> framework in (IEnumerable)options["frameworks"])if(Convert.ToString(framework["name"])=="Microsoft.WindowsDesktop.App"){int major=int.Parse(Convert.ToString(framework["version"]).Split('.')[0]);if(!new[]{6,8,9,10}.Contains(major))throw new InvalidDataException("이 스캐너의 실행 환경은 지원하지 않습니다.");return major;}throw new InvalidDataException("스캐너 실행 환경 정보가 없습니다.");
 }
 internal static string RequirementsHash(){string path=Path.Combine(Root,"integrations","ai","requirements.txt");return File.Exists(path)?UpdateService.Hash(path):"";}
 internal static void Prepare(ComponentSpec spec){if(spec.Available()){Remember(spec);return;}using(var mutex=new System.Threading.Mutex(false,"Local\\Catheryne.Components")){bool locked=false;try{try{locked=mutex.WaitOne(0);}catch(System.Threading.AbandonedMutexException){locked=true;}if(!locked)throw new InvalidOperationException("다른 구성요소 준비가 진행 중입니다.");if(CollectionScanner.Busy()||(ProcessGuard.Busy()&&!(spec.Id=="kamera"||(spec.Id=="scanner"&&UseBundledScanner&&Setup.HasDesktopRuntime(ScannerRuntimeMajor())))))throw new InvalidOperationException("게임 또는 스캐너가 실행 중입니다.");spec.Prepare();if(!spec.Available())throw new IOException("구성요소 준비를 확인하지 못했습니다.");Remember(spec);}finally{if(locked)mutex.ReleaseMutex();}}}
 internal static int Command(string id){try{var spec=Catalog().Single(c=>c.Id==id);Prepare(spec);AtomicFile.Write(Path.Combine(Setup.DataFolder,"components","last-command.txt"),id+": ready");return 0;}catch(Exception error){Directory.CreateDirectory(Path.Combine(Setup.DataFolder,"components"));AtomicFile.Write(Path.Combine(Setup.DataFolder,"components","last-command.txt"),id+": "+error.Message);return 1;}}
 internal static void Remember(ComponentSpec spec){string folder=Path.Combine(Setup.DataFolder,"components");Directory.CreateDirectory(folder);AtomicFile.Write(Path.Combine(folder,spec.Id+"-selected"),"1");}
 internal static void Maintain(){
  if(!AppPreferences.Flag("componentsAutoUpdate",true)||Busy())return;
  string stamp=Path.Combine(Setup.DataFolder,"components","last-check");if(File.Exists(stamp)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(stamp)<TimeSpan.FromDays(1))return;
  foreach(var spec in Catalog()){if(Busy())return;if(!File.Exists(Path.Combine(Setup.DataFolder,"components",spec.Id+"-selected")))continue;try{if(!spec.Available())Prepare(spec);}catch(Exception error){string folder=Path.Combine(Setup.DataFolder,"components");Directory.CreateDirectory(folder);AtomicFile.Write(Path.Combine(folder,spec.Id+"-last-error.txt"),DateTime.UtcNow.ToString("o")+"\n"+error.Message);}}
  Directory.CreateDirectory(Path.GetDirectoryName(stamp));AtomicFile.Write(stamp,DateTime.UtcNow.ToString("o"));
 }
 internal static string McpRequirement(){string path=Path.Combine(Root,"integrations","ai","requirements.txt");return File.Exists(path)?File.ReadAllText(path).Trim():Locale.T("MCP · 패키지 파일 없음");}
 internal static string WebViewVersion(){try{return CoreWebView2Environment.GetAvailableBrowserVersionString();}catch{return "WebView2 · 설치 필요";}}
 internal static bool ManagedProcessRunning(string folder){foreach(var process in Process.GetProcesses())using(process)try{string path=process.MainModule.FileName;if(path.StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return true;}catch{}return false;}
 internal static void Remove(ComponentSpec spec){
  if(spec.Id=="hutao")throw new InvalidOperationException("후타오는 Windows 설치된 앱에서 제거해 주세요.");
  if(spec.Id=="bettergi"&&!string.IsNullOrEmpty(CodexChat.S(AppPreferences.Read(),"externalPath.bettergi")))throw new InvalidOperationException("연결한 외부 설치는 원래 설치 위치에서 관리해 주세요.");
  if(spec.Id=="xxmi"&&!string.IsNullOrEmpty(CodexChat.S(AppPreferences.Read(),"xxmiPath")))throw new InvalidOperationException("연결한 외부 설치는 원래 설치 위치에서 관리해 주세요.");
  string root=Path.Combine(Setup.DataFolder,"components"),folder=ExternalTools.Catalog.Any(x=>x.Id==spec.Id)?new ExternalTools(Setup.DataFolder).Folder(spec.Id):spec.Id=="xxmi"?ModIntegration.ManagedRoot(Setup.DataFolder):spec.Id=="kamera"?Path.GetDirectoryName(Kamera):spec.Id=="scanner"?Path.GetDirectoryName(Scanner):spec.Id=="ai"?Path.Combine(root,"ai"):null;
  if(folder==null)throw new InvalidOperationException("공유 실행 환경은 Windows 설치된 앱에서 관리합니다.");
  if(Busy()||(spec.Id=="bettergi"&&ExternalTools.BetterGiRunning())||ManagedProcessRunning(folder))throw new InvalidOperationException("구성요소가 사용 중입니다. 관련 작업을 종료한 뒤 다시 시도해 주세요.");
  if(!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("관리 폴더 밖의 경로입니다.");
  if(Directory.Exists(folder)){string archives=Path.Combine(root,"archives");Directory.CreateDirectory(archives);Directory.Move(folder,Path.Combine(archives,spec.Id+"-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N")));}
  File.Delete(Path.Combine(root,spec.Id+"-selected"));
  if(spec.Id=="ai"){File.Delete(Path.Combine(root,"ai-ready.json"));var client=new StoryClient(Setup.DataFolder);var config=client.Config();object python;if(config.TryGetValue("story_python",out python)&&Convert.ToString(python).StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){config.Remove("story_python");client.Save(config);}File.Delete(Path.Combine(Setup.DataFolder,"mcp-client-config.json"));}
 }
 internal static bool WebViewReady(){try{return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());}catch{return false;}}
 internal static bool AiReady(){if(!File.Exists(Path.Combine(Setup.DataFolder,"components","ai","Scripts","python313._pth")))return false;var config=new StoryClient(Setup.DataFolder).Config();return config.ContainsKey("story_python")&&File.Exists(Convert.ToString(config["story_python"]))&&File.Exists(Path.Combine(Setup.DataFolder,"components","ai-ready.json"))&&File.ReadAllText(Path.Combine(Setup.DataFolder,"components","ai-ready.json"))==RequirementsHash()&&File.Exists(Path.Combine(Root,"core","story-control","src","story_control","__main__.py"));}
 internal static void Run(string executable,string arguments,int timeout=600000){
  using(var process=Process.Start(new ProcessStartInfo(executable,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){
   var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
   if(!process.WaitForExit(timeout)){try{process.Kill();}catch{}throw new IOException("구성요소 준비 시간이 초과되었습니다. 상태를 다시 확인해 주세요.");}
   Task.WaitAll(output,error);if(process.ExitCode!=0)throw new IOException("구성요소 설치 실패 ("+process.ExitCode+"). 네트워크와 Windows 설치 승인 여부를 확인한 뒤 다시 시도해 주세요.");
  }
 }
 static void Install(string id){Run("winget.exe","install --id "+id+" --exact --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity");}
 static void PrepareAi(){
  string source=Path.Combine(Root,"core","story-control","src","story_control","__main__.py"),requirements=Path.Combine(Root,"integrations","ai","requirements.txt");
  if(!File.Exists(source)||!File.Exists(requirements))throw new FileNotFoundException("실행 파일이 누락됐습니다. 캣서린 설치 프로그램으로 복구해 주세요.");
  string bundle=Path.Combine(Root,"integrations","runtime"),environment=Path.Combine(Setup.DataFolder,"components","ai"),scripts=Path.Combine(environment,"Scripts"),managed=Path.Combine(scripts,"python.exe");
  if(!File.Exists(Path.Combine(bundle,"python.exe"))||!File.Exists(Path.Combine(bundle,"requirements.sha256")))throw new FileNotFoundException("실행 환경이 누락됐습니다. 캣서린 설치 프로그램으로 복구해 주세요.");
  if(File.ReadAllText(Path.Combine(bundle,"requirements.sha256")).Trim()!=RequirementsHash())throw new InvalidDataException("앱과 실행 환경의 버전이 일치하지 않습니다.");
  if(ManagedProcessRunning(environment))throw new InvalidOperationException("진행 중인 작업을 종료한 뒤 실행 환경을 준비해 주세요.");
  string staging=Path.Combine(Setup.DataFolder,"components","ai-stage-"+Guid.NewGuid().ToString("N")),backup=environment+"-previous";
  Directory.CreateDirectory(Path.Combine(staging,"Scripts"));
  try{
   foreach(string file in Directory.GetFiles(bundle,"*",SearchOption.AllDirectories)){string target=Path.Combine(staging,"Scripts",file.Substring(bundle.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target);}
   Run(Path.Combine(staging,"Scripts","python.exe"),"-I -c \"import mcp; import ctypes; import sqlite3; import PIL; import numpy\"",15000);
   if(Directory.Exists(backup))Directory.Delete(backup,true);
   if(Directory.Exists(environment))Directory.Move(environment,backup);
   try{Directory.Move(staging,environment);}catch{if(Directory.Exists(backup))Directory.Move(backup,environment);throw;}
   if(Directory.Exists(backup))Directory.Delete(backup,true);
  }finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
  var client=new StoryClient(Setup.DataFolder);var config=client.Config();config["story_python"]=managed;client.Save(config);
  AtomicFile.Write(Path.Combine(Setup.DataFolder,"components","ai-ready.json"),RequirementsHash());
  var json=new System.Web.Script.Serialization.JavaScriptSerializer();
  AtomicFile.Write(Path.Combine(Setup.DataFolder,"mcp-client-config.json"),json.Serialize(new {mcpServers=new Dictionary<string,object>{{"catheryne",new{command=System.Reflection.Assembly.GetExecutingAssembly().Location,args=new[]{"--mcp-server"}}}}}));
 }
 internal static ComponentSpec[] Catalog(){return new[]{
  new ComponentSpec("xxmi","모드 실행 환경","XXMI Launcher · GIMI",()=>ModIntegration.Ready(Setup.DataFolder),()=>ModIntegration.EnsureRuntime(Setup.DataFolder)),
  new ComponentSpec("bettergi","반복 작업","BetterGI 기반 반복 작업 실행",()=>new ExternalTools(Setup.DataFolder).Ready("bettergi"),()=>new ExternalTools(Setup.DataFolder).Install("bettergi")),
  new ComponentSpec("hutao","육성 자료","Snap Hutao Remastered 기반 육성 자료 연결",()=>new ExternalTools(Setup.DataFolder).Ready("hutao"),()=>new ExternalTools(Setup.DataFolder).Install("hutao")),
  new ComponentSpec("kamera","캐릭터와 장비 스캐너","Inventory Kamera",()=>File.Exists(Kamera),PrepareKamera),
  new ComponentSpec("scanner","업적 스캐너","Akasha Scanner와 실행에 필요한 .NET을 준비합니다.",()=>File.Exists(Scanner)&&Setup.HasDesktopRuntime(ScannerRuntimeMajor()),PrepareScanner),
  new ComponentSpec("ai","스토리·AI 실행 환경","Python과 MCP를 준비합니다. 사용하는 AI에 연결하는 단계는 별도입니다.",AiReady,PrepareAi),
  new ComponentSpec("hoyolab","HoYoLAB 로그인 환경","WebView2를 준비합니다. 계정 로그인은 일상 메뉴에서 진행합니다.",WebViewReady,()=>{if(!WebViewReady())Install("Microsoft.EdgeWebView2Runtime");if(!WebViewReady())throw new IOException("로그인 환경 준비를 확인하지 못했습니다.");})
 };}
 internal static void UnlockerRuntime(){if(!Setup.HasDesktopRuntime())Install("Microsoft.DotNet.DesktopRuntime.8");if(!Setup.HasDesktopRuntime())throw new IOException("언락커 실행 환경을 확인하지 못했습니다.");}
}

internal static class ComponentTests {
 internal static void Run(){
  var release=new Dictionary<string,object>{{"scannerVersion","0.6.4"},{"scannerSha256",new string('a',64)},{"scannerUrl","https://github.com/akrios-d/AkashaScanner/releases/download/v0.6.4/tool.zip"}};Components.ValidateRelease(release);
  release["scannerVersion"]="../escape";bool rejected=false;try{Components.ValidateRelease(release);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("Unsafe version accepted");
  string root=Path.Combine(Path.GetTempPath(),"catheryne-component-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   rejected=false;try{Components.SafeArchivePath(root,"../escape");}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("Archive traversal accepted");
   string bundle=Path.Combine(root,"bundle"),installed=Path.Combine(root,"installed");Directory.CreateDirectory(Path.Combine(bundle,"ScannedData"));File.WriteAllText(Path.Combine(bundle,"AkashaScanner.exe"),"test");File.WriteAllText(Path.Combine(bundle,"config.json"),"private");File.WriteAllText(Path.Combine(bundle,"ScannedData","record.json"),"private");Components.CopyScannerBundle(bundle,installed);if(!File.Exists(Path.Combine(installed,"AkashaScanner.exe"))||File.Exists(Path.Combine(installed,"config.json"))||Directory.Exists(Path.Combine(installed,"ScannedData")))throw new Exception("Scanner bundle copied runtime data");
   foreach(string arch in new[]{"win-x86","win-x64"}){string native=Path.Combine(bundle,"runtimes",arch,"native");Directory.CreateDirectory(native);File.WriteAllText(Path.Combine(native,"library.dll"),"test");}
   Components.CopyScannerBundle(bundle,Path.Combine(root,"filtered"));if(Directory.Exists(Path.Combine(root,"filtered","runtimes","win-x86"))||!File.Exists(Path.Combine(root,"filtered","runtimes","win-x64","native","library.dll")))throw new Exception("Scanner architecture filtering failed");
   string staged=null;rejected=false;try{Components.WithStage(Path.Combine(root,"failed"),stage=>{staged=stage;File.WriteAllText(Path.Combine(stage,"package.zip"),"partial");throw new InvalidDataException("fixture");});}catch(InvalidDataException){rejected=true;}
   if(!rejected||Directory.Exists(staged))throw new Exception("Failed preparation retained staging files");
   string prepared=Path.Combine(root,"prepared");Components.WithStage(prepared,stage=>{File.WriteAllText(Path.Combine(stage,"tool.exe"),"test");Directory.Move(stage,prepared);});if(!File.Exists(Path.Combine(prepared,"tool.exe")))throw new Exception("Staging cleanup removed the installed tool");
   string game=Path.Combine(root,"GenshinImpact.exe");File.WriteAllText(game,"");Directory.CreateDirectory(Path.Combine(root,"GenshinImpact_Data"));
   string engine=Path.Combine(root,"engine","unlockfps_nc.exe");Directory.CreateDirectory(Path.GetDirectoryName(engine));
   var installation=Setup.SaveInstallation(engine,root,Path.Combine(root,"installation.json"),game);
   AtomicFile.Write(Path.Combine(root,"launcher-settings.json"),"{\"UseUnlockerLaunch\":false}");if(!Setup.Ready(installation))throw new Exception("Direct launch requires optional unlocker");
   AtomicFile.Write(Path.Combine(root,"launcher-settings.json"),"{\"UseUnlockerLaunch\":true}");if(Setup.Ready(installation))throw new Exception("Missing selected unlocker accepted");
  }finally{Directory.Delete(root,true);}
 }
}
