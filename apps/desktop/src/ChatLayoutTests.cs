using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Linq;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Threading.Tasks;

internal static class ChatLayoutTests {
 static void Wait(int milliseconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 static void Check(bool value,string message){if(!value)throw new Exception("Chat layout: "+message);}
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
 static IntPtr Packed(Point point){return new IntPtr(((long)((int)point.Y&65535)<<16)|(uint)((int)point.X&65535));}
 static FieldInfo Field(Type type,string name){return type.GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);}
 static void Capture(FrameworkElement surface,string name){
  var drawing=new DrawingVisual();using(var canvas=drawing.RenderOpen())canvas.DrawRectangle(new VisualBrush(surface),null,new Rect(0,0,surface.ActualWidth,surface.ActualHeight));
  var bitmap=new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(Path.GetTempPath(),name+".png")))png.Save(output);
 }
 static System.Collections.Generic.Dictionary<string,object> Model(string id,string name,bool reasoning=false){return CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(new{model=id,displayName=name,isDefault=true,defaultReasoningEffort=reasoning?"high":"",supportedReasoningEfforts=reasoning?new[]{new{reasoningEffort="low"},new{reasoningEffort="high"},new{reasoningEffort="xhigh"}}:new object[0]})));}
 static void Await(Task task){for(int i=0;i<500&&!task.IsCompleted;i++)Wait(10);Check(task.IsCompleted,"async fixture completes");task.GetAwaiter().GetResult();}
 static void CatalogCache(){
  int reads=0;var pending=new TaskCompletionSource<CodexChat.ModelCatalog>();
  using(var host=new CodexChat(Setup.DataFolder,readCatalog:kind=>{System.Threading.Interlocked.Increment(ref reads);return pending.Task;})){
   var one=host.ProviderModels(AiProviders.Claude);var two=host.ProviderModels(AiProviders.Claude);Check(ReferenceEquals(one,two)&&!one.IsCompleted,"catalog requests share their in-flight task");
   pending.SetResult(new CodexChat.ModelCatalog{Connected=true,Error="fixture transient failure"});Await(one);Await(host.ProviderModels(AiProviders.Claude));Check(reads==1,"error retry is bounded");
   var times=(System.Collections.Generic.Dictionary<string,DateTime>)Field(typeof(CodexChat),"catalogTimes").GetValue(host);times[AiProviders.Claude]=DateTime.UtcNow.AddMinutes(-1);
   pending=new TaskCompletionSource<CodexChat.ModelCatalog>();pending.SetResult(new CodexChat.ModelCatalog{Connected=true,Models=new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,object>>{Model("sonnet","Claude Sonnet")}});
   var recovered=host.ProviderModels(AiProviders.Claude);Await(recovered);Check(reads==2&&recovered.Result.Models.Count==1&&recovered.Result.Error==null,"expired transient errors recover without logout");
   host.InvalidateModels(AiProviders.Claude);pending=new TaskCompletionSource<CodexChat.ModelCatalog>();var stale=host.ProviderModels(AiProviders.Claude);Wait(30);var staleRead=pending;
   host.InvalidateModels(AiProviders.Claude);pending=new TaskCompletionSource<CodexChat.ModelCatalog>();pending.SetResult(new CodexChat.ModelCatalog{Connected=false});Await(host.ProviderModels(AiProviders.Claude));
   staleRead.SetResult(new CodexChat.ModelCatalog{Connected=true});Wait(30);Check(stale.IsCanceled&&!host.ProviderConnected(AiProviders.Claude),"stale catalog cannot restore a disconnected provider");
  }
 }
 static void UnifiedModels(Window window,WorkspaceHome workspace){
  var panel=(AiWorkspacePanel)Field(typeof(WorkspaceHome),"aiPanel").GetValue(workspace);var chat=(CodexChat)Field(typeof(WorkspaceHome),"chat").GetValue(workspace);
  var apply=typeof(WorkspaceHome).GetMethod("ApplyConnection",BindingFlags.NonPublic|BindingFlags.Instance);
  var provider=typeof(CodexChat).GetProperty("Provider",BindingFlags.NonPublic|BindingFlags.Instance);var connected=typeof(CodexChat).GetProperty("Connected",BindingFlags.NonPublic|BindingFlags.Instance);
  var hint=Field(typeof(WorkspaceHome),"connectionHint");var originalHint=hint.GetValue(workspace);var draft=(TextBox)Field(typeof(WorkspaceHome),"draft").GetValue(workspace);var originalTip=draft.ToolTip;
  var runtime=Field(typeof(CodexChat),"findRuntime");var originalRuntime=runtime.GetValue(chat);var catalogRead=Field(typeof(CodexChat),"readCatalog");var originalRead=catalogRead.GetValue(chat);var factory=Field(typeof(CodexChat),"createClaude");var originalFactory=factory.GetValue(chat);
  string originalProvider=chat.Provider;bool originalConnected=chat.Connected;var preferences=AppPreferences.Read();var originalLogin=panel.LoginRequested;
  bool codexLogin=false,claudeLogin=false;int reads=0;int uiThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
  Func<string,Task<CodexChat.ModelCatalog>> read=kind=>{
   Check(System.Threading.Thread.CurrentThread.ManagedThreadId!=uiThread,"catalog I/O stays off the UI thread");System.Threading.Interlocked.Increment(ref reads);
   bool logged=kind==AiProviders.Codex?codexLogin:claudeLogin;return Task.FromResult(new CodexChat.ModelCatalog{Connected=logged,Models=logged?new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,object>>{kind==AiProviders.Codex?Model("fixture-codex","A long model display name for narrow layout",true):Model("sonnet","Claude Sonnet"),kind==AiProviders.Codex?Model("fixture-second","Second Codex model",true):Model("opus","Claude Opus")}:new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,object>>()});
  };
  Check(panel.ProviderRequested.Method.Name=="SelectProvider"&&panel.LoginRequested.Method.Name=="ConnectProvider","model/login actions share the existing owners");
  try{
   catalogRead.SetValue(chat,read);runtime.SetValue(chat,new Func<Task<string>>(()=>{throw new InvalidOperationException("fixture offline");}));
   factory.SetValue(chat,new Func<IAiProvider>(()=>new ClaudeProvider(Setup.DataFolder,null,null,args=>Task.FromResult(CodexChat.Map(CatheryneTools.Json().DeserializeObject("{\"exitCode\":0,\"output\":\"{\\\"loggedIn\\\":true,\\\"authMethod\\\":\\\"claude.ai\\\"}\"}"))),()=>Task.FromResult("fixture.exe"))));
   Action reload=()=>{chat.InvalidateModels(AiProviders.Codex);chat.InvalidateModels(AiProviders.Claude);Await(panel.LoadModels());Await((Task)Field(typeof(AiWorkspacePanel),"modelLoading").GetValue(panel));apply.Invoke(workspace,null);window.UpdateLayout();};
   provider.SetValue(chat,AiProviders.Codex,null);connected.SetValue(chat,true,null);codexLogin=true;claudeLogin=true;
   var delayed=new TaskCompletionSource<CodexChat.ModelCatalog>();catalogRead.SetValue(chat,new Func<string,Task<CodexChat.ModelCatalog>>(kind=>kind==AiProviders.Claude?delayed.Task:read(kind)));
   chat.InvalidateModels(AiProviders.Codex);chat.InvalidateModels(AiProviders.Claude);Await(panel.LoadModels());
   Check(!((Task)Field(typeof(AiWorkspacePanel),"modelLoading").GetValue(panel)).IsCompleted&&panel.Models.Items.OfType<ListBoxItem>().Count(x=>x.Tag is AiWorkspacePanel.ModelChoice&&((AiWorkspacePanel.ModelChoice)x.Tag).Provider==AiProviders.Codex)==2,"a slow inactive provider never blocks active models or connection setup");
   delayed.SetResult(new CodexChat.ModelCatalog{Connected=true,Models=new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,object>>{Model("sonnet","Claude Sonnet"),Model("opus","Claude Opus")}});Await((Task)Field(typeof(AiWorkspacePanel),"modelLoading").GetValue(panel));catalogRead.SetValue(chat,read);
   codexLogin=false;claudeLogin=false;connected.SetValue(chat,false,null);reload();
   var choices=panel.Models.Items.OfType<ListBoxItem>().Where(x=>x.Tag is AiWorkspacePanel.ModelChoice).ToArray();
   Check(choices.Length==2&&choices.All(x=>((AiWorkspacePanel.ModelChoice)x.Tag).Model==null),"disconnected providers contain only login entries");
   Check(panel.ModelButton.IsVisible&&panel.ModelButton.IsEnabled,"model popup remains available before login");
   string requestedLogin=null;panel.LoginRequested=kind=>{requestedLogin=kind;claudeLogin=true;chat.InvalidateModels(kind);return Task.FromResult(0);};
   Await(panel.ChooseModel(new AiWorkspacePanel.ModelChoice{Provider=AiProviders.Claude}));Await((Task)Field(typeof(AiWorkspacePanel),"modelLoading").GetValue(panel));panel.LoginRequested=originalLogin;
   Check(requestedLogin==AiProviders.Claude&&panel.Models.Items.OfType<ListBoxItem>().Count(x=>x.Tag is AiWorkspacePanel.ModelChoice&&((AiWorkspacePanel.ModelChoice)x.Tag).Provider==AiProviders.Claude)==2,"login action requests its provider and publishes models afterwards");
   codexLogin=true;connected.SetValue(chat,true,null);reload();int cachedReads=reads;Await(panel.LoadModels());Await(panel.LoadModels());Check(reads==cachedReads,"reopening reuses bounded provider catalogs");
   Check(panel.Models.Items.OfType<ListBoxItem>().Count(x=>x.Tag is AiWorkspacePanel.ModelChoice)==4,"both connected providers share one model list");
   Await(panel.ChooseModel(new AiWorkspacePanel.ModelChoice{Provider=AiProviders.Codex,Model="fixture-second"}));
   Check(chat.Model=="fixture-second"&&Convert.ToString(AppPreferences.Read()[AiProviders.Preference(AiProviders.Codex,"aiModel")])=="fixture-second","same-provider model selection persists");
   panel.Efforts.SelectedItem="xhigh";Check(Convert.ToString(AppPreferences.Read()[AiProviders.Preference(AiProviders.Codex,"aiEffort")])=="xhigh","reasoning persists under its provider key");
   Await(panel.ChooseModel(new AiWorkspacePanel.ModelChoice{Provider=AiProviders.Claude,Model="opus"}));
   Check(chat.Provider==AiProviders.Claude&&chat.Model=="opus"&&chat.Connected&&Convert.ToString(AppPreferences.Read()["aiProvider"])==AiProviders.Claude&&Convert.ToString(AppPreferences.Read()[AiProviders.Preference(AiProviders.Claude,"aiModel")])=="opus","cross-provider choice switches canonically and applies the selected model");
   Check(chat.Effort==null&&panel.Efforts.Items.Count==0,"Claude does not inherit Codex reasoning");
   foreach(string state in new[]{"busy","connecting","resuming","switchingProvider"}){
    Field(typeof(WorkspaceHome),state).SetValue(workspace,true);apply.Invoke(workspace,null);Check(!panel.ModelButton.IsEnabled&&!panel.Models.IsEnabled&&ToolTipService.GetShowOnDisabled(panel.ModelButton)&&Convert.ToString(panel.ModelButton.ToolTip)==Locale.T("진행 중인 작업이 끝난 뒤 AI를 변경해 주세요."),"busy choice explains its guard: "+state);
    bool rejected=false;try{Await(panel.ChooseModel(new AiWorkspacePanel.ModelChoice{Provider=AiProviders.Codex,Model="fixture-second"}));}catch(InvalidOperationException){rejected=true;}Check(rejected&&chat.Provider==AiProviders.Claude,"busy choice cannot change providers");Field(typeof(WorkspaceHome),state).SetValue(workspace,false);apply.Invoke(workspace,null);
   }
   // Verify the canonical owner also refuses direct requests, even if UI is bypassed.
   Field(typeof(WorkspaceHome),"busy").SetValue(workspace,true);bool directRejected=false;try{Await(panel.ProviderRequested(AiProviders.Codex));}catch(InvalidOperationException){directRejected=true;}Check(directRejected,"canonical provider owner guards busy work");Field(typeof(WorkspaceHome),"busy").SetValue(workspace,false);
   Await(panel.ChooseModel(new AiWorkspacePanel.ModelChoice{Provider=AiProviders.Codex,Model="fixture-second"}));Check(!chat.Connected&&chat.Model==null&&Convert.ToString(hint.GetValue(workspace))=="fixture offline","failed connection stays visible and does not apply a model");
   foreach(string kind in new[]{AiProviders.Codex,AiProviders.Claude})foreach(bool logged in new[]{false,true}){
    provider.SetValue(chat,kind,null);connected.SetValue(chat,logged,null);codexLogin=logged;claudeLogin=logged;hint.SetValue(workspace,Locale.Format("{0} 로그인 대기 중",chat.ProviderName));chat.Model=kind==AiProviders.Codex?"fixture-codex":"sonnet";reload();
    if(!logged)Check(System.Windows.Automation.AutomationProperties.GetName(draft)==Locale.Format("{0} 로그인 대기 중",chat.ProviderName),"input hint names the selected disconnected provider");
    if(logged&&kind==AiProviders.Codex){chat.Effort="xhigh";typeof(AiWorkspacePanel).GetMethod("UpdateModelLabel",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(panel,null);}
    window.UpdateLayout();var group=(StackPanel)panel.ModelButton.Parent;var row=(DockPanel)group.Parent;var send=row.Children.OfType<Button>().Single();var controls=row.Children.OfType<StackPanel>().Single(x=>!ReferenceEquals(x,group));
    Check(DockPanel.GetDock(group)==Dock.Right&&group.Orientation==Orientation.Horizontal,"compact model/usage group docks beside send");
    double center=send.TranslatePoint(new Point(0,18),row).Y;
    foreach(var item in new FrameworkElement[]{controls,panel.ModelButton,panel.Usage,send})Check(item.ActualHeight==36&&Math.Abs(item.TranslatePoint(new Point(0,18),row).Y-center)<1,"composer controls share 36 DIP height and center");
    Check(group.TranslatePoint(new Point(group.ActualWidth,0),row).X<=send.TranslatePoint(new Point(),row).X&&group.TranslatePoint(new Point(),row).X>=controls.TranslatePoint(new Point(controls.DesiredSize.Width,0),row).X,"controls and compact group do not overlap");
    Check(panel.ModelButton.ActualWidth<300&&panel.ModelButton.ActualWidth<=panel.ModelButton.DesiredSize.Width+1,"model button never stretches across available width");
    Capture(window,"catheryne-models-window-"+Locale.LanguageCode+"-"+(int)window.Width+"-"+kind+"-"+(logged?"connected":"login"));
    Capture((FrameworkElement)((FrameworkElement)row.Parent).Parent,"catheryne-chat-models-"+Locale.LanguageCode+"-"+(int)window.Width+"-"+kind+"-"+(logged?"connected":"login"));
    panel.ModelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(40);var popup=(System.Windows.Controls.Primitives.Popup)Field(typeof(AiWorkspacePanel),"modelPopup").GetValue(panel);Check(popup.IsOpen,"unified model popup opens before and after login");Capture((FrameworkElement)popup.Child,"catheryne-model-list-"+Locale.LanguageCode+"-"+(int)window.Width+"-"+kind+"-"+(logged?"connected":"login"));popup.IsOpen=false;
   }
   // Clearing a connection invalidates its models and returns the login entry.
   claudeLogin=false;provider.SetValue(chat,AiProviders.Claude,null);connected.SetValue(chat,false,null);reload();Check(panel.Models.Items.OfType<ListBoxItem>().Single(x=>x.Tag is AiWorkspacePanel.ModelChoice&&((AiWorkspacePanel.ModelChoice)x.Tag).Provider==AiProviders.Claude).Content.ToString()==Locale.T("로그인"),"disconnect removes that provider's model rows");
  }finally{
   foreach(string state in new[]{"busy","connecting","resuming","switchingProvider"})Field(typeof(WorkspaceHome),state).SetValue(workspace,false);
   var transport=(IAiProvider)Field(typeof(CodexChat),"providerTransport").GetValue(chat);if(transport!=null)transport.Dispose();Field(typeof(CodexChat),"providerTransport").SetValue(chat,null);
   runtime.SetValue(chat,originalRuntime);catalogRead.SetValue(chat,originalRead);factory.SetValue(chat,originalFactory);panel.LoginRequested=originalLogin;provider.SetValue(chat,originalProvider,null);connected.SetValue(chat,originalConnected,null);hint.SetValue(workspace,originalHint);draft.ToolTip=originalTip;
   foreach(string key in new[]{"aiProvider","aiModel","aiEffort","claudeModel","claudeEffort"}){object saved;preferences.TryGetValue(key,out saved);AppPreferences.Set(key,saved);}panel.Models.Items.Clear();chat.Model=null;chat.Effort=null;apply.Invoke(workspace,null);
  }
 }
 static void Accounts(Window window,WorkspaceHome workspace){
  var panel=(AiWorkspacePanel)typeof(WorkspaceHome).GetField("aiPanel",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(workspace);
  typeof(AiWorkspacePanel).GetMethod("ShowAccounts",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(panel,null);
  var popup=(System.Windows.Controls.Primitives.Popup)typeof(AiWorkspacePanel).GetField("accountPopup",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(panel);
  try{
   var surface=(Border)popup.Child;surface.Measure(new Size(360,double.PositiveInfinity));surface.Arrange(new Rect(0,0,360,surface.DesiredSize.Height));surface.UpdateLayout();var rows=((StackPanel)surface.Child).Children.Cast<Grid>().ToArray();
   Check(rows.Length==4&&rows.All(r=>r.ActualHeight==44),"all account providers share equal control heights");
   foreach(var row in rows){var button=row.Children.OfType<Button>().First();var content=(DockPanel)button.Content;var label=content.Children.OfType<TextBlock>().Last();Check(label.ActualWidth>50&&button.ActualHeight==44,"account names fit without squeezing login actions");}
   var claude=(DockPanel)rows[1].Children.OfType<Button>().First().Content;Check(claude.Children.OfType<TextBlock>().Last().Text=="Claude"&&claude.Children.OfType<TextBlock>().First().Text==Locale.T("로그인"),"Claude shows its actual disconnected state");
   var bitmap=new RenderTargetBitmap(360,(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(Path.GetTempPath(),"catheryne-accounts-"+Locale.LanguageCode+"-"+(int)window.Width+".png")))png.Save(output);
  }finally{popup.IsOpen=false;}
 }
 static void Caption(Window window){
  var chrome=System.Windows.Shell.WindowChrome.GetWindowChrome(window);Check(!chrome.UseAeroCaptionButtons&&chrome.GlassFrameThickness.Top==0&&chrome.CaptionHeight==LauncherWindowLayout.HeaderHeight,"custom caption visuals retain Windows chrome behavior");
  var hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;
  string[] names={"WindowMinimize","WindowMaximize","WindowClose"};
  foreach(string name in names){
   var button=(Button)window.FindName(name);Check(button.ActualHeight==LauncherWindowLayout.HeaderHeight&&button.ActualWidth==46,"caption controls fill the header at Windows button width");
   foreach(double y in new[]{2.0,20.0,38.0}){var point=button.PointToScreen(new Point(button.ActualWidth/2,y));Check(SendMessage(hwnd,0x84,IntPtr.Zero,Packed(point)).ToInt32()==(name=="WindowMaximize"?9:1),"caption hit area includes header top, center and bottom: "+name+" / "+y);}
  }
  var rail=(FrameworkElement)window.FindName("NavigationRail");Check(rail.TranslatePoint(new Point(),window).Y<2,"navigation rail joins the top window edge");
  var title=(Grid)window.FindName("TitleBar");var toggle=(FrameworkElement)window.FindName("ToggleHomeChat");var account=System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<Button>(title.Children),b=>b!=toggle);
  var controls=new FrameworkElement[]{account,toggle,(FrameworkElement)window.FindName("HeaderIndicators")};double center=toggle.PointToScreen(new Point(0,toggle.ActualHeight/2)).Y;var caption=(FrameworkElement)window.FindName("WindowMinimize");
  foreach(var control in controls){Check(control.PointToScreen(new Point(control.ActualWidth,0)).X<caption.PointToScreen(new Point()).X,"app controls sit left of caption buttons");Check(Math.Abs(control.PointToScreen(new Point(0,control.ActualHeight/2)).Y-center)<1,"app toolbar controls share one center line");}
  Check(account.PointToScreen(new Point(account.ActualWidth,0)).X<toggle.PointToScreen(new Point()).X,"account and hide controls use distinct columns");
  // Release at the enlarged lower edge must maximize and restore through the same system command.
  var maximize=(Button)window.FindName("WindowMaximize");var original=window.WindowState;
  for(int i=0;i<2;i++){var point=maximize.PointToScreen(new Point(maximize.ActualWidth/2,38));var origin=window.PointToScreen(new Point());SendMessage(hwnd,0xA1,new IntPtr(9),Packed(point));SendMessage(hwnd,0x202,IntPtr.Zero,Packed(new Point(point.X-origin.X,point.Y-origin.Y)));Wait(180);Check(window.WindowState==(i==0?WindowState.Maximized:original),"lower caption edge uses maximize/restore commands");}
  var between=window.PointToScreen(new Point(500,20));Check(SendMessage(hwnd,0x84,IntPtr.Zero,Packed(between)).ToInt32()==2,"unused header retains native dragging and double-click hit testing");
 }
 internal static void Run(Window window,WorkspaceHome workspace){
  CatalogCache();
  var menu=(Border)window.FindName("WorkspaceMenu");var chat=(Border)window.FindName("WorkspaceHome");double width=window.Width,minWidth=window.MinWidth;
  // The hosted runner can have a smaller desktop than the wide-layout fixture.
  // Set the fixture minimum too so native restore cannot clamp its requested viewport.
  Action<double> resize=value=>{window.MinWidth=value;window.Width=value;Wait(300);Check(Math.Abs(window.ActualWidth-value)<1,"fixture viewport: expected "+value+", actual "+window.ActualWidth);};
  Action<double> margin=left=>{window.UpdateLayout();var expected=new Thickness(left,LauncherWindowLayout.ChatTop,18,84);Check((Thickness)chat.GetAnimationBaseValue(FrameworkElement.MarginProperty)==expected&&chat.Margin==expected,"transcript and composer reservation: expected "+expected+", base "+chat.GetAnimationBaseValue(FrameworkElement.MarginProperty)+", current "+chat.Margin+", viewport "+window.ActualWidth+", state "+window.WindowState);};
  try{
   resize(1240);workspace.Show("설정");Wait(300);margin(258);Caption(window);Accounts(window,workspace);UnifiedModels(window,workspace);Wait(300);margin(258);
   Check(chat.TranslatePoint(new Point(),window).X>=menu.TranslatePoint(new Point(menu.ActualWidth,0),window).X,"wide chat must remain to the right of the menu: chat "+chat.TranslatePoint(new Point(),window).X+", menu right "+menu.TranslatePoint(new Point(menu.ActualWidth,0),window).X+", menu width "+menu.ActualWidth+", visibility "+chat.Visibility+", window "+window.ActualWidth);
   DrawerMotion.Hide(menu);Wait(20);workspace.EnsureMenu("설정");Wait(300);Check(DrawerMotion.IsOpen(menu),"reopening the same menu must cancel its pending close");margin(258);
   // Recomputed geometry must be recoverable even when the requested destination did not change.
   chat.BeginAnimation(FrameworkElement.MarginProperty,null);chat.Margin=new Thickness(18,LauncherWindowLayout.ChatTop,18,84);workspace.EnsureMenu("설정");Wait(300);margin(258);
   foreach(string route in new[]{"플레이","내 계정","캘린더","설정"}){workspace.HideMenu();workspace.Show(route);Wait(15);}Wait(300);margin(258);
   resize(980);margin(18);Caption(window);Accounts(window,workspace);UnifiedModels(window,workspace);Check(DrawerMotion.IsOpen(menu),"narrow layout must retain the overlay menu");
   resize(780);margin(18);UnifiedModels(window,workspace);
   resize(1240);margin(258);
   var feature=(Border)window.FindName("CompanionPage");DrawerMotion.Show(feature);Wait(250);Check(chat.Visibility==Visibility.Collapsed,"a detail panel must keep covering chat");DrawerMotion.Hide(feature);Wait(250);Check(chat.Visibility==Visibility.Visible,"chat must return after the detail panel closes");margin(258);
   workspace.HideMenu();Wait(300);margin(18);
  }finally{workspace.HideMenu();window.MinWidth=minWidth;window.Width=width;Wait(300);}
 }
}
