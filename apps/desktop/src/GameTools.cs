using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

// Game tools share StoryService's single input owner and durable evidence ledger.
internal sealed class GameTools {
 readonly string root; readonly StoryClient story;
 internal GameTools(string root){this.root=root;story=new StoryClient(root);}
 string FrameFile {get{return Path.Combine(root,"game-frame.json");}}
 static string S(Dictionary<string,object> p,string k){return CodexChat.S(p,k);}
 static bool B(Dictionary<string,object> p,string k){return p.ContainsKey(k)&&Equals(p[k],true);}
 static double Wall(){return (DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;}
 internal static readonly string[] Commands={"observe","register","act","delegate","assist","check_dialogue","result","complete","status","interrupt","stop"};
 internal const string Guide="Prefer one register call per observed checkpoint: {task_id,frame_id,mode,evidence,paused,pause_available,previous_result:{outcome:success|failure,evidence},execution:{command:act|delegate|assist,parameters:{...}}}. previous_result is only for a pending input you have verified in this frame; omit when none. execution inherits task_id/frame_id and uses the existing executor. Omit execution to maintain a healthy running worker or enter automatic dialogue. These phases run sequentially; if a phase fails, earlier phases may already be recorded, so inspect status instead of blindly replaying. Do not split result/register/act into separate model turns when the same observed frame grounds all three. observe: {focus:false} returns a fresh HDR-corrected game-client image and frame_id without starting a task or changing focus. It can read the game behind Catheryne chat, but not a minimized window. For a request to look at or assess the current screen, observe and inspect the image, then answer; do not start a gameplay task. focus:true activates the game inside the selected execution environment through the existing controller. In isolated mode this never activates the user desktop; never substitute host computer-use or shell input when a child connection fails. Input commands still require a current foreground game, an explicit task, and fresh registered evidence. Read image before acting. Start task with catheryne_request; it launches the configured game with the shared AI display preset if needed and prepares the controller. An already running game is preserved. Do not launch separately. Then register: {task_id,frame_id,mode,evidence,paused:false,pause_available:false} (modes navigation,interaction,dialogue,puzzle,combat,escape,stealth,recovery,cutscene,paused). Set urgent:true only when observed danger requires protective pausing during reasoning; mode names alone do not imply urgency. act: {task_id,frame_id,intent,expected,steps:[{keys:['b'],seconds:0.1,dx:0,dy:0}]} max 5s total for direct interactions. When no verified map route is available, nonurgent navigation accepts a visible-ground movement/camera plan up to 30s total, at most 5s per step, using only w/a/s/d/shift/space/dodge and no positioned clicks. Navigation runs in the background by default (background:true); interrupt if the observed route diverges. This fallback has no automatic map correction. dx/dy must be integers within -1000..1000 per step. For a menu click include x,y in IMAGE pixels with keys:['attack']; coordinates must come from latest frame. Keys w a s d shift space e q f 1 2 3 4 5 v m b t j z escape enter attack dodge. Prefer the game shortcut j for quests instead of navigating Paimon menus when the user has not remapped it. Optional plan_revision explains changed strategy after failures. Observed paused menus allow one short (at most 0.3s) escape, enter, b, or positioned click/drag per act; gameplay inputs remain blocked. Register paused:true when the menu visibly pauses the world. Use background:true for bounded multi-step movement or dialogue: act returns immediately while the shared worker executes, so prepare the next conditional plan during execution. One steps array is one bounded batch, not one tool call per key. Do not submit competing input while owner is set. Once released, observe and record result before the next batch; do not execute a guessed future scene. Direct interactions default to foreground execution; background:false explicitly requests the same bounded wait and a fresh image. A returned image or pending result is NOT completion; inspect owner before assuming input has ended. If observation_error is returned, input was dispatched but no post-input image is available; do not repeat input. Observe again or stop if the game closed. result: {task_id,frame_id,outcome:success|failure|unknown,evidence} requires new post-input image. Unknown keeps the action pending: observe a NEW image and call result again after the transition, without repeating input. Reuse the returned image for result and the next register/act when still fresh and unchanged; batch those dependent calls sequentially in one exec after inspecting the image. Reobserve only for transitions, expired frames or uncertainty. register accepts dialogue_executor:repeat (default), manual, or bettergi. Use repeat normally; when repetition is ineffective, interrupt, record the observed result, then register dialogue with bettergi to use recognition or manual to use explicit clicks. Omitting dialogue_executor while remaining in dialogue preserves the chosen executor; a new dialogue defaults to repeat. Switching releases the old executor first; if a prior result is pending, resolve it before registering again to start the replacement. Registering nonurgent, unpaused dialogue automatically starts the existing continuous choice-position click executor, including ordinary choices. Do not call act or repeat_dialogue to start it separately. Observe/status do not interrupt it; registering dialogue again leaves it running. Register another observed mode to stop the dialogue executor automatically. If input results remain pending, record them before starting the next execution. Do not classify purchases or item handovers as ordinary dialogue. Plan: catheryne_request accepts plan as an ordered list of observable milestones. Its Result.plan_steps contains the canonical stage IDs. register accepts stage to select the observed milestone; omit only for a single-step plan or to keep the current stage. complete: {task_id,frame_id,evidence} verifies the current stage only. For a multi-stage plan continue with a new observation and register the next stage; the task ends only when all registered stages are verified. Never mark stages complete just to advance the UI. status and stop take task_id. Do not spend resin/currency, buy, pull or delete unless specifically requested. Never infer success from input receipt. Use stop when interrupted. For a model-planned continuous route use delegate with route:{title,evidence,positions:[{x,y,type:teleport|path|target,move_mode:walk|run|dash|climb|fly}]}, intent and expected. Coordinates MUST be verified Teyvat map coordinates from catheryne_query bettergi_route (query: exact route name) or delegate position:current, whose recognized coordinates are returned in status.execution.result.observation after execution, never screen pixels or guessed numbers. To obtain a destination from an open Teyvat map, first inspect and center the intended marker, then delegate position:map_center while registering the actual paused map state. This read-only built-in preserves pause and returns the MAP CENTER, not the character or an automatically selected quest. Verify the center matches the intended destination before using it in a route; close the map and register navigation before moving. Teleport is optional and first only; final point must be target. The same existing pathing engine follows all points continuously with local position correction. Do not replace a known route with repeated 5-second act calls. Route and groups are mutually exclusive. dialogue:auto is a state-bound continuous executor: use it alone, never inside a sequential bundle with other projects. When dialogue ends, change state, record the result, and submit the next plan. For registered automation bundles or routes inspect catheryne_query bettergi_groups, then delegate:{task_id,frame_id,groups:[exact catalogue names in execution order],intent,expected}. The catalogue includes installed routes and combat:auto using the existing party-recognition/rotation/end-detection engine. Check configuration_ready and configuration_issue before selecting built-ins; configuration_ready does not verify the current game scene. Inspect route/project definitions before selecting; do not invent coordinates. Non-dialogue delegated bundles are bounded to 10 minutes. Dialogue continues until its state changes or it is interrupted. interrupt requests cancellation and confirms input release before returning. During execution use observe/status without interrupting: keep a healthy execution running while reasoning. When stuck or the plan is wrong call interrupt:{task_id,evidence}; this stops only current execution and preserves the parent task and milestones. Inspect its returned image, record result if pending, register the newly observed state, then delegate a revised plan. Stop cancels the entire task and is not the replanning operation. Never press B concurrently with a running executor; interrupt and verify input release first, then pause only when observed and needed. The managed bundle runs asynchronously under this same task/input owner; reason about the next checkpoint while it runs. Observe and verify after owner is released. Prefer that route over per-click model navigation. For combat prefer the registered combat:auto delegate when available; its party recognition and rotations do not require the local calibrated HUD profile. Alternatively, with an installed calibrated HUD profile use assist:{task_id,slots:[observed party slots],seconds:120,require_target:true,allow_approach:false} after registering combat. It starts the existing local HUD/state worker and returns immediately; do not replace it with repeated manual skill calls. It requires the installed calibrated HUD profile. Optional use_food:true requires explicit food-use authorization and a calibrated heal_ready detector for equipped NRE on Z; it prioritizes low HP locally and verifies HP recovery. It defaults off. Poll status for changes while the worker continues without model calls. Stop uses the same task control. Unsupported or uncalibrated fields are errors, not permission to guess.";
 Dictionary<string,object> Call(string method,object parameters){return story.Call(method,parameters,true);}
 internal bool Available(){try{if(GameEnvironment.Remote(root))return Convert.ToBoolean(GameEnvironment.Invoke(root,"game.available",new Dictionary<string,object>(),false));return Window(false)!=IntPtr.Zero;}catch{return false;}}
 internal static bool MatchesAspect(int width,int height,int horizontal,int vertical){return width>0&&height>0&&Math.Abs((long)width*vertical-(long)height*horizontal)<=horizontal;}
 internal void RequireAspect(int horizontal,int vertical,int minimumHeight=0){
  if(GameEnvironment.Remote(root)){GameEnvironment.Invoke(root,"game.aspect",new Dictionary<string,object>{{"horizontal",horizontal},{"vertical",vertical},{"minimum_height",minimumHeight}},false);return;}
  IntPtr prior=SetThreadDpiAwarenessContext(new IntPtr(-4));try{var bounds=Bounds(Window(false));if(bounds.Height<minimumHeight)throw new InvalidOperationException(Locale.Format("현재 게임 화면은 {0} × {1}입니다. 이 작업에는 {2}p 이상의 게임 화면이 필요합니다.",bounds.Width,bounds.Height,minimumHeight));if(!MatchesAspect(bounds.Width,bounds.Height,horizontal,vertical))throw new InvalidOperationException(Locale.Format("현재 게임 화면은 {0} × {1}입니다. 화면 설정에서 {2}:{3} 해상도로 전환한 뒤 실행해 주세요.",bounds.Width,bounds.Height,horizontal,vertical));}finally{SetThreadDpiAwarenessContext(prior);}
 }
 internal static string StageId(AiTaskRecord task,int index){return task.Plan==null||task.Plan.Length==1?"task":"step-"+(index+1);}
 internal static object BuildPlan(AiTaskRecord task){return new{id="ai-"+task.Id,title=task.Title,draft=false,scope=task.Title,sources=new object[0],steps=(task.Plan??new[]{task.Title}).Select((title,index)=>new{id=StageId(task,index),title=title,modes=new[]{"navigation","interaction","dialogue","puzzle","combat","escape","stealth","recovery","cutscene","paused"},tools=new[]{"sequence","computer_use"},completion=title+" — 현재 게임 화면에서 결과 확인",checkpoint="새 화면에서 결과를 확인하고 기록",failure_cost="예상과 다르면 입력 중단 후 재관측",sources=new object[0]}).ToArray()};}
 internal void Start(AiTaskRecord task){
  if(GameEnvironment.Remote(root)){task.Action="game_control";task.Reason="게임 실행 환경 연결 중";new AiTaskStore(root).Save(task);GameEnvironmentOperations.UpdateTask(task,GameEnvironment.Invoke(root,"task.start",new Dictionary<string,object>{{"task",task}},true,()=>new AiTaskStore(root).ThrowIfCancelled(task.Id)));return;}
  task.Action="game_control";
  var store=new AiTaskStore(root);
  if(!Available()){
   var launcher=new LauncherOperations(root);
   if(!Setup.Ready(launcher.Installation())){task.State="blocked";task.Reason="게임 설치 설정이 필요합니다.";return;}
   task.Reason="원신 실행 중";store.Save(task);
   if(!ProcessGuard.Busy())launcher.Apply("launch",0,false);
   var waiting=Stopwatch.StartNew();
   while(!Available()){
    CheckPreparing(task,store);
    if(waiting.Elapsed.TotalSeconds>=60){task.State="blocked";task.Reason="원신 창을 확인하지 못했습니다.";return;}
    Thread.Sleep(250);
   }
  }
  CheckPreparing(task,store);
  try{Call("status",new{after_sequence=0});task.State="blocked";task.Reason="기존 게임 작업이 연결되어 있습니다. 실행 현황에서 이어가거나 종료해 주세요.";return;}catch(InvalidOperationException){}
  var plan=BuildPlan(task);
  string path=Path.Combine(root,"plans","ai-"+task.Id+".json");Directory.CreateDirectory(Path.GetDirectoryName(path));AtomicFile.Write(path,CatheryneTools.Json().Serialize(plan));story.SetPlan(path);story.Start(true);var connected=Call("status",new{after_sequence=0});using(var owner=Process.GetProcessById(Convert.ToInt32(connected["host_pid"]))){task.OwnerPid=owner.Id;task.OwnerStarted=owner.StartTime.ToUniversalTime().ToString("o");}
  try{CheckPreparing(task,store);}catch(OperationCanceledException){story.Shutdown();throw;}
  task.State="running";task.Reason="게임 화면 확인 대기";task.Result=connected;
 }
 AiTaskRecord TaskFor(Dictionary<string,object> p,string thread){var t=new AiTaskStore(root).Find(S(p,"task_id"));if(t==null||t.Action!="game_control"||t.State!="running"||t.Thread!=thread)throw new GameTaskMismatch("현재 대화의 실행 중인 게임 작업이 아닙니다.",S(p,"task_id"),t,new AiTaskStore(root).List(thread));var status=Call("status",new{after_sequence=0});if(S(status,"plan_id")!="ai-"+t.Id)throw new InvalidOperationException("연결된 임무가 변경되었습니다.");return t;}
 internal object ChangeControl(string id,string target,string thread){
  if(GameEnvironment.Remote(root)){var remote=GameEnvironment.Invoke(root,"game.control",new Dictionary<string,object>{{"id",id},{"target",target},{"thread",thread}},false);if(target=="user")GameEnvironment.ShowView(root,true);else GameEnvironment.HideView(root);return remote;}
  TaskFor(new Dictionary<string,object>{{"task_id",id}},thread);
  if(target!="user"&&target!="agent")throw new ArgumentException("Invalid control");
  var result=Call("set_control",new{target=target,reason="user_request"});
  if(S(result,"control")!=target)throw new InvalidOperationException("조작권 변경을 확인하지 못했습니다.");
  return result;
 }
 // Keep rich canonical status for the UI and journal; project only the model reply.
 internal static object ModelReply(object value){
  var report=value as Dictionary<string,object>;
  if(report==null)return value;
  if(report.ContainsKey("input_status")){
   var reply=new Dictionary<string,object>(report);
   var input=CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(report["input_status"])));
   object state;if(input.TryGetValue("state",out state))input["state"]=ModelReply(state);
   reply["input_status"]=input;return reply;
  }
  if(!report.ContainsKey("plan_id")||!report.ContainsKey("mode"))return value;
  var result=new Dictionary<string,object>(report);
  foreach(string field in new[]{"plan_title","stage_title","host_pid","settings","session_seconds","supervisor_wait_seconds","cursor"})result.Remove(field);
  return result;
 }
 internal object Run(string command,Dictionary<string,object> p,string thread,Action<AiTaskRecord> changed){
  if(command=="stop"&&GameEnvironment.Remote(root)&&CodexChat.S(GameEnvironment.Status(root),"state")!="ready")return StopTask(S(p,"task_id"),thread);
  if(GameEnvironment.Remote(root)){
   object reply=GameEnvironment.Invoke(root,"game.run",new Dictionary<string,object>{{"command",command},{"parameters",p},{"thread",thread}},false);
   var task=new AiTaskStore(root).Find(S(p,"task_id"));if(task!=null&&changed!=null)changed(task);return reply;
  }
  return ModelReply(command=="register"&&(p.ContainsKey("previous_result")||p.ContainsKey("execution"))
   ?RegisterAndExecute(p,(next,args)=>Execute(next,args,thread,changed)):Execute(command,p,thread,changed));
 }
 // Compose existing domain commands against one observed frame. This is not a second executor.
 internal static object RegisterAndExecute(Dictionary<string,object> p,Func<string,Dictionary<string,object>,object> run){
  string task=Required(p,"task_id"),frame=Required(p,"frame_id");
  var observation=new Dictionary<string,object>(p);observation.Remove("previous_result");observation.Remove("execution");
  Dictionary<string,object> previous=null,next=null;string command=null;
  if(p.ContainsKey("previous_result")){
   var value=p["previous_result"] as Dictionary<string,object>;if(value==null)throw new ArgumentException("previous_result must be an object");
   string outcome=Required(value,"outcome");if(outcome!="success"&&outcome!="failure")throw new ArgumentException("Resolve an unknown result with a new observation before continuing");
   previous=new Dictionary<string,object>{{"task_id",task},{"frame_id",frame},{"outcome",outcome},{"evidence",Required(value,"evidence")}};
  }
  if(p.ContainsKey("execution")){
   var value=p["execution"] as Dictionary<string,object>;if(value==null)throw new ArgumentException("execution must be an object");
   command=Required(value,"command");if(!new[]{"act","delegate","assist"}.Contains(command))throw new ArgumentException("execution.command must be act, delegate, or assist");
   object raw;if(!value.TryGetValue("parameters",out raw)||!(raw is Dictionary<string,object>))throw new ArgumentException("execution.parameters must be an object");
   next=new Dictionary<string,object>((Dictionary<string,object>)raw);
   if(new[]{"task_id","frame_id","previous_result","execution"}.Any(next.ContainsKey))throw new ArgumentException("Execution inherits this task and frame; do not override them");
   if(S(p,"mode")=="dialogue"&&S(p,"dialogue_executor")!="manual")throw new ArgumentException("Dialogue starts automatically on registration; omit execution");
   next["task_id"]=task;next["frame_id"]=frame;
  }
  Required(observation,"mode");Required(observation,"evidence");
  if(previous!=null)run("result",previous);
  var registered=run("register",observation);
  return next==null?registered:run(command,next);
 }
 object Execute(string command,Dictionary<string,object> p,string thread,Action<AiTaskRecord> changed){
 if(ExternalTools.BetterGiRunning()&&command!="observe"&&command!="status"&&command!="interrupt"&&command!="register"&&command!="stop")throw new InvalidOperationException("BetterGI 실행 중에는 다른 게임 입력을 사용할 수 없습니다.");
 if(CollectionScanner.Busy()&&command!="stop"&&command!="status")throw new InvalidOperationException("정보 수집 중에는 다른 게임 조작을 할 수 없습니다.");
  if(!Commands.Contains(command))throw new ArgumentException("Unknown game command");
  if(command=="observe")return Capture(B(p,"focus"));
  if(command=="stop"){var stopped=StopTask(S(p,"task_id"),thread);if(changed!=null)changed(stopped);return stopped;}
  var t=TaskFor(p,thread);var store=new AiTaskStore(root);
  if(command=="status"){var children=store.List().Where(x=>x.ParentTaskId==t.Id).ToArray();new ExternalTools(root).RefreshTasks(children);var report=Call("status",new{after_sequence=0});var child=children.FirstOrDefault(x=>x.Id==S(report,"external_task"))??children.OrderByDescending(x=>x.Started,StringComparer.Ordinal).FirstOrDefault();if(child!=null){var childProgress=CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(child.Result)));DateTime updated;string stamp=S(childProgress,"progress_updated_at");if(string.IsNullOrEmpty(stamp))stamp=child.Started;double? age=DateTime.TryParse(stamp,out updated)?(double?)Math.Max(0,(DateTime.UtcNow-updated.ToUniversalTime()).TotalSeconds):null;report["execution"]=new{task_id=child.Id,state=child.State,reason=child.Reason,started=child.Started,result=child.Result,seconds_since_progress_report=age,review_needed=child.State=="running"&&age>=60,review_reason=child.State=="running"&&age>=60?"No recent executor progress report; observe without interrupting. Elapsed time alone does not prove a stall.":null};}return report;}
  if(command=="interrupt"){
   string reason=Required(p,"evidence");
   StopDelegates(t.Id);Call("stop",new{reason="agent_replan"});
   var interrupted=WaitForInput();if(!string.IsNullOrEmpty(S(interrupted,"owner")))throw new InvalidOperationException("입력 종료를 확인하는 중입니다.");
   t.Reason=reason;store.Save(t);if(changed!=null)changed(t);
   return p.ContainsKey("include_image")&&!B(p,"include_image")?interrupted:AfterInput(()=>Capture(false),interrupted);
  }

  if(command=="check_dialogue"&&p.ContainsKey("active")&&Equals(p["active"],false)){
   var stopped=Call("status",new{after_sequence=0});
   if(S(stopped,"owner")=="dialogue")Call("check_dialogue",new{active=false,evidence=Required(p,"evidence"),captured_wall=0});
   stopped=WaitForInput();t.Reason="대화 입력 중지, 화면 확인 중";store.Save(t);if(changed!=null)changed(t);
   return AfterInput(()=>Capture(false),stopped);
  }
  if(command=="delegate"){
   var delegateFrame=FreshFrame(S(p,"frame_id"));var status=Call("status",new{after_sequence=0});
   if(!Equals(Convert.ToDouble(status["observation_wall"]),Convert.ToDouble(delegateFrame["captured_wall"])))throw new InvalidOperationException("현재 화면의 상황을 먼저 등록해 주세요.");
   if(p.ContainsKey("route")&&(p.ContainsKey("groups")||p.ContainsKey("group")))throw new ArgumentException("경로와 작업 묶음 중 하나만 지정해 주세요.");
   var route=p.ContainsKey("route")?CodexChat.Map(p["route"]):null;string[] bundles=route!=null?new string[0]:p.ContainsKey("groups")?((System.Collections.IEnumerable)p["groups"]).Cast<object>().Select(Convert.ToString).ToArray():new[]{Required(p,"group")};
   return Delegate(t,bundles,route,Required(p,"intent"),Required(p,"expected"),changed);
  }
  if(command=="assist"){
   var args=new Dictionary<string,object>();foreach(string key in new[]{"slots","seconds","require_target","allow_approach","use_food"})if(p.ContainsKey(key))args[key]=p[key];
   var started=Call("start_assist",args);RecordAction(t.Id);t.Reason="전투 보조 실행 중";store.Save(t);if(changed!=null)changed(t);return started;
  }
  var frame=FreshFrame(S(p,"frame_id"));
  if(command=="check_dialogue"){
   if(!p.ContainsKey("active")||!(p["active"] is bool))throw new ArgumentException("active must be boolean");
   var checkedDialogue=Call("check_dialogue",new{active=true,evidence=Required(p,"evidence"),captured_wall=Convert.ToDouble(frame["captured_wall"])});
   t.Reason=S(p,"evidence");store.Save(t);if(changed!=null)changed(t);return checkedDialogue;
  }
  if(command=="register"){
   var prior=Call("status",new{after_sequence=0});string dialogueExecutor=S(p,"dialogue_executor");if(string.IsNullOrEmpty(dialogueExecutor)&&S(prior,"mode")=="dialogue"&&S(p,"mode")=="dialogue")dialogueExecutor=S(prior,"dialogue_executor");if(string.IsNullOrEmpty(dialogueExecutor))dialogueExecutor="repeat";
   if(!new[]{"repeat","manual","bettergi"}.Contains(dialogueExecutor))throw new ArgumentException("Unknown dialogue executor");
   var externalDialogue=store.List().FirstOrDefault(x=>x.ParentTaskId==t.Id&&x.State=="running"&&ExternalTools.IsDialogueTask(x));
   bool keepExternal=externalDialogue!=null&&S(p,"mode")=="dialogue"&&dialogueExecutor=="bettergi"&&!B(p,"paused")&&!B(p,"urgent");
   if(externalDialogue!=null&&!keepExternal)StopDelegates(t.Id);
   var before=Call("status",new{after_sequence=0});
   bool continuing=(keepExternal||S(before,"owner")=="dialogue"&&dialogueExecutor=="repeat")&&S(p,"mode")=="dialogue"&&!B(p,"paused")&&!B(p,"urgent");
   if(S(before,"owner")=="dialogue"&&!continuing){Call("stop",new{reason="dialogue_state_changed"});var released=WaitForInput();if(!string.IsNullOrEmpty(S(released,"owner")))throw new InvalidOperationException("대화 입력 종료를 확인하는 중입니다.");}
   var status=Call("status",new{after_sequence=0});string stage=S(p,"stage");if(string.IsNullOrEmpty(stage))stage=string.IsNullOrEmpty(S(status,"stage"))?StageId(t,0):S(status,"stage");var result=Call("observe",new{stage=stage,mode=S(p,"mode"),evidence=Required(p,"evidence"),captured_wall=Convert.ToDouble(frame["captured_wall"]),paused=B(p,"paused"),pause_available=B(p,"pause_available"),urgent=B(p,"urgent"),dialogue_executor=dialogueExecutor});t.Reason=S(p,"evidence");t.Result=result;store.Save(t);if(changed!=null)changed(t);if(S(p,"mode")=="dialogue"&&!B(p,"paused")&&!B(p,"urgent")&&!continuing&&dialogueExecutor!="manual"&&result["pending_result"]==null)return dialogueExecutor=="bettergi"?Delegate(t,new[]{"dialogue:auto"},null,"Continue ordinary dialogue using recognition","Dialogue ends",changed):StartDialogue(t,p,frame,changed);return result;
  }
  if(command=="act"){
   var status=Call("status",new{after_sequence=0});if(!status.ContainsKey("observation_wall")||!Equals(Convert.ToDouble(status["observation_wall"]),Convert.ToDouble(frame["captured_wall"])))throw new InvalidOperationException("현재 화면의 상황을 먼저 등록해 주세요.");
   var steps=new List<Dictionary<string,object>>();object raw;if(!p.TryGetValue("steps",out raw))throw new ArgumentException("steps required");
   foreach(var item in (IEnumerable)raw){var step=new Dictionary<string,object>(CodexChat.Map(item));if(step.ContainsKey("x")||step.ContainsKey("y")){
    int x=Coordinate(step,"x",Convert.ToInt32(frame["image_width"])),y=Coordinate(step,"y",Convert.ToInt32(frame["image_height"]));step.Remove("x");step.Remove("y");step["cursor"]=new{ x=(int)((long)x*Convert.ToInt32(frame["width"])/Convert.ToInt32(frame["image_width"])),y=(int)((long)y*Convert.ToInt32(frame["height"])/Convert.ToInt32(frame["image_height"])),width=frame["width"],height=frame["height"]};}steps.Add(step);}
   var args=new Dictionary<string,object>{{"steps",steps},{"intent",Required(p,"intent")},{"expected",Required(p,"expected")}};if(p.ContainsKey("plan_revision"))args["plan_revision"]=p["plan_revision"];
   if(p.ContainsKey("repeat_dialogue")){if(!(p["repeat_dialogue"] is bool))throw new ArgumentException("repeat_dialogue must be boolean");args["repeat_dialogue"]=p["repeat_dialogue"];}
   bool background=RunInBackground(p,status);
   Call("run",args);RecordAction(t.Id);
   if(background){t.Reason=B(p,"repeat_dialogue")?"대화 진행 중":"계획 실행 중";store.Save(t);if(changed!=null)changed(t);return Call("status",new{after_sequence=0});}
   status=B(p,"repeat_dialogue")?Call("status",new{after_sequence=0}):WaitForInput();
   t.Reason=B(p,"repeat_dialogue")?"대화 진행 중":"입력 후 결과 확인 중";store.Save(t);if(changed!=null)changed(t);
   Thread.Sleep(250);var result=AfterInput(()=>Capture(false),status);if(result.ContainsKey("observation_error")){t.Reason="입력 후 화면 확인이 필요합니다.";store.Save(t);if(changed!=null)changed(t);}return result;
  }
  if(command=="result"){
   string path=Path.Combine(root,"game-action.json");if(!File.Exists(path))throw new InvalidOperationException("확인할 입력 기록이 없습니다.");var action=StoryClient.Read(path);if(S(action,"task_id")!=t.Id||Convert.ToDouble(frame["captured_wall"])<=Convert.ToDouble(action["started"]))throw new InvalidOperationException("입력 후의 새 화면이 필요합니다.");
   double captured=Convert.ToDouble(frame["captured_wall"]);if(action.ContainsKey("result_observed_at")&&captured<=Convert.ToDouble(action["result_observed_at"]))throw new InvalidOperationException("전환이 끝난 새 화면을 확인한 뒤 결과를 다시 기록해 주세요.");
   var result=Call("result",new{outcome=S(p,"outcome"),evidence=Required(p,"evidence")});action["result_observed_at"]=captured;AtomicFile.Write(path,CatheryneTools.Json().Serialize(action));return result;
  }
  var active=Call("status",new{after_sequence=0});var complete=Call("complete",new{stage=S(active,"stage"),evidence=Required(p,"evidence")});t.Result=complete;var progress=CodexChat.Map(complete["progress"]);bool finished=Convert.ToInt32(progress["completed"])==Convert.ToInt32(progress["total"]);store.End(t,finished?"completed":"running",S(p,"evidence"));if(changed!=null)changed(t);if(finished)story.Shutdown();return t;
 }
 internal static bool RunInBackground(Dictionary<string,object> parameters,Dictionary<string,object> scene){
  object value;if(parameters.TryGetValue("background",out value)){if(!(value is bool))throw new ArgumentException("background must be boolean");return (bool)value;}
  return S(scene,"mode")=="navigation"&&!B(scene,"paused");
 }
 Dictionary<string,object> StartDialogue(AiTaskRecord t,Dictionary<string,object> p,Dictionary<string,object> frame,Action<AiTaskRecord> changed){
  int width=Convert.ToInt32(frame["width"]),height=Convert.ToInt32(frame["height"]);
  int x=p.ContainsKey("x")?Coordinate(p,"x",Convert.ToInt32(frame["image_width"]))*width/Convert.ToInt32(frame["image_width"]):width*61/80;
  int y=p.ContainsKey("y")?Coordinate(p,"y",Convert.ToInt32(frame["image_height"]))*height/Convert.ToInt32(frame["image_height"]):height*107/144;
  var result=Call("run",new{steps=new object[]{new{keys=new[]{"attack"},seconds=.05,cursor=new{x=x,y=y,width=width,height=height}},new{keys=new string[0],seconds=.4}},intent="Advance ordinary dialogue and choices",expected="Dialogue ends",repeat_dialogue=true});
  RecordAction(t.Id);t.Reason="대화 진행 중";new AiTaskStore(root).Save(t);if(changed!=null)changed(t);return result;
 }
 Dictionary<string,object> Delegate(AiTaskRecord t,string[] bundles,Dictionary<string,object> route,string intent,string expected,Action<AiTaskRecord> changed){
  var store=new AiTaskStore(root);var external=new ExternalTools(root);
   var child=store.Begin(t.Title,t.Thread,Guid.NewGuid().ToString("N"),"catheryne_game",t.TurnId);child.ParentTaskId=t.Id;store.Save(child);
   bool reserved=false,dispatched=false;
   try{string bundle=external.PrepareBundle(bundles,child.Id,route);Call("begin_external",new{task_id=child.Id,intent=intent,expected=expected,observation_only=ExternalTools.IsObservationTask(store.Find(child.Id))});reserved=true;RecordAction(t.Id);external.RunGroup(bundle,child,()=>dispatched=true);}
   catch(Exception error){
    if(!dispatched){
     try{if(reserved)Call("end_external",new{task_id=child.Id,error=error.Message});}
     finally{try{store.End(child,"failed",error.Message);}finally{external.ReleaseBundle(child.Id);}}
    }
    throw;
   }
   t.Reason="계획 실행 중";store.Save(t);if(changed!=null)changed(t);return Call("status",new{after_sequence=0});
 }
 void StopDelegates(string taskId){
  var store=new AiTaskStore(root);
  foreach(var child in store.List().Where(x=>x.ParentTaskId==taskId&&x.State=="running"))store.RequestCancellation(child.Id);
  var wait=System.Diagnostics.Stopwatch.StartNew();while(S(Call("status",new{after_sequence=0}),"owner")=="external"){
   if(wait.Elapsed.TotalSeconds>10)throw new InvalidOperationException("위임 작업의 종료를 기다리고 있습니다. 입력권은 잠긴 상태입니다.");System.Threading.Thread.Sleep(100);
  }
 }
 void RecordAction(string taskId){AtomicFile.Write(Path.Combine(root,"game-action.json"),CatheryneTools.Json().Serialize(new{task_id=taskId,started=Wall()}));}
 Dictionary<string,object> WaitForInput(){var clock=Stopwatch.StartNew();Dictionary<string,object> status;do{Thread.Sleep(80);status=Call("status",new{after_sequence=0});}while(status.ContainsKey("owner")&&status["owner"]!=null&&clock.ElapsedMilliseconds<6500);return status;}
 internal static Dictionary<string,object> AfterInput(Func<Dictionary<string,object>> capture,Dictionary<string,object> status){
  Dictionary<string,object> result;
  try{result=capture();}
  catch(InvalidOperationException ex){result=ObservationUnavailable(ex.Message);}
  catch(System.ComponentModel.Win32Exception ex){result=ObservationUnavailable(ex.Message);}
  result["input_status"]=new{outcome="unverified",state=status};return result;
 }
 static Dictionary<string,object> ObservationUnavailable(string message){return new Dictionary<string,object>{{"observation_error",message},{"next","Input was dispatched; its result is unknown. Do not repeat the action. Observe the current game state again, or stop the task if the game has closed. This is not verified completion."}};}
 void CheckPreparing(AiTaskRecord task,AiTaskStore store){store.ThrowIfCancelled(task.Id);}
 AiTaskRecord StopTask(string id,string thread){
  var store=new AiTaskStore(root);var task=store.Find(id);
  if(task==null||task.Thread!=thread||task.Action!="game_control")throw new InvalidOperationException("현재 대화의 게임 작업이 아닙니다.");
  if(AiTaskStore.Terminal(task.State))return task;
  store.RequestCancellation(id);
  Dictionary<string,object> status=null;try{status=Call("status",new{after_sequence=0});}catch(InvalidOperationException){}
  if(status!=null&&S(status,"plan_id")=="ai-"+id){Call("set_control",new{target="user",reason="chat_stop"});StopDelegates(id);story.Shutdown();}
  return store.End(task,"cancelled","작업을 중단했습니다.");
 }
 internal void StopThread(string thread){foreach(var task in new AiTaskStore(root).List().Where(t=>t.Thread==thread&&t.Action=="game_control"&&t.State=="running"))StopTask(task.Id,thread);}
 static string Required(Dictionary<string,object> p,string k){string s=S(p,k);if(string.IsNullOrWhiteSpace(s)||s.Length>2000)throw new ArgumentException(k+" is required (1..2000 chars)");return s;}
 internal static int Coordinate(Dictionary<string,object> p,string key,int limit){object value;if(!p.TryGetValue(key,out value)||!(value is int)||Convert.ToInt32(value)<0||Convert.ToInt32(value)>=limit)throw new ArgumentException("화면 내부의 정수 좌표가 필요합니다.");return (int)value;}
 Dictionary<string,object> FreshFrame(string id){IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(-4));try{if(!File.Exists(FrameFile))throw new InvalidOperationException("게임 화면을 먼저 확인해 주세요.");var f=StoryClient.Read(FrameFile);double age=Wall()-Convert.ToDouble(f["captured_wall"]);if(S(f,"frame_id")!=id||age<0||age>30)throw new InvalidOperationException("새 게임 화면이 필요합니다.");var hwnd=Window(false);var r=Bounds(hwnd);if(hwnd.ToInt64()!=Convert.ToInt64(f["hwnd"])||GetForegroundWindow()!=hwnd||r.X!=Convert.ToInt32(f["left"])||r.Y!=Convert.ToInt32(f["top"])||r.Width!=Convert.ToInt32(f["width"])||r.Height!=Convert.ToInt32(f["height"]))throw new InvalidOperationException("게임 창이 이동했거나 포커스가 바뀌었습니다. 다시 확인해 주세요.");return f;}finally{SetThreadDpiAwarenessContext(previous);}}
 static readonly ImageCodecInfo FrameCodec=ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid);
 internal static string EncodeFrame(Bitmap frame){using(var stream=new MemoryStream())using(var options=new EncoderParameters(1)){options.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,90L);frame.Save(stream,FrameCodec,options);return "data:image/jpeg;base64,"+Convert.ToBase64String(stream.ToArray());}}
 internal Dictionary<string,object> Capture(bool focus){
  bool tracked=false;string captureError=null;IntPtr prior=SetThreadDpiAwarenessContext(new IntPtr(-4));try{
   IntPtr hwnd=Window(focus);if(IsIconic(hwnd))throw new InvalidOperationException("최소화된 게임 창을 표시해 주세요.");var r=Bounds(hwnd);if(r.Width<64||r.Height<64||!System.Windows.Forms.SystemInformation.VirtualScreen.Contains(r))throw new InvalidOperationException("게임 창 전체가 화면 안에 보여야 합니다.");
   if(story.HasConnection()){Call("capture_started",new{});tracked=true;}
   double capturedAt=Wall();
   int width=Math.Min(1280,r.Width),height=(int)((long)r.Height*width/r.Width);string image;
   bool hdr;float whiteNits;
   using(var capture=GameCapture.Read(hwnd)){if(Bounds(hwnd)!=r||capture.Bounds!=r)throw new InvalidOperationException("캡처 중 게임 창이 변경되었습니다.");hdr=capture.Hdr;whiteNits=capture.WhiteNits;using(var scaled=new Bitmap(width,height)){using(var g=Graphics.FromImage(scaled)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(capture.Bitmap,0,0,width,height);}image=EncodeFrame(scaled);}}
   var result=new Dictionary<string,object>{{"frame_id",Guid.NewGuid().ToString("N")},{"captured_wall",capturedAt},{"hwnd",hwnd.ToInt64()},{"left",r.Left},{"top",r.Top},{"width",r.Width},{"height",r.Height},{"image_width",width},{"image_height",height},{"color_space","sRGB"},{"hdr_tonemapped",hdr},{"sdr_white_nits",whiteNits}};Directory.CreateDirectory(root);AtomicFile.Write(FrameFile,CatheryneTools.Json().Serialize(result));result["image_url"]=image;return result;
  }catch(Exception error){captureError=error.Message;throw;}finally{try{if(tracked)Call("capture_finished",new{error=captureError});}finally{SetThreadDpiAwarenessContext(prior);}}
 }
 IntPtr Window(bool focus){var hwnd=GameWindow.Find(root);if(focus&&GetForegroundWindow()!=hwnd){var host=Call("status",new{after_sequence=0});AllowSetForegroundWindow(Convert.ToUInt32(host["host_pid"]));Call("focus",new{});Thread.Sleep(120);}return hwnd;}
 static Rectangle Bounds(IntPtr hwnd){return GameWindow.Bounds(hwnd);}
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(uint processId);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr hwnd);
 [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}

// A stale handle is an identity/ownership error, not a capability failure.
internal sealed class GameTaskMismatch : InvalidOperationException {
 internal readonly object Recovery;
 internal GameTaskMismatch(string message,string requested,AiTaskRecord previous,IEnumerable<AiTaskRecord> tasks):base(message){Recovery=new{requested_task_id=requested,requested_state=previous==null?null:previous.State,current_tasks=tasks.Where(t=>t.Action=="game_control"&&t.State=="running").Select(t=>new{task_id=t.Id,title=t.Title,operation=t.Operation}).ToArray(),next="Read current task state and ownership. Never revive a terminal task; a successor requires an explicit user request."};}
}
