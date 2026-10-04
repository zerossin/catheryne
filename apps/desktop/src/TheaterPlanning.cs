using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Planning claims are separate from observations. This validates evidence and
// resource feasibility; it cannot prove damage or a player's eventual clear.
internal static class TheaterPlanning {
 internal const int Version=1;
 internal const string Principles="Priority order: (1) satisfy this fight's mandatory mechanics, elemental shields, immunities and survival; (2) meet the user's clear/star objective with a workable damage/energy/rotation plan; (3) allocate remaining vigor across ALL remaining required base and requested bonus battles; (4) improve cards and resource efficiency. Never sacrifice a present mechanic for an unverified future reserve. Four available names are not a viable team. Check elemental application rate/AoE/uptime, reaction enablers and damage ownership, field time, energy and healing conditions, actual constellation/gear and player familiarity. Normal/Lunar/Stellar variants and their buffs are distinct; read current sources rather than assuming a same-element buff applies. A healer whose healing needs normals/stance/burst must have that activation in rotation. Owned, registered, recruited, recommended and actually deployed are different. Plan every remaining required fight with requirements, team, support evidence, fallback and explicit status=supported|recruitment|unverified|blocked. supported means source-backed feasibility, NOT guaranteed clear. Unknown future enemies stay unknown. Do not count random recruitment, recovery or rerolls as guaranteed resources. Recompute downstream fights when a reserve is spent, a card changes, a recruitment fails or a real fight underperforms. Explain a changed recommendation with the changed evidence; do not reverse merely because the user questions it. Before new research, reuse pinned sources and the existing plan; update only affected assumptions. Recommend first, short reason next; use the structured box for party/rotation/remaining plans. A user reporting difficulty is evidence to reassess coverage/damage/execution, not to repeat the same plan confidently.";
 internal static readonly string[] CheckKinds={"mechanics","survival","damage","execution","reactions"};
 static string S(Dictionary<string,object> d,string key){return CodexChat.S(d,key);}
 static Dictionary<string,object> M(object d){return CodexChat.Map(d);}
 static void Text(Dictionary<string,object> row,string key,bool required=true){string text=S(row,key);if((required&&string.IsNullOrWhiteSpace(text))||text.Length>3000)throw new ArgumentException("계획의 "+key+" 근거를 확인해 주세요.");}
 static Dictionary<string,object>[] Rows(object raw,int max){var list=raw as IEnumerable;var rows=CodexChat.Items(raw).ToArray();if(list==null||raw is string||raw is IDictionary||rows.Length>max||list.Cast<object>().Count()!=rows.Length)throw new ArgumentException("Invalid planning rows");return rows;}
 internal static string[] Team(object raw){return TeamList(raw,8);}
 internal static Dictionary<string,object>[] Remaining(Dictionary<string,object> state,Dictionary<string,object> plan){
  var battles=plan.ContainsKey("battlePlan")?Rows(plan["battlePlan"],40):new Dictionary<string,object>[0];int done=state.ContainsKey("completedActs")?Convert.ToInt32(state["completedActs"]):0;var completed=state.ContainsKey("completedChallenges")?((IEnumerable)state["completedChallenges"]).Cast<object>().Select(Convert.ToString).ToArray():new string[0];
  return battles.Where(b=>!completed.Contains(S(b,"id"))&&(!b.ContainsKey("act")||Convert.ToInt32(b["act"])>done)).ToArray();
 }
 internal static string[] BattleIds(object raw){var ids=TeamList(raw,40);if(ids.Any(id=>id.Length>3000))throw new ArgumentException("Invalid battle ID");return ids;}
 static string[] TeamList(object raw,int max){var list=raw as IEnumerable;if(list==null||raw is string||raw is IDictionary)throw new ArgumentException("Invalid identity list");var values=list.Cast<object>().ToArray();if(values.Length>max||values.Any(x=>!(x is string)||string.IsNullOrWhiteSpace((string)x)))throw new ArgumentException("Invalid identity list");var ids=values.Cast<string>().ToArray();if(ids.Distinct().Count()!=ids.Length)throw new ArgumentException("Duplicate identity");return ids;}
 internal static Dictionary<string,object>[] Details(Dictionary<string,object> state,string[] ids=null){
  var plan=state.ContainsKey("lastPlan")?M(state["lastPlan"]):new Dictionary<string,object>();var rows=Remaining(state,plan);if(ids==null)return rows;if(ids.Any(id=>!rows.Any(row=>S(row,"id")==id)))throw new ArgumentException("남은 계획에 없는 전투입니다.");return rows.Where(row=>ids.Contains(S(row,"id"))).ToArray();
 }
 internal static object[] Board(Dictionary<string,object> state){return Details(state).Select(row=>(object)row.Where(p=>new[]{"id","act","title","team","status","requirements"}.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value)).ToArray();}
 // Patches are merged into the canonical complete plan before the same validator
 // checks every future deployment. Review acknowledgements never bypass checks.
 internal static object Resolve(Dictionary<string,object> state,Dictionary<string,object> input){
  bool full=input.ContainsKey("battlePlan"),patch=input.ContainsKey("battleChanges"),review=input.ContainsKey("reviewedBattles");if(full&&(patch||review))throw new ArgumentException("전체 계획과 부분 변경을 함께 제출할 수 없습니다.");if(full)return input["battlePlan"];
  var old=state.ContainsKey("lastPlan")?M(state["lastPlan"]):new Dictionary<string,object>();if(!old.ContainsKey("battlePlan")){if(patch||review)throw new ArgumentException("먼저 전체 남은 전투 계획을 등록해 주세요.");return null;}
  var rows=Remaining(state,old);var changes=patch?Rows(input["battleChanges"],40):new Dictionary<string,object>[0];var acknowledged=review?BattleIds(input["reviewedBattles"]):new string[0];var known=rows.Select(row=>S(row,"id")).ToArray();var changed=changes.Select(row=>S(row,"id")).ToArray();
  if(changed.Distinct().Count()!=changed.Length||changed.Any(id=>!known.Contains(id))||acknowledged.Any(id=>!known.Contains(id)))throw new ArgumentException("부분 변경과 검토에는 남은 계획의 전투 ID를 사용해 주세요. 전투 범위가 바뀌면 전체 계획을 제출하세요.");
  var audit=M(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(Audit(state))));var affected=BattleIds(audit["affectedBattles"]);if(affected.Except(changed.Concat(acknowledged)).Any())throw new ArgumentException("영향받은 전투를 변경하거나 reviewedBattles로 유지 검토를 확인해 주세요.");
  return rows.Select(row=>(object)(changes.FirstOrDefault(change=>S(change,"id")==S(row,"id"))??row)).ToArray();
 }
 internal static object Audit(Dictionary<string,object> state){
  var plan=state.ContainsKey("lastPlan")?M(state["lastPlan"]):new Dictionary<string,object>();var members=M(state["members"]);var changes=((IEnumerable)state["planChanges"]).Cast<object>().Select(Convert.ToString).ToArray();int done=state.ContainsKey("completedActs")?Convert.ToInt32(state["completedActs"]):0;
  var completed=state.ContainsKey("completedChallenges")?((IEnumerable)state["completedChallenges"]).Cast<object>().Select(Convert.ToString).ToArray():new string[0];var required=state.ContainsKey("requiredChallenges")?((IEnumerable)state["requiredChallenges"]).Cast<object>().Select(Convert.ToString).Except(completed).ToArray():new string[0];
  var remaining=Remaining(state,plan);var affected=new List<string>();var conflicts=new List<string>();var use=new Dictionary<string,int>();
  foreach(var battle in remaining){var team=battle.ContainsKey("team")?Team(battle["team"]):new string[0];bool dependent=changes.Any(c=>c.StartsWith("members:")?team.Contains(c.Substring(8)):true);if(dependent)affected.Add(S(battle,"id"));foreach(string id in team){use[id]=use.ContainsKey(id)?use[id]+1:1;if(!members.ContainsKey(id)||S(battle,"status")=="supported"&&(!M(members[id]).ContainsKey("available")||!Equals(M(members[id])["available"],true)))conflicts.Add(S(battle,"id")+":availability:"+id);}}
  foreach(var pair in use){var member=members.ContainsKey(pair.Key)?M(members[pair.Key]):new Dictionary<string,object>();if(member.ContainsKey("vigor")&&member["vigor"]!=null&&pair.Value>Convert.ToInt32(member["vigor"]))conflicts.Add("vigor:"+pair.Key);}
  int known=members.Values.Select(M).Where(m=>Equals(m.ContainsKey("available")?m["available"]:false,true)&&m.ContainsKey("vigor")&&m["vigor"]!=null).Sum(m=>Convert.ToInt32(m["vigor"]));object slots=state.ContainsKey("totalActs")&&state.ContainsKey("partySize")?(object)((Convert.ToInt32(state["totalActs"])-done+required.Length)*Convert.ToInt32(state["partySize"])):null;
  return new{needsReview=changes.Length>0||conflicts.Count>0,changes=changes,affectedBattles=affected.ToArray(),conflicts=conflicts.Distinct().ToArray(),remainingPlannedBattles=remaining.Length,remainingRequiredSlots=slots,knownAvailableUses=known,missingUses=slots==null?null:(object)Math.Max(0,Convert.ToInt32(slots)-known),meaning="Every remaining candidate rechecked for resource feasibility. Missing uses are a dependency, not certain failure. This is not damage simulation or guaranteed clear."};
 }
 internal static void Validate(Dictionary<string,object> state,Dictionary<string,object> plan,bool required){
  object raw;bool team=plan.ContainsKey("team");var members=M(state["members"]);
  if(team&&required&&(!Equals(state.ContainsKey("participantsConfirmed")?state["participantsConfirmed"]:false,true)||!state.ContainsKey("partySize")))throw new ArgumentException("전체 참가 명단과 출전 인원을 먼저 확인해 주세요.");
  if(team&&required&&!plan.ContainsKey("assessment"))throw new ArgumentException("assessment에 기믹·생존·화력·운용·반응 근거를 각각 기록해 주세요.");
  if(plan.TryGetValue("assessment",out raw)){
   var checks=Rows(raw,5);if(checks.Select(x=>S(x,"kind")).Distinct().Count()!=checks.Length||checks.Any(x=>!CheckKinds.Contains(S(x,"kind"))||x.Keys.Any(k=>!new[]{"kind","status","evidence"}.Contains(k))||!new[]{"passed","unknown","failed"}.Contains(S(x,"status"))))throw new ArgumentException("Invalid team assessment");
   foreach(var check in checks)Text(check,"evidence");
   if(team&&(checks.Length!=5||checks.Any(x=>S(x,"status")=="failed")))throw new ArgumentException("필수 조건이 실패한 파티는 추천할 수 없습니다. 미확인은 unknown으로 명시해 주세요.");
  }
  if(team&&state.ContainsKey("partySize")&&Team(plan["team"]).Length!=Convert.ToInt32(state["partySize"]))throw new ArgumentException("확인된 출전 인원에 맞춰 파티를 구성해 주세요.");
  if(team&&required&&state.ContainsKey("totalActs")&&!plan.ContainsKey("battlePlan"))throw new ArgumentException("battlePlan에 남은 필수 전투별 편성·요구 조건·대체안을 기록해 주세요.");
  if(plan.TryGetValue("battlePlan",out raw)){
   var battles=Rows(raw,40);var ids=new HashSet<string>();var acts=new HashSet<int>();var usage=new Dictionary<string,int>();int done=state.ContainsKey("completedActs")?Convert.ToInt32(state["completedActs"]):0,total=state.ContainsKey("totalActs")?Convert.ToInt32(state["totalActs"]):30;
   foreach(var battle in battles){
    if(battle.Keys.Any(k=>!new[]{"id","act","title","team","requirements","status","evidence","fallback"}.Contains(k)))throw new ArgumentException("Unknown battle plan field");Text(battle,"id");Text(battle,"title");Text(battle,"requirements");Text(battle,"evidence");Text(battle,"fallback");if(!ids.Add(S(battle,"id")))throw new ArgumentException("Duplicate planned battle");
    string status=S(battle,"status");if(!new[]{"supported","recruitment","unverified","blocked"}.Contains(status))throw new ArgumentException("Invalid battle readiness");
    int act=0;if(battle.ContainsKey("act")){act=UnlockerOptions.Number(battle["act"],"act",1,total);if(act<=done||!acts.Add(act))throw new ArgumentException("Plan only remaining base acts once");}
    var planned=battle.ContainsKey("team")?Team(battle["team"]):new string[0];if(status=="supported"&&(planned.Length==0||state.ContainsKey("partySize")&&planned.Length!=Convert.ToInt32(state["partySize"])))throw new ArgumentException("근거 확보 편성은 확인된 출전 인원을 모두 채워야 합니다.");
    foreach(string id in planned){if(!members.ContainsKey(id))throw new ArgumentException("참가 명단 밖 캐릭터는 미래 편성에 넣을 수 없습니다.");var member=M(members[id]);bool available=member.ContainsKey("available")&&Equals(member["available"],true),vigor=member.ContainsKey("vigor")&&member["vigor"]!=null;
     if(status=="supported"&&(!available||!vigor||Convert.ToInt32(member["vigor"])<1))throw new ArgumentException("영입이나 출전 횟수가 미확인인 미래 편성은 근거 확보로 표시할 수 없습니다.");
     usage[id]=usage.ContainsKey(id)?usage[id]+1:1;
    }
    if(team&&S(state,"challenge")==""&&act>0&&state.ContainsKey("act")&&act==Convert.ToInt32(state["act"])&&!planned.SequenceEqual(Team(plan["team"])))throw new ArgumentException("현재 추천과 해당 막의 미래 계획이 다릅니다.");
   }
   if(team&&required&&S(state,"challenge")!=""){
    var current=battles.Where(b=>!b.ContainsKey("act")&&S(b,"title")==S(state,"challenge")).ToArray();if(current.Length!=1||!Team(current[0].ContainsKey("team")?current[0]["team"]:new string[0]).SequenceEqual(Team(plan["team"])))throw new ArgumentException("현재 추가 도전 이름과 추천 파티를 남은 전투 계획에 함께 기록해 주세요.");
   }
   if(required&&state.ContainsKey("totalActs")&&Enumerable.Range(done+1,total-done).Any(n=>!acts.Contains(n)))throw new ArgumentException("남은 기본 막의 누락된 편성은 unverified 또는 blocked로 명시해 주세요.");
   if(required&&state.ContainsKey("requiredChallenges")){var completed=state.ContainsKey("completedChallenges")?((IEnumerable)state["completedChallenges"]).Cast<object>().Select(Convert.ToString).ToArray():new string[0];if(((IEnumerable)state["requiredChallenges"]).Cast<object>().Select(Convert.ToString).Except(completed).Any(id=>!ids.Contains(id)))throw new ArgumentException("목표에 포함된 남은 추가 도전도 계획에 넣어 주세요.");}
   foreach(var pair in usage){var member=M(members[pair.Key]);if(member.ContainsKey("vigor")&&member["vigor"]!=null&&pair.Value>Convert.ToInt32(member["vigor"]))throw new ArgumentException("미래 계획이 "+pair.Key+"의 남은 출전 횟수를 초과합니다. 실제 회복 확인 전에는 추가 출전을 배정할 수 없습니다.");}
  }
  var old=state.ContainsKey("lastPlan")?M(state["lastPlan"]):new Dictionary<string,object>();
  bool changed=old.ContainsKey("team")&&plan.ContainsKey("team")&&!Team(old["team"]).SequenceEqual(Team(plan["team"]));
  if(changed&&required)Text(plan,"changeReason");else if(plan.ContainsKey("changeReason"))Text(plan,"changeReason");
 }
}
