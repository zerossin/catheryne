using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

internal static class ClaudeProviderTests {
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static Dictionary<string,object> D(object value){return CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(value)));}
 static async Task Deadline(Task task){if(await Task.WhenAny(task,Task.Delay(10000))!=task)throw new Exception("Claude fixture timed out");await task;}
 internal static int Fixture(string[] args){
  var json=CatheryneTools.Json();using(var input=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8))using(var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true}){
   var message=json.Deserialize<Dictionary<string,object>>(input.ReadLine());string text=CodexChat.S(CodexChat.Items(CodexChat.Map(message["message"])["content"]).First(),"text");
   output.WriteLine(json.Serialize(new{type="system",subtype="init"}));output.WriteLine(json.Serialize(new{type="stream_event",@event=new{type="content_block_delta",delta=new{type="text_delta",text="fixture answer"}}}));
   if(text=="cancel"){System.Threading.Thread.Sleep(15000);return 0;}
   int at=Array.IndexOf(args,"--mcp-config");var config=json.Deserialize<Dictionary<string,object>>(args[at+1]);var server=CodexChat.Map(CodexChat.Map(config["mcpServers"])["catheryne"]);var bridgeArgs=((System.Collections.IEnumerable)server["args"]).Cast<object>().Select(Convert.ToString).ToArray();
   using(var bridge=Process.Start(ClaudeProvider.Info(CodexChat.S(server,"command"),bridgeArgs,Path.GetTempPath()))){
    bridge.StandardInput.WriteLine(json.Serialize(new{jsonrpc="2.0",id=1,method="initialize",@params=new{protocolVersion="2024-11-05"}}));bridge.StandardInput.Flush();bridge.StandardOutput.ReadLine();
    bridge.StandardInput.WriteLine(json.Serialize(new{jsonrpc="2.0",method="notifications/initialized"}));bridge.StandardInput.Flush();
    bridge.StandardInput.WriteLine(json.Serialize(new{jsonrpc="2.0",id=2,method="tools/call",@params=new{name="catheryne_context",arguments=new{}}}));bridge.StandardInput.Flush();var result=json.Deserialize<Dictionary<string,object>>(bridge.StandardOutput.ReadLine());if(Equals(CodexChat.Map(result["result"])["isError"],true))return 2;
    bridge.StandardInput.Close();bridge.WaitForExit();
   }
   output.WriteLine(json.Serialize(new{type="assistant",message=new{content=new[]{new{type="text",text="fixture answer"}}}}));bool failed=text=="fail"||text=="limit";output.WriteLine(json.Serialize(new{type="result",subtype=failed?"error_during_execution":"success",is_error=failed,result=text=="limit"?"rate limit reached":""}));return 0;
  }
 }
 internal static void Run(){Task.Run(async()=>{
  Check(AiProviders.IsChat("claude")&&AccountConnections.Providers.Contains("claude"),"Claude belongs to the single login owner");
  Check(AiProviders.LoginAddress("https://claude.ai/oauth/authorize","claude"),"Official Claude login host accepted");
  foreach(string url in new[]{"http://claude.ai/","https://claude.ai.evil.invalid/","https://claude.ai@evil.invalid/","https://claude.ai:444/","file:///tmp/auth"})Check(!AiProviders.LoginAddress(url,"claude"),"Reject unsafe login URL");
  Check(!AiProviders.LoginAddress("https://claude.ai/","chatgpt")&&AiProviders.LoginAddress("https://auth.openai.com/","chatgpt"),"Provider login hosts do not mix");
  Check(ClaudeProvider.VersionSupported("2.1.259 (Claude Code)")&&!ClaudeProvider.VersionSupported("2.1.258")&&!ClaudeProvider.VersionSupported("unknown"),"Required CLI security flags are version gated");
  Check(ClaudeProvider.AccountStatus(D(new{loggedIn=true,authMethod="claude.ai"}))&&!ClaudeProvider.AccountStatus(D(new{loggedIn=true,authMethod="api_key"}))&&!ClaudeProvider.AccountStatus(D(new{loggedIn=false,authMethod="claude.ai"})),"Only official logged-in Claude accounts qualify");
  string[] arguments=ClaudeProvider.Arguments(Guid.NewGuid().ToString(),false,"sonnet","prompt","{}");Check(arguments.Contains("--restricted")&&arguments[Array.IndexOf(arguments,"--tools")+1]==""&&arguments.Contains("--strict-mcp-config")&&arguments.Contains("--permission-prompts")&&!arguments.Contains("--dangerously-skip-permissions")&&!arguments.Contains("--fallback-model"),"No shell, bypass, fallback or unrelated MCP");Check(ClaudeProvider.Arguments(Guid.NewGuid().ToString(),true,"opus","prompt","{}").Contains("--resume"),"Continuation resumes its exact session");
  Check(ClaudeProvider.Quote("a\\")=="\"a\\\\\""&&ClaudeProvider.Quote("a\"b")=="\"a\\\"b\"","argv protects embedded quotes and trailing backslashes");
  var info=ClaudeProvider.Info("fake.exe",new[]{"auth","status"},Path.GetTempPath());Check(!info.UseShellExecute&&info.CreateNoWindow&&!info.EnvironmentVariables.ContainsKey("CLAUDE_CODE_OAUTH_TOKEN")&&!info.EnvironmentVariables.ContainsKey("ANTHROPIC_API_KEY"),"No inherited key or token changes billing");
  var image=new Dictionary<string,object>{{"image_url","data:image/png;base64,AA=="},{"observed","synthetic"}};var content=CodexChat.Items(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(ClaudeMcpBridge.Content(image)))).ToArray();Check(content.Length==2&&CodexChat.S(content[1],"type")=="image"&&CodexChat.S(content[1],"mimeType")=="image/png"&&!CodexChat.S(content[0],"text").Contains("base64")&&image.ContainsKey("image_url"),"MCP images stay visible without mutating domain results");
  var denied=await ClaudeMcpBridge.Reply(D(new{id="rpc",method="tools/call",@params=new{name="catheryne_context",arguments=new{}}}),(name,args,id)=>{throw new InvalidOperationException("synthetic denial");});Check(Equals(CodexChat.Map(D(denied)["result"])["isError"],true),"Tool gate errors remain visible MCP errors");
  string root=Path.Combine(Path.GetTempPath(),"catheryne-claude-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   int authCalls=0,tools=0;bool denyTool=false,failedTool=false;var completed=new TaskCompletionSource<Dictionary<string,object>>();var delta=new TaskCompletionSource<bool>();var output=new StringBuilder();
   Func<string[],Task<Dictionary<string,object>>> auth=args=>{authCalls++;return Task.FromResult(D(new{exitCode=0,output=CatheryneTools.Json().Serialize(new{loggedIn=true,authMethod="claude.ai"})}));};
   Action<string,Dictionary<string,object>> notification=(name,value)=>{if(name=="turn/completed")completed.TrySetResult(CodexChat.Map(value["turn"]));if(name=="item/agentMessage/delta"){output.Append(CodexChat.S(value,"delta"));delta.TrySetResult(true);}if(name=="item/completed"&&Equals(CodexChat.Map(value["item"]).ContainsKey("success")?CodexChat.Map(value["item"])["success"]:null,false))failedTool=true;};
   Func<string,Dictionary<string,object>,string,string,string,Task<object>> tool=(name,args,thread,turn,id)=>{Check(name=="catheryne_context"&&Guid.Parse(thread)!=Guid.Empty&&Guid.Parse(turn)!=Guid.Empty&&id.StartsWith("mcp-"),"MCP preserves turn ownership");tools++;if(denyTool)throw new InvalidOperationException("Synthetic tool failure");return Task.FromResult((object)image);};
   Func<ProcessStartInfo,Process> launch=start=>{start.Arguments="--claude-provider-fixture "+start.Arguments;return Process.Start(start);};string executable=Assembly.GetExecutingAssembly().Location,session;
   using(var provider=new ClaudeProvider(root,notification,tool,auth,()=>Task.FromResult(executable),launch)){
    var account=await provider.Call("account/read",new Dictionary<string,object>());Check(account["account"]!=null&&authCalls==1,"Account uses the fake official auth command");await provider.Login();Check(authCalls==2,"Login delegates without accepting credentials");var started=await provider.Call("thread/start",new Dictionary<string,object>());session=CodexChat.S(CodexChat.Map(started["thread"]),"id");
    foreach(string request in new[]{"hello","fail","limit","deny","cancel"}){
     denyTool=request=="deny";completed=new TaskCompletionSource<Dictionary<string,object>>();delta=new TaskCompletionSource<bool>();output.Clear();await provider.Call("turn/start",D(new{threadId=session,input=CodexChat.PrepareInput(request,null),model="sonnet"}));if(request=="cancel"){await Deadline(delta.Task);await provider.Call("turn/interrupt",new Dictionary<string,object>());}await Deadline(completed.Task);
     Check(CodexChat.S(completed.Task.Result,"status")== (request=="hello"?"completed":request=="cancel"?"interrupted":"failed"),"Process adapter preserves success/failure/stop: "+request);Check(output.ToString()=="fixture answer","Partial and final stream do not duplicate text");
     if(request=="limit")Check(CodexChat.S(CodexChat.Map(completed.Task.Result["error"]),"message")==Locale.T("Claude 사용 한도에 도달했습니다. 한도가 갱신된 뒤 다시 시도해 주세요."),"Rate limits are visible without retrying another account");
    }
    Check(tools==4&&failedTool,"Synthetic MCP failures reach UI as failed tools");var page=await provider.Call("thread/turns/list",D(new{threadId=session,limit=2,sortDirection="desc"}));Check(CodexChat.Items(page["data"]).Count()==2&&CodexChat.S(page,"nextCursor")=="2","History pagination is bounded");Check(!Directory.GetFiles(Path.Combine(root,"ai-workspace","claude"),"*.prompt.txt").Any(),"Temporary prompts removed after every turn");
   }
   using(var reopened=new ClaudeProvider(root,null,tool,auth,()=>Task.FromResult(executable),launch)){var read=await reopened.Call("thread/read",D(new{threadId=session}));Check(CodexChat.Items(CodexChat.Map(read["thread"])["turns"]).Count()==5,"History survives restart");await reopened.Call("thread/archive",D(new{threadId=session}));Check(!CodexChat.Items((await reopened.Call("thread/list",new Dictionary<string,object>()))["data"]).Any(),"Archive preserves records while hiding its list entry");}
   using(var host=new CodexChat(root,null,message=>{})){host.ThreadId="fixture-thread";host.TurnId="fixture-turn";bool rejected=false;try{await host.ExecuteTool("catheryne_context",new Dictionary<string,object>(),"stale-thread","fixture-turn","call");}catch(InvalidOperationException){rejected=true;}Check(rejected&&host.ToolCalls==0,"Shared gate denies inactive conversations");await host.ExecuteTool("catheryne_context",new Dictionary<string,object>(),host.ThreadId,host.TurnId,"call");Check(host.ToolCalls==1,"Canonical tool executor is shared");host.TurnId=null;await host.Interrupt(false);host.TurnId="fixture-turn";rejected=false;try{await host.ExecuteTool("catheryne_context",new Dictionary<string,object>(),host.ThreadId,host.TurnId,"late");}catch(InvalidOperationException){rejected=true;}Check(rejected&&host.ToolCalls==1,"Late MCP calls cannot reclaim stopped work");}
  }finally{Directory.Delete(root,true);}
 }).GetAwaiter().GetResult();}
}
