using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;

// One persistent task surface, accessible independently of the current conversation.
internal sealed class AiWorkspacePanel : IDisposable {
 readonly Window window;readonly CodexChat chat;readonly Action close;
 readonly Button tasksButton;readonly Border surface;readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
 readonly List<AiTaskCard> cards=new List<AiTaskCard>();StackPanel taskList=new StackPanel();readonly DetailPanel detail;
 readonly CatheryneTools tools=new CatheryneTools(Setup.DataFolder);
 readonly Dictionary<string,KeyValuePair<long,AiTaskRecord>> taskSnapshot=new Dictionary<string,KeyValuePair<long,AiTaskRecord>>();long taskCursor;
 bool showing,polling,disposed;string signature;Dictionary<string,object> story=new Dictionary<string,object>();List<AiTaskRecord> latestTasks=new List<AiTaskRecord>();
 internal readonly ListBox Models=new ListBox{MaxHeight=300,Foreground=Brushes.White,Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};
 internal readonly ComboBox Efforts=new ComboBox{MinWidth=85,MaxWidth=110};
 internal readonly Button Usage=PanelUi.Button("",false);
 internal readonly Button ModelButton=PanelUi.Button("",false);
 readonly System.Windows.Controls.Primitives.Popup modelPopup=new System.Windows.Controls.Primitives.Popup{StaysOpen=false,AllowsTransparency=true,Placement=System.Windows.Controls.Primitives.PlacementMode.Top};
 readonly System.Windows.Controls.Primitives.Popup usagePopup=new System.Windows.Controls.Primitives.Popup{StaysOpen=false,AllowsTransparency=true,Placement=System.Windows.Controls.Primitives.PlacementMode.Top};
 readonly PointerSlider effortSlider=new PointerSlider{Minimum=0,TickFrequency=1,IsSnapToTickEnabled=false,Margin=new Thickness(8,16,8,8)};
 readonly Grid effortDots=new Grid{IsHitTestVisible=false,Margin=new Thickness(15,0,15,0)};
 internal Func<string,Task> LoginRequested;
 internal Func<string,Task> ProviderRequested;
 readonly Dictionary<string,CodexChat.ModelCatalog> catalogs=new Dictionary<string,CodexChat.ModelCatalog>();bool modelBlocked;int modelLoadVersion;
 internal event Action<AiTaskRecord> OpenTask;
 AiTaskRecord activeTask;
 bool showingAttention;
 readonly Dictionary<string,string> readAttention=new AiTaskStore(Setup.DataFolder).ReadAttention();
 internal static string ExecutionStatus(AiTaskRecord task,Dictionary<string,object> story){
  if(task.Action!="game_control")return Locale.T(task.State=="blocked"?"확인 필요":task.State=="waiting"?"다음 판단 대기":"진행 중")+"  "+task.Reason;
  if(CodexChat.S(story,"control")=="user")return Locale.T("직접 조작 중");
  if(CodexChat.S(story,"owner")=="external")return Locale.T("계획 실행 중");
  if(CodexChat.S(story,"owner")=="dialogue")return Locale.T("대화 진행 중");
  if(CodexChat.S(story,"owner")=="combat")return Locale.T("전투 보조 실행 중");
  object value;if(story.TryGetValue("assistance",out value)){string state=CodexChat.S(CodexChat.Map(value),"state");if(state=="needs_review"||state=="failed")return Locale.T("확인 필요");if(state=="completed")return Locale.T("결과 확인 중");}
  return Locale.T(task.State=="blocked"?"확인 필요":"진행 중")+"  "+task.Reason;
 }
 internal event Action<AiTaskRecord> TaskUpdated;
 internal event Action<AiTaskRecord,Dictionary<string,object>> ExecutionChanged;
 bool tasksInitialized;
 readonly Button accountButton=PanelUi.Button("");
 readonly System.Windows.Controls.Primitives.Popup accountPopup=new System.Windows.Controls.Primitives.Popup{StaysOpen=false,AllowsTransparency=true,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
 readonly TextBlock effortLabel=PanelUi.Text("",true);
 Dictionary<string,object> tokenUsage;
 static Border PopupCard(UIElement body){return new Border{Child=body,Padding=new Thickness(16),CornerRadius=PanelUi.Corners,Background=new SolidColorBrush(Color.FromRgb(38,41,46)),BorderBrush=new SolidColorBrush(Color.FromRgb(65,69,74)),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,8),MinWidth=260,MaxWidth=360};}
 internal void ResetUsage(){tokenUsage=null;DrawUsage();}
 internal void UpdateUsage(Dictionary<string,object> value){tokenUsage=value;DrawUsage();}
 internal static double? ContextPercent(Dictionary<string,object> value){if(value==null||!value.ContainsKey("modelContextWindow")||value["modelContextWindow"]==null)return null;double limit=Convert.ToDouble(value["modelContextWindow"]);if(limit<=0||!value.ContainsKey("last"))return null;var last=CodexChat.Map(value["last"]);if(!last.ContainsKey("totalTokens"))return null;return Math.Max(0,Math.Min(100,100*Convert.ToDouble(last["totalTokens"])/limit));}
 void DrawUsage(){double? percent=ContextPercent(tokenUsage);var grid=new Grid{Width=30,Height=30};grid.Children.Add(new System.Windows.Shapes.Ellipse{Width=22,Height=22,Stroke=new SolidColorBrush(Color.FromRgb(92,99,106)),StrokeThickness=2});if(percent.HasValue&&percent.Value>0){double angle=Math.Min(359.99,percent.Value*3.6)*Math.PI/180;var geometry=new PathGeometry();var figure=new PathFigure{StartPoint=new Point(15,4)};figure.Segments.Add(new ArcSegment(new Point(15+11*Math.Sin(angle),15-11*Math.Cos(angle)),new Size(11,11),0,percent.Value>50,SweepDirection.Clockwise,true));geometry.Figures.Add(figure);grid.Children.Add(new System.Windows.Shapes.Path{Data=geometry,Stroke=new SolidColorBrush(Color.FromRgb(112,194,165)),StrokeThickness=2});}Usage.Content=grid;Usage.ToolTip=percent.HasValue?Locale.Format("컨텍스트 {0}% 사용",percent.Value.ToString("0",Locale.Culture)):Locale.T("사용량");}
 static string EffortName(string value){if(string.IsNullOrEmpty(value))return "";return value=="xhigh"?"Extra high":char.ToUpperInvariant(value[0])+value.Substring(1);}
 internal sealed class ModelChoice {internal string Provider,Model;}
 static string ProviderTitle(string provider){return provider==AiProviders.Codex?"Codex (ChatGPT)":"Claude";}
 void UpdateModelLabel(){
  var model=models.FirstOrDefault(x=>CodexChat.S(x,"model")==chat.Model);
  var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
  row.Children.Add(new TextBlock{Text=model==null?Locale.T("모델 선택"):CodexChat.S(model,"displayName"),TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=180,VerticalAlignment=VerticalAlignment.Center});
  row.Children.Add(new TextBlock{Text=EffortName(chat.Effort),Foreground=new SolidColorBrush(Color.FromRgb(155,162,172)),Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center});
  var chevron=PanelUi.Chevron();chevron.Margin=new Thickness(6,0,0,0);row.Children.Add(chevron);ModelButton.Content=row;ModelButton.ToolTip=modelBlocked?Locale.T("진행 중인 작업이 끝난 뒤 AI를 변경해 주세요."):ProviderTitle(chat.Provider);effortLabel.Text=EffortName(chat.Effort);
 }
 internal void UpdateAccount(){accountButton.ToolTip=Locale.Format(chat.Connected?"계정 · {0} 연결됨":"계정 · {0} 로그인",chat.ProviderName);RenderModels();}
 Grid AccountRow(string name,bool connected,Action open,Func<Task> disconnect){
  var group=new Grid{Height=44,Margin=new Thickness(0,0,0,8)};group.ColumnDefinitions.Add(new ColumnDefinition());group.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  var button=PanelUi.Button("");button.MinWidth=0;button.Height=44;button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(0);button.Padding=new Thickness(12,0,12,0);
  var row=new DockPanel();var status=new TextBlock{Text=connected?"✓ "+Locale.T("연결됨"):Locale.T("로그인"),Margin=new Thickness(8,0,0,0),Foreground=new SolidColorBrush(connected?Color.FromRgb(112,194,165):Color.FromRgb(180,188,199)),VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(status,Dock.Right);row.Children.Add(status);row.Children.Add(new TextBlock{Text=name,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center});button.Content=row;button.Click+=(s,e)=>{accountPopup.IsOpen=false;open();};group.Children.Add(button);
  if(connected){var action=PanelUi.Button("");action.Content=new TextBlock{Text=Locale.T("연결 해제"),TextWrapping=TextWrapping.NoWrap};action.MinWidth=0;action.Height=32;action.FontSize=11;action.Padding=new Thickness(8,0,8,0);action.Margin=new Thickness(8,0,0,0);action.VerticalAlignment=VerticalAlignment.Center;action.Click+=async(s,e)=>{action.IsEnabled=false;try{await disconnect();accountPopup.IsOpen=false;}catch(Exception error){MessageBox.Show(window,error.Message,"Catheryne");action.IsEnabled=true;}};Grid.SetColumn(action,1);group.Children.Add(action);}
  return group;
 }
 void ShowAccounts(){
  var accounts=AccountConnections.For(window);var body=new StackPanel{Width=300};
  foreach(string provider in AccountConnections.Providers){string kind=provider;bool connected=accounts.Connected(kind);body.Children.Add(AccountRow(AccountConnections.Name(kind),connected,async()=>{try{await accounts.Connect(kind);}catch(Exception error){MessageBox.Show(window,error.Message,"Catheryne");}},()=>accounts.Disconnect(kind)));}
  accountPopup.Child=PopupCard(body);accountPopup.IsOpen=!accountPopup.IsOpen;
 }



 List<Dictionary<string,object>> models=new List<Dictionary<string,object>>();bool binding;
 internal AiWorkspacePanel(Window window,CodexChat chat,Action close,DetailPanel detail){
  this.window=window;this.chat=chat;this.close=close;this.detail=detail;surface=(Border)window.FindName("DetailPanel");
  var accounts=AccountConnections.For(window);accounts.ChatConnected=chat.ProviderConnected;accounts.ChatLogin=async provider=>{if(LoginRequested!=null)await LoginRequested(provider);};accounts.ChatLogout=async provider=>{if(ProviderRequested!=null)await ProviderRequested(provider);await chat.Logout();UpdateAccount();};
  var title=(Grid)window.FindName("TitleBar");tasksButton=PanelUi.Button(Locale.T("실행 현황"));tasksButton.Height=30;tasksButton.MinWidth=90;tasksButton.Margin=new Thickness(8,0,0,0);tasksButton.Visibility=Visibility.Collapsed;tasksButton.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border CornerRadius='15' Background='#354B46' Padding='14,0'><ContentPresenter IsHitTestVisible='False' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");tasksButton.HorizontalAlignment=HorizontalAlignment.Right;tasksButton.VerticalAlignment=VerticalAlignment.Center;tasksButton.IsVisibleChanged+=(s,e)=>{if(tasksButton.IsVisible)tasksButton.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));};((StackPanel)window.FindName("HeaderIndicators")).Children.Add(tasksButton);System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(tasksButton,true);tasksButton.Click+=(s,e)=>{if(activeTask!=null&&activeTask.State=="blocked")ShowTaskList(true);else OpenTaskOrHistory(activeTask);};
  accountButton.Width=32;accountButton.Height=32;accountButton.MinWidth=0;accountButton.Padding=new Thickness(0);accountButton.Margin=new Thickness(0,0,10,0);Grid.SetColumn(accountButton,2);accountButton.HorizontalAlignment=HorizontalAlignment.Right;accountButton.VerticalAlignment=VerticalAlignment.Center;accountButton.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Grid Width='32' Height='32'><Ellipse Fill='#343B45'/><Path Data='M12,11 A4,4 0 1 0 20,11 A4,4 0 1 0 12,11 M9,24 C9,16 23,16 23,24' Stroke='#DAE0E7' StrokeThickness='1.6'/></Grid></ControlTemplate>");title.Children.Add(accountButton);System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(accountButton,true);System.Windows.Automation.AutomationProperties.SetName(accountButton,Locale.T("계정"));accountPopup.PlacementTarget=accountButton;accountButton.Click+=(s,e)=>ShowAccounts();UpdateAccount();
  Efforts.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");Efforts.Height=34;Efforts.Margin=new Thickness(0,0,8,0);
  Usage.MinWidth=0;Usage.Width=34;Usage.Height=36;Usage.Padding=new Thickness(0);Usage.Background=Brushes.Transparent;Usage.Margin=new Thickness(8,0,8,0);
  ModelButton.HorizontalContentAlignment=HorizontalAlignment.Center;ModelButton.MinWidth=0;ModelButton.MaxWidth=300;ModelButton.Height=36;ModelButton.Margin=new Thickness(0);ModelButton.Background=Brushes.Transparent;ModelButton.Padding=new Thickness(8,0,8,0);ModelButton.Click+=async(s,e)=>{modelPopup.IsOpen=!modelPopup.IsOpen;if(modelPopup.IsOpen)await LoadModels();};
  var chooser=new StackPanel{Width=320};Models.Margin=new Thickness(0,0,0,18);chooser.Children.Add(Models);effortLabel.Margin=new Thickness(8,0,8,8);chooser.Children.Add(effortLabel);var effortArea=new Grid{Margin=new Thickness(8,4,8,8)};effortSlider.Margin=new Thickness(0);effortArea.Children.Add(effortSlider);effortArea.Children.Add(effortDots);chooser.Children.Add(effortArea);modelPopup.PlacementTarget=ModelButton;modelPopup.Child=PopupCard(chooser);usagePopup.PlacementTarget=Usage;
  effortDots.SizeChanged+=(s,e)=>DrawEffortDots();effortSlider.Height=38;effortSlider.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Slider'><Grid><Border Height='24' Background='#555B65' CornerRadius='12'/><Track Name='PART_Track'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='24' Background='#4387FA' CornerRadius='12,0,0,12' Margin='0,0,-15,0'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton><Track.Thumb><Thumb Width='30' Height='30'><Thumb.Template><ControlTemplate TargetType='Thumb'><Ellipse Fill='#F5F7FA'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid></ControlTemplate>");
  effortSlider.ValueChanged+=(s,e)=>{if(!effortSlider.Animating&&!binding&&Efforts.Items.Count>0){if(!effortSlider.Dragging)Efforts.SelectedIndex=(int)Math.Round(effortSlider.Value);else effortLabel.Text=EffortName(Convert.ToString(Efforts.Items[(int)Math.Round(effortSlider.Value)]));}DrawEffortDots();};effortSlider.Committed+=()=>{if(!binding&&Efforts.Items.Count>0)Efforts.SelectedIndex=(int)effortSlider.TargetIndex;};DrawUsage();Usage.Click+=async(s,e)=>await ShowUsage();
  Models.SelectionChanged+=async(s,e)=>{
   if(binding)return;var item=Models.SelectedItem as ListBoxItem;var choice=item==null?null:item.Tag as ModelChoice;if(choice==null)return;
   try{await ChooseModel(choice);}catch(Exception error){RenderModels();MessageBox.Show(window,error.Message,"Catheryne");}
  };
  Efforts.SelectionChanged+=(s,e)=>{if(binding)return;chat.Effort=Efforts.SelectedItem as string;AppPreferences.Set(AiProviders.Preference(chat.Provider,"aiEffort"),chat.Effort);UpdateModelLabel();};
  surface.IsVisibleChanged+=(s,e)=>{if(!surface.IsVisible)showing=false;};
  taskSearch.TextChanged+=(s,e)=>{taskLimit=30;signature=null;if(showing)RenderTasks(latestTasks);};taskState.SelectionChanged+=(s,e)=>{taskLimit=30;signature=null;if(showing)RenderTasks(latestTasks);};taskKind.SelectionChanged+=(s,e)=>{taskLimit=30;signature=null;if(showing)RenderTasks(latestTasks);};
  timer.Tick+=async(s,e)=>await Refresh();timer.Start();window.Closed+=(s,e)=>Dispose();
  new AiTaskStore(Setup.DataFolder).Recover();
 }
 internal void SetBusy(bool busy,bool providerBlocked=false){modelBlocked=busy||providerBlocked;ModelButton.IsEnabled=!modelBlocked;Models.IsEnabled=!modelBlocked;Efforts.IsEnabled=!modelBlocked&&chat.Connected;effortSlider.IsEnabled=!modelBlocked&&Efforts.Items.Count>1;ModelButton.ToolTip=modelBlocked?Locale.T("진행 중인 작업이 끝난 뒤 AI를 변경해 주세요."):ProviderTitle(chat.Provider);ToolTipService.SetShowOnDisabled(ModelButton,true);}
 public void Dispose(){disposed=true;++modelLoadVersion;timer.Stop();}
 internal async Task ChooseModel(ModelChoice choice){
  if(modelBlocked)throw new InvalidOperationException(Locale.T("진행 중인 작업이 끝난 뒤 AI를 변경해 주세요."));
  if(ProviderRequested==null)return;
  modelPopup.IsOpen=false;
  if(choice.Model==null){if(LoginRequested==null)return;await LoginRequested(choice.Provider);await LoadModels();return;}
  await ProviderRequested(choice.Provider);
  if(disposed||chat.Provider!=choice.Provider||!chat.Connected)return;
  chat.Model=choice.Model;await Task.Run(()=>AppPreferences.Set(AiProviders.Preference(choice.Provider,"aiModel"),choice.Model));
  await LoadModels();
 }
 void RenderModels(){
  binding=true;try{
   Models.Items.Clear();
   foreach(string provider in new[]{AiProviders.Codex,AiProviders.Claude}){
    var heading=new ListBoxItem{Content=PanelUi.Text(ProviderTitle(provider),true),IsEnabled=false,Padding=new Thickness(8,8,8,4),Focusable=false};Models.Items.Add(heading);
    CodexChat.ModelCatalog catalog;catalogs.TryGetValue(provider,out catalog);
    bool connected=chat.ProviderConnected(provider);var rows=catalog!=null&&connected?catalog.Models:new List<Dictionary<string,object>>();
    if(!connected){Models.Items.Add(new ListBoxItem{Content=Locale.T("로그인"),Tag=new ModelChoice{Provider=provider},Padding=new Thickness(8),ToolTip=catalog==null?null:catalog.Error});continue;}
    if(rows.Count==0){Models.Items.Add(new ListBoxItem{Content=catalog!=null&&catalog.Error!=null?catalog.Error:Locale.T("모델 목록을 불러오는 중…"),IsEnabled=false,Padding=new Thickness(8)});continue;}
    foreach(var model in rows){var choice=new ModelChoice{Provider=provider,Model=CodexChat.S(model,"model")};var item=new ListBoxItem{Content=CodexChat.S(model,"displayName"),Tag=choice,Padding=new Thickness(8)};Models.Items.Add(item);if(provider==chat.Provider&&choice.Model==chat.Model)Models.SelectedItem=item;}
   }
  }finally{binding=false;}UpdateModelLabel();
 }
 Task modelLoading;
 internal Task LoadModels(){
  int version=++modelLoadVersion;string provider=chat.Provider;RenderModels();
  var prefs=Task.Run(()=>AppPreferences.Read());var active=LoadProviderModels(provider,provider,version,prefs);
  var other=LoadProviderModels(provider==AiProviders.Codex?AiProviders.Claude:AiProviders.Codex,provider,version,prefs);
  modelLoading=Task.WhenAll(active,other);return active;
 }
 async Task LoadProviderModels(string kind,string provider,int version,Task<Dictionary<string,object>> preferences){
  CodexChat.ModelCatalog catalog;Dictionary<string,object> prefs;
  try{catalog=await chat.ProviderModels(kind);prefs=await preferences;}catch(OperationCanceledException){return;}catch(Exception error){catalog=new CodexChat.ModelCatalog{Connected=chat.ProviderConnected(kind),Error=error.Message};prefs=new Dictionary<string,object>();}
  if(version!=modelLoadVersion||provider!=chat.Provider||disposed)return;
  catalogs[kind]=catalog;
  if(kind==provider){
   models=chat.Connected?catalog.Models:new List<Dictionary<string,object>>();
   object saved;prefs.TryGetValue(AiProviders.Preference(provider,"aiModel"),out saved);string selected=chat.Model??Convert.ToString(saved);
   var chosen=models.FirstOrDefault(x=>CodexChat.S(x,"model")==selected)??models.FirstOrDefault(x=>x.ContainsKey("isDefault")&&Equals(x["isDefault"],true))??models.FirstOrDefault();chat.Model=chosen==null?null:CodexChat.S(chosen,"model");
   BindEfforts(prefs);
  }RenderModels();
 }
 void DrawEffortDots(){effortDots.Children.Clear();int count=Efforts.Items.Count;for(int i=0;i<count;i++){if(i==(int)Math.Round(effortSlider.Value))continue;var dot=new System.Windows.Shapes.Ellipse{Width=4,Height=4,Fill=new SolidColorBrush(Color.FromArgb(150,235,240,250)),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};double width=Math.Max(0,effortDots.ActualWidth);dot.Margin=new Thickness(count>1?width*i/(count-1)-2:0,0,0,0);effortDots.Children.Add(dot);}}
 void BindEfforts(Dictionary<string,object> prefs){binding=true;try{Efforts.Items.Clear();chat.Effort=null;var model=models.FirstOrDefault(x=>CodexChat.S(x,"model")==chat.Model);if(model!=null){object list;if(model.TryGetValue("supportedReasoningEfforts",out list))foreach(var effort in CodexChat.Items(list))Efforts.Items.Add(CodexChat.S(effort,"reasoningEffort"));object saved;prefs.TryGetValue(AiProviders.Preference(chat.Provider,"aiEffort"),out saved);string desired=Convert.ToString(saved);Efforts.SelectedItem=Efforts.Items.Contains(desired)?desired:CodexChat.S(model,"defaultReasoningEffort");chat.Effort=Efforts.SelectedItem as string;}effortSlider.Maximum=Math.Max(0,Efforts.Items.Count-1);effortSlider.Value=Math.Max(0,Efforts.SelectedIndex);effortSlider.IsEnabled=!modelBlocked&&Efforts.Items.Count>1;effortLabel.Visibility=Efforts.Items.Count>0?Visibility.Visible:Visibility.Collapsed;effortSlider.Visibility=effortLabel.Visibility;effortDots.Visibility=effortLabel.Visibility;DrawEffortDots();UpdateModelLabel();}finally{binding=false;}}
 internal static bool HasConversation(AiTaskRecord task){Guid id;return task!=null&&Guid.TryParse(task.Thread,out id);}
 internal void OpenTaskOrHistory(AiTaskRecord task){if(HasConversation(task)&&OpenTask!=null)OpenTask(task);else ShowTasks();}
 internal Action<string> OpenFeature;
 int taskLimit=30;
 readonly TextBox taskSearch=new TextBox{MinHeight=36};
 readonly ComboBox taskState=new ComboBox{ItemsSource=Locale.Options("모든 상태","진행 중","확인 필요","완료","실패","중단","다음 판단 대기"),SelectedIndex=0,MinWidth=120};
 readonly ComboBox taskKind=new ComboBox{ItemsSource=Locale.Options("모든 작업","수집","반복 작업","육성","게임","설정"),SelectedIndex=0,MinWidth=120};
 string ResultPage(AiTaskRecord t){if(t.Action=="scanner")return "Collection";if(t.Action=="bettergi")return "Daily";if((t.Action??"").StartsWith("hutao"))return "Cultivation";if((t.Action??"").StartsWith("launcher"))return "Graphics";if((t.Action??"").StartsWith("goals"))return "Today";return null;}
 bool MatchesTask(AiTaskRecord t){if(showingAttention)return AiTaskStore.NeedsAttention(t,readAttention);string[] states={"","running","blocked","completed","failed","cancelled","waiting"};if(taskState.SelectedIndex>0&&t.State!=states[taskState.SelectedIndex])return false;string page=ResultPage(t);if(taskKind.SelectedIndex==1&&t.Action!="scanner"||taskKind.SelectedIndex==2&&t.Action!="bettergi"||taskKind.SelectedIndex==3&&page!="Cultivation"||taskKind.SelectedIndex==4&&t.Action!="game_control"||taskKind.SelectedIndex==5&&page!="Graphics")return false;return (t.Title+" "+t.Reason).IndexOf(taskSearch.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)>=0;}
 internal void ShowTasks(){ShowTaskList(false);}
 void ShowTaskList(bool attention){if(showing&&surface.IsVisible&&showingAttention==attention)return;showingAttention=attention;signature=null;taskList=new StackPanel();var body=new StackPanel();foreach(var control in new FrameworkElement[]{taskSearch,taskState,taskKind})if(control.Parent is Panel)((Panel)control.Parent).Children.Remove(control);if(!attention)body.Children.Add(PanelUi.SearchInput(taskSearch,"작업 검색"));var filters=new WrapPanel{Margin=new Thickness(0,12,0,12)};foreach(var combo in new[]{taskState,taskKind}){combo.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");combo.Margin=new Thickness(0,0,8,0);filters.Children.Add(combo);}if(!attention)body.Children.Add(filters);body.Children.Add(taskList);detail.Show(attention?"ai-attention":"ai-tasks",Locale.T(attention?"확인 필요":"작업 기록"),body,()=>showing=false);showingAttention=attention;showing=true;RenderTasks(latestTasks);var pending=Refresh();}
 internal Func<AiTaskRecord,Task> TheaterEnded;internal Action<AiTaskRecord> TheaterJudge;internal Action<AiTaskRecord,string,string> TheaterConfirm;internal Func<bool> CanJudgeTheater;
 internal AiTaskCard Card(AiTaskRecord task){return CreateCard(task,ShowTasks,"작업 기록",null);}
 AiTaskCard CreateCard(AiTaskRecord task,Action open,string openLabel,Action dismiss){AiTaskCard card=null;card=new AiTaskCard(task,open,()=>{close();DailyPanel.LoginCommand.Execute(null,window);},async()=>{try{foreach(var active in card.Request.Operations.Where(AiTaskCard.CanStop).ToArray())await Control("stop",active.Id);}catch(Exception error){card.ShowIssue(error.Message);}},openLabel,dismiss,handoff:async()=>{try{var active=card.Request.Operations.FirstOrDefault(t=>t.Action=="game_control"&&t.State=="running");if(active!=null)await Control("user",active.Id);}catch(Exception error){card.ShowIssue(error.Message);}});if(task.Action=="theater")card.SetJudge(t=>{if(TheaterJudge!=null)TheaterJudge(t);},()=>CanJudgeTheater!=null&&CanJudgeTheater(),(t,choice,identity)=>{if(TheaterConfirm!=null)TheaterConfirm(t,choice,identity);});return card;}
 internal async Task Refresh(){if(disposed||polling)return;polling=true;try{
  var previous=latestTasks;
  var update=await Task.Run(()=>{
   var store=new AiTaskStore(Setup.DataFolder);var changes=store.Changes(taskCursor);var changed=new HashSet<string>();
   foreach(var change in changes){taskSnapshot[change.Value.Id]=change;taskCursor=change.Key;changed.Add(change.Value.Id);}
   var tasks=!tasksInitialized||changes.Count>0?taskSnapshot.Values.OrderByDescending(x=>x.Key).Select(x=>x.Value).ToList():previous;
   var active=tasks.Where(t=>t.Action=="bettergi"&&t.State=="running").Select(t=>new{Task=t,Result=t.Result,State=t.State,Reason=t.Reason}).ToArray();
   new ExternalTools(Setup.DataFolder).RefreshTasks(tasks);
   foreach(var prior in active)if(!ReferenceEquals(prior.Result,prior.Task.Result)||prior.State!=prior.Task.State||prior.Reason!=prior.Task.Reason)changed.Add(prior.Task.Id);
   return new{Tasks=tasks,Changed=changed};
  });
  if(disposed)return;tasksInitialized=true;latestTasks=update.Tasks;RenderTasks(latestTasks,update.Changed);
  var state=await Task.Run(()=>tools.StoryStatus());if(disposed)return;story=CodexChat.Map(state);RenderTasks(latestTasks);

 }catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.TaskReadFailure,error);if(!disposed)tasksButton.ToolTip=Locale.T("실행 기록을 불러오지 못했습니다.");}finally{polling=false;}}
 void RenderTasks(List<AiTaskRecord> data,HashSet<string> changed=null){
  if(changed!=null&&TaskUpdated!=null)foreach(var task in data.Where(t=>changed.Contains(t.Id)))TaskUpdated(task);
  int running=data.Count(x=>x.State=="running");bool live=story.ContainsKey("available")&&Equals(story["available"],true);
  activeTask=data.FirstOrDefault(t=>t.State=="running")??data.FirstOrDefault(t=>AiTaskStore.NeedsAttention(t,readAttention))??data.FirstOrDefault(t=>t.State=="waiting");tasksButton.Visibility=activeTask!=null||live?Visibility.Visible:Visibility.Collapsed;tasksButton.Content=running>0?"● "+Locale.T("진행 중")+(running>1?"  "+running:""):activeTask!=null&&activeTask.State=="waiting"?Locale.T("환상극 판단 대기"):activeTask!=null?Locale.Format("확인 필요 {0}",data.Count(t=>AiTaskStore.NeedsAttention(t,readAttention))):"● "+Locale.T("진행 중");tasksButton.ToolTip=activeTask==null?Locale.T("실행 중인 작업"):activeTask.Title+"\n"+activeTask.Reason;
  if(ExecutionChanged!=null)ExecutionChanged(activeTask,story);
  if(!showing)return;
  string liveKey=live?"|live":"|offline";bool rebuild=false;
  string key=signature==null||changed!=null&&changed.Count>0||!signature.EndsWith(liveKey,StringComparison.Ordinal)?CatheryneTools.Json().Serialize(data.Select(t=>new{t.Id,t.State,t.Title,t.Action,t.Operation,t.Thread,t.TurnId,visible=MatchesTask(t),attention=AiTaskStore.NeedsAttention(t,readAttention)}).ToArray())+liveKey:signature;
  if(signature!=key){signature=key;rebuild=true;taskList.Children.Clear();cards.Clear();
   if(live&&!showingAttention){var box=PanelUi.Section(Locale.T("스토리"),new TextBlock{Name="StorySummary",TextWrapping=TextWrapping.Wrap});var stop=PanelUi.Button(Locale.T("입력 중지"));var handoff=PanelUi.Button(Locale.T("직접 조작"));stop.Click+=async(s,e)=>await Control("stop","story");handoff.Click+=async(s,e)=>await Control("user","story");((StackPanel)box.Child).Children.Add(PanelUi.Actions(stop,handoff));taskList.Children.Add(box);}
   var groups=data.GroupBy(AiTaskRequest.KeyFor).Where(g=>g.Any(MatchesTask)).ToList();
   foreach(var group in groups.Take(taskLimit)){var members=group.OrderBy(t=>t.Started,StringComparer.Ordinal).ToList();var projection=new AiTaskRequest(members[0]);foreach(var member in members.Skip(1))projection.Update(member);var item=projection.Primary;string page=ResultPage(item);Action result=page!=null&&OpenFeature!=null?(Action)(()=>OpenFeature(page)):HasConversation(item)?(Action)(()=>OpenTaskOrHistory(item)):null;var card=CreateCard(members[0],result,Locale.T(page!=null?"결과 보기":"대화 열기"),members.Any(t=>AiTaskStore.NeedsAttention(t,readAttention))?(Action)(async()=>{foreach(var member in latestTasks.Where(t=>AiTaskRequest.KeyFor(t)==group.Key&&AiTaskStore.NeedsAttention(t,readAttention)).ToArray())await DismissAttention(member);}):null);foreach(var member in members.Skip(1))card.Update(member);cards.Add(card);taskList.Children.Add(card);}if(groups.Count>taskLimit){var more=PanelUi.Button(Locale.T("더 보기"));more.Click+=(s,e)=>{taskLimit+=30;signature=null;RenderTasks(latestTasks);};taskList.Children.Add(more);}if(groups.Count==0)taskList.Children.Add(PanelUi.Text(Locale.T(showingAttention?"확인할 알림이 없습니다.":"일치하는 작업이 없습니다."),true));

   if(data.Count==0&&!live&&!showingAttention)taskList.Children.Add(PanelUi.Text(Locale.T("진행 중인 작업이 없습니다."),true));
  }
  var currentGroups=!rebuild&&changed!=null&&changed.Count>0?data.Where(t=>changed.Contains(t.Id)).GroupBy(AiTaskRequest.KeyFor).ToDictionary(g=>g.Key,g=>g.ToArray()):new Dictionary<string,AiTaskRecord[]>();
  foreach(var card in cards){AiTaskRecord[] members;if(currentGroups.TryGetValue(card.Request.Key,out members))foreach(var member in members)card.Update(member);if(card.IsOpen&&card.Request.Operations.Any(t=>t.Action=="game_control"&&CodexChat.S(story,"plan_id")=="ai-"+t.Id))card.SetExecution(story);card.Tick();}
  if(live&&!showingAttention&&taskList.Children.Count>0){var box=taskList.Children[0] as Border;if(box!=null){var text=((StackPanel)box.Child).Children.OfType<TextBlock>().FirstOrDefault(t=>t.Name=="StorySummary");if(text!=null){var p=story.ContainsKey("progress")?CodexChat.Map(story["progress"]):new Dictionary<string,object>();text.Text=CodexChat.S(story,"plan_title")+"\n"+CodexChat.S(story,"stage_title")+"  "+CodexChat.S(story,"mode")+"\n"+CodexChat.S(p,"completed")+Locale.Format(" / {0} 단계",CodexChat.S(p,"total"))+"  "+TimeSpan.FromSeconds(story.ContainsKey("session_seconds")?Convert.ToDouble(story["session_seconds"]):0).ToString(@"hh\:mm\:ss")+"\n"+CodexChat.S(story,"alert");}}}

 }
 async Task DismissAttention(AiTaskRecord task){try{await Task.Run(()=>new AiTaskStore(Setup.DataFolder).DismissAttention(task));readAttention[task.Id]=AiTaskStore.AttentionKey(task);signature=null;RenderTasks(latestTasks);}catch(Exception error){MessageBox.Show(window,error.Message,Locale.T("알림 해제"));}}
 internal async Task Control(string target,string id){var task=await Task.Run(()=>{var active=new AiTaskStore(Setup.DataFolder).Find(id);tools.Run("catheryne_control",new Dictionary<string,object>{{"target",target},{"task_id",id}},active==null?"":active.Thread,Guid.NewGuid().ToString("N"));return active;});if(target=="stop"&&task!=null&&task.Action=="theater"&&TheaterEnded!=null)await TheaterEnded(task);signature=null;await Refresh();}
 static string WindowLabel(Dictionary<string,object> value){object raw;if(!value.TryGetValue("windowDurationMins",out raw)||raw==null)return Locale.T("사용 한도");int mins=Convert.ToInt32(raw);return mins%1440==0?Locale.Format("{0}일 한도",mins/1440):mins%60==0?Locale.Format("{0}시간 한도",mins/60):Locale.Format("{0}분 한도",mins);}
 async Task ShowUsage(){
  if(usagePopup.IsOpen){usagePopup.IsOpen=false;return;}var body=new StackPanel();body.Children.Add(PanelUi.Text(Locale.T("조회 중…"),true));usagePopup.Child=PopupCard(body);usagePopup.IsOpen=true;
  try{var result=await chat.Call("account/rateLimits/read",new{});body.Children.Clear();var percent=ContextPercent(tokenUsage);body.Children.Add(PanelUi.Meter(Locale.T("컨텍스트"),percent.HasValue?percent.Value.ToString("0")+"%":"—",percent,Color.FromRgb(112,194,165)));
   if(tokenUsage!=null&&tokenUsage.ContainsKey("last")){var last=CodexChat.Map(tokenUsage["last"]);Func<string,double> number=k=>last.ContainsKey(k)?Convert.ToDouble(last[k]):0;double input=number("inputTokens"),cache=number("cachedInputTokens"),output=number("outputTokens"),reason=number("reasoningOutputTokens"),total=Math.Max(1,input+output);string[] labels=Locale.Options("입력","캐시 입력","답변","추론");double[] values={Math.Max(0,input-cache),cache,Math.Max(0,output-reason),reason};Color[] colors={Color.FromRgb(145,122,224),Color.FromRgb(93,159,210),Color.FromRgb(112,194,165),Color.FromRgb(217,169,108)};for(int i=0;i<labels.Length;i++)body.Children.Add(PanelUi.Meter(labels[i],values[i].ToString("N0"),values[i]*100/total,colors[i],Locale.T("최근 응답 토큰")));}
   object raw;var buckets=result.TryGetValue("rateLimitsByLimitId",out raw)&&raw!=null?CodexChat.Map(raw):new Dictionary<string,object>{{"codex",result.ContainsKey("rateLimits")?result["rateLimits"]:null}};
   foreach(var bucket in buckets){var d=CodexChat.Map(bucket.Value);foreach(string key in new[]{"primary","secondary"}){object item;if(!d.TryGetValue(key,out item)||item==null)continue;var v=CodexChat.Map(item);if(!v.ContainsKey("usedPercent")||v["usedPercent"]==null)continue;double remaining=Math.Max(0,Math.Min(100,100-Convert.ToDouble(v["usedPercent"])));string reset=v.ContainsKey("resetsAt")&&v["resetsAt"]!=null?new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(Convert.ToDouble(v["resetsAt"])).ToLocalTime().ToString("MM/dd HH:mm"):"—";body.Children.Add(PanelUi.Meter(WindowLabel(v),Locale.Format("{0}% 남음",remaining.ToString("0",Locale.Culture)),remaining,Color.FromRgb(93,159,210),Locale.Format("초기화: {0}",reset)));}}

  }catch(Exception error){body.Children.Clear();body.Children.Add(PanelUi.Text(chat.Provider==AiProviders.Claude?error.Message:Locale.T("사용량을 조회하지 못했습니다. 잠시 후 다시 시도해 주세요."),true));}
 }
}
