using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
internal sealed class AppUpdateState {
 public int Schema=1;
 public string Repository,CheckedAt,State,AppVersion;
 public bool Available,Repair;
 public AppRelease Release;
 public bool Prepared;
}
internal sealed class AppUpdateService {
 readonly string app,root;
 readonly Func<string,string> fetch;
 readonly Func<bool> installationBusy;
 readonly object gate=new object();
 internal static bool Applying;
 internal string CurrentVersion {get{return AppUpdateProtocol.InstalledVersion(app);}}
 internal AppUpdateService(string directory,string data,Func<string,string> fetchRelease=null,Func<bool> isBusy=null){app=Path.GetFullPath(directory);root=Path.GetFullPath(data);fetch=fetchRelease??Latest;installationBusy=isBusy??(()=>Busy(root));}
 string StateFile {get{return Path.Combine(AppUpdateProtocol.Cache(root),"check.json");}}
 internal static HttpWebRequest Request(Uri uri){
  if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0)throw new InvalidDataException("Invalid app update transport");
  System.Net.ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072;
  var request=(HttpWebRequest)WebRequest.Create(uri);request.AllowAutoRedirect=false;request.UserAgent="Catheryne-AppUpdate";request.Timeout=20000;request.ReadWriteTimeout=30000;return request;
 }
 internal static string Latest(string repository){
  var request=Request(new Uri("https://api.github.com/repos/"+repository+"/releases/latest"));
  request.Accept="application/vnd.github+json";request.Headers["X-GitHub-Api-Version"]="2022-11-28";
  using(var response=request.GetResponse())using(var input=response.GetResponseStream())using(var output=new MemoryStream()){
   var bytes=new byte[16384];int count;while((count=input.Read(bytes,0,bytes.Length))>0){if(output.Length+count>1024*1024)throw new InvalidDataException("App release response too large");output.Write(bytes,0,count);}return System.Text.Encoding.UTF8.GetString(output.ToArray());
  }
 }
 internal static bool Fresh(AppUpdateState state,string repository,DateTime utc){
  DateTime at;return state!=null&&state.Schema==1&&state.Repository==repository&&DateTime.TryParse(state.CheckedAt,out at)&&utc>=at.ToUniversalTime()&&utc-at.ToUniversalTime()<TimeSpan.FromHours(state.State=="failed"||state.State=="unpublished"?1:6);
 }
 internal AppUpdateState Check(bool force=false){
  lock(gate){
   CleanCache(null,false);
   string repository=AppUpdateProtocol.InstalledRepository(app);
   if(repository==null)return new AppUpdateState{State="unconfigured"};
   AppUpdateState state=null;try{if(File.Exists(StateFile)){state=AppUpdateProtocol.Json().Deserialize<AppUpdateState>(AppUpdateProtocol.ReadText(StateFile));if(state.Release!=null)AppUpdateProtocol.ValidateRelease(state.Release,repository);}}catch{state=null;}
   if(!force&&Fresh(state,repository,DateTime.UtcNow)&&state.AppVersion==CurrentVersion){state.Available=AppUpdateProtocol.CanInstall(root,state.Release,CurrentVersion);state.Repair=AppUpdateProtocol.NeedsRepair(root,state.Release);state.Prepared=Prepared(state.Release);if(state.State!="failed")state.State=state.Available?"available":state.Release!=null?"current":state.State;return state;}
   var previous=state;
   state=new AppUpdateState{Repository=repository,AppVersion=CurrentVersion,CheckedAt=DateTime.UtcNow.ToString("o"),State="failed"};
   try{
    state.Release=AppUpdateProtocol.ParseRelease(fetch(repository),repository);
    state.Available=AppUpdateProtocol.CanInstall(root,state.Release,CurrentVersion);state.Repair=AppUpdateProtocol.NeedsRepair(root,state.Release);state.State=state.Available?"available":"current";
    state.Prepared=Prepared(state.Release);
   }catch(WebException error){var response=error.Response as HttpWebResponse;if(response!=null){if(response.StatusCode==HttpStatusCode.NotFound)state.State="unpublished";response.Dispose();}}
   catch(Exception){state.State="failed";}
   if(state.State=="failed"&&previous!=null&&previous.Release!=null&&AppUpdateProtocol.CanInstall(root,previous.Release,CurrentVersion)){state.Release=previous.Release;state.Available=true;state.Repair=AppUpdateProtocol.NeedsRepair(root,state.Release);state.Prepared=Prepared(state.Release);}
   AppUpdateProtocol.Write(StateFile,state);return state;
  }
 }
 internal bool Prepared(AppRelease release){
  if(release==null||!File.Exists(AppUpdateProtocol.Pending(root)))return false;
  try{var plan=AppUpdateProtocol.ReadPlan(root);AppUpdateProtocol.ValidatePlan(plan,app,root);if(plan.Release.Sha256!=release.Sha256)return false;AppUpdateProtocol.Verify(plan.Installer,plan.Release);return true;}catch{return false;}
 }
 internal AppUpdateState Download(AppRelease release,Action<double> progress=null,Action<AppRelease,string,Action<double>> download=null){
  lock(gate){
   string repository=AppUpdateProtocol.InstalledRepository(app);AppUpdateProtocol.ValidateRelease(release,repository);
   if(!AppUpdateProtocol.CanInstall(root,release,CurrentVersion))throw new InvalidDataException("App downgrade refused");
   string destination=AppUpdateProtocol.Package(root,release);Directory.CreateDirectory(Path.GetDirectoryName(destination));
   if(!File.Exists(destination)||!Verified(destination,release)){
    string temporary=destination+"."+Guid.NewGuid().ToString("N")+".part";
    try{(download??DownloadFile)(release,temporary,progress);AppUpdateProtocol.Verify(temporary,release);if(File.Exists(destination))File.Delete(destination);File.Move(temporary,destination);}
    finally{if(File.Exists(temporary))File.Delete(temporary);}
   }
   var plan=new AppUpdatePlan{AppDirectory=app,DataDirectory=root,Repository=repository,Installer=destination,PriorHash=AppUpdateProtocol.Hash(Path.Combine(app,"GenshinLauncher.exe")),Release=release};
   AppUpdateProtocol.ValidatePlan(plan,app,root);
   // A failed installation remains manual until a fresh, explicit preparation.
   if(!AppUpdateProtocol.NeedsRepair(root,release))AppUpdateProtocol.Write(Path.Combine(AppUpdateProtocol.Cache(root),"result.json"),new{state="prepared",version=release.Version});
   AppUpdateProtocol.Write(AppUpdateProtocol.Pending(root),plan);
   CleanCache(destination,true);
   var state=new AppUpdateState{Repository=repository,AppVersion=CurrentVersion,CheckedAt=DateTime.UtcNow.ToString("o"),State="available",Available=true,Repair=AppUpdateProtocol.NeedsRepair(root,release),Release=release,Prepared=true};AppUpdateProtocol.Write(StateFile,state);return state;
  }
 }
 internal void CleanCache(string keptPackage,bool removeOldInstallers){
  string folder=AppUpdateProtocol.Cache(root);if(!Directory.Exists(folder))return;
  foreach(string directory in Directory.GetDirectories(folder)){
   string name=Path.GetFileName(directory);if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?-[a-f0-9]{64}$"))continue;
   try{
    if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)continue;
    string version=name.Substring(0,name.LastIndexOf('-')),installer="Catheryne-Setup-"+version+".exe";
    foreach(string file in Directory.GetFiles(directory)){
     string leaf=Path.GetFileName(file);if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;
     bool partial=System.Text.RegularExpressions.Regex.IsMatch(leaf,"^"+System.Text.RegularExpressions.Regex.Escape(installer)+@"\.[a-f0-9]{32}\.part$")&&File.GetLastWriteTimeUtc(file)<DateTime.UtcNow.AddDays(-1);
     if(partial||removeOldInstallers&&leaf==installer&&!string.Equals(file,keptPackage,StringComparison.OrdinalIgnoreCase))try{File.Delete(file);}catch(IOException){}
    }
    if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);
   }catch(IOException){}catch(UnauthorizedAccessException){}
  }
 }

 static bool Verified(string path,AppRelease release){try{AppUpdateProtocol.Verify(path,release);return true;}catch(IOException){return false;}catch(InvalidDataException){return false;}}
 internal static void DownloadFile(AppRelease release,string destination,Action<double> progress){
  Uri url=new Uri(release.Url);HttpWebResponse response=null;
  try{
   for(int redirects=0;redirects<=5;redirects++){
    if(url.Host!="github.com"&&url.Host!="release-assets.githubusercontent.com"&&url.Host!="objects.githubusercontent.com")throw new InvalidDataException("Unexpected app download host");
    response=(HttpWebResponse)Request(url).GetResponse();int code=(int)response.StatusCode;
    if(code<300||code>=400)break;
    string location=response.Headers["Location"];response.Dispose();response=null;if(string.IsNullOrEmpty(location)||redirects==5)throw new InvalidDataException("Invalid app download redirect");url=new Uri(url,location);
   }
   if(response==null||response.StatusCode!=HttpStatusCode.OK||response.ContentLength>release.Size)throw new InvalidDataException("Invalid app package response");
   using(var input=response.GetResponseStream())using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
    var clock=Stopwatch.StartNew();var bytes=new byte[65536];long total=0;int count,reported=-1;
    while((count=input.Read(bytes,0,bytes.Length))>0){
     total+=count;if(total>release.Size||clock.Elapsed>TimeSpan.FromMinutes(30))throw new InvalidDataException("App package exceeded size or time limit");output.Write(bytes,0,count);
     int percent=(int)(100*total/release.Size);if(progress!=null&&percent>=reported+2){reported=percent;progress(percent);}
    }
    if(total!=release.Size)throw new InvalidDataException("Incomplete app package");
   }
  }finally{if(response!=null)response.Dispose();}
 }
 internal static bool Busy(string root){
  if(AppUpdateProtocol.EnvironmentActive()||ProcessGuard.Busy()||CollectionScanner.Busy())return true;
  try{GameInputLease.CheckAvailable(root);var tasks=new AiTaskStore(root);return tasks.List().Any(t=>t.State=="running"||t.State=="waiting");}catch{return true;}
 }
 internal string PrepareHandoff(){
  if(installationBusy())return null;
  var plan=AppUpdateProtocol.ReadPlan(root);AppUpdateProtocol.ValidatePlan(plan,app,root);AppUpdateProtocol.Verify(plan.Installer,plan.Release);
  string source=Path.Combine(app,"AppUpdater.exe"),helper=Path.Combine(AppUpdateProtocol.Cache(root),"AppUpdater.exe");File.Copy(source,helper,true);
  if(AppUpdateProtocol.Hash(source)!=AppUpdateProtocol.Hash(helper))throw new InvalidDataException("App updater copy verification failed");
  return helper;
 }
 internal bool StartHandoff(string helper){
  DailyAgent.Stop();
  using(var caller=Process.GetCurrentProcess())using(var process=Process.Start(new ProcessStartInfo(helper,"--apply "+AppUpdateProtocol.Quote(root)+" "+caller.Id+" "+caller.StartTime.ToUniversalTime().Ticks){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){if(process==null)throw new IOException("App updater did not start");}
  Applying=true;return true;
 }
 internal bool Handoff(){string helper=PrepareHandoff();return helper!=null&&StartHandoff(helper);}
 internal bool AutoApply(){
  try{
   object enabled;if(!AppPreferences.Read(root).TryGetValue("appAutoUpdate",out enabled)||!Equals(enabled,true)||!File.Exists(AppUpdateProtocol.Pending(root)))return false;
   string result=Path.Combine(AppUpdateProtocol.Cache(root),"result.json");if(File.Exists(result)&&AppUpdateProtocol.Text(AppUpdateProtocol.Json().Deserialize<Dictionary<string,object>>(AppUpdateProtocol.ReadText(result)),"state")=="failed")return false;
   return Handoff();
  }catch{return false;}
 }
}
