using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

internal sealed class ClaudeAccountRequired : InvalidOperationException {
 internal ClaudeAccountRequired():base(Locale.T("Claude 계정으로 로그인해 주세요. API 키와 외부 클라우드 연결은 이 채팅에서 사용하지 않습니다.")){}
}

// Official CLI adapter. Authentication stays in Claude Code's credential store.
// Local files hold only Catheryne's presentation of sessions, never credentials.
internal sealed class ClaudeProvider : IAiProvider {
 readonly string workspace;
 readonly Action<string,Dictionary<string,object>> notify;
 readonly Func<string,Dictionary<string,object>,string,string,string,Task<object>> execute;
 readonly Func<string[],Task<Dictionary<string,object>>> auth;
 readonly Func<Task<string>> find;
 readonly Func<ProcessStartInfo,Process> launch;
 readonly object gate=new object();
 Process running,login;NamedPipeServerStream pipe;bool disposed;
 Task finishingTask;Dictionary<string,object> activeTurn;
 readonly System.Threading.SemaphoreSlim startup=new System.Threading.SemaphoreSlim(1,1);
 string executable;
 internal ClaudeProvider(string root,Action<string,Dictionary<string,object>> notify,Func<string,Dictionary<string,object>,string,string,string,Task<object>> execute,Func<string[],Task<Dictionary<string,object>>> auth=null,Func<Task<string>> find=null,Func<ProcessStartInfo,Process> launch=null){
  workspace=Path.Combine(root,"ai-workspace","claude");this.notify=notify;this.execute=execute;this.auth=auth;this.find=find??(()=>Task.Run(()=>Find()));this.launch=launch??Process.Start;
 }
 static Dictionary<string,object> D(object value){return CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(value)));}
 static string S(Dictionary<string,object> value,string key){return CodexChat.S(value,key);}
 void Emit(string name,object value){if(!disposed&&notify!=null)notify(name,D(value));}
 internal static string Find(){
  var candidates=new List<string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".local","bin","claude.exe")};
  candidates.AddRange((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>Path.Combine(x.Trim('"'),"claude.exe")));
  string desktop=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Claude","claude-code");
  if(Directory.Exists(desktop))foreach(string version in Directory.GetDirectories(desktop).OrderByDescending(x=>{Version parsed;return Version.TryParse(Path.GetFileName(x),out parsed)?parsed:new Version(0,0);})){candidates.Add(Path.Combine(version,"claude.exe"));foreach(string build in Directory.GetDirectories(version))candidates.Add(Path.Combine(build,"claude.exe"));}
  string selected=candidates.FirstOrDefault(File.Exists);if(selected==null)throw new FileNotFoundException(Locale.T("Claude Code를 설치한 뒤 다시 시도해 주세요."));return selected;
 }
 // A real argv encoder, including quotes and trailing backslashes (no shell).
 internal static string Quote(string value){var text=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"'){text.Append('\\',slashes*2+1);text.Append(c);}else{text.Append('\\',slashes);text.Append(c);}slashes=0;}text.Append('\\',slashes*2);return text.Append('"').ToString();}
 internal static ProcessStartInfo Info(string executable,IEnumerable<string> args,string workspace){
  var info=new ProcessStartInfo(executable,string.Join(" ",args.Select(Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=workspace};
  // Do not let an inherited key/token/cloud endpoint silently change billing or account.
  foreach(string key in info.EnvironmentVariables.Keys.Cast<string>().Where(k=>k.StartsWith("ANTHROPIC_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("CLAUDE_CODE_USE_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("CLAUDE_CODE_OAUTH",StringComparison.OrdinalIgnoreCase)||k.StartsWith("CLAUDE_CODE_API_KEY",StringComparison.OrdinalIgnoreCase)||new[]{"CLAUDE_CONFIG_DIR","CLAUDE_CODE_SUBAGENT_MODEL","CLAUDE_CODE_SYSTEM_PROMPT"}.Contains(k,StringComparer.OrdinalIgnoreCase)).ToArray())info.EnvironmentVariables.Remove(key);
  info.EnvironmentVariables.Remove("CLAUDECODE");return info;
 }
 internal static bool VersionSupported(string version){Version parsed;return Version.TryParse(version.Split(' ','\r','\n')[0],out parsed)&&parsed>=new Version(2,1,259);}
 public async Task Start(){
  await startup.WaitAsync().ConfigureAwait(false);try{
  if(disposed)throw new ObjectDisposedException("ClaudeProvider");if(executable!=null)return;
  string selected=await find().ConfigureAwait(false);if(disposed)throw new ObjectDisposedException("ClaudeProvider");
  if(auth==null){var version=await RunCommand(selected,new[]{"--version"},15000,false);if(!VersionSupported(S(version,"output")))throw new InvalidOperationException(Locale.T("Claude Code 2.1.259 이상으로 업데이트해 주세요."));}
  if(disposed)throw new ObjectDisposedException("ClaudeProvider");Directory.CreateDirectory(workspace);executable=selected;
  }finally{startup.Release();}
 }
 async Task<Dictionary<string,object>> RunCommand(string command,string[] args,int timeout,bool isLogin){
  using(var process=new Process{StartInfo=Info(command,args,workspace)}){
   // --version must work before creating the private workspace.
   if(!Directory.Exists(workspace))process.StartInfo.WorkingDirectory=Path.GetTempPath();
   lock(gate){if(disposed)throw new ObjectDisposedException("ClaudeProvider");process.Start();if(isLogin)login=process;}
   var output=isLogin?Discard(process.StandardOutput):process.StandardOutput.ReadToEndAsync();var error=Discard(process.StandardError);process.StandardInput.Close();
   try{var ended=Task.Run(()=>process.WaitForExit());if(await Task.WhenAny(ended,Task.Delay(timeout))!=ended){process.Kill();await ended;throw new TimeoutException(Locale.T("Claude 연결 응답 시간이 초과되었습니다."));}await error;string result=await output;return D(new{exitCode=process.ExitCode,output=result});}
   finally{lock(gate){if(login==process)login=null;}}
  }
 }
 static async Task<string> Discard(StreamReader reader){var buffer=new char[4096];while(await reader.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false)>0){}return "";}
 async Task<Dictionary<string,object>> Auth(string[] args,int timeout=15000,bool isLogin=false){await Start().ConfigureAwait(false);return auth!=null?await auth(args).ConfigureAwait(false):await RunCommand(executable,args,timeout,isLogin).ConfigureAwait(false);}
 internal static bool AccountStatus(Dictionary<string,object> value){return value.ContainsKey("loggedIn")&&Equals(value["loggedIn"],true)&&S(value,"authMethod")=="claude.ai";}
 public async Task Login(){var result=await Auth(new[]{"auth","login"},300000,true);if(Convert.ToInt32(result["exitCode"])!=0)throw new InvalidOperationException(Locale.T("Claude 로그인을 완료하지 못했습니다. 공식 로그인 창에서 다시 시도해 주세요."));}
 string PathFor(string id){Guid value;if(!Guid.TryParse(id,out value))throw new ArgumentException("Invalid Claude session");return Path.Combine(workspace,value.ToString()+".json");}
 Dictionary<string,object> Read(string id){return CatheryneTools.Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(PathFor(id)));}
 void Save(Dictionary<string,object> thread){AtomicFile.Write(PathFor(S(thread,"id")),CatheryneTools.Json().Serialize(thread));}
 IEnumerable<Dictionary<string,object>> Sessions(){foreach(string file in Directory.GetFiles(workspace,"*.json")){Guid id;if(!Guid.TryParse(Path.GetFileNameWithoutExtension(file),out id))continue;Dictionary<string,object> thread;try{thread=Read(id.ToString());}catch(IOException){continue;}if(!thread.ContainsKey("archived")||!Equals(thread["archived"],true))yield return thread;}}
 static Dictionary<string,object> Page(IEnumerable<Dictionary<string,object>> rows,Dictionary<string,object> args){int start=0,limit=100;int.TryParse(S(args,"cursor"),out start);if(args.ContainsKey("limit"))limit=Convert.ToInt32(args["limit"]);if(start<0||limit<1||limit>100)throw new ArgumentException("Invalid page");var all=rows.ToList();return D(new{data=all.Skip(start).Take(limit).ToArray(),nextCursor=start+limit<all.Count?(start+limit).ToString():null});}
 public async Task<Dictionary<string,object>> Call(string method,Dictionary<string,object> args){
  await Start().ConfigureAwait(false);if(method=="account/read"){
   var status=await Auth(new[]{"auth","status"});Dictionary<string,object> value;
   try{value=CodexChat.Map(CatheryneTools.Json().DeserializeObject(S(status,"output")));}catch{throw new InvalidDataException(Locale.T("Claude 로그인 상태를 확인하지 못했습니다."));}
   int exit=Convert.ToInt32(status["exitCode"]);if(exit!=0&&exit!=1)throw new InvalidOperationException(Locale.T("Claude 로그인 상태를 확인하지 못했습니다."));
   if(exit==0&&!AccountStatus(value)&&S(value,"authMethod")!="none")throw new ClaudeAccountRequired();
   return D(new{account=exit==0&&AccountStatus(value)?new{type="claude"}:null});
  }
  if(method=="account/logout"){if(running!=null||login!=null)throw new InvalidOperationException(Locale.T("진행 중인 작업이 끝난 뒤 AI를 변경해 주세요."));var result=await Auth(new[]{"auth","logout"});if(Convert.ToInt32(result["exitCode"])!=0)throw new InvalidOperationException(Locale.T("Claude 연결을 해제하지 못했습니다."));return new Dictionary<string,object>();}
  if(method=="model/list")return D(new{data=new[]{new{model="sonnet",displayName="Claude Sonnet",isDefault=true,defaultReasoningEffort="",supportedReasoningEfforts=new object[0]},new{model="opus",displayName="Claude Opus",isDefault=false,defaultReasoningEffort="",supportedReasoningEfforts=new object[0]},new{model="haiku",displayName="Claude Haiku",isDefault=false,defaultReasoningEffort="",supportedReasoningEfforts=new object[0]}}});
  if(method=="account/rateLimits/read")throw new InvalidOperationException(Locale.T("Claude Code는 이 연결에서 계정 한도 조회를 제공하지 않습니다."));
  if(method=="project/list")return D(new{data=new[]{new{id="claude",roots=new[]{new{path=workspace}}}}});
  if(method=="thread/metadata/update")return new Dictionary<string,object>();
  if(method=="thread/start"){
   var thread=D(new{id=Guid.NewGuid().ToString(),projectId="claude",cwd=workspace,preview="",createdAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),updatedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),turns=new object[0],native=false});await Task.Run(()=>Save(thread)).ConfigureAwait(false);return D(new{thread=thread});
  }
  if(method=="thread/list")return await Task.Run(()=>Page(Sessions().Select(t=>D(new{id=t["id"],projectId="claude",cwd=workspace,preview=t["preview"],createdAt=t["createdAt"],updatedAt=t["updatedAt"]})).OrderByDescending(t=>Convert.ToInt64(t["updatedAt"])),args));
  if(method=="thread/goal/get")return D(new{goal=(object)null});
  if(method=="thread/goal/clear")return new Dictionary<string,object>();
  if(method=="thread/fork")throw new InvalidOperationException(Locale.T("Claude 연결에서는 중단된 요청의 수정본 대화를 아직 지원하지 않습니다. 새 대화에서 요청해 주세요."));
  if(method=="turn/interrupt"){await Stop();return new Dictionary<string,object>();}
  var stored=await Task.Run(()=>Read(S(args,"threadId"))).ConfigureAwait(false);var turns=CodexChat.Items(stored["turns"]).ToList();
  if(method=="thread/read")return D(new{thread=stored});
  if(method=="thread/resume")return D(new{thread=stored});
  if(method=="thread/archive"){stored["archived"]=true;Save(stored);return new Dictionary<string,object>();}
  if(method=="thread/turns/list")return Page(S(args,"sortDirection")=="desc"?turns.AsEnumerable().Reverse():turns,args);
  if(method=="thread/items/list")return Page(turns.Where(t=>S(args,"turnId")==""||S(t,"id")==S(args,"turnId")).SelectMany(t=>CodexChat.Items(t["items"]).Select(item=>D(new{turnId=t["id"],item=item}))),args);
  if(method=="turn/start"){
   lock(gate){if(running!=null)throw new InvalidOperationException("Claude turn already running");}
   string id=Guid.NewGuid().ToString();var turn=D(new{id=id,status="inProgress",startedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),items=new[]{new{id=Guid.NewGuid().ToString(),type="userMessage",content=args["input"],createdAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}});
   turns.Add(turn);stored["turns"]=turns;stored["updatedAt"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();if(S(stored,"preview")=="")stored["preview"]=string.Join(" ",CodexChat.Items(args["input"]).Where(p=>S(p,"type")=="text").Select(p=>S(p,"text"))).Substring(0,Math.Min(160,string.Join(" ",CodexChat.Items(args["input"]).Where(p=>S(p,"type")=="text").Select(p=>S(p,"text"))).Length));
   await Task.Run(()=>Save(stored));await Launch(stored,turn,args);return D(new{turn=turn});
  }
  throw new NotSupportedException("Unsupported Claude operation: "+method);
 }
 internal static object UserInput(object input){
  var parts=new List<object>();foreach(var part in CodexChat.Items(input)){
   if(S(part,"type")=="text")parts.Add(new{type="text",text=S(part,"text")});
   else if(S(part,"type")=="localImage"){string file=S(part,"path");ChatAttachments.Validate(file);string extension=Path.GetExtension(file).ToLowerInvariant();string mime=extension==".jpg"||extension==".jpeg"?"image/jpeg":extension==".png"?"image/png":extension==".gif"?"image/gif":"image/webp";parts.Add(new{type="image",source=new{type="base64",media_type=mime,data=Convert.ToBase64String(File.ReadAllBytes(file))}});}
   else throw new ArgumentException("Unsupported Claude input");
  }return new{type="user",message=new{role="user",content=parts}};
 }
 internal static string[] Arguments(string session,bool resume,string model,string prompt,string config){
  var args=new List<string>{"--print","--input-format","stream-json","--output-format","stream-json","--verbose","--include-partial-messages","--restricted","--tools","","--disable-slash-commands","--strict-mcp-config","--mcp-config",config,"--permission-mode","dontAsk","--permission-prompts","none","--allowedTools","mcp__catheryne__*","--system-prompt-file",prompt,resume?"--resume":"--session-id",session};
  if(!string.IsNullOrEmpty(model)){if(!new[]{"sonnet","opus","haiku"}.Contains(model))throw new ArgumentException("Invalid Claude model");args.Add("--model");args.Add(model);}return args.ToArray();
 }
 async Task Launch(Dictionary<string,object> thread,Dictionary<string,object> turn,Dictionary<string,object> args){
  string threadId=S(thread,"id"),turnId=S(turn,"id"),name=ClaudeMcpBridge.Prefix+Guid.NewGuid().ToString("N"),prompt=Path.Combine(workspace,turnId+".prompt.txt");
  NamedPipeServerStream server=null;Process process=null;
  try{
   var input=await Task.Run(()=>{var value=UserInput(args["input"]);AtomicFile.Write(prompt,AiProviders.Instructions(AiProviders.Claude));return value;}).ConfigureAwait(false);
   var config=new{mcpServers=new Dictionary<string,object>{{"catheryne",new{type="stdio",command=Assembly.GetExecutingAssembly().Location,args=new[]{"--ai-mcp-bridge",name}}}}};
   server=PrivateIpc.Server(name,1,65536);var info=Info(executable,Arguments(threadId,Equals(thread["native"],true),S(args,"model"),prompt,CatheryneTools.Json().Serialize(config)),workspace);
   lock(gate){if(disposed)throw new ObjectDisposedException("ClaudeProvider");process=launch(info);running=process;pipe=server;activeTurn=turn;}
   Emit("turn/started",new{threadId=threadId,turn=turn});
   var serving=Serve(server,threadId,turnId,turn);var reading=ReadStream(process,thread,turn);var errors=Discard(process.StandardError);
   using(var writer=new StreamWriter(process.StandardInput.BaseStream,new UTF8Encoding(false),65536,true)){await writer.WriteLineAsync(CatheryneTools.Json().Serialize(input));await writer.FlushAsync();}process.StandardInput.Close();
   finishingTask=Finish(process,server,serving,reading,errors,thread,turn,prompt);
  }catch{
   if(server!=null)server.Dispose();if(process!=null){try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}process.Dispose();}lock(gate){if(running==process)running=null;if(pipe==server)pipe=null;}if(File.Exists(prompt))File.Delete(prompt);turn["status"]="failed";Save(thread);throw;
  }
 }
 async Task Serve(NamedPipeServerStream server,string thread,string turn,Dictionary<string,object> storedTurn){
  await Task.Factory.FromAsync(server.BeginWaitForConnection,server.EndWaitForConnection,null);
  using(var reader=new StreamReader(server,new UTF8Encoding(false),false,65536,true))using(var writer=new StreamWriter(server,new UTF8Encoding(false),65536,true){AutoFlush=true}){
   string line;while((line=await reader.ReadLineAsync())!=null){if(line.Length>16000000)throw new IOException("MCP message too large");
    var reply=await ClaudeMcpBridge.Reply(CatheryneTools.Json().Deserialize<Dictionary<string,object>>(line),async(name,args,id)=>{
     string call="mcp-"+turn+"-"+id;bool success=false;Emit("item/started",new{threadId=thread,turnId=turn,item=new{id=call,type="dynamicToolCall",tool=name}});
     try{var result=await execute(name,args,thread,turn,call);success=true;return result;}
     finally{var item=D(new{id=call,type="dynamicToolCall",tool=name,success=success,status=success?"completed":"failed",createdAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()});lock(gate){var items=CodexChat.Items(storedTurn["items"]).ToList();items.Add(item);storedTurn["items"]=items;}Emit("item/completed",new{threadId=thread,turnId=turn,item=item});}
    });await writer.WriteLineAsync(reply==null?"":CatheryneTools.Json().Serialize(reply));
   }
  }
 }
 internal static string Delta(Dictionary<string,object> message){if(S(message,"type")!="stream_event")return "";var evt=CodexChat.Map(message["event"]);object raw;if(S(evt,"type")!="content_block_delta"||!evt.TryGetValue("delta",out raw))return "";var delta=CodexChat.Map(raw);return S(delta,"type")=="text_delta"?S(delta,"text"):"";}
 internal static string ErrorReason(Dictionary<string,object> message){string error=CatheryneTools.Json().Serialize(message).ToLowerInvariant();return error.Contains("rate_limit")||error.Contains("rate limit")||error.Contains("usage limit")?Locale.T("Claude 사용 한도에 도달했습니다. 한도가 갱신된 뒤 다시 시도해 주세요."):Locale.T("Claude 요청을 완료하지 못했습니다. 로그인과 사용 한도를 확인해 주세요.");}
 async Task ReadStream(Process process,Dictionary<string,object> thread,Dictionary<string,object> turn){
  string line,threadId=S(thread,"id"),turnId=S(turn,"id"),itemId=Guid.NewGuid().ToString();var text=new StringBuilder();bool streaming=false;
  try{
  while((line=await process.StandardOutput.ReadLineAsync())!=null){var message=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(line);string type=S(message,"type");
   if(type=="system"&&S(message,"subtype")=="init")thread["native"]=true;
   string delta=Delta(message);if(delta.Length>0){streaming=true;text.Append(delta);Emit("item/agentMessage/delta",new{threadId=threadId,turnId=turnId,itemId=itemId,delta=delta});}
   if(type=="assistant"){
    string complete=string.Join("\n",CodexChat.Items(CodexChat.Map(message["message"])["content"]).Where(p=>S(p,"type")=="text").Select(p=>S(p,"text")));
    if(!streaming&&complete.Length>0){text.Append(complete);Emit("item/agentMessage/delta",new{threadId=threadId,turnId=turnId,itemId=itemId,delta=complete});}
    if(text.Length>0){var item=D(new{id=itemId,type="agentMessage",text=text.ToString(),createdAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()});lock(gate){var items=CodexChat.Items(turn["items"]).ToList();items.Add(item);turn["items"]=items;}Emit("item/completed",new{threadId=threadId,turnId=turnId,item=item});}
    itemId=Guid.NewGuid().ToString();text.Clear();streaming=false;
   }
   if(type=="result"){
    bool failed=message.ContainsKey("is_error")&&Equals(message["is_error"],true)||S(message,"subtype")!="success";if(S(turn,"status")!="interrupted")turn["status"]=failed?"failed":"completed";
    if(failed)turn["error"]=new{message=ErrorReason(message)};
   }
  }
  // Preserve partial streamed text if interrupted before an assistant envelope.
  if(text.Length>0){lock(gate){var items=CodexChat.Items(turn["items"]).ToList();items.Add(D(new{id=itemId,type="agentMessage",text=text.ToString()}));turn["items"]=items;}}
  }catch{try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}throw;}
 }
 async Task Finish(Process process,NamedPipeServerStream server,Task serving,Task reading,Task<string> errors,Dictionary<string,object> thread,Dictionary<string,object> turn,string prompt){
  try{await Task.Run(()=>process.WaitForExit());await reading;await errors;if(S(turn,"status")=="inProgress")turn["status"]="failed";}
  catch(Exception){if(S(turn,"status")!="interrupted")turn["status"]="failed";}
  server.Dispose();try{await serving;}catch(IOException){}catch(ObjectDisposedException){}catch(Exception){if(S(turn,"status")!="interrupted")turn["status"]="failed";}
   lock(gate){if(running==process){running=null;activeTurn=null;}if(pipe==server)pipe=null;}process.Dispose();try{if(File.Exists(prompt))File.Delete(prompt);}catch(IOException){Emit("client/notice",new{message=Locale.T("Claude 임시 지침 파일을 정리하지 못했습니다.")});}catch(UnauthorizedAccessException){Emit("client/notice",new{message=Locale.T("Claude 임시 지침 파일을 정리하지 못했습니다.")});}
   turn["completedAt"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();if(S(turn,"status")=="failed"&&!turn.ContainsKey("error"))turn["error"]=new{message=Locale.T("Claude 연결이 종료되었습니다. 설치 상태와 로그인을 확인해 주세요.")};
   try{Save(thread);}catch(IOException){Emit("client/notice",new{message=Locale.T("Claude 대화 기록을 저장하지 못했습니다.")});}catch(UnauthorizedAccessException){Emit("client/notice",new{message=Locale.T("Claude 대화 기록을 저장하지 못했습니다.")});}Emit("turn/completed",new{threadId=thread["id"],turn=turn});
 }
 async Task Stop(){Process process;Task finishing;lock(gate){process=running;finishing=finishingTask;if(activeTurn!=null)activeTurn["status"]="interrupted";}if(process==null)return;await Task.Run(()=>{try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}});if(finishing!=null)await finishing;}
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;try{if(running!=null&&!running.HasExited)running.Kill();if(login!=null&&!login.HasExited)login.Kill();}catch(InvalidOperationException){}if(pipe!=null)pipe.Dispose();}}
}
