using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class ChallengeSeasonTests {
 static void Check(bool value,string message){if(!value)throw new Exception("Challenge seasons: "+message);}
 static Dictionary<string,object> Map(object value){return CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(value)));}
 static long Unix(DateTime time){return (long)(time-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;}
 internal static void Run(){
  DateTime begins,ends;var wall=Map(new{start="2026-09-16T04:00:00+00:00",end="2026-10-16T13:00:00+09:00",endEstimated=true});Check(EndgameKnowledge.SeasonWindow(wall,"os_asia",out begins,out ends)&&begins==new DateTime(2026,9,15,20,0,0,DateTimeKind.Utc)&&ends==new DateTime(2026,10,15,20,0,0,DateTimeKind.Utc),"server wall-clock parsing is independent of this computer timezone and old estimated-end encoding");
  var now=DateTime.UtcNow;long start=Unix(now.AddDays(-2)),end=Unix(now.AddDays(2));
  var calendar=Map(new{type="ActTypeTower",name="Abyss",start_timestamp=start,end_timestamp=end,is_finished=true});var entry=ChallengeSeasons.Calendar(calendar,now);
  Check(entry.Done&&entry.Start.Value.Kind==DateTimeKind.Utc&&entry.Due==new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(end),"official timestamps and completion retained without local-time guessing");
  Check(ChallengeSeasons.Active(new[]{entry},"abyss",now)!=null&&ChallengeSeasons.Active(new[]{entry},"theater",now)==null,"mode identity is exact");
  Check(ChallengeSeasons.Active(new[]{entry},"abyss",entry.Due)==null,"ended season is not current");entry.Observed=now.AddSeconds(1);Check(ChallengeSeasons.Active(new[]{entry},"abyss",now)==null,"future observation rejected");entry.Observed=now.AddDays(-3);Check(ChallengeSeasons.Active(new[]{entry},"abyss",now)==null,"observation before this season cannot claim its completion");
  var hard=Map(new{schedule=new{schedule_id=123,start_time=start,end_time=end},single=new{has_data=true,best=new{difficulty=5,second=120},challenge=new[]{new{second=40},new{second=40},new{second=40}}},mp=new{has_data=true}});
  var old=Map(new{schedule=new{schedule_id=122,start_time=Unix(now.AddDays(-50)),end_time=Unix(now.AddDays(-20))}});var seasons=ChallengeSeasons.HardChallenges(Map(new{data=new[]{old,hard}}),now);Check(ChallengeSeasons.Active(seasons,"stygian",now).Start.Value==new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(start),"current season selected from multi-season official response");
  var clear=ChallengeSeasons.HardChallenge(hard,now);Check(clear.Done&&clear.CompletionText.Contains(PartyRoles.Difficulty(5)),"three official solo fields show exact cleared difficulty");
  var single=CodexChat.Map(hard["single"]);single["challenge"]=new[]{new{second=40},new{second=40}};Check(!ChallengeSeasons.HardChallenge(hard,now).Done,"partial run not complete");single["has_data"]=false;Check(!ChallengeSeasons.HardChallenge(hard,now).Done,"multiplayer or missing solo data cannot imply solo clear");
  var missing=Map(new{schedule=new{start_time=0,end_time=end}});Check(ChallengeSeasons.HardChallenge(missing,now)==null,"missing start is not an invented season");
  string root=Path.Combine(Path.GetTempPath(),"catheryne-challenge-seasons-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   AppPreferences.Set("resinUid","123456789",root);AppPreferences.Set("resinServer","os_asia",root);
   using(var db=new LocalDataService(root))db.Observe("123456789",ChallengeSeasons.CalendarKind,new{fixed_act_list=new[]{calendar}});
   var store=new ChallengeSeasons(root);var current=store.Current("abyss",Map(new{start="2001-01-01",end="2001-02-01"}));Check(current!=null&&current.Done&&current.Source=="hoyolab","official active schedule overrides unrelated statistics period");
   var calendarEntry=new CalendarStore(root).Read().Single(e=>e.Id.StartsWith("fixed_act_list:ActTypeTower:"));Check(calendarEntry.Done==current.Done&&calendarEntry.Due==current.Due,"calendar and header share one projection");
   AppPreferences.Set("resinUid","987654321",root);Check(store.Current("abyss")==null,"account switch never reuses previous completion");
  }finally{Directory.Delete(root,true);}
 }
}
