using System;
using System.Collections.Generic;
using System.Linq;

// The environment transports canonical operations; it does not implement game actions.
internal static class GameEnvironmentOperations {
 internal static object Run(string root,string command,Dictionary<string,object> p){
  if(command=="story.call")return new StoryClient(root).Call(CodexChat.S(p,"method"),p["parameters"],Convert.ToBoolean(p["compact"]));
  if(command=="game.running")return GameRequirement.Running();
  if(command=="external.open")return new ExternalTools(root).Open(CodexChat.S(p,"id"));
  if(command=="environment.manual"){
   Quiesce(root,true);
   return null;
  }
  if(command=="game.capture")return GameCapture.Export(GameCapture.Read(root));
  if(command=="external.stop"){new ExternalTools(root).Stop(CodexChat.S(p,"id"));return null;}
  if(command=="task.start"){
   var task=CatheryneTools.Json().Deserialize<AiTaskRecord>(CatheryneTools.Json().Serialize(p["task"]));new GameTools(root).Start(task);return task;
  }
  if(command=="game.available")return new GameTools(root).Available();
  if(command=="game.aspect"){new GameTools(root).RequireAspect(Convert.ToInt32(p["horizontal"]),Convert.ToInt32(p["vertical"]),p.ContainsKey("minimum_height")?Convert.ToInt32(p["minimum_height"]):0);return null;}
  if(command=="game.run")return new GameTools(root).Run(CodexChat.S(p,"command"),CodexChat.Map(p["parameters"]),CodexChat.S(p,"thread"),null);
  if(command=="game.control")return new GameTools(root).ChangeControl(CodexChat.S(p,"id"),CodexChat.S(p,"target"),CodexChat.S(p,"thread"));
  if(command=="game.launch"){
   var installation=new LauncherOperations(root).Installation();if(!Setup.Ready(installation))throw new InvalidOperationException("게임 설치 경로를 확인해 주세요.");
   LauncherOperations.Start(installation,CodexChat.Map(p["config"]),Convert.ToBoolean(p["unlocker"]),GameLaunchPurpose.Automation);return null;
  }
  if(command=="story.start"){new StoryClient(root).Start(Convert.ToBoolean(p["input"]));return null;}
  if(command=="external.run"){
   var task=CatheryneTools.Json().Deserialize<AiTaskRecord>(CatheryneTools.Json().Serialize(p["task"]));new ExternalTools(root).RunGroup(CodexChat.S(p,"group"),task);return task;
  }
  if(command=="scan.start"){
   var task=CatheryneTools.Json().Deserialize<AiTaskRecord>(CatheryneTools.Json().Serialize(p["task"]));
   ApplicationOperations.Execute(root,"scanner.start",CodexChat.Map(p["parameters"]),task);return task;
  }
  throw new ArgumentException("지원하지 않는 실행 환경 명령입니다.");
 }
 internal static void Stop(string root){
  Quiesce(root,false);
  // A connected story host is still the only input owner. Shutdown releases its keys.
  var story=new StoryClient(root);try{var status=story.Call("status",new{after_sequence=0});if(GameEnvironment.OwnsProcess(Convert.ToInt32(status["host_pid"])))story.Shutdown();}catch(InvalidOperationException){}
 }
 static void Quiesce(string root,bool manual){
  var store=new AiTaskStore(root);var scans=new List<AiTaskRecord>();
  foreach(var item in store.List().Where(t=>t.State=="running"&&GameEnvironment.OwnsProcess(t.OwnerPid,t.OwnerStarted))){
   var task=store.Find(item.Id);if(task==null||task.State!="running")continue;
   if(task.Action=="game_control"){
    if(manual)new GameTools(root).ChangeControl(task.Id,"user",task.Thread);
    else new GameTools(root).Run("stop",new Dictionary<string,object>{{"task_id",task.Id}},task.Thread,null);
   }else if(task.Action=="bettergi")new ExternalTools(root).Stop(task.Id);
   else if(task.Action=="scanner"){CollectionScanner.Stop(root,task.Id);scans.Add(task);}
  }
  var wait=System.Diagnostics.Stopwatch.StartNew();
  while(scans.Any(t=>GameEnvironment.ProcessAlive(t.OwnerPid,t.OwnerStarted))){
   if(wait.Elapsed.TotalSeconds>15)throw new System.IO.IOException("수집 입력 종료를 확인하지 못했습니다.");
   System.Threading.Thread.Sleep(100);
  }
 }
 internal static void UpdateTask(AiTaskRecord target,object result){
  var saved=CatheryneTools.Json().Deserialize<AiTaskRecord>(CatheryneTools.Json().Serialize(result));
  target.Action=saved.Action;target.State=saved.State;target.Reason=saved.Reason;target.Result=saved.Result;target.OwnerPid=saved.OwnerPid;target.OwnerStarted=saved.OwnerStarted;
 }
}