using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

// One Windows execution boundary for the GUI and stdio clients. Domain code stays unchanged.
internal static class AppRuntime {
 const string Prefix="Catheryne.Runtime.";
 static readonly Encoding Utf8=new UTF8Encoding(false);
 internal static bool Redirected(string root){
  Directory.CreateDirectory(root);
  string probe=Path.Combine(root,".storage-"+Guid.NewGuid().ToString("N")+".tmp");
  using(var file=new FileStream(probe,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete,1,FileOptions.DeleteOnClose))
   return !string.Equals(Path.GetFullPath(probe),NativePaths.Resolve(probe),StringComparison.OrdinalIgnoreCase);
 }
 static object Invoke(object target,string member,params object[] args){return target.GetType().InvokeMember(member,BindingFlags.InvokeMethod,null,target,args);}
 static object Property(object target,string member){return target.GetType().InvokeMember(member,BindingFlags.GetProperty,null,target,null);}
 static void Release(object value){if(value!=null&&System.Runtime.InteropServices.Marshal.IsComObject(value))System.Runtime.InteropServices.Marshal.ReleaseComObject(value);}
 [System.Runtime.InteropServices.DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int information,out int elevation,int size,out int returned);
 internal static bool Elevated(){using(var identity=WindowsIdentity.GetCurrent()){int elevation,size;if(!GetTokenInformation(identity.Token,20,out elevation,4,out size))throw new System.ComponentModel.Win32Exception();return elevation!=0;}}
 internal static void LaunchDesktop(string executable,string arguments){
  object shell=null,windows=null,desktop=null,document=null,application=null;
  try{
   shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
   windows=Invoke(shell,"Windows");
   var lookup=new object[]{0,0,8,0,1};
   desktop=Invoke(windows,"FindWindowSW",lookup);
   if(desktop==null)throw new IOException("Windows 데스크톱 실행 경로를 찾지 못했습니다.");
   document=Property(desktop,"Document");application=Property(document,"Application");
   Invoke(application,"ShellExecute",executable,arguments,Path.GetDirectoryName(executable),Elevated()?"runas":"open",0);
  }finally{Release(application);Release(document);Release(desktop);Release(windows);Release(shell);}
 }
 static bool Detached(string[] args){return args.Length==0||args.Any(a=>new[]{"--preview","--settings","--play","--setup","--daily-agent"}.Contains(a));}
 internal static int Forward(string[] args,string root){
  string name=Prefix+Guid.NewGuid().ToString("N");
  using(var pipe=PrivateIpc.Server(name,1,65536)){
   var pending=pipe.BeginWaitForConnection(null,null);
   LaunchDesktop(NativePaths.Resolve(Assembly.GetExecutingAssembly().Location),"--runtime-host "+name);
   if(!pending.AsyncWaitHandle.WaitOne(15000))throw new IOException("캣서린 실행 연결 시간이 초과되었습니다.");
   pipe.EndWaitForConnection(pending);pending.AsyncWaitHandle.Close();
   var reader=new BinaryReader(pipe,Utf8,true);var writer=new BinaryWriter(pipe,Utf8,true);
   writer.Write(CatheryneTools.Json().Serialize(new{args=args,root=Path.GetFullPath(root),thread=Environment.GetEnvironmentVariable("CATHERYNE_THREAD")??"",detached=Detached(args)}));writer.Flush();
   if(!reader.ReadBoolean())throw new IOException(reader.ReadString());
   if(Detached(args))return 0;
   Task.Run(()=>{try{Pump(Console.OpenStandardInput(),writer,0);lock(writer){writer.Write((byte)4);writer.Write(0);writer.Flush();}}catch(IOException){}catch(ObjectDisposedException){}});
   using(var output=Console.OpenStandardOutput())using(var error=Console.OpenStandardError()){
    while(true){
     byte kind=reader.ReadByte();int count=reader.ReadInt32();
     if(kind==3)return count;
     if((kind!=1&&kind!=2)||count<0||count>16384)throw new IOException("잘못된 실행 응답입니다.");
     var data=reader.ReadBytes(count);if(data.Length!=count)throw new EndOfStreamException();
     var stream=kind==1?output:error;stream.Write(data,0,data.Length);stream.Flush();
    }
   }
  }
 }
 static void Pump(Stream input,BinaryWriter writer,byte kind){
  var buffer=new byte[16384];int count;
  while((count=input.Read(buffer,0,buffer.Length))>0)lock(writer){writer.Write(kind);writer.Write(count);writer.Write(buffer,0,count);writer.Flush();}
 }
 internal static int Host(string name,Func<string[],int> run){
  Guid id;if(!name.StartsWith(Prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(Prefix.Length),"N",out id))return 1;
  using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous)){
   pipe.Connect(15000);var reader=new BinaryReader(pipe,Utf8,true);var writer=new BinaryWriter(pipe,Utf8,true);
   string[] args;bool detached;
   try{
    var request=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(reader.ReadString());
    string root=CodexChat.S(request,"root");if(!Path.IsPathRooted(root)||Redirected(root))throw new IOException("사용자 자료 저장소를 직접 열지 못했습니다.");
    Environment.SetEnvironmentVariable("CATHERYNE_TOOL_DATA",root);Environment.SetEnvironmentVariable("CATHERYNE_THREAD",CodexChat.S(request,"thread"));
    args=((System.Collections.IEnumerable)request["args"]).Cast<object>().Select(Convert.ToString).ToArray();detached=Equals(request["detached"],true);
    if(args.Contains("--runtime-host"))throw new ArgumentException("중복 실행 전달은 지원하지 않습니다.");
    writer.Write(true);writer.Flush();
   }catch(Exception error){writer.Write(false);writer.Write(error.Message);writer.Flush();return 1;}
   if(detached){pipe.Close();return run(args);}
   using(var process=args.Length>0&&args[0]=="--mcp-server"?StartMcp(args.Skip(1).ToArray()):StartChild(Assembly.GetExecutingAssembly().Location,args)){
    var stdout=Task.Run(()=>Pump(process.StandardOutput.BaseStream,writer,1));
    var stderr=Task.Run(()=>Pump(process.StandardError.BaseStream,writer,2));
    Task.Run(()=>{
     try{while(true){byte kind=reader.ReadByte();int count=reader.ReadInt32();if(kind==4&&count==0)break;if(kind!=0||count<0||count>16384)throw new IOException("잘못된 실행 입력입니다.");var data=reader.ReadBytes(count);if(data.Length!=count)throw new EndOfStreamException();process.StandardInput.BaseStream.Write(data,0,count);process.StandardInput.BaseStream.Flush();}}
     catch(IOException){}finally{try{process.StandardInput.Close();}catch(InvalidOperationException){}}
    });
    try{process.WaitForExit();Task.WaitAll(stdout,stderr);lock(writer){writer.Write((byte)3);writer.Write(process.ExitCode);writer.Flush();}return process.ExitCode;}
    finally{if(!process.HasExited){process.Kill();process.WaitForExit();}}
   }
  }
 }
 static Process StartChild(string executable,string[] args){
  var info=new ProcessStartInfo(executable,string.Join(" ",args.Select(StoryClient.Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(executable)};
  var process=Process.Start(info);if(process==null)throw new IOException("실행 요청이 전달되지 않았습니다.");return process;
 }
 static void CopyFlushed(Stream input,Stream output){var buffer=new byte[16384];int count;while((count=input.Read(buffer,0,buffer.Length))>0){output.Write(buffer,0,count);output.Flush();}}
 internal static string[] McpCommand(){
  string python=Path.Combine(Setup.DataFolder,"components","ai","Scripts","python.exe"),server=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"integrations","ai","server.py");
  if(!File.Exists(server))server=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","integrations","ai","server.py"));
  if(!File.Exists(python)||!File.Exists(server))throw new FileNotFoundException("AI 구성요소를 준비해 주세요.");
  return new[]{python,server};
 }
 static Process StartMcp(string[] args){
  var command=McpCommand();
  return StartChild(command[0],command.Skip(1).Concat(args).ToArray());
 }
 internal static int Mcp(string[] args){
  using(var process=StartMcp(args)){
   Task.Run(()=>{try{CopyFlushed(Console.OpenStandardInput(),process.StandardInput.BaseStream);}catch(IOException){}finally{try{process.StandardInput.Close();}catch(InvalidOperationException){}}});
   var output=Task.Run(()=>CopyFlushed(process.StandardOutput.BaseStream,Console.OpenStandardOutput()));
   var error=Task.Run(()=>CopyFlushed(process.StandardError.BaseStream,Console.OpenStandardError()));
   process.WaitForExit();Task.WaitAll(output,error);return process.ExitCode;
  }
 }
}
