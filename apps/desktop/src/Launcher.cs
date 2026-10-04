using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Shell;
using Forms = System.Windows.Forms;

internal static class Entry {
 [STAThread] static int Main(string[] args) {
  AppDomain.CurrentDomain.UnhandledException+=(s,e)=>AppDiagnostics.Record(DiagnosticEvent.UnhandledFailure,e.ExceptionObject as Exception);
  TaskScheduler.UnobservedTaskException+=(s,e)=>AppDiagnostics.Record(DiagnosticEvent.UnhandledFailure,e.Exception);
  try{
   if(args.Contains("--notification-server")){if(!AppNotifications.IsActivationServer(args))return 1;string link=AppNotifications.AwaitActivation();if(link==null)return 0;args=new[]{"--preview","--notification",link};}
   if(args.Contains("--notification")){if(args.Length!=3||args[0]!="--preview"||args[1]!="--notification")return 1;NotificationTarget.Parse(args[2]);}
   if(args.Length==4&&args[0]=="--game-environment-host"&&new[]{"automatic","interactive"}.Contains(args[3]))return GameEnvironment.Host(args[1],int.Parse(args[2]),args[3]=="interactive");
   if(args.Length==7&&args[0]=="--game-environment-worker")return GameEnvironment.Worker(args[1],int.Parse(args[2]),int.Parse(args[3]),args[4],int.Parse(args[5]),long.Parse(args[6]));
   if(args.Length==3&&args[0]=="--game-environment")return GameEnvironment.Command(args[1],args[2]);
   if(args.Length>0&&args[0].StartsWith("--game-environment",StringComparison.Ordinal))throw new ArgumentException(Locale.T("게임 실행 환경 요청이 올바르지 않습니다."));
   if(args.Length==2&&args[0]=="--runtime-host")return AppRuntime.Host(args[1],Run);
   if(args.Contains("--self-test"))return Tests.Run(args.Contains("--english")?"en-US":"ko-KR");
   if(AppUpdateProtocol.Installing(AppDomain.CurrentDomain.BaseDirectory)){if(args.Length==0||args.Any(a=>new[]{"--preview","--settings","--play","--setup"}.Contains(a)))return 0;throw new IOException(Locale.T("앱 업데이트가 진행 중입니다."));}
   // Elevated helpers already receive the caller's resolved data path.
   if(args.Length>0&&new[]{"--external-run","--external-stop","--unlocker-session","--application-job"}.Contains(args[0]))return Run(args);
   if(AppRuntime.Redirected(Setup.DataFolder))return AppRuntime.Forward(args,Setup.DataFolder);
   return Run(args);
  }catch(Exception error){
   AppDiagnostics.Record(DiagnosticEvent.RuntimeFailure,error);
   if(args.Contains("--runtime-host")||args.Any(a=>a.StartsWith("--game-environment"))){using(var writer=new StreamWriter(Console.OpenStandardError(),new UTF8Encoding(false)))writer.Write(error.GetBaseException().Message);return 1;}
   if(args.Contains("--ai-tool")){using(var writer=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)))writer.Write(CatheryneTools.Json().Serialize(new{error="CATHERYNE_RUNTIME_FAILED",message=error.Message}));}
   else if(args.Contains("--mcp-server")){using(var writer=new StreamWriter(Console.OpenStandardError(),new UTF8Encoding(false)))writer.Write(error.Message);}
   else MessageBox.Show(error.Message,Locale.T("캣서린"),MessageBoxButton.OK,MessageBoxImage.Error);
   return 1;
  }
 }
 static int Run(string[] args) {
  if(args.Length==1&&args[0]=="--notification-probe")return AppNotifications.Probe();
  if(args.Length>0&&args[0]=="--mcp-server")return AppRuntime.Mcp(args.Skip(1).ToArray());
  if(args.Length==3&&args[0]=="--unlocker-session")return UnlockerSession.Run(args[1],args[2]);
  if(args.Length==3&&args[0]=="--external-run")return new ExternalTools(args[1]).StartOwnedGroup(args[2]);
  if(args.Length==3&&args[0]=="--external-stop"){try{new ExternalTools(args[2]).Stop(args[1]);return 0;}catch{return 1;}}
  if(args.Length==2&&args[0]=="--prepare-component")return Components.Command(args[1]);
  if(args.Length==3&&args[0]=="--import-verified-achievements") {try {new ProfileStore(Setup.DataFolder).Import("achievements",args[1],true);new ProfileStore(Setup.DataFolder).ExportAchievements(args[2]);return 0;}catch{return 1;}}
  if(args.Length==2&&(args[0]=="--export-achievements"||args[0]=="--export-achievement-records")) {try {new ProfileStore(Setup.DataFolder).ExportAchievements(args[1],args[0]=="--export-achievement-records");return 0;}catch{return 1;}}
  if(args.Length==3&&args[0]=="--achievement-context"){try{AchievementCatalog.ExportContext(Setup.DataFolder,int.Parse(args[1]),args[2]);return 0;}catch{return 1;}}
  if(args.Length==5&&args[0]=="--application-job")return ApplicationOperations.Job(args[1],args[2],args[3],args[4]);
  if(args.Contains("--ai-tool"))return CatheryneTools.Command();
  if(args.Contains("--self-test")) return Tests.Run(args.Contains("--english")?"en-US":"ko-KR");
  if(args.Contains("--diagnostics")){using(var writer=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)))writer.Write(new AppDiagnostics(Setup.DataFolder).Export());return 0;}
  if(args.Contains("--daily-agent"))return DailyAgent.Run();
  if(args.Length==2&&args[0]=="--story-integration-test")return StoryClientTests.Run(args[1]);
  // Let Windows use the same executable identity as existing pinned shortcuts.
  if(args.Contains("--notification"))NotificationActivation.Save(Setup.DataFolder,NotificationTarget.Parse(args[2]));
  bool first;
  using(var mutex=PrivateIpc.Mutex("Local\\GenshinCompanion"))
  using(var wake=PrivateIpc.Event("Local\\GenshinCompanion.Show",EventResetMode.AutoReset))
  using(var home=PrivateIpc.Event("Local\\GenshinCompanion.Home",EventResetMode.AutoReset))
  using(var play=PrivateIpc.Event("Local\\GenshinCompanion.Play",EventResetMode.AutoReset)) {
   try{first=mutex.WaitOne(0);}catch(AbandonedMutexException){first=true;}
   if(!first) { (args.Contains("--notification")?wake:args.Contains("--play")?play:args.Contains("--preview")?home:wake).Set(); return 0; }
   try {
    if(!args.Contains("--play")&&!args.Contains("--setup")&&new AppUpdateService(AppDomain.CurrentDomain.BaseDirectory,Setup.DataFolder).AutoApply())return 0;
    DailyAgent.Stop();GameDataCatalog.Start(Setup.DataFolder);
    AppDiagnostics.Record(DiagnosticEvent.Started);
    try{AppNotifications.Register();}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.NotificationFailure,error);}
    var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
    app.DispatcherUnhandledException+=(s,e)=>AppDiagnostics.Record(DiagnosticEvent.UnhandledFailure,e.Exception);
    var installation=Setup.FindExisting();
    var controller=new Launcher(installation,args.Contains("--settings"),args.Contains("--preview"),args.Contains("--play"),args.Contains("--setup"));
    if(args.Contains("--background"))controller.Window.ShowActivated=false;
    app.ShutdownMode=ShutdownMode.OnMainWindowClose;app.MainWindow=controller.Window;
    Task.Run(delegate { while(wake.WaitOne()) { app.Dispatcher.BeginInvoke(new Action(()=>{controller.Show();controller.OpenPendingNotification();})); } });
    Task.Run(delegate { while(home.WaitOne()) { app.Dispatcher.BeginInvoke(new Action(controller.ShowHome)); } });
    Task.Run(delegate { while(play.WaitOne()) { app.Dispatcher.BeginInvoke(new Action(controller.PlayRequested)); } });
    var jumps=new JumpList {ShowFrequentCategory=false,ShowRecentCategory=false};
    string executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GenshinLauncher.exe");
    string jumpIcon=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launcher.ico");if(!File.Exists(jumpIcon))jumpIcon=executable;
    jumps.JumpItems.Add(new JumpTask {Title=Locale.T("런처 열기"),Arguments="--preview",ApplicationPath=executable,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory,IconResourcePath=jumpIcon,IconResourceIndex=0});
    jumps.JumpItems.Add(new JumpTask {Title=Locale.T("게임 바로 시작"),Arguments="--play",ApplicationPath=executable,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory,IconResourcePath=jumpIcon,IconResourceIndex=0});
    JumpList.SetJumpList(app,jumps);
    jumps.Apply();
    controller.Window.Loaded+=(s,e)=>controller.OpenPendingNotification();
    app.Run(controller.Window);
    controller.Dispose();AppDiagnostics.Record(DiagnosticEvent.Stopped);if(!AppUpdateService.Applying)DailyAgent.StartIfEnabled();
   } catch(Exception e) { AppDiagnostics.Record(DiagnosticEvent.RuntimeFailure,e);MessageBox.Show(e.Message,Locale.T("원신 런처"),MessageBoxButton.OK,MessageBoxImage.Error); return 1; }finally{mutex.ReleaseMutex();}
  }
  return 0;
 }
}

internal sealed class ConfigStore {
 internal readonly string PathName;
 internal readonly JavaScriptSerializer Json=new JavaScriptSerializer();
 internal ConfigStore(string path) { PathName=path; }
 internal Dictionary<string,object> Read() { return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(PathName)); }
 internal void Save(Dictionary<string,object> changes) {
  var c=Read(); // Preserve metadata and unknown fields from the current file.
  var supported=UnlockerOptions.Defaults();
  foreach(var item in changes) {
   if(!supported.ContainsKey(item.Key))throw new ArgumentException("지원하지 않는 설정 항목: "+item.Key);
   c[item.Key]=item.Value;
  }
  UnlockerOptions.Validate(UnlockerOptions.WithDefaults(c));
  c["AutoStart"]=true; // Explicit game start always invokes the engine's auto-start.
  AtomicFile.Write(PathName,Json.Serialize(c));
 }
}

internal sealed class Launcher : IDisposable {
 [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 internal readonly Window Window;
 readonly string root=AppDomain.CurrentDomain.BaseDirectory;
 string engine;
 ConfigStore store;
 Installation installation;
 readonly SetupPanel setup;
 readonly WorkspaceHome workspace;
 internal readonly CompanionPanel companion;
 internal readonly UnlockerOptionsPanel options;
 readonly Forms.NotifyIcon tray;
 readonly CaptureController captures;
 readonly AppUpdatePanel appUpdates;
 readonly SettingsAutoSave settingsSave;
 bool bindingSettings;
 readonly DispatcherTimer timer=new DispatcherTimer();
 bool active,seenGame,autoClose,checking,launching,disposed,ticking;
 internal Func<ProcessState> processState=ProcessGuard.Inspect;
 ChannelService channel;
 UpdateService updater;
 Task updateTask;
 Dictionary<string,object> preferences=new Dictionary<string,object>();
 string preferencesPath;
 DateTime launched;
 Dictionary<string,object> config;
 TextBox FPS { get {return (TextBox)Window.FindName("FPS");} }
 T Get<T>(string name) where T:class {return Window.FindName(name) as T;}
 static readonly string[] NavigationPages={"Graphics","Story","Characters","Achievements","Daily","Settings"};
 static readonly Brush SelectedNavigation=new SolidColorBrush(Color.FromRgb(53,64,71));
 void SelectNavigation(string selected) {
  foreach(string name in NavigationPages)Get<Button>(name).Background=name==selected?SelectedNavigation:Brushes.Transparent;
  if(workspace!=null)workspace.Select(selected);
 }
 void OpenFeature(string page){
  if(page=="ExternalLinks"){ResourceLinks.Show(Window,companion.Details);return;}
  if(page=="Installation"&&(checking||launching))return;
  if(page=="Abyss"||page=="Stygian"){EndgamePanel.Show(Window,companion.Details,page=="Abyss"?"abyss":"stygian",workspace.PrepareQuestion);return;}
  if(page=="Theater"){TheaterPanel.Show(Window,companion.Details,()=>workspace.CurrentThread,workspace.PrepareTheater);return;}
  if(page=="Tasks"){workspace.ShowTasks();return;}
  if(page=="Capture"){CapturePanel.Show(Window,companion.Details,captures,file=>workspace.AttachFile(file,true));return;}
  if(page=="Mods"){companion.ShowMods();return;}
  if(page=="Installation"){setup.Open(installation,true);Tick();return;}
  if(page=="Graphics"||page=="SettingsPage"){var panel=Get<Border>(page=="Graphics"?"GraphicsPanel":"SettingsPanel");LauncherWindowLayout.Primary(Window,panel,false);if(page=="SettingsPage")RefreshChannel();DrawerMotion.Show(panel);}else companion.Show(page);
 }
 void HideFeatures(){companion.Hide();companion.Details.Hide();setup.Hide();PanelNavigation.CloseFeatures(Window);}
 void ClosePanels() {
  if(workspace!=null)workspace.HideMenu();HideFeatures();
  SelectNavigation(null);
 }
 static bool Within(DependencyObject child,DependencyObject parent){return PanelInteraction.Within(child,parent);}
 void Status(string text) {text=Locale.T(text);Get<TextBlock>("Status").Text=text;Get<TextBlock>("SettingsStatus").Text=text;Get<TextBlock>("GraphicsStatus").Text=text;}
 bool Busy {get {return ProcessGuard.Busy();}}
 internal Launcher(Installation candidate,bool settings,bool preview,bool play,bool forceSetup,bool connectAi=true) {
  Window=(Window)XamlReader.Parse(Locale.Xaml(File.ReadAllText(Path.Combine(root,"Main.xaml"))));
  PanelUi.ApplyGeometry(Window);Typography.Apply(Window,Locale.LanguageCode,null);PanelInteraction.Attach(Window);
  var area=SystemParameters.WorkArea;Window.Width=Math.Min(1240,Math.Max(1,area.Width-48));Window.Height=Math.Min(760,Math.Max(1,area.Height-48));
  WindowChrome.SetWindowChrome(Window,new WindowChrome{CaptionHeight=LauncherWindowLayout.HeaderHeight,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=PanelUi.Corners,UseAeroCaptionButtons=false});
  Window.SourceInitialized+=(s,e)=>{try{var hwnd=new System.Windows.Interop.WindowInteropHelper(Window).Handle;int preference=2;DwmSetWindowAttribute(hwnd,33,ref preference,sizeof(int));int dark=SystemParameters.HighContrast?0:1;DwmSetWindowAttribute(hwnd,20,ref dark,sizeof(int));if(!SystemParameters.HighContrast){var brush=((Border)Window.Content).Background as SolidColorBrush;if(brush!=null){int caption=brush.Color.R|(brush.Color.G<<8)|(brush.Color.B<<16);DwmSetWindowAttribute(hwnd,35,ref caption,sizeof(int));}}}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}};
  string icon=Path.Combine(root,"launcher.ico");
  Window.Icon=System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(icon),System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames.OrderByDescending(frame=>frame.PixelWidth).First();
  var iconPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"branding","launcher.png");if(File.Exists(iconPath)){var homeBitmap=new System.Windows.Media.Imaging.BitmapImage();homeBitmap.BeginInit();homeBitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;homeBitmap.DecodePixelWidth=128;homeBitmap.UriSource=new Uri(iconPath);homeBitmap.EndInit();homeBitmap.Freeze();Get<Image>("HomeIcon").Source=homeBitmap;RenderOptions.SetBitmapScalingMode(Get<Image>("HomeIcon"),BitmapScalingMode.HighQuality);}else Get<Image>("HomeIcon").Source=Window.Icon;
  string background=Path.Combine(root,"branding","background.png");
  if(!File.Exists(background))background=Path.Combine(root,"branding","background.jpg");
  if(!File.Exists(background))background=Path.Combine(root,"assets","background.png");
  if(File.Exists(background))Get<Image>("HeroArt").Source=new System.Windows.Media.Imaging.BitmapImage(new Uri(background));
  GameRequirement.Configure(Window,()=>Launch(false,GameLaunchPurpose.Automation));
  companion=new CompanionPanel(Window,()=>SelectNavigation(null));
  setup=new SetupPanel(Window,CompleteSetup,CancelSetup);
  options=new UnlockerOptionsPanel(Window,Status,Save);
  var languagePicker=Get<ComboBox>("Language");languagePicker.Items.Clear();foreach(string code in Locale.AvailableLanguages()){var item=new ComboBoxItem{Content=System.Globalization.CultureInfo.GetCultureInfo(code).NativeName,Tag=code};languagePicker.Items.Add(item);if(code==Locale.LanguageCode)languagePicker.SelectedItem=item;}languagePicker.SelectionChanged+=(s,e)=>{var item=languagePicker.SelectedItem as ComboBoxItem;if(item!=null)AppPreferences.Set("language",Convert.ToString(item.Tag));};
  Get<Button>("AiPreset").Click+=async(s,e)=>await options.ApplyAiPreset();
  Get<Button>("PersonalPreset").Click+=async(s,e)=>await options.RestorePersonalPreset();
  Get<Button>("SavePersonalPreset").Click+=async(s,e)=>await options.SavePersonalPreset();
  Get<Button>("WindowsHdrSettings").Click+=(s,e)=>Process.Start(new ProcessStartInfo("ms-settings:display") {UseShellExecute=true});
  Get<Button>("Graphics").Click+=(s,e)=>{if(installation==null||setup.BlocksNavigation)return;OpenFeature("Graphics");SelectNavigation("Graphics");};
  Get<Button>("Today").Click+=(s,e)=>{if(setup.BlocksNavigation)return;ClosePanels();companion.Show("Today");if(workspace!=null)workspace.Select("Today");};
  Get<ContentControl>("GraphicsHeading").Content=PanelUi.Heading(new TextBlock {Text=Locale.T("화면 설정"),FontSize=21,FontWeight=FontWeights.SemiBold},PanelUi.Help(Locale.T("게임 화면과 프레임을 조절합니다.")));
  Get<Button>("CloseGraphics").Click+=(s,e)=>DrawerMotion.Hide(Get<Border>("GraphicsPanel"));
  Get<Button>("Settings").Click+=(s,e)=>{if(installation==null||setup.BlocksNavigation)return;workspace.Show("설정");SelectNavigation("Settings");};
  Get<Button>("Home").Click+=(s,e)=>{if(!setup.BlocksNavigation)ClosePanels();};
  Get<Button>("CloseSettings").Click+=(s,e)=>DrawerMotion.Hide(Get<Border>("SettingsPanel"));
  Get<Button>("CopyDiagnostics").Click+=async(s,e)=>await CopyDiagnostics();
  Get<Button>("WindowsNotificationSettings").Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo("ms-settings:notifications"){UseShellExecute=true});}catch(Exception error){Status(error.Message);}};
  foreach(string name in new[]{"Story","Achievements","Characters","Daily"}) {string page=name;Get<Button>(name).Click+=(s,e)=>{OpenFeature(page);SelectNavigation(page);};}
  new LauncherWindowLayout(Window);
  Get<Button>("Play").Click+=async(s,e)=>await Launch();
  Get<Button>("CheckUpdate").Click+=async(s,e)=>await RefreshUpdate(true);
  Get<Button>("GoogleChannel").Click+=(s,e)=>SwitchChannel(true);
  Get<Button>("OriginalChannel").Click+=(s,e)=>SwitchChannel(false);
  Get<Button>("GameUpdate").Click+=(s,e)=>{
   try {
    string hoyo=Setup.FindHoYoPlay(Convert.ToString(config["GamePath"]));
    if(hoyo==null){var picker=new Microsoft.Win32.OpenFileDialog{Title=Locale.T("HoYoPlay launcher.exe 선택"),Filter="HoYoPlay|launcher.exe"};if(picker.ShowDialog(Window)!=true)return;hoyo=picker.FileName;}
    Process.Start(new ProcessStartInfo(hoyo){UseShellExecute=true});
   }
   catch(Exception error){Status(error.Message);}
  };
  tray=new Forms.NotifyIcon {Icon=new System.Drawing.Icon(icon),Text=Locale.T("원신 런처 · ")+FPS.Text+" FPS",Visible=false};
  tray.ContextMenuStrip=new Forms.ContextMenuStrip();
  tray.ContextMenuStrip.Items.Add(Locale.T("설정 열기"),null,(s,e)=>Show());
  tray.ContextMenuStrip.Items.Add(Locale.T("런처 닫기"),null,(s,e)=>Window.Close());
  tray.DoubleClick+=(s,e)=>Show();
  Window.StateChanged+=(s,e)=>{tray.Visible=Window.WindowState==WindowState.Minimized;};
  Window.Closing+=(s,e)=>{if(setup.Preparing)e.Cancel=true;};
  Window.PreviewMouseDown+=(s,e)=>{
   if(e.Handled||setup.BlocksNavigation)return;
   var source=e.OriginalSource as DependencyObject;
   var companionHost=Get<Border>("CompanionPage");var graphics=Get<Border>("GraphicsPanel");var settingsPanel=Get<Border>("SettingsPanel");
   bool open=Get<Border>("WorkspaceMenu").Visibility==Visibility.Visible||companionHost.Visibility==Visibility.Visible||graphics.Visibility==Visibility.Visible||settingsPanel.Visibility==Visibility.Visible||setup.IsOpen;
   if(open&&!Within(source,Get<Border>("WorkspaceHome"))&&!Within(source,Get<Button>("ToggleHomeChat"))&&!Within(source,Get<Grid>("TitleBar"))&&!Within(source,Get<Border>("WorkspaceMenu"))&&!Within(source,Get<Border>("DetailPanel"))&&!Within(source,companionHost)&&!Within(source,graphics)&&!Within(source,settingsPanel)&&!Within(source,Get<Border>("SetupPanel"))&&!Within(source,Get<Border>("NavigationRail")))ClosePanels();
  };
  captures=new CaptureController(Window,message=>{if(connectAi){var notice=AppNotifications.Current.Send(new AppNotification("capture:"+Guid.NewGuid().ToString("N"),Locale.T("캡처"),message,new NotificationTarget("Capture")));}});
  workspace=new WorkspaceHome(Window,ClosePanels,companion.Details,OpenFeature,connectAi);if(connectAi){workspace.InputNotification=request=>{var notice=AppNotifications.Current.Send(AppNotification.Question(request));};workspace.InputResolved=request=>{var clear=AppNotifications.Current.Clear(AppNotification.QuestionKey(request));};workspace.ExecutionNotification=report=>AppNotifications.Current.ObserveController(report);}workspace.CaptureRequested=async()=>{var shot=await captures.Capture();return shot==null?null:shot.Path;};companion.Ask=workspace.PrepareQuestion;captures.Captured+=workspace.TheaterCapture;workspace.TheaterCaptureRequested=()=>captures.Capture();workspace.TaskUpdated+=companion.UpdateTask;workspace.TaskUpdated+=task=>{if(task.State=="completed"&&(task.Action??"").StartsWith("display.")&&!settingsSave.Pending){try{options.RefreshPresets();}catch(Exception error){Status(error.Message);}}};
  new GameEnvironmentPanel(Window,Get<StackPanel>("GameEnvironmentSection"),Setup.DataFolder);
  appUpdates=new AppUpdatePanel(Get<StackPanel>("AppUpdateSection"),root,Setup.DataFolder,()=>!setup.Preparing&&!launching&&!workspace.UpdateBlocked,()=>Window.Close());
  PanelNavigation.Reframe(Window,"GraphicsPanel","화면 설정",()=>DrawerMotion.Hide(Get<Border>("GraphicsPanel")));
  PanelNavigation.Reframe(Window,"SettingsPanel","일반 설정",()=>DrawerMotion.Hide(Get<Border>("SettingsPanel")));
  settingsSave=new SettingsAutoSave(()=>{if(installation!=null)Save();});
  settingsSave.Attach(Get<Border>("GraphicsPanel"),()=>Window.IsLoaded&&installation!=null&&!bindingSettings&&!options.Loading);
  settingsSave.Attach(Get<Border>("SettingsPanel"),()=>Window.IsLoaded&&installation!=null&&!bindingSettings&&!options.Loading);
  Window.Closing+=(s,e)=>settingsSave.Flush();
  if(Setup.Ready(candidate))BindInstallation(candidate);
  timer.Interval=TimeSpan.FromSeconds(1); timer.Tick+=async(s,e)=>await TickAsync(); timer.Start();
  Window.Activated+=(s,e)=>RefreshChannel();
  Window.Loaded+=async(s,e)=> {
   var appMaintenance=appUpdates.Start();
   Tick();
   RefreshChannel();
   if(installation==null||forceSetup){OpenSetup(candidate);return;}
   if(!Busy){var maintenance=Task.Run(()=>Components.Maintain());}
   if((play || (!settings && !preview && Get<CheckBox>("AutoStart").IsChecked==true)) && !Busy) await Launch();
   else { if(settings){workspace.Show("설정");SelectNavigation("Settings");} if(Get<CheckBox>("UseUnlockerLaunch").IsChecked==true)await RefreshUpdate(Get<CheckBox>("AutoUpdate").IsChecked==true); }
  };
 }
 void BindInstallation(Installation value) {
  bindingSettings=true;try{
  installation=value;engine=value.Engine;store=new ConfigStore(value.Config);config=store.Read();
  Typography.Apply(Window,Locale.LanguageCode,Convert.ToString(config["GamePath"]));
  channel=new ChannelService(Path.GetDirectoryName(Convert.ToString(config["GamePath"])));
  updater=new UpdateService(engine,Path.Combine(value.Data,"unlocker-state.json"),ProcessGuard.Busy);
  preferencesPath=Path.Combine(value.Data,"launcher-settings.json");
  preferences=File.Exists(preferencesPath)?store.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(preferencesPath)):new Dictionary<string,object>();
  options.Load(config);options.RefreshPresets();
  Get<CheckBox>("AutoStart").IsChecked=Convert.ToBoolean(config["AutoStart"]);
  if(preferences.ContainsKey("LaunchOnOpen"))Get<CheckBox>("AutoStart").IsChecked=Convert.ToBoolean(preferences["LaunchOnOpen"]);
  Get<CheckBox>("UseUnlockerLaunch").IsChecked=!preferences.ContainsKey("UseUnlockerLaunch")||Convert.ToBoolean(preferences["UseUnlockerLaunch"]);
  Get<CheckBox>("AutoUpdate").IsChecked=!preferences.ContainsKey("AutoUpdateUnlocker")||Convert.ToBoolean(preferences["AutoUpdateUnlocker"]);
  autoClose=Convert.ToBoolean(config["AutoClose"]);
  Get<TextBlock>("GamePath").Text=Convert.ToString(config["GamePath"]);Get<TextBlock>("HeroFPS").Text=FPS.Text;
  Get<TextBlock>("Version").Text=Locale.T("설치됨");
  tray.Text=Locale.T("원신 런처 · ")+FPS.Text+" FPS";RefreshChannel();
  }finally{bindingSettings=false;}
 }
 void OpenSetup(Installation candidate) {
  ClosePanels();
  setup.Open(candidate,installation!=null);Tick();
 }
 void CancelSetup(){setup.Hide();workspace.Show("설정");SelectNavigation("Settings");Tick();}
 void CompleteSetup(Installation value){BindInstallation(value);setup.Hide();SelectNavigation(null);Status("준비 완료");Tick();}
 internal void Show() {companion.Hide();DrawerMotion.Hide(Get<Border>("GraphicsPanel"));Window.Show();Window.WindowState=WindowState.Normal;Window.Activate();RefreshChannel();if(installation!=null&&!setup.BlocksNavigation){workspace.Show("설정");SelectNavigation("Settings");}}
 internal async void OpenPendingNotification(){try{var target=await Task.Run(()=>NotificationActivation.Take(Setup.DataFolder));if(target==null)return;ShowHome();if(target.Page=="Chat")workspace.OpenNotification(target.Thread);else OpenFeature(target.Page);}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.NotificationFailure,error);}}
 internal void ShowHome() {Show();if(!setup.BlocksNavigation)ClosePanels();}
 internal async void PlayRequested() {if(installation==null||setup.BlocksNavigation){Show();return;}if(updateTask!=null)await updateTask;await Launch();}
 void RefreshChannel(){if(channel!=null)Get<TextBlock>("Channel").Text=channel.Available?Locale.T(channel.Current()):Locale.T("전환 프로필 없음");}
 void SwitchChannel(bool google) {
  try {if(checking||launching)throw new InvalidOperationException(Locale.T("진행 중인 작업이 끝난 뒤 전환해 주세요."));channel.Switch(google,ProcessGuard.Busy);RefreshChannel();Status(Locale.Format("{0}으로 전환됨",google?"Google Play":Locale.T("원본")));}
  catch(Exception error){Status(error.Message);}
 }
 bool Save() {
  try {
   if(Busy) throw new InvalidOperationException(Locale.T("게임 실행이 끝난 뒤 설정을 저장해 주세요."));
   var edits=options.Read();int fps=(int)edits["FPSTarget"];
   // LaunchOnOpen belongs to this frontend. Engine AutoStart must remain true so
   // the explicit Play button always starts the game, even in manual frontend mode.
   preferences=LauncherOperations.SaveSettings(installation,edits,new Dictionary<string,object>{
    {"LaunchOnOpen",Get<CheckBox>("AutoStart").IsChecked==true},
    {"UseUnlockerLaunch",Get<CheckBox>("UseUnlockerLaunch").IsChecked==true},
    {"AutoUpdateUnlocker",Get<CheckBox>("AutoUpdate").IsChecked==true}});
   config=store.Read();options.RefreshPresets();settingsSignature=SettingsSignature(); autoClose=Convert.ToBoolean(config["AutoClose"]);
   Get<TextBlock>("HeroFPS").Text=fps.ToString();
   tray.Text=Locale.T("원신 런처 · ")+fps+" FPS";
   Status(fps>120?"120 FPS 초과 시 나선 오류가 재발할 수 있습니다.":"저장됨");
   return true;
  } catch(Exception e) { Status(e.Message);return false; }
 }
 async Task<bool> Launch(bool minimize=true,GameLaunchPurpose purpose=GameLaunchPurpose.Player) {
  if(installation==null||setup.BlocksNavigation||launching||checking||!Save()) return false;
  bool useUnlocker=Get<CheckBox>("UseUnlockerLaunch").IsChecked==true;
  launching=true;Tick();
  try {
   bool modsEnabled=new ModManager(installation.Data).Enabled;
  if(useUnlocker&&!modsEnabled&&!Setup.HasDesktopRuntime()) {
   ShowHome();
   if(MessageBox.Show(Window,Locale.T("언락커 실행에 .NET 8 Desktop Runtime(x64)이 필요합니다. Microsoft 다운로드 페이지를 열까요?"),Locale.T("런타임 설치 필요"),MessageBoxButton.YesNo,MessageBoxImage.Information)==MessageBoxResult.Yes)
    Process.Start(new ProcessStartInfo("https://dotnet.microsoft.com/en-us/download/dotnet/8.0"){UseShellExecute=true});
   return false;
  }

   if(useUnlocker&&!modsEnabled)await RefreshUpdate(Get<CheckBox>("AutoUpdate").IsChecked==true);
   if(Busy) {Status("이미 실행 중입니다.");return false;}
   if(!File.Exists(Convert.ToString(config["GamePath"]))) throw new FileNotFoundException(Locale.T("원신 실행 파일을 찾을 수 없습니다."));
   await Task.Run(()=>LauncherOperations.Start(installation,config,useUnlocker,purpose,value=>Window.Dispatcher.Invoke(new Action(()=>Status(value.Stage)))));
   active=true;seenGame=false;launched=DateTime.UtcNow;
   Status("게임 실행 중…");
   if(minimize)Window.WindowState=WindowState.Minimized;
   return true;
  } catch(System.ComponentModel.Win32Exception e) {Status(e.NativeErrorCode==1223?"실행이 취소되었습니다.":e.Message);}
    catch(Exception e) {Status(e.Message);}
    finally {launching=false;Tick();}
  return false;
 }
 string settingsSignature;
 string SettingsSignature(){return File.GetLastWriteTimeUtc(installation.Config).Ticks+":"+File.GetLastWriteTimeUtc(preferencesPath).Ticks;}
 internal async Task TickAsync(){if(disposed||ticking)return;ticking=true;try{var state=await Task.Run(processState);if(!disposed)ApplyProcessState(state);}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.RuntimeFailure,error);}finally{ticking=false;}}
 void Tick(){if(!disposed)ApplyProcessState(processState());}
 void ApplyProcessState(ProcessState state) {
  if(installation!=null&&!bindingSettings&&!launching&&!checking&&!settingsSave.Pending){string stamp=SettingsSignature();if(settingsSignature!=stamp){settingsSignature=stamp;BindInstallation(installation);}}
  bool game=state.Game;
  Get<Button>("Play").Content=Locale.T(game?"게임 중":"게임 시작");
  bool busy=state.Busy||checking||launching||setup.BlocksNavigation||installation==null;
  Get<Button>("Home").IsEnabled=!setup.BlocksNavigation;Get<Button>("Settings").IsEnabled=installation!=null&&!setup.BlocksNavigation;
  FPS.IsEnabled=!busy; Get<StackPanel>("Options").IsEnabled=!busy;
  options.SetEditable(!busy);
  Get<Button>("Graphics").IsEnabled=installation!=null&&!setup.BlocksNavigation;Get<Button>("Play").IsEnabled=!busy;
  Get<Button>("GoogleChannel").IsEnabled=!busy&&channel!=null&&channel.Available;Get<Button>("OriginalChannel").IsEnabled=!busy&&channel!=null&&channel.Available;
  Get<Button>("GameUpdate").IsEnabled=!busy;
  if(!active)return;
  if(game){seenGame=true;if(Get<TextBlock>("Status").Text==Locale.T("게임 실행 중…"))Status("");}
  else if(seenGame) {active=false;if(autoClose) Window.Close();else {Show();Status("게임이 종료되었습니다.");}}
  else if(DateTime.UtcNow-launched>TimeSpan.FromSeconds(60)) {active=false;Show();Status("게임 시작을 확인하지 못했습니다. 실행 상태를 확인해 주세요.");}
 }
 Task RefreshUpdate(bool install) {
  if(updateTask!=null&&!updateTask.IsCompleted)return updateTask;
  updateTask=RunUpdate(install);return updateTask;
 }
 async Task RunUpdate(bool install) {
  checking=true;Get<Button>("CheckUpdate").IsEnabled=false;Tick();
  Get<TextBlock>("Version").Text=Locale.T("프레임 기능 확인 중…");
  try {Get<TextBlock>("Version").Text=await Task.Run(()=>updater.Check(install));}
  catch(Exception error) {Get<TextBlock>("Version").Text=Locale.T("업데이트 보류");Get<TextBlock>("Version").ToolTip=error.Message;}
  finally{checking=false;Get<Button>("CheckUpdate").IsEnabled=true;Tick();}
 }
 internal async Task CopyDiagnostics(Action<string> copy=null) {
  var button=Get<Button>("CopyDiagnostics");var status=Get<TextBlock>("SettingsStatus");button.IsEnabled=false;
  try {string report=await Task.Run(()=>new AppDiagnostics(Setup.DataFolder).Export());if(copy!=null)copy(report);else Clipboard.SetText(report);status.Text=Locale.T("복사됨");}
  catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.DiagnosticExportFailure,error);status.Text=Locale.T("다시 복사해 주세요.");}
  finally{button.IsEnabled=true;}
 }
 public void Dispose(){if(disposed)return;disposed=true;appUpdates.Dispose();workspace.Dispose();captures.Dispose();settingsSave.Dispose();setup.Hide();timer.Stop();tray.Visible=false;tray.Dispose();}
}
internal static class Tests {
 internal static int Run(string language="ko-KR") {
  // All automated paths, including AI actions, use fake HDR state; never toggle the user's display.
  bool? testAutoHdr=true;WindowsAutoHdr.TestBackend=(write,enabled)=>{if(write)testAutoHdr=enabled;return new WindowsAutoHdrState{Supported=true,Enabled=testAutoHdr};};WindowsAutoHdr.Test();
  var testHdr=new Dictionary<int,bool>();WindowsHdr.TestBackend=(monitor,enabled,expected)=>{string identity="selftest-"+monitor;if(expected!=null&&expected!=identity)throw new InvalidOperationException("Fixture monitor changed");if(!testHdr.ContainsKey(monitor))testHdr[monitor]=true;if(enabled.HasValue)testHdr[monitor]=enabled.Value;return new WindowsHdrState{Monitor=monitor,Identity=identity,Supported=true,Enabled=testHdr[monitor]};};
  // Offscreen WPF fixtures need a deterministic compositor; production keeps its renderer.
  RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
  string priorData=Environment.GetEnvironmentVariable("CATHERYNE_TOOL_DATA");string testData=Path.Combine(Path.GetTempPath(),"catheryne-selftest-"+Guid.NewGuid().ToString("N"));Environment.SetEnvironmentVariable("CATHERYNE_TOOL_DATA",testData);
  string path=Path.Combine(Path.GetTempPath(),"genshin-launcher-test-"+Guid.NewGuid()+".json");
  try {
   Directory.CreateDirectory(testData);File.WriteAllText(Path.Combine(testData,"settings.json"),CatheryneTools.Json().Serialize(new{language=language}));LocalizationTests.Run();
   File.WriteAllText(path,"{\"FPSTarget\":120,\"AutoStart\":true,\"AutoClose\":true,\"Fullscreen\":true,\"UnknownFutureField\":{\"value\":42},\"DllList\":[]}");
   File.SetAttributes(path,FileAttributes.Hidden);
   var store=new ConfigStore(path);store.Save(UnlockerOptions.Defaults());
   var c=store.Read();if(!c.ContainsKey("UnknownFutureField") || !(bool)c["AutoStart"] || (int)c["FPSTarget"]!=120) throw new Exception("Roundtrip failed");
   if((File.GetAttributes(path)&FileAttributes.Hidden)==0)throw new Exception("Hidden attribute lost");
   bool rejected=false;try{store.Save(new Dictionary<string,object>{{"FPSTarget",0}});}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Invalid FPS accepted");
   AiPersistenceTests.Run();NotificationTests.Run();ArtifactReviewTests.Run();AppUpdateTests.Run();AppUpdateServiceTests.Run();GameDataTests.Run();UiConsistencyTests.Run();DiagnosticsTests.Run();PerformanceTests.Run();SharedReadTests.Run();RuntimePerformanceTests.Run();UiReadTests.Run();EndgameTests.Run();TheaterTests.Run();CaptureTests.Run();ModManagerTests.Run();PanelInteractionTests.Run();HoyoAccountTests.Run();MaterialInventoryTests.Run();CollectionHistoryTests.Run();ChatAttachmentTests.Run();PrimogemTests.Run();RedemptionTests.Run();AiToolTests.Run();ServiceTests.Run();LocalDataTests.Run();ComponentTests.Run();GoalPlanningTests.Run();
   string profileRoot=Path.Combine(Path.GetTempPath(),"companion-profile-test-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(profileRoot);
   try {
    var profiles=new ProfileStore(profileRoot);
    string samplePath=Path.Combine(profileRoot,"sample.json");
    File.WriteAllText(samplePath,"{\"format\":\"GOOD\",\"characters\":[],\"weapons\":[]}");
    string summary=profiles.Import("account",samplePath);
    if(!summary.Contains(Locale.T("미수집")))throw new Exception("Missing data treated as zero");
    profiles.Import("account",samplePath);
    if(Directory.GetFiles(Path.Combine(profileRoot,"profiles","default","account","snapshots")).Length!=1)throw new Exception("Duplicate snapshot");
    string before=profiles.Summary("account");File.WriteAllText(samplePath,"{}");
    bool invalid=false;try{profiles.Import("account",samplePath);}catch(InvalidDataException){invalid=true;}
    if(!invalid||profiles.Summary("account")!=before)throw new Exception("Invalid import replaced current data");
    File.WriteAllText(samplePath,"{\"Version\":1,\"Data\":{\"80032\":4}}");
    if(!profiles.Import("achievements",samplePath).Contains(Locale.T("검증 대기")))throw new Exception("Unverified scan promoted to verified");
    profiles.Import("achievements",samplePath,true);
    profiles.Import("achievements",samplePath);
    File.WriteAllText(samplePath,"{\"Version\":1,\"Data\":{\"80033\":4}}");profiles.Import("achievements",samplePath);
    string canonical=Path.Combine(profileRoot,"canonical.json");profiles.ExportAchievements(canonical,true);profiles.Import("achievements",canonical);
    string export=Path.Combine(profileRoot,"export.json");new ProfileStore(profileRoot).ExportAchievements(export);
    var exported=File.ReadAllText(export);if(!exported.Contains("80032")||exported.Contains("80033"))throw new Exception("Verified merge/export failed");
    File.WriteAllText(samplePath,"{\"Version\":1,\"Data\":{\"80034\":4,\"80032\":99}}");
    bool conflict=false;try{profiles.Import("achievements",samplePath,true);}catch(InvalidDataException){conflict=true;}
    new ProfileStore(profileRoot).ExportAchievements(export);if(!conflict||File.ReadAllText(export)!=exported)throw new Exception("Achievement transaction rollback failed");
   } finally {Directory.Delete(profileRoot,true);}

   // A new user must be able to create the normal shell before any engine exists.
   // Raise Loaded without showing a test window or modifying installation data.
   using(var shell=new Launcher(null,false,true,false,false,connectAi:false)) {
    ModEngineTests.LayoutContracts(shell.Window);
    var shortAction=PanelUi.Button(Locale.T("우선 처리"));var longAction=PanelUi.Button(Locale.T("다시 계획에 포함"));
    var actionRow=PanelUi.Actions(shortAction,longAction);actionRow.Resources=shell.Window.Resources;
    actionRow.Measure(new Size(220,double.PositiveInfinity));actionRow.Arrange(new Rect(0,0,220,actionRow.DesiredSize.Height));
    if(shortAction.TranslatePoint(new Point(),actionRow).Y>=longAction.TranslatePoint(new Point(),actionRow).Y||longAction.ActualWidth<180)throw new Exception("Narrow actions must retain readable labels on separate rows");
    actionRow.Measure(new Size(600,double.PositiveInfinity));actionRow.Arrange(new Rect(0,0,600,actionRow.DesiredSize.Height));
    if(Math.Abs(shortAction.TranslatePoint(new Point(),actionRow).Y-longAction.TranslatePoint(new Point(),actionRow).Y)>0.1)throw new Exception("Wide actions must share a row");
    var historyButton=((StackPanel)((Button)shell.Window.FindName("Story")).Parent).Children.OfType<Button>().Single(x=>Convert.ToString(x.ToolTip)==Locale.T("AI"));
    ((Button)shell.Window.FindName("Home")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if(historyButton.Background!=Brushes.Transparent)throw new Exception("Home must not select history navigation");
    var workspace=(Border)shell.Window.FindName("WorkspaceHome");if(workspace.Margin.Bottom!=84)throw new Exception("Composer bottom spacing regression");
    shell.companion.Show("Story");
    if(shell.companion.Current!="Story")throw new Exception("Navigation failed");
    var scanButton=(Button)typeof(CompanionPanel).GetField("refreshStart",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(shell.companion);
    var externalScan=new AiTaskRecord{Id="external-scan-test",Action="scanner",Operation="account",State="running",Reason="collecting",Started=DateTime.UtcNow.ToString("o")};
    shell.companion.UpdateTask(externalScan);shell.companion.Show("Characters");shell.companion.ShowCollection();
    if(Convert.ToString(scanButton.Content)!=Locale.T("중단"))throw new Exception("Externally started scan is not stoppable from collection UI");
    shell.companion.Show("Achievements");shell.companion.ShowCollection();
    if(Convert.ToString(scanButton.Content)!=Locale.T("중단"))throw new Exception("Navigation lost the shared running scan");
    shell.companion.UpdateTask(new AiTaskRecord{Id=externalScan.Id,Action="scanner",Operation="account",State="cancelled",Reason="stopped",Started=externalScan.Started});
    if(Convert.ToString(scanButton.Content)!=Locale.T("최신화 시작"))throw new Exception("Terminal scan did not restore start control");
    shell.companion.Show("Collection");shell.companion.Show("Characters");shell.companion.Show("Collection");shell.companion.Show("Achievements");shell.companion.Show("Characters");shell.companion.Hide();
    if(((Border)shell.Window.FindName("CompanionPage")).Visibility!=Visibility.Collapsed)throw new Exception("Navigation did not return home");
    if(shell.Window.FindName("ChangeInstallation")!=null)throw new Exception("Duplicate installation entry in general settings");
    ((SetupPanel)typeof(Launcher).GetField("setup",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(shell)).Open(null,true);typeof(Launcher).GetMethod("OpenFeature",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(shell,new object[]{"Story"});
    var frame=new System.Windows.Threading.DispatcherFrame();var navigationWait=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};navigationWait.Tick+=(sender,args)=>{navigationWait.Stop();frame.Continue=false;};navigationWait.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
    if(((Border)shell.Window.FindName("SetupPanel")).Visibility!=Visibility.Collapsed)throw new Exception("Installation panel survived feature navigation");
    shell.companion.Show("Daily");
    shell.companion.Details.Show("ai-tasks","작업 기록",new StackPanel());
    if(((Border)shell.Window.FindName("CompanionPage")).Visibility!=Visibility.Collapsed||((Border)shell.Window.FindName("DetailPanel")).Visibility!=Visibility.Visible)throw new Exception("Daily obscures task history");
    var updated=new TextBlock{Text="updated"};var detailHost=(Border)shell.Window.FindName("DetailPanel");var shellBefore=detailHost.Child;shell.companion.Details.Show("ai-tasks","작업 기록",updated);
    if(!object.ReferenceEquals(shellBefore,detailHost.Child)||updated.Parent==null)throw new Exception("Same route must update content without replacing shell");
    var calendarCheck=new CalendarPanel(shell.companion.Details);
    typeof(CalendarPanel).GetField("menu",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(calendarCheck,shell.Window.FindName("WorkspaceMenu"));
    shell.companion.Details.Show("calendar-add","일정 추가",new TextBox());
    typeof(CalendarPanel).GetMethod("Render",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(calendarCheck,new object[]{false});
    if(!shell.companion.Details.IsShowing("calendar-add"))throw new Exception("Calendar refresh replaced editor");
    shell.companion.Details.Hide();
    typeof(CalendarPanel).GetMethod("Render",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(calendarCheck,new object[]{false});
    if(shell.companion.Details.IsShowing("calendar-day"))throw new Exception("Calendar refresh reopened closed detail");
    foreach(string from in new[]{"CompanionPage","GraphicsPanel","SettingsPanel","SetupPanel","DetailPanel"})foreach(string to in new[]{"CompanionPage","GraphicsPanel","SettingsPanel","SetupPanel","DetailPanel"}){
     var first=(Border)shell.Window.FindName(from);var next=(Border)shell.Window.FindName(to);LauncherWindowLayout.Primary(shell.Window,first,false);DrawerMotion.Show(first);DrawerMotion.Hide(first);DrawerMotion.Show(next);
     foreach(string name in PanelNavigation.Names){var candidateSurface=shell.Window.FindName(name) as Border;if(candidateSurface!=null&&!PanelNavigation.Primary(candidateSurface)&&candidateSurface!=next&&candidateSurface.Visibility!=Visibility.Collapsed)throw new Exception("Nonexclusive navigation "+from+" -> "+to);}
    }
    shell.companion.Show("Story");
    var setupHost=(Border)shell.Window.FindName("SetupPanel");var featureHost=(Border)shell.Window.FindName("CompanionPage");
    if(setupHost.Margin!=featureHost.Margin||setupHost.HorizontalAlignment!=featureHost.HorizontalAlignment||!double.IsNaN(setupHost.Width))throw new Exception("Installation panel geometry diverged");
    ((Button)shell.Window.FindName("Home")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    var menuWorkspace=(WorkspaceHome)typeof(Launcher).GetField("workspace",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(shell);
    menuWorkspace.VerifyQuestionConversation();menuWorkspace.VerifyTaskConversation();PerformanceTests.TaskProjection(menuWorkspace);
    shell.Window.ShowInTaskbar=false;shell.Window.ShowActivated=false;shell.Window.Left=-10000;shell.Window.Top=-10000;shell.Window.Show();
    TheaterPanel.Show(shell.Window,shell.companion.Details,()=>null,(question,thread)=>{},false,Environment.GetEnvironmentVariable("CATHERYNE_THEATER_UI_DATA"));
    if(!shell.companion.Details.IsShowing("Theater"))throw new Exception("Theater page did not open");
    var theaterFrame=new System.Windows.Threading.DispatcherFrame();var theaterWait=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};theaterWait.Tick+=(sender,args)=>{theaterWait.Stop();theaterFrame.Continue=false;};theaterWait.Start();System.Windows.Threading.Dispatcher.PushFrame(theaterFrame);
    shell.Window.Measure(new Size(1240,760));shell.Window.Arrange(new Rect(0,0,1240,760));shell.Window.UpdateLayout();
    var theaterImage=new System.Windows.Media.Imaging.RenderTargetBitmap(1240,760,96,96,PixelFormats.Pbgra32);theaterImage.Render(shell.Window);var theaterPng=new System.Windows.Media.Imaging.PngBitmapEncoder();theaterPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(theaterImage));using(var output=File.Create(Path.Combine(Path.GetTempPath(),"catheryne-theater-ui.png")))theaterPng.Save(output);ChatLayoutTests.Run(shell.Window,menuWorkspace);shell.Window.Hide();shell.companion.Details.Hide();
    DiagnosticsTests.Settings(shell);AppUpdateServiceTests.Ui(shell.Window);
    EndgameTests.Render(shell.Window,shell.companion.Details);
    TheaterTaskTests.Render(shell.Window);
    var messagesField=typeof(WorkspaceHome).GetField("hasMessages",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    var menuGroupField=typeof(WorkspaceHome).GetField("menuGroup",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    foreach(bool existingChat in new[]{false,true}){
     messagesField.SetValue(menuWorkspace,existingChat);
     foreach(string route in new[]{"내 계정","플레이","화면·성능","캘린더","설정","내 계정"}){
      menuWorkspace.Show(route);
      var menuSurface=(Border)shell.Window.FindName("WorkspaceMenu");
      if(menuSurface.Visibility!=Visibility.Visible||menuSurface.Child==null||!Equals(menuGroupField.GetValue(menuWorkspace),route))throw new Exception("Primary menu failed to open: "+route);
     }
    }
    foreach(string detailRoute in new[]{"Characters","Daily","Primogems"}){
     historyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     shell.companion.Show(detailRoute);
     historyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     if(!Equals(menuGroupField.GetValue(menuWorkspace),"AI")||((Border)shell.Window.FindName("WorkspaceMenu")).Visibility!=Visibility.Visible||((Border)shell.Window.FindName("CompanionPage")).Visibility!=Visibility.Collapsed)throw new Exception("Chat navigation blocked after "+detailRoute);
    }
    messagesField.SetValue(menuWorkspace,false);menuWorkspace.Show("AI");
    var expanding=new Expander{Style=(Style)shell.Window.FindResource("OptionsExpander"),Content=new Border{Height=1600},IsExpanded=true};
    expanding.ApplyTemplate();expanding.Measure(new Size(600,double.PositiveInfinity));
    if(expanding.DesiredSize.Height<1600)throw new Exception("Shared expander clips long content");
    expanding.IsExpanded=false;expanding.InvalidateMeasure();expanding.UpdateLayout();expanding.Measure(new Size(600,double.PositiveInfinity));
    if(expanding.DesiredSize.Height>=1600)throw new Exception("Shared expander did not collapse");
    var reuse=new DetailPanel(shell.Window);var reuseBody=new StackPanel();reuse.Show("calendar-test","첫 날짜",reuseBody);var surface=((Border)shell.Window.FindName("DetailPanel")).Child;reuse.Show("calendar-test","다음 날짜",reuseBody);if(!ReferenceEquals(surface,((Border)shell.Window.FindName("DetailPanel")).Child))throw new Exception("Detail refresh recreated its surface");reuse.Hide();
    var panel=shell.options;
    var sample=UnlockerOptions.Defaults();sample["Fullscreen"]=false;sample["PopupWindow"]=true;sample["UseCustomRes"]=true;sample["CustomResX"]=2560;sample["DllList"]=new[]{"b.dll","a.dll"};
    panel.Load(sample);panel.SetEditable(true);
    if(store.Json.Serialize(sample)!=store.Json.Serialize(panel.Read()))throw new Exception("Options UI roundtrip failed");
    ((ComboBox)shell.Window.FindName("WindowMode")).SelectedIndex=2;
    if(Convert.ToBoolean(panel.Read()["PopupWindow"])||!Convert.ToBoolean(panel.Read()["Fullscreen"]))throw new Exception("Window mode conflict in UI");
    ((ComboBox)shell.Window.FindName("ResolutionMode")).SelectedIndex=0;
    if(((TextBox)shell.Window.FindName("CustomResX")).IsEnabled)throw new Exception("Resolution dependency failed");
    shell.Window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
    if(((Border)shell.Window.FindName("SetupPanel")).Visibility!=Visibility.Visible)throw new Exception("First-run panel was not shown in the launcher");
    if(((Expander)shell.Window.FindName("SetupAdvanced")).IsExpanded)throw new Exception("Advanced setup must be collapsed by default");
    if(((Border)shell.Window.FindName("SettingsPanel")).Visibility!=Visibility.Collapsed)throw new Exception("Settings overlap first-run setup");
    if(((Button)shell.Window.FindName("Play")).IsEnabled)throw new Exception("Game start enabled before setup");
    shell.Window.Close();
   }
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"PASS: all editable option roundtrips, metadata preservation, invalid input rejection, x64 DLL checks, UI window-mode/resolution dependencies, first-run shell; config roundtrip, hidden attributes, FPS validation; channel-only switch and rollback, version preservation, missing/duplicate fields, busy guards; update URL/hash checks, check-only, corrupt download cleanup, locked engine preservation, atomic replacement, backup, downgrade prevention");return 0;
  }catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"FAIL: "+e);return 1;}
  finally{Environment.SetEnvironmentVariable("CATHERYNE_TOOL_DATA",priorData);if(Directory.Exists(testData))Directory.Delete(testData,true);if(File.Exists(path)){File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);}}
 }
}


