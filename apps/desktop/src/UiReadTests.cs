using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class UiReadTests {
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static IEnumerable<T> Children<T>(DependencyObject node) where T:DependencyObject{foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()){if(child is T)yield return (T)child;foreach(var nested in Children<T>(child))yield return nested;}}
 static void Pump(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(8);while(!done()){Check(DateTime.UtcNow<until,"UI read fixture timed out");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(1);}}
 static MaterialPanel.ViewData Data(string name,string signature){
  var row=new Dictionary<string,object>{{"id","1"},{"name",name},{"num",5}};
  var plan=new Dictionary<string,object>{{"requirements",new[]{row}}};
  var metadata=new Dictionary<string,object>{{"materialPlan",plan}};
  return new MaterialPanel.ViewData{Snapshot=new Dictionary<string,object>{{"catheryne",metadata}},Goals=new GoalState(),Signature=signature};
 }
 internal static void Run(){var prior=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());try{
  int ui=Thread.CurrentThread.ManagedThreadId,reads=0,beats=0;using(var release=new ManualResetEventSlim()){
   var view=MaterialPanel.View(new TextBlock{Text="fixture"},()=>{Check(Thread.CurrentThread.ManagedThreadId!=ui,"Material disk reads must not run on the UI thread");int call=Interlocked.Increment(ref reads);if(call==1)Check(release.Wait(5000),"Material fixture released");return call==1?Data("stale-fixture","old"):Data("fresh-fixture","new");});
   var window=new Window{Content=view,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,Left=-16000,Top=-16000,Width=500,Height=500};var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(5)};timer.Tick+=(s,e)=>{beats++;if(beats>=3)release.Set();};
   try{window.Show();Pump(()=>reads==1);view.Visibility=Visibility.Collapsed;view.Visibility=Visibility.Visible;timer.Start();Pump(()=>Children<TextBlock>(view).Any(x=>x.Text=="fresh-fixture"));Check(reads==2&&beats>=3&&!Children<TextBlock>(view).Any(x=>x.Text=="stale-fixture"),"Material re-entry must keep the UI responsive and discard stale results");var label=Children<TextBlock>(view).Single(x=>x.Text=="fresh-fixture");view.Visibility=Visibility.Collapsed;view.Visibility=Visibility.Visible;Pump(()=>reads==3);Pump(()=>!Children<TextBlock>(view).Any(x=>x.Text=="stale-fixture"));Check(ReferenceEquals(label,Children<TextBlock>(view).Single(x=>x.Text=="fresh-fixture")),"Unchanged materials must preserve their existing rows");}finally{release.Set();timer.Stop();window.Close();}
  }
  string root=Path.Combine(Path.GetTempPath(),"catheryne-import-detect-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{var store=new ProfileStore(root);string file=Path.Combine(root,"fixture.json");File.WriteAllText(file,"{\"format\":\"GOOD\",\"characters\":[],\"weapons\":[]}");store.ImportDetected(file);Check(store.Summary("account").Contains("0"),"Detected imports must use the canonical account path");File.WriteAllText(file,"{\"Version\":1,\"Data\":{\"80032\":4}}");store.ImportDetected(file);using(var db=new LocalDataService(root))Check(db.Query("SELECT verified FROM achievements WHERE id=80032").Single()["verified"]=="0","Detected imports must not promote unverified achievements");AppPreferences.Set("resinUid","123456789",root);AppPreferences.Set("resinServer","os_asia",root);var service=new PrimogemService(root);var report=service.ReadVersion("7.0");var primo=new PrimogemPanel(new Window(),root);primo.RenderReport(report);AppPreferences.Set("resinUid","987654321",root);var reward=new PrimoReward{Id="old-account-fixture",Version="7.0",Currency="primogem",Name="fixture",Total=10,Received=0};typeof(PrimogemPanel).GetMethod("Edit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(primo,new object[]{reward});string owner=(string)typeof(PrimogemPanel).GetField("editorAccount",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(primo);Check(owner==report.Account&&owner!=service.Account(),"Editing an old report must retain its original account and cannot write it into the newly selected account");bool wrongAccount=false;try{service.SaveReward(owner,reward);}catch(InvalidOperationException){wrongAccount=true;}Check(wrongAccount,"The canonical reward store must reject an editor from another account");File.WriteAllText(file,"{}");bool rejected=false;try{store.ImportDetected(file);}catch(InvalidDataException){rejected=true;}Check(rejected,"Malformed detected imports must still be rejected");}finally{Directory.Delete(root,true);}
 }finally{SynchronizationContext.SetSynchronizationContext(prior);}}
}
