using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

internal sealed class ModEngineChoice {
 public int Schema=1;public bool Enabled,UnsafePresent,UnsafeValue,SignaturePresent;
 public string PackageVersion="",OriginalHash="",BuiltHash="",Commit="",UnsafeSignature="";
}
internal sealed class ModEngineStatus {
 public bool Selected,Applied,CanRestore,HasConsent;public string Message,Version;
}
internal static class ModEngine4001 {
 internal const string Help="설치된 XXMI 버전의 원본 소스를 직접 빌드해 적용합니다. 비보안 모드를 사용하며 4001 해결을 보장하지 않습니다. 엔진 업데이트 후에는 맞는 버전으로 다시 준비하고, 원본 복원은 이전 비보안 설정도 되돌립니다.";
 static string Folder(string runtime){return Path.Combine(runtime,"catheryne-4001");}
 static string StateFile(string runtime){return Path.Combine(Folder(runtime),"state.json");}
 static string Target(string runtime){string folder=Path.GetDirectoryName(ModIntegration.ModsFolder(runtime));if(!ModManager.IsInside(folder,runtime)||folder.Equals(Path.Combine(runtime,"Resources"),StringComparison.OrdinalIgnoreCase)||ModManager.IsInside(folder,Path.Combine(runtime,"Resources")))throw new InvalidDataException("GIMI 폴더는 실행 환경의 독립된 하위 폴더여야 합니다.");return Path.Combine(folder,"d3d11.dll");}
 static string Official(string runtime){return Path.Combine(runtime,"Resources","Packages","XXMI","d3d11.dll");}
 internal static ModEngineChoice Read(string runtime){
  if(runtime==null||!File.Exists(StateFile(runtime)))return null;
  var state=CatheryneTools.Json().Deserialize<ModEngineChoice>(File.ReadAllText(StateFile(runtime)));
  if(state==null||state.Schema!=1||!Hash(state.OriginalHash)||!Hash(state.BuiltHash)||!System.Text.RegularExpressions.Regex.IsMatch(state.PackageVersion??"",@"^\d+\.\d+\.\d+$"))throw new InvalidDataException("4001 대응 기록을 확인할 수 없습니다.");
  return state;
 }
 static bool Hash(string value){return System.Text.RegularExpressions.Regex.IsMatch(value??"",@"^[a-f0-9]{64}$");}
 static Dictionary<string,object> Section(Dictionary<string,object> parent,string name){object value;var result=parent.TryGetValue(name,out value)?CodexChat.Map(value):new Dictionary<string,object>();parent[name]=result;return result;}
 static Dictionary<string,object> Migoto(Dictionary<string,object> config){return Section(Section(Section(config,"Importers"),"GIMI"),"Migoto");}
 internal static ModEngineStatus Status(string runtime){
  if(runtime==null)return new ModEngineStatus{Message="실행 환경 준비 필요"};
  var state=Read(runtime);if(state==null||!state.Enabled)return new ModEngineStatus{Message="원본 엔진",CanRestore=false,HasConsent=state!=null};
  bool same=ModRuntimePackages.InstalledVersion(Path.GetDirectoryName(Official(runtime)),false)==state.PackageVersion;
  bool applied=same&&File.Exists(Target(runtime))&&UpdateService.Hash(Target(runtime))==state.BuiltHash;
  var config=StoryClient.Read(Path.Combine(runtime,"XXMI Launcher Config.json"));applied=applied&&Equals(Migoto(config).ContainsKey("unsafe_mode")?Migoto(config)["unsafe_mode"]:null,true)&&ModEngineConsent.Valid(runtime,config);
  return new ModEngineStatus{Selected=true,Applied=applied,CanRestore=true,HasConsent=true,Version=state.PackageVersion,Message=applied?"4001 대응 적용됨":same?"4001 대응 확인 필요":"업데이트 후 다시 준비 필요"};
 }
 static void Guard(string runtime,Func<bool> busy){ModManager.EnsureEditable(busy);if(Components.ManagedProcessRunning(runtime))throw new InvalidOperationException("모드 실행기를 닫은 뒤 다시 시도해 주세요.");foreach(string file in new[]{Target(runtime),Folder(runtime)})if((File.Exists(file)||Directory.Exists(file))&&(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("링크된 실행 환경 파일은 변경할 수 없습니다.");}
 internal static void Ensure(string runtime,Action<ModProgress> progress=null,Func<bool> busy=null,Func<string,string,Action<ModProgress>,ModEngineBuildResult> build=null,Action<string,string> verify=null){
  var state=Read(runtime);if(state==null||!state.Enabled)return;
  foreach(string library in new[]{"3dmloader.dll","d3d11.dll","d3dcompiler_47.dll"})(verify??ModRuntimePackages.ValidateLibrary)(runtime,library);
  if(verify==null)ModRuntimePackages.ValidateDeployedLibrary(runtime,"d3dcompiler_47.dll");
  string version=ModRuntimePackages.InstalledVersion(Path.GetDirectoryName(Official(runtime)),false);
  if(version!=state.PackageVersion){Apply(runtime,progress,busy,build,null,verify);return;}
  // An unexpected external replacement is visible and must not silently become our managed DLL.
  if(!File.Exists(Target(runtime))||UpdateService.Hash(Target(runtime))!=state.BuiltHash)throw new InvalidOperationException("4001 대응 파일이 변경되었습니다. 실행 환경에서 다시 적용하거나 원본 복원을 선택해 주세요.");
  string path=Path.Combine(runtime,"XXMI Launcher Config.json");var config=StoryClient.Read(path);var mode=Migoto(config);
  if(!Equals(mode.ContainsKey("unsafe_mode")?mode["unsafe_mode"]:null,true)||!ModEngineConsent.Valid(runtime,config))throw new InvalidOperationException("4001 대응 설정이 변경되었습니다. 실행 환경에서 다시 적용하거나 원본 복원을 선택해 주세요.");
 }
 internal static void Apply(string runtime,Action<ModProgress> progress=null,Func<bool> busy=null,Func<string,string,Action<ModProgress>,ModEngineBuildResult> build=null,Action<string> commitCheck=null,Action<string,string> verify=null){
  Guard(runtime,busy);string official=Official(runtime),version=ModRuntimePackages.InstalledVersion(Path.GetDirectoryName(official),false);
  foreach(string library in new[]{"3dmloader.dll","d3d11.dll","d3dcompiler_47.dll"})(verify??ModRuntimePackages.ValidateLibrary)(runtime,library);
  if(verify==null)ModRuntimePackages.ValidateDeployedLibrary(runtime,"d3dcompiler_47.dll");
  string packageHash=UpdateService.Hash(official);
  var prior=Read(runtime);string target=Target(runtime),path=Path.Combine(runtime,"XXMI Launcher Config.json");byte[] oldDll=File.Exists(target)?File.ReadAllBytes(target):null,oldConfig=File.ReadAllBytes(path);
  if((prior==null||!prior.Enabled)&&oldDll!=null&&UpdateService.Hash(target)!=packageHash)throw new InvalidDataException("현재 엔진이 외부에서 변경되었습니다. 기존 실행기에서 원본 엔진을 확인한 뒤 적용해 주세요.");
  var config=StoryClient.Read(path);var mode=Migoto(config);var choice=new ModEngineChoice{Enabled=true,PackageVersion=version,OriginalHash=packageHash};
  if(prior!=null&&prior.Enabled){choice.UnsafePresent=prior.UnsafePresent;choice.UnsafeValue=prior.UnsafeValue;choice.SignaturePresent=prior.SignaturePresent;choice.UnsafeSignature=prior.UnsafeSignature;}
  else {choice.UnsafePresent=mode.ContainsKey("unsafe_mode");choice.UnsafeValue=choice.UnsafePresent&&Equals(mode["unsafe_mode"],true);choice.SignaturePresent=mode.ContainsKey("unsafe_mode_signature");choice.UnsafeSignature=CodexChat.S(mode,"unsafe_mode_signature");}
  string stage=Folder(runtime)+"-stage-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(stage);
  try {
   ModProgress.Report(progress,"원본 소스 준비 중…");var result=(build??ModEngineBuild.Build)(version,stage,progress);ValidateDll(result.Bytes);using(var stream=new MemoryStream(result.Bytes))choice.BuiltHash=AppUpdateProtocol.Hash(stream);choice.Commit=result.Commit;
   if(!System.Text.RegularExpressions.Regex.IsMatch(choice.Commit??"",@"^[a-f0-9]{40}$"))throw new InvalidDataException("빌드한 원본 버전을 확인할 수 없습니다.");
   // Consent is recorded in XXMI's own per-user setting format; signed package files remain unchanged.
   mode["unsafe_mode"]=true;mode["unsafe_mode_signature"]=ModEngineConsent.Sign(runtime,config);
   File.WriteAllBytes(Path.Combine(stage,"original.dll"),File.ReadAllBytes(official));File.WriteAllBytes(Path.Combine(stage,"built.dll"),result.Bytes);AtomicFile.Write(Path.Combine(stage,"state.json"),CatheryneTools.Json().Serialize(choice));
   Guard(runtime,busy);if(!File.ReadAllBytes(path).SequenceEqual(oldConfig)||UpdateService.Hash(official)!=packageHash||!SameFile(target,oldDll))throw new IOException("실행 환경이 변경되어 적용하지 않았습니다. 다시 시도해 주세요.");
   ModProgress.Report(progress,"4001 대응 적용 중…");Commit(runtime,stage,oldDll,oldConfig,CatheryneTools.Json().Serialize(config),commitCheck);
  }catch{string log=Path.Combine(stage,"build.log");if(File.Exists(log))try{AtomicFile.Write(Path.Combine(runtime,"catheryne-4001-build.log"),File.ReadAllBytes(log));}catch{}throw;}finally{ModManager.DeleteInside(stage,Path.GetDirectoryName(stage));}
 }
 static bool SameFile(string path,byte[] bytes){return bytes==null?!File.Exists(path):File.Exists(path)&&File.ReadAllBytes(path).SequenceEqual(bytes);}
 static void Commit(string runtime,string stage,byte[] oldDll,byte[] oldConfig,string config,Action<string> check){
  string folder=Folder(runtime),backup=folder+"-previous-"+Guid.NewGuid().ToString("N");bool moved=false,installed=false;
  try {
   if(Directory.Exists(folder)){Directory.Move(folder,backup);moved=true;}Directory.Move(stage,folder);installed=true;
   AtomicFile.Write(Target(runtime),File.ReadAllBytes(Path.Combine(folder,"built.dll")));if(check!=null)check("dll");
   AtomicFile.Write(Path.Combine(runtime,"XXMI Launcher Config.json"),config);if(check!=null)check("config");
  }catch{
   if(oldDll==null){if(File.Exists(Target(runtime)))File.Delete(Target(runtime));}else AtomicFile.Write(Target(runtime),oldDll);
   AtomicFile.Write(Path.Combine(runtime,"XXMI Launcher Config.json"),oldConfig);
   if(installed)ModManager.DeleteInside(folder,runtime);if(moved)Directory.Move(backup,folder);throw;
  }
  if(moved)ModManager.DeleteInside(backup,runtime);
 }
 internal static void Restore(string runtime,Action<ModProgress> progress=null,Func<bool> busy=null,Action<string> commitCheck=null,Action<string,string> verify=null){
  Guard(runtime,busy);var choice=Read(runtime);if(choice==null||!choice.Enabled)return;
  // Restore the currently installed signed package after an update, never an incompatible old engine.
  (verify??ModRuntimePackages.ValidateLibrary)(runtime,"d3d11.dll");string target=Target(runtime),path=Path.Combine(runtime,"XXMI Launcher Config.json");byte[] oldDll=File.Exists(target)?File.ReadAllBytes(target):null,oldConfig=File.ReadAllBytes(path),oldState=File.ReadAllBytes(StateFile(runtime));
  var config=StoryClient.Read(path);var mode=Migoto(config);
  if(choice.UnsafePresent)mode["unsafe_mode"]=choice.UnsafeValue;else mode.Remove("unsafe_mode");
  if(choice.SignaturePresent)mode["unsafe_mode_signature"]=choice.UnsafeSignature;else mode.Remove("unsafe_mode_signature");
  ModProgress.Report(progress,"원본 엔진 복원 중…");Guard(runtime,busy);
  try{
   AtomicFile.Write(target,File.ReadAllBytes(Official(runtime)));if(commitCheck!=null)commitCheck("dll");AtomicFile.Write(path,CatheryneTools.Json().Serialize(config));choice.Enabled=false;AtomicFile.Write(StateFile(runtime),CatheryneTools.Json().Serialize(choice));if(commitCheck!=null)commitCheck("state");
  }catch{if(oldDll==null){if(File.Exists(target))File.Delete(target);}else AtomicFile.Write(target,oldDll);AtomicFile.Write(path,oldConfig);AtomicFile.Write(StateFile(runtime),oldState);throw;}
 }
 internal static void ValidateDll(byte[] data){
  if(data==null||data.Length<256||data.Length>50*1024*1024||data[0]!='M'||data[1]!='Z')throw new InvalidDataException("빌드 결과가 올바른 실행 라이브러리가 아닙니다.");
  int at=BitConverter.ToInt32(data,0x3c);if(at<64||at>data.Length-26||BitConverter.ToUInt32(data,at)!=0x4550||BitConverter.ToUInt16(data,at+4)!=0x8664||BitConverter.ToUInt16(data,at+24)!=0x20b||(BitConverter.ToUInt16(data,at+22)&0x2000)==0)throw new InvalidDataException("64비트 모드 실행 라이브러리를 확인할 수 없습니다.");
 }
}

// Adapter to XXMI's per-user ECDSA consent format, used only after the explicit Apply choice.
internal static class ModEngineConsent {
 internal static bool Valid(string runtime,Dictionary<string,object> config){
  try{string key=File.ReadAllText(Path.Combine(runtime,"Resources","Security","public_key.der"));byte[] user=Encoding.UTF8.GetBytes(Environment.UserName);var importer=CodexChat.Map(CodexChat.Map(CodexChat.Map(config["Importers"])["GIMI"])["Migoto"]);return ModRuntimePackages.Verify(key,CodexChat.S(importer,"unsafe_mode_signature"),user)&&ModRuntimePackages.Verify(key,CodexChat.S(CodexChat.Map(config["Security"]),"user_signature"),user);}catch{return false;}
 }
 internal static string Sign(string runtime,Dictionary<string,object> config){
  string folder=Path.Combine(runtime,"Resources","Security"),privateFile=Path.Combine(folder,"private_key.der"),publicFile=Path.Combine(folder,"public_key.der");byte[] user=Encoding.UTF8.GetBytes(Environment.UserName);
  string signature,publicKey;
  if(File.Exists(privateFile)||File.Exists(publicFile)){
   if(!File.Exists(privateFile)||!File.Exists(publicFile))throw new InvalidDataException("XXMI 사용자 확인 기록이 불완전합니다. 원본 실행기에서 설정을 확인해 주세요.");
   publicKey=File.ReadAllText(publicFile);using(var key=CngKey.Import(Convert.FromBase64String(File.ReadAllText(privateFile)),CngKeyBlobFormat.Pkcs8PrivateBlob))signature=Sign(key,user);
   if(!ModRuntimePackages.Verify(publicKey,signature,user))throw new InvalidDataException("XXMI 사용자 확인 기록을 검증하지 못했습니다.");
  }else{
   var options=new CngKeyCreationParameters{ExportPolicy=CngExportPolicies.AllowPlaintextExport};
   using(var key=CngKey.Create(CngAlgorithm.ECDsaP384,null,options)){
    signature=Sign(key,user);byte[] blob=key.Export(CngKeyBlobFormat.EccPublicBlob);
    byte[] prefix=Convert.FromBase64String("MHYwEAYHKoZIzj0CAQYFK4EEACIDYgAE");publicKey=Convert.ToBase64String(prefix.Concat(blob.Skip(8)).ToArray());
    Directory.CreateDirectory(folder);AtomicFile.Write(privateFile,Convert.ToBase64String(key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob)));AtomicFile.Write(publicFile,publicKey);
   }
  }
  object section;var security=config.TryGetValue("Security",out section)?CodexChat.Map(section):new Dictionary<string,object>();config["Security"]=security;security["user_signature"]=signature;return signature;
 }
 internal static string Sign(CngKey key,byte[] data){
  using(var ec=new ECDsaCng(key)){ec.HashAlgorithm=CngAlgorithm.Sha256;var raw=ec.SignData(data);var encoded=new List<byte>();
   for(int part=0;part<2;part++){var value=raw.Skip(part*48).Take(48).SkipWhile(x=>x==0).ToArray();if(value.Length==0)value=new byte[]{0};if(value[0]>=128)value=new byte[]{0}.Concat(value).ToArray();encoded.Add(2);encoded.Add((byte)value.Length);encoded.AddRange(value);}
   return Convert.ToBase64String(new byte[]{0x30,(byte)encoded.Count}.Concat(encoded).ToArray());
  }
 }
}
