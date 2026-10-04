using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PerformanceTests {
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static BitmapSource Image(int side=2){var image=BitmapSource.Create(side,side,96,96,PixelFormats.Bgra32,null,new byte[side*side*4],side*4);image.Freeze();return image;}
 internal static void TaskProjection(WorkspaceHome workspace){
  var panel=(AiWorkspacePanel)typeof(WorkspaceHome).GetField("aiPanel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(workspace);
  var show=typeof(AiWorkspacePanel).GetField("showing",BindingFlags.Instance|BindingFlags.NonPublic);var signature=typeof(AiWorkspacePanel).GetField("signature",BindingFlags.Instance|BindingFlags.NonPublic);var render=typeof(AiWorkspacePanel).GetMethod("RenderTasks",BindingFlags.Instance|BindingFlags.NonPublic);
  var cards=(List<AiTaskCard>)typeof(AiWorkspacePanel).GetField("cards",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(panel);int notices=0;Action<AiTaskRecord> notified=t=>notices++;panel.TaskUpdated+=notified;
  var task=new AiTaskRecord{Id="projection-fixture",Thread="projection-fixture",Title="Fixture",State="waiting",Reason="before"};var tasks=new List<AiTaskRecord>{task};
  try{
   show.SetValue(panel,true);signature.SetValue(panel,null);render.Invoke(panel,new object[]{tasks,new HashSet<string>{task.Id}});var card=cards.Single();
   var fold=(Expander)typeof(AiTaskCard).GetField("previous",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card);fold.IsExpanded=true;
   render.Invoke(panel,new object[]{tasks,new HashSet<string>()});Check(notices==1&&ReferenceEquals(card,cards.Single())&&fold.IsExpanded,"Idle polling must preserve cards, disclosure and notification count");
   tasks[0]=new AiTaskRecord{Id=task.Id,Thread=task.Thread,Title=task.Title,State=task.State,Reason="after"};render.Invoke(panel,new object[]{tasks,new HashSet<string>{task.Id}});Check(notices==2&&ReferenceEquals(card,cards.Single())&&card.Task.Reason=="after"&&fold.IsExpanded,"Changed task payload must update its retained card and notify once");
  }finally{panel.TaskUpdated-=notified;show.SetValue(panel,false);signature.SetValue(panel,null);}
 }
 internal static void Run(){
  var image=Image();var cache=new BitmapCache(64,2);int loads=0;var pending=new TaskCompletionSource<BitmapSource>();
  var first=cache.Get("same",()=>{loads++;return pending.Task;});var second=cache.Get("same",()=>{loads++;return Task.FromResult(image);});Check(ReferenceEquals(first,second)&&loads==1,"Concurrent portrait requests must share one load");pending.SetResult(image);first.GetAwaiter().GetResult();
  Check(ReferenceEquals(cache.Get("same",()=>{throw new Exception("Unexpected decode");}).GetAwaiter().GetResult(),image),"Completed portrait must reuse frozen pixels");
  cache.Get("second",()=>Task.FromResult(image)).GetAwaiter().GetResult();cache.Get("same",()=>Task.FromResult(image));cache.Get("third",()=>Task.FromResult(image)).GetAwaiter().GetResult();Check(cache.Count==2&&cache.Bytes==32,"Image cache must have bounded decoded memory and entries");
  cache.Get("second",()=>{loads++;return Task.FromResult(image);}).GetAwaiter().GetResult();Check(loads==2,"Least recently used image must be evicted");
  cache=new BitmapCache(16,10);cache.Get("small",()=>Task.FromResult(image)).GetAwaiter().GetResult();cache.Get("other",()=>Task.FromResult(image)).GetAwaiter().GetResult();Check(cache.Bytes==16&&cache.Count==1,"Image byte budget must evict independently of entry count");
  cache.Get("large",()=>Task.FromResult(Image(4))).GetAwaiter().GetResult();Check(cache.Bytes==16&&cache.Count==1,"Oversized images must not evict reusable small images or remain cached");
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));byte[] bytes;using(var output=new MemoryStream()){encoder.Save(output);bytes=output.ToArray();}
  var decoded=new BitmapImage();using(var input=new MemoryStream(bytes,0,bytes.Length,false,true)){decoded.BeginInit();decoded.StreamSource=input;decoded.CacheOption=BitmapCacheOption.OnLoad;decoded.EndInit();decoded.Freeze();}
  cache=new BitmapCache(16+bytes.Length-1,10);cache.Get("encoded",()=>Task.FromResult<BitmapSource>(decoded)).GetAwaiter().GetResult();Check(cache.Count==0,"Image byte budget must include retained encoded source buffers");
  var resized=new TransformedBitmap(Image(4),new ScaleTransform(.5,.5));resized.Freeze();cache=new BitmapCache(16,10);cache.Get("resized",()=>Task.FromResult<BitmapSource>(resized)).GetAwaiter().GetResult();Check(cache.Count==0,"Resized images must count the retained original pixels");
  int tries=0;var failed=new TaskCompletionSource<BitmapSource>();failed.SetException(new IOException("Synthetic failure"));try{cache.Get("retry",()=>{tries++;return failed.Task;}).GetAwaiter().GetResult();}catch(IOException){}cache.Get("retry",()=>{tries++;return Task.FromResult(image);}).GetAwaiter().GetResult();Check(tries==2,"Failed image requests must be retryable");
  cache.Get("missing",()=>Task.FromResult<BitmapSource>(null)).GetAwaiter().GetResult();Check(cache.Get("missing",()=>Task.FromResult(image)).GetAwaiter().GetResult()!=null,"Missing image must not be cached permanently");
  var result=new Dictionary<string,object>{{"progress",new Dictionary<string,object>{{"completed",2},{"total",4}}}};var task=new AiTaskRecord{State="running",Result=result};Check(ReferenceEquals(task.ResultData,result)&&TaskProgress.Fraction(task,DateTime.UtcNow)==.5,"Decoded task results must retain progress semantics without conversion");result["progress"]=new Dictionary<string,object>{{"completed",3},{"total",4}};Check(TaskProgress.Fraction(task,DateTime.UtcNow)==.75,"Result updates must not return stale cached progress");task.Result=new{progress=new{completed=1,total=4}};Check(TaskProgress.Fraction(task,DateTime.UtcNow)==.25,"In-process anonymous results must still be normalized");task.Result=new Dictionary<string,object>{{"progress",new{measured=true,completed=2,total=4}}};Check(TaskProgress.Measured(task)&&TaskProgress.Fraction(task,DateTime.UtcNow.AddDays(1))==.5,"Mixed dictionaries must normalize anonymous children and preserve measured progress");
  string root=Path.Combine(Path.GetTempPath(),"catheryne-index-test-"+Guid.NewGuid().ToString("N"));try{
   using(var db=new LocalDataService(root)){db.Observe("default","ai-task",new AiTaskRecord{Id="indexed",State="running"});Check(db.Query("EXPLAIN QUERY PLAN SELECT payload FROM observations WHERE profile='default' AND kind='ai-task' AND json_extract(payload,'$.Id')='indexed' ORDER BY id DESC LIMIT 1").Any(r=>r.Values.Any(v=>v!=null&&v.Contains("observations_task_id"))),"Task identity lookup must use its derived index");db.Execute("DROP INDEX observations_task_id");}
   using(var db=new LocalDataService(root))Check(db.Query("SELECT name FROM sqlite_master WHERE name='observations_task_id'").Count==1&&db.Recent("default","ai-task").Count==1,"Derived index must rebuild without losing task history");
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
