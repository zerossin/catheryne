using System;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

// Both proofs describe the same original XXMI connection; neither starts an injector.
internal static class ModLaunch {
 internal static string Result(string log){
  int start=log.LastIndexOf("root DEBUG App Start",StringComparison.Ordinal);if(start>=0)log=log.Substring(start);
  if(log.Contains("Failed to verify d3d11.dll ->"))return "failed";
  if(log.Contains("Successfully passed early d3d11.dll ->")||log.Contains("Successfully passed late d3d11.dll ->"))return "verified";
  return "pending";
 }
 internal static bool Initialized(string log,string gamePath,string dllPath,DateTime sinceUtc){
  const string marker="D3D11 DLL starting init - ";int start=log.LastIndexOf(marker,StringComparison.Ordinal);if(start<0)return false;
  string[] lines=log.Substring(start).Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);int date=lines[0].LastIndexOf(" - ",StringComparison.Ordinal);DateTime initialized;
  if(date<0||!DateTime.TryParseExact(lines[0].Substring(date+3).Trim(),"ddd MMM d HH:mm:ss yyyy",CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces|DateTimeStyles.AssumeLocal,out initialized))return false;
  // Native log timestamps have one-second precision.
  DateTime boundary=new DateTime(sinceUtc.Ticks-sinceUtc.Ticks%TimeSpan.TicksPerSecond,DateTimeKind.Utc);if(initialized.ToUniversalTime()<boundary)return false;
  bool game=false,dll=false;foreach(string line in lines){if(line.StartsWith("Game path: ",StringComparison.Ordinal))game=SamePath(line.Substring(11),gamePath);if(line.StartsWith("3DMigoto path: ",StringComparison.Ordinal))dll=SamePath(line.Substring(14),dllPath);}return game&&dll;
 }
 static bool SamePath(string actual,string expected){try{return string.Equals(Path.GetFullPath(actual.Trim()),Path.GetFullPath(expected),StringComparison.OrdinalIgnoreCase);}catch(ArgumentException){return false;}catch(NotSupportedException){return false;}}
 internal static string DllLog(string file){try{if(!File.Exists(file))return "";using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream)){char[] buffer=new char[8192];return new string(buffer,0,reader.ReadBlock(buffer,0,buffer.Length));}}catch(IOException){return "";}catch(UnauthorizedAccessException){return "";}}
 internal static void Wait(Process process,Func<string> read,Action<ModProgress> progress,int timeout=180000,Func<bool> initialized=null){
  var watch=Stopwatch.StartNew();string previous="";
  while(watch.ElapsedMilliseconds<timeout){string log=read();string result=Result(log);if(result=="verified"||initialized!=null&&initialized()){ModProgress.Report(progress,"모드 실행 환경 연결을 확인했습니다.");return;}if(result=="failed")throw new InvalidOperationException("게임은 실행됐지만 모드 연결을 확인하지 못했습니다. 모드 상세의 관리에서 실행 로그를 확인해 주세요.");
   string stage=log.Contains("ApplicationEvents.VerifyHook")?"게임에 모드 연결 확인 중…":log.Contains("ApplicationEvents.StartGameExe")?"모드와 함께 게임 실행 중…":"모드 실행 환경 시작 중…";if(stage!=previous){ModProgress.Report(progress,stage);previous=stage;}
   if(process.WaitForExit(200)){log=read();if(Result(log)=="verified"||initialized!=null&&initialized())return;throw new InvalidOperationException("모드 실행 환경이 연결 확인 전에 종료됐습니다. 실행 환경의 로그를 확인해 주세요.");}
  }throw new TimeoutException("모드 연결 확인 시간이 초과됐습니다. 게임과 모드 실행 환경의 로그를 확인해 주세요.");
 }
 internal static string Read(string file,long offset){if(!File.Exists(file))return "";using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if(stream.Length<offset)offset=0;stream.Position=Math.Max(offset,stream.Length-2*1024*1024);using(var reader=new StreamReader(stream))return reader.ReadToEnd();}}
 // Configure only supported startup presentation in the managed projection.
 // Keep signed Core files, manual F12 help, and diagnostic overlays unchanged.
 internal static void ConfigurePresentation(string runtime,string projection){
  string importer=Path.GetDirectoryName(ModIntegration.ModsFolder(runtime)),main=Path.Combine(importer,"Core","GIMI","main.ini");if(!File.Exists(main))return;
  string source=File.ReadAllText(main);var scope=Regex.Match(source,@"(?m)^\s*namespace\s*=\s*([A-Za-z0-9_\\]+)\s*(?:;[^\r\n]*)?$");if(!scope.Success)return;
  bool guide=Regex.IsMatch(source,@"(?m)^\s*global\s+persist(?:ent)?\s+\$first_run\s*="),banner=Regex.IsMatch(source,@"(?m)^\s*\[ResourceVersionNotification\]\s*(?:;[^\r\n]*)?$");if(!guide&&!banner)return;
  string settings="; Quiet automatic startup notices; F12 remains available.\nnamespace = Catheryne.Startup\n[Constants]\n",ns=scope.Groups[1].Value;
  if(guide)settings+="post $\\"+ns+"\\first_run = 0\n";
  if(banner){
   // Substantiate before clearing: native lazy creation resets is_null on first use.
   string resource="Resource\\"+ns+"\\VersionNotification";
   settings+="post "+resource+" = ref "+resource+"\npost "+resource+" = null\n";
  }
  AtomicFile.Write(Path.Combine(projection,"Startup.ini"),settings);
 }
 internal static void Start(string runtime,string gamePath,Action<ModProgress> progress){
  string file=Path.Combine(runtime,"XXMI Launcher Log.txt"),dllPath=Path.Combine(Path.GetDirectoryName(ModIntegration.ModsFolder(runtime)),"d3d11.dll"),dllFile=Path.Combine(Path.GetDirectoryName(dllPath),"d3d11_log.txt");long offset=File.Exists(file)?new FileInfo(file).Length:0;string previousDll=DllLog(dllFile);DateTime since=DateTime.UtcNow;
  using(var process=Process.Start(ModIntegration.StartInfo(runtime,gamePath))){if(process==null)throw new IOException("모드 실행 요청을 전달하지 못했습니다.");Wait(process,()=>Read(file,offset),progress,initialized:()=>{string current=DllLog(dllFile);return current!=previousDll&&Initialized(current,gamePath,dllPath,since);});}
 }
}
