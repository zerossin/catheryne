using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class DiagnosticsTests {
 static void Check(bool value,string reason){if(!value)throw new Exception("Diagnostics: "+reason);}
 static Exception PrivateFailure(){try{throw new IOException("private-user-path, email@example.invalid, account-roster, "+new string('s',48));}catch(Exception error){return error;}}
 static void Wait(Task task){
  var frame=new DispatcherFrame();var deadline=DateTime.UtcNow.AddSeconds(5);var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
  timer.Tick+=(s,e)=>{if(task.IsCompleted||DateTime.UtcNow>=deadline){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);Check(task.IsCompleted,"support copy must finish without blocking the dispatcher");task.GetAwaiter().GetResult();
 }
 internal static void Settings(Launcher shell){
  var window=shell.Window;var panel=(Border)window.FindName("SettingsPanel");var button=(Button)window.FindName("CopyDiagnostics");var status=(TextBlock)window.FindName("SettingsStatus");Check(button!=null,"general settings must provide the support action");
  string report=null;Wait(shell.CopyDiagnostics(value=>report=value));Check(report!=null&&report.Contains("Catheryne")&&button.IsEnabled&&status.Text==Locale.T("복사됨"),"copy must use the safe export and restore the button");
  Wait(shell.CopyDiagnostics(value=>{throw PrivateFailure();}));Check(button.IsEnabled&&status.Text==Locale.T("다시 복사해 주세요."),"clipboard failure must be actionable without exposing its message");
  Check(new AppDiagnostics(Setup.DataFolder).Export().Contains("DiagnosticExportFailure"),"failed support copy must be diagnosable");
  Wait(shell.CopyDiagnostics(value=>report=value));Check(status.Text==Locale.T("복사됨")&&!report.Contains("private-user-path"),"retry must succeed with the same private-data boundary");
  double oldWidth=window.Width,oldHeight=window.Height;bool visible=window.IsVisible;window.Show();
  try{foreach(int width in new[]{780,1240}){
   window.Width=width;window.Height=760;LauncherWindowLayout.Primary(window,panel,false);DrawerMotion.Show(panel);
   var frame=new DispatcherFrame();var delay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};delay.Tick+=(s,e)=>{delay.Stop();frame.Continue=false;};delay.Start();Dispatcher.PushFrame(frame);
   var scroll=(ScrollViewer)((Grid)panel.Child).Children[1];scroll.ScrollToEnd();window.UpdateLayout();
   var bounds=button.TransformToAncestor(scroll).TransformBounds(new Rect(new Point(),button.RenderSize));
   Check(button.ActualWidth>0&&button.ActualHeight>0&&bounds.Left>=-1&&bounds.Right<=scroll.ActualWidth+1&&bounds.Top>=-1&&bounds.Bottom<=scroll.ActualHeight+1,"support action must remain reachable at "+width);
   var image=new RenderTargetBitmap(width,760,96,96,PixelFormats.Pbgra32);image.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(Path.GetTempPath(),"catheryne-diagnostics-settings-"+width+".png")))png.Save(output);
  }}finally{DrawerMotion.Collapse(panel);window.Width=oldWidth;window.Height=oldHeight;if(!visible)window.Hide();status.Text="";}
 }
 internal static void Run(){
  string root=Path.Combine(Path.GetTempPath(),"catheryne-diagnostics-test-"+Guid.NewGuid().ToString("N"));try{
   var diagnostics=new AppDiagnostics(root,2048);diagnostics.Write(DiagnosticEvent.ToolFailure,PrivateFailure());
   string report=diagnostics.Export();Check(report.Contains("System.IO.IOException")&&report.Contains("PrivateFailure")&&report.Contains("0x"),"fault type, code and application call site must be usable");Check(!report.Contains("private-user-path")&&!report.Contains("email@example.invalid")&&!report.Contains("account-roster")&&!report.Contains(new string('s',48))&&!report.Contains(root),"support reports must omit messages, paths, account content and tokens");
   string folder=Path.Combine(root,"diagnostics"),file=Path.Combine(folder,"app.jsonl");File.AppendAllText(file,"not-json\n{\"event\":\"private-account\"}\n");File.WriteAllText(Path.Combine(folder,"achievement-view.txt"),"private legacy detail");report=diagnostics.Export();Check(!report.Contains("private-account")&&!report.Contains("legacy"),"malformed and unrelated raw diagnostics must not enter reports");
   for(int i=0;i<100;i++)diagnostics.Write(DiagnosticEvent.ToolFailure,PrivateFailure());var files=Directory.GetFiles(folder,"app*.jsonl");Check(files.Length<=4&&files.All(f=>new FileInfo(f).Length<=2048)&&files.Sum(f=>new FileInfo(f).Length)<=8192,"log rotation must bound disk storage");
   var data=CodexChat.Map(CatheryneTools.Json().DeserializeObject(diagnostics.Export()));Check(CodexChat.Items(data["events"]).Count()<=64,"support reports must remain bounded");
   string denied=Path.Combine(root,"not-a-directory");File.WriteAllText(denied,"fixture");new AppDiagnostics(denied).Write(DiagnosticEvent.Started);Check(new AppDiagnostics(denied).Export().Contains("read_incomplete"),"logging failures must not replace the original failure");
   Task.Run(async()=>{var waiting=new TaskCompletionSource<string>();bool requested=false;using(var chat=new CodexChat(root,()=>{requested=true;return waiting.Task;})){var startup=chat.Start();Check(requested,"runtime selection must be pending");chat.Dispose();waiting.SetResult("never-launch-this-fixture.exe");bool stopped=false;try{await startup;}catch(ObjectDisposedException){stopped=true;}Check(stopped,"disposal during runtime selection must prevent late process startup");}Check(!Directory.Exists(Path.Combine(root,"ai-workspace")),"disposed startup must not retain a workspace directory");}).GetAwaiter().GetResult();
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
