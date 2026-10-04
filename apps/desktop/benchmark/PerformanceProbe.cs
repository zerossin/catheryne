using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PerformanceProbe {
 static readonly Dictionary<string,object> metrics=new Dictionary<string,object>();
 static void Measure(string name,int repeats,Action action){action();GC.Collect();GC.WaitForPendingFinalizers();int collections=GC.CollectionCount(0);var watch=Stopwatch.StartNew();for(int i=0;i<repeats;i++)action();watch.Stop();metrics[name]=new{iterations=repeats,elapsed_ms=watch.Elapsed.TotalMilliseconds,gen0=GC.CollectionCount(0)-collections};}
 [STAThread] static int Main(string[] args){
  string root=Path.Combine(Path.GetTempPath(),"catheryne-probe-data-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Environment.SetEnvironmentVariable("CATHERYNE_TOOL_DATA",root);
  try {
   // Only synthetic data; no game input, external authentication or network.
   var payload=new Dictionary<string,object>{{"progress",new Dictionary<string,object>{{"completed",1},{"total",20}}},{"reference",new string('x',16000)}};
   var task=new AiTaskRecord{Id="probe",State="running",Started=DateTime.UtcNow.ToString("o"),Result=payload};
   Measure("progress_2000",2000,()=>TaskProgress.Fraction(task,DateTime.UtcNow));
   using(var db=new LocalDataService(root)){db.Execute("BEGIN IMMEDIATE");for(int i=0;i<3000;i++)db.Observe("default","ai-task",new AiTaskRecord{Id="task-"+(i%300),Title="Synthetic task "+i,State="completed",Started="2026-09-01T00:00:00Z",Ended="2026-09-01T00:01:00Z",Result=payload});db.Execute("COMMIT");}
   var store=new AiTaskStore(root);Measure("task_find_100",100,()=>{if(store.Find("task-0")==null)throw new Exception("Missing task");});Measure("task_list_5",5,()=>{if(store.List().Count!=300)throw new Exception("Missing task list");});
  }catch(Exception error){metrics["error"]=error.ToString();}
  try {
   // Use the normal shell and its task projection with a closed task history.
   using(var shell=new Launcher(null,false,true,false,false,connectAi:false)){
    var workspace=(WorkspaceHome)typeof(Launcher).GetField("workspace",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(shell);
    var panel=(AiWorkspacePanel)typeof(WorkspaceHome).GetField("aiPanel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(workspace);
    var list=new AiTaskStore(root).List();var render=typeof(AiWorkspacePanel).GetMethod("RenderTasks",BindingFlags.Instance|BindingFlags.NonPublic);
    object[] parameters=render.GetParameters().Length==1?new object[]{list}:new object[]{list,null};
    Measure("idle_tasks_20",20,()=>render.Invoke(panel,parameters));shell.Window.Close();
   }
   var view=new ChatText();string text=string.Join("\n\n",Enumerable.Range(0,300).Select(i=>"Paragraph "+i+" **bold** text."));view.Text=text;bool toggle=false;
   Measure("markdown_initial_10",10,()=>{var message=new ChatText();message.Text=text;});
   Measure("markdown_30",30,()=>{toggle=!toggle;view.Text=text+"\n\nTail "+(toggle?"one":"two");});
   string file=Path.Combine(root,"cache","portraits","portrait.png");Directory.CreateDirectory(Path.GetDirectoryName(file));var image=BitmapSource.Create(256,256,96,96,PixelFormats.Bgra32,null,new byte[256*256*4],256*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(file))encoder.Save(output);
   // An allowlisted URL hashes to this fixture; no request leaves the machine.
   string url="https://act-webstatic.hoyoverse.com/probe.png",hash;using(var sha=System.Security.Cryptography.SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-","").ToLowerInvariant();File.Copy(file,Path.Combine(Path.GetDirectoryName(file),hash+".png"));
   var pictures=Enumerable.Range(0,100).Select(i=>new Image()).ToArray();int checks=0;var clock=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1)};var frame=new DispatcherFrame();var watch=Stopwatch.StartNew();clock.Tick+=(s,e)=>{checks++;if(pictures.All(p=>p.Source!=null)||checks>15000){clock.Stop();frame.Continue=false;}};foreach(var picture in pictures)GameCatalog.Portrait(picture,"probe",160,url);clock.Start();Dispatcher.PushFrame(frame);watch.Stop();if(pictures.Any(p=>p.Source==null))throw new Exception("Portrait fixture failed");
   metrics["portraits_100"]=new{elapsed_ms=watch.Elapsed.TotalMilliseconds,distinct_bitmaps=pictures.Select(p=>p.Source).Distinct().Count()};
  }catch(Exception error){metrics["ui_error"]=error.ToString();}
  File.WriteAllText(args[0],CatheryneTools.Json().Serialize(metrics));Console.WriteLine(CatheryneTools.Json().Serialize(metrics));if(Directory.Exists(root))Directory.Delete(root,true);return metrics.ContainsKey("error")||metrics.ContainsKey("ui_error")?1:0;
 }
}
