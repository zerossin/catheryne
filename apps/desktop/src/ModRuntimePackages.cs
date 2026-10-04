using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

// Installation adapter for XXMI's signed package protocol. Injection remains in XXMI.
internal sealed class ModProgress {
 public readonly string Stage;public readonly long Downloaded,Total;
 internal ModProgress(string stage,long downloaded=0,long total=0){Stage=stage;Downloaded=downloaded;Total=total;}
 internal static void Report(Action<ModProgress> progress,string stage,long downloaded=0,long total=0){if(progress!=null)progress(new ModProgress(stage,downloaded,total));}
 internal static void Download(string url,string target,long limit,Action<ModProgress> progress,string stage){
  Report(progress,stage);var request=HttpTransport.Create(url);request.UserAgent="Catheryne";request.Timeout=30000;request.ReadWriteTimeout=30000;
  using(var response=request.GetResponse())using(var input=response.GetResponseStream())using(var output=File.Create(target)){
   var buffer=new byte[81920];int n;long total=0;var last=DateTime.MinValue;
   while((n=input.Read(buffer,0,buffer.Length))>0){total+=n;if(total>limit)throw new InvalidDataException("모드 배포 파일 크기가 너무 큽니다.");output.Write(buffer,0,n);if((DateTime.UtcNow-last).TotalMilliseconds>=100){Report(progress,stage,total,response.ContentLength);last=DateTime.UtcNow;}}
   Report(progress,stage,total,response.ContentLength);
  }
 }
}
internal static class ModRuntimePackages {
 internal const string SharedRepository="SpectrumQT/XXMI-Libs-Package", UnlockerRepository="SpectrumQT/GI-FPS-Unlocker-Package", GimiRepository="SilentNightSound/GIMI-Package";
 const string SharedKey="MHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEYac352uRGKZh6LOwK0fVDW/TpyECEfnRtUp+bP2PJPP63SWOkJ3a/d9pAnPfYezRVJ1hWjZtpRTT8HEAN/b4mWpJvqO43SAEV/1Q6vz9Rk/VvRV3jZ6B/tmqVnIeHKEb";
 const string GimiKey="MHYwEAYHKoZIzj0CAQYFK4EEACIDYgAET5SWORxEdlJ3RXWIFiuwMX6oyZedz+DgaxtsbpWyxNQJDgIDj4uKLLJlvhRNpnkFEuQntgJKzJs0SpASBEguPOTE7VSnmp+x5uyDmsQsWzsRSAZip++a02jqR/K2j18H";
 internal static bool Ready(string runtime){return runtime!=null&&ModIntegration.ImporterReady(runtime)&&File.Exists(Path.Combine(Path.GetDirectoryName(ModIntegration.ModsFolder(runtime)),"Core","GIMI","main.ini"))&&new[]{"XXMI","GI-FPS-Unlocker"}.All(x=>File.Exists(Path.Combine(runtime,"Resources","Packages",x,"Manifest.json")))&&new[]{"3dmloader.dll","d3d11.dll","d3dcompiler_47.dll"}.All(x=>File.Exists(Path.Combine(runtime,"Resources","Packages","XXMI",x)))&&File.Exists(Path.Combine(runtime,"Resources","Packages","GI-FPS-Unlocker","unlockfps_nc.exe"));}
 internal static void Prepare(string runtime,Action<ModProgress> progress=null,Func<bool> busy=null){
  ModManager.EnsureEditable(busy);if(Components.ManagedProcessRunning(runtime))throw new InvalidOperationException("모드 실행기를 닫은 뒤 다시 시도해 주세요.");
  string packages=Path.Combine(runtime,"Resources","Packages"),checkedFile=Path.Combine(runtime,"catheryne-packages-checked.txt");
  if(Ready(runtime)&&File.Exists(checkedFile)&&(DateTime.UtcNow-File.GetLastWriteTimeUtc(checkedFile)).TotalHours<24)return;
  bool ready=Ready(runtime);
  try{
  Install(runtime,SharedRepository,"XXMI-PACKAGE-v",SharedKey,Path.Combine(packages,"XXMI"),false,progress,busy);
  Install(runtime,UnlockerRepository,"GENSHIN-FPS-UNLOCK-PACKAGE-v",SharedKey,Path.Combine(packages,"GI-FPS-Unlocker"),false,progress,busy);
  Install(runtime,GimiRepository,"GIMI-PACKAGE-v",GimiKey,Path.GetDirectoryName(ModIntegration.ModsFolder(runtime)),true,progress,busy);
  if(!Ready(runtime))throw new IOException("모드 실행 환경 설치를 확인하지 못했습니다.");ModManager.EnsureEditable(busy);AtomicFile.Write(checkedFile,DateTime.UtcNow.ToString("o"));
  }catch(System.Net.WebException){if(!ready||!Ready(runtime))throw;ModProgress.Report(progress,"업데이트를 확인하지 못해 기존 실행 환경을 사용합니다.");ModManager.EnsureEditable(busy);AtomicFile.Write(checkedFile,DateTime.UtcNow.ToString("o"));}
 }
 internal static string InstalledVersion(string target,bool importer){
  if(importer){string file=Path.Combine(target,"Core","GIMI","main.ini");if(!File.Exists(file))return "";var match=Regex.Match(File.ReadAllText(file),@"(?m)^global \$version = (\d+)\.*(\d)(\d*)");return match.Success?match.Groups[1].Value+"."+match.Groups[2].Value+"."+(match.Groups[3].Value.Length==0?"0":match.Groups[3].Value):"";}
  string manifest=Path.Combine(target,"Manifest.json");return File.Exists(manifest)?CodexChat.S(StoryClient.Read(manifest),"version"):"";
 }
 static void Install(string runtime,string repo,string prefix,string key,string target,bool importer,Action<ModProgress> progress,Func<bool> busy){
  string stage=target+"-stage-"+Guid.NewGuid().ToString("N"),backup=target+"-backup-"+Guid.NewGuid().ToString("N"),archive=stage+".zip",payload=Path.Combine(stage,"payload");bool old=false,installed=false,modsMoved=false;
  Directory.CreateDirectory(Path.GetDirectoryName(target));
  try{
   ModProgress.Report(progress,"실행 환경 확인 중…");var request=HttpTransport.Create("https://api.github.com/repos/"+repo+"/releases/latest");request.UserAgent="Catheryne";request.Timeout=30000;request.ReadWriteTimeout=30000;
   Dictionary<string,object> release;using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){string json=reader.ReadToEnd();if(json.Length>1024*1024)throw new InvalidDataException("배포 정보를 확인하지 못했습니다.");release=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(json);}
   string version=CodexChat.S(release,"tag_name").TrimStart('v');if(!Regex.IsMatch(version,@"^\d+\.\d+\.\d+$"))throw new InvalidDataException("배포 버전을 확인하지 못했습니다.");
   bool intact=importer?ModIntegration.ImporterReady(runtime)&&File.Exists(Path.Combine(target,"Core","GIMI","main.ini")):File.Exists(Path.Combine(target,"Manifest.json"))&&(repo.Contains("XXMI-Libs")?new[]{"3dmloader.dll","d3d11.dll","d3dcompiler_47.dll"}.All(x=>File.Exists(Path.Combine(target,x))):File.Exists(Path.Combine(target,"unlockfps_nc.exe")));
   System.Version localVersion,releaseVersion;if(intact&&System.Version.TryParse(InstalledVersion(target,importer),out localVersion)&&System.Version.TryParse(version,out releaseVersion)&&localVersion.CompareTo(releaseVersion)>=0)return;
   if(importer&&(!ModManager.IsInside(target,runtime)||target.Equals(Path.Combine(runtime,"Resources"),StringComparison.OrdinalIgnoreCase)||ModManager.IsInside(target,Path.Combine(runtime,"Resources"))))throw new InvalidDataException("GIMI 폴더는 실행 환경의 독립된 하위 폴더여야 합니다.");
   string name=prefix+version+".zip";var assets=((System.Collections.IEnumerable)release["assets"]).Cast<object>().Select(CodexChat.Map).ToArray();var asset=assets.Single(x=>CodexChat.S(x,"name")==name);string url=CodexChat.S(asset,"browser_download_url");if(!url.StartsWith("https://github.com/"+repo+"/releases/download/",StringComparison.Ordinal))throw new InvalidDataException("배포 주소를 확인하지 못했습니다.");
   var signature=Regex.Match(CodexChat.S(release,"body"),@"(?m)^## Signature\r?\n- ([A-Za-z0-9+/=]+)\r?$");if(!signature.Success)throw new InvalidDataException("배포 파일의 서명이 없습니다.");
   ModProgress.Download(url,archive,100L*1024*1024,progress,"실행 환경 다운로드 중…");ModProgress.Report(progress,"배포 파일 검증 중…");if(!Verify(key,signature.Groups[1].Value,File.ReadAllBytes(archive)))throw new InvalidDataException("모드 실행 환경 서명 검증에 실패했습니다.");
   ModProgress.Report(progress,"실행 환경 설치 중…");Directory.CreateDirectory(stage);string unpacked=Path.Combine(stage,"unpacked");ModManager.Extract(archive,unpacked,false);Directory.CreateDirectory(payload);
   // Preserve ShaderFixes and INI settings; move the user Mods directory unchanged at commit, without copying large payloads.
   if(importer&&Directory.Exists(target))ModManager.Copy(target,payload,false,"Mods");
   if(importer)Commands(Path.Combine(unpacked,"Core","auto_update.xcmd"),"PreInstall",payload);
   foreach(string file in Directory.GetFiles(unpacked,"*",SearchOption.AllDirectories)){string destination=Components.SafeArchivePath(payload,file.Substring(unpacked.Length+1));if(importer&&Path.GetFileName(file)=="d3dx.ini"&&File.Exists(destination))continue;Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(file,destination,true);}
   if(importer){Commands(Path.Combine(payload,"Core","auto_update.xcmd"),"PostInstall",payload);Directory.CreateDirectory(Path.Combine(payload,"Mods"));if(!File.Exists(Path.Combine(payload,"d3dx.ini"))||!File.Exists(Path.Combine(payload,"Core","GIMI","main.ini")))throw new InvalidDataException("GIMI 설치 파일이 없습니다.");}
   else{
    // Archive data is signed. Sidecar manifests are trusted only after every library signature verifies against the pinned author key.
    if(!File.Exists(Path.Combine(payload,"Manifest.json"))&&repo.Contains("XXMI-Libs")){
     var manifestAsset=assets.Single(x=>CodexChat.S(x,"name")=="Manifest.json");string manifestUrl=CodexChat.S(manifestAsset,"browser_download_url");if(!manifestUrl.StartsWith("https://github.com/"+repo+"/releases/download/",StringComparison.Ordinal))throw new InvalidDataException("배포 주소를 확인하지 못했습니다.");ModProgress.Download(manifestUrl,Path.Combine(payload,"Manifest.json"),1024*1024,progress,"배포 파일 검증 중…");
    }
    if(!File.Exists(Path.Combine(payload,"Manifest.json")))AtomicFile.Write(Path.Combine(payload,"Manifest.json"),CatheryneTools.Json().Serialize(new{version=version,signatures=new Dictionary<string,string>{{name,signature.Groups[1].Value}}}));
    var manifest=StoryClient.Read(Path.Combine(payload,"Manifest.json"));if(repo.Contains("XXMI-Libs")){var signatures=CodexChat.Map(manifest["signatures"]);foreach(string file in new[]{"3dmloader.dll","d3d11.dll","d3dcompiler_47.dll"})if(!File.Exists(Path.Combine(payload,file))||!Verify(key,CodexChat.S(signatures,file),File.ReadAllBytes(Path.Combine(payload,file))))throw new InvalidDataException("실행 라이브러리 서명 검증에 실패했습니다.");}
    else if(!File.Exists(Path.Combine(payload,"unlockfps_nc.exe")))throw new InvalidDataException("프레임 실행 구성요소가 없습니다.");
   }
   ModManager.EnsureEditable(busy);if(Components.ManagedProcessRunning(runtime))throw new InvalidOperationException("모드 실행기를 닫은 뒤 다시 시도해 주세요.");
   if(importer&&Directory.Exists(Path.Combine(target,"Mods"))){if((File.GetAttributes(Path.Combine(target,"Mods"))&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("링크된 모드 폴더를 확인해 주세요.");if(Directory.EnumerateFileSystemEntries(Path.Combine(payload,"Mods")).Any())throw new InvalidDataException("설치 패키지의 모드 폴더가 기존 모드와 충돌합니다.");ModManager.DeleteInside(Path.Combine(payload,"Mods"),payload);}
   if(Directory.Exists(target)){Directory.Move(target,backup);old=true;}
   if(importer&&Directory.Exists(Path.Combine(backup,"Mods"))){Directory.Move(Path.Combine(backup,"Mods"),Path.Combine(payload,"Mods"));modsMoved=true;}
   Directory.Move(payload,target);installed=true;
  }catch{if(modsMoved)Directory.Move(Path.Combine(installed?target:payload,"Mods"),Path.Combine(backup,"Mods"));if(installed)ModManager.DeleteInside(target,Path.GetDirectoryName(target));if(old&&Directory.Exists(backup))Directory.Move(backup,target);throw;}
  finally{if(File.Exists(archive))File.Delete(archive);ModManager.DeleteInside(stage,Path.GetDirectoryName(target));}if(old)ModManager.DeleteInside(backup,Path.GetDirectoryName(target));
 }
 internal static void Commands(string file,string section,string root){
  if(!File.Exists(file))return;bool active=false;
  foreach(string raw in File.ReadAllLines(file)){string line=raw.Split(';')[0].Trim();if(line.Length==0)continue;if(line.StartsWith("[")){active=line=="["+section+"]";continue;}if(!active)continue;int eq=line.IndexOf('=');if(eq<0||line.Substring(0,eq).Trim()!="delete")throw new InvalidDataException("지원하지 않는 설치 명령입니다.");string relative=line.Substring(eq+1).Trim().Replace('\\','/').TrimEnd('/');string[] parts=relative.Split('/');if(parts.Length<2||parts.Any(x=>x.Length==0||x=="."||x=="..")||!(parts[0].Equals("Core",StringComparison.OrdinalIgnoreCase)||parts[0].Equals("ShaderFixes",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("설치 명령 경로가 올바르지 않습니다.");string path=Components.SafeArchivePath(root,relative);if(Directory.Exists(path))ModManager.DeleteInside(path,root);else if(File.Exists(path))File.Delete(path);}
 }
 internal static void ValidateLibrary(string runtime,string name){ValidateLibraryAt(Path.Combine(runtime,"Resources","Packages","XXMI"),name,SharedKey);}
 internal static void ValidateDeployedLibrary(string runtime,string name){
  if(name!="d3dcompiler_47.dll")throw new InvalidDataException("지원하지 않는 실행 라이브러리입니다.");
  string deployed=Path.Combine(Path.GetDirectoryName(ModIntegration.ModsFolder(runtime)),name),official=Path.Combine(runtime,"Resources","Packages","XXMI",name);if(!File.Exists(deployed)||UpdateService.Hash(deployed)==UpdateService.Hash(official))return;
  var config=StoryClient.Read(Path.Combine(runtime,"XXMI Launcher Config.json"));var importer=CodexChat.Map(CodexChat.Map(CodexChat.Map(config["Importers"])["GIMI"])["Importer"]);object signatures;
  if(!importer.TryGetValue("deployed_migoto_signatures",out signatures)||!Verify(SharedKey,CodexChat.S(CodexChat.Map(signatures),name),File.ReadAllBytes(deployed)))throw new InvalidDataException("다른 실행 라이브러리가 외부에서 변경되었습니다. 기존 실행기에서 원본 엔진을 확인해 주세요.");
 }
 internal static void ValidateLibraryAt(string folder,string name,string key){
  if(!new[]{"3dmloader.dll","d3d11.dll","d3dcompiler_47.dll"}.Contains(name))throw new InvalidDataException("지원하지 않는 실행 라이브러리입니다.");
  string file=Path.Combine(folder,name);var manifest=StoryClient.Read(Path.Combine(folder,"Manifest.json"));object signatures;
  if(!File.Exists(file)||!manifest.TryGetValue("signatures",out signatures)||!Verify(key,CodexChat.S(CodexChat.Map(signatures),name),File.ReadAllBytes(file)))throw new InvalidDataException("실행 라이브러리 서명 검증에 실패했습니다.");
 }
 internal static bool Verify(string key,string signature,byte[] data){
  try{byte[] der=Convert.FromBase64String(key),sig=Convert.FromBase64String(signature);if(der.Length!=120||der[23]!=4||sig.Length<8||sig[0]!=0x30||sig[1]!=sig.Length-2)return false;byte[] blob=new byte[104];Array.Copy(BitConverter.GetBytes(0x33534345),blob,4);Array.Copy(BitConverter.GetBytes(48),0,blob,4,4);Array.Copy(der,24,blob,8,96);byte[] raw=new byte[96];int cursor=2;for(int part=0;part<2;part++){if(sig[cursor++]!=2)return false;int length=sig[cursor++];if(length<1||length>49||cursor+length>sig.Length)return false;if(length==49){if(sig[cursor++]!=0)return false;length--;}Array.Copy(sig,cursor,raw,part*48+48-length,length);cursor+=length;}if(cursor!=sig.Length)return false;using(var imported=CngKey.Import(blob,CngKeyBlobFormat.EccPublicBlob))using(var ec=new ECDsaCng(imported)){ec.HashAlgorithm=CngAlgorithm.Sha256;return ec.VerifyData(data,raw);}}
  catch(ArgumentException){return false;}catch(FormatException){return false;}catch(CryptographicException){return false;}catch(IndexOutOfRangeException){return false;}
 }
 // Prevent upstream quick launch from offering its own update dialog. Internal preparation owns installation.
 internal static void ConfigureQuickLaunch(Dictionary<string,object> data,string runtime=null){
  object value;var launcher=data.TryGetValue("Launcher",out value)?CodexChat.Map(value):new Dictionary<string,object>();data["Launcher"]=launcher;launcher["auto_update"]=false;
  var packages=data.TryGetValue("Packages",out value)?CodexChat.Map(value):new Dictionary<string,object>();data["Packages"]=packages;var installed=packages.TryGetValue("packages",out value)?CodexChat.Map(value):new Dictionary<string,object>();packages["packages"]=installed;
  foreach(var entry in installed){var package=CodexChat.Map(entry.Value);string version=runtime==null?CodexChat.S(package,"deployed_version"):InstalledVersion(entry.Key=="GIMI"?Path.GetDirectoryName(ModIntegration.ModsFolder(runtime)):Path.Combine(runtime,"Resources","Packages",entry.Key),entry.Key=="GIMI");package["latest_version"]=version;package["deployed_version"]=version;}
 }
}
