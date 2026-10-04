using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal sealed class CalendarEntry {
 public bool Deleted {get;set;}
 public string Repeat {get;set;} public string Id {get;set;} public string Title {get;set;} public DateTime Due {get;set;} public DateTime? Start {get;set;} public bool Done {get;set;} public string CompletionText {get;set;} public string Source {get;set;} public DateTime Observed {get;set;}
}
// Views and AI use the same persisted source snapshots and deadline projection.
internal sealed class CalendarStore {
 static string CacheKind {get{return ChallengeSeasons.CalendarKind;}}
 readonly string root;internal CalendarStore(string root){this.root=root;}
 internal static string AccountScope(string root){var prefs=AppPreferences.Read(root);return CodexChat.S(prefs,"resinUid")+"|"+CodexChat.S(prefs,"resinServer")+"|"+System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine(root,"settings.json")).Ticks;}
 internal static bool ChangedBy(AiTaskRecord task){return task!=null&&task.State=="completed"&&((task.Action??"").StartsWith("calendar.")||task.Action=="daily.refresh"||task.Action=="resin.refresh"||(task.Tool=="catheryne_request"&&task.Operation=="daily_checkin"));}
 static string S(Dictionary<string,object> d,string k){return CodexChat.S(d,k);}
 static double Number(Dictionary<string,object> d,string k){double v;return double.TryParse(S(d,k),out v)?v:0;}
 static DateTime Epoch(double n){return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(n);}
 static DateTime Reset(DateTime now,string server){int offset=server=="os_usa"?-5:server=="os_euro"?1:8;var day=now.AddHours(offset).Date.AddHours(4);if(day<=now.AddHours(offset))day=day.AddDays(1);return DateTime.SpecifyKind(day.AddHours(-offset),DateTimeKind.Utc);}
 internal void Save(CalendarEntry e){if(string.IsNullOrWhiteSpace(e.Title)||e.Title.Length>160)throw new ArgumentException(Locale.T("일정 이름을 입력해 주세요."));if(!new[]{"none","daily","weekly","monthly"}.Contains(e.Repeat??"none"))throw new ArgumentException(Locale.T("반복 주기를 확인해 주세요."));if(e.Due==default(DateTime))throw new ArgumentException(Locale.T("일정 날짜를 확인해 주세요."));e.Source="manual";e.Observed=DateTime.UtcNow;if(string.IsNullOrEmpty(e.Id))e.Id=Guid.NewGuid().ToString("N");using(var db=new LocalDataService(root))db.Observe("default","calendar-manual",e);}
 internal void Remove(string id){var entry=Read().Single(x=>x.Id==id&&x.Source=="manual");entry.Deleted=true;Save(entry);}
 internal CalendarEntry SetDone(string id,bool done){
  if(string.IsNullOrWhiteSpace(id))throw new ArgumentException(Locale.T("완료 상태를 바꿀 일정을 선택해 주세요."));
  var entry=Read().SingleOrDefault(e=>e.Id==id&&e.Source=="manual");
  if(entry==null)throw new InvalidOperationException(Locale.T("직접 등록한 일정을 찾을 수 없습니다."));
  if(entry.Done!=done){entry.Done=done;Save(entry);}return entry;
 }
 internal async Task Refresh(bool force=true){var p=AppPreferences.Read(root);string uid=S(p,"resinUid"),server=S(p,"resinServer"),cookie;using(var db=new LocalDataService(root))cookie=db.GetSecret("hoyolab");if(string.IsNullOrEmpty(cookie))throw new InvalidOperationException(Locale.T("HoYoLAB 로그인이 필요합니다."));await Task.Run(()=>{var account=HoyoClient.Account(cookie,root);uid=S(account,"resinUid");server=S(account,"resinServer");new ChallengeSeasons(root).Refresh(cookie,uid,server,force);HoyoNotes.Refresh(root,cookie,uid,server,force);});}
 internal List<CalendarEntry> Read(){
  var result=new List<CalendarEntry>();var prefs=AppPreferences.Read(root);string uid=S(prefs,"resinUid"),server=S(prefs,"resinServer");DateTime now=DateTime.UtcNow;
  using(var db=new LocalDataService(root)){
   foreach(var row in db.Query("SELECT payload FROM observations WHERE profile='default' AND kind='calendar-manual' AND id IN (SELECT MAX(id) FROM observations WHERE profile='default' AND kind='calendar-manual' GROUP BY json_extract(payload,'$.Id'))")){var item=CatheryneTools.Json().Deserialize<CalendarEntry>(row["payload"]);if(item.Deleted)continue;if(item.Repeat=="daily"||item.Repeat=="weekly"||item.Repeat=="monthly"){int advances=0;while(item.Due<now&&advances++<10000){item.Due=item.Repeat=="monthly"?item.Due.AddMonths(1):item.Due.AddDays(item.Repeat=="weekly"?7:1);item.Done=false;}}result.Add(item);}
   var calendar=db.Recent(uid,CacheKind,1);if(calendar.Count>0){var data=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(calendar[0]["payload"]);DateTime observed=DateTime.Parse(calendar[0]["observed_at"]).ToUniversalTime();foreach(string group in new[]{"avatar_card_pool_list","weapon_card_pool_list","mixed_card_pool_list","act_list"}){object raw;if(!data.TryGetValue(group,out raw))continue;foreach(var entry in CodexChat.Items(raw)){double end=Number(entry,"end_timestamp"),start=Number(entry,"start_timestamp");if(end<=0)continue;result.Add(new CalendarEntry{Id=group+":"+S(entry,group.Contains("pool")?"pool_id":"id"),Title=S(entry,group.Contains("pool")?"pool_name":"name"),Start=start>0?(DateTime?)Epoch(start):null,Due=Epoch(end),Done=entry.ContainsKey("is_finished")&&Equals(entry["is_finished"],true),Source="hoyolab",Observed=observed});}}}
   var notes=db.Recent(uid,"daily-note",1);if(notes.Count>0){var data=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(notes[0]["payload"]);DateTime observed=DateTime.Parse(notes[0]["observed_at"]).ToUniversalTime();if(now-observed<TimeSpan.FromHours(24)){
    foreach(var pair in new[]{new[]{"레진 충전","resin_recovery_time"},new[]{"선계 화폐 충전","home_coin_recovery_time"}})if(data.ContainsKey(pair[1]))result.Add(new CalendarEntry{Id=pair[1],Title=Locale.T(pair[0]),Due=observed.AddSeconds(Number(data,pair[1])),Source="estimate",Observed=observed});
    DateTime reset=Reset(observed,server);if(reset>now&&data.ContainsKey("is_extra_task_reward_received"))result.Add(new CalendarEntry{Id="commissions",Title=Locale.T("일일 의뢰 보상"),Due=reset,Done=Equals(data["is_extra_task_reward_received"],true),Source="hoyolab",Observed=observed});
   }}
   var attendance=db.Recent("default","attendance",1);if(attendance.Count>0){var data=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(attendance[0]["payload"]);DateTime observed=DateTime.Parse(attendance[0]["observed_at"]).ToUniversalTime();DateTime reset=DateTime.SpecifyKind(observed.AddHours(8).Date.AddDays(1).AddHours(-8),DateTimeKind.Utc);if(reset>now)result.Add(new CalendarEntry{Id="attendance",Title=Locale.T("HoYoLAB 출석"),Due=reset,Done=Equals(data.ContainsKey("is_sign")?data["is_sign"]:null,true),Source="hoyolab",Observed=observed});}
  }
  result.AddRange(new ChallengeSeasons(root).Read());return result.OrderBy(e=>e.Due).ToList();
 }
 internal static List<CalendarEntry> Critical(IEnumerable<CalendarEntry> entries,DateTime now){return entries.Where(e=>!e.Done&&!e.Deleted&&(!e.Start.HasValue||e.Start<=now)&&e.Due<=now.AddHours(6)&&(e.Due>now||((e.Source=="estimate"||e.Source=="manual")&&e.Due>=now.AddHours(-24)))&&(e.Source=="manual"||(e.Observed<=now&&now-e.Observed<TimeSpan.FromHours(24)))).OrderBy(e=>e.Due).ToList();}
 internal List<CalendarEntry> Urgent(){return Read().Where(e=>!e.Done&&(!e.Start.HasValue||e.Start<=DateTime.UtcNow)&&e.Due>=DateTime.UtcNow.AddHours(-24)&&e.Due<=DateTime.UtcNow.AddDays(1)&&(e.Source=="manual"||DateTime.UtcNow-e.Observed<TimeSpan.FromHours(24))).ToList();}
 internal static string Remaining(CalendarEntry e){var left=e.Due-DateTime.UtcNow;if(e.Done)return string.IsNullOrEmpty(e.CompletionText)?Locale.T("완료"):e.CompletionText;if(left.TotalSeconds<=0)return Locale.T(e.Source=="estimate"?"충전 예상":"기한 지남");return left.TotalDays>=1?Locale.Format("{0}일 남음",(int)Math.Ceiling(left.TotalDays)):left.TotalHours>=1?Locale.Format("{0}시간 남음",(int)Math.Ceiling(left.TotalHours)):Locale.Format("{0}분 남음",Math.Max(1,(int)Math.Ceiling(left.TotalMinutes)));}
}
internal sealed class CalendarPanel {
 readonly CalendarStore store=new CalendarStore(Setup.DataFolder);readonly DetailPanel detail;readonly StackPanel entries=new StackPanel();readonly StackPanel agenda=new StackPanel();Border menu;Action close;UIElement menuContent;int generation;readonly TextBlock status=PanelUi.Text("",true);DateTime month=DateTime.Today;DateTime? day=DateTime.Today;
 List<CalendarEntry> currentEntries=new List<CalendarEntry>();int readGeneration;bool hasRead;string cachedAccount;
 readonly System.Windows.Threading.DispatcherTimer timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};bool updating;
 internal CalendarPanel(DetailPanel detail){this.detail=detail;timer.Tick+=async(s,e)=>{if(updating||menu==null||!menu.IsVisible||menu.Child!=menuContent)return;updating=true;int request=generation;try{await store.Refresh(false);if(request==generation&&menu.IsVisible&&menu.Child==menuContent){status.Text="";await Reload(false);}}catch(Exception error){if(request==generation){status.Text=error.Message;status.Visibility=Visibility.Visible;}}finally{updating=false;}};}

 internal void SelectDate(DateTime date){day=date.Date;month=date.Date;Render();}
 internal void UpdateTask(AiTaskRecord task){if(!CalendarStore.ChangedBy(task)||menu==null||!menu.IsVisible||menu.Child!=menuContent)return;var pending=Reload(false);}
 internal void Show(Border menu,Action close){if(this.menu==null){menu.IsVisibleChanged+=(s,e)=>{if(menu.IsVisible)timer.Start();else timer.Stop();};menu.Unloaded+=(s,e)=>timer.Stop();}this.menu=menu;this.close=close;Show();timer.Start();}
 internal async void Show(){int request=++generation;hasRead=false;currentEntries=new List<CalendarEntry>();if(entries.Parent is Panel)((Panel)entries.Parent).Children.Remove(entries);if(status.Parent is Panel)((Panel)status.Parent).Children.Remove(status);var body=new StackPanel();var add=PanelUi.Button(Locale.T("일정 추가"));add.Click+=(s,e)=>Edit();body.Children.Add(PanelUi.Actions(add));body.Children.Add(status);body.Children.Add(entries);Render();if(menu!=null){menuContent=PanelUi.Shell(new TextBlock{Text=Locale.T("캘린더")},body,new StackPanel(),close);menu.Child=menuContent;DrawerMotion.Show(menu);}else detail.Show("calendar","캘린더",body);await Reload();try{await store.Refresh(false);if(request!=generation||menu!=null&&(menu.Child!=menuContent||!menu.IsVisible))return;status.Text="";await Reload(false);}catch(Exception error){if(request!=generation||menu!=null&&(menu.Child!=menuContent||!menu.IsVisible))return;status.Text=error.Message;status.Visibility=Visibility.Visible;}}
 static Brush B(string c){return (Brush)new BrushConverter().ConvertFromString(c);}
 static string Category(CalendarEntry e){if((e.Id??"").Contains("card_pool_list:"))return "기원";if((e.Id??"").StartsWith("fixed_act_list:"))return "도전";if((e.Id??"").StartsWith("act_list:"))return "이벤트";return e.Source=="manual"?"내 일정":"일상";}
 static Brush Tint(CalendarEntry e){switch(Category(e)){case "기원":return B("#E8C66A");case "이벤트":return B("#C895E8");case "도전":return B("#85B5EE");case "내 일정":return B("#EBA0A7");default:return B("#82C5A7");}}
 internal static bool OccursOn(CalendarEntry e,DateTime date){return e.Start.HasValue?e.Start.Value.ToLocalTime()<date.Date.AddDays(1)&&e.Due.ToLocalTime()>date.Date:e.Due.ToLocalTime().Date==date.Date;}
 void MoveMonth(int amount){month=new DateTime(month.Year,month.Month,1).AddMonths(amount);day=month.Year==DateTime.Today.Year&&month.Month==DateTime.Today.Month?DateTime.Today:month;Render();}
 Button DayButton(DateTime date,List<CalendarEntry> data){
  bool today=date==DateTime.Today,selected=day==date;
  var layout=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};
  var circle=new Border{Width=26,Height=26,CornerRadius=new CornerRadius(13),Background=today?B("#DA5B60"):selected?B("#3D6156"):Brushes.Transparent,BorderBrush=selected&&today?Brushes.White:Brushes.Transparent,BorderThickness=new Thickness(1)};
  circle.Child=new TextBlock{Text=date.Day.ToString(),FontSize=15,FontWeight=today||selected?FontWeights.SemiBold:FontWeights.Normal,Foreground=today||selected?Brushes.White:date.DayOfWeek==DayOfWeek.Sunday||date.DayOfWeek==DayOfWeek.Saturday?B("#9EA6B1"):B("#EDF0F3"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  layout.Children.Add(circle);
  var markers=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Height=12,Margin=new Thickness(0,3,0,0)};
  foreach(var group in data.Where(e=>OccursOn(e,date)).GroupBy(Category))markers.Children.Add(new System.Windows.Shapes.Ellipse{Width=5,Height=5,Fill=Tint(group.First()),Margin=new Thickness(2,0,2,0),VerticalAlignment=VerticalAlignment.Center,ToolTip=group.Key});
  layout.Children.Add(markers);
  var button=new Button{Content=layout,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Margin=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,ToolTip=date.ToString("m",Locale.Culture)+(today?" ("+Locale.T("오늘")+")":"")};
  button.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='Surface' Background='Transparent' CornerRadius='{DynamicResource SurfaceCorners}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Opacity' Value='0.75'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
  button.Click+=(s,e)=>{day=date;Render();};return button;
 }
 async Task Reload(bool navigate=true){int request=++readGeneration,owner=generation;string account=CalendarStore.AccountScope(Setup.DataFolder);try{var data=await Task.Run(()=>store.Read());if(request!=readGeneration||owner!=generation||menu!=null&&(menu.Child!=menuContent||!menu.IsVisible))return;if(account!=CalendarStore.AccountScope(Setup.DataFolder)){var pending=Reload(navigate);return;}currentEntries=data;cachedAccount=account;hasRead=true;Render(navigate);}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.DataRefreshFailure,error);if(request==readGeneration&&owner==generation){status.Text=error.Message;status.Visibility=Visibility.Visible;}}}
 void Render(bool navigate=true){
  if(hasRead&&cachedAccount!=CalendarStore.AccountScope(Setup.DataFolder)){hasRead=false;var pending=Reload(navigate);}
  entries.Children.Clear();if(!hasRead){entries.Children.Add(PanelUi.Text(Locale.T("불러오는 중…"),true));return;}status.Visibility=string.IsNullOrWhiteSpace(status.Text)?Visibility.Collapsed:Visibility.Visible;var data=currentEntries;
  var prev=PanelUi.Button("");var next=PanelUi.Button("");
  foreach(var button in new[]{prev,next}){button.Width=26;button.MinWidth=0;button.Margin=new Thickness(0);button.Padding=new Thickness(0);button.Background=Brushes.Transparent;button.Content=PanelUi.Chevron();((FrameworkElement)button.Content).RenderTransformOrigin=new Point(.5,.5);}
  ((FrameworkElement)prev.Content).RenderTransform=new RotateTransform(90);((FrameworkElement)next.Content).RenderTransform=new RotateTransform(-90);
  prev.Click+=(s,e)=>MoveMonth(-1);next.Click+=(s,e)=>MoveMonth(1);
  var heading=new DockPanel{Margin=new Thickness(0,12,0,8)};var nav=new StackPanel{Orientation=Orientation.Horizontal};
  var today=PanelUi.Button(Locale.T("오늘"));today.MinWidth=0;today.Margin=new Thickness(0,0,8,0);today.Click+=(s,e)=>{month=DateTime.Today;day=DateTime.Today;Render();};
  entries.Children.Add(today);nav.Children.Add(prev);nav.Children.Add(next);DockPanel.SetDock(nav,Dock.Right);heading.Children.Add(nav);
  heading.Children.Add(new TextBlock{Text=month.ToString("Y",Locale.Culture),FontSize=16,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});entries.Children.Add(heading);
  var first=new DateTime(month.Year,month.Month,1);int count=DateTime.DaysInMonth(month.Year,month.Month),weeks=((int)first.DayOfWeek+count+6)/7;
  var grid=new Grid{Margin=new Thickness(0,0,0,18)};
  for(int i=0;i<7;i++)grid.ColumnDefinitions.Add(new ColumnDefinition());
  grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(32)});
  for(int i=0;i<weeks;i++)grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(48)});
  string[] names=Locale.Culture.DateTimeFormat.AbbreviatedDayNames;
  for(int i=0;i<7;i++){var label=new TextBlock{Text=names[i],Foreground=B("#9EA6B1"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(label,i);grid.Children.Add(label);}
  for(int n=0;n<count;n++){int cell=(int)first.DayOfWeek+n;var button=DayButton(first.AddDays(n),data);Grid.SetColumn(button,cell%7);Grid.SetRow(button,cell/7+1);grid.Children.Add(button);}entries.Children.Add(grid);
  agenda.Children.Clear();
  var selected=data.Where(e=>OccursOn(e,day.Value)).ToList();
  
  foreach(var entry in selected){
   var e=entry;var box=EntryCard(e);var content=(StackPanel)box.Child;
   if(e.Source=="manual"){var done=PanelUi.Button(Locale.T(e.Done?"완료 취소":"완료"));done.Margin=new Thickness(0,12,0,0);done.Click+=(s,a)=>{store.SetDone(e.Id,!e.Done);var pending=Reload();};content.Children.Add(done);var change=PanelUi.Button(Locale.T("수정"));change.Click+=(s,a)=>Edit(e);var remove=PanelUi.Button(Locale.T("삭제"));remove.Click+=(s,a)=>{if(MessageBox.Show(Locale.T("이 일정과 반복을 삭제할까요?"),Locale.T("일정 삭제"),MessageBoxButton.YesNo)==MessageBoxResult.Yes){store.Remove(e.Id);var pending=Reload();}};content.Children.Add(PanelUi.Actions(change,remove));}agenda.Children.Add(box);
  }
  if(selected.Count==0)agenda.Children.Add(PanelUi.Text(Locale.T("등록된 일정이 없습니다."),true));
  if(menu!=null){if(navigate)detail.Show("calendar-day",day.Value.ToString("m",Locale.Culture),agenda);}else {if(agenda.Parent is Panel)((Panel)agenda.Parent).Children.Remove(agenda);entries.Children.Add(agenda);}
 }
 internal static DockPanel Timing(CalendarEntry e){
   string period=e.Start.HasValue?e.Start.Value.ToLocalTime().ToString("M/d HH:mm")+" – "+e.Due.ToLocalTime().ToString("M/d HH:mm"):e.Due.ToLocalTime().ToString("HH:mm");
   var timing=new DockPanel();var remaining=new TextBlock{Text=CalendarStore.Remaining(e),Foreground=e.Done?B("#82C5A7"):B("#BAC2CC"),FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0)};
   timing.ToolTip=(e.Source=="season-estimate"?Locale.T("시즌 일정·완료 상태 미확인"):e.Source=="hoyolab"?"HoYoLAB":e.Source=="manual"?Locale.T("직접 등록"):Locale.T("실시간 메모 기준 예상"))+Environment.NewLine+e.Observed.ToLocalTime().ToString("g",Locale.Culture);
   DockPanel.SetDock(remaining,Dock.Right);timing.Children.Add(remaining);timing.Children.Add(new TextBlock{Text=period,Foreground=B("#9EA6B1"),FontSize=12,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap});return timing;
 }
 internal static Border EntryCard(CalendarEntry e){
   var content=new StackPanel();
   content.Children.Add(new TextBlock{Text=Locale.T(Category(e)),Foreground=Tint(e),FontSize=11,Margin=new Thickness(0,0,0,6)});
   content.Children.Add(new TextBlock{Text=e.Title,FontSize=14,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
   content.Children.Add(Timing(e));
   var box=new Border{Background=B("#303640"),BorderBrush=B("#49515D"),BorderThickness=new Thickness(1),CornerRadius=PanelUi.Corners,Padding=new Thickness(16),Margin=new Thickness(0,0,0,8),Child=content,ToolTip=(e.Source=="manual"?Locale.T("직접 등록"):e.Source=="estimate"?Locale.T("실시간 메모 기준 예상"):"HoYoLAB")+Environment.NewLine+e.Observed.ToLocalTime().ToString("g",Locale.Culture)};
   return box;
 }
 void Edit(CalendarEntry original=null){++generation;var body=new StackPanel();var title=new TextBox{MaxLength=160,Text=original==null?"":original.Title};var date=new DatePicker{SelectedDate=original==null?(day??DateTime.Today):original.Due.ToLocalTime().Date};var time=new TextBox{Text=original==null?"04:00":original.Due.ToLocalTime().ToString("HH:mm"),MaxLength=5};var repeat=new ComboBox{ItemsSource=Locale.Options("반복 없음","매일","매주","매월"),SelectedIndex=original==null?0:Math.Max(0,Array.IndexOf(new[]{"none","daily","weekly","monthly"},original.Repeat))};repeat.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");var error=PanelUi.Text("",true);body.Children.Add(PanelUi.InputField(Locale.T("일정"),title));body.Children.Add(PanelUi.InputField(Locale.T("날짜"),date));body.Children.Add(PanelUi.InputField(Locale.T("시간"),time));body.Children.Add(PanelUi.InputField(Locale.T("반복"),repeat));body.Children.Add(error);var add=PanelUi.Button(Locale.T(original==null?"일정 추가":"저장"));add.Click+=(s,e)=>{TimeSpan clock;if(!date.SelectedDate.HasValue||!TimeSpan.TryParse(time.Text,out clock)||clock<TimeSpan.Zero||clock>=TimeSpan.FromDays(1)){error.Text=Locale.T("날짜와 시간을 확인해 주세요.");return;}try{store.Save(new CalendarEntry{Id=original==null?null:original.Id,Done=original!=null&&original.Done,Title=title.Text.Trim(),Repeat=new[]{"none","daily","weekly","monthly"}[repeat.SelectedIndex],Due=DateTime.SpecifyKind(date.SelectedDate.Value.Date+clock,DateTimeKind.Local).ToUniversalTime()});Show();}catch(Exception ex){error.Text=ex.Message;}};body.Children.Add(add);detail.Show("calendar-add",Locale.T(original==null?"일정 추가":"일정 수정"),body,back:()=>Render());}
}

