using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Net;
using System.Security.Cryptography;
using System.Text;

// Desired state is canonical here; the XXMI Mods folder is a rebuildable projection.
internal sealed class ManagedMod {
 public List<ModKeyOverride> Keys {get;set;}
 public string Id {get;set;} public string Name {get;set;} public bool Enabled {get;set;}
 public string Cover {get;set;} public string Target {get;set;} public string Builtin {get;set;} public string ExternalFolder {get;set;} public string DisabledFolder {get;set;}
}
internal sealed class ModState {public int Version=1;public List<ManagedMod> Items=new List<ManagedMod>();public string AppliedSignature="";}
internal sealed class ModManager {
 readonly string root;readonly Func<bool> busy;
 internal ModManager(string root):this(root,ProcessGuard.Busy){}
 internal ModManager(string root,Func<bool> busy){this.root=Path.GetFullPath(root);this.busy=busy;}
 internal static void EnsureEditable(Func<bool> busy=null){if((busy??ProcessGuard.Busy)())throw new InvalidOperationException("게임 종료 후 모드를 변경할 수 있습니다.");}
 void Guard(){if(busy())throw new InvalidOperationException("게임 종료 후 모드를 변경할 수 있습니다.");}
 internal static string Source(ManagedMod item,ModManager manager){if(item.ExternalFolder==null)return manager.Payload(item);if(Directory.Exists(item.ExternalFolder))return item.ExternalFolder;return item.DisabledFolder??Path.Combine(Path.GetDirectoryName(item.ExternalFolder),"DISABLED_"+Path.GetFileName(item.ExternalFolder));}
 internal string Root {get{return root;}}
 string characterPointer;Dictionary<string,object>[] characters;
 internal Dictionary<string,object>[] Characters(){string file=Path.Combine(root,"profiles","default","account","current.json"),reference=File.Exists(file)?File.ReadAllText(file):"";if(characters==null||reference!=characterPointer){Dictionary<string,object> pointer;characters=AccountMerge.Inventory(new CatheryneTools(root).AccountSnapshot(out pointer),"characters").GroupBy(AccountIdentity.Key).Select(x=>x.First()).OrderBy(x=>AccountIdentity.Name(x)).ToArray();characterPointer=reference;}return characters;}
 static string CanonicalTarget(string target,Dictionary<string,object>[] owned){if(!(target??"").StartsWith("character:"))return target;string key=target.Substring(10);var row=owned.FirstOrDefault(x=>AccountIdentity.Key(x)==key||CodexChat.S(x,"key")==key||CodexChat.S(x,"gameId").Length>0&&"game:"+CodexChat.S(x,"gameId")==key);return row==null?target:"character:"+AccountIdentity.Key(row);}
 string CanonicalTarget(string target){return CanonicalTarget(target,Characters());}
 static bool Matches(string name,IEnumerable<string> aliases){return aliases.Any(alias=>alias.Length>1&&Regex.IsMatch(name,@"(?<![\p{L}\p{N}])"+Regex.Escape(alias)+@"(?![\p{L}\p{N}])",RegexOptions.IgnoreCase));}
 static IEnumerable<string> CatalogTargets(string name){return GameCatalog.CharacterKeys().Where(key=>Matches(name,new[]{key,GameCatalog.Name(key,"ko"),GameCatalog.Name(key,"en")})).Select(key=>"character:"+key);}
 string InferPersonalTarget(string name){var owned=Characters();var candidates=CatalogTargets(name).Concat(owned.Where(row=>Matches(name,new[]{AccountIdentity.Key(row),CodexChat.S(row,"key"),AccountIdentity.Name(row,null,"ko"),AccountIdentity.Name(row,null,"en")})).Select(row=>"character:"+AccountIdentity.Key(row))).Select(target=>CanonicalTarget(target,owned)).Distinct().ToArray();return candidates.Length==1?candidates[0]:null;}
 void ValidateTarget(string target){if(!string.IsNullOrEmpty(target)&&target!="common"&&target!="unclassified"&&(!target.StartsWith("character:")||!GameCatalog.CharacterKeys().Contains(target.Substring(10))&&!Characters().Any(x=>AccountIdentity.Key(x)==target.Substring(10))))throw new ArgumentException("모드 대상을 확인해 주세요.");}
 string Folder {get{return Path.Combine(root,"mods");}}
 string StateFile {get{return Path.Combine(Folder,"state.json");}}
 internal string Payload(ManagedMod item){return Path.Combine(Folder,"library",item.Id);}
 static string Clean(string name){return name.StartsWith("DISABLED",StringComparison.OrdinalIgnoreCase)?name.Substring(8).TrimStart('_','-',' '):name;}
 internal ModState Read(){
  var state=File.Exists(StateFile)?CatheryneTools.Json().Deserialize<ModState>(File.ReadAllText(StateFile)):new ModState();
  if(state==null||state.Version!=1||state.Items==null||state.Items.Any(x=>x==null||!Regex.IsMatch(x.Id??"",@"^[a-f0-9]{32}$")))throw new InvalidDataException("모드 목록을 읽지 못했습니다.");
  foreach(var builtin in ModBuiltins.Catalog){var item=state.Items.SingleOrDefault(x=>x.Id==builtin.Id);if(item==null)state.Items.Add(new ManagedMod{Id=builtin.Id,Builtin=builtin.Key,Name=builtin.Name});else if(item.Builtin!=builtin.Key||item.ExternalFolder!=null)throw new InvalidDataException("기본 모드 목록이 올바르지 않습니다.");state.Items.Single(x=>x.Id==builtin.Id).Target="common";}
  if(state.Items.Any(x=>x.Builtin!=null&&!ModBuiltins.Catalog.Any(b=>b.Id==x.Id&&b.Key==x.Builtin)))throw new InvalidDataException("알 수 없는 기본 모드입니다.");
  foreach(var item in state.Items.Where(x=>x.Builtin==null&&x.Target==null))item.Target=InferPersonalTarget(item.Name);
  string runtime=ModIntegration.RuntimeRoot(root,false);
  if(runtime!=null){string mods=ModIntegration.ModsFolder(runtime);if(Directory.Exists(mods))foreach(string dir in Directory.GetDirectories(mods)){
   string name=Path.GetFileName(dir);if(Clean(name).StartsWith("Catheryne",StringComparison.OrdinalIgnoreCase)||!HasIni(dir))continue;
   string normal=Path.Combine(mods,Clean(name));if(state.Items.Any(x=>string.Equals(x.ExternalFolder,normal,StringComparison.OrdinalIgnoreCase)))continue;
   state.Items.Add(new ManagedMod{Id=ExternalId(normal),Name=Clean(name),Target=InferPersonalTarget(Clean(name)),ExternalFolder=normal,DisabledFolder=name.StartsWith("DISABLED",StringComparison.OrdinalIgnoreCase)?dir:null,Enabled=!name.StartsWith("DISABLED",StringComparison.OrdinalIgnoreCase)});
  }}if(state.Items.Any(x=>Kind(x)=="character")){var owned=Characters();foreach(var item in state.Items.Where(x=>Kind(x)=="character"))item.Target=CanonicalTarget(item.Target,owned);}return state;
 }
 void Save(ModState state){Directory.CreateDirectory(Folder);AtomicFile.Write(StateFile,CatheryneTools.Json().Serialize(state));}
 static string ExternalId(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))).Replace("-","").ToLowerInvariant().Substring(0,32);}
 T Edit<T>(Func<ModState,T> edit,bool persist=true){using(var gate=new System.Threading.Mutex(false,"Local\\Catheryne.Mods")){bool held=false;try{try{held=gate.WaitOne(0);}catch(System.Threading.AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException("다른 모드 처리가 진행 중입니다.");Guard();var state=Read();T result=edit(state);if(persist){Guard();Save(state);}return result;}finally{if(held)gate.ReleaseMutex();}}}
 internal ManagedMod Import(string source){return Import(source,null);}
 internal ManagedMod Import(string source,string targetContext){ValidateTarget(targetContext);return Edit(state=>{
  string id=Guid.NewGuid().ToString("N"),stage=Path.Combine(Folder,"import-"+id),target=Path.Combine(Folder,"library",id);Directory.CreateDirectory(stage);
  try{if(Directory.Exists(source)){if(IsInside(source,Folder)||IsInside(Folder,source))throw new InvalidDataException("관리용 폴더는 가져올 수 없습니다.");Copy(source,stage,true);}else if(File.Exists(source)&&new[]{".zip",".7z",".rar"}.Contains(Path.GetExtension(source).ToLowerInvariant())){Extract(source,stage,true,root);}else throw new ArgumentException("ZIP 파일 또는 모드 폴더를 선택해 주세요.");
   if(!HasIni(stage))throw new InvalidDataException("모드 INI 파일이 없습니다.");Directory.CreateDirectory(Path.GetDirectoryName(target));Directory.Move(stage,target);var item=new ManagedMod{Id=id,Name=Path.GetFileNameWithoutExtension(source.TrimEnd(Path.DirectorySeparatorChar)),Target=targetContext==null?InferPersonalTarget(Path.GetFileNameWithoutExtension(source.TrimEnd(Path.DirectorySeparatorChar))):CanonicalTarget(targetContext),Enabled=false};state.Items.Add(item);return item;
  }finally{DeleteInside(stage,Folder);}
 });}
 internal void SetEnabled(string id,bool enabled){SetEnabled(id,enabled,null);}
 internal void SetEnabled(string id,bool enabled,Action<ModProgress> progress){Edit(state=>{var item=state.Items.SingleOrDefault(x=>x.Id==id);if(item==null)throw new ArgumentException("모드를 찾을 수 없습니다.");if(enabled&&item.Builtin!=null)ModBuiltins.Prepare(root,item,progress);Guard();item.Enabled=enabled;return 0;});}
 internal static string Kind(ManagedMod item){return item.Target=="common"?"common":(item.Target??"").StartsWith("character:")?"character":"unclassified";}
 internal static string InferTarget(string name){var targets=CatalogTargets(name).Distinct().ToArray();return targets.Length==1?targets[0]:null;}
 internal void SetTarget(string id,string target){Edit(state=>{var item=state.Items.SingleOrDefault(x=>x.Id==id);if(item==null||item.Builtin!=null)throw new ArgumentException("개인 모드의 분류만 변경할 수 있습니다.");ValidateTarget(target);item.Target=string.IsNullOrEmpty(target)?"unclassified":CanonicalTarget(target);return 0;});}
 internal void SetCover(string id,string path){Edit(state=>{var item=state.Items.SingleOrDefault(x=>x.Id==id);if(item==null||item.Builtin!=null)throw new ArgumentException("가져온 모드의 이미지만 변경할 수 있습니다.");item.Cover=ModArtwork.Store(root,id,path);return 0;});}
 internal void SetKey(string id,ModBinding binding,string value){if(value!=null)ModBindings.Validate(value);Edit(state=>{var item=state.Items.Single(x=>x.Id==id);var source=ModBindings.Read(item,this).Single(x=>x.Path==binding.Path&&x.Section==binding.Section&&x.Option==binding.Option&&x.Occurrence==binding.Occurrence);if(!ModBindings.CanEdit(source))throw new InvalidOperationException("원본 실행기의 단축키는 해당 실행기에서 변경해 주세요.");if(item.Keys==null)item.Keys=new List<ModKeyOverride>();item.Keys.RemoveAll(source.Matches);if(value!=null&&value!=source.Original)item.Keys.Add(new ModKeyOverride{Path=source.Path,Section=source.Section,Option=source.Option,Occurrence=source.Occurrence,Original=source.Original,Value=value});return 0;});}
 internal object KeysStatus(string id){var state=Read();var item=state.Items.Single(x=>x.Id==id);var active=ModBindings.Active(state,this);return new {id=id,bindings=ModBindings.Read(item,this).Select(key=>new{file=key.Path,section=key.Section,option=key.Option,occurrence=key.Occurrence,original=key.Original,value=key.Value,editable=ModBindings.CanEdit(key)&&!busy(),possible_conflicts=ModBindings.Conflicts(key,active)}).ToArray()};}
 internal void SetKey(string id,string file,string section,string option,int occurrence,string value){var item=Read().Items.Single(x=>x.Id==id);var key=ModBindings.Read(item,this).Single(x=>x.Path==file&&x.Section==section&&x.Option==option&&x.Occurrence==occurrence);SetKey(id,key,value);}
 internal void DisableAll(){Edit(state=>{foreach(var item in state.Items)item.Enabled=false;return 0;});}
 internal bool ForLaunch(Func<bool,bool> action){return Edit(state=>action(state.Items.Any(x=>x.Enabled)),false);}
 internal bool Enabled {get{return Read().Items.Any(x=>x.Enabled);}}
 internal bool Pending {get{var s=Read();return Signature(s)!=s.AppliedSignature;}}
 internal static string Signature(ModState state){return string.Join("|",state.Items.Where(x=>x.Enabled||x.ExternalFolder!=null).OrderBy(x=>x.Id).Select(x=>x.Id+":"+x.Enabled+string.Concat((x.Keys??new List<ModKeyOverride>()).OrderBy(k=>k.Path).ThenBy(k=>k.Section).ThenBy(k=>k.Option).ThenBy(k=>k.Occurrence).Select(k=>":"+k.Path+":"+k.Section+":"+k.Option+":"+k.Occurrence+":"+k.Original+":"+k.Value))));}
 internal void Apply(string runtime){
  Edit(state=>{string mods=ModIntegration.ModsFolder(runtime),target=Path.Combine(mods,"Catheryne"),stage=Path.Combine(mods,"DISABLED_Catheryne_"+Guid.NewGuid().ToString("N").Substring(0,16)),backup=Path.Combine(mods,"DISABLED_Catheryne_"+Guid.NewGuid().ToString("N").Substring(0,16));if(Directory.Exists(stage)||Directory.Exists(backup))throw new IOException("다른 모드 처리 폴더가 있습니다. 다시 시도해 주세요.");Directory.CreateDirectory(stage);
   var moved=new List<Tuple<string,string>>();bool old=false,installed=false;
   try{
    foreach(var item in state.Items.Where(x=>x.Enabled&&x.ExternalFolder==null)){if(item.Builtin!=null)ModBuiltins.Prepare(root,item);Copy(Payload(item),Path.Combine(stage,item.Id),true);ModBindings.Project(item,this,Path.Combine(stage,item.Id));}
    foreach(var active in state.Items.Where(x=>x.Enabled)){string folder=active.ExternalFolder==null?Path.Combine(stage,active.Id):Source(active,this);ValidateIniFiles(folder,active.Name);}ModBuiltins.ConfigureProjection(state,this,runtime,stage);ModLaunch.ConfigurePresentation(runtime,stage);ModBuiltins.ValidateLibraries(state,runtime,stage);PreserveIniTimes(stage,target);Guard();
    foreach(var item in state.Items.Where(x=>x.ExternalFolder!=null)){
     string normal=Path.GetFullPath(item.ExternalFolder);if(!string.Equals(Path.GetDirectoryName(normal),Path.GetFullPath(mods),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("외부 모드 위치가 변경되었습니다.");string disabled=item.DisabledFolder??Path.Combine(mods,"DISABLED_"+Path.GetFileName(normal));if(!string.Equals(Path.GetDirectoryName(Path.GetFullPath(disabled)),Path.GetFullPath(mods),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("외부 모드 위치가 변경되었습니다.");string from=item.Enabled?disabled:normal,to=item.Enabled?normal:disabled;
     if(Directory.Exists(from)){if(Directory.Exists(to))throw new IOException("켜진 모드와 꺼진 모드 폴더가 중복되어 있습니다.");Directory.Move(from,to);moved.Add(Tuple.Create(from,to));}else if(!Directory.Exists(to))throw new DirectoryNotFoundException("모드 파일을 찾을 수 없습니다: "+item.Name);
    }
    if(Directory.Exists(target)){Directory.Move(target,backup);old=true;}Directory.Move(stage,target);installed=true;
    string prior=state.AppliedSignature;state.AppliedSignature=Signature(state);try{Save(state);}catch{state.AppliedSignature=prior;throw;}
   }catch{if(installed)DeleteInside(target,mods);if(old&&Directory.Exists(backup))Directory.Move(backup,target);for(int i=moved.Count-1;i>=0;i--)Directory.Move(moved[i].Item2,moved[i].Item1);throw;}
   finally{DeleteInside(stage,mods);}if(old)DeleteInside(backup,mods);return 0;
  },false);
 }
 // The projection is rebuilt from canonical inputs; only identical INI metadata is reused.
 // XXMI's optimizer cache compares paths and modification times.
 static void PreserveIniTimes(string stage,string prior){
  if(!Directory.Exists(prior))return;
  foreach(string file in ModBindings.IniFiles(stage)){
   string previous=Components.SafeArchivePath(prior,file.Substring(stage.TrimEnd(Path.DirectorySeparatorChar).Length+1));if(!File.Exists(previous))continue;
   var current=new FileInfo(file);var old=new FileInfo(previous);if(current.Length>4*1024*1024||old.Length>4*1024*1024)continue;
   if(current.Length==old.Length&&File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(previous)))File.SetLastWriteTimeUtc(file,old.LastWriteTimeUtc);
   else if(current.LastWriteTimeUtc==old.LastWriteTimeUtc)File.SetLastWriteTimeUtc(file,DateTime.UtcNow);
  }
 }
 internal static void ValidateIniFiles(string folder,string name){foreach(string file in SafeFiles(folder).Where(x=>Path.GetExtension(x).Equals(".ini",StringComparison.OrdinalIgnoreCase)&&!x.Substring(folder.TrimEnd(Path.DirectorySeparatorChar).Length+1).Split(Path.DirectorySeparatorChar).Any(part=>part.StartsWith("DISABLED",StringComparison.OrdinalIgnoreCase)))){if(new FileInfo(file).Length>4*1024*1024)throw new InvalidOperationException(Locale.Format("모드 설정 파일이 너무 큽니다: {0}",name));if(Path.GetFileName(file).Equals("d3dx.ini",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(File.ReadAllText(file),@"(?im)^\s*\[(?:loader|system|stereo|commandlistunbindallrendertargets)"))throw new InvalidOperationException(Locale.Format("모드에 실행기 전역 설정이 포함되어 있습니다: {0}. 모드 파일만 포함된 패키지를 사용해 주세요.",name));}}
 internal object Status(){var s=Read();var keys=ModBindings.Active(s,this);return new{enabled_count=s.Items.Count(x=>x.Enabled),runtime_ready=ModIntegration.Ready(root),pending=Signature(s)!=s.AppliedSignature,editable=!busy(),builtins=ModBuiltins.Catalog.OrderBy(x=>x.Order).ToArray(),groups=ModBuiltins.Groups,key_conflicts=keys.Select(k=>new {mod=k.ModId,section=k.Section,key=k.Value,conflicts=ModBindings.Conflicts(k,keys)}).Where(k=>k.conflicts.Length>0).ToArray(),items=ModBuiltins.Ordered(s.Items).ToArray(),application_verified=false};}
 internal static bool IsInside(string path,string parent){return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
 internal static void DeleteInside(string path,string parent){if(!IsInside(path,parent))throw new InvalidDataException("관리 폴더 밖의 경로입니다.");if(Directory.Exists(path))Directory.Delete(path,true);}
 static bool HasIni(string path){return SafeFiles(path).Any(x=>Path.GetExtension(x).Equals(".ini",StringComparison.OrdinalIgnoreCase)&&!Path.GetFileName(x).StartsWith("DISABLED",StringComparison.OrdinalIgnoreCase));}
 internal static IEnumerable<string> SafeFiles(string folder,string excludeTopLevel=null){if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("링크된 폴더는 가져올 수 없습니다.");foreach(string file in Directory.GetFiles(folder)){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("링크된 파일은 가져올 수 없습니다.");yield return file;}foreach(string child in Directory.GetDirectories(folder).Where(x=>!string.Equals(Path.GetFileName(x),excludeTopLevel,StringComparison.OrdinalIgnoreCase)))foreach(string file in SafeFiles(child))yield return file;}
 static void ValidateFile(string name,bool mod){if(mod&&new[]{".exe",".dll",".bat",".cmd",".ps1",".vbs",".js",".py",".msi",".lnk"}.Contains(Path.GetExtension(name).ToLowerInvariant()))throw new InvalidDataException("실행 파일이 포함된 모드는 가져올 수 없습니다.");}
 internal static void Copy(string source,string target,bool mod,string excludeTopLevel=null){var files=SafeFiles(source,excludeTopLevel).ToArray();if(files.Length>20000||files.Sum(x=>new FileInfo(x).Length)>2L*1024*1024*1024)throw new InvalidDataException("모드 크기가 너무 큽니다.");Directory.CreateDirectory(target);foreach(string file in files){string relative=file.Substring(source.TrimEnd(Path.DirectorySeparatorChar).Length+1);ValidateFile(relative,mod);string dest=Components.SafeArchivePath(target,relative);Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(file,dest,false);}}
 internal static void Extract(string archive,string target,bool mod,string dataRoot=null){if(!Path.GetExtension(archive).Equals(".zip",StringComparison.OrdinalIgnoreCase)){Directory.CreateDirectory(target);ExternalTools.ExtractArchive(dataRoot??Setup.DataFolder,archive,target,2L*1024*1024*1024);var files=SafeFiles(target).ToArray();if(files.Length>20000||files.Sum(x=>new FileInfo(x).Length)>2L*1024*1024*1024)throw new InvalidDataException("압축 파일 크기가 너무 큽니다.");foreach(string file in files)ValidateFile(file,mod);return;}using(var zip=ZipFile.OpenRead(archive)){if(zip.Entries.Count>20000||zip.Entries.Sum(x=>x.Length)>2L*1024*1024*1024)throw new InvalidDataException("압축 파일 크기가 너무 큽니다.");foreach(var e in zip.Entries){if(e.FullName.Contains(":"))throw new InvalidDataException("압축 경로가 올바르지 않습니다.");string dest=Components.SafeArchivePath(target,e.FullName);if((e.ExternalAttributes>>16&0xf000)==0xa000)throw new InvalidDataException("링크가 포함된 압축 파일은 가져올 수 없습니다.");ValidateFile(e.Name,mod);if(e.Name.Length==0){Directory.CreateDirectory(dest);continue;}Directory.CreateDirectory(Path.GetDirectoryName(dest));e.ExtractToFile(dest,false);}}}
}

internal static class ModIntegration {
 const string Version="2.2.1",Hash="71265ec92d2e72dffeb6e561e55c9e4480c57e3896b60f7026dbc68ae7be716e";
 internal static string ManagedRoot(string root){return Path.Combine(root,"components","xxmi",Version);}
 internal static string RuntimeRoot(string root,bool required){
  string path=CodexChat.S(AppPreferences.Read(root),"xxmiPath");if(!File.Exists(path))path=Path.Combine(ManagedRoot(root),"Resources","Bin","XXMI Launcher.exe");
  if(!File.Exists(path)){string existing=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"XXMI Launcher","Resources","Bin","XXMI Launcher.exe");if(File.Exists(existing))path=existing;}
  if(File.Exists(path)&&Path.GetFileName(path).Equals("XXMI Launcher.exe",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(Path.GetDirectoryName(path)).Equals("Bin",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))).Equals("Resources",StringComparison.OrdinalIgnoreCase))return Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(path)));
  if(required)throw new InvalidOperationException("모드 실행 환경을 준비해 주세요.");return null;
 }
 internal static void Connect(string root,string path,Func<bool> busy=null){ModManager.EnsureEditable(busy);if(!File.Exists(path)||!Path.GetFileName(path).Equals("XXMI Launcher.exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("XXMI Launcher 실행 파일을 선택해 주세요.");string previous=CodexChat.S(AppPreferences.Read(root),"xxmiPath");AppPreferences.Set("xxmiPath",Path.GetFullPath(path),root);try{RuntimeRoot(root,true);}catch{AppPreferences.Set("xxmiPath",previous,root);throw;}}
 internal static void Prepare(string root){Prepare(root,null);}
 internal static void Prepare(string root,Action<ModProgress> progress){
  ModManager.EnsureEditable();  if(RuntimeRoot(root,false)!=null)return;string target=ManagedRoot(root),stage=target+"-stage-"+Guid.NewGuid().ToString("N"),archive=stage+".zip";Directory.CreateDirectory(Path.GetDirectoryName(target));
  try{ModProgress.Download("https://github.com/SpectrumQT/XXMI-Launcher/releases/download/v"+Version+"/XXMI-Launcher-Portable-v"+Version+".zip",archive,400L*1024*1024,progress,"실행 환경 다운로드 중…");ModProgress.Report(progress,"배포 파일 검증 중…");
   if(UpdateService.Hash(archive)!=Hash)throw new InvalidDataException("모드 실행 환경 검증에 실패했습니다.");ModProgress.Report(progress,"실행 환경 설치 중…");Directory.CreateDirectory(stage);ModManager.Extract(archive,stage,false);string exe=Directory.GetFiles(stage,"XXMI Launcher.exe",SearchOption.AllDirectories).Single();string unpacked=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(exe)));if(Directory.Exists(target))throw new IOException("기존 실행 환경을 확인해 주세요.");ModManager.EnsureEditable();Directory.Move(unpacked,target);if(RuntimeRoot(root,false)==null)throw new IOException("모드 실행 환경 설치를 확인하지 못했습니다.");
  }finally{if(File.Exists(archive))File.Delete(archive);ModManager.DeleteInside(stage,Path.GetDirectoryName(target));}
 }
 internal static bool Ready(string root){return ModRuntimePackages.Ready(RuntimeRoot(root,false));}
 internal static void EnsureRuntime(string root,Action<ModProgress> progress=null,Func<bool> busy=null){
  RuntimeOperation(()=>{Prepare(root,progress);string runtime=RuntimeRoot(root,true);ModRuntimePackages.Prepare(runtime,progress,busy);ModEngine4001.Ensure(runtime,progress,busy);},busy);
 }
 internal static void RuntimeOperation(Action work,Func<bool> busy=null){
  ModManager.EnsureEditable(busy);using(var gate=new System.Threading.Mutex(false,"Local\\Catheryne.ModRuntime")){bool held=false;try{try{held=gate.WaitOne(0);}catch(System.Threading.AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException("다른 모드 처리가 진행 중입니다.");work();}finally{if(held)gate.ReleaseMutex();}}
 }
 internal static void SetEngine4001(string root,bool enabled,Action<ModProgress> progress=null){
  RuntimeOperation(()=>{if(enabled){Prepare(root,progress);ModRuntimePackages.Prepare(RuntimeRoot(root,true),progress);ModEngine4001.Apply(RuntimeRoot(root,true),progress);}else ModEngine4001.Restore(RuntimeRoot(root,true),progress);});
 }
 internal static void SetEnabled(string root,string id,bool enabled,Action<ModProgress> progress=null){ModManager.EnsureEditable();if(enabled)EnsureRuntime(root,progress);new ModManager(root).SetEnabled(id,enabled,progress);}
 static Dictionary<string,object> Section(Dictionary<string,object> parent,string key){object value;var d=parent.TryGetValue(key,out value)?CodexChat.Map(value):new Dictionary<string,object>();parent[key]=d;return d;}
 internal static string ModsFolder(string runtime){string file=Path.Combine(runtime,"XXMI Launcher Config.json");var data=File.Exists(file)?StoryClient.Read(file):new Dictionary<string,object>();var importer=Section(Section(Section(data,"Importers"),"GIMI"),"Importer");string folder=CodexChat.S(importer,"importer_folder");if(folder.Length==0)folder="GIMI";return Path.GetFullPath(Path.Combine(runtime,folder,"Mods"));}
 internal static bool ImporterReady(string runtime){return File.Exists(Path.Combine(Path.GetDirectoryName(ModsFolder(runtime)),"d3dx.ini"));}
 internal static void OpenSetup(string root){ModManager.EnsureEditable();var installation=new LauncherOperations(root).Installation();if(installation!=null&&File.Exists(installation.Config)&&!ProcessGuard.Busy()&&!Components.ManagedProcessRunning(RuntimeRoot(root,true))){var config=new ConfigStore(installation.Config).Read();var prefs=AppPreferences.Read(root);bool unlocker=!prefs.ContainsKey("UseUnlockerLaunch")||Equals(prefs["UseUnlockerLaunch"],true);Configure(RuntimeRoot(root,true),config,unlocker);}Launch(root);}
 internal static Dictionary<string,object> Configure(string runtime,Dictionary<string,object> config,bool unlocker){
  string path=Path.Combine(runtime,"XXMI Launcher Config.json");var data=File.Exists(path)?StoryClient.Read(path):new Dictionary<string,object>();var launcher=Section(data,"Launcher");launcher["active_importer"]="GIMI";ModRuntimePackages.ConfigureQuickLaunch(data,runtime);
  var importer=Section(Section(Section(data,"Importers"),"GIMI"),"Importer");var values=UnlockerOptions.WithDefaults(config);importer["game_folder"]=Path.GetDirectoryName(Convert.ToString(values["GamePath"]));if(!importer.ContainsKey("importer_folder"))importer["importer_folder"]="GIMI/";importer["launch_options"]=UnlockerOptions.GameArguments(values);importer["use_launch_options"]=true;importer["unlock_fps"]=unlocker;importer["unlock_fps_value"]=values["FPSTarget"];importer["window_mode"]=new[]{"Windowed","Borderless","Fullscreen","ExclusiveFullscreen"}[UnlockerOptions.WindowMode(values)];importer["process_priority"]=new[]{"Realtime","High","AboveNormal","Normal","BelowNormal","Idle"}[Convert.ToInt32(values["Priority"])];importer["enable_hdr"]=values["UseHDR"];importer["configure_game"]=false;
  // A tiny DLL-side log confirms loading when protected processes reject module enumeration.
  var gim=Section(Section(data,"Importers"),"GIMI");var migoto=Section(gim,"Migoto");var logging=Section(Section(Section(importer,"d3dx_ini"),"core"),"Logging");if(!new[]{"warning","info","debug"}.Contains(CodexChat.S(logging,"log_level").ToLowerInvariant()))logging["log_level"]=Equals(migoto.ContainsKey("calls_logging")?migoto["calls_logging"]:null,true)?(Equals(migoto.ContainsKey("debug_logging")?migoto["debug_logging"]:null,true)?"debug":"info"):"warning";logging["unbuffered"]=1;
  // Do not run externally configured commands in the canonical game launch path.
  foreach(string key in new[]{"custom_launch_enabled","run_pre_launch_enabled","run_post_load_enabled"})importer[key]=false;
  string fps=Path.Combine(runtime,"Resources","Packages","GI-FPS-Unlocker","fps_config.json");if(File.Exists(fps)){var settings=StoryClient.Read(fps);foreach(var pair in values)settings[pair.Key]=pair.Value;AtomicFile.Write(fps,CatheryneTools.Json().Serialize(settings),fps+".catheryne-backup");}
  AtomicFile.Write(path,CatheryneTools.Json().Serialize(data),File.Exists(path)?path+".catheryne-backup":null);return data;
 }
 internal static bool TryStart(string root,Dictionary<string,object> config,bool unlocker,Func<bool> busy=null,Action<ModProgress> progress=null){var manager=new ModManager(root,busy??ProcessGuard.Busy);return manager.ForLaunch(enabled=>{if(!enabled){string installed=RuntimeRoot(root,false);if(installed!=null&&manager.Pending)manager.Apply(installed);return false;}EnsureRuntime(root,progress,busy);string runtime=RuntimeRoot(root,true);if(Components.ManagedProcessRunning(runtime))throw new InvalidOperationException("모드 실행기를 닫은 뒤 게임을 시작해 주세요.");if(unlocker&&!Setup.HasDesktopRuntime())Components.UnlockerRuntime();Configure(runtime,config,unlocker);if(!ImporterReady(runtime))throw new IOException("모드 실행 환경 설치를 확인하지 못했습니다.");manager.Apply(runtime);ModLaunch.Start(runtime,Convert.ToString(config["GamePath"]),progress);return true;});}
 internal static ProcessStartInfo StartInfo(string runtime,string gamePath){return new ProcessStartInfo(Path.Combine(runtime,"Resources","Bin","XXMI Launcher.exe"),"--xxmi GIMI --nogui "+StoryClient.Quote(gamePath)){UseShellExecute=true,Verb=AppRuntime.Elevated()?"":"runas",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=runtime};}
 internal static void Launch(string root,bool game=false,string gamePath=null){ModManager.EnsureEditable();string runtime=RuntimeRoot(root,true),args=game?"--xxmi GIMI --nogui "+StoryClient.Quote(gamePath):"--xxmi GIMI";using(var process=Process.Start(game?StartInfo(runtime,gamePath):new ProcessStartInfo(Path.Combine(runtime,"Resources","Bin","XXMI Launcher.exe"),args){UseShellExecute=true,WorkingDirectory=runtime})){if(process==null)throw new IOException("모드 실행 요청을 전달하지 못했습니다.");}}
}
