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
 static void Providers(Window window,WorkspaceHome workspace){
  var panel=(AiWorkspacePanel)Field(typeof(WorkspaceHome),"aiPanel").GetValue(workspace);
  var chat=(CodexChat)Field(typeof(WorkspaceHome),"chat").GetValue(workspace);
  var apply=typeof(WorkspaceHome).GetMethod("ApplyConnection",BindingFlags.NonPublic|BindingFlags.Instance);
  var provider=typeof(CodexChat).GetProperty("Provider",BindingFlags.NonPublic|BindingFlags.Instance);
  var connected=typeof(CodexChat).GetProperty("Connected",BindingFlags.NonPublic|BindingFlags.Instance);
  var hint=Field(typeof(WorkspaceHome),"connectionHint");var originalHint=hint.GetValue(workspace);
  var draft=(TextBox)Field(typeof(WorkspaceHome),"draft").GetValue(workspace);var originalTip=draft.ToolTip;
  var runtime=Field(typeof(CodexChat),"findRuntime");var originalRuntime=runtime.GetValue(chat);
  string originalProvider=chat.Provider;bool originalConnected=chat.Connected;var preferences=AppPreferences.Read();object saved;preferences.TryGetValue("aiProvider",out saved);
  Check(panel.ProviderRequested.Method.Name=="SelectProvider","composer uses the existing provider selection owner");
  Check(panel.Providers.Items.Cast<string>().SequenceEqual(new[]{"Codex (ChatGPT)","Claude"}),"provider list is complete");
  try{
   // The real selection/save path runs with a failing fake runtime, never an account/API call.
   runtime.SetValue(chat,new Func<Task<string>>(()=>{throw new InvalidOperationException("fixture offline");}));
   provider.SetValue(chat,AiProviders.Claude,null);connected.SetValue(chat,false,null);apply.Invoke(workspace,null);
   Check(panel.Providers.SelectedIndex==1&&panel.Providers.IsEnabled,"saved Claude remains visible while disconnected");
   panel.Providers.SelectedIndex=0;for(int i=0;i<100&&((bool)Field(typeof(WorkspaceHome),"switchingProvider").GetValue(workspace));i++)Wait(10);
   Check(chat.Provider==AiProviders.Codex&&Convert.ToString(AppPreferences.Read()["aiProvider"])==AiProviders.Codex&&panel.Providers.IsEnabled,"selection persists through the canonical path and releases after a connection failure");
   Check(Convert.ToString(hint.GetValue(workspace))=="fixture offline","connection errors remain visible");
   foreach(string state in new[]{"busy","connecting","resuming","switchingProvider"}){
    Field(typeof(WorkspaceHome),state).SetValue(workspace,true);apply.Invoke(workspace,null);
    Check(!panel.Providers.IsEnabled&&ToolTipService.GetShowOnDisabled(panel.Providers)&&Convert.ToString(panel.Providers.ToolTip)==Locale.T("진행 중인 작업이 끝난 뒤 AI를 변경해 주세요."),"disabled provider explains its guard: "+state);
    bool rejected=false;try{panel.ProviderRequested(AiProviders.Claude).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}
    Check(rejected&&chat.Provider==AiProviders.Codex,"guard cannot switch provider: "+state);Field(typeof(WorkspaceHome),state).SetValue(workspace,false);
   }
   foreach(string kind in new[]{AiProviders.Codex,AiProviders.Claude})foreach(bool login in new[]{false,true}){
    provider.SetValue(chat,kind,null);connected.SetValue(chat,login,null);hint.SetValue(workspace,Locale.Format("{0} 로그인 대기 중",chat.ProviderName));apply.Invoke(workspace,null);
    Check(panel.Providers.IsVisible&&panel.Providers.IsEnabled&&panel.Providers.SelectedIndex==(kind==AiProviders.Claude?1:0),"current provider stays visible before and after login");
    if(!login)Check(System.Windows.Automation.AutomationProperties.GetName(draft)==Locale.Format("{0} 로그인 대기 중",chat.ProviderName),"login hint names its provider");
    panel.Models.Items.Clear();panel.Models.Items.Add(new ComboBoxItem{Content="A long model display name for narrow layout",Tag="fixture-model"});panel.Models.SelectedIndex=0;chat.Effort="Extra high";typeof(AiWorkspacePanel).GetMethod("UpdateModelLabel",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(panel,null);window.UpdateLayout();
    var row=(Grid)panel.Providers.Parent;Check(ReferenceEquals(panel.ModelButton.Parent,row),"selector is adjacent to the model button in the composer");
    var items=row.Children.OfType<FrameworkElement>().OrderBy(Grid.GetColumn).ToArray();double right=0,center=panel.Providers.TranslatePoint(new Point(0,18),row).Y;
    foreach(var item in items){var point=item.TranslatePoint(new Point(),row);Check(item.ActualHeight==36&&Math.Abs(item.TranslatePoint(new Point(0,item.ActualHeight/2),row).Y-center)<1,"composer controls share 36 DIP height and center");Check(point.X>=right&&point.X+item.ActualWidth<=row.ActualWidth+1,"composer controls do not overlap or overflow");right=point.X+item.ActualWidth;}
    Check(panel.ModelButton.TranslatePoint(new Point(),row).X-(panel.Providers.TranslatePoint(new Point(),row).X+panel.Providers.ActualWidth)>=8,"provider/model gap is at least 8 DIP");
    Capture((FrameworkElement)((FrameworkElement)row.Parent).Parent,"catheryne-chat-provider-"+Locale.LanguageCode+"-"+(int)window.Width+"-"+kind+"-"+(login?"connected":"login"));
    panel.Providers.IsDropDownOpen=true;Wait(20);var popup=(System.Windows.Controls.Primitives.Popup)panel.Providers.Template.FindName("PART_Popup",panel.Providers);Check(popup.IsOpen,"provider list opens in the composer");Capture((FrameworkElement)popup.Child,"catheryne-provider-list-"+Locale.LanguageCode);panel.Providers.IsDropDownOpen=false;
   }
  }finally{
   foreach(string state in new[]{"busy","connecting","resuming","switchingProvider"})Field(typeof(WorkspaceHome),state).SetValue(workspace,false);
   runtime.SetValue(chat,originalRuntime);provider.SetValue(chat,originalProvider,null);connected.SetValue(chat,originalConnected,null);hint.SetValue(workspace,originalHint);draft.ToolTip=originalTip;AppPreferences.Set("aiProvider",saved);panel.Models.Items.Clear();chat.Model=null;chat.Effort=null;apply.Invoke(workspace,null);
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
  var menu=(Border)window.FindName("WorkspaceMenu");var chat=(Border)window.FindName("WorkspaceHome");double width=window.Width,minWidth=window.MinWidth;
  // The hosted runner can have a smaller desktop than the wide-layout fixture.
  // Set the fixture minimum too so native restore cannot clamp its requested viewport.
  Action<double> resize=value=>{window.MinWidth=value;window.Width=value;Wait(300);Check(Math.Abs(window.ActualWidth-value)<1,"fixture viewport: expected "+value+", actual "+window.ActualWidth);};
  Action<double> margin=left=>{window.UpdateLayout();var expected=new Thickness(left,LauncherWindowLayout.ChatTop,18,84);Check((Thickness)chat.GetAnimationBaseValue(FrameworkElement.MarginProperty)==expected&&chat.Margin==expected,"transcript and composer reservation: expected "+expected+", base "+chat.GetAnimationBaseValue(FrameworkElement.MarginProperty)+", current "+chat.Margin+", viewport "+window.ActualWidth+", state "+window.WindowState);};
  try{
   resize(1240);workspace.Show("설정");Wait(300);margin(258);Caption(window);Accounts(window,workspace);Providers(window,workspace);Wait(300);margin(258);
   Check(chat.TranslatePoint(new Point(),window).X>=menu.TranslatePoint(new Point(menu.ActualWidth,0),window).X,"wide chat must remain to the right of the menu: chat "+chat.TranslatePoint(new Point(),window).X+", menu right "+menu.TranslatePoint(new Point(menu.ActualWidth,0),window).X+", menu width "+menu.ActualWidth+", visibility "+chat.Visibility+", window "+window.ActualWidth);
   DrawerMotion.Hide(menu);Wait(20);workspace.EnsureMenu("설정");Wait(300);Check(DrawerMotion.IsOpen(menu),"reopening the same menu must cancel its pending close");margin(258);
   // Recomputed geometry must be recoverable even when the requested destination did not change.
   chat.BeginAnimation(FrameworkElement.MarginProperty,null);chat.Margin=new Thickness(18,LauncherWindowLayout.ChatTop,18,84);workspace.EnsureMenu("설정");Wait(300);margin(258);
   foreach(string route in new[]{"플레이","내 계정","캘린더","설정"}){workspace.HideMenu();workspace.Show(route);Wait(15);}Wait(300);margin(258);
   resize(980);margin(18);Caption(window);Accounts(window,workspace);Providers(window,workspace);Check(DrawerMotion.IsOpen(menu),"narrow layout must retain the overlay menu");
   resize(780);margin(18);Providers(window,workspace);
   resize(1240);margin(258);
   var feature=(Border)window.FindName("CompanionPage");DrawerMotion.Show(feature);Wait(250);Check(chat.Visibility==Visibility.Collapsed,"a detail panel must keep covering chat");DrawerMotion.Hide(feature);Wait(250);Check(chat.Visibility==Visibility.Visible,"chat must return after the detail panel closes");margin(258);
   workspace.HideMenu();Wait(300);margin(18);
  }finally{workspace.HideMenu();window.MinWidth=minWidth;window.Width=width;Wait(300);}
 }
}
