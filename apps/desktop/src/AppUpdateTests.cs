using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
internal static class AppUpdateTests {
 static void Check(bool value,string reason){if(!value)throw new Exception("App update: "+reason);}
 static void Reject(Action action,string reason){try{action();}catch(InvalidDataException){return;}catch(ArgumentException){return;}throw new Exception("App update accepted "+reason);}
 static string Fixture(string repo,string version,string hash,long size){
  return AppUpdateProtocol.Json().Serialize(new{tag_name="v"+version,draft=false,prerelease=false,assets=new[]{new{name="Catheryne-Setup-"+version+".exe",state="uploaded",size=size,digest="sha256:"+hash,browser_download_url="https://github.com/"+repo+"/releases/download/v"+version+"/Catheryne-Setup-"+version+".exe"}}});
 }
 internal static void Run(){
  string repo="example/catheryne",hash=new string('a',64),raw=Fixture(repo,"0.2.1",hash,2048);
  Check(AppUpdateProtocol.Repository(null)==null,"unpublished development builds stay unconfigured");
  Check(AppUpdateProtocol.Repository("https://github.com/Example/Catheryne")==repo,"repository identity ignores case");
  Check(AppUpdateProtocol.Repository("https://github.com/"+repo)==repo,"one canonical project URL");
  foreach(string url in new[]{"http://github.com/"+repo,"https://github.com.example/"+repo,"https://user@github.com/"+repo,"https://github.com/"+repo+"?feed=other","https://github.com/example"})Reject(()=>AppUpdateProtocol.Repository(url),"untrusted repository");
  var valid=AppUpdateProtocol.ParseRelease(raw,repo);Check(valid.Version=="0.2.1"&&valid.Sha256==hash,"one stable matching installer");
  foreach(string bad in new[]{raw.Replace("\"draft\":false","\"draft\":true"),raw.Replace("\"prerelease\":false","\"prerelease\":true"),raw.Replace("sha256:","sha1:"),raw.Replace("example/catheryne/releases","other/project/releases"),raw.Replace(".exe\"}",".exe?redirect=elsewhere\"}"),raw.Replace("\"size\":2048","\"size\":-1"),raw.Replace("\"state\":\"uploaded\"","\"state\":\"new\""),raw.Replace("v0.2.1","v0.2.1-rc")})Reject(()=>AppUpdateProtocol.ParseRelease(bad,repo),"invalid release");
  var duplicate=AppUpdateProtocol.Json().Deserialize<Dictionary<string,object>>(raw);object asset=((System.Collections.IEnumerable)duplicate["assets"]).Cast<object>().First();duplicate["assets"]=new[]{asset,asset};Reject(()=>AppUpdateProtocol.ParseRelease(AppUpdateProtocol.Json().Serialize(duplicate),repo),"ambiguous installer");
  Check(AppUpdateProtocol.Version("1.10.0")>AppUpdateProtocol.Version("1.9.9")&&AppUpdateProtocol.Version("0.2.0")==AppUpdateProtocol.Version("0.2.0.0"),"numeric version ordering");
  string folder=Path.Combine(Path.GetTempPath(),"catheryne-update-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try{
   string app=Path.Combine(folder,"app"),root=Path.Combine(folder,"data");Directory.CreateDirectory(app);
   File.Copy(typeof(AppUpdateTests).Assembly.Location,Path.Combine(app,"GenshinLauncher.exe"));
   File.WriteAllText(Path.Combine(app,"resources.json"),AppUpdateProtocol.Json().Serialize(new{projectUrl="https://github.com/"+repo}));
   var installed=AppUpdateProtocol.Version(AppUpdateProtocol.InstalledVersion(app));var package=AppUpdateProtocol.ParseRelease(Fixture(repo,(installed.Major+1)+".0.0",hash,2048),repo);string file=AppUpdateProtocol.Package(root,package);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllBytes(file,new byte[2048]);package.Sha256=AppUpdateProtocol.Hash(file);
   string expected=AppUpdateProtocol.Package(root,package);Directory.CreateDirectory(Path.GetDirectoryName(expected));File.Move(file,expected);AppUpdateProtocol.Verify(expected,package);File.WriteAllBytes(expected,new byte[1024]);Reject(()=>AppUpdateProtocol.Verify(expected,package),"truncated installer");File.WriteAllBytes(expected,Enumerable.Repeat((byte)1,2048).ToArray());Reject(()=>AppUpdateProtocol.Verify(expected,package),"corrupt installer");
   var plan=new AppUpdatePlan{AppDirectory=app,DataDirectory=root,Repository=repo,Installer=expected,PriorHash=AppUpdateProtocol.Hash(Path.Combine(app,"GenshinLauncher.exe")),Release=package};
   AppUpdateProtocol.ValidatePlan(plan,app,root);
   Reject(()=>AppUpdateProtocol.ValidatePlan(plan,app,Path.Combine(folder,"different")),"different data root");plan.Installer=Path.Combine(folder,"outside.exe");Reject(()=>AppUpdateProtocol.ValidatePlan(plan,app,root),"package outside update cache");plan.Installer=expected;
   string prior=plan.PriorHash;plan.PriorHash=new string('f',64);Reject(()=>AppUpdateProtocol.ValidatePlan(plan,app,root),"changed application binary");plan.PriorHash=prior;
   package.Version=AppUpdateProtocol.InstalledVersion(app);package.Tag="v"+package.Version;package.Url="https://github.com/"+repo+"/releases/download/"+package.Tag+"/Catheryne-Setup-"+package.Version+".exe";plan.Installer=AppUpdateProtocol.Package(root,package);Reject(()=>AppUpdateProtocol.ValidatePlan(plan,app,root),"downgrade or reinstall");
   AppUpdateProtocol.Write(Path.Combine(AppUpdateProtocol.Cache(root),"result.json"),new{state="failed",version=package.Version,packageHash=package.Sha256});AppUpdateProtocol.ValidatePlan(plan,app,root);Check(AppUpdateProtocol.CanInstall(root,package,AppUpdateProtocol.InstalledVersion(app)),"verified failed same-version installation can be retried manually");
   File.WriteAllText(Path.Combine(app,"install-manifest.txt"),"GenshinLauncher.exe|"+prior+"\nresources.json|"+AppUpdateProtocol.Hash(Path.Combine(app,"resources.json")));Check(AppUpdateProtocol.InstallationMatches(plan),"every installed manifest file verified");File.AppendAllText(Path.Combine(app,"resources.json")," ");Check(!AppUpdateProtocol.InstallationMatches(plan),"partial or modified install cannot report success");
   AppUpdateProtocol.Write(AppUpdateProtocol.Pending(root),plan);Check(AppUpdateProtocol.ReadPlan(root).PriorHash==prior,"atomic pending handoff roundtrip");Check(!Directory.EnumerateFiles(AppUpdateProtocol.Cache(root),"*.tmp").Any(),"no incomplete metadata remains");
   Check(AppUpdateProtocol.InstallerArguments(plan).Contains("/NOCLOSEAPPLICATIONS")&&AppUpdateProtocol.InstallerArguments(plan).Contains("/NORESTART"),"never force-stop other programs or reboot");Reject(()=>AppUpdateProtocol.Quote("bad\"argument"),"installer argument injection");
  }finally{Directory.Delete(folder,true);}
 }
}
