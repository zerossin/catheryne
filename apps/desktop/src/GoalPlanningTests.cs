using System;
using System.IO;
using System.Linq;
internal static class GoalPlanningTests {
 internal static void Run(){
  var now=DateTimeOffset.UtcNow;
  Func<string,int?,GoalPlanInput> step=(id,cost)=>new GoalPlanInput {TaskId=id,GoalId="goal",Resin=cost,InventoryKnown=true,ExecutorAvailable=true};
  var rows=GoalPlanner.Preview(new[]{step("mora",20),step("xp",20),step("boss",40)},60,60,now,DayOfWeek.Monday);
  if(rows.Sum(x=>x.ReservedResin)!=40||rows[2].Status!="waiting_resin")throw new Exception("Shared resin budget failed");
  var later=step("specialty",0);later.NotBefore=now.AddHours(48);
  var weekday=step("talent",20);weekday.ServerDays=new[]{(int)DayOfWeek.Tuesday};
  var unknown=step("unknown",20);unknown.InventoryKnown=false;
  rows=GoalPlanner.Preview(new[]{later,weekday,unknown},60,60,now,DayOfWeek.Monday);
  if(rows[0].Status!="waiting_respawn"||rows[1].Status!="waiting_weekday"||rows[2].Status!="needs_inventory"||rows.Sum(x=>x.ReservedResin)!=0)throw new Exception("Wait/unknown reservation failed");
  if(GoalPlanner.Preview(new[]{step("a",20)},null,60,now,null)[0].Status!="needs_resin")throw new Exception("Unknown resin treated as zero");
  if(GoalPlanner.Preview(new[]{step("free",0)},null,null,now,null)[0].Status!="planned")throw new Exception("Free task blocked by resin");
  if(GoalPlanner.Preview(new[]{weekday},60,60,now,null)[0].Status!="needs_server_clock")throw new Exception("PC weekday used as server clock");
  bool duplicate=false;try{GoalPlanner.Preview(new[]{step("same",20),step("same",20)},60,60,now,null);}catch(ArgumentException){duplicate=true;}if(!duplicate)throw new Exception("Double reservation accepted");
  string root=Path.Combine(Path.GetTempPath(),"catheryne-goals-"+Guid.NewGuid().ToString("N"));
  try{DisplayPresets.Test(root);var a=new GoalStore(root);var b=new GoalStore(root);a.Add("Synthetic goal","character");b.Add("Another goal","daily");a.SetBudget(80);var state=b.Read();if(state.Goals.Count!=2||state.ResinLimit!=80)throw new Exception("Goal persistence failed");var first=state.Goals[0];first.CharacterKey="Nahida";first.TargetLevel=90;a.Save(first);b.Complete(first.Id,true);if(!a.Read().Goals[0].Completed||a.Read().Goals[0].CharacterKey!="Nahida")throw new Exception("Goal edit identity lost");b.Complete(first.Id,false);
  var deadlineNow=DateTime.UtcNow;
  var deadlines=new[]{
   new CalendarEntry{Id="soon",Due=deadlineNow.AddHours(6),Source="hoyolab",Observed=deadlineNow},
   new CalendarEntry{Id="late",Due=deadlineNow.AddHours(7),Source="manual"},
   new CalendarEntry{Id="done",Due=deadlineNow.AddHours(1),Source="manual",Done=true},
   new CalendarEntry{Id="stale",Due=deadlineNow.AddHours(1),Source="hoyolab",Observed=deadlineNow.AddDays(-2)},
   new CalendarEntry{Id="expired",Due=deadlineNow.AddMinutes(-1),Source="hoyolab",Observed=deadlineNow},
   new CalendarEntry{Id="full",Due=deadlineNow.AddMinutes(-1),Source="estimate",Observed=deadlineNow},
   new CalendarEntry{Id="future",Start=deadlineNow.AddHours(1),Due=deadlineNow.AddHours(2),Source="manual"},
   new CalendarEntry{Id="deleted",Due=deadlineNow.AddHours(1),Source="manual",Deleted=true}};
  if(string.Join(",",CalendarStore.Critical(deadlines,deadlineNow).Select(x=>x.Id))!="full,soon")throw new Exception("Critical calendar boundary/completion/freshness filtering failed");
  var calendar=new CalendarStore(root);var eventItem=new CalendarEntry{Title="Synthetic",Due=DateTime.UtcNow.AddDays(2),Repeat="weekly"};calendar.Save(eventItem);string eventId=eventItem.Id;eventItem.Title="Edited";calendar.Save(eventItem);if(calendar.Read().Count(x=>x.Id==eventId)!=1)throw new Exception("Calendar edit duplicated entry");calendar.Remove(eventId);if(new CalendarStore(root).Read().Any(x=>x.Id==eventId))throw new Exception("Deleted calendar entry resurrected");
  string id=state.Goals[1].Id;a.MoveFirst(id);b.Pause(id);if(a.Read().Goals[0].Id!=id||!a.Read().Goals[0].Paused)throw new Exception("Shared goal state failed");try{a.Change(s=>{s.Goals.Clear();throw new Exception("rollback");});}catch(Exception){}if(b.Read().Goals.Count!=2)throw new Exception("Rollback lost goals");a.Remove(id);if(b.Read().Goals.Count!=1)throw new Exception("Goal removal failed");}
  finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
