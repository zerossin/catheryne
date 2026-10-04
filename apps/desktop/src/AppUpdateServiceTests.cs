using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
internal static class AppUpdateServiceTests {
 static void Check(bool value,string message){if(!value)throw new Exception("App update service: "+message);}
 internal static void Ui(System.Windows.Window owner){
  var host=owner.FindName("AppUpdateSection") as System.Windows.Controls.StackPanel;
  if(host==null)throw new Exception("App update settings section missing");
  var panel=(System.Windows.Controls.Border)owner.FindName("SettingsPanel");var prior=panel.Visibility;bool shown=owner.IsVisible;owner.Show();panel.Visibility=System.Windows.Visibility.Visible;
  var button=host.Children.OfType<System.Windows.Controls.DockPanel>().SelectMany(row=>row.Children.OfType<System.Windows.Controls.Button>()).Single();
  object oldContent=button.Content;button.Content=StoryClient.Read(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"en-US.json"))["업데이트 후 다시 시작"];
  foreach(double width in new[]{780d,1240d}){
   owner.Width=width;LauncherWindowLayout.Primary(owner,panel,false);owner.Measure(new System.Windows.Size(width,760));owner.Arrange(new System.Windows.Rect(0,0,width,760));owner.UpdateLayout();
   if(host.ActualWidth<250)throw new Exception("App update card did not render");
   if(button.ActualWidth>0){var point=button.TranslatePoint(new System.Windows.Point(button.ActualWidth,0),host);if(point.X>host.ActualWidth+1)throw new Exception("App update action clipped in general settings");}
  }
  button.Content=oldContent;panel.Visibility=prior;if(!shown)owner.Hide();
 }

 internal static void Run(){
  string directory=Path.Combine(Path.GetTempPath(),"catheryne-update-service-"+Guid.NewGuid().ToString("N")),app=Path.Combine(directory,"app"),root=Path.Combine(directory,"data");
  Directory.CreateDirectory(app);
  try{
   File.Copy(typeof(AppUpdateServiceTests).Assembly.Location,Path.Combine(app,"GenshinLauncher.exe"));File.WriteAllText(Path.Combine(app,"resources.json"),"{\"projectUrl\":\"https://github.com/example/catheryne\"}");
   byte[] bytes=Enumerable.Repeat((byte)3,2048).ToArray();string body=Path.Combine(directory,"installer-fixture");File.WriteAllBytes(body,bytes);
   var installed=AppUpdateProtocol.Version(AppUpdateProtocol.InstalledVersion(app));string version=(installed.Major+1)+".0.0",hash=AppUpdateProtocol.Hash(body);
   string raw=AppUpdateProtocol.Json().Serialize(new{tag_name="v"+version,draft=false,prerelease=false,assets=new[]{new{name="Catheryne-Setup-"+version+".exe",state="uploaded",size=bytes.Length,digest="sha256:"+hash,browser_download_url="https://github.com/example/catheryne/releases/download/v"+version+"/Catheryne-Setup-"+version+".exe"}}});
   int fetched=0;bool offline=false;var service=new AppUpdateService(app,root,repo=>{fetched++;if(offline)throw new IOException("fixture offline");return raw;},()=>true);
   var state=service.Check();Check(state.State=="available"&&!state.Prepared,"available version without installation");service.Check();Check(fetched==1,"automatic checks have a cooldown");service.Check(true);Check(fetched==2,"manual check bypasses cooldown");
   Check(!AppUpdateService.Fresh(state,"other/repository",DateTime.UtcNow)&&!AppUpdateService.Fresh(state,state.Repository,DateTime.UtcNow.AddDays(-1)),"repository changes and future timestamps are not fresh");
   bool rejected=false;try{service.Download(state.Release,null,(release,file,progress)=>File.WriteAllBytes(file,new byte[1024]));}catch(InvalidDataException){rejected=true;}Check(rejected&&!File.Exists(AppUpdateProtocol.Pending(root)),"bad download never creates a pending installation");Check(!Directory.GetFiles(AppUpdateProtocol.Cache(root),"*.part",SearchOption.AllDirectories).Any(),"failed download removes partial files");
   state=service.Download(state.Release,null,(release,file,progress)=>File.WriteAllBytes(file,bytes));Check(state.Prepared&&service.Prepared(state.Release),"complete package is atomically staged");
   string pending=File.ReadAllText(AppUpdateProtocol.Pending(root));Check(!service.Handoff()&&pending==File.ReadAllText(AppUpdateProtocol.Pending(root))&&!AppUpdateService.Applying,"busy guard keeps the app and prepared installer intact");
   offline=true;state=service.Check(true);Check(state.State=="failed"&&state.Prepared,"offline checks retain an already verified installer");
   Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"settings.json"),"{\"appAutoUpdate\":true,\"fixturePreference\":42}");AppUpdateProtocol.Write(Path.Combine(AppUpdateProtocol.Cache(root),"result.json"),new{state="failed"});Check(!service.AutoApply(),"failed installation cannot become an automatic restart loop");
   File.WriteAllText(Path.Combine(app,"resources.json"),"{\"projectUrl\":null}");Check(service.Check(true).State=="unconfigured","unknown release repository never claims latest");Check(File.ReadAllText(Path.Combine(root,"settings.json")).Contains("fixturePreference"),"update checks do not rewrite user settings");
  }finally{Directory.Delete(directory,true);}
 }
}
