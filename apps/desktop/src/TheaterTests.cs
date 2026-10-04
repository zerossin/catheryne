using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
internal static class TheaterTests {
 static Dictionary<string,object> D(string text){return CatheryneTools.Json().Deserialize<Dictionary<string,object>>(text);}
 static Dictionary<string,object> M(object value){return CodexChat.Map(value);}
 static void Check(bool value,string reason){if(!value)throw new Exception("Theater: "+reason);}
 static void Reject(Action action,string reason){try{action();}catch(ArgumentException){return;}catch(InvalidOperationException){return;}throw new Exception("Theater accepted: "+reason);}
 static Dictionary<string,object> WithPlanning(Dictionary<string,object> p){if(p.ContainsKey("team")){p["assessment"]=TheaterPlanningTests.Assessment();p["battlePlan"]=TheaterPlanningTests.Remaining(5,10);p["changeReason"]="Fixture changed observation";}return p;}
 internal static void Run(){
  string root=Path.Combine(Path.GetTempPath(),"theater-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   TheaterTaskTests.Run(root);TheaterPlanningTests.Run(root);TheaterOptimizationTests.Run(root);
   Check(TheaterKnowledge.Period(new DateTime(2026,10,1,3,0,0,DateTimeKind.Utc),"os_asia")=="2026-10-01","Asian server season");
   Check(TheaterKnowledge.Period(new DateTime(2026,10,1,3,0,0,DateTimeKind.Utc),"os_usa")=="2026-09-01","American server has not reset");
   var beforeAsiaReset=new DateTime(2026,9,30,19,59,59,DateTimeKind.Utc);
   Check(TheaterKnowledge.Period(beforeAsiaReset,"os_asia")=="2026-09-01","Korean October date retains September before server reset");
   Check(TheaterKnowledge.Period(beforeAsiaReset.AddSeconds(1),"os_asia")=="2026-10-01","Asian season changes at the exact reset");
   Check(TheaterKnowledge.PeriodEnd(beforeAsiaReset,"os_asia")==beforeAsiaReset,"shown deadline matches the reset boundary");
   string period=new TheaterKnowledge(root).Period();int calls=0;bool fullBattles=false;
   var knowledge=new TheaterKnowledge(root,page=>{System.Threading.Interlocked.Increment(ref calls);return new Dictionary<string,object>{{"text",page.Contains("/Seasons/")?"{{Imaginarium Theater Season\n|start = "+period+" 04:00:00\n|elements = Hydro; Cryo; Anemo\n}}\n"+(fullBattles?"=== Act 1 ===\n|enemies1 = First enemy\n|target1 = Win\n=== Arcana Challenge I ===\n|enemies1 = Additional enemy\n|target1 = Win":"<!-- === Act 2 ===\n|enemies1 = fake\n|target1 = fake -->"):"Rules and events reference. A visible result is required."},{"source","https://example.org/reference"},{"checkedAt",DateTime.UtcNow.ToString("o")}};});
   var prepared=knowledge.Prepare();knowledge.Prepare();Check(calls==3+TheaterCombatReference.Sources.Length,"fresh preparation does not redownload");
   var facts=TheaterKnowledge.SeasonFacts(M(M(M(prepared["documents"])["season"])));Check(Equals(facts["battlesAvailable"],false),"commented draft battles are not ready");
   string oldReference=Convert.ToString(prepared["referenceId"]);fullBattles=true;var updated=knowledge.Prepare(true);Check(Convert.ToString(updated["referenceId"])!=oldReference&&knowledge.Read(oldReference).ContainsKey("documents"),"reference refresh retains previous immutable source");
   var service=new TheaterService(root,()=>"Fixture account refreshed");var overview=service.Status("preview");
   Check(M(overview["preparedData"]).ContainsKey("characters")&&CodexChat.Items(overview["references"]).Count()==3+TheaterCombatReference.Sources.Length,"GUI and tools share reference metadata without serialization");
   Check(Convert.ToInt32(M(overview["preparedData"])["acts"])==1&&Convert.ToInt32(M(overview["preparedData"])["additionalBattles"])==1,"base acts and additional battles are separately counted from source text");
   Check(M(overview["preparedData"])["weapons"]==null,"uncollected gear count remains unknown");
   var started=M(service.Run("start",D("{\"goal\":\"완주와 별 확보\"}"),"test-chat"));var session=M(started["session"]);string id=Convert.ToString(session["id"]);Check(Convert.ToString(session["goal"])=="완주와 별 확보","resume context exposes the persisted user goal");
   Check(Convert.ToString(M(M(service.Run("start",D("{}"),"test-chat"))["session"])["id"])==id,"start reuses active run");
   Func<string,int,string,Dictionary<string,object>> eventArgs=(eventId,rev,extra)=>WithPlanning(D("{\"session_id\":\""+id+"\",\"event_id\":\""+eventId+"\",\"expected_revision\":"+rev+",\"source\":\"user\",\"evidence\":\"Observed game state\""+extra+"}"));
   var observe=eventArgs("initial",0,",\"delta\":{\"phase\":\"choice\",\"participantsConfirmed\":true,\"partySize\":1,\"act\":1,\"members\":[{\"identity\":\"game:10000001\",\"available\":true,\"vigor\":2},{\"identity\":\"game:10000002\",\"available\":true}],\"cards\":[{\"id\":\"a\",\"name\":\"Observed card\"}],\"reactions\":[{\"id\":\"swirl\",\"level\":2}],\"choices\":[{\"id\":\"c\",\"kind\":\"card\",\"text\":\"Visible card\"}]}");
   service.Run("observe",observe,"test-chat");service.Run("observe",observe,"test-chat");Reject(()=>service.Run("observe",eventArgs("bad-list",1,",\"delta\":{\"members\":[\"not a member\"]}"),"test-chat"),"malformed member rows");Check(Convert.ToInt32(M(M(service.Status("test-chat")["session"])["state"])["revision"])==1,"idempotent event replay");
   Reject(()=>service.Run("observe",eventArgs("stale",0,",\"delta\":{\"flowers\":0}"),"test-chat"),"stale revision");
   Reject(()=>service.Run("observe",observe,"other-chat"),"foreign chat");
   var invalid=eventArgs("initial",1,",\"delta\":{\"flowers\":99}");Reject(()=>service.Run("observe",invalid,"test-chat"),"event id collision");
   Reject(()=>service.Run("recommend",eventArgs("bad-team",1,",\"reason\":\"r\",\"futurePlan\":\"f\",\"alternatives\":\"a\",\"team\":[\"game:10000002\"]"),"test-chat"),"unknown vigor");
   service.Run("recommend",eventArgs("plan",1,",\"reason\":\"Current fight\",\"futurePlan\":\"Reserve support\",\"alternatives\":\"Recruit next\",\"team\":[\"game:10000001\"],\"choice_id\":\"c\""),"test-chat");
   service.Run("select",eventArgs("selection",2,",\"choice_id\":\"c\""),"test-chat");var state=M(M(service.Status("test-chat")["session"])["state"]);Check(!state.ContainsKey("recommendation")&&!CodexChat.Items(state["choices"]).Any(),"observed selection invalidates old recommendation/choices");Check(M(state["cards"]).Count==1&&Convert.ToInt32(M(M(state["members"])["game:10000001"])["vigor"])==2,"selection does not invent effects/vigor consumption");
   service.Run("observe",eventArgs("combat",3,",\"delta\":{\"phase\":\"combat\",\"flowers\":0,\"members\":[{\"identity\":\"game:10000001\",\"vigor\":1}]}"),"test-chat");state=M(M(service.Status("test-chat")["session"])["state"]);Check(M(state["cards"]).Count==1&&M(state["members"]).Count==2&&Convert.ToInt32(M(M(state["reactions"])["swirl"])["level"])==2,"incremental changes preserve untouched data");
   Reject(()=>service.Run("recommend",eventArgs("combat-plan",4,",\"reason\":\"r\",\"futurePlan\":\"f\",\"alternatives\":\"a\""),"test-chat"),"no coach input while combat");
   Reject(()=>service.Run("observe",eventArgs("negative",4,",\"delta\":{\"members\":[{\"identity\":\"x\",\"vigor\":-1}]}"),"test-chat"),"negative vigor");
   Reject(()=>service.Run("finish",eventArgs("early",4,""),"test-chat"),"completion before result");service.Run("observe",eventArgs("result",4,",\"delta\":{\"phase\":\"result\"}"),"test-chat");service.Run("observe",eventArgs("expire",5,",\"delta\":{\"cards\":[{\"id\":\"a\",\"removed\":true}]}"),"test-chat");Check(M(M(M(service.Status("test-chat")["session"])["state"])["cards"]).Count==0,"expired card removal is explicit");service.Run("finish",eventArgs("complete",6,""),"test-chat");Check(Convert.ToString(M(M(new TheaterService(root).Status("test-chat")["session"])["state"])["phase"])=="completed","journal restores completion after restart");
   Reject(()=>service.Run("observe",eventArgs("after",7,",\"delta\":{\"flowers\":1}"),"test-chat"),"closed run mutation");
   Check(Convert.ToString(M(M(service.Run("start",D("{}"),"test-chat"))["session"])["id"])!=id,"new run preserves old journal");
   AppPreferences.Set("captureFolder",Path.Combine(root,"shots"),root);SavedCapture shot;using(var frame=new GameCaptureFrame{Bitmap=new System.Drawing.Bitmap(4,4)})shot=new CaptureStore(root).Save(frame);
   var captureRun=M(M(service.Run("start",D("{}"),"capture-chat"))["session"]);string captureRunId=Convert.ToString(captureRun["id"]);var image=M(service.Run("capture",D("{\"session_id\":\""+captureRunId+"\",\"path\":\""+shot.Path.Replace("\\","\\\\")+"\"}"),"capture-chat"));
   var resultEvent=new Dictionary<string,object>{{"session_id",captureRunId},{"event_id","result"},{"expected_revision",0},{"source","capture"},{"capture_id",image["capture_id"]},{"evidence","Observed final result"},{"delta",D("{\"phase\":\"result\"}")}};
   service.Run("observe",resultEvent,"capture-chat");resultEvent.Remove("delta");resultEvent["event_id"]="finish";resultEvent["expected_revision"]=1;service.Run("finish",resultEvent,"capture-chat");Check(Convert.ToString(M(M(service.Status("capture-chat")["session"])["state"])["phase"])=="completed","confirmed final capture can complete after its observation");
   string actualThread=Guid.NewGuid().ToString();var latest=M(M(service.Run("start",D("{}"),actualThread))["session"]);
   var resume=service.ResumeTarget("preview");Check(Convert.ToString(resume["id"])==Convert.ToString(latest["id"])&&resume.ContainsKey("events"),"resume always returns the canonical run journal");
   var guarded=M(M(service.Run("start",D("{}"),"identity-test"))["session"]);string guardedId=Convert.ToString(guarded["id"]);
   Func<string,int,string,Dictionary<string,object>> guardedArgs=(eventId,rev,extra)=>WithPlanning(D("{\"session_id\":\""+guardedId+"\",\"event_id\":\""+eventId+"\",\"expected_revision\":"+rev+",\"source\":\"user\",\"evidence\":\"Observed state\""+extra+"}"));
   service.Run("observe",guardedArgs("cast",0,",\"delta\":{\"phase\":\"choice\",\"totalActs\":10,\"completedActs\":4,\"partySize\":1,\"participantsConfirmed\":true,\"members\":[{\"identity\":\"Fischl\",\"name\":\"피슬\",\"available\":true,\"vigor\":1}],\"choices\":[{\"id\":\"unknown\",\"kind\":\"character\",\"text\":\"Unreadable candidate\"}]}"),"identity-test");
   Reject(()=>service.Run("recommend",guardedArgs("guessed",1,",\"choice_id\":\"unknown\",\"reason\":\"r\",\"futurePlan\":\"f\",\"alternatives\":\"a\""),"identity-test"),"unresolved candidate must not be recommended");
   Reject(()=>service.Run("observe",guardedArgs("overflow",1,",\"delta\":{\"completedActs\":11}"),"identity-test"),"completed acts exceed confirmed difficulty");
   service.Run("observe",guardedArgs("name-confirmation",1,",\"delta\":{\"choices\":[{\"id\":\"unknown\",\"kind\":\"character\",\"text\":\"피슬\",\"identity\":\"Fischl\"}]}"),"identity-test");
   service.Run("recommend",guardedArgs("matched",2,",\"choice_id\":\"unknown\",\"reason\":\"r\",\"futurePlan\":\"f\",\"alternatives\":\"a\",\"team\":[\"Fischl\"],\"rotation\":[{\"identity\":\"Fischl\",\"action\":\"Observed skill order\"}]"),"identity-test");
   var identified=M(M(service.Status("identity-test")["session"])["state"]);Check(TheaterService.IdentifiedChoice(identified,D("{\"identity\":\"Fischl\",\"text\":\"피슬\"}"))&&!TheaterService.IdentifiedChoice(identified,D("{\"identity\":\"Fischl\",\"text\":\"다른 이름\"}")),"a roster identity without the same visible name is not confirmed");
   Reject(()=>service.Run("recommend",guardedArgs("foreign-skill",3,",\"reason\":\"r\",\"futurePlan\":\"f\",\"alternatives\":\"a\",\"team\":[\"Fischl\"],\"rotation\":[{\"identity\":\"Foreign\",\"action\":\"Skill\"}]"),"identity-test"),"skill rotation cannot use a foreign member");
   service.Run("observe",guardedArgs("cost",3,",\"delta\":{\"flowers\":30,\"choices\":[{\"id\":\"costly\",\"kind\":\"card\",\"cost\":40,\"text\":\"Costly card\"}]}"),"identity-test");Reject(()=>service.Run("recommend",guardedArgs("over-budget",4,",\"choice_id\":\"costly\",\"reason\":\"r\",\"futurePlan\":\"f\",\"alternatives\":\"a\""),"identity-test"),"unaffordable card must not be recommended");
   Check(!M(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(service.Status("identity-test")["task"]))).ContainsKey("Result"),"tool response must not duplicate the canonical run state");
   var control=new Dictionary<string,object>{{"target","stop"},{"task_id","theater:"+guardedId}};Reject(()=>new CatheryneTools(root).Run("catheryne_control",control,"foreign-chat","stop-foreign"),"theater stop cannot cross chats");
   new CatheryneTools(root).Run("catheryne_control",control,"identity-test","stop-coach");Check(Convert.ToString(M(M(service.Status("identity-test")["session"])["state"])["phase"])=="ended"&&new AiTaskStore(root).Find("theater:"+guardedId).State=="cancelled","stop closes the journal and its derived card without claiming completion");
   Check(PanelNavigation.Group("Theater")=="플레이","play navigation");Check(CatheryneTools.Definitions().Select(x=>M(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(x)))).Any(x=>Convert.ToString(x["name"])=="catheryne_theater"),"canonical tool schema");
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
