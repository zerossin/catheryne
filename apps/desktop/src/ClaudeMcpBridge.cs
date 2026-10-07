using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

// Stdio is only a relay. The GUI's shared tool gate remains the execution owner.
internal static class ClaudeMcpBridge {
 internal const string Prefix="Catheryne.Claude.";
 internal static int Run(string name){
  Guid id;if(!name.StartsWith(Prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(Prefix.Length),"N",out id))return 1;
  try{using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous)){
   pipe.Connect(15000);using(var reader=new StreamReader(pipe,new UTF8Encoding(false),false,65536,true))using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),65536,true){AutoFlush=true})
   using(var input=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8))using(var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true}){
    string line;while((line=input.ReadLine())!=null){if(line.Length>16000000)return 1;writer.WriteLine(line);string reply=reader.ReadLine();if(reply==null)return 1;if(reply.Length>0)output.WriteLine(reply);}
   }
  }return 0;}catch(IOException){return 1;}catch(TimeoutException){return 1;}
 }
 internal static object[] Content(object result){
  var map=result as Dictionary<string,object>;if(map==null||!map.ContainsKey("image_url"))return new object[]{new{type="text",text=CatheryneTools.Json().Serialize(result)}};
  var metadata=new Dictionary<string,object>(map);string image=CodexChat.S(metadata,"image_url");metadata.Remove("image_url");int split=image.IndexOf(',');
  if(split<0||!image.StartsWith("data:image/",StringComparison.Ordinal)||!image.Substring(0,split).EndsWith(";base64",StringComparison.Ordinal))throw new InvalidDataException("Invalid tool image");
  string mime=image.Substring(5,image.IndexOf(';')-5);if(mime!="image/png"&&mime!="image/jpeg")throw new InvalidDataException("Invalid tool image type");
  return new object[]{new{type="text",text=CatheryneTools.Json().Serialize(metadata)},new{type="image",mimeType=mime,data=image.Substring(split+1)}};
 }
 internal static async Task<object> Reply(Dictionary<string,object> message,Func<string,Dictionary<string,object>,string,Task<object>> execute){
  object id;bool request=message.TryGetValue("id",out id);string method=CodexChat.S(message,"method");if(!request)return null;
  try{
   object result;
   if(method=="initialize")result=new{protocolVersion="2024-11-05",capabilities=new{tools=new{}},serverInfo=new{name="catheryne",version=CodexChat.ToolContractVersion.ToString()}};
   else if(method=="ping")result=new{};
   else if(method=="tools/list")result=new{tools=CatheryneTools.Definitions()};
   else if(method=="tools/call"){
    var args=CodexChat.Map(message["params"]);object raw;var parameters=args.TryGetValue("arguments",out raw)?CodexChat.Map(raw):new Dictionary<string,object>();
    try{result=new{content=Content(await execute(CodexChat.S(args,"name"),parameters,Convert.ToString(id))),isError=false};}
    catch(Exception error){result=new{content=Content(CatheryneTools.Failure(error)),isError=true};}
   }else return new{jsonrpc="2.0",id=id,error=new{code=-32601,message="Unsupported MCP method"}};
   return new{jsonrpc="2.0",id=id,result=result};
  }catch(Exception){return new{jsonrpc="2.0",id=id,error=new{code=-32602,message="Invalid MCP request"}};}
 }
}
