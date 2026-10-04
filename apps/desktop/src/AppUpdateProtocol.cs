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
using System.Threading;
using System.Web.Script.Serialization;

// Shared by the app and the small, standalone installer handoff.
internal sealed class AppRelease {
 public string Tag, Version, Url, Sha256;
 public long Size;
}
internal sealed class AppUpdatePlan {
 public int Schema {get;set;}
 public string AppDirectory {get;set;}
 public string DataDirectory {get;set;}
 public string Repository {get;set;}
 public string Installer {get;set;}
 public string PriorHash {get;set;}
 public AppRelease Release {get;set;}
 public AppUpdatePlan(){Schema=1;}
}
internal static class AppUpdateProtocol {
 internal const string EnvironmentMutex="Local\\Catheryne.GameEnvironment",EnvironmentLifecycleMutex="Local\\Catheryne.GameEnvironment.Lifecycle";
 internal static bool EnvironmentActive(){
  try{using(var mutex=Mutex.OpenExisting(EnvironmentMutex)){
   bool owned=false;try{try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}return !owned;}finally{if(owned)mutex.ReleaseMutex();}
  }}catch(WaitHandleCannotBeOpenedException){return false;}catch(UnauthorizedAccessException){return true;}
 }
 internal const long MaximumPackage=2L*1024*1024*1024;
 internal static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=1024*1024,RecursionLimit=64};}
 internal static string Text(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)?Convert.ToString(value):"";}
 internal static string Repository(string projectUrl){
  if(string.IsNullOrWhiteSpace(projectUrl))return null;
  Uri uri;
  if(!Uri.TryCreate(projectUrl,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Query.Length!=0||uri.Fragment.Length!=0)throw new InvalidDataException("Invalid app release repository");
  string repo=uri.AbsolutePath.Trim('/');
  if(!Regex.IsMatch(repo,@"^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$"))throw new InvalidDataException("Invalid app release repository");
  return repo.ToLowerInvariant();
 }
 internal static Version Version(string value){
  Version version;
  if(!Regex.IsMatch(value??"",@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*))?$")||!System.Version.TryParse(value,out version))throw new InvalidDataException("Invalid app version");
  return new Version(version.Major,version.Minor,version.Build,Math.Max(0,version.Revision));
 }
 internal static AppRelease ParseRelease(string text,string repository){
  var data=Json().Deserialize<Dictionary<string,object>>(text);
  if(!data.ContainsKey("draft")||!data.ContainsKey("prerelease")||!Equals(data["draft"],false)||!Equals(data["prerelease"],false))throw new InvalidDataException("Not a stable app release");
  string tag=Text(data,"tag_name"),version=tag.StartsWith("v",StringComparison.Ordinal)?tag.Substring(1):tag;
  Version(version);
  object raw;if(!data.TryGetValue("assets",out raw)||!(raw is IEnumerable)||raw is string)throw new InvalidDataException("Missing app installer");
  string name="Catheryne-Setup-"+version+".exe";
  var matches=((IEnumerable)raw).Cast<object>().OfType<Dictionary<string,object>>().Where(a=>Text(a,"name")==name).ToArray();
  if(matches.Length!=1)throw new InvalidDataException("Missing or ambiguous app installer");
  var asset=matches[0];
  if(Text(asset,"state")!="uploaded")throw new InvalidDataException("Incomplete app installer");
  string digest=Text(asset,"digest");long size;
  if(!long.TryParse(Text(asset,"size"),out size))throw new InvalidDataException("Invalid app installer size");
  var result=new AppRelease{Tag=tag,Version=version,Url=Text(asset,"browser_download_url"),Sha256=digest.StartsWith("sha256:",StringComparison.Ordinal)?digest.Substring(7):"",Size=size};
  ValidateRelease(result,repository);return result;
 }
 internal static void ValidateRelease(AppRelease release,string repository){
  if(release==null||repository!=Repository("https://github.com/"+repository))throw new InvalidDataException("Invalid app release");
  Version(release.Version);
  if(release.Tag!=release.Version&&release.Tag!="v"+release.Version)throw new InvalidDataException("App tag and version disagree");
  Uri uri;
  if(!Uri.TryCreate(release.Url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Query.Length!=0||uri.Fragment.Length!=0||(!uri.AbsolutePath.StartsWith("/"+repository+"/releases/download/",StringComparison.OrdinalIgnoreCase)||uri.AbsolutePath.Substring(("/"+repository+"/releases/download/").Length)!=release.Tag+"/Catheryne-Setup-"+release.Version+".exe")||!Regex.IsMatch(release.Sha256??"","^[a-f0-9]{64}$")||release.Size<1024||release.Size>MaximumPackage)throw new InvalidDataException("Invalid official app installer");
 }
 internal static string Hash(Stream stream){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
 internal static string Hash(string file){using(var stream=File.OpenRead(file))return Hash(stream);}
 internal static void Verify(Stream stream,AppRelease release){
  if(stream.Length!=release.Size||Hash(stream)!=release.Sha256)throw new InvalidDataException("App installer verification failed");
 }
 internal static void Verify(string file,AppRelease release){using(var stream=File.OpenRead(file))Verify(stream,release);}
 internal static string ReadText(string path,int limit=1024*1024){if(new FileInfo(path).Length>limit)throw new InvalidDataException("App update metadata too large");return File.ReadAllText(path);}
 internal static void Write(string path,object value){
  Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temporary,Json().Serialize(value),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
  finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 internal static string Quote(string value){
  if(value==null||value.IndexOf('"')>=0||value.IndexOf('\r')>=0||value.IndexOf('\n')>=0)throw new InvalidDataException("Invalid installer argument");
  return "\""+value.TrimEnd('\\')+"\"";
 }
 internal static string Cache(string root){return Path.Combine(Path.GetFullPath(root),"cache","app-updates");}
 internal static string Package(string root,AppRelease release){return Path.Combine(Cache(root),release.Version+"-"+release.Sha256,"Catheryne-Setup-"+release.Version+".exe");}
 internal static string Pending(string root){return Path.Combine(Cache(root),"pending.json");}
 internal static AppUpdatePlan ReadPlan(string root){return Json().Deserialize<AppUpdatePlan>(ReadText(Pending(root)));}
 internal static string InstalledRepository(string app){var data=Json().Deserialize<Dictionary<string,object>>(ReadText(Path.Combine(app,"resources.json")));return Repository(Text(data,"projectUrl"));}
 internal static string InstalledVersion(string app){var info=FileVersionInfo.GetVersionInfo(Path.Combine(app,"GenshinLauncher.exe"));try{Version(info.ProductVersion);return info.ProductVersion;}catch(InvalidDataException){return info.FileVersion;}}
 internal static bool NeedsRepair(string root,AppRelease release){
  if(release==null)return false;string file=Path.Combine(Cache(root),"result.json");
  try{if(!File.Exists(file))return false;var result=Json().Deserialize<Dictionary<string,object>>(ReadText(file));return Text(result,"state")=="failed"&&Text(result,"version")==release.Version&&Text(result,"packageHash")==release.Sha256;}catch{return false;}
 }
 internal static bool CanInstall(string root,AppRelease release,string current){
  if(release==null)return false;int order=Version(release.Version).CompareTo(Version(current));return order>0||order==0&&NeedsRepair(root,release);
 }
 internal static void ValidatePlan(AppUpdatePlan plan,string app,string root){
  app=Path.GetFullPath(app).TrimEnd('\\');root=Path.GetFullPath(root).TrimEnd('\\');
  if(plan==null||plan.Schema!=1||!string.Equals(Path.GetFullPath(plan.AppDirectory).TrimEnd('\\'),app,StringComparison.OrdinalIgnoreCase)||!string.Equals(Path.GetFullPath(plan.DataDirectory).TrimEnd('\\'),root,StringComparison.OrdinalIgnoreCase)||plan.Repository!=InstalledRepository(app))throw new InvalidDataException("App update belongs to another installation");
  ValidateRelease(plan.Release,plan.Repository);
  if(!string.Equals(Path.GetFullPath(plan.Installer),Package(root,plan.Release),StringComparison.OrdinalIgnoreCase)||!CanInstall(root,plan.Release,InstalledVersion(app))||Hash(Path.Combine(app,"GenshinLauncher.exe"))!=plan.PriorHash)throw new InvalidDataException("App update installation changed");
 }
 internal static string LockName(string app){return "Local\\Catheryne.AppUpdate."+Hash(new MemoryStream(Encoding.UTF8.GetBytes(Path.GetFullPath(app).TrimEnd('\\').ToUpperInvariant()))).Substring(0,24);}
 internal static bool Installing(string app){
  using(var mutex=new Mutex(false,LockName(app))){bool held=false;try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}return !held;}finally{if(held)mutex.ReleaseMutex();}}
 }
 internal static string InstallerArguments(AppUpdatePlan plan){
  return "/SP- /SILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /NOFORCECLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /DIR="+Quote(Path.GetFullPath(plan.AppDirectory));
 }
 internal static bool InstallationMatches(AppUpdatePlan plan){
  if(Version(InstalledVersion(plan.AppDirectory))!=Version(plan.Release.Version)||InstalledRepository(plan.AppDirectory)!=plan.Repository)return false;
  string manifest=Path.Combine(plan.AppDirectory,"install-manifest.txt");if(!File.Exists(manifest))return false;
  string[] lines=ReadText(manifest,4*1024*1024).Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
  if(lines.Length<2||lines.Length>30000)return false;
  foreach(string line in lines){
   var split=line.Split('|');string relative=split[0].TrimStart('\uFEFF');
   if(split.Length!=2||Path.IsPathRooted(relative)||relative.Split('\\','/').Any(p=>p==".."||p==".")||relative.IndexOf(':')>=0||!Regex.IsMatch(split[1],"^[a-fA-F0-9]{64}$"))return false;
   string file=Path.GetFullPath(Path.Combine(plan.AppDirectory,relative));if(!file.StartsWith(Path.GetFullPath(plan.AppDirectory).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)||!File.Exists(file)||Hash(file)!=split[1].ToLowerInvariant())return false;
  }return true;
 }
}
