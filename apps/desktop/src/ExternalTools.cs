using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Text;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

// One adapter catalogue for human controls, chat tools and component installation.
internal sealed class ExternalToolSpec {
 internal string Id,Title,Version,Repository,Asset,Hash,Executable;
 internal string Website {get{return "https://github.com/"+Repository;}}
 internal string Url {get{return Website+"/releases/download/"+(Id=="hutao"?"v":"")+Version+"/"+Asset;}}
}
internal sealed class BuiltinAutomation {
 internal readonly string Title,EntryPoint,Prerequisites,Resources,Script;
 internal readonly bool ObservationOnly;
 internal readonly int MinimumHeight;
 internal BuiltinAutomation(string title,string entryPoint,string prerequisites,string resources,string script,bool observationOnly=false,int minimumHeight=0){MinimumHeight=minimumHeight;ObservationOnly=observationOnly;Title=title;EntryPoint=entryPoint;Prerequisites=prerequisites;Resources=resources;Script=script;}
}
internal sealed class ExternalTools {
 readonly string root;
 internal static void ExtractArchive(string root,string package,string stage,long maxBytes=0){
  string runtime=Path.Combine(root,"components","archive-runtime");Directory.CreateDirectory(runtime);
  string[] hashes={"ce80ff25847ee1e927de031fdf5359ea9bb99cd251ee1a8b2cbb49160286d307","51ab8a0f4be6ba6cca7f651e1caa4c7a16496460b90570a495c9b5322055ebde"};
  int index=0;foreach(string name in new[]{"7z.exe","7z.dll"}){string binary=Path.Combine(runtime,name);string hash=hashes[index++];if(!File.Exists(binary)||UpdateService.Hash(binary)!=hash){using(var client=new WebClient())client.DownloadFile("https://raw.githubusercontent.com/babalae/better-genshin-impact/0af68c2/Build/MicaSetup.Tools/7-Zip/"+name,binary);}if(UpdateService.Hash(binary)!=hash)throw new InvalidDataException("압축 해제 도구의 해시가 일치하지 않습니다.");}
  string archiveTool=Path.Combine(runtime,"7z.exe");
  string listing=RunProcess(archiveTool,"l -slt -ba -sccUTF-8 "+StoryClient.Quote(package));
  foreach(string entry in listing.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){if(entry.StartsWith("Path = "))Components.SafeArchivePath(stage,entry.Substring(7));if(entry.StartsWith("Symbolic Link = ")||entry.StartsWith("Hard Link = "))throw new InvalidDataException("링크가 포함된 압축 파일은 지원하지 않습니다.");}
  if(maxBytes>0){long total=0;int files=0;foreach(string line in listing.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries))if(line.StartsWith("Size = ")){long size;if(!long.TryParse(line.Substring(7),out size)||size<0||++files>20000||size>maxBytes-total)throw new InvalidDataException("압축 파일 크기가 너무 큽니다.");total+=size;}}
  RunProcess(archiveTool,"x -y -sccUTF-8 -o"+StoryClient.Quote(stage)+" "+StoryClient.Quote(package));
 }
 internal ExternalTools(string root){this.root=root;}
 internal static readonly ExternalToolSpec[] Catalog={
  new ExternalToolSpec{Id="bettergi",Title="BetterGI",Version="0.66.0",Repository="babalae/better-genshin-impact",Asset="BetterGI_v0.66.0.7z",Hash="28d4756e55f32637538bf84427f5f31afd5b153bf996530c5975bd9c8c0b9343",Executable="BetterGI.exe"},
  new ExternalToolSpec{Id="hutao",Title="Snap Hutao Remastered",Version="1.20.3",Repository="SnapHutaoRemasteringProject/Snap.Hutao.Remastered",Asset="Snap.Hutao.Remastered-1.20.3.0-Setup.exe",Hash="12c608592417f2fb14c8158b315fff3b5157ef93e61e0b04e9b4c468a02422c5",Executable="Snap.Hutao.Remastered.exe"}
 };
 internal static ExternalToolSpec Spec(string id){var s=Catalog.SingleOrDefault(x=>x.Id==id);if(s==null)throw new ArgumentException("도구를 확인해 주세요.");return s;}
 internal string Folder(string id){var s=Spec(id);return Path.Combine(root,"components",s.Id,s.Version);}
 internal string PathFor(string id){var s=Spec(id);string selected=CodexChat.S(AppPreferences.Read(root),"externalPath."+id);if(!string.IsNullOrEmpty(selected))return NativePaths.Resolve(selected);return NativePaths.Resolve(Path.Combine(Folder(id),s.Executable));}
 internal bool Ready(string id){return File.Exists(PathFor(id));}
 internal static bool BetterGiRunning(){return ProcessGuard.Running("BetterGI");}
 internal void Connect(string id,string path){var s=Spec(id);if(!File.Exists(path)||!string.Equals(Path.GetFileName(path),s.Executable,StringComparison.OrdinalIgnoreCase))throw new ArgumentException(s.Executable+" 파일을 선택해 주세요.");AppPreferences.Set("externalPath."+id,Path.GetFullPath(path),root);}
 static string RunProcess(string file,string args,int timeout=600000){
  using(var p=Process.Start(new ProcessStartInfo(file,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8})){
   var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();
   if(!p.WaitForExit(timeout))throw new IOException("도구 준비가 아직 진행 중입니다. 실행 중인 설치 프로그램을 확인해 주세요.");
   Task.WaitAll(output,error);if(p.ExitCode!=0)throw new IOException("도구 준비에 실패했습니다 ("+p.ExitCode+"). "+error.Result.Substring(0,Math.Min(400,error.Result.Length)));return output.Result;
  }
 }
 internal void Install(string id){
  var s=Spec(id);if(Ready(id))return;
  string folder=Folder(id);if(ProcessGuard.Running(Path.GetFileNameWithoutExtension(s.Executable)))throw new InvalidOperationException("도구를 종료한 뒤 준비해 주세요.");
  string cache=Path.Combine(root,"components","downloads");Directory.CreateDirectory(cache);string package=Path.Combine(cache,s.Asset);
  if(!File.Exists(package)||UpdateService.Hash(package)!=s.Hash){
   string part=package+"."+Guid.NewGuid().ToString("N")+".part";
   try{
   var request=HttpTransport.Create(s.Url);request.UserAgent="Catheryne";request.Timeout=30000;request.ReadWriteTimeout=30000;
   using(var response=request.GetResponse())using(var input=response.GetResponseStream())using(var output=File.Create(part))input.CopyTo(output);
   if(UpdateService.Hash(part)!=s.Hash)throw new InvalidDataException("공식 배포 파일의 검증 해시가 일치하지 않습니다.");
   if(File.Exists(package))File.Delete(package);File.Move(part,package);
   }finally{try{if(File.Exists(part))File.Delete(part);}catch(IOException error){AppDiagnostics.Record(DiagnosticEvent.ComponentCleanupFailure,error);}catch(UnauthorizedAccessException error){AppDiagnostics.Record(DiagnosticEvent.ComponentCleanupFailure,error);}}
  }
  Directory.CreateDirectory(Path.GetDirectoryName(folder));
  if(id=="bettergi"){
   Components.WithStage(folder,stage=>{
   ExtractArchive(root,package,stage);
   string executable=Directory.GetFiles(stage,s.Executable,SearchOption.AllDirectories).Single();
   if(Directory.Exists(folder))throw new IOException("기존 설치 폴더가 있습니다. 기존 설치 연결로 확인해 주세요.");
   Directory.Move(Path.GetDirectoryName(executable),folder);
   });
  }else{
   using(var installer=Process.Start(new ProcessStartInfo(NativePaths.Resolve(package),"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /TASKS=\"\" /DIR="+StoryClient.Quote(folder)){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(NativePaths.Resolve(package))})){
    if(!installer.WaitForExit(600000))throw new IOException("후타오 설치 완료를 기다리고 있습니다.");
    if(installer.ExitCode!=0)throw new IOException("후타오 설치가 완료되지 않았습니다 ("+installer.ExitCode+").");
   }
  }
  if(!Ready(id))throw new IOException("설치된 실행 파일을 확인하지 못했습니다.");
  AtomicFile.Write(Path.Combine(folder,"catheryne-release.json"),CatheryneTools.Json().Serialize(new{version=s.Version,url=s.Url,sha256=s.Hash}));
 }
 internal object Status(string id){
  var s=Spec(id);string path=PathFor(id);bool ready=File.Exists(path);string version=ready?FileVersionInfo.GetVersionInfo(path).ProductVersion:null;
  bool running=ready&&ProcessGuard.Running(Path.GetFileNameWithoutExtension(s.Executable));
  return new{id=id,title=s.Title,installed=ready,running=running,version=string.IsNullOrEmpty(CodexChat.S(AppPreferences.Read(root),"externalPath."+id))?s.Version:version,binary_version=version,compatible_version=s.Version,path=ready?path:null,website=s.Website,capabilities=id=="bettergi"?new[]{"open","groups","run_group","stop_owned_run","progress"}:new[]{"open","cultivation_read"}};
 }
 internal object[] Status(){return Catalog.Select(s=>Status(s.Id)).ToArray();}
 void CheckInputAvailable(bool validateScreen=false,AiTaskRecord delegated=null){
  if(validateScreen)new GameTools(root).RequireAspect(16,9,RequiredHeight(delegated==null?null:delegated.ExecutionPlan));
  if(delegated!=null&&!string.IsNullOrEmpty(delegated.ParentTaskId))RequireReservation(delegated);
  else GameInputLease.CheckAvailable(root);
 }
 void RequireReservation(AiTaskRecord task){
  var status=new StoryClient(root).Call("status",new{after_sequence=0},true);
  if(CodexChat.S(status,"plan_id")!="ai-"+task.ParentTaskId||CodexChat.S(status,"owner")!="external"||CodexChat.S(status,"external_task")!=task.Id||CodexChat.S(status,"control")!="agent"||Equals(status["stopped"],true))throw new InvalidOperationException("위임 작업의 입력권이 해제되었습니다.");
 }
 internal object Open(string id){
  if(GameEnvironment.Remote(root))return GameEnvironment.Invoke(root,"external.open",new Dictionary<string,object>{{"id",id}},true);
  var s=Spec(id);if(!Ready(id))throw new InvalidOperationException("도구를 먼저 설치하거나 기존 설치를 연결해 주세요.");
  if(id=="bettergi")CheckInputAvailable();
  var lease=id=="bettergi"?GameInputLease.Acquire(root):null;
  try{using(var p=Process.Start(new ProcessStartInfo(PathFor(id)){UseShellExecute=true,Verb=id=="bettergi"&&!AppRuntime.Elevated()?"runas":"",WorkingDirectory=Path.GetDirectoryName(PathFor(id))})){
   System.Threading.Thread.Sleep(700);
   bool alive=!p.HasExited||Components.ManagedProcessRunning(Path.GetDirectoryName(PathFor(id)));
   if(!alive)throw new InvalidOperationException("도구가 바로 종료되었습니다. 실행 환경을 확인해 주세요.");
   if(lease!=null){var retained=lease;int pid=p.Id;Task.Run(()=>{try{using(var owner=Process.GetProcessById(pid))owner.WaitForExit();}catch(ArgumentException){}finally{retained.Dispose();}});lease=null;}
   return new{launch_requested=true,process_observed=alive,gameplay_completed=false};
  }}finally{if(lease!=null)lease.Dispose();}
 }
 internal static object Value(Dictionary<string,object> d,string key){var match=d.FirstOrDefault(x=>string.Equals(x.Key,key,StringComparison.OrdinalIgnoreCase));return match.Value;}
 const string ManagedPrefix="Catheryne-";
 internal string GroupRevision(){if(!Ready("bettergi"))return "";string user=Path.Combine(Path.GetDirectoryName(PathFor("bettergi")),"User");return string.Join("|",new[]{"ScriptGroup","AutoPathing","AutoFight","AutoGeniusInvokation"}.SelectMany(n=>Directory.Exists(Path.Combine(user,n))?new DirectoryInfo(Path.Combine(user,n)).EnumerateFiles("*",SearchOption.AllDirectories):Enumerable.Empty<FileInfo>()).Concat(File.Exists(Path.Combine(user,"config.json"))?new[]{new FileInfo(Path.Combine(user,"config.json"))}:new FileInfo[0]).Where(f=>!f.FullName.Substring(user.Length+1).Split(Path.DirectorySeparatorChar).Any(part=>part.StartsWith(ManagedPrefix,StringComparison.Ordinal))).OrderBy(f=>f.FullName).Select(f=>f.FullName+":"+f.Length+":"+f.LastWriteTimeUtc.Ticks));}
 // Reviewed official 0.66.0 entry points only; no discovery/execution of arbitrary scripts.
 internal static readonly Dictionary<string,BuiltinAutomation> BuiltinTasks=new Dictionary<string,BuiltinAutomation>{
  {"position:current",new BuiltinAutomation("현재 위치 확인","Locate","야외 화면의 미니맵이 보여야 함", "없음", "const p = genshin.getPositionFromMap(); if (p === null || !Number.isFinite(p.X) || !Number.isFinite(p.Y)) throw new Error('Position recognition failed'); if (!file.writeTextSync('observation.json', JSON.stringify({task_id:'__TASK_ID__', map:'Teyvat', x:p.X, y:p.Y, kind:'player',captured_at:new Date().toISOString()}))) throw new Error('Position output failed');",true,DisplayPresets.AiHeight)},
  {"position:map_center",new BuiltinAutomation("지도 중심 위치 확인","MapCenter","티바트 지도가 열려 있고 확인할 지점이 화면 중앙에 있어야 함", "없음", "const p = genshin.getPositionFromBigMap(); if (p === null || !Number.isFinite(p.X) || !Number.isFinite(p.Y)) throw new Error('Position recognition failed'); if (!file.writeTextSync('observation.json', JSON.stringify({task_id:'__TASK_ID__', map:'Teyvat', x:p.X, y:p.Y, kind:'map_center',captured_at:new Date().toISOString()}))) throw new Error('Position output failed');",true,DisplayPresets.AiHeight)},
  {"domain:once",new BuiltinAutomation("비경 1회 (레진 20)","AutoDomain","비경 대상과 전투 전략 설정 필요", "퓨어 레진 20", "const p = new AutoDomainParam(1); p.combatStrategyPath = __COMBAT_STRATEGY__; if (!p.domainName || !p.combatStrategyPath) throw new Error(\"Domain target and combat strategy are required\"); p.autoArtifactSalvage = false; p.specifyResinUse = true; p.originalResinUseCount = 0; p.originalResin20UseCount = 1; p.originalResin40UseCount = 0; p.condensedResinUseCount = 0; p.transientResinUseCount = 0; p.fragileResinUseCount = 0; p.rewardRecognitionEnabled = true; await dispatcher.runAutoDomainTask(p);",false,DisplayPresets.AiHeight)},
  {"boss:once",new BuiltinAutomation("보스 1회 (레진 40)","AutoBoss","보스 대상과 전투 전략 설정 필요", "퓨어 레진 40", "const p = new AutoBossParam(); p.combatStrategyPath = __COMBAT_STRATEGY__; if (!p.bossName || !p.combatStrategyPath) throw new Error(\"Boss target and combat strategy are required\"); p.specifyRunCount = true; p.runCount = 1; p.useTransientResin = false; p.useFragileResin = false; p.rewardRecognitionEnabled = true; p.timeout = 240; await dispatcher.runAutoBossTask(p);")},
  {"fishing:auto",new BuiltinAutomation("낚시","AutoFishing","현재 낚시 지점과 미끼 준비", "미끼", "await dispatcher.runTask(new SoloTask(\"AutoFishing\", { wholeProcessTimeoutSeconds: 600, throwRodTimeOutTimeoutSeconds: 30, fishingTimePolicy: 0, saveScreenshotOnKeyTick: false }));")},
  {"wood:auto",new BuiltinAutomation("목재 수집","AutoWood","나무 근처에서 벌목 설정과 수집 도구 준비", "없음", "await dispatcher.runTask(new SoloTask(\"AutoWood\"));")},
  {"cards:auto",new BuiltinAutomation("일곱 성인의 소환","AutoGeniusInvokation","대전 화면과 저장된 덱 전략 필요", "없음", "await dispatcher.runTask(new SoloTask(\"AutoGeniusInvokation\"));",false,DisplayPresets.AiHeight)}
 };
 internal static string BuiltinFolder(string name){return ManagedPrefix+BuiltinTasks[name].EntryPoint;}
 internal static Dictionary<string,object> Bundle(string name,object[] projects){return new Dictionary<string,object>{{"name",name},{"definition",new Dictionary<string,object>{{"name",name},{"projects",projects.Select(p=>p as Dictionary<string,object>??CatheryneTools.Json().Deserialize<Dictionary<string,object>>(CatheryneTools.Json().Serialize(p))).ToArray()}}}};}
 internal Dictionary<string,object>[] Groups(){
  if(!Ready("bettergi"))return new Dictionary<string,object>[0];
  string user=Path.Combine(Path.GetDirectoryName(PathFor("bettergi")),"User"),folder=Path.Combine(user,"ScriptGroup");
  var groups=new List<Dictionary<string,object>>();
  if(Directory.Exists(folder))foreach(string path in Directory.GetFiles(folder,"*.json").OrderBy(x=>x)){
   if(Path.GetFileName(path).StartsWith(ManagedPrefix,StringComparison.Ordinal))continue;
   var d=StoryClient.Read(path);string name=Convert.ToString(Value(d,"Name"));if(!string.IsNullOrEmpty(name))groups.Add(new Dictionary<string,object>{{"name",name},{"definition",d}});
  }
  string routes=Path.Combine(user,"AutoPathing");
  if(Directory.Exists(routes))foreach(string path in Directory.GetFiles(routes,"*.json",SearchOption.AllDirectories).OrderBy(x=>x)){
   if(path.Substring(routes.Length+1).Split(Path.DirectorySeparatorChar).Any(part=>part.StartsWith(ManagedPrefix,StringComparison.Ordinal)))continue;
   var route=StoryClient.Read(path);if(Value(route,"positions")==null)continue;
   groups.Add(RouteBundle(path.Substring(routes.Length+1),route));
  }
  var dialogue=Bundle("dialogue:auto",new object[]{new{index=0,name="Catheryne dialogue",folderName="Catheryne-Dialogue",type="Javascript",status="Enabled",schedule="Daily",runNum=1}});dialogue["title"]="대화 자동 진행";groups.Add(dialogue);
  foreach(var entry in BuiltinTasks){var builtin=Bundle(entry.Key,new object[]{new{index=0,name=entry.Value.Title,folderName=BuiltinFolder(entry.Key),type="Javascript",status="Enabled",schedule="Daily",runNum=1}});builtin["title"]=entry.Value.Title;builtin["prerequisites"]=entry.Value.Prerequisites;builtin["resources"]=entry.Value.Resources;builtin["minimum_game_height"]=entry.Value.MinimumHeight;builtin["observation_only"]=entry.Value.ObservationOnly;builtin["requires_explicit_request"]=true;groups.Add(builtin);}
  string fights=Path.Combine(user,"AutoFight");
  if(Directory.Exists(fights)&&Directory.EnumerateFiles(fights,"*",SearchOption.AllDirectories).Any(f=>new[]{".txt",".json"}.Contains(Path.GetExtension(f).ToLowerInvariant()))){var combat=Bundle("combat:auto",new object[]{new{index=0,name="Catheryne combat",folderName="Catheryne-Combat",type="Javascript",status="Enabled",schedule="Daily",runNum=1}});combat["title"]="전투 보조";groups.Add(combat);}
  foreach(var group in groups.Where(g=>BuiltinTasks.ContainsKey(CodexChat.S(g,"name"))||CodexChat.S(g,"name")=="combat:auto")){
   string issue=ConfigurationIssue(CodexChat.S(group,"name"),user);group["configuration_ready"]=issue==null;group["configuration_issue"]=issue;
  }
  return groups.ToArray();
 }
 internal object NavigationCapability(Dictionary<string,object>[] catalog){
  return new{available=File.Exists(PathFor("bettergi")),saved_routes=catalog.Count(g=>CodexChat.S(g,"name").StartsWith("route:",StringComparison.Ordinal)),inline_route_supported=true,
   execution="catheryne_game register with execution.command=delegate and execution.parameters.route, or the same delegate command after registration",
   coordinates="Verified Teyvat world coordinates only; never screen pixels or guessed positions",
   grounding=new{player="delegate position:current while the outdoor minimap is visible",waypoint="Center each intended waypoint on the open Teyvat map, then delegate position:map_center. Its result is the map center, not the player or an automatically detected quest target."},
   planning="No saved routes does not mean navigation is unavailable. Ground destination and safe intermediate waypoints, then submit one route plan using the game tool route contract. Reuse verified coordinates during this task. If grounding fails, report that limitation and use a visible-ground batch; do not invent coordinates.",
   authorization="Position observations and route execution may support the user's explicitly requested game task. They do not authorize spending or unrelated tasks."};
 }
 internal static Dictionary<string,object> RouteBundle(string relative,Dictionary<string,object> route){
  var bundle=Bundle("route:"+relative,new object[]{new{index=0,name=Path.GetFileName(relative),folderName=Path.GetDirectoryName(relative),type="Pathing",status="Enabled",schedule="Daily",runNum=1}});
  bundle["title"]=Path.GetFileNameWithoutExtension(relative);
  var points=CodexChat.Items(Value(route,"positions")).ToArray();
  bundle["route"]=new Dictionary<string,object>{{"info",Value(route,"info")},{"waypoints",points.Length},{"actions",points.Select(x=>Convert.ToString(Value(x,"action"))).Where(x=>!string.IsNullOrEmpty(x)).Distinct().ToArray()}};
  return bundle;
 }
 internal static bool GroupMatches(Dictionary<string,object> group,string query){
  if(string.IsNullOrWhiteSpace(query))return true;
  var route=CodexChat.Map(Value(group,"route"));var info=CodexChat.Map(Value(route,"info"));
  var fields=new[]{Value(group,"name"),Value(group,"title"),Value(info,"name"),Value(info,"description"),Value(info,"mapName")};
  string text=string.Join(" ",fields.Select(Convert.ToString));
  return query.Split((char[])null,StringSplitOptions.RemoveEmptyEntries).All(term=>text.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0);
 }
 internal object RoutePage(string name,int offset){
  var group=Groups().SingleOrDefault(g=>CodexChat.S(g,"name")==name&&name.StartsWith("route:",StringComparison.Ordinal));
  if(group==null)throw new ArgumentException("등록된 경로를 선택해 주세요.");
  string folder=Path.Combine(Path.GetDirectoryName(PathFor("bettergi")),"User","AutoPathing");
  string file=Path.GetFullPath(Path.Combine(folder,name.Substring(6)));
  if(!file.StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Invalid route path");
  var route=StoryClient.Read(file);var points=CodexChat.Items(Value(route,"positions")).ToArray();
  return new{name=name,coordinate_system="world coordinates for the map identified in info",info=Value(route,"info"),total=points.Length,items=points.Skip(offset).Take(50).ToArray(),next_offset=offset+50<points.Length?(object)(offset+50):null};
 }
 internal static Dictionary<string,object> ComposeBundle(string name,Dictionary<string,object>[] selected,bool allowShell=false){
  if(selected.Length<1||selected.Length>20)throw new ArgumentException("작업은 1개에서 20개까지 선택해 주세요.");
  var projects=new List<object>();object config=null;
  foreach(var group in selected){var definition=CodexChat.Map(group["definition"]);var nextConfig=Value(definition,"config");
   if(selected.Length>1&&CatheryneTools.Json().Serialize(nextConfig??new Dictionary<string,object>())!=CatheryneTools.Json().Serialize(Value(CodexChat.Map(selected[0]["definition"]),"config")??new Dictionary<string,object>()))throw new ArgumentException("개별 설정이 있는 작업은 따로 실행해 주세요.");
   config=nextConfig;
   foreach(var original in CodexChat.Items(Value(definition,"projects"))){var project=new Dictionary<string,object>(original);if(!allowShell&&Convert.ToString(Value(project,"type"))=="Shell")throw new ArgumentException("셸 작업은 게임 묶음으로 실행할 수 없습니다.");foreach(string key in project.Keys.Where(k=>string.Equals(k,"index",StringComparison.OrdinalIgnoreCase)).ToArray())project.Remove(key);project["index"]=projects.Count;projects.Add(project);}
  }
  if(projects.Count==0||projects.Count>100)throw new ArgumentException("실행할 작업 구성을 확인해 주세요.");
  if(projects.Count>1&&projects.Any(p=>Convert.ToString(Value(CodexChat.Map(p),"folderName"))=="Catheryne-Dialogue"))throw new ArgumentException("대화 보조는 대화 상태에서 단독으로 실행해 주세요. 상태가 바뀌면 다음 계획을 실행합니다.");
  var result=CodexChat.Map(Bundle(name,projects.ToArray())["definition"]);if(config!=null)result["config"]=config;return result;
 }
 internal static Dictionary<string,object> ValidateRoute(Dictionary<string,object> route){
  if(route==null||route.Keys.Any(k=>!new[]{"title","evidence","positions"}.Contains(k)))throw new ArgumentException("경로 계획 형식을 확인해 주세요.");
  string title=CodexChat.S(route,"title"),evidence=CodexChat.S(route,"evidence");
  if(string.IsNullOrWhiteSpace(title)||string.IsNullOrWhiteSpace(evidence))throw new ArgumentException("경로 이름과 좌표 근거가 필요합니다.");
  var raw=Value(route,"positions") as System.Collections.IEnumerable;if(raw==null||raw is string)throw new ArgumentException("경유점 목록이 필요합니다.");var entries=raw.Cast<object>().ToArray();if(entries.Any(v=>!(v is Dictionary<string,object>)))throw new ArgumentException("모든 경유점은 좌표 객체여야 합니다.");var positions=entries.Cast<Dictionary<string,object>>().ToArray();
  if(positions.Length<1||positions.Length>100)throw new ArgumentException("경유점은 1개에서 100개까지 지정해 주세요.");
  var normalized=new List<object>();
  for(int i=0;i<positions.Length;i++){
   var point=positions[i];if(point.Keys.Any(k=>!new[]{"x","y","type","move_mode"}.Contains(k)))throw new ArgumentException("이동 경로에는 좌표와 이동 방식만 지정할 수 있습니다.");
   string type=CodexChat.S(point,"type"),move=CodexChat.S(point,"move_mode");
   if(!new[]{"path","target","teleport"}.Contains(type)||!new[]{"walk","run","dash","climb","fly"}.Contains(move)||(type=="teleport"&&i!=0))throw new ArgumentException("경유점 이동 방식을 확인해 주세요.");
   var coords=new double[2];int c=0;foreach(string axis in new[]{"x","y"}){object value=Value(point,axis);if(value==null||value is string||value is bool)throw new ArgumentException("실제 지도 좌표가 필요합니다.");double number=Convert.ToDouble(value);if(double.IsNaN(number)||double.IsInfinity(number)||Math.Abs(number)>100000)throw new ArgumentException("지도 좌표 범위를 확인해 주세요.");coords[c++]=number;}
   normalized.Add(new Dictionary<string,object>{{"x",coords[0]},{"y",coords[1]},{"type",type},{"move_mode",move},{"action",""}});
  }
  if(CodexChat.S(positions.Last(),"type")!="target")throw new ArgumentException("마지막 경유점은 도착 지점이어야 합니다.");
  return new Dictionary<string,object>{{"info",new{name=title,type="collect",mapName="Teyvat",description=evidence}},{"positions",normalized.ToArray()}};
 }
 internal static string ConfigurationIssue(string name,string user){
  try{
   if(name=="combat:auto"||name=="domain:once"||name=="boss:once")ReadCombatStrategyPath(user,name=="boss:once"?"autoBossConfig":"autoFightConfig");
   if(name=="cards:auto")ReadCardStrategy(user);
   if(name=="domain:once"||name=="boss:once"){
    var config=StoryClient.Read(Path.Combine(user,"config.json"));
    string section=name=="domain:once"?"autoDomainConfig":"autoBossConfig",field=name=="domain:once"?"domainName":"bossName";
    if(string.IsNullOrWhiteSpace(Convert.ToString(Value(CodexChat.Map(Value(config,section)),field))))return name=="domain:once"?"비경 대상이 설정되지 않았습니다.":"보스 대상이 설정되지 않았습니다.";
   }
   return null;
  }catch(InvalidOperationException error){return error.Message;}catch(IOException){return "자동화 설정 파일을 읽을 수 없습니다.";}catch(ArgumentException){return "자동화 설정 형식을 확인해 주세요.";}catch(UnauthorizedAccessException){return "자동화 설정 파일에 접근할 수 없습니다.";}
 }
 internal static string ReadCombatStrategyPath(string user,string configSection="autoFightConfig"){
  string configPath=Path.Combine(user,"config.json");
  if(!File.Exists(configPath))throw new InvalidOperationException("저장된 전투 전략 설정이 없습니다.");
  string name=Convert.ToString(Value(CodexChat.Map(Value(StoryClient.Read(configPath),configSection)),"strategyName"));
  if(configSection=="autoBossConfig"&&string.IsNullOrWhiteSpace(name))name="根据队伍自动选择";
  if(string.IsNullOrWhiteSpace(name))throw new InvalidOperationException("전투 전략을 먼저 선택해 주세요.");
  if(name=="自动连招（实验）")return name;
  string folder=Path.GetFullPath(Path.Combine(user,"AutoFight"))+Path.DirectorySeparatorChar;
  if(name=="根据队伍自动选择"){
   if(!Directory.Exists(folder)||!Directory.EnumerateFiles(folder,"*.txt",SearchOption.AllDirectories).Any())throw new InvalidOperationException("파티에 맞춰 선택할 전투 전략이 없습니다.");
   return folder;
  }
  foreach(string extension in new[]{".json",".txt"}){
   string path=Path.GetFullPath(Path.Combine(folder,name+extension));
   if(!path.StartsWith(folder,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("전투 전략 경로가 올바르지 않습니다.");
   if(File.Exists(path)){
    if(string.IsNullOrWhiteSpace(File.ReadAllText(path)))throw new InvalidOperationException("선택한 전투 전략이 비어 있습니다.");
    return path;
   }
  }
  throw new InvalidOperationException("선택한 전투 전략 파일을 찾을 수 없습니다.");
 }
 internal static string ReadCardStrategy(string user){
  string configPath=Path.Combine(user,"config.json");
  if(!File.Exists(configPath))throw new InvalidOperationException("저장된 카드 전략 설정이 없습니다.");
  string name=Convert.ToString(Value(CodexChat.Map(Value(StoryClient.Read(configPath),"autoGeniusInvokationConfig")),"strategyName"));
  if(string.IsNullOrWhiteSpace(name))throw new InvalidOperationException("카드 전략을 먼저 선택해 주세요.");
  string folder=Path.GetFullPath(Path.Combine(user,"AutoGeniusInvokation"))+Path.DirectorySeparatorChar;
  string path=Path.GetFullPath(Path.Combine(folder,name+".txt"));
  if(!path.StartsWith(folder,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("카드 전략 경로가 올바르지 않습니다.");
  if(!File.Exists(path))throw new InvalidOperationException("선택한 카드 전략 파일을 찾을 수 없습니다.");
  string content=File.ReadAllText(path);
  if(string.IsNullOrWhiteSpace(content))throw new InvalidOperationException("선택한 카드 전략이 비어 있습니다.");
  return content;
 }
 internal string PrepareBundle(string[] names,string taskId,Dictionary<string,object> route=null){
  if(BetterGiRunning())throw new InvalidOperationException("실행 중인 작업을 마친 뒤 계획을 변경해 주세요.");
  if(route!=null&&names.Length!=0)throw new ArgumentException("새 경로와 저장된 작업은 한 번에 함께 지정할 수 없습니다."); var routePlan=route==null?null:ValidateRoute(route); var catalog=Groups();var selected=names.Select(n=>{var found=catalog.SingleOrDefault(g=>CodexChat.S(g,"name")==n);if(found==null)throw new ArgumentException("등록된 작업을 선택해 주세요.");return found;}).ToArray();
  string name=ManagedPrefix+taskId, user=Path.Combine(Path.GetDirectoryName(PathFor("bettergi")),"User");
  foreach(string entry in names){string issue=ConfigurationIssue(entry,user);if(issue!=null)throw new InvalidOperationException(issue);}
  string cardStrategy=names.Contains("cards:auto")?ReadCardStrategy(user):null;
  var combatStrategies=names.Where(n=>new[]{"combat:auto","domain:once","boss:once"}.Contains(n)).Distinct().ToDictionary(n=>n,n=>ReadCombatStrategyPath(user,n=="boss:once"?"autoBossConfig":"autoFightConfig"));
  if(routePlan!=null){string cache=Path.Combine(user,"AutoPathing",ManagedPrefix+taskId);Directory.CreateDirectory(cache);AtomicFile.Write(Path.Combine(cache,"route.json"),CatheryneTools.Json().Serialize(routePlan));selected=new[]{Bundle(name,new object[]{new{index=0,name="route.json",folderName=ManagedPrefix+taskId,type="Pathing",status="Enabled",schedule="Daily",runNum=1}})};}
  var taskStore=new AiTaskStore(root);var task=taskStore.Find(taskId);var definition=ComposeBundle(name,selected,task!=null&&string.IsNullOrEmpty(task.ParentTaskId));if(routePlan!=null)definition["route"]=routePlan;definition["observation_only"]=ObservationOnly(names);if(task!=null){task.ExecutionPlan=definition;taskStore.Save(task);}

  if(names.Contains("dialogue:auto")){
   string script=Path.Combine(user,"JsScript","Catheryne-Dialogue");Directory.CreateDirectory(script);
   AtomicFile.Write(Path.Combine(script,"manifest.json"),CatheryneTools.Json().Serialize(new{manifestVersion=1,name="Catheryne dialogue",version="1.0",bgiVersion="0.66.0",main="main.js",library=new string[0]}));
   AtomicFile.Write(Path.Combine(script,"main.js"),"(async () => { dispatcher.clearAllTriggers(); const c = new AutoSkipConfig(); c.enabled = true; c.quicklySkipConversationsEnabled = true; c.autoGetDailyRewardsEnabled = false; c.autoReExploreEnabled = false; c.submitGoodsEnabled = false; c.closePopupPagedEnabled = false; c.autoHangoutEventEnabled = false; c.runBackgroundEnabled = false; c.autoWaitDialogueOptionVoiceEnabled = false; dispatcher.addTrigger(new RealtimeTimer(\"AutoSkip\", c)); try { while (true) { await sleep(500); } } finally { dispatcher.clearAllTriggers(); } })();");
  }
  foreach(string builtin in names.Where(n=>BuiltinTasks.ContainsKey(n))){
   string script=Path.Combine(user,"JsScript",BuiltinFolder(builtin));Directory.CreateDirectory(script);
   AtomicFile.Write(Path.Combine(script,"manifest.json"),CatheryneTools.Json().Serialize(new{manifestVersion=1,name=BuiltinTasks[builtin].Title,version="1.0",bgiVersion="0.66.0",main="main.js",library=new string[0]}));
   string body=builtin=="cards:auto"?"await dispatcher.runTask(new SoloTask(\"AutoGeniusInvokation\", { strategy: "+CatheryneTools.Json().Serialize(cardStrategy)+" }));":BuiltinTasks[builtin].Script.Replace("__TASK_ID__",taskId);
   if(combatStrategies.ContainsKey(builtin))body=body.Replace("__COMBAT_STRATEGY__",CatheryneTools.Json().Serialize(combatStrategies[builtin]));
   AtomicFile.Write(Path.Combine(script,"main.js"),"(async () => { dispatcher.clearAllTriggers(); "+body+" })();");
  }
  if(names.Contains("combat:auto")){
   string script=Path.Combine(user,"JsScript","Catheryne-Combat");Directory.CreateDirectory(script);
   AtomicFile.Write(Path.Combine(script,"manifest.json"),CatheryneTools.Json().Serialize(new{manifestVersion=1,name="Catheryne combat",version="1.0",bgiVersion="0.66.0",main="main.js",library=new string[0]}));
   AtomicFile.Write(Path.Combine(script,"main.js"),"(async () => { dispatcher.clearAllTriggers(); const p = new AutoFightParam(); p.combatStrategyPath = "+CatheryneTools.Json().Serialize(combatStrategies["combat:auto"])+"; p.fightFinishDetectEnabled = true; p.pickDropsAfterFightEnabled = false; p.kazuhaPickupEnabled = false; await dispatcher.runAutoFightTask(p); })();");
  }
  string folder=Path.Combine(user,"ScriptGroup");Directory.CreateDirectory(folder);var projection=new Dictionary<string,object>(definition);projection.Remove("route");AtomicFile.Write(Path.Combine(folder,name+".json"),CatheryneTools.Json().Serialize(projection));return name;
 }
 internal static int RequiredHeight(object executionPlan){
  return CodexChat.Items(Value(CodexChat.Map(executionPlan),"projects")).Select(p=>Convert.ToString(Value(p,"type"))=="Pathing"?DisplayPresets.AiHeight:BuiltinTasks.Where(b=>BuiltinFolder(b.Key)==Convert.ToString(Value(p,"folderName"))).Select(b=>b.Value.MinimumHeight).DefaultIfEmpty(0).Max()).DefaultIfEmpty(0).Max();
 }
 internal static bool ObservationOnly(string[] names){return names.Length==1&&BuiltinTasks.ContainsKey(names[0])&&BuiltinTasks[names[0]].ObservationOnly;}
 internal static bool IsObservationTask(AiTaskRecord task){return task!=null&&task.ExecutionPlan!=null&&Equals(Value(CodexChat.Map(task.ExecutionPlan),"observation_only"),true);}
 internal static bool IsDialogueTask(AiTaskRecord task){return task!=null&&task.ExecutionPlan!=null&&CodexChat.Items(Value(CodexChat.Map(task.ExecutionPlan),"projects")).Any(p=>Convert.ToString(Value(p,"folderName"))=="Catheryne-Dialogue");}
 internal void ReleaseBundle(string taskId){
  Guid parsed;if(!Guid.TryParseExact(taskId,"N",out parsed))throw new ArgumentException("Invalid task id");
  string user=Path.Combine(Path.GetDirectoryName(PathFor("bettergi")),"User"),name=ManagedPrefix+taskId;
  foreach(string file in new[]{Path.Combine(user,"ScriptGroup",name+".json"),Path.Combine(user,"AutoPathing",name,"route.json")})try{if(File.Exists(file))File.Delete(file);}catch(IOException){}
  string folder=Path.Combine(user,"AutoPathing",name);try{if(Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);}catch(IOException){}
 }
 bool HasGroup(string name){return name.StartsWith(ManagedPrefix,StringComparison.Ordinal)&&name.Substring(ManagedPrefix.Length).All(Uri.IsHexDigit)&&name.Length==ManagedPrefix.Length+32?File.Exists(Path.Combine(Path.GetDirectoryName(PathFor("bettergi")),"User","ScriptGroup",name+".json")):Groups().Any(g=>CodexChat.S(g,"name")==name);}
 internal static string ExecutionState(Dictionary<string,object> progress){
  var history=CodexChat.Items(Value(progress,"history")).ToArray();
  var current=CodexChat.Map(Value(progress,"currentScriptGroupProjectInfo"));
  // BetterGI can finish a failed project without writing the group's endTime.
  if(history.Concat(new[]{current}).Any(x=>Equals(Value(x,"taskEnd"),true)&&Convert.ToString(Value(x,"status"))=="2"))return "failed";
  DateTime ended;if(!DateTime.TryParse(Convert.ToString(Value(progress,"endTime")),out ended))return "running";
  return history.Length>0&&history.All(x=>Equals(Value(x,"taskEnd"),true)&&Convert.ToString(Value(x,"status"))=="1")&&Equals(Value(current,"taskEnd"),true)&&Convert.ToString(Value(current,"status"))=="1"?"completed":"blocked";
 }
 internal void RefreshTasks(IEnumerable<AiTaskRecord> snapshot=null){
  var store=new AiTaskStore(root);var tasks=(snapshot??store.List()).Where(t=>t.Action=="bettergi"&&t.State=="running").ToArray();if(tasks.Length==0)return;
  string folder=Path.Combine(Path.GetDirectoryName(PathFor("bettergi")),"log","task_progress");
  var files=Directory.Exists(folder)?new DirectoryInfo(folder).GetFiles("*.json"):new FileInfo[0];
  foreach(var task in tasks){
   var result=CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(task.Result)));string group=CodexChat.S(result,"group");DateTime started=DateTime.Parse(task.OwnerStarted).ToUniversalTime();
   foreach(var file in files.Where(f=>f.LastWriteTimeUtc>=started).OrderByDescending(f=>f.LastWriteTimeUtc)){
    Dictionary<string,object> progress;try{progress=StoryClient.Read(file.FullName);}catch(IOException){continue;}catch(ArgumentException){continue;}
    DateTime runStart;if(!DateTime.TryParse(Convert.ToString(Value(progress,"startTime")),out runStart)||runStart.ToUniversalTime()<started.AddSeconds(-2))continue;
    var names=Value(progress,"scriptGroupNames") as System.Collections.IEnumerable;if(names==null||!names.Cast<object>().Any(n=>Convert.ToString(n)==group))continue;
    string state=ExecutionState(progress);if(state=="running"){
     object previous=Value(result,"progress");
     if(CatheryneTools.Json().Serialize(previous)!=CatheryneTools.Json().Serialize(progress)){
      task.Result=new{group=group,progress=progress,source=file.FullName,progress_updated_at=file.LastWriteTimeUtc.ToString("o"),execution_state="running",execution_completed=false,gameplay_completed=false};
      task.Reason="계획 실행 중";store.Save(task);
     }
     break;
    }
    task.Result=new{group=group,progress=progress,source=file.FullName,observation=PositionObservation(task.Id),execution_state=state,execution_completed=state=="completed",gameplay_completed=false};task.Reason=state=="completed"?"반복 작업 실행을 마쳤습니다. 게임 결과는 별도 확인이 필요합니다.":state=="failed"?"반복 작업 실행 중 오류가 발생했습니다.":"실행이 종료됐지만 작업 완료를 확인하지 못했습니다.";store.Save(task);break;
   }
   if(task.State!="running")continue;
   bool alive=GameEnvironment.ProcessAlive(task.OwnerPid,task.OwnerStarted);
   if(!alive){string outcome=CodexChat.S(CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(task.Result))),"execution_state");store.End(task,string.IsNullOrEmpty(outcome)||outcome=="running"?"interrupted":outcome,string.IsNullOrEmpty(outcome)||outcome=="running"?"실행이 종료되어 게임 결과를 확인하지 못했습니다.":task.Reason);}
  }
 }
 internal object PositionObservation(string taskId){
  foreach(var entry in BuiltinTasks.Where(x=>x.Value.ObservationOnly)){
   string path=Path.Combine(Path.GetDirectoryName(PathFor("bettergi")),"User","JsScript",BuiltinFolder(entry.Key),"observation.json");
   if(!File.Exists(path))continue;
   try{var value=StoryClient.Read(path);if(CodexChat.S(value,"task_id")!=taskId)continue;foreach(string axis in new[]{"x","y"}){if(!value.ContainsKey(axis)||value[axis]==null||value[axis] is string||value[axis] is bool)return null;double n=Convert.ToDouble(value[axis]);if(double.IsNaN(n)||double.IsInfinity(n)||Math.Abs(n)>100000)return null;}return value;}catch(IOException){}catch(ArgumentException){}
  }
  return null;
 }
 internal object Progress(){
  RefreshTasks();
  if(!Ready("bettergi"))return new{available=false};
  string folder=Path.Combine(Path.GetDirectoryName(PathFor("bettergi")),"log","task_progress");
  var files=Directory.Exists(folder)?new DirectoryInfo(folder).GetFiles("*.json").OrderByDescending(x=>x.LastWriteTimeUtc).Take(5).ToArray():new FileInfo[0];
  return new{available=true,records=files.Select(f=>new{observed_at=f.LastWriteTimeUtc.ToString("o"),data=StoryClient.Read(f.FullName)}).ToArray(),gameplay_completed=false};
 }
 [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd,int command);
 static void HideOwnedMainWindow(Process process){
  for(int i=0;i<40&&!process.HasExited;i++){process.Refresh();if(process.MainWindowHandle!=IntPtr.Zero){ShowWindowAsync(process.MainWindowHandle,0);return;}System.Threading.Thread.Sleep(100);}
 }
 internal void RunGroup(string name,AiTaskRecord task,Action dispatched=null){
  if(GameEnvironment.Remote(root)){GameEnvironmentOperations.UpdateTask(task,GameEnvironment.Invoke(root,"external.run",new Dictionary<string,object>{{"group",name},{"task",task}},true));if(dispatched!=null)dispatched();return;}
  CheckInputAvailable(true,task);if(BetterGiRunning())throw new InvalidOperationException("다른 반복 작업이 실행 중입니다. 해당 작업을 마친 뒤 다시 시작해 주세요.");
  if(!GameRequirement.Running())throw new InvalidOperationException("원신을 실행한 뒤 작업을 시작해 주세요.");
  if(!HasGroup(name)||name.IndexOfAny(new[]{'"','\r','\n'})>=0)throw new ArgumentException("등록된 반복 작업을 선택해 주세요.");
  if(name!=ManagedPrefix+task.Id)name=PrepareBundle(new[]{name},task.Id);
  task.State="running";task.Reason="반복 작업 준비 중";task.Result=new{group=name,gameplay_completed=false};var store=new AiTaskStore(root);store.Save(task);
  string physicalRoot=Path.GetDirectoryName(NativePaths.Resolve(Path.Combine(root,"catheryne.db")));
  Process worker;
  try{worker=Process.Start(new ProcessStartInfo(NativePaths.Resolve(System.Reflection.Assembly.GetExecutingAssembly().Location),"--external-run "+StoryClient.Quote(physicalRoot)+" "+StoryClient.Quote(task.Id)){UseShellExecute=true,Verb=AppRuntime.Elevated()?"":"runas",WindowStyle=ProcessWindowStyle.Hidden});if(worker==null)throw new InvalidOperationException("작업을 시작하지 못했습니다.");}
  catch{try{ReleaseBundle(task.Id);}catch(IOException){}catch(UnauthorizedAccessException){}throw;}
  using(worker){
   if(dispatched!=null){dispatched();return;}
   for(int attempt=0;attempt<150;attempt++){
    var saved=store.Find(task.Id);if(saved.State=="failed")throw new InvalidOperationException(saved.Reason);
    if(saved.Action=="bettergi") {task.OwnerPid=saved.OwnerPid;task.OwnerStarted=saved.OwnerStarted;task.Action=saved.Action;task.State=saved.State;task.Reason=saved.Reason;task.Result=saved.Result;return;}
    if(worker.HasExited)throw new InvalidOperationException("작업을 시작하지 못했습니다.");System.Threading.Thread.Sleep(100);
   }
   throw new InvalidOperationException("작업 시작 확인이 지연되고 있습니다.");
  }
 }
 internal int StartOwnedGroup(string taskId){
  var store=new AiTaskStore(root);var task=store.Find(taskId);bool inputReleased=true;GameInputLease lease=null;try{
   if(task==null||task.State!="running")throw new InvalidOperationException("실행 요청이 유효하지 않습니다.");store.ThrowIfCancelled(task.Id);
   using(var worker=Process.GetCurrentProcess()){task.OwnerPid=worker.Id;task.OwnerStarted=worker.StartTime.ToUniversalTime().ToString("o");store.Save(task);}
   string name=CodexChat.S(CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(task.Result))),"group");
   store.ThrowIfCancelled(task.Id);CheckInputAvailable(true,task);if(!GameRequirement.Running())throw new InvalidOperationException("원신이 종료되어 작업을 시작하지 않았습니다.");if(BetterGiRunning()||!HasGroup(name))throw new InvalidOperationException("작업 실행 조건이 바뀌었습니다.");
   if(string.IsNullOrEmpty(task.ParentTaskId))lease=GameInputLease.Acquire(root);
   using(var p=Process.Start(new ProcessStartInfo(PathFor("bettergi"),"--startGroups "+StoryClient.Quote(name)){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(PathFor("bettergi"))})){
    string outcome=null,reason=null;inputReleased=false;try{
    task.OwnerPid=p.Id;task.OwnerStarted=p.StartTime.ToUniversalTime().ToString("o");task.Action="bettergi";task.State="running";task.Reason="반복 작업 실행 중";task.Result=new{group=name,process_started=true,gameplay_completed=false};store.Save(task);if(!string.IsNullOrEmpty(task.ParentTaskId))RequireReservation(task);
    Task.Run(()=>{try{HideOwnedMainWindow(p);}catch(InvalidOperationException){}});
    DateTime nextProgress=DateTime.MinValue;var budget=System.Diagnostics.Stopwatch.StartNew();
    while(!p.WaitForExit(100)){
     if(!string.IsNullOrEmpty(task.ParentTaskId)&&budget.Elapsed.TotalSeconds>600&&!IsDialogueTask(task)){outcome="blocked";reason="실행 시간 제한에 도달했습니다. 현재 화면을 확인해 주세요.";break;}
     if(!string.IsNullOrEmpty(task.ParentTaskId)){
      try{RequireReservation(task);}catch{if(!p.HasExited){p.Kill();p.WaitForExit(5000);}outcome="cancelled";reason="위임 작업을 중단했습니다.";break;}
     }
     if(DateTime.UtcNow>=nextProgress){
      RefreshTasks(new[]{task});nextProgress=DateTime.UtcNow.AddSeconds(1);var current=store.Find(task.Id);
      string receipt=CodexChat.S(CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(current.Result))),"execution_state");
      if(current.CancelRequested){outcome="cancelled";reason="작업을 중단했습니다.";break;}
      if(current.State!="running"||(!string.IsNullOrEmpty(receipt)&&receipt!="running")){outcome=current.State!="running"?current.State:receipt;reason=current.Reason;break;}
     }
    }
    RefreshTasks();
    }finally{if(!p.HasExited){p.Kill();if(!p.WaitForExit(5000))throw new IOException("자동화 입력 종료를 확인하지 못했습니다.");}inputReleased=true;}
    if(outcome!=null)store.End(store.Find(task.Id),outcome,reason);else RefreshTasks(new[]{task});
   }return 0;
  }catch(OperationCanceledException){if(task!=null)store.End(store.Find(task.Id)??task,"cancelled","작업을 중단했습니다.");return 2;}catch(Exception error){if(task!=null)store.End(store.Find(task.Id)??task,"failed",error.Message);return 1;}
  finally{
   try{
    if(inputReleased&&task!=null&&!string.IsNullOrEmpty(task.ParentTaskId)){
     try{var latest=store.Find(task.Id);new StoryClient(root).Call("end_external",new{task_id=task.Id,error=latest.State=="completed"?null:latest.Reason},true);}catch(InvalidOperationException){}
    }
   }finally{
    try{if(lease!=null)lease.Dispose();}
    finally{if(inputReleased&&task!=null)ReleaseBundle(task.Id);}
   }
  }
 }
 internal void Stop(string taskId){
  if(GameEnvironment.Remote(root)){GameEnvironment.Invoke(root,"external.stop",new Dictionary<string,object>{{"id",taskId}},false);return;}
  var store=new AiTaskStore(root);var task=store.Find(taskId);if(task==null||task.Action!="bettergi"||task.State!="running")throw new InvalidOperationException("진행 중인 반복 작업이 아닙니다.");store.RequestCancellation(taskId);
  if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)){
   string physicalRoot=Path.GetDirectoryName(NativePaths.Resolve(Path.Combine(root,"catheryne.db")));
   using(var helper=Process.Start(new ProcessStartInfo(NativePaths.Resolve(System.Reflection.Assembly.GetExecutingAssembly().Location),"--external-stop "+StoryClient.Quote(taskId)+" "+StoryClient.Quote(physicalRoot)){UseShellExecute=true,Verb="runas"})){
    if(!helper.WaitForExit(30000)||helper.ExitCode!=0)throw new IOException("반복 작업 중단을 확인하지 못했습니다.");
   }
   if(store.Find(taskId).State!="cancelled")throw new IOException("작업 중단 기록을 확인하지 못했습니다.");return;
  }
  using(var p=Process.GetProcessById(task.OwnerPid)){
   if(p.StartTime.ToUniversalTime().ToString("o")!=task.OwnerStarted||!string.Equals(p.MainModule.FileName,PathFor("bettergi"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("작업 프로세스가 바뀌어 중단하지 않았습니다.");
   // Upstream exposes no stop CLI. Terminate only the exact process started for this task.
   p.Kill();if(!p.WaitForExit(5000))throw new IOException("작업 중단을 확인하지 못했습니다.");
  }
  store.End(task,"cancelled","반복 작업을 중단했습니다.");
 }
 internal object Cultivation(){
  if(!Ready("hutao"))throw new InvalidOperationException("육성 자료 기능 준비가 필요합니다.");
  object response;
  if(ProcessGuard.Running("Snap.Hutao.Remastered"))response=HutaoPipe.ReadCultivation();
  else using(var process=Process.Start(new ProcessStartInfo(PathFor("hutao")){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(PathFor("hutao"))})){
   response=null;for(int attempt=0;attempt<4;attempt++){try{response=HutaoPipe.ReadCultivation();break;}catch(InvalidOperationException){if(process.HasExited)throw;}}
   if(response==null)throw new InvalidOperationException("초기 설정이 필요합니다. 설정의 구성요소 관리에서 준비해 주세요.");
   HideOwnedMainWindow(process);
  }
  using(var db=new LocalDataService(root))db.Observe("default","hutao-cultivation",response);
  return new{observed_at=DateTime.UtcNow.ToString("o"),source="Snap Hutao Remastered",data=response,account_inventory_merged=false};
 }
}

// Upstream read-only pipe contract, version 1, request kind 40. No game/account switching.
internal static class HutaoPipe {
 static ulong Rot(ulong x,int n){return (x<<n)|(x>>(64-n));}
 const ulong P1=11400714785074694791UL,P2=14029467366897019727UL,P3=1609587929392839161UL,P4=9650029242287828579UL,P5=2870177450012600261UL;
 static ulong Round(ulong a,ulong x){unchecked{return Rot(a+x*P2,31)*P1;}}
 internal static ulong Hash(byte[] a){
  unchecked{int i=0;ulong h;
   if(a.Length>=32){ulong v1=P1+P2,v2=P2,v3=0,v4=0-P1;while(i<=a.Length-32){v1=Round(v1,BitConverter.ToUInt64(a,i));v2=Round(v2,BitConverter.ToUInt64(a,i+8));v3=Round(v3,BitConverter.ToUInt64(a,i+16));v4=Round(v4,BitConverter.ToUInt64(a,i+24));i+=32;}h=Rot(v1,1)+Rot(v2,7)+Rot(v3,12)+Rot(v4,18);foreach(ulong v in new[]{v1,v2,v3,v4})h=(h^Round(0,v))*P1+P4;}else h=P5;
   h+=(ulong)a.Length;while(i<=a.Length-8){h=Rot(h^Round(0,BitConverter.ToUInt64(a,i)),27)*P1+P4;i+=8;}if(i<=a.Length-4){h=Rot(h^((ulong)BitConverter.ToUInt32(a,i)*P1),23)*P2+P3;i+=4;}while(i<a.Length){h=Rot(h^((ulong)a[i++]*P5),11)*P1;}h^=h>>33;h*=P2;h^=h>>29;h*=P3;return h^(h>>32);
  }
 }
 static byte[] Read(Stream pipe,int length){var b=new byte[length];int at=0;while(at<length){int n=pipe.Read(b,at,length-at);if(n==0)throw new EndOfStreamException();at+=n;}return b;}
 internal static object ReadCultivation(){
  using(var pipe=new NamedPipeClientStream(".","Snap.Hutao.Remastered.PrivateNamedPipe",PipeDirection.InOut,PipeOptions.Asynchronous)){
   try{pipe.Connect(3000);}catch(TimeoutException){throw new InvalidOperationException("재료 계획 관리에서 초기 연결을 완료해 주세요.");}
   var job=Task.Run(()=>{
    byte[] data=Encoding.UTF8.GetBytes("{\"Kind\":40,\"Data\":null}");byte[] header=new byte[16];header[0]=1;header[1]=1;header[2]=20;header[3]=1;Array.Copy(BitConverter.GetBytes(data.Length),0,header,4,4);Array.Copy(BitConverter.GetBytes(Hash(data)),0,header,8,8);
    pipe.Write(header,0,16);pipe.Write(data,0,data.Length);pipe.Flush();
    var reply=Read(pipe,16);int length=BitConverter.ToInt32(reply,4);
    if(reply[0]!=1||reply[1]!=2||reply[2]!=23||reply[3]!=1||length<0||length>4000000)throw new InvalidDataException("육성 자료 형식이 일치하지 않습니다.");
    var bytes=Read(pipe,length);if(Hash(bytes)!=BitConverter.ToUInt64(reply,8))throw new InvalidDataException("육성 자료 검증에 실패했습니다.");
    return CatheryneTools.Json().DeserializeObject(Encoding.UTF8.GetString(bytes));
   });
   if(!job.Wait(10000))throw new InvalidOperationException("육성 자료 조회 시간이 초과되었습니다.");return job.Result;
  }
 }
}

// Task-oriented projections; no provider-specific navigation or execution path.
internal static class IntegratedWorkflows {
 static async Task<AiTaskRecord> Execute(string action,Dictionary<string,object> args){return (AiTaskRecord)await Task.Run(()=>new CatheryneTools(Setup.DataFolder).Run("catheryne_execute",new Dictionary<string,object>{{"action",action},{"parameters",args}},"",Guid.NewGuid().ToString("N")));}
 static Button Manage(string id,string title,TextBlock status){var button=PanelUi.Button(title);button.Click+=async(s,e)=>{button.IsEnabled=false;try{if(!new ExternalTools(Setup.DataFolder).Ready(id)){status.Text=Locale.T("기능 준비 중…");status.Visibility=Visibility.Visible;await Task.Run(()=>Components.Prepare(Components.Catalog().Single(x=>x.Id==id)));}var result=await Execute("tools.open",new Dictionary<string,object>{{"id",id}});status.Text=result.State=="completed"?"":result.Reason;}catch(Exception ex){status.Text=ex.Message;}finally{status.Visibility=string.IsNullOrEmpty(status.Text)?Visibility.Collapsed:Visibility.Visible;button.IsEnabled=true;}};return button;}
 internal static FrameworkElement Cultivation(){return MaterialPanel.View(HutaoCultivation());}
 internal static FrameworkElement HutaoCultivation(){
  var rows=new StackPanel();var status=PanelUi.Text("",true);status.Visibility=Visibility.Collapsed;bool updating=false;string rendered=null;
  Action render=()=>{rows.Children.Clear();using(var db=new LocalDataService(Setup.DataFolder)){var saved=db.Recent("default","hutao-cultivation",1);if(saved.Count==0){rows.Children.Add(PanelUi.Text(Locale.T("연결된 육성 계획이 없습니다."),true));return;}var response=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(saved[0]["payload"]);var data=ExternalTools.Value(response,"Data") as Dictionary<string,object>;if(data==null){rows.Children.Add(PanelUi.Text(Locale.T("선택된 육성 계획이 없습니다."),true));return;}var inventory=CodexChat.Items(ExternalTools.Value(data,"InventoryItems")).GroupBy(x=>Convert.ToString(ExternalTools.Value(x,"ItemId"))).ToDictionary(g=>g.Key,g=>Convert.ToString(ExternalTools.Value(g.First(),"Count")));foreach(var entry in CodexChat.Items(ExternalTools.Value(data,"Entries")))foreach(var item in CodexChat.Items(ExternalTools.Value(entry,"Items"))){string owned;inventory.TryGetValue(Convert.ToString(ExternalTools.Value(item,"ItemId")),out owned);rows.Children.Add(PanelUi.Row(Convert.ToString(ExternalTools.Value(item,"Name")),PanelUi.Text((owned??Locale.T("미확인"))+" / "+ExternalTools.Value(item,"Count"),true)));}}};

  var view=PanelUi.Section(Locale.T("육성 재료"),rows,status,PanelUi.Details(Locale.T("관리"),PanelUi.Actions(Manage("hutao","재료 계획 관리",status)),PanelUi.Text(Locale.T("연결 구성요소: Snap Hutao Remastered"),true)));
  Func<Task> update=async()=>{if(updating||!view.IsVisible)return;updating=true;try{await Task.Run(()=>{if(ProcessGuard.Running("Snap.Hutao.Remastered"))ObservationRefresh.Run(Setup.DataFolder,"default","hutao-cultivation",TimeSpan.FromMinutes(1),false,()=>new ExternalTools(Setup.DataFolder).Cultivation());});string payload;using(var db=new LocalDataService(Setup.DataFolder)){var saved=db.Recent("default","hutao-cultivation",1);payload=saved.Count==0?"":saved[0]["payload"];}if(payload!=rendered){rendered=payload;render();}status.Text="";}catch(Exception error){status.Text=error.Message;}finally{status.Visibility=string.IsNullOrEmpty(status.Text)?Visibility.Collapsed:Visibility.Visible;updating=false;}};
  var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};timer.Tick+=async(s,e)=>await update();view.IsVisibleChanged+=async(s,e)=>{if(view.IsVisible){timer.Start();await update();}else timer.Stop();};view.Unloaded+=(s,e)=>timer.Stop();render();return view;
 }
 internal static FrameworkElement Routines(){
  var groups=new ComboBox{SelectedValuePath="Tag"};groups.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");groups.MinHeight=36;groups.Margin=new Thickness(0,0,0,12);
  var run=PanelUi.Button(Locale.T("실행"));var stop=PanelUi.Button(Locale.T("중단"));var status=PanelUi.Text("",true);string taskId=null;bool loading=false,needsGroups=true;string groupRevision="";int generation=0;
  var groupIssues=new Dictionary<string,string>();
  Func<string> selectedIssue=()=>{string issue;return groupIssues.TryGetValue(Convert.ToString(groups.SelectedValue)??"",out issue)?issue:null;};
  Func<bool> canRun=()=>taskId==null&&groups.SelectedItem!=null&&selectedIssue()==null;
  groups.SelectionChanged+=(s,e)=>{run.IsEnabled=canRun();if(taskId==null){status.Text=selectedIssue()??"";status.Visibility=string.IsNullOrEmpty(status.Text)?Visibility.Collapsed:Visibility.Visible;}};
  var view=PanelUi.Section(Locale.T("반복 작업"),groups,PanelUi.Actions(run,stop),status,PanelUi.Details(Locale.T("관리"),PanelUi.Actions(Manage("bettergi","작업 등록 및 관리",status)),PanelUi.Text(Locale.T("연결 구성요소: BetterGI"),true)));
  Func<bool,Task> update=async reload=>{if(loading||!view.IsVisible)return;loading=true;reload=reload||needsGroups;int request=generation;try{
   var result=await Task.Run(()=>{var external=new ExternalTools(Setup.DataFolder);var tasks=new AiTaskStore(Setup.DataFolder).List();external.RefreshTasks(tasks);string revision=external.GroupRevision();return new{Tasks=tasks,Revision=revision,Groups=reload||revision!=groupRevision?external.Groups():null};});
   if(request!=generation||!view.IsVisible)return;
   if(result.Groups!=null){groupRevision=result.Revision;needsGroups=false;groupIssues.Clear();foreach(var item in result.Groups){string issue=CodexChat.S(item,"configuration_issue");if(!string.IsNullOrEmpty(issue))groupIssues[CodexChat.S(item,"name")]=issue;}var selected=groups.SelectedValue;groups.ItemsSource=result.Groups.Select(g=>new ComboBoxItem{Content=Locale.T(string.IsNullOrEmpty(CodexChat.S(g,"title"))?CodexChat.S(g,"name"):CodexChat.S(g,"title")),Tag=CodexChat.S(g,"name")}).ToArray();groups.SelectedValue=selected;if(groups.SelectedIndex<0&&groups.Items.Count>0)groups.SelectedIndex=0;}
   var active=result.Tasks.FirstOrDefault(t=>t.Action=="bettergi"&&t.State=="running");var current=active??result.Tasks.FirstOrDefault(t=>t.Id==taskId);taskId=active==null?null:active.Id;stop.IsEnabled=active!=null;run.IsEnabled=canRun();status.Text=current!=null?current.Reason:groups.Items.Count==0?Locale.T("등록된 반복 작업이 없습니다."):selectedIssue()??"";status.Visibility=string.IsNullOrEmpty(status.Text)?Visibility.Collapsed:Visibility.Visible;
  }catch(Exception error){if(request==generation){status.Text=error.Message;status.Visibility=Visibility.Visible;}}finally{loading=false;}};
  var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};timer.Tick+=async(s,e)=>await update(false);
  run.Click+=async(s,e)=>{if(groups.SelectedItem==null)return;run.IsEnabled=false;try{if(!await GameRequirement.Ensure(Window.GetWindow(view)))return;var t=await Execute("bettergi.run",new Dictionary<string,object>{{"group",Convert.ToString(groups.SelectedValue)}});taskId=t.State=="running"?t.Id:null;status.Text=t.Reason;status.Visibility=Visibility.Visible;await update(false);}finally{run.IsEnabled=canRun();}};
  stop.Click+=async(s,e)=>{if(taskId==null)return;var t=await Execute("bettergi.stop",new Dictionary<string,object>{{"task_id",taskId}});status.Text=t.Reason;status.Visibility=Visibility.Visible;await update(false);};
  view.IsVisibleChanged+=async(s,e)=>{++generation;if(view.IsVisible){needsGroups=true;timer.Start();await update(true);}else timer.Stop();};view.Unloaded+=(s,e)=>{++generation;timer.Stop();};run.IsEnabled=stop.IsEnabled=false;return view;
 }
}
