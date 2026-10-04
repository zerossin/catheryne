using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Generic;

internal sealed class ModEngineBuildResult {
 public byte[] Bytes;public string Commit;
}
internal static class ModEngineBuild {
 internal const string ToolsUrl="https://visualstudio.microsoft.com/visual-cpp-build-tools/";
 internal static string Tools(){
  string query=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft Visual Studio","Installer","vswhere.exe");if(!File.Exists(query))return null;
  using(var process=Process.Start(new ProcessStartInfo(query,"-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find MSBuild\\**\\Bin\\MSBuild.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){
   var text=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(10000)){process.Kill();return null;}
   return text.Result.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(File.Exists);
  }
 }
 internal static ModEngineBuildResult Build(string version,string stage,Action<ModProgress> progress){
  if(!Regex.IsMatch(version??"", @"^\d+\.\d+\.\d+$"))throw new InvalidDataException("설치된 엔진 버전을 확인할 수 없습니다.");
  string tools=Tools();if(tools==null){ModProgress.Report(progress,"C++ 빌드 도구 준비 중…");Components.Run("winget.exe","install --id Microsoft.VisualStudio.2022.BuildTools --exact --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity --override \"--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended\"",30*60*1000);tools=Tools();if(tools==null)throw new InvalidOperationException("C++ 빌드 도구를 확인하지 못했습니다. 관리의 설치 안내를 확인한 뒤 다시 적용해 주세요.");}
  string commit=SourceCommit(version),archive=Path.Combine(stage,"source.zip"),source=Path.Combine(stage,"source");
  ModProgress.Download("https://api.github.com/repos/"+ModRuntimePackages.SharedRepository+"/zipball/"+commit,archive,200L*1024*1024,progress,"원본 소스 다운로드 중…");ModProgress.Report(progress,"원본 소스 확인 중…");ModManager.Extract(archive,source,false);
  string solution=Directory.GetFiles(source,"StereovisionHacks.sln",System.IO.SearchOption.AllDirectories).Single();string project=Path.GetDirectoryName(solution);string log=Path.Combine(stage,"build.log");
  ModProgress.Report(progress,"실행 라이브러리 빌드 중…");Compile(tools,solution,log);string dll=Path.Combine(project,"x64","Release","d3d11.dll");var bytes=File.ReadAllBytes(dll);ModEngine4001.ValidateDll(bytes);
  // Retain exact source provenance and license with the local build; no modified upstream code is distributed.
  File.Copy(Path.Combine(project,"LICENSE.GPL.txt"),Path.Combine(stage,"LICENSE.GPL.txt"));AtomicFile.Write(Path.Combine(stage,"source.json"),CatheryneTools.Json().Serialize(new{repository=ModRuntimePackages.SharedRepository,version=version,commit=commit,archive_sha256=UpdateService.Hash(archive)}));
  // Build intermediates are derived; keep only provenance, the build log and the source archive for the user.
  ModManager.DeleteInside(source,stage);
  return new ModEngineBuildResult{Bytes=bytes,Commit=commit};
 }
 static Dictionary<string,object> ReadApi(string suffix){
  var request=HttpTransport.Create("https://api.github.com/repos/"+ModRuntimePackages.SharedRepository+"/"+suffix);request.UserAgent="Catheryne";request.Timeout=30000;request.ReadWriteTimeout=30000;
  using(var response=request.GetResponse())using(var input=new StreamReader(response.GetResponseStream())){string text=input.ReadToEnd();if(text.Length>1024*1024)throw new InvalidDataException("원본 버전 응답이 너무 큽니다.");return CatheryneTools.Json().Deserialize<Dictionary<string,object>>(text);}
 }
 internal static string SourceCommit(string version){
  if(!Regex.IsMatch(version??"", @"^\d+\.\d+\.\d+$"))throw new ArgumentException("원본 버전을 확인해 주세요.");
  var record=CodexChat.Map(ReadApi("git/ref/tags/v"+version)["object"]);
  for(int depth=0;depth<3;depth++){string sha=CodexChat.S(record,"sha");if(!Regex.IsMatch(sha,@"^[a-f0-9]{40}$"))break;if(CodexChat.S(record,"type")=="commit")return sha;if(CodexChat.S(record,"type")!="tag")break;record=CodexChat.Map(ReadApi("git/tags/"+sha)["object"]);}
  throw new InvalidDataException("원본 소스의 버전을 확인할 수 없습니다.");
 }
 internal static void Compile(string tools,string solution,string log){
  using(var writer=new StreamWriter(log,false,new UTF8Encoding(false)))
  using(var process=new Process{StartInfo=new ProcessStartInfo(tools,StoryClient.Quote(solution)+" /nologo /t:DirectX11 /p:Configuration=Release /p:Platform=x64 /m:2 /verbosity:minimal"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(solution)}}){
   object sync=new object();System.Diagnostics.DataReceivedEventHandler capture=(s,e)=>{if(e.Data!=null)lock(sync)writer.WriteLine(e.Data);};process.OutputDataReceived+=capture;process.ErrorDataReceived+=capture;process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
   if(!process.WaitForExit(20*60*1000)){
    try{using(var stop=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"taskkill.exe"),"/PID "+process.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){var output=stop.StandardOutput.ReadToEndAsync();var error=stop.StandardError.ReadToEndAsync();stop.WaitForExit(10000);}}catch{try{process.Kill();}catch{}}
    process.WaitForExit(10000);process.CancelOutputRead();process.CancelErrorRead();throw new IOException("빌드 시간이 초과되었습니다. 기존 엔진은 유지됩니다.");
   }process.WaitForExit();
   if(process.ExitCode!=0)throw new IOException("실행 라이브러리 빌드에 실패했습니다. C++ 도구와 Windows SDK 설치를 확인해 주세요. 기존 엔진은 유지됩니다.");
  }
 }
}
