using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

internal static class TheaterOptimizationTests {
 static Dictionary<string,object> M(object value){return CodexChat.Map(value);}
 static Dictionary<string,object> D(string text){return CatheryneTools.Json().Deserialize<Dictionary<string,object>>(text);}
 static Dictionary<string,object> Copy(object value){return D(CatheryneTools.Json().Serialize(value));}
 static void Check(bool value,string message){if(!value)throw new Exception("Theater optimization: "+message);}
 static void Reject(Action action,string message){try{action();}catch(ArgumentException){return;}catch(InvalidOperationException){return;}throw new Exception("Theater optimization accepted: "+message);}
 internal static void Run(string root){
  var state=D("{\"participantsConfirmed\":true,\"partySize\":1,\"totalActs\":3,\"completedActs\":0,\"act\":1,\"planChanges\":[\"members:Fischl\"],\"members\":{\"Fischl\":{\"available\":true,\"vigor\":2},\"Nahida\":{\"available\":true,\"vigor\":2}}}");
  var rows=TheaterPlanningTests.Remaining(1,3,"Fischl");M(rows[1])["team"]=new[]{"Nahida"};var old=D("{\"team\":[\"Fischl\"]}");old["assessment"]=TheaterPlanningTests.Assessment();old["battlePlan"]=rows;state["lastPlan"]=old;
  var replacement=Copy(rows[2]);replacement["team"]=new[]{"Nahida"};replacement["evidence"]="Observed roster change; test evidence only";
  var patch=new Dictionary<string,object>{{"battleChanges",new[]{replacement}},{"reviewedBattles",new[]{"act:1"}}};
  var resolved=new Dictionary<string,object>{{"battlePlan",TheaterPlanning.Resolve(state,patch)}};TheaterPlanning.Validate(state,resolved,true);
  var merged=CodexChat.Items(resolved["battlePlan"]).ToArray();Check(merged.Length==3&&CatheryneTools.Json().Serialize(merged[1])==CatheryneTools.Json().Serialize(rows[1])&&TheaterPlanning.Team(merged[2]["team"])[0]=="Nahida","only changed fights replaced, all other evidence retained");
  Reject(()=>TheaterPlanning.Resolve(state,D("{}")),"affected plan requires explicit review");
  Reject(()=>TheaterPlanning.Resolve(state,new Dictionary<string,object>{{"reviewedBattles",new[]{"act:1"}}}),"all affected IDs must be reviewed");
  Reject(()=>TheaterPlanning.Resolve(state,new Dictionary<string,object>{{"battlePlan",rows},{"battleChanges",new[]{replacement}}}),"ambiguous full and partial plan");
  Reject(()=>TheaterPlanning.Resolve(state,new Dictionary<string,object>{{"reviewedBattles",new[]{"act:1","act:1"}}}),"duplicate review");
  replacement["id"]="unknown";Reject(()=>TheaterPlanning.Resolve(state,patch),"partial update cannot invent another fight");replacement["id"]="act:3";
  replacement["team"]=new[]{"Fischl"};M(M(state["members"])["Fischl"])["vigor"]=1;resolved["battlePlan"]=TheaterPlanning.Resolve(state,patch);Reject(()=>TheaterPlanning.Validate(state,resolved,true),"partial update still checks the whole vigor budget");
  M(M(state["members"])["Fischl"])["vigor"]=2;var reviewed=TheaterPlanning.Resolve(state,new Dictionary<string,object>{{"reviewedBattles",new[]{"act:1","act:3"}}});Check(CatheryneTools.Json().Serialize(reviewed)==CatheryneTools.Json().Serialize(rows),"review without edits preserves the exact canonical rows");
  var bonus=D("{\"id\":\"extra:1\",\"title\":\"Fixture bonus\",\"team\":[],\"status\":\"unverified\",\"requirements\":\"Unknown bonus mechanics\",\"evidence\":\"Not observed\",\"fallback\":\"Read next capture\"}");old["battlePlan"]=rows.Concat(new object[]{bonus}).ToArray();state["requiredChallenges"]=new[]{"extra:1"};state["planChanges"]=new[]{"recruited:Nahida"};
  Reject(()=>TheaterPlanning.Resolve(state,new Dictionary<string,object>{{"reviewedBattles",new[]{"act:1","act:2","act:3"}}}),"required bonus also needs review after recruitment");
  resolved["battlePlan"]=TheaterPlanning.Resolve(state,new Dictionary<string,object>{{"reviewedBattles",new[]{"act:1","act:2","act:3","extra:1"}}});TheaterPlanning.Validate(state,resolved,true);Check(CodexChat.Items(resolved["battlePlan"]).Last()["status"].ToString()=="unverified","unknown bonus preserved rather than fabricated");
  state["completedChallenges"]=new[]{"extra:1"};Check(TheaterPlanning.Details(state).Length==3,"completed bonus excluded from reusable plan");old["battlePlan"]=rows;state.Remove("requiredChallenges");state.Remove("completedChallenges");state["planChanges"]=new[]{"members:Fischl"};
  state["completedActs"]=1;state["act"]=2;Reject(()=>TheaterPlanning.Resolve(state,new Dictionary<string,object>{{"reviewedBattles",new[]{"act:1","act:3"}}}),"completed fights cannot be patched or reviewed");
  state.Remove("lastPlan");Reject(()=>TheaterPlanning.Resolve(state,patch),"initial plan must be complete");

  var service=new TheaterService(root,()=>"Fixture refresh");const string thread="optimization-test";var started=M(service.Run("start",D("{\"goal\":\"Fixture goal\"}"),thread));string run=Convert.ToString(M(started["session"])["id"]);
  service.Run("observe",new Dictionary<string,object>{{"session_id",run},{"expected_revision",0},{"event_id","setup"},{"source","user"},{"evidence","Synthetic complete roster"},{"delta",D("{\"phase\":\"party\",\"participantsConfirmed\":true,\"partySize\":1,\"totalActs\":2,\"completedActs\":0,\"act\":1,\"members\":[{\"identity\":\"Fischl\",\"available\":true,\"vigor\":2},{\"identity\":\"Nahida\",\"available\":false,\"vigor\":2}]}" )}},thread);
  var request=D("{\"event_id\":\"first\",\"expected_revision\":1,\"team\":[\"Fischl\"],\"reason\":\"Test fight\",\"futurePlan\":\"Test resource plan\",\"alternatives\":\"Test fallback\"}");request["session_id"]=run;request["assessment"]=TheaterPlanningTests.Assessment();request["battlePlan"]=TheaterPlanningTests.Remaining(1,2,"Fischl");
  var brief=M(service.Run("recommend",request,thread));var compact=M(M(brief["session"])["state"]);Check(!compact.ContainsKey("lastPlan")&&!M(compact["recommendation"]).ContainsKey("battlePlan")&&CodexChat.Items(compact["planningBoard"]).Count()==2,"decision response excludes duplicated detailed plans");
  request["view"]="full";var full=M(service.Run("recommend",request,thread));Check(M(M(full["session"])["state"]).ContainsKey("lastPlan")&&Convert.ToInt32(M(M(full["session"])["state"])["revision"])==2,"response view does not change event identity or commit twice");
  var query=new Dictionary<string,object>{{"session_id",run},{"battle_ids",new[]{"act:2"}}};var details=Copy(service.Run("plan",query,thread));Check(CodexChat.Items(details["battles"]).Count()==1&&CodexChat.Items(details["battles"]).First().ContainsKey("fallback")&&Convert.ToInt32(details["revision"])==2,"targeted details preserve evidence without journal mutation");
  Reject(()=>service.Run("plan",query,"another-chat"),"plan details require run ownership");query["battle_ids"]=new[]{"unknown"};Reject(()=>service.Run("plan",query,thread),"unknown detail query");
  service.Run("observe",new Dictionary<string,object>{{"session_id",run},{"expected_revision",2},{"event_id","recruit"},{"source","user"},{"evidence","Test confirmed recruitment"},{"delta",D("{\"members\":[{\"identity\":\"Nahida\",\"available\":true}]}" )}},thread);
  var follow=Copy(request);follow.Remove("battlePlan");follow.Remove("view");follow["event_id"]="partial";follow["expected_revision"]=3;var changed=Copy(TheaterPlanningTests.Remaining(2,2,"Nahida")[0]);follow["battleChanges"]=new[]{changed};follow["reviewedBattles"]=new[]{"act:1"};service.Run("recommend",follow,thread);
  var saved=M(M(service.Status(thread)["session"])["state"]);Check(Convert.ToInt32(saved["revision"])==4&&TheaterPlanning.Team(CodexChat.Items(M(saved["lastPlan"])["battlePlan"]).Last()["team"])[0]=="Nahida","service saves one merged full plan after a partial edit");
  follow["event_id"]="invalid-view";follow["expected_revision"]=4;follow["view"]="invalid";Reject(()=>service.Run("recommend",follow,thread),"invalid presentation rejected before mutation");Check(Convert.ToInt32(M(M(service.Status(thread)["session"])["state"])["revision"])==4,"invalid view leaves journal untouched");

  var fixture=TheaterTaskTests.Fixture("choice");full["session"]=new Dictionary<string,object>{{"id",fixture["id"]},{"goal",fixture["goal"]},{"state",TheaterService.Project(fixture)}};var snapshot=CatheryneTools.Json().Serialize(full);var decision=TheaterService.DecisionStatus(full);var decisionState=M(M(decision["session"])["state"]);var original=M(M(full["session"])["state"]);
  foreach(string key in new[]{"revision","members","cards","reactions","choices","planningAudit","totalActs","completedActs"})Check(CatheryneTools.Json().Serialize(decisionState[key])==CatheryneTools.Json().Serialize(original[key]),"compact view retains "+key);
  Check(snapshot==CatheryneTools.Json().Serialize(full),"projection never modifies the full state");
  int before=Encoding.UTF8.GetByteCount(snapshot),after=Encoding.UTF8.GetByteCount(CatheryneTools.Json().Serialize(decision));Check(after<before,"compact response is smaller for a six-fight fixture");
  File.WriteAllText(Path.Combine(Path.GetTempPath(),"catheryne-theater-context-measurement.json"),CatheryneTools.Json().Serialize(new{fixture="four members, three choices, six remaining fights",fullBytes=before,decisionBytes=after,reductionPercent=Math.Round(100.0*(before-after)/before,1)}));
 }
}
