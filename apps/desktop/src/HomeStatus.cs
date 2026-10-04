using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Projections of the existing account observations and calendar. No second schedule store.
internal sealed class HomeStatus {
 readonly Window window;readonly Action<DateTime> openDate;readonly Button resin,warning;readonly TextBlock amount,warningText;
 readonly CalendarStore calendar=new CalendarStore(Setup.DataFolder);
 readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};
 List<CalendarEntry> deadlines=new List<CalendarEntry>();bool busy,closed;
 internal HomeStatus(Window window,Action openDaily,Action<DateTime> openDate){
  this.window=window;this.openDate=openDate;resin=(Button)window.FindName("HeroResin");amount=(TextBlock)window.FindName("HeroResinValue");resin.Click+=(s,e)=>openDaily();
  string icon=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"branding","resin.png");if(File.Exists(icon)){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=new Uri(icon);bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.EndInit();bitmap.Freeze();((Image)window.FindName("ResinIcon")).Source=bitmap;}
  warningText=new TextBlock{TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=170,VerticalAlignment=VerticalAlignment.Center};
  warning=PanelUi.HeaderPill();warning.Content=warningText;warning.Foreground=new SolidColorBrush(Color.FromRgb(255,218,157));warning.Background=new SolidColorBrush(Color.FromRgb(88,70,51));warning.Visibility=Visibility.Collapsed;
  ((StackPanel)window.FindName("HeaderIndicators")).Children.Insert(0,warning);System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(warning,true);warning.Click+=(s,e)=>{if(deadlines.Count==1){openDate(deadlines[0].Due.ToLocalTime());return;}var menu=PanelUi.Menu();foreach(var deadline in deadlines){var item=deadline;var row=new MenuItem{Header=item.Title+"  "+CalendarStore.Remaining(item)};row.Click+=(o,a)=>openDate(item.Due.ToLocalTime());menu.Items.Add(row);}menu.PlacementTarget=warning;menu.IsOpen=true;};
  timer.Tick+=async(s,e)=>{if(window.IsVisible&&window.WindowState!=WindowState.Minimized)await Refresh();};window.Loaded+=async(s,e)=>{timer.Start();await Refresh();};window.Activated+=async(s,e)=>await Refresh();window.Closed+=(s,e)=>{closed=true;timer.Stop();};
 }
 async Task Refresh(){
  if(busy||closed)return;busy=true;
  try{
   // Show saved data before waiting for the service, just like the calendar itself.
   await Read();
   bool connected=await Task.Run(()=>{using(var db=new LocalDataService(Setup.DataFolder))return !string.IsNullOrEmpty(db.GetSecret("hoyolab"));});
   if(connected){bool failed=false;try{await calendar.Refresh(false);}catch(Exception){failed=true;}await Read();if(failed&&!closed)resin.ToolTip=Convert.ToString(resin.ToolTip)+"\n"+Locale.T("자동 갱신을 하지 못했습니다.");}
  }catch(Exception){if(!closed){amount.Text="—";resin.ToolTip=Locale.T("레진 정보를 확인하지 못했습니다.");warning.Visibility=Visibility.Collapsed;}}
  finally{busy=false;}
 }
 async Task Read(){
  var data=await Task.Run(()=>{var p=AppPreferences.Read();return new{Resin=HoyoNotes.Latest(Setup.DataFolder,CodexChat.S(p,"resinUid"),CodexChat.S(p,"resinServer")),Entries=CalendarStore.Critical(calendar.Read(),DateTime.UtcNow)};});if(closed)return;
  if(data.Resin==null){amount.Text="—";resin.ToolTip=Locale.T("레진 확인 / 계정 연결");}else{var value=CodexChat.Map(data.Resin["value"]);var observed=DateTime.Parse(Convert.ToString(data.Resin["observed_at"])).ToUniversalTime();bool stale=DateTime.UtcNow-observed>TimeSpan.FromHours(24);amount.Text=stale?"—":CodexChat.S(value,"current")+" / "+CodexChat.S(value,"maximum");resin.ToolTip=Locale.T("레진\n")+Locale.Format("{0} 확인",observed.ToLocalTime().ToString("g",Locale.Culture))+(stale?"\n"+Locale.T("최근 정보 확인 필요"):"");}
  deadlines=data.Entries;bool show=deadlines.Count>0;if(show){var first=deadlines[0];warningText.Text=first.Title+"  "+CalendarStore.Remaining(first)+(deadlines.Count>1?" +"+(deadlines.Count-1):"");warning.ToolTip=string.Join("\n",deadlines.Select(e=>e.Title+"  "+CalendarStore.Remaining(e)));System.Windows.Automation.AutomationProperties.SetName(warning,Convert.ToString(warning.ToolTip));if(!warning.IsVisible)warning.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));}warning.Visibility=show?Visibility.Visible:Visibility.Collapsed;
 }
}
