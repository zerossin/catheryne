using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

// One navigation surface; workers and account imports remain separate integrations.
internal sealed class CompanionPanel {
 readonly Border host;
 readonly Window window;
 readonly StoryPanel story;
 readonly AchievementPanel achievements;
 readonly DetailPanel detail;
 internal DetailPanel Details {get{return detail;}}
 readonly DailyPanel daily;
 readonly PrimogemPanel primogems;
 readonly GoalPanel goals;
 readonly StackPanel accountContent=new StackPanel();
 internal Action<string> Ask;
 readonly InventoryPanel inventory=new InventoryPanel();
 static string AccountSection(string page){return page=="Characters"?"characters":page=="Weapons"?"weapons":page=="Artifacts"?"artifacts":page=="Materials"?"materials":null;}
 static bool AccountPage(string page){return AccountSection(page)!=null;}
 readonly StackPanel accountPages=new StackPanel();
 readonly ProfileStore profiles=new ProfileStore(Setup.DataFolder);
 readonly Button exportFile=PanelUi.Button(Locale.T("결과 내보내기"));
 readonly Button importFile=PanelUi.Button(Locale.T("결과 불러오기"));
 readonly StackPanel footer=new StackPanel();
 readonly TextBlock title=new TextBlock();
 readonly TextBlock description=PanelUi.Text("",true);
 readonly Action closed;bool windowClosed;readonly System.Threading.SemaphoreSlim fileJobs=new System.Threading.SemaphoreSlim(1,1);
 internal string Current {get;private set;}
 internal CompanionPanel(Window window,Action closed) {
  this.window=window;
  this.closed=closed;
  detail=new DetailPanel(window);achievements=new AchievementPanel(window,Setup.DataFolder);goals=new GoalPanel(detail);goals.OpenGoals=()=>Show("Today");story=new StoryPanel(window,detail);daily=new DailyPanel(window);primogems=new PrimogemPanel(window,Setup.DataFolder);window.Closed+=(s,e)=>{windowClosed=true;daily.Dispose();};
  inventory.Review=key=>new BuildReview(Setup.DataFolder,key,detail,question=>{if(Ask!=null)Ask(question);}).Show();
  inventory.Plan=key=>goals.Cultivation(key);inventory.Ask=question=>{if(Ask!=null)Ask(question);};
  host=(Border)window.FindName("CompanionPage");
  description.Margin=new Thickness(0,0,0,20);
  var content=new StackPanel();
  accountContent.Children.Add(inventory.View);
  content.Children.Add(goals.View);goals.View.Visibility=Visibility.Collapsed;
  accountPages.Children.Add(accountContent);accountPages.Children.Add(achievements.View);
  daily.View.Children.Insert(0,IntegratedWorkflows.Routines());
  content.Children.Add(primogems.View);primogems.View.Visibility=Visibility.Collapsed;content.Children.Add(accountPages);content.Children.Add(story.View);content.Children.Add(daily.View);daily.View.Visibility=Visibility.Collapsed;
  footer.Children.Add(story.Footer);
  importFile.Click+=(s,e)=>Import();exportFile.Click+=(s,e)=>{var menu=PanelUi.Menu();foreach(string kind in new[]{"account","achievements"}){string selected=kind;var item=new MenuItem{Header=Locale.T(kind=="account"?"계정 자료":"업적 자료")};item.Click+=(sender,args)=>Export(selected);menu.Items.Add(item);}menu.PlacementTarget=exportFile;menu.IsOpen=true;};
  foreach(var c in scanAreas.Concat(new[]{achievementArea})){c.Checked+=(s,e)=>{RenderCollection();};c.Unchecked+=(s,e)=>{RenderCollection();};}
  foreach(var combo in new[]{weaponRarity,weaponLevel,artifactRarity,artifactLevel})combo.SelectionChanged+=(s,e)=>{RenderCollection();};
  for(int i=0;i<collectionModes.Length;i++){int index=i;collectionModes[i].GroupName="CollectionMode";collectionModes[i].Checked+=(s,e)=>{collectionMode=index;collectionError="";RenderCollection();};}
  collectionModes[0].ToolTip=Locale.T("연결 상태와 저장된 자료를 확인해 필요한 부분만 최신화합니다.");
  refreshStart.Click+=async(s,e)=>await RefreshSelection();
  host.Child=PanelUi.Shell(title,content,footer,()=>{bool primary=Current=="Today";Hide(true);if(primary)closed();},true,description);
  host.IsVisibleChanged+=(s,e)=>{if(host.Visibility!=Visibility.Visible){achievements.Active=false;story.Visible(false);Current=null;}};

 }
 internal void ShowAccountRecords(){Show("Characters");}
 internal void ShowMods(){ModPanel.Show(window,detail);}
 int pageGeneration;
 internal void Show(string page) {
  if(page=="Collection"){ShowCollection();return;}
  if(page=="Cultivation"){goals.Cultivation();return;}
  if(Current==page&&host.Visibility==Visibility.Visible)return;
  PanelNavigation.Owner(host,page);
  LauncherWindowLayout.Primary(window,host,false);
  string heading,body;
  switch(page) {
   case "Today": heading=Locale.T("목표와 오늘 할 일");body=Locale.T("장기 목표를 모으고 오늘 할 일을 준비합니다.");break;
   case "Primogems": heading=Locale.T("원석 명세서");body="";break;
   case "Daily": heading=Locale.T("일상");body=Locale.T("HoYoLAB 출석과 일상 기록을 관리합니다.");break;
   case "Story": heading=Locale.T("스토리");body=Locale.T("스토리 진행을 확인하고 AI가 임무를 자동으로 수행하도록 관리합니다.");break;
   case "Achievements": heading=Locale.T("업적");body=Locale.T("업적 현황과 공략을 확인합니다. 필요하면 AI가 남은 업적을 수행하도록 관리합니다.");break;
   case "Characters": heading=Locale.T("캐릭터");body="";break;
   case "Weapons": heading=Locale.T("무기");body="";break;
   case "Artifacts": heading=Locale.T("성유물");body="";break;
   case "Materials": heading=Locale.T("재료");body="";break;
   default:throw new ArgumentException("Unknown page");
  }
  int request=++pageGeneration;Current=page;title.Text=heading;description.Text=body;description.Visibility=page=="Achievements"||page=="Primogems"||string.IsNullOrEmpty(body)?Visibility.Collapsed:Visibility.Visible;
  primogems.View.Visibility=page=="Primogems"?Visibility.Visible:Visibility.Collapsed;if(page=="Primogems")primogems.Open();
  goals.View.Visibility=page=="Today"?Visibility.Visible:Visibility.Collapsed;
  story.View.Visibility=page=="Story"?Visibility.Visible:Visibility.Collapsed;story.Footer.Visibility=story.View.Visibility;
  daily.View.Visibility=page=="Daily"?Visibility.Visible:Visibility.Collapsed;accountPages.Visibility=page=="Primogems"||page=="Today"||page=="Daily"||page=="Story"?Visibility.Collapsed:Visibility.Visible;story.Visible(page=="Story");
  RenderScan();
  SetContent();achievements.Active=page=="Achievements";if(achievements.Active)achievements.Refresh();
  ((Border)window.FindName("SettingsPanel")).Visibility=Visibility.Collapsed;
  DrawerMotion.Show(host);
  if(AccountPage(page))inventory.Show(AccountSection(page));
 }
 void SetContent(){
  accountContent.Visibility=AccountPage(Current)?Visibility.Visible:Visibility.Collapsed;
  achievements.View.Visibility=Current=="Achievements"?Visibility.Visible:Visibility.Collapsed;
 }
 async void Import(){
  var picker=new Microsoft.Win32.OpenFileDialog {Filter=Locale.T("JSON 자료|*.json"),Title=Locale.T("결과 불러오기")};
  if(picker.ShowDialog(window)!=true)return;
  await fileJobs.WaitAsync();try{await Task.Run(()=>profiles.ImportDetected(picker.FileName));if(windowClosed)return;collectionError=Locale.T("결과를 불러왔습니다.");RenderCollection();}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.CollectionFailure,error);if(windowClosed)return;collectionError=Locale.T("가져오기 실패: ")+error.Message;RenderCollection();MessageBox.Show(window,error.Message,Locale.T("결과 불러오기"));}finally{fileJobs.Release();}
 }
 async void Export(string kind){var picker=new Microsoft.Win32.SaveFileDialog{Filter="JSON|*.json",FileName=kind+".json"};if(picker.ShowDialog(window)!=true)return;await fileJobs.WaitAsync();try{await Task.Run(()=>profiles.Export(kind,picker.FileName));if(windowClosed)return;collectionError=Locale.T("결과를 내보냈습니다.");RenderCollection();}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.CollectionFailure,error);if(windowClosed)return;collectionError=error.Message;RenderCollection();MessageBox.Show(window,error.Message,Locale.T("결과 내보내기"));}finally{fileJobs.Release();}}
 readonly RadioButton[] collectionModes=new[]{PanelUi.ChoiceCard(Locale.T("자동")),PanelUi.ChoiceCard(Locale.T("전체")),PanelUi.ChoiceCard(Locale.T("사용자 설정"))};
 int collectionMode;
 string collectionError="";
 DateTime collectionOpened;
 readonly CheckBox achievementArea=new CheckBox{Content=Locale.T("업적")};
 bool refreshPreparing;
 readonly Button refreshStart=PanelUi.Button(Locale.T("최신화 시작"));
 readonly TextBlock refreshStatus=PanelUi.Text("",true);
 Expander collectionOptions;
 readonly StackPanel collectionHistory=new StackPanel();
 readonly System.Collections.Generic.Dictionary<string,TextBlock> collectionDates=new System.Collections.Generic.Dictionary<string,TextBlock>();
 string SelectionKind(){return collectionMode==0?"auto":collectionMode==1?"all":achievementArea.IsChecked==true?(scanAreas.Any(c=>c.IsChecked==true)?"all":"achievements"):"account";}
 System.Collections.Generic.Dictionary<string,object> Selection(){string kind=SelectionKind();var p=collectionMode!=2?new System.Collections.Generic.Dictionary<string,object>():ScanParameters(kind=="all"?"account":kind);p["kind"]=kind;return p;}

 internal void ShowCollection(){
  Hide();collectionOpened=DateTime.UtcNow;collectionError="";var body=new StackPanel();
  var modes=new Grid{Margin=new Thickness(0,0,0,20)};
  for(int i=0;i<collectionModes.Length;i++){
   modes.ColumnDefinitions.Add(new ColumnDefinition());var card=collectionModes[i];if(card.Parent is Panel)((Panel)card.Parent).Children.Remove(card);
   Grid.SetColumn(card,i);card.Margin=new Thickness(i==0?0:4,0,i==2?0:4,0);modes.Children.Add(card);
  }
  body.Children.Add(modes);
  var advanced=new StackPanel();var checks=scanAreas.Concat(new[]{achievementArea}).ToArray();
  foreach(var check in checks){if(check.Parent is Panel)((Panel)check.Parent).Children.Remove(check);check.Margin=new Thickness(0,0,0,12);advanced.Children.Add(check);}
  advanced.Children.Add(ScanOptions());collectionOptions=PanelUi.Details(Locale.T("고급 설정"),advanced);collectionOptions.IsExpanded=false;collectionOptions.ToolTip=Locale.T("게임 언어 영어 / 화면 16:9 또는 16:10 / HDR 끄기");body.Children.Add(collectionOptions);
  if(refreshStatus.Parent is Panel)((Panel)refreshStatus.Parent).Children.Remove(refreshStatus);body.Children.Add(refreshStatus);
  if(refreshStart.Parent is Panel)((Panel)refreshStart.Parent).Children.Remove(refreshStart);PanelUi.PrimaryAction(refreshStart);body.Children.Add(PanelUi.Actions(refreshStart));
  var files=new StackPanel();if(importFile.Parent is Panel)((Panel)importFile.Parent).Children.Remove(importFile);if(exportFile.Parent is Panel)((Panel)exportFile.Parent).Children.Remove(exportFile);files.Children.Add(PanelUi.FooterActions(new[]{1,1},importFile,exportFile));files.Children.Add(PanelUi.Details(Locale.T("저장 위치"),PanelUi.Text(Path.Combine(Setup.DataFolder,"profiles"),true)));body.Children.Add(PanelUi.Details(Locale.T("파일 관리"),files));
  var dates=new StackPanel();collectionDates.Clear();
  foreach(string area in CollectionRefresh.Areas){var row=new Grid{Margin=new Thickness(0,0,0,12)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var label=PanelUi.Text(CollectionRefresh.Label(area));label.Margin=new Thickness(0);row.Children.Add(label);var updated=PanelUi.Text("",true);updated.Margin=new Thickness(0);collectionDates[area]=updated;Grid.SetColumn(updated,1);row.Children.Add(updated);dates.Children.Add(row);}
  body.Children.Add(PanelUi.Details(Locale.T("최근 최신화"),dates));
  if(collectionHistory.Parent is Expander)((Expander)collectionHistory.Parent).Content=null;var historyView=PanelUi.Details(Locale.T("최신화 기록"),collectionHistory);historyView.IsExpanded=false;body.Children.Add(historyView);


  detail.Show("collection-refresh",Locale.T("최신화"),body,()=>{Current=null;},owner:"Collection");Current="Collection";
  collectionModes[collectionMode].IsChecked=true;RenderCollection();
 }
 void RenderCollection(){
  if(!detail.IsShowing("collection-refresh"))return;
  var active=scanTasks.Values.FirstOrDefault(t=>t.State=="running");var latest=active??scanTasks.Values.Where(t=>{DateTime started;return DateTime.TryParse(t.Started,out started)&&started.ToUniversalTime()>=collectionOpened;}).OrderByDescending(t=>t.Started).FirstOrDefault();
  bool selected=collectionMode!=2||scanAreas.Any(c=>c.IsChecked==true)||achievementArea.IsChecked==true;
  bool resume=selected&&CollectionRefresh.Pending(Setup.DataFolder,SelectionKind(),CatheryneScanning.AccountScanOptions.Read(Selection()));
  refreshStart.Content=Locale.T(active!=null?"중단":resume?"이어서 최신화":"최신화 시작");
  var checks=CollectionHistory.Checks(Setup.DataFolder);var dates=CollectionHistory.Dates(Setup.DataFolder);foreach(var entry in collectionDates){var check=MaterialInventory.Map(checks,entry.Key);DateTime when;bool known=DateTime.TryParse(CodexChat.S(check,"observedAt"),out when);entry.Value.Text=known?CollectionHistory.Scope(CodexChat.S(check,"scope"))+" · "+when.ToLocalTime().ToString("g",Locale.Culture):Locale.T("최신화 기록 없음");bool due=CollectionRefresh.Due(entry.Key,dates,DateTime.UtcNow);entry.Value.Foreground=due?new SolidColorBrush(Color.FromRgb(229,163,88)):new SolidColorBrush(Color.FromRgb(174,181,190));entry.Value.ToolTip=Locale.T(due?"최근 확인이 필요합니다.":"최근 확인한 자료입니다.")+"\n"+Locale.T(entry.Key=="characters"||entry.Key=="weapons"?"확인 주기: 7일":"확인 주기: 24시간");}
  RenderCollectionHistory();
  importFile.IsEnabled=!scanStarting&&!refreshPreparing&&active==null;
  refreshStart.IsEnabled=!scanStarting&&!refreshPreparing&&(active!=null||collectionMode!=2||scanAreas.Any(c=>c.IsChecked==true)||achievementArea.IsChecked==true);
  if(collectionOptions!=null){collectionOptions.Visibility=collectionMode==2?Visibility.Visible:Visibility.Collapsed;collectionOptions.IsEnabled=active==null;}
  foreach(var mode in collectionModes)mode.IsEnabled=active==null&&!scanStarting&&!refreshPreparing;foreach(var c in scanAreas.Concat(new[]{achievementArea}))c.IsEnabled=active==null;
  refreshStatus.Text=!string.IsNullOrEmpty(collectionError)?collectionError:latest==null?"":latest.Reason;refreshStatus.Visibility=string.IsNullOrEmpty(refreshStatus.Text)?Visibility.Collapsed:Visibility.Visible;
 }
 void RenderCollectionHistory(){
  collectionHistory.Children.Clear();object raw;var history=CollectionHistory.Read(Setup.DataFolder);var events=history.TryGetValue("events",out raw)?CodexChat.Items(raw).Reverse().Take(10).ToArray():new System.Collections.Generic.Dictionary<string,object>[0];
  if(events.Length==0){collectionHistory.Children.Add(PanelUi.Text(Locale.T("저장된 변경 기록이 없습니다."),true));return;}
  for(int i=0;i<events.Length;i++){var item=events[i];var body=new StackPanel();var changes=item.TryGetValue("changes",out raw)?CodexChat.Items(raw).ToArray():new System.Collections.Generic.Dictionary<string,object>[0];var scopes=MaterialInventory.Map(item,"scopes");foreach(var scope in scopes){var lines=changes.Where(x=>CodexChat.S(x,"area")==scope.Key).ToArray();var area=new StackPanel();area.Children.Add(PanelUi.Text(CollectionHistory.Scope(Convert.ToString(scope.Value)),true));if(lines.Length==0)area.Children.Add(PanelUi.Text(Locale.T("확인한 범위에서 변경 없음"),true));else foreach(var line in lines)area.Children.Add(PanelUi.Text(CodexChat.S(line,"text")));body.Children.Add(PanelUi.Section(CollectionRefresh.Label(scope.Key),area));}DateTime at;string heading=DateTime.TryParse(CodexChat.S(item,"at"),out at)?at.ToLocalTime().ToString("g",Locale.Culture):"";var detail=PanelUi.Details(heading+" · "+(changes.Length==0?Locale.T("변경 없음"):Locale.Format("변경 {0}건",changes.Length)),body);detail.IsExpanded=false;collectionHistory.Children.Add(detail);}
 }
 readonly CheckBox[] scanAreas=CatheryneScanning.AccountScanOptions.Areas.Select(x=>new CheckBox{Content=Locale.T(x=="characters"?"캐릭터":x=="weapons"?"무기":x=="materials"?"재료":"성유물"),IsChecked=true,Margin=new Thickness(0,0,20,12)}).ToArray();
 readonly ComboBox weaponRarity=new ComboBox{ItemsSource=new[]{1,2,3,4,5},SelectedIndex=0};
 readonly ComboBox weaponLevel=new ComboBox{ItemsSource=new[]{1,20,40,60,80,90},SelectedIndex=0};
 readonly ComboBox artifactRarity=new ComboBox{ItemsSource=new[]{1,2,3,4,5},SelectedIndex=0};
 readonly ComboBox artifactLevel=new ComboBox{ItemsSource=new[]{0,4,8,12,16,20},SelectedIndex=0};
 FrameworkElement scanOptionsView;
 FrameworkElement ScanOptions(){if(scanOptionsView!=null){if(scanOptionsView.Parent is ContentControl)((ContentControl)scanOptionsView.Parent).Content=null;else if(scanOptionsView.Parent is Panel)((Panel)scanOptionsView.Parent).Children.Remove(scanOptionsView);return scanOptionsView;}
  var body=new StackPanel();foreach(var combo in new[]{weaponRarity,weaponLevel,artifactRarity,artifactLevel})if(combo.Parent is Panel)((Panel)combo.Parent).Children.Remove(combo);
  var weapons=new StackPanel();weapons.Children.Add(PanelUi.InputField(Locale.T("무기 최소 등급"),weaponRarity));weapons.Children.Add(PanelUi.InputField(Locale.T("무기 최소 레벨"),weaponLevel));body.Children.Add(weapons);
  var artifacts=new StackPanel();artifacts.Children.Add(PanelUi.InputField(Locale.T("성유물 최소 등급"),artifactRarity));artifacts.Children.Add(PanelUi.InputField(Locale.T("성유물 최소 레벨"),artifactLevel));body.Children.Add(artifacts);
  weapons.Visibility=scanAreas[1].IsChecked==true?Visibility.Visible:Visibility.Collapsed;artifacts.Visibility=scanAreas[2].IsChecked==true?Visibility.Visible:Visibility.Collapsed;
  scanAreas[1].Checked+=(s,e)=>weapons.Visibility=Visibility.Visible;scanAreas[1].Unchecked+=(s,e)=>weapons.Visibility=Visibility.Collapsed;
  scanAreas[2].Checked+=(s,e)=>artifacts.Visibility=Visibility.Visible;scanAreas[2].Unchecked+=(s,e)=>artifacts.Visibility=Visibility.Collapsed;
  scanOptionsView=body;return scanOptionsView;
 }
 System.Collections.Generic.Dictionary<string,object> ScanParameters(string kind){
  var values=new System.Collections.Generic.Dictionary<string,object>{{"kind",kind}};
  if(kind=="account"){
   values["sections"]=CatheryneScanning.AccountScanOptions.Areas.Where((x,i)=>scanAreas[i].IsChecked==true).ToArray();
   values["minimum_weapon_rarity"]=weaponRarity.SelectedItem;values["minimum_weapon_level"]=weaponLevel.SelectedItem;values["minimum_artifact_rarity"]=artifactRarity.SelectedItem;values["minimum_artifact_level"]=artifactLevel.SelectedItem;
   CatheryneScanning.AccountScanOptions.Read(values);
  }return values;
 }
 readonly System.Collections.Generic.Dictionary<string,AiTaskRecord> scanTasks=new System.Collections.Generic.Dictionary<string,AiTaskRecord>();
 bool scanStarting;
 internal void UpdateTask(AiTaskRecord task){
  if(task!=null&&task.State=="completed"&&(task.Tool=="catheryne_goal_add"||(task.Action??"").StartsWith("goals.")||CalendarStore.ChangedBy(task))&&(goals.View.IsVisible||detail.IsShowing("today-plan")))goals.Refresh();
  if(task.Action=="account.refresh"||task.Action=="collection.refresh"){
   collectionError=task.Reason;RenderCollection();if(task.State=="completed"&&AccountPage(Current))inventory.Show(AccountSection(Current));if(task.State=="completed"&&Current=="Achievements")achievements.Refresh();return;
  }
  if(task.Action!="scanner")return;
  scanTasks[task.Id]=task;RenderScan();
  if(task.State!="running"&&AccountPage(Current))inventory.Show(AccountSection(Current));
  if(task.State!="running"&&Current=="Achievements")achievements.Refresh();
 }
 void RenderScan(){RenderCollection();}
 async System.Threading.Tasks.Task RefreshSelection(){
  if(refreshPreparing||scanStarting)return;
  if(scanTasks.Values.Any(t=>t.State=="running")){await Scan();return;}
  if(collectionMode!=0){await Scan(Selection());return;}
  refreshPreparing=true;collectionError="";RenderCollection();bool connected=false,declined=false;
  try{
   connected=await AccountConnections.For(window).Ensure("hoyolab");declined=!connected;
   if(connected){var task=(AiTaskRecord)await System.Threading.Tasks.Task.Run(()=>new CatheryneTools(Setup.DataFolder).Run("catheryne_execute",new System.Collections.Generic.Dictionary<string,object>{{"action","collection.refresh"},{"parameters",new System.Collections.Generic.Dictionary<string,object>{{"kind","auto"}}}},"launcher",Guid.NewGuid().ToString("N")));collectionError=task.Reason;}
  }catch(Exception error){collectionError=error.Message;}
  finally{refreshPreparing=false;RenderCollection();}
  if(declined){var request=Selection();request["offline"]=true;await Scan(request);}
 }
 async System.Threading.Tasks.Task Scan(System.Collections.Generic.Dictionary<string,object> request=null){
  if(scanStarting)return;
  var displayed=scanTasks.Values.FirstOrDefault(t=>t.State=="running");
  string kind=AccountPage(Current)?"account":"achievements";
  scanStarting=true;collectionError="";RenderScan();
  try{
   var parameters=displayed==null?(request??ScanParameters(kind)):null;
   // A stale Stop click must never turn into a new scan after completion.
   if(displayed!=null){var saved=await System.Threading.Tasks.Task.Run(()=>new AiTaskStore(Setup.DataFolder).Find(displayed.Id));if(saved!=null&&saved.State=="running")CollectionScanner.Stop(Setup.DataFolder,saved.Id);return;}
   var active=await System.Threading.Tasks.Task.Run(()=>new AiTaskStore(Setup.DataFolder).List().FirstOrDefault(t=>t.Action=="scanner"&&t.State=="running"));
   if(active!=null)return;
   var options=CatheryneScanning.AccountScanOptions.Read(parameters);
   bool needsGame=CollectionRefresh.Plan(CollectionRefresh.Kind(parameters),options,CollectionRefresh.Dates(Setup.DataFolder),DateTime.UtcNow).Length>0;
   if(needsGame&&!await GameRequirement.Ensure(window))return;
   await System.Threading.Tasks.Task.Run(()=>new CatheryneTools(Setup.DataFolder).Run("catheryne_execute",new System.Collections.Generic.Dictionary<string,object>{{"action","collection.refresh"},{"parameters",parameters}},"launcher",Guid.NewGuid().ToString("N")));
   // The shared task feed owns UI state; the start response may already be stale.
  }catch(Exception error){collectionError=error.Message;}
  finally{scanStarting=false;RenderCollection();}
 }
 internal void Hide(bool animated=false){++pageGeneration;achievements.Active=false;story.Visible(false);Current=null;if(animated)DrawerMotion.Hide(host);else DrawerMotion.Collapse(host);}
}

internal static class DrawerMotion {
 sealed class MotionState {internal int Version;internal bool Closing;internal UIElement Content;internal string Route;}
 static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Border,MotionState> States=new System.Runtime.CompilerServices.ConditionalWeakTable<Border,MotionState>();
 static readonly Duration OpenDuration=new Duration(TimeSpan.FromMilliseconds(190));
 static readonly Duration CloseDuration=new Duration(TimeSpan.FromMilliseconds(150));
 static TranslateTransform Transform(Border panel) {
  var transform=panel.RenderTransform as TranslateTransform;
  if(transform==null){transform=new TranslateTransform();panel.RenderTransform=transform;}
  return transform;
 }
 internal static bool IsOpen(Border panel){MotionState state;return panel.Visibility==Visibility.Visible&&(!States.TryGetValue(panel,out state)||!state.Closing);}
 internal static void Show(Border panel,string route=null) {
  PanelNavigation.Activate(panel);
  var state=States.GetOrCreateValue(panel);if(panel.Visibility==Visibility.Visible&&!state.Closing&&ReferenceEquals(state.Content,panel.Child)&&state.Route==route)return;state.Content=panel.Child;state.Route=route;state.Closing=false;int version=++state.Version;
  var transform=Transform(panel);
  panel.BeginAnimation(UIElement.OpacityProperty,null);transform.BeginAnimation(TranslateTransform.XProperty,null);
  panel.Opacity=0;transform.X=-36;panel.Visibility=Visibility.Visible;
  var ease=new CubicEase {EasingMode=EasingMode.EaseOut};
  panel.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,new Action(()=>{
   if(version!=state.Version)return;
   panel.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,OpenDuration){EasingFunction=ease});
   transform.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,OpenDuration){EasingFunction=ease});
  }));
 }
 internal static void Collapse(Border panel){if(panel.Visibility==Visibility.Visible&&PanelNavigation.Primary(panel)){var owner=Window.GetWindow(panel);if(owner!=null)PanelNavigation.CloseFeatures(owner);}var state=States.GetOrCreateValue(panel);state.Version++;state.Closing=false;panel.BeginAnimation(UIElement.OpacityProperty,null);var transform=Transform(panel);transform.BeginAnimation(TranslateTransform.XProperty,null);transform.X=0;panel.Opacity=1;panel.Visibility=Visibility.Collapsed;}
 internal static void Hide(Border panel) {
  if(PanelNavigation.Primary(panel)){var owner=Window.GetWindow(panel);if(owner!=null)PanelNavigation.CloseFeatures(owner);}
  if(!panel.IsVisible){Collapse(panel);return;}
  var state=States.GetOrCreateValue(panel);if(panel.Visibility!=Visibility.Visible||state.Closing)return;state.Closing=true;int version=++state.Version;
  var transform=Transform(panel);var ease=new CubicEase {EasingMode=EasingMode.EaseIn};
  var fade=new DoubleAnimation(0,CloseDuration){EasingFunction=ease};
  fade.Completed+=(s,e)=>{if(version!=state.Version)return;Collapse(panel);};
  panel.BeginAnimation(UIElement.OpacityProperty,fade);
  transform.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-28,CloseDuration){EasingFunction=ease});
 }
}

