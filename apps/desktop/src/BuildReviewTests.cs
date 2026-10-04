using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class BuildReviewTests {
 static void Check(bool condition,string message){if(!condition)throw new Exception("Build review: "+message);}
 static void Pump(Func<bool> done){var deadline=DateTime.UtcNow.AddSeconds(10);while(!done()){if(DateTime.UtcNow>deadline)throw new Exception("Build review UI timed out");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(1);}}
 static IEnumerable<T> Children<T>(DependencyObject node) where T:DependencyObject {foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()){if(child is T)yield return (T)child;foreach(var nested in Children<T>(child))yield return nested;}}
 internal static void Run(string root){
  root=System.IO.Path.Combine(root,"build-review");System.IO.Directory.CreateDirectory(root);var previousContext=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());try{
  int ui=Thread.CurrentThread.ManagedThreadId,reads=0;var character=new Dictionary<string,object>{{"key","Faruzan"},{"names",new Dictionary<string,object>{{"ko","시험비교"},{"en","Review fixture"}}},{"level",90},{"constellation",6}};
  var snapshot=new Dictionary<string,object>{{"characters",new[]{character}},{"artifacts",new object[0]}};
  var account=new Dictionary<string,object>(snapshot){{"format",AccountIdentity.Format},{"version",1}};string file=System.IO.Path.Combine(root,"fixture.json");System.IO.File.WriteAllText(file,CatheryneTools.Json().Serialize(account));new ProfileStore(root).Import("account",file);
  var blocks=Enumerable.Range(0,500).Select(i=>new BuildReferenceBlock{Character=i==499?"시험비교":"다른 캐릭터"+i,First=i+5,Last=i+5,Rows=new List<BuildReferenceRow>{new BuildReferenceRow{Row=i+5,Values=new Dictionary<string,string>{{"무기","시험 무기"+i},{"공격력","1234"}},Notes=new Dictionary<string,string>()}}}).ToList();
  var references=new BuildReferenceData{FetchedUtc="2026-10-01T00:00:00Z",Sheets=new List<BuildReferenceSheet>{new BuildReferenceSheet{Name="fixture",Url=BuildReferences.Workbook+"/edit?gid=0",Characters=blocks}}};
  using(var shell=new Launcher(null,false,true,false,false,connectAi:false))using(var allowSnapshot=new ManualResetEventSlim())using(var allowReference=new ManualResetEventSlim()){
   var panel=(Border)shell.Window.FindName("DetailPanel");var detail=new DetailPanel(shell.Window);int heartbeats=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(5)};timer.Tick+=(s,e)=>heartbeats++;timer.Start();
   try{
    var review=new BuildReview(root,"Faruzan",detail,null);var opening=review.ShowAsync(()=>{Interlocked.Increment(ref reads);Check(Thread.CurrentThread.ManagedThreadId!=ui,"snapshot I/O runs outside the UI thread");Check(allowSnapshot.Wait(5000),"snapshot released");return snapshot;},()=>{Check(Thread.CurrentThread.ManagedThreadId!=ui,"reference loading runs outside the UI thread");Check(allowReference.Wait(5000),"references released");return references;});
    Check(!opening.IsCompleted&&detail.IsShowing("character-build-Faruzan"),"comparison shell opens before a slow snapshot completes");Pump(()=>heartbeats>=3);allowSnapshot.Set();Pump(()=>Children<CheckBox>(panel).Count()==BuildAnalysis.StatKeys.Length);int before=heartbeats;Pump(()=>heartbeats>=before+3);allowReference.Set();Pump(()=>opening.IsCompleted);opening.GetAwaiter().GetResult();
    Check(reads==1&&Children<ComboBox>(panel).Count()==2&&Children<TextBlock>(panel).Any(x=>x.Text=="1234"),"500-block reference filtering uses one snapshot and preserves names and field values");
    foreach(int width in new[]{760,1500}){shell.Window.Width=width;panel.Measure(new Size(width,760));panel.Arrange(new Rect(0,0,width,760));panel.UpdateLayout();Check(Children<CheckBox>(panel).Count()==BuildAnalysis.StatKeys.Length,"narrow/wide render retains score options");}
    var checks=Children<CheckBox>(panel).ToArray();checks[0].IsChecked=true;checks[1].IsChecked=false;Pump(()=>{var saved=AppPreferences.Read(root);object raw;if(!saved.TryGetValue("buildCriterion.Faruzan",out raw))return false;var criterion=CatheryneTools.Json().Deserialize<BuildCriterion>(CatheryneTools.Json().Serialize(raw));return criterion.Useful.Contains(BuildAnalysis.StatKeys[0])&&!criterion.Useful.Contains(BuildAnalysis.StatKeys[1]);});Check(CharacterBuild.Read(root,character).Useful.Contains(BuildAnalysis.StatKeys[0])&&!CharacterBuild.Read(root,character).Useful.Contains(BuildAnalysis.StatKeys[1]),"rapid option changes persist in order");
    // Replace an opening instance with a new instance using the same navigation key.
    var delayed=new TaskCompletionSource<Dictionary<string,object>>();var stale=new BuildReview(root,"Faruzan",detail,null).ShowAsync(()=>delayed.Task.GetAwaiter().GetResult(),()=>references);var replacement=new TextBlock{Text="replacement"};detail.Show("character-build-Faruzan","replacement",replacement);delayed.SetResult(snapshot);Pump(()=>stale.IsCompleted);Check(replacement.Parent!=null,"late completion cannot replace a newer same-key view");
    var failed=new BuildReview(root,"Faruzan",detail,null).ShowAsync(()=>{throw new System.IO.IOException("fixture read failure");},()=>references);Pump(()=>failed.IsCompleted);failed.GetAwaiter().GetResult();Check(Children<TextBlock>(panel).Any(x=>x.Text=="fixture read failure"),"snapshot failure remains visible instead of escaping async void");Check(new AppDiagnostics(root).Export().Contains("DataRefreshFailure"),"comparison failures enter the shared privacy-safe diagnostic log");
    detail.Hide();
   }finally{allowSnapshot.Set();allowReference.Set();timer.Stop();}
  }
  }finally{SynchronizationContext.SetSynchronizationContext(previousContext);}
 }
}
