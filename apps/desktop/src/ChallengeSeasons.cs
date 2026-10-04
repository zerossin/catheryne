using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

// The calendar and challenge headers project the same account-scoped official observations.
internal sealed class ChallengeSeasons {
 readonly string root;
 internal ChallengeSeasons(string root){this.root=root;}
 internal static string CalendarKind {get{return "calendar:"+Locale.LanguageCode;}}
 static string RecordKind(string server){return "challenge:hard_challenge:"+server+":"+Locale.LanguageCode;}
 static string S(Dictionary<string,object> d,string key){return CodexChat.S(d,key);}
 static double N(Dictionary<string,object> d,string key){double value;return double.TryParse(S(d,key),NumberStyles.Float,CultureInfo.InvariantCulture,out value)?value:0;}
 static DateTime? Epoch(double seconds){return seconds>0&&seconds<253402300799?(DateTime?)new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds):null;}
 internal static string Type(string mode){return mode=="theater"?"ActTypeRoleCombat":mode=="abyss"?"ActTypeTower":"ActTypeHardChallenge";}
 internal static CalendarEntry Calendar(Dictionary<string,object> item,DateTime observed){
  DateTime? start=Epoch(N(item,"start_timestamp")),end=Epoch(N(item,"end_timestamp"));if(!start.HasValue||!end.HasValue||end<=start)return null;
  return new CalendarEntry{Id="fixed_act_list:"+S(item,"type")+":"+S(item,"start_timestamp"),Title=S(item,"name"),Start=start,Due=end.Value,Done=Equals(item.ContainsKey("is_finished")?item["is_finished"]:null,true),Source="hoyolab",Observed=observed};
 }
 internal static CalendarEntry HardChallenge(Dictionary<string,object> data,DateTime observed){
  object raw;var schedule=CodexChat.Map(data.TryGetValue("schedule",out raw)?raw:null);DateTime? start=Epoch(N(schedule,"start_time")),end=Epoch(N(schedule,"end_time"));if(!start.HasValue||!end.HasValue||end<=start||N(schedule,"schedule_id")<=0||Equals(schedule.ContainsKey("is_valid")?schedule["is_valid"]:null,false))return null;
  var entry=new CalendarEntry{Id="fixed_act_list:ActTypeHardChallenge:"+S(schedule,"schedule_id"),Title=Locale.T("지맥 제압전"),Start=start,Due=end.Value,Source="hoyolab",Observed=observed};
  var single=CodexChat.Map(data.TryGetValue("single",out raw)?raw:null);var best=CodexChat.Map(single.TryGetValue("best",out raw)?raw:null);double rank=N(best,"difficulty");int difficulty=(int)rank;
  // A best time alone does not establish three completed battlefields. Multiplayer is separate.
  if(Equals(single.ContainsKey("has_data")?single["has_data"]:null,true)&&rank==difficulty&&difficulty>=1&&difficulty<=6&&N(best,"second")>0&&single.TryGetValue("challenge",out raw)){
   var fields=CodexChat.Items(raw).ToArray();if(fields.Length==3&&fields.All(f=>N(f,"second")>0)) {entry.Done=true;entry.CompletionText=Locale.Format("{0} 클리어",PartyRoles.Difficulty(difficulty));}
  }
  return entry;
 }
 internal static CalendarEntry[] HardChallenges(Dictionary<string,object> response,DateTime observed){object raw;return response.TryGetValue("data",out raw)?CodexChat.Items(raw).Select(item=>HardChallenge(item,observed)).Where(entry=>entry!=null).ToArray():new CalendarEntry[0];}
 internal static bool StatisticsCurrent(Dictionary<string,object> statistics,CalendarEntry schedule,string server){
  DateTime start,end;if(!EndgameKnowledge.SeasonWindow(statistics,server,out start,out end))return false;
  if(schedule!=null&&schedule.Source=="hoyolab")return schedule.Start.HasValue&&schedule.Start.Value.AddHours(EndgameKnowledge.ServerOffset(server)).Date==start.AddHours(EndgameKnowledge.ServerOffset(server)).Date;
  return EndgameKnowledge.Current(statistics,DateTime.UtcNow,server);
 }
 internal List<CalendarEntry> Read(){
  var prefs=AppPreferences.Read(root);string uid=S(prefs,"resinUid"),server=S(prefs,"resinServer");var entries=new List<CalendarEntry>();
  using(var db=new LocalDataService(root)){
   var rows=db.Recent(uid,CalendarKind,1);if(rows.Count>0){var data=CodexChat.Map(CatheryneTools.Json().DeserializeObject(rows[0]["payload"]));object raw;DateTime observed=DateTime.Parse(rows[0]["observed_at"]).ToUniversalTime();if(data.TryGetValue("fixed_act_list",out raw))foreach(var item in CodexChat.Items(raw)){var entry=Calendar(item,observed);if(entry!=null)entries.Add(entry);}}
   rows=db.Recent(uid,RecordKind(server),1);if(rows.Count>0)foreach(var entry in HardChallenges(CodexChat.Map(CatheryneTools.Json().DeserializeObject(rows[0]["payload"])),DateTime.Parse(rows[0]["observed_at"]).ToUniversalTime()))if(!entries.Any(e=>e.Id.StartsWith("fixed_act_list:ActTypeHardChallenge:")&&e.Start==entry.Start&&Math.Abs((e.Due-entry.Due).TotalSeconds)<=1))entries.Add(entry);
  }
  return entries;
 }
 internal static CalendarEntry Active(IEnumerable<CalendarEntry> entries,string mode,DateTime now){return entries.Where(e=>e.Id.StartsWith("fixed_act_list:"+Type(mode)+":")&&e.Start<=now&&e.Due>now&&e.Observed<=now&&e.Observed>=e.Start).OrderByDescending(e=>e.Observed).FirstOrDefault();}
 internal CalendarEntry Current(string mode,Dictionary<string,object> statistics=null){
  DateTime now=DateTime.UtcNow;var official=Active(Read(),mode,now);if(official!=null)return official;
  string server=S(AppPreferences.Read(root),"resinServer");DateTime start,end;
  if(mode=="theater"){start=TheaterKnowledge.PeriodStart(now,server);end=TheaterKnowledge.PeriodEnd(now,server);}
  else {if(statistics==null||!EndgameKnowledge.SeasonWindow(statistics,server,out start,out end)||start>now||end<=now)return null;}
  return new CalendarEntry{Title=Locale.T(mode=="theater"?"환상극":mode=="abyss"?"나선비경":"지맥 제압전"),Start=start,Due=end,Source="season-estimate",Observed=now};
 }
 internal void Refresh(bool force=false){
  var prefs=AppPreferences.Read(root);string uid=S(prefs,"resinUid"),server=S(prefs,"resinServer"),cookie;using(var db=new LocalDataService(root))cookie=db.GetSecret("hoyolab");if(string.IsNullOrWhiteSpace(cookie)||uid.Length==0)return;try{Refresh(cookie,uid,server,force);}catch{/* Optional season lookup must not block public preparation. Failures are logged by the shared refresh gate. */}
 }
 internal void Refresh(string cookie,string uid,string server,bool force){
  // Gates coalesce concurrent tab/calendar requests; all network work runs on a worker.
  var tasks=new[]{Task.Run(()=>ObservationRefresh.Run(root,uid,CalendarKind,TimeSpan.FromMinutes(5),force,()=>{var data=HoyoRecord.Request(cookie,uid,server,"act_calendar");using(var db=new LocalDataService(root))db.Observe(uid,CalendarKind,data);})),Task.Run(()=>{try{ObservationRefresh.Run(root,uid,RecordKind(server),TimeSpan.FromMinutes(5),force,()=>{var data=HoyoRecord.Request(cookie,uid,server,"hard_challenge");if(HardChallenges(data,DateTime.UtcNow).Length==0)throw new InvalidOperationException("HoYoLAB challenge schedule is unavailable");using(var db=new LocalDataService(root))db.Observe(uid,RecordKind(server),data);});}catch{/* Optional private/unavailable records never become a fabricated completion. ObservationRefresh logs the failure. */}})};
  Task.WhenAll(tasks).GetAwaiter().GetResult();
 }
}
