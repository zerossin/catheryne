using System;
using System.Linq;
using System.Collections.Generic;

// Estimates never change verified stage status or task completion.
internal static class TaskProgress {
 internal static bool Measured(AiTaskRecord task){var data=task.ResultData;object raw;return data.TryGetValue("progress",out raw)&&Equals(ExternalTools.Value(CodexChat.Map(raw),"measured"),true);}
 internal static double Fraction(AiTaskRecord task,DateTime now){
  if(task.State=="completed")return 1;
  if(Measured(task)){var progress=CodexChat.Map(task.ResultData["progress"]);double total=Convert.ToDouble(progress["total"]);return total>0?Math.Max(0,Math.Min(1,Convert.ToDouble(progress["completed"])/total)):0;}
  DateTime start,end;double seconds=0;if(DateTime.TryParse(task.Started,null,System.Globalization.DateTimeStyles.RoundtripKind,out start)){if(task.State!="running"&&DateTime.TryParse(task.Ended,null,System.Globalization.DateTimeStyles.RoundtripKind,out end))now=end;seconds=Math.Max(0,(now.ToUniversalTime()-start.ToUniversalTime()).TotalSeconds);}
  double timed=task.ExpectedSeconds>0?.85*(1-Math.Exp(-seconds/task.ExpectedSeconds)):0;
  var data=task.ResultData;object raw;double verified=0;
  if(data.TryGetValue("progress",out raw)){var progress=CodexChat.Map(raw);if(progress.ContainsKey("completed")&&progress.ContainsKey("total")&&Convert.ToDouble(progress["total"])>0)verified=Convert.ToDouble(progress["completed"])/Convert.ToDouble(progress["total"]);}
  return Math.Min(.95,Math.Max(task.EstimatedProgress,Math.Max(timed,verified)));
 }
 internal static void Learn(AiTaskRecord task,IEnumerable<AiTaskRecord> history){
  var samples=history.Where(t=>t.Id!=task.Id&&t.State=="completed"&&t.Operation==task.Operation&&(!string.IsNullOrEmpty(task.Operation)||t.Action==task.Action)&&t.Tool==task.Tool&&!t.HadInterruption&&string.IsNullOrEmpty(t.ParentTaskId)).Select(t=>{DateTime a,b;return DateTime.TryParse(t.Started,out a)&&DateTime.TryParse(t.Ended,out b)?(b-a).TotalSeconds/Math.Max(1,t.Plan==null?0:t.Plan.Length):0;}).Where(v=>v>0).Take(20).ToArray();
  task.ProgressSampled=true;task.ProgressSamples=samples.Length;task.ExpectedSeconds=samples.Length>=3?samples.Average()*Math.Max(1,task.Plan==null?0:task.Plan.Length):0;
 }
 internal static object Report(string root,Dictionary<string,object> args,string thread,Action<AiTaskRecord> changed){
  var store=new AiTaskStore(root);var task=store.Find(CodexChat.S(args,"task_id"));if(task==null||task.Thread!=thread||task.State!="running")throw new InvalidOperationException("현재 대화의 실행 중인 작업을 선택해 주세요.");
  if(Measured(task))throw new InvalidOperationException("확인된 막 수로 계산하는 진행도는 추정값으로 바꿀 수 없습니다.");
  double percent=Convert.ToDouble(args["percent"]);string evidence=CodexChat.S(args,"evidence").Trim();if(double.IsNaN(percent)||double.IsInfinity(percent)||percent<0||percent>95||evidence.Length==0||evidence.Length>2000)throw new ArgumentException("진행 추정은 0~95 사이 값과 현재 상황의 근거가 필요합니다.");
  task.EstimatedProgress=percent/100;task.ProgressEvidence=evidence;task.ProgressUpdated=DateTime.UtcNow.ToString("o");store.Save(task);if(changed!=null)changed(task);return new{task_id=task.Id,estimated_percent=task.EstimatedProgress*100,evidence=task.ProgressEvidence,verified_completion=false};
 }
 internal static void StoreTest(string root){
  root=System.IO.Path.Combine(root,"progress-fixture");System.IO.Directory.CreateDirectory(root);
  var store=new AiTaskStore(root);var task=store.Begin("Progress fixture","progress-test","progress-test","test");var stale=store.Find(task.Id);var args=new Dictionary<string,object>{{"task_id",task.Id},{"percent",80},{"evidence","Observed final section"}};
  Report(root,args,"progress-test",null);store.Save(stale);if(store.Find(task.Id).EstimatedProgress!=.8)throw new Exception("Stale task writes must preserve progress reports");bool denied=false;try{Report(root,args,"foreign",null);}catch(InvalidOperationException){denied=true;}if(!denied)throw new Exception("Progress cannot cross threads");args["percent"]=100;denied=false;try{Report(root,args,"progress-test",null);}catch(ArgumentException){denied=true;}if(!denied)throw new Exception("Estimate cannot claim completion");store.End(store.Find(task.Id),"cancelled","fixture end");
 }
 internal static void Test(){
  var now=DateTime.UtcNow;var task=new AiTaskRecord{State="running",Started=now.ToString("o"),ExpectedSeconds=100};if(Fraction(task,now)!=0||Fraction(task,now.AddSeconds(100))<=0||Fraction(task,now.AddDays(1))>=1)throw new Exception("Estimated progress must start empty and never complete a task");task.EstimatedProgress=.8;if(Fraction(task,now)!=.8)throw new Exception("AI estimate must reach the UI");task.State="completed";if(Fraction(task,now)!=1)throw new Exception("Verified completion must fill the track");
  task=new AiTaskRecord{Id="new",Tool="test",Operation="story",Plan=new[]{"A","B"}};var samples=Enumerable.Range(0,3).Select(i=>new AiTaskRecord{Id=i.ToString(),Tool="test",Operation="story",State="completed",Started=now.AddSeconds(-100).ToString("o"),Ended=now.ToString("o"),Plan=new[]{"A","B"}}).ToArray();Learn(task,samples.Take(2));if(task.ExpectedSeconds!=0)throw new Exception("Insufficient history must not invent a speed");Learn(task,samples);if(task.ExpectedSeconds!=100)throw new Exception("Completed history must determine mean duration");
 }
}
