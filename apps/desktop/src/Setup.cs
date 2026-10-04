using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

internal sealed class Installation {
 internal string Engine,Data;
 internal string Config {get{return Path.Combine(Path.GetDirectoryName(Engine),"fps_config.json");}}
}

internal static class Setup {
 internal static bool ValidGame(string path) {
  if(string.IsNullOrWhiteSpace(path))return false;
  try {
  string name=Path.GetFileName(path);
  return (name.Equals("GenshinImpact.exe",StringComparison.OrdinalIgnoreCase)||name.Equals("YuanShen.exe",StringComparison.OrdinalIgnoreCase))&&File.Exists(path)&&Directory.Exists(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+"_Data"));
  }catch(ArgumentException){return false;}catch(NotSupportedException){return false;}
 }
 internal static IEnumerable<string> Candidates(string root) {
  if(string.IsNullOrWhiteSpace(root))yield break;
  foreach(string suffix in new[]{"","Genshin Impact game","games\\Genshin Impact game","Genshin Impact\\Genshin Impact game","Genshin Impact","YuanShen Game","games\\YuanShen Game"})
   foreach(string exe in new[]{"GenshinImpact.exe","YuanShen.exe"})yield return Path.Combine(root,suffix,exe);
 }
 static List<string> Roots() {
  var roots=new List<string>{AppDomain.CurrentDomain.BaseDirectory,Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."))};
  foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64}) {
   try {using(var registry=RegistryKey.OpenBaseKey(hive,view))using(var uninstall=registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")) {
    if(uninstall==null)continue;
    foreach(string name in uninstall.GetSubKeyNames())using(var key=uninstall.OpenSubKey(name)) {
     if(key==null)continue;
     string display=Convert.ToString(key.GetValue("DisplayName"));
     if(!Regex.IsMatch(display+" "+name,"Genshin|YuanShen|原神|HoYoPlay",RegexOptions.IgnoreCase))continue;
     string location=Convert.ToString(key.GetValue("InstallLocation"));if(!string.IsNullOrWhiteSpace(location))roots.Add(location.Trim('"'));
     string icon=Convert.ToString(key.GetValue("DisplayIcon"));if(!string.IsNullOrWhiteSpace(icon))roots.Add(Path.GetDirectoryName(Regex.Replace(icon,@",-?\d+$","").Trim('"')));
    }
   }}catch(System.Security.SecurityException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
  }
  foreach(string program in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}) {
   roots.Add(Path.Combine(program,"HoYoPlay"));roots.Add(Path.Combine(program,"Genshin Impact"));
  }
  foreach(var drive in DriveInfo.GetDrives())if(drive.DriveType==DriveType.Fixed&&drive.IsReady)foreach(string folder in new[]{"HoYoPlay","Games\\HoYoPlay","Genshin Impact","Games\\Genshin Impact"})roots.Add(Path.Combine(drive.RootDirectory.FullName,folder));
  // Legacy launchers store the game directory here. Only read known config files.
  foreach(string root in roots.ToArray())try {
   string ini=Path.Combine(root,"config.ini");if(!File.Exists(ini))continue;
   var match=Regex.Match(File.ReadAllText(ini),@"(?im)^game_install_path\s*=\s*(.+)$");
   if(match.Success)roots.Add(match.Groups[1].Value.Trim().Replace('/','\\'));
  }catch(IOException){}catch(UnauthorizedAccessException){}
  return roots.Where(r=>!string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
 }
 internal static List<string> Detect() {
  return Roots().SelectMany(Candidates).Where(ValidGame).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
 }
 internal static string FindHoYoPlay(string game) {
  var roots=Roots();
  var parent=new DirectoryInfo(Path.GetDirectoryName(game));
  for(int i=0;i<4&&parent!=null;i++,parent=parent.Parent)roots.Insert(0,parent.FullName);
  return roots.Select(r=>Path.Combine(r,"launcher.exe")).FirstOrDefault(File.Exists);
 }
 internal static bool HasDesktopRuntime(int major=8) {
  string shared=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet","shared","Microsoft.WindowsDesktop.App");
  return Directory.Exists(shared)&&Directory.GetDirectories(shared,major+".*").Any(p=>File.Exists(Path.Combine(p,"PresentationFramework.dll")));
 }
 internal static Dictionary<string,object> Defaults(string game) {
  var defaults=UnlockerOptions.Defaults();defaults["GamePath"]=game;defaults["AutoStart"]=true;return defaults;
 }
 internal static Installation SaveInstallation(string chosen,string storage,string binding,string gamePath) {
  if(!ValidGame(gamePath))throw new InvalidDataException("게임 설치를 확인할 수 없습니다.");
  var json=new JavaScriptSerializer();
  string configPath=Path.Combine(Path.GetDirectoryName(chosen),"fps_config.json");
  var config=File.Exists(configPath)?json.Deserialize<Dictionary<string,object>>(File.ReadAllText(configPath)):Defaults(gamePath);
  if(config==null)throw new InvalidDataException("기존 언락커 설정을 읽을 수 없습니다.");
  foreach(var item in Defaults(gamePath))if(!config.ContainsKey(item.Key))config[item.Key]=item.Value;
  config["GamePath"]=Path.GetFullPath(gamePath);
  Directory.CreateDirectory(storage);
  AtomicFile.Write(configPath,json.Serialize(config),File.Exists(configPath)?configPath+".launcher-setup-backup":null);
  if(!File.Exists(Path.Combine(storage,"launcher-settings.json")))AtomicFile.Write(Path.Combine(storage,"launcher-settings.json"),json.Serialize(new{LaunchOnOpen=false,AutoUpdateUnlocker=true}));
  AtomicFile.Write(binding,json.Serialize(new{Engine=chosen,Data=storage}));
  return new Installation{Engine=chosen,Data=storage};
 }
 internal static string DataFolder {get{return Environment.GetEnvironmentVariable("CATHERYNE_TOOL_DATA")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Catheryne");}}
 internal static string BindingPath {get{return Path.Combine(DataFolder,"installation.json");}}
 internal static Installation FindExisting() {
  string root=AppDomain.CurrentDomain.BaseDirectory;
  var json=new JavaScriptSerializer();
  Installation result=null;
  if(File.Exists(BindingPath))try {
   var saved=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(BindingPath));
   if(saved!=null)result=new Installation{Engine=Convert.ToString(saved["Engine"]),Data=saved.ContainsKey("Data")?Convert.ToString(saved["Data"]):DataFolder};
  }catch(Exception error) {if(!(error is IOException||error is ArgumentException||error is KeyNotFoundException))throw;}
  string legacy=Path.GetFullPath(Path.Combine(root,"..","games","unlockfps_nc.exe"));
  if(result==null&&File.Exists(legacy)&&File.Exists(Path.Combine(Path.GetDirectoryName(legacy),"fps_config.json")))result=new Installation{Engine=legacy,Data=root};
  return result;
 }
 internal static bool Ready(Installation installation) {
  if(installation==null||!File.Exists(installation.Config))return false;
  if(!File.Exists(installation.Engine)){string prefs=Path.Combine(installation.Data,"launcher-settings.json");if(!File.Exists(prefs))return false;var options=StoryClient.Read(prefs);if(!options.ContainsKey("UseUnlockerLaunch")||Convert.ToBoolean(options["UseUnlockerLaunch"]))return false;}
  try {
   var saved=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(installation.Config));
   return saved!=null&&new[]{"GamePath","FPSTarget","AutoStart","AutoClose","Fullscreen"}.All(saved.ContainsKey)&&ValidGame(Convert.ToString(saved["GamePath"]));
  }catch(ArgumentException){return false;}catch(IOException){return false;}
 }
 internal static async Task<Installation> Prepare(string gamePath,string selectedEngine,Installation existing,bool installUnlocker=true) {
  if(!ValidGame(gamePath))throw new InvalidDataException("원신 실행 파일과 게임 데이터 폴더를 확인해 주세요.");
  if(ProcessGuard.Busy())throw new InvalidOperationException("게임과 언락커를 종료한 뒤 설정해 주세요.");
  string chosen=selectedEngine.Trim();
  if(installUnlocker&&chosen.Length>0&&!File.Exists(chosen))throw new FileNotFoundException("선택한 언락커가 없습니다. 자동 설치를 선택하거나 다른 파일을 지정해 주세요.");
  if(chosen.Length==0)chosen=Path.Combine(DataFolder,"engine","unlockfps_nc.exe");
  if(Path.GetDirectoryName(chosen).Equals(Path.GetDirectoryName(gamePath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("언락커는 게임 실행 파일과 다른 폴더에 두세요.");
  string storage=existing!=null&&chosen==existing.Engine?existing.Data:DataFolder;
  Directory.CreateDirectory(DataFolder);Directory.CreateDirectory(storage);Directory.CreateDirectory(Path.GetDirectoryName(chosen));
  if(installUnlocker&&!File.Exists(chosen))await Task.Run(()=>new UpdateService(chosen,Path.Combine(storage,"unlocker-state.json"),ProcessGuard.Busy).Check(true));
  if(ProcessGuard.Busy())throw new InvalidOperationException("게임 또는 언락커가 실행되었습니다. 종료 후 다시 시도해 주세요.");
  var result=SaveInstallation(chosen,storage,BindingPath,gamePath);
  string preferences=Path.Combine(storage,"launcher-settings.json");var options=StoryClient.Read(preferences);options["UseUnlockerLaunch"]=installUnlocker;AtomicFile.Write(preferences,new JavaScriptSerializer().Serialize(options));return result;
 }
}
