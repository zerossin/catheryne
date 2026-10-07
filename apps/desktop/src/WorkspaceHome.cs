using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Presentation only: existing pages retain ownership of data and execution.
internal sealed class WorkspaceHome : IDisposable {
 static readonly Random promptRandom=new Random();
 static readonly string[] promptPool={"오늘 뭐 할까?","레진 어디 쓸까?","육성 도와줘","파티 추천해줘","장비 점검해줘","업적 찾아줘","남은 일정 알려줘","출석 확인해줘","게임 설정 바꿔줘","스토리 진행해줘"};
 string[] welcomePrompts=PickPrompts();
 static string[] PickPrompts(){var choices=(string[])promptPool.Clone();for(int i=0;i<3;i++){int next=promptRandom.Next(i,choices.Length);string swap=choices[i];choices[i]=choices[next];choices[next]=swap;}return choices.Take(3).ToArray();}

 readonly CodexChat chat=new CodexChat(); readonly Action<string> openFeature; readonly DetailPanel detail; readonly CalendarPanel calendar;
 readonly Dictionary<ChatText,string> pendingText=new Dictionary<ChatText,string>();
 readonly System.Windows.Threading.DispatcherTimer typingClock=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(60)};
 readonly Dictionary<string,TextBlock> toolEvents=new Dictionary<string,TextBlock>();
 readonly Dictionary<string,ChatText> replies=new Dictionary<string,ChatText>();
 readonly List<string> selectedFiles=new List<string>();
 internal Func<System.Threading.Tasks.Task<string>> CaptureRequested;
 internal Func<System.Threading.Tasks.Task<SavedCapture>> TheaterCaptureRequested;bool judgingTheater;
 Button send;string connectionHint; readonly AiWorkspacePanel aiPanel; readonly Dictionary<string,AiTaskCard> taskCards=new Dictionary<string,AiTaskCard>();ScrollViewer conversation;int sendingRequest,connectionVersion;bool busy,hasMessages,connecting,recovering,switchingProvider;
 ChatResponseCopy responseCopy;
 ChatMessageActions lastUserActions;Button editRequestButton;string editableTurn;
 CodexChat.InterruptedRequest editingRequest;string savedDraft;int savedCaret;string[] savedFiles;UIElement[] savedPreviews;
 readonly DockPanel editMode=new DockPanel{Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,8)};

 readonly List<AiTaskCard> taskOrder=new List<AiTaskCard>();
 readonly HashSet<string> closedTaskTurns=new HashSet<string>();
 AiTaskFeed taskFeed;string feedThread,currentRequestTurnId;string restoringItem;DateTimeOffset? restoringTime;List<AiTaskRecord> restoredTasks=new List<AiTaskRecord>();
 readonly Window window;
 readonly Button execution=PanelUi.HeaderPill();
 readonly TextBlock executionTitle=new TextBlock{MaxWidth=112,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
 readonly MenuItem executionStop=new MenuItem{Header=Locale.T("중단")},executionHandoff=new MenuItem{Header=Locale.T("직접 조작")};
 AiTaskRecord executionTask;
 readonly Dictionary<string,AiQuestionCard> questionCards=new Dictionary<string,AiQuestionCard>();
 internal Action<AiUserInput> InputNotification,InputResolved;
 internal Action<Dictionary<string,object>> ExecutionNotification;
 internal void ShowTasks(){aiPanel.ShowTasks();}
 internal async void OpenNotification(string thread){if(thread!=null&&thread!=chat.ThreadId&&!await Resume(thread))return;OpenCurrentChat();var question=PendingQuestion();if(question!=null)question.BringIntoView();}
 void ClearQuestions(){if(InputResolved!=null)foreach(var card in questionCards.Values.Where(c=>!c.Resolved))InputResolved(card.Request);questionCards.Clear();}
 AiQuestionCard PendingQuestion(){return questionCards.Values.FirstOrDefault(c=>!c.Resolved&&c.Request.Thread==chat.ThreadId&&c.Request.Blocking)??questionCards.Values.FirstOrDefault(c=>!c.Resolved&&c.Request.Thread==chat.ThreadId);}
 internal bool HasPendingQuestion {get{return PendingQuestion()!=null;}}
 internal void ShowPendingQuestion(){if(HasPendingQuestion)OpenCurrentChat();}
 void OpenCurrentChat(){close();conversationOpen=true;RefreshVisibility();var card=PendingQuestion();if(card!=null)card.BringIntoView();else conversation.ScrollToEnd();}

 internal bool UpdateBlocked {get{return busy||judgingTheater||editingRequest!=null||questionCards.Values.Any(c=>!c.Resolved)||selectedFiles.Count>0||!string.IsNullOrWhiteSpace(draft.Text);}}
 Button collectionLink;
 int collectionRequest;
 async void RefreshCollectionLink(){var link=collectionLink;if(link==null)return;int request=++collectionRequest;string root=Setup.DataFolder;try{var due=await System.Threading.Tasks.Task.Run(()=>CollectionHistory.Due(root,DateTime.UtcNow));if(request!=collectionRequest||link!=collectionLink)return;PanelUi.NavigationLabel(link,Locale.T("최신화"),due.Length>0);link.ToolTip=due.Length>0?Locale.T("최신 확인이 필요한 자료")+": "+string.Join(", ",due.Select(CollectionRefresh.Label)):Locale.T("자료가 최신 상태입니다.");}catch(Exception){if(request==collectionRequest&&link==collectionLink)link.ToolTip=Locale.T("최신화 상태를 확인하지 못했습니다.");}}
 readonly Action close;
 readonly Border host;
 readonly Border menu;
 int conversationRequest;bool resuming;bool historyClosing,conversationOpen,loadingHistory;bool? shaded;
 StackPanel historyList;
 bool homeChatVisible=!AppPreferences.Flag("hideHomeChat");
 readonly WrapPanel attachments=new WrapPanel();
 readonly StackPanel body=new StackPanel();
 readonly StackPanel composer=new StackPanel();
 TextBlock elapsed; DateTime turnStarted;
 readonly System.Windows.Threading.DispatcherTimer activityClock=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
 readonly TextBlock activity=PanelUi.Text("",true);
 readonly StackPanel directory=new StackPanel();
 string menuGroup; bool openingMenu;
 bool historyOpen {get{return menuGroup=="AI"&&DrawerMotion.IsOpen(menu);}}
 readonly TextBox draft=new TextBox {AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=86,MaxHeight=160,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=15};
 readonly Dictionary<string,Button> navigation=new Dictionary<string,Button>();
 internal WorkspaceHome(Window window,Action close,DetailPanel detail,Action<string> openFeature,bool connectAi=true){
  Action<string> historyChanged=root=>{if(root==Setup.DataFolder&&!window.Dispatcher.HasShutdownStarted)window.Dispatcher.BeginInvoke(new Action(RefreshCollectionLink));};CollectionHistory.Changed+=historyChanged;window.Closed+=(s,e)=>CollectionHistory.Changed-=historyChanged;
  this.openFeature=openFeature;this.detail=detail;detail.NavigateMenu=Show;calendar=new CalendarPanel(detail);this.window=window;this.close=close;host=(Border)window.FindName("WorkspaceHome");menu=(Border)window.FindName("WorkspaceMenu");PanelNavigation.Register(window,EnsureMenu,Open);
  var rail=(StackPanel)((Button)window.FindName("Story")).Parent;
  foreach(string name in new[]{"Graphics","Story","Characters","Achievements","Daily"})((Button)window.FindName(name)).Visibility=Visibility.Collapsed;
  AddNavigation(rail,"AI","M6,4 H18 Q21,4 21,7 V14 Q21,17 18,17 H10 L5,21 V17 Q3,17 3,14 V7 Q3,4 6,4 Z",()=>History());
  AddNavigation(rail,"목표","M5,21 V4 M5,5 C10,1 14,9 20,4 V14 C14,19 10,11 5,15",()=>Open("Today"));
  AddNavigation(rail,"내 계정","M4,4.5 C7,3.8 9.6,4.2 12,6 V20 C9.6,18.2 7,17.8 4,18.5 Z M20,4.5 C17,3.8 14.4,4.2 12,6 V20 C14.4,18.2 17,17.8 20,18.5 Z",()=>Show("내 계정"));
  AddNavigation(rail,"플레이","M8,4 L21,12 L8,20 Z",()=>Show("플레이"));
  AddNavigation(rail,"화면·성능","M3,4 H21 V16 H3 Z M8,20 H16 M12,16 V20",()=>Show("화면·성능"));
  AddNavigation(rail,"캘린더","M4,5 H20 V21 H4 Z M4,10 H20 M8,3 V7 M16,3 V7",()=>Show("캘린더"));
  ((Button)window.FindName("Home")).Click+=(s,e)=>Home();
  connectionHint=Locale.Format("{0} 연결 확인 중…",chat.ProviderName);
  var layout=new Grid {Margin=new Thickness(28,12,28,12)};
  layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  aiPanel=new AiWorkspacePanel(window,chat,close,detail);aiPanel.OpenFeature=Open;
  execution.Content=executionTitle;execution.Margin=new Thickness(8,0,0,0);execution.Visibility=Visibility.Collapsed;execution.ToolTip="";
  ((StackPanel)window.FindName("HeaderIndicators")).Children.Add(execution);System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(execution,true);
  execution.Click+=(s,e)=>{if(PendingQuestion()!=null||executionTask==null){OpenCurrentChat();return;}if(executionTask!=null)aiPanel.OpenTaskOrHistory(executionTask);};
  execution.ContextMenu=PanelUi.Menu();execution.ContextMenu.Items.Add(executionHandoff);execution.ContextMenu.Items.Add(executionStop);
  aiPanel.ExecutionChanged+=UpdateExecution;
  executionStop.Click+=async(s,e)=>{var task=executionTask;try{if(task==null){await chat.Interrupt();if(chat.TurnId==null)Finish();return;}if(task.Action=="theater")await aiPanel.Control("stop",task.Id);else if(busy&&task.Thread==chat.ThreadId)await chat.Interrupt();else await aiPanel.Control("stop",task.Id);}catch(Exception error){Message(error.Message,false);}};
  executionHandoff.Click+=async(s,e)=>{var task=executionTask;if(task==null)return;try{await aiPanel.Control("user","story");if(busy&&task.Thread==chat.ThreadId)await chat.Interrupt(false);}catch(Exception error){Message(error.Message,false);}};
  new HomeStatus(window,()=>Open("Daily"),date=>{Show("캘린더");calendar.SelectDate(date);});
  
  activity.Margin=new Thickness(18,4,18,14);activity.Visibility=Visibility.Collapsed;activityClock.Tick+=(s,e)=>UpdateElapsed(false);window.Closed+=(s,e)=>activityClock.Stop();
  body.VerticalAlignment=VerticalAlignment.Center;body.HorizontalAlignment=HorizontalAlignment.Center;var scroll=new ScrollViewer {Content=body,VerticalContentAlignment=VerticalAlignment.Center,VerticalScrollBarVisibility=ScrollBarVisibility.Visible,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,BorderThickness=new Thickness(0),Padding=new Thickness(0),Margin=new Thickness(0,12,0,16)};conversation=scroll;Grid.SetRow(scroll,1);layout.Children.Add(scroll);
  draft.CaretBrush=new SolidColorBrush(Color.FromRgb(224,228,234));draft.Foreground=Brushes.White;draft.IsReadOnlyCaretVisible=true;draft.MinHeight=54;draft.Background=Brushes.Transparent;draft.BorderThickness=new Thickness(0);draft.Padding=new Thickness(4,8,4,8);
  draft.FocusVisualStyle=null;
  draft.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TextBox'><Border Background='{TemplateBinding Background}'><ScrollViewer x:Name='PART_ContentHost' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Focusable='False'/></Border></ControlTemplate>");
  var inputArea=PanelUi.TextInput(draft,Locale.Format("{0} 연결 확인 중…",chat.ProviderName));inputArea.Background=Brushes.Transparent;inputArea.PreviewMouseDown+=(s,e)=>{if(!chat.Connected){e.Handled=true;Connection();}};
  var editTitle=PanelUi.Text(Locale.T("중단한 요청 수정"),true);editTitle.VerticalAlignment=VerticalAlignment.Center;var cancelEdit=PanelUi.Button(Locale.T("취소"));cancelEdit.Margin=new Thickness(8,0,0,0);cancelEdit.HorizontalAlignment=HorizontalAlignment.Right;cancelEdit.Click+=(s,e)=>{if(!busy&&!resuming)CancelEdit();};DockPanel.SetDock(cancelEdit,Dock.Right);editMode.Children.Add(cancelEdit);editMode.Children.Add(editTitle);composer.Children.Add(editMode);
  composer.Children.Add(attachments);composer.Children.Add(inputArea);
  System.Windows.Input.CommandManager.AddPreviewCanExecuteHandler(draft,(s,e)=>{if(e.Command!=System.Windows.Input.ApplicationCommands.Paste)return;try{if(ChatAttachments.CanPaste(Clipboard.GetDataObject())){e.CanExecute=true;e.Handled=true;}}catch(System.Runtime.InteropServices.ExternalException){}});
  System.Windows.Input.CommandManager.AddPreviewExecutedHandler(draft,(s,e)=>{if(e.Command!=System.Windows.Input.ApplicationCommands.Paste)return;try{e.Handled=PasteAttachments(Clipboard.GetDataObject());}catch(Exception error){e.Handled=true;MessageBox.Show(window,error.Message,"Catheryne");}});
  var row=new Grid {Margin=new Thickness(0,6,0,0)};
  for(int column=0;column<5;column++)row.ColumnDefinitions.Add(new ColumnDefinition{Width=column==2?new GridLength(1,GridUnitType.Star):GridLength.Auto});
  send=IconButton("M12,18 V6 M6,12 L12,6 L18,12","보내기",true);send.IsEnabled=false;send.Click+=async(s,e)=>{if(!chat.Connected)Connection();else await SendMessage();};Grid.SetColumn(send,4);row.Children.Add(send);
  draft.PreviewKeyDown+=async(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter&&(System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Shift)==0){e.Handled=true;if(!busy&&send.IsEnabled)await SendMessage();}};
  var controls=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
  var attach=IconButton("M8,14 L15,7 Q19,3 21,7 Q22,9 19,12 L10,21 Q6,24 3,20 Q1,17 5,13 L14,4","파일 첨부",false);
  attach.Click+=(s,e)=>{
   var attachMenu=PanelUi.Menu();var files=new MenuItem{Header=Locale.T("첨부하기")};var capture=new MenuItem{Header=Locale.T("캡처하기")};
   files.Click+=(sender,args)=>{var picker=new Microsoft.Win32.OpenFileDialog{Multiselect=true};if(picker.ShowDialog(window)==true)foreach(string file in picker.FileNames)AttachFile(file);};
   capture.Click+=async(sender,args)=>{attach.IsEnabled=false;try{if(CaptureRequested==null)throw new InvalidOperationException(Locale.T("캡처 기능을 준비하지 못했습니다."));string file=await CaptureRequested();if(file!=null)AttachFile(file);}catch(Exception error){MessageBox.Show(window,error.Message,"Catheryne");}finally{attach.IsEnabled=true;}};
   attachMenu.Items.Add(files);attachMenu.Items.Add(capture);attachMenu.PlacementTarget=attach;attachMenu.IsOpen=true;
  };controls.Children.Add(attach);
  var folder=PanelUi.Button("",false);folder.Background=Brushes.Transparent;folder.MinWidth=0;folder.Margin=new Thickness(6,0,0,0);folder.Padding=new Thickness(8,0,8,0);folder.Height=36;folder.VerticalAlignment=VerticalAlignment.Center;
  var folderLabel=new StackPanel{Orientation=Orientation.Horizontal};folderLabel.Children.Add(Icon("M3,7 H10 L12,10 H21 V20 H3 Z M3,7 V5 H10 L12,7",18));folderLabel.Children.Add(new TextBlock{Text="Catheryne",Foreground=new SolidColorBrush(Color.FromRgb(174,181,190)),Margin=new Thickness(7,0,0,0),VerticalAlignment=VerticalAlignment.Center});folder.Content=folderLabel;folder.ToolTip=chat.Workspace;
  folder.Click+=(s,e)=>{System.IO.Directory.CreateDirectory(chat.Workspace);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(chat.Workspace){UseShellExecute=true});};controls.Children.Add(folder);
  Grid.SetColumn(aiPanel.Providers,1);row.Children.Add(aiPanel.Providers);Grid.SetColumn(aiPanel.ModelButton,2);aiPanel.ModelButton.HorizontalAlignment=HorizontalAlignment.Stretch;row.Children.Add(aiPanel.ModelButton);Grid.SetColumn(aiPanel.Usage,3);row.Children.Add(aiPanel.Usage);
  row.Children.Add(controls);composer.Children.Add(row);  var inputCard=new Border {Child=composer,Background=new SolidColorBrush(Color.FromRgb(38,41,46)),BorderBrush=new SolidColorBrush(Color.FromRgb(65,69,74)),BorderThickness=new Thickness(1),CornerRadius=PanelUi.Corners,Padding=new Thickness(18,12,18,12),MaxWidth=ChatLayout.MaxWidth,Margin=new Thickness(0,0,SystemParameters.VerticalScrollBarWidth,0),HorizontalAlignment=HorizontalAlignment.Stretch};
  new ChatAttachmentDrop(window,inputCard,PasteAttachments,error=>MessageBox.Show(window,error.Message,"Catheryne"));
  Grid.SetRow(inputCard,2);layout.Children.Add(inputCard);host.Child=layout;host.PreviewMouseDown+=(s,e)=>{if(menu.Visibility==Visibility.Visible){conversationOpen=true;close();RefreshVisibility();}};
  foreach(string name in new[]{"CompanionPage","GraphicsPanel","SettingsPanel","SetupPanel","WorkspaceMenu","DetailPanel"})((Border)window.FindName(name)).IsVisibleChanged+=(s,e)=>RefreshVisibility();
  ((Button)window.FindName("Graphics")).Click+=(s,e)=>HideMenu();
  var toggle=(Button)window.FindName("ToggleHomeChat");toggle.Click+=(s,e)=>{homeChatVisible=!homeChatVisible;conversationOpen=false;AppPreferences.Set("hideHomeChat",!homeChatVisible);RefreshVisibility();};
  chat.Notification+=(name,data)=>window.Dispatcher.BeginInvoke(new Action(()=>OnNotification(name,data)));
  typingClock.Tick+=(s,e)=>RenderPending(false);window.Closed+=(s,e)=>Dispose();window.Loaded+=async(s,e)=>{if(connectAi){try{object provider;AppPreferences.Read().TryGetValue("aiProvider",out provider);if(Convert.ToString(provider)==AiProviders.Claude)await chat.SelectProvider(AiProviders.Claude);await CheckConnection();}catch(Exception error){connectionHint=error.Message;draft.ToolTip=error.Message;ApplyConnection();}}};
  menu.IsVisibleChanged+=(s,e)=>{if(!menu.IsVisible){historyClosing=false;RefreshVisibility();}};
  aiPanel.LoginRequested=ConnectProvider;aiPanel.ProviderRequested=SelectProvider;
  aiPanel.OpenTask+=async task=>{if(busy&&task.Thread!=chat.ThreadId)return;if(task.Thread!=chat.ThreadId&&!await Resume(task.Thread))return;close();conversationOpen=true;RefreshVisibility();AiTaskCard card;if(taskCards.TryGetValue(task.Id,out card))await window.Dispatcher.InvokeAsync(new Action(()=>card.BringIntoView()));};
  aiPanel.ExecutionChanged+=(task,story)=>{foreach(var card in taskOrder)if(card.IsOpen&&card.Request.Operations.Any(t=>t.Action=="game_control"&&CodexChat.S(story,"plan_id")=="ai-"+t.Id))card.SetExecution(story);};
  aiPanel.TheaterEnded=async task=>{if(task.Thread!=chat.ThreadId)return;pendingTheaterCapture=null;pendingTheaterThread=null;if(busy&&task.TurnId==(currentRequestTurnId??chat.TurnId))await chat.Interrupt(false);};aiPanel.TheaterJudge=JudgeTheater;aiPanel.TheaterConfirm=ConfirmTheater;aiPanel.CanJudgeTheater=CanJudgeTheater;
  aiPanel.TaskUpdated+=task=>{calendar.UpdateTask(task);if(task.Action=="scanner"||task.Action=="collection.refresh"||task.Action=="account.refresh"||task.Action=="materials.refresh")RefreshCollectionLink();if(TaskUpdated!=null)TaskUpdated(task);if(task.Thread==chat.ThreadId&&hasMessages&&(taskCards.ContainsKey(task.Id)||task.Action=="theater"))OnNotification("catheryne/task",CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(task))));};
  window.SizeChanged+=(s,e)=>{RefreshVisibility();};ApplyConnection();Home();
 }
 void UpdateExecution(AiTaskRecord task,Dictionary<string,object> story){
  executionTask=task;if(ExecutionNotification!=null)ExecutionNotification(story);var question=PendingQuestion();bool goal=CodexChat.S(chat.Goal,"status")=="active";execution.Visibility=task!=null||question!=null||goal?Visibility.Visible:Visibility.Collapsed;
  if(question!=null){executionTitle.Text=Locale.T(question.Request.Blocking?"응답 필요":"선택 질문");execution.ToolTip=string.Join(Environment.NewLine,question.Request.Questions.Select(q=>CodexChat.S(q,"question")));executionHandoff.Visibility=Visibility.Collapsed;executionStop.Visibility=Visibility.Visible;return;}
  if(task==null){if(goal){executionTitle.Text=Locale.T("AI 작업 진행 중");execution.ToolTip=Locale.T("진행 중인 대화 열기");executionHandoff.Visibility=Visibility.Collapsed;executionStop.Visibility=Visibility.Visible;}return;}
  executionStop.Visibility=Visibility.Visible;
  executionTitle.Text=Locale.T(AiTaskCard.Kind(task));execution.ToolTip=task.Title+Environment.NewLine+AiWorkspacePanel.ExecutionStatus(task,story);System.Windows.Automation.AutomationProperties.SetName(execution,Convert.ToString(execution.ToolTip));
  bool user=task.Action=="game_control"&&CodexChat.S(story,"control")=="user";executionHandoff.Visibility=task.Action=="game_control"&&task.State=="running"?Visibility.Visible:Visibility.Collapsed;executionHandoff.IsEnabled=!user;
 }
 void RefreshVisibility(){
  bool blocking=false;
  foreach(string name in new[]{"CompanionPage","GraphicsPanel","SettingsPanel","SetupPanel","DetailPanel"})blocking|=((Border)window.FindName(name)).Visibility==Visibility.Visible;
  AnimateInset(DrawerMotion.IsOpen(menu)&&!LauncherWindowLayout.Compact(window)?LauncherWindowLayout.NavigationWidth(window.ActualWidth)+18:18);
  host.Visibility=!blocking&&(homeChatVisible||historyOpen||historyClosing||conversationOpen)?Visibility.Visible:Visibility.Collapsed;
  ((TextBlock)window.FindName("HeroTitle")).Visibility=homeChatVisible||historyOpen||historyClosing||conversationOpen?Visibility.Collapsed:Visibility.Visible;
  var toggle=(Button)window.FindName("ToggleHomeChat");
  toggle.Visibility=Visibility.Visible;
  AnimateBackground(hasMessages&&host.Visibility==Visibility.Visible);
  toggle.Content=Icon(homeChatVisible?"M5,5 H19 V16 H10 L5,20 Z M3,21 L21,3":"M5,5 H19 V16 H10 L5,20 Z",18);
  toggle.ToolTip=Locale.T(homeChatVisible?"배경만 보기":"홈 채팅 표시");
  System.Windows.Automation.AutomationProperties.SetName(toggle,Convert.ToString(toggle.ToolTip));
  toggle.Background=new SolidColorBrush(homeChatVisible?Color.FromArgb(64,42,48,56):Color.FromArgb(135,65,82,85));
 } void Home(){if(busy){conversationOpen=true;Show("AI");return;}NewConversation();}
 void NewConversation(){CancelEdit();ClearEditAction();responseCopy=null;currentRequestTurnId=null;welcomePrompts=PickPrompts();activityClock.Stop();elapsed=null;restoringImages=null;chat.New();pendingText.Clear();typingClock.Stop();activity.Visibility=Visibility.Collapsed;aiPanel.ResetUsage();hasMessages=false;conversationOpen=false;AnimateBackground(false);ClearQuestions();replies.Clear();toolEvents.Clear();taskCards.Clear();taskOrder.Clear();closedTaskTurns.Clear();taskFeed=null;body.VerticalAlignment=VerticalAlignment.Center;body.HorizontalAlignment=HorizontalAlignment.Center;Show("AI");}
 internal bool PasteAttachments(IDataObject data){var files=ChatAttachments.ReadPaste(data,Setup.DataFolder);if(files==null)return false;foreach(string file in files)AttachFile(file);return true;}
 internal void AttachFile(string file,bool show=false){
  ChatAttachments.Validate(file);file=System.IO.Path.GetFullPath(file);
  if(!selectedFiles.Any(selected=>string.Equals(selected,file,StringComparison.OrdinalIgnoreCase))){selectedFiles.Add(file);FrameworkElement preview=null;preview=ChatAttachments.Composer(ChatAttachment.File(file),window,()=>{selectedFiles.Remove(file);attachments.Children.Remove(preview);});attachments.Children.Add(preview);}
  if(show){close();Show("AI");conversationOpen=true;RefreshVisibility();draft.Focus();}
 }
 internal string CurrentThread {get{return chat.ThreadId;}}
 internal async void PrepareTheater(string question,string thread){if(busy)return;try{if(thread!=null&&thread!=chat.ThreadId){await Resume(thread);if(chat.ThreadId!=thread)return;}close();PrepareQuestion(question);await SendMessage();}catch(Exception error){Message(error.Message,false);}}
 List<ChatAttachment> restoringImages;
 bool CanJudgeTheater(){return !busy&&!resuming&&!judgingTheater&&chat.Connected&&editingRequest==null;}
 async void ConfirmTheater(AiTaskRecord task,string choiceId,string identity){
  if(!CanJudgeTheater())return;judgingTheater=true;foreach(var card in taskOrder)card.Tick();
  try{
   if(task.Thread!=chat.ThreadId){await Resume(task.Thread);if(task.Thread!=chat.ThreadId)return;}
   var state=CodexChat.Map(CodexChat.Map(task.ResultData["theater"])["state"]);var choices=CodexChat.Items(state["choices"]).Select(c=>new Dictionary<string,object>(c)).ToArray();var choice=choices.FirstOrDefault(c=>CodexChat.S(c,"id")==choiceId);var members=CodexChat.Map(state["members"]);if(choice==null||!members.ContainsKey(identity))throw new InvalidOperationException(Locale.T("현재 후보와 참가 명단을 다시 확인해 주세요."));
   string name=CodexChat.S(CodexChat.Map(members[identity]),"name");if(name.Length==0)name=GameCatalog.Name(identity);choice["identity"]=identity;choice["text"]=name;
   var parameters=new Dictionary<string,object>{{"session_id",CodexChat.Map(task.ResultData["theater"])["sessionId"]},{"expected_revision",state["revision"]},{"event_id",Guid.NewGuid().ToString("N")},{"source","user"},{"evidence",Locale.Format("사용자가 {0} 후보의 이름을 {1}로 확인했습니다.",CodexChat.S(choice,"position"),name)},{"delta",new Dictionary<string,object>{{"choices",choices}}}};
   await System.Threading.Tasks.Task.Run(()=>new TheaterService(Setup.DataFolder).Run("observe",parameters,task.Thread));var projected=new AiTaskStore(Setup.DataFolder).Find(task.Id);if(projected!=null)OnNotification("catheryne/task",CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(projected))));
  }catch(Exception error){var card=taskOrder.FirstOrDefault(c=>c.Task.Id==task.Id);if(card!=null)card.ShowIssue(error.Message);}
  finally{judgingTheater=false;foreach(var card in taskOrder)card.Tick();}
 }
 async void JudgeTheater(AiTaskRecord task){
  if(!CanJudgeTheater())return;foreach(var old in taskOrder.Where(c=>c.Task.Id==task.Id))old.ShowIssue(null);judgingTheater=true;foreach(var card in taskOrder)card.Tick();
  try{if(task.Thread!=chat.ThreadId){await Resume(task.Thread);if(task.Thread!=chat.ThreadId)return;}if(TheaterCaptureRequested==null)throw new InvalidOperationException("캡처 기능을 준비하지 못했습니다.");await TheaterCaptureRequested();}
  catch(Exception error){var card=taskOrder.FirstOrDefault(c=>c.Task.Id==task.Id);if(card!=null)card.ShowIssue(error.Message);else Message(error.Message,false);}
  finally{judgingTheater=false;foreach(var card in taskOrder)card.Tick();}
 }
 SavedCapture pendingTheaterCapture;string pendingTheaterThread;
 internal async void TheaterCapture(SavedCapture shot){
  if(resuming||editingRequest!=null||chat.ThreadId==null)return;
  try{
   var active=new TheaterService(Setup.DataFolder).Active(chat.ThreadId);if(active==null)return;var state=TheaterService.Project(active);if(new[]{"ended","completed"}.Contains(CodexChat.S(state,"phase")))return;
   if(busy){pendingTheaterCapture=shot;pendingTheaterThread=chat.ThreadId;return;}
   string previous=draft.Text;string[] previousFiles=selectedFiles.ToArray();selectedFiles.Clear();attachments.Children.Clear();
   AttachFile(shot.Path,true);draft.Text=Locale.T("환상극 선택·결과 화면을 확인해 주세요. 저장된 도전 상태를 읽고, 이 캡처를 catheryne_theater capture의 path로 등록한 뒤 확인된 변화만 observe로 반영하세요. 후보의 화면 위치와 읽힌 이름을 실제 참가 명단과 대조하고, 이름이 불확실하면 추측하지 말고 확인을 요청하세요. 기믹·생존·화력·운용·반응 근거를 먼저 점검하고, 저장된 자료와 계획에서 달라진 부분만 재평가하세요. 캐릭터 보존으로 현재 기믹을 희생하지 말고, 프로그램의 전체 자원 검증과 planningBoard를 참고하되 AI는 현재 후보 2~3개와 영향받은 편성만 비교하세요. 초기 전체 계획 이후에는 battleChanges로 수정된 전투만 제출하고, 그대로 유지할 영향 전투는 reviewedBattles로 확인하세요. 필요한 전투의 근거·대체안은 plan과 battle_ids로 조회하고 고정 자료를 매번 다시 읽지 마세요. 판단이 바뀌면 변경 근거를 남기세요. 그 상태에서 다음 선택·출전 조합, 스킬 순서와 대안을 recommend에 기록해 주세요. 완료한 막 수만 진행도에 반영하고, 작업 상자와 겹치는 긴 목록은 답변에 반복하지 마세요. 전투는 제가 합니다.");
   await SendMessage();if(draft.Text.Length==0)draft.Text=previous;foreach(string file in previousFiles)AttachFile(file);
  }catch(Exception error){Message(error.Message,false);}
 }
 void ContinueTheaterCapture(bool interrupted){
  var shot=pendingTheaterCapture;string thread=pendingTheaterThread;pendingTheaterCapture=null;pendingTheaterThread=null;
  if(interrupted||shot==null||chat.ThreadId!=thread)return;window.Dispatcher.BeginInvoke(new Action(()=>TheaterCapture(shot)));
 }
 internal void PrepareQuestion(string question){
  CancelEdit();Show("AI");conversationOpen=true;RefreshVisibility();draft.Text=string.IsNullOrWhiteSpace(draft.Text)?question:draft.Text+Environment.NewLine+question;draft.CaretIndex=draft.Text.Length;draft.Focus();
 }
 internal void HideMenu(){bool wasHistory=historyOpen;menuGroup=null;if(wasHistory){historyClosing=true;Select(null);}DrawerMotion.Hide(menu);RefreshVisibility();}
 void AddNavigation(StackPanel rail,string name,string geometry,Action action){
  var button=new Button{ToolTip=Locale.T(name)};button.SetResourceReference(FrameworkElement.StyleProperty,"NavigationButton");
  var canvas=new Canvas {Width=24,Height=24};canvas.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),Stroke=new SolidColorBrush(Color.FromRgb(232,232,235)),StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round});var icon=new Viewbox {Child=canvas};icon.SetResourceReference(FrameworkElement.StyleProperty,"NavigationIcon");button.Content=icon;
  System.Windows.Automation.AutomationProperties.SetName(button,Locale.T(name));button.Click+=(s,e)=>action();navigation.Add(name,button);rail.Children.Add(button);
 }
 internal void Select(string page){
  string group=new[]{"Today","Collection","Characters","Weapons","Artifacts","Materials","Achievements","Primogems","Graphics","Mods","Story","Theater","Abyss","Stygian","Daily","Cultivation","ExternalLinks"}.Contains(page)?PanelNavigation.Group(page):page;
  foreach(var item in navigation)item.Value.Background=item.Key==group?new SolidColorBrush(Color.FromRgb(53,64,71)):Brushes.Transparent;
 }
 void CancelResume(){++conversationRequest;chat.CancelResume();resuming=false;loadingHistory=false;ApplyConnection();}
 void Open(string page){CancelResume();if(page=="Today"){close();openFeature(page);}else openFeature(page);Select(page);}
 void Link(string title,string subtitle,string page){
  object icon=GameUiIcons.Create(page);if(icon==null&&subtitle.Length>0)icon=Icon(subtitle,24);if(icon==null){var original=(Button)window.FindName(page);icon=System.Windows.Markup.XamlReader.Parse(System.Windows.Markup.XamlWriter.Save(original.Content));}
  directory.Children.Add(PanelUi.NavigationRow(Locale.T(title),icon,()=>Open(page)));
 }
 internal void EnsureMenu(string page){
  if(openingMenu)return;
  if(menuGroup==page&&menu.Child!=null){DrawerMotion.Show(menu);RefreshVisibility();return;}
  openingMenu=true;menuGroup=page;Select(page);
  try{
   if(page=="캘린더"){calendar.Show(menu,close);return;}
   directory.Children.Clear();
   if(page=="내 계정"){collectionLink=PanelUi.NavigationRow(Locale.T("최신화"),Icon("M20,7 A8,8 0 1 0 20,17 M20,3 V8 H15",24),()=>Open("Collection"));directory.Children.Add(collectionLink);RefreshCollectionLink();Link("캐릭터","","Characters");Link("무기","","Weapons");Link("성유물","","Artifacts");Link("재료","","Materials");Link("업적","","Achievements");directory.Children.Add(PanelUi.NavigationRow(Locale.T("원석 명세서"),GameUiIcons.Create("Primogems"),()=>Open("Primogems")));}
   else if(page=="화면·성능"){Link("화면 설정","","Graphics");directory.Children.Add(PanelUi.NavigationRow(Locale.T("캡처"),Icon("M3,7 H7 L9,4 H15 L17,7 H21 V20 H3 Z M16,13 A4,4 0 1 0 8,13 A4,4 0 1 0 16,13",24),()=>Open("Capture")));directory.Children.Add(PanelUi.NavigationRow(Locale.T("모드"),Icon("M5,5 H11 V11 H5 Z M14,5 H20 V11 H14 Z M5,14 H11 V20 H5 Z M14,14 H20 V20 H14 Z",24),()=>Open("Mods")));}
   else if(page=="설정"){directory.Children.Add(PanelUi.NavigationRow(Locale.T("일반 설정"),Icon("M4,6 H20 M4,12 H20 M4,18 H20",24),()=>Open("SettingsPage")));directory.Children.Add(PanelUi.NavigationRow(Locale.T("설치 관리"),Icon("M4,4 H20 V20 H4 Z",24),()=>Open("Installation")));directory.Children.Add(PanelUi.NavigationRow(Locale.T("외부 링크"),Icon("M10,5 H5 V19 H19 V14 M13,3 H21 V11 M21,3 L10,14",24),()=>Open("ExternalLinks")));}
   else if(page=="목표"){directory.Children.Add(PanelUi.NavigationRow(Locale.T("목표와 오늘 할 일"),Icon("M5,21 V4 M5,5 H20 V14 H5",24),()=>Open("Today")));}
   else{Link("스토리","","Story");directory.Children.Add(PanelUi.NavigationRow(Locale.T("환상극"),Icon("M4,4 H20 V13 C20,23 4,23 4,13 Z M7,10 H9 M15,10 H17 M8,15 Q12,19 16,15",24),()=>Open("Theater")));directory.Children.Add(PanelUi.NavigationRow(Locale.T("나선비경"),Icon("M4,21 L4,15 L8,15 L8,10 L12,10 L12,5 L18,5 L18,21 Z",24),()=>Open("Abyss")));directory.Children.Add(PanelUi.NavigationRow(Locale.T("지맥 제압전"),Icon("M12,2 L21,7 V16 L12,22 L3,16 V7 Z M12,6 V18 M6,9 L18,15 M18,9 L6,15",24),()=>Open("Stygian")));Link("일상","","Daily");directory.Children.Add(PanelUi.NavigationRow(Locale.T("육성"),Icon("M12,3 V21 M3,12 H21",24),()=>Open("Cultivation")));directory.Children.Add(PanelUi.NavigationRow(Locale.T("작업 기록"),Icon("M5,4 H19 V21 H5 Z M8,8 H16 M8,12 H16 M8,16 H13",24),()=>aiPanel.ShowTasks()));}
   menu.Child=null;
   if(directory.Parent is Border)((Border)directory.Parent).Child=null;
   menu.Child=PanelUi.Shell(new TextBlock{Text=Locale.T(page)},directory,new StackPanel(),()=>close());
   DrawerMotion.Show(menu);
  }finally{openingMenu=false;RefreshVisibility();}
 }
 internal void Show(string page){
  CancelResume();
  if(page!="AI"){PanelNavigation.CloseFeatures(window);menu.Width=LauncherWindowLayout.NavigationWidth(window.ActualWidth);EnsureMenu(page);return;}
  close();menu.Width=LauncherWindowLayout.NavigationWidth(window.ActualWidth);RefreshVisibility();Select(page=="AI"?null:page);

  if(hasMessages)return;
  body.Children.Clear();
  string mascot=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"branding","welcome.png");if(System.IO.File.Exists(mascot)){var bitmap=new System.Windows.Media.Imaging.BitmapImage();bitmap.BeginInit();bitmap.UriSource=new Uri(mascot);bitmap.DecodePixelHeight=300;bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.EndInit();var greeting=new Image{Source=bitmap,Height=120,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,16),IsHitTestVisible=false};RenderOptions.SetBitmapScalingMode(greeting,BitmapScalingMode.HighQuality);body.Children.Add(greeting);}
   body.Children.Add(new TextBlock {Text=Locale.T("무엇을 해드릴까요?"),Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=18,ShadowDepth=2,Opacity=1},FontSize=28,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,24)});
  var prompts=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Center};
  foreach(string prompt in welcomePrompts){
   string text=prompt;var button=PanelUi.Button(Locale.T(text));button.Background=new SolidColorBrush(Color.FromArgb(160,64,68,76));button.Click+=(s,e)=>{draft.Text=Locale.T(text);draft.Focus();draft.CaretIndex=draft.Text.Length;};prompts.Children.Add(button);
  }
  body.Children.Add(prompts);
 }
 static FrameworkElement Icon(string path,double size,bool bright=false){var canvas=new Canvas{Width=24,Height=24};canvas.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(path),Stroke=bright?Brushes.White:new SolidColorBrush(Color.FromRgb(174,181,190)),StrokeThickness=2.4,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round});return new Viewbox{Child=canvas,Width=size,Height=size};}
 static Button IconButton(string path,string label,bool circle){var button=new Button{Content=Icon(path,20,circle),Width=36,Height=36,Padding=new Thickness(0),ToolTip=Locale.T(label),Background=Brushes.Transparent};System.Windows.Automation.AutomationProperties.SetName(button,Locale.T(label));if(circle){button.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Grid Width='36' Height='36'><Ellipse Fill='#497C68'/><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid></ControlTemplate>");}return button;}
 void AnimateInset(double left){
  var target=new Thickness(left,LauncherWindowLayout.ChatTop,18,84);
  // The base margin is the current destination; a separate cached destination can become stale.
  if((Thickness)host.GetAnimationBaseValue(FrameworkElement.MarginProperty)==target)return;
  var from=host.Margin;host.BeginAnimation(FrameworkElement.MarginProperty,null);host.Margin=target;
  if(!host.IsVisible||!SystemParameters.ClientAreaAnimation)return;
  host.BeginAnimation(FrameworkElement.MarginProperty,new System.Windows.Media.Animation.ThicknessAnimation(from,target,TimeSpan.FromMilliseconds(240)){EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut},FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop});
 }

 async void History(){
  if(historyOpen)return;CancelResume();PanelNavigation.CloseFeatures(window);historyClosing=false;Select("AI");menu.Width=LauncherWindowLayout.NavigationWidth(window.ActualWidth);
  var list=new StackPanel();historyList=list;var create=PanelUi.Button(Locale.T("새 대화"),false);create.IsEnabled=!busy;create.HorizontalAlignment=HorizontalAlignment.Left;create.Content="＋  "+Locale.T("새 대화");create.Background=Brushes.Transparent;create.Margin=new Thickness(0,0,0,20);create.Click+=(s,e)=>{if(!busy)NewConversation();};list.Children.Add(create);
  menuGroup="AI";menu.Child=PanelUi.Shell(new TextBlock{Text=Locale.T("대화")},list,new StackPanel(),()=>{close();RefreshVisibility();});DrawerMotion.Show(menu);RefreshVisibility();
  try{RenderHistory(await chat.History());}catch(Exception error){list.Children.Add(PanelUi.Text(error.Message,true));}
 }
 internal static string HistoryTitle(Dictionary<string,object> entry){
  string name=CodexChat.S(entry,"name"),preview=CodexChat.S(entry,"preview"),value=string.IsNullOrWhiteSpace(name)?preview:name;
  if(value.StartsWith("catheryne_endgame의 mode=",StringComparison.Ordinal)||value.StartsWith("catheryne_endgame mode=",StringComparison.Ordinal)){foreach(var mode in new[]{"abyss","stygian"})if(preview.StartsWith("catheryne_endgame의 mode="+mode+", command=status로",StringComparison.Ordinal)||preview.StartsWith("catheryne_endgame mode="+mode+", command=status:",StringComparison.Ordinal))return Locale.T(mode=="abyss"?"나선 비경 · 편성 상담":"지맥 제압전 · 편성 상담");}
  int key=preview.IndexOf(" (계정 식별자: ",StringComparison.Ordinal);if(key>0&&value.StartsWith(preview.Substring(0,key)+" (계정 식별자:",StringComparison.Ordinal)&&(preview.Contains(")의 build_analysis와 역할별 평가 기준")||preview.Contains(")를 catheryne_query의 build_analysis에서 조회")))return preview.Substring(0,key)+" · "+Locale.T("육성 상담");
  int englishKey=preview.IndexOf(" (account key: ",StringComparison.Ordinal);if(englishKey>0&&value.StartsWith(preview.Substring(0,englishKey)+" (account key:",StringComparison.Ordinal)&&(preview.Contains("): review build_analysis")||preview.Contains("): query build_analysis in catheryne_query")))return preview.Substring(0,englishKey)+" · "+Locale.T("육성 상담");
  return value;
 }
 void RenderHistory(List<Dictionary<string,object>> entries){
  if(!historyOpen||historyList==null)return;while(historyList.Children.Count>1)historyList.Children.RemoveAt(1);
  foreach(var entry in entries){string id=CodexChat.S(entry,"id"),title=HistoryTitle(entry);if(string.IsNullOrWhiteSpace(title))title=Locale.T("대화");var button=PanelUi.Button("",false);button.Content=new TextBlock{Text=title,TextTrimming=TextTrimming.CharacterEllipsis};button.Height=44;button.Margin=new Thickness(0,0,0,6);button.Background=id==chat.ThreadId?new SolidColorBrush(Color.FromRgb(53,64,71)):new SolidColorBrush(Color.FromRgb(35,38,44));button.HorizontalContentAlignment=HorizontalAlignment.Left;button.HorizontalAlignment=HorizontalAlignment.Stretch;button.ToolTip=title;button.IsEnabled=!busy;button.Click+=async(s,e)=>await Resume(id);var actions=PanelUi.Menu();var archive=new MenuItem{Header=Locale.T("대화 보관")};archive.Click+=async(s,e)=>{try{int navigation=conversationRequest;bool reset=await chat.Archive(id);if(reset&&navigation==conversationRequest&&chat.ThreadId==null)NewConversation();else RenderHistory(chat.CachedHistory());}catch(Exception error){MessageBox.Show(window,error.Message,"Catheryne");}};actions.Items.Add(archive);button.ContextMenu=actions;historyList.Children.Add(button);}
 }

 void ApplyConnection(){aiPanel.UpdateAccount();aiPanel.SetBusy(busy||switchingProvider,connecting||resuming);PanelUi.InputHint(draft,chat.Connected?Locale.T("메시지를 입력하세요."):connectionHint??Locale.Format("{0}에 로그인하세요.",chat.ProviderName));draft.IsEnabled=chat.Connected&&!resuming&&!switchingProvider;send.IsEnabled=!resuming&&!connecting&&!switchingProvider;send.ToolTip=chat.Connected?Locale.T("보내기"):Locale.Format("{0} 로그인",chat.ProviderName);aiPanel.ModelButton.Visibility=chat.Connected?Visibility.Visible:Visibility.Hidden;aiPanel.Usage.Visibility=aiPanel.ModelButton.Visibility;}
 async System.Threading.Tasks.Task SelectProvider(string provider){
  if(provider==chat.Provider)return;if(busy||connecting||resuming||switchingProvider)throw new InvalidOperationException(Locale.T("진행 중인 작업이 끝난 뒤 AI를 변경해 주세요."));
  switchingProvider=true;++connectionVersion;ApplyConnection();
  try{await chat.SelectProvider(provider);CancelEdit();CancelResume();RenderConversation(new Dictionary<string,object>(),new List<AiTaskRecord>());AppPreferences.Set("aiProvider",provider);aiPanel.ResetUsage();await CheckConnection();}
  finally{switchingProvider=false;ApplyConnection();}
 }
 async System.Threading.Tasks.Task ConnectProvider(string provider){await SelectProvider(provider);await Connect();}
 public void Dispose(){typingClock.Stop();activityClock.Stop();ClearQuestions();aiPanel.Dispose();chat.Dispose();}
 async System.Threading.Tasks.Task CheckConnection(){
  int version=++connectionVersion;
  connectionHint=Locale.Format("{0} 연결 확인 중…",chat.ProviderName);ApplyConnection();
  try{await chat.Account();if(version!=connectionVersion)return;connectionHint=null;draft.ToolTip=null;ApplyConnection();if(chat.Connected){await aiPanel.LoadModels();if(version!=connectionVersion)return;await chat.EnsureProject();if(version==connectionVersion){var warm=chat.History();}}}
  catch(Exception error){if(version!=connectionVersion)return;AppDiagnostics.Record(DiagnosticEvent.AiConnectionFailure,error);connectionHint=error.Message;draft.ToolTip=error.Message;ApplyConnection();}
 }
 async void Connection(){await AccountConnections.For(window).Connect(chat.Provider);}
 async System.Threading.Tasks.Task Connect(){
  if(connecting)return;connecting=true;connectionHint=Locale.Format("{0} 연결 확인 중…",chat.ProviderName);ApplyConnection();
  try{bool connected=false;try{connected=await chat.Account();}catch(ClaudeAccountRequired){}if(connected){connectionHint=null;ApplyConnection();await aiPanel.LoadModels();return;}connectionHint=Locale.Format("{0} 로그인 대기 중",chat.ProviderName);ApplyConnection();await chat.Login();if(chat.Provider==AiProviders.Claude)await CheckConnection();}
  catch(Exception error){connectionHint=Locale.Format("{0} 연결 다시 시도",chat.ProviderName);draft.ToolTip=error.Message;ApplyConnection();MessageBox.Show(window,error.Message,"Catheryne");}
  finally{connecting=false;ApplyConnection();}
 }
 void AnimateBackground(bool active){if(shaded==active)return;shaded=active;var shade=(Border)window.FindName("ChatShade");shade.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(active?0.88:0,TimeSpan.FromMilliseconds(420)));var brush=(LinearGradientBrush)shade.Background;brush.BeginAnimation(LinearGradientBrush.StartPointProperty,new System.Windows.Media.Animation.PointAnimation(active?new Point(0,0):new Point(0,0.8),TimeSpan.FromMilliseconds(420)){EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut}});}
 static string EventLabel(string type,bool completed){
  if(type=="contextCompaction")return Locale.T(completed?"대화를 최적화했습니다":"대화 최적화 중");
  if(type=="webSearch")return Locale.T(completed?"웹을 검색했습니다":"웹 검색 중");
  if(type=="computerUse")return Locale.T(completed?"화면을 조작했습니다":"화면 조작 중");
  if(type=="imageView")return Locale.T(completed?"이미지를 확인했습니다":"이미지 확인 중");
  if(type=="commandExecution")return Locale.T(completed?"명령을 실행했습니다":"명령 실행 중");
  if(type=="mcpToolCall"||type=="dynamicToolCall")return Locale.T(completed?"도구를 사용했습니다":"도구 사용 중");
  return null;
 }
 void ToolEvent(Dictionary<string,object> item,bool completed){
  string label=EventLabel(CodexChat.S(item,"type"),completed),id=CodexChat.S(item,"id");if(label==null||string.IsNullOrEmpty(id))return;
  bool follow=FollowingEnd();PrepareMessages();TextBlock line;
  if(!toolEvents.TryGetValue(id,out line)){line=new ChatToolLine{FontSize=12,Margin=ChatLayout.EntryMargin(true),LineHeight=ChatLayout.LineHeight,TextWrapping=TextWrapping.Wrap};toolEvents[id]=line;var owner=ItemOwner(id);if(owner!=null){owner.AddEntry(line,false);RememberOwner(id,owner);}else body.Children.Add(line);}
  if(!loadingHistory)RememberOwner(id,ItemOwner(id));
  string status=CodexChat.S(item,"status");bool failed=status=="failed"||status=="declined"||(item.ContainsKey("success")&&Equals(item["success"],false));
  var failureOwner=ItemOwner(id);if(completed&&failureOwner!=null)failureOwner.SetToolFailure(id,failed);
  string toolName=Locale.T(CatheryneTools.DisplayName(CodexChat.S(item,"tool")));line.ToolTip=CodexChat.S(item,"tool");line.Text=(failed?"! ":"◦ ")+(failed?Locale.T("도구 실행 실패"):label)+(string.IsNullOrEmpty(toolName)?"":"  "+toolName);line.Tag=!completed;
  if(completed){line.Foreground=new SolidColorBrush(failed?Color.FromRgb(240,154,128):Color.FromRgb(155,162,172));}
  else{Shimmer(line);if(ItemOwner(id)!=null)SetActivity(label+(string.IsNullOrEmpty(toolName)?"":"  "+toolName));else activity.Visibility=Visibility.Collapsed;}
  if(completed&&busy&&!loadingHistory&&!System.Linq.Enumerable.Any(toolEvents.Values,x=>Equals(x.Tag,true)))SetActivity(Locale.T("생각 중…"));
  PlaceResponseCopy();ChatLayout.CompactTools(body);if(follow)conversation.ScrollToEnd();
 }
 static void Shimmer(TextBlock line){var brush=new LinearGradientBrush();brush.StartPoint=new Point(0,0);brush.EndPoint=new Point(1,0);brush.GradientStops.Add(new GradientStop(Color.FromRgb(135,143,155),0));var shine=new GradientStop(Color.FromRgb(226,231,238),0);brush.GradientStops.Add(shine);brush.GradientStops.Add(new GradientStop(Color.FromRgb(135,143,155),1));line.Foreground=brush;if(SystemParameters.ClientAreaAnimation)shine.BeginAnimation(GradientStop.OffsetProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(1200)){AutoReverse=true,RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever});}
 void SetActivity(string label){bool follow=FollowingEnd();PrepareMessages();DetachActivity();var owner=ActiveCard();activity.Margin=owner==null?ChatLayout.EntryMargin(true):new Thickness(0);activity.LineHeight=ChatLayout.LineHeight;if(owner==null)body.Children.Add(activity);else owner.SetActivity(activity);activity.Text=label;activity.Visibility=Visibility.Visible;Shimmer(activity);PlaceResponseCopy();if(follow)conversation.ScrollToEnd();}
 static string ElapsedText(double? seconds,bool completed){if(!seconds.HasValue)return Locale.T("작업 시간 미기록");int value=(int)Math.Max(0,seconds.Value);return value>=60?Locale.Format(completed?"{0}분 {1}초 동안 작업했습니다":"{0}분 {1}초 동안 작업 중입니다",value/60,value%60):Locale.Format(completed?"{0}초 동안 작업했습니다":"{0}초 동안 작업 중입니다",value);}
 static double? TurnSeconds(Dictionary<string,object> turn){double duration,start,end;object raw;if(turn.TryGetValue("durationMs",out raw)&&raw!=null&&double.TryParse(Convert.ToString(raw),out duration)&&duration>=0)return duration/1000;if(double.TryParse(CodexChat.S(turn,"startedAt"),out start)&&double.TryParse(CodexChat.S(turn,"completedAt"),out end)&&end>=start)return end-start;return null;}
 Border ElapsedLine(string text,out TextBlock label){label=PanelUi.Text(text,true);label.FontSize=12;label.Margin=new Thickness(ChatLayout.Inset,0,ChatLayout.Inset,8);return new Border{Child=label,BorderThickness=new Thickness(0,0,0,1),BorderBrush=new SolidColorBrush(Color.FromArgb(35,180,188,200)),Margin=ChatLayout.EntryMargin(false)};}
 void UpdateElapsed(bool completed){if(elapsed!=null)elapsed.Text=ElapsedText((DateTime.UtcNow-turnStarted).TotalSeconds,completed);}
 void BeginActivity(){turnStarted=DateTime.UtcNow;body.Children.Add(ElapsedLine(ElapsedText(0,false),out elapsed));activityClock.Start();SetActivity(Locale.T("생각 중…"));}
 void RestoreTurn(Dictionary<string,object> turn,List<AiTaskRecord> tasks){
  responseCopy=null;object items;if(!turn.TryGetValue("items",out items))return;restoringImages=chat.ImagesForTurn(CodexChat.S(turn,"id"));bool timingShown=false;
  foreach(var item in CodexChat.Items(items)){if(!timingShown&&CodexChat.S(item,"type")!="userMessage"){PrepareMessages();TextBlock label;body.Children.Add(ElapsedLine(ElapsedText(TurnSeconds(turn),CodexChat.S(turn,"status")!="inProgress"),out label));timingShown=true;}RestoreItem(item,tasks);}
  if(!timingShown){PrepareMessages();TextBlock label;body.Children.Add(ElapsedLine(ElapsedText(TurnSeconds(turn),CodexChat.S(turn,"status")!="inProgress"),out label));}
  foreach(var task in tasks)if(!taskCards.ContainsKey(task.Id)&&!string.IsNullOrEmpty(task.TurnId)&&task.TurnId==CodexChat.S(turn,"id"))RestoreCard(task);
  restoringItem=null;restoringTime=null;restoringImages=null;RenderTurnOutcome(turn);
 }
 void PrepareMessages(){if(!hasMessages){responseCopy=null;body.Children.Clear();replies.Clear();toolEvents.Clear();hasMessages=true;AnimateBackground(true);taskCards.Clear();taskOrder.Clear();closedTaskTurns.Clear();taskFeed=null;}body.HorizontalAlignment=HorizontalAlignment.Stretch;body.VerticalAlignment=VerticalAlignment.Top;body.MaxWidth=ChatLayout.MaxWidth;}
 bool FollowingEnd(){return conversation.ScrollableHeight-conversation.VerticalOffset<64;}
 void UserMessage(object content,DateTimeOffset? timestamp=null){
  var value=CodexChat.ReadUserContent(content,restoringImages);Message(value.Text,true,timestamp,value.Attachments.ToArray());
 }
 ChatText Message(string text,bool user,DateTimeOffset? timestamp=null,ChatAttachment[] files=null){
  bool follow=user||FollowingEnd();PrepareMessages();if(user)responseCopy=null;var message=new ChatText(user){Text=text};
  UIElement content=message;
  if(files!=null&&files.Any(x=>!x.IsImage)){
   var parts=new StackPanel();if(!string.IsNullOrEmpty(text))parts.Children.Add(message);
   var fileRow=new WrapPanel{Margin=new Thickness(0,string.IsNullOrEmpty(text)?0:8,0,0)};
   foreach(var file in files.Where(x=>!x.IsImage)){var label=PanelUi.Text(file.Name,true);label.FontSize=12;label.Margin=new Thickness(0,0,12,0);fileRow.Children.Add(label);}
   parts.Children.Add(fileRow);content=parts;
  }
  var card=new Border{Child=content,Background=user?(Brush)new SolidColorBrush(Color.FromArgb(235,46,51,58)):Brushes.Transparent,CornerRadius=PanelUi.Corners,Padding=new Thickness(ChatLayout.Inset,user?12:0,ChatLayout.Inset,user?12:0),Margin=new Thickness(user?60:0,0,user?0:40,16),HorizontalAlignment=HorizontalAlignment.Stretch};
  var messageGroup=new Grid{Margin=ChatLayout.EntryMargin(false)};card.Margin=new Thickness(user?60:0,0,user?0:40,0);
  var messageParts=new StackPanel();if(files!=null&&files.Any(x=>x.IsImage)){var strip=new WrapPanel{HorizontalAlignment=user?HorizontalAlignment.Right:HorizontalAlignment.Left,Margin=new Thickness(user?60:0,0,user?0:40,0)};foreach(var file in files.Where(x=>x.IsImage)){var preview=ChatAttachments.Thumbnail(file,window,false);preview.Margin=new Thickness(ChatAttachments.Gap,0,0,ChatAttachments.Gap);strip.Children.Add(preview);}messageParts.Children.Add(strip);}
  if(!user||!string.IsNullOrEmpty(text)||(files!=null&&files.Any(x=>!x.IsImage)))messageParts.Children.Add(card);messageGroup.Children.Add(messageParts);
  var when=timestamp??(loadingHistory?(DateTimeOffset?)null:DateTimeOffset.Now);
  if(user){ClearEditAction();var actions=MessageActions(()=>message.Text,when,true,!string.IsNullOrEmpty(text));actions.VerticalAlignment=VerticalAlignment.Bottom;messageGroup.Children.Add(actions);actions.Watch(messageGroup);lastUserActions=actions;}
  else card.ToolTip=when.HasValue?when.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):Locale.T("시간 미기록");

  if(user)body.Children.Add(messageGroup);else{AddChat(messageGroup,true);RegisterResponse(message,when);}
  if(!loadingHistory&&SystemParameters.ClientAreaAnimation){
   var move=new TranslateTransform(0,user?32:12);messageGroup.RenderTransform=move;messageGroup.Opacity=0;
   window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>{
    if(messageGroup.Parent==null)return;
    if(follow){conversation.UpdateLayout();conversation.ScrollToEnd();}
    move.BeginAnimation(TranslateTransform.YProperty,new System.Windows.Media.Animation.DoubleAnimation(user?32:12,0,TimeSpan.FromMilliseconds(user?340:260)){EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut}});
    messageGroup.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(200)));
   }));
  }
  if(follow&&!loadingHistory)conversation.ScrollToEnd();return message;
 }
 Button CopyButton(Func<string> text){
  var copy=IconButton("M9,8 H20 V21 H9 Z M5,16 H3 V3 H14 V5","복사",false);copy.Content=Icon("M9,8 H20 V21 H9 Z M5,16 H3 V3 H14 V5",15);copy.Width=24;copy.Height=24;copy.MinWidth=0;copy.Opacity=.65;copy.Padding=new Thickness(0);copy.Margin=new Thickness(0);
  copy.Click+=(s,e)=>{try{string value=text();if(!string.IsNullOrEmpty(value))System.Windows.Clipboard.SetText(value);copy.ToolTip=Locale.T("복사됨");}catch(System.Runtime.InteropServices.ExternalException){copy.ToolTip=Locale.T("다시 복사해 주세요.");}};return copy;
 }
 ChatMessageActions MessageActions(Func<string> text,DateTimeOffset? when,bool user,bool canCopy=true){return new ChatMessageActions(CopyButton(text),when,user,canCopy);}
 void RegisterResponse(ChatText message,DateTimeOffset? when){
  if(responseCopy==null){var response=new ChatResponseCopy();responseCopy=response;response.Actions=MessageActions(()=>ResponseText(response),when,false);response.Children.Add(response.Actions);}
  responseCopy.Messages.Add(message);responseCopy.Actions.SetTime(when);PlaceResponseCopy();
 }
 string ResponseText(ChatResponseCopy response){return string.Join(Environment.NewLine+Environment.NewLine,response.Messages.Select(message=>{string pending;pendingText.TryGetValue(message,out pending);return message.Text+(pending??"");}).Where(text=>!string.IsNullOrWhiteSpace(text)));}
 void PlaceResponseCopy(){if(responseCopy==null)return;body.Children.Remove(responseCopy);var last=body.Children.Count>0?body.Children[body.Children.Count-1] as FrameworkElement:null;if(last!=null)responseCopy.Actions.Watch(last);body.Children.Add(responseCopy);}
 int TranscriptEntryCount(){return body.Children.Cast<UIElement>().Count(item=>!(item is ChatResponseCopy));}
 void ClearEditAction(){if(editRequestButton!=null&&lastUserActions!=null)lastUserActions.SetEdit(null);editRequestButton=null;editableTurn=null;}
 void SetEditable(Dictionary<string,object> turn){
  ClearEditAction();if(CodexChat.S(turn,"status")!="interrupted"||lastUserActions==null)return;editableTurn=CodexChat.S(turn,"id");if(string.IsNullOrEmpty(editableTurn))return;
  editRequestButton=IconButton("M4,16 L16,4 L20,8 L8,20 L4,20 Z M14,6 L18,10","수정 후 다시 보내기",false);editRequestButton.Width=24;editRequestButton.Height=24;editRequestButton.MinWidth=0;editRequestButton.Padding=new Thickness(0);editRequestButton.Content=Icon("M4,16 L16,4 L20,8 L8,20 L4,20 Z M14,6 L18,10",15);editRequestButton.Click+=async(s,e)=>await EditInterrupted();lastUserActions.SetEdit(editRequestButton);
 }
 async System.Threading.Tasks.Task EditInterrupted(){
  if(busy||resuming||editingRequest!=null||string.IsNullOrEmpty(editableTurn))return;int navigation=conversationRequest;string thread=chat.ThreadId,turn=editableTurn;resuming=true;ApplyConnection();
  try{var request=await chat.ReadInterrupted(turn);if(navigation!=conversationRequest||chat.ThreadId!=thread||editableTurn!=turn)return;BeginEdit(request);}
  catch(Exception error){if(navigation==conversationRequest){CancelEdit();MessageBox.Show(window,error.Message,"Catheryne");}}
  finally{if(navigation==conversationRequest){resuming=false;ApplyConnection();}}
 }
 internal void BeginEdit(CodexChat.InterruptedRequest request){
  foreach(string file in request.Files)ChatAttachments.Validate(file);savedDraft=draft.Text;savedCaret=draft.CaretIndex;savedFiles=selectedFiles.ToArray();savedPreviews=attachments.Children.Cast<UIElement>().ToArray();selectedFiles.Clear();attachments.Children.Clear();editingRequest=request;draft.Text=request.Text;foreach(string file in request.Files)AttachFile(file);editMode.Visibility=Visibility.Visible;draft.CaretIndex=draft.Text.Length;draft.Focus();
 }
 internal void CancelEdit(){if(editingRequest==null)return;draft.Text=savedDraft;draft.CaretIndex=Math.Min(savedCaret,draft.Text.Length);selectedFiles.Clear();selectedFiles.AddRange(savedFiles);attachments.Children.Clear();foreach(var preview in savedPreviews)attachments.Children.Add(preview);EndEdit();}
 void EndEdit(){editingRequest=null;savedDraft=null;savedFiles=null;savedPreviews=null;editMode.Visibility=Visibility.Collapsed;}
 void RenderConversation(Dictionary<string,object> thread,List<AiTaskRecord> tasks){
  ClearEditAction();currentRequestTurnId=null;responseCopy=null;elapsed=null;hasMessages=false;aiPanel.ResetUsage();body.Children.Clear();ClearQuestions();replies.Clear();toolEvents.Clear();taskCards.Clear();taskOrder.Clear();closedTaskTurns.Clear();taskFeed=null;restoredTasks=tasks;object turns;
  if(thread.TryGetValue("turns",out turns))foreach(var turn in CodexChat.Items(turns)){RestoreTurn(turn,tasks);SetEditable(turn);}
  restoringItem=null;restoringTime=null;foreach(var task in tasks.Where(t=>!taskCards.ContainsKey(t.Id)).OrderBy(t=>t.Started))RestoreCard(task);
 }
 async System.Threading.Tasks.Task SendMessage(){
  if(resuming)return;
  if(busy){++sendingRequest;try{await chat.Interrupt();if(chat.TurnId==null)Finish();}catch(Exception error){Message(error.Message,false);}return;}
  string text=draft.Text.Trim();if(text.Length==0&&!selectedFiles.Any(ChatAttachments.IsImage))return;
  int request=++sendingRequest;busy=true;currentRequestTurnId=null;elapsed=null;send.Content=Icon("M7,7 H17 V17 H7 Z",20,true);aiPanel.SetBusy(true);
  string[] files=selectedFiles.ToArray();
  try{
   var input=CodexChat.PrepareInput(text,files);
   if(!await chat.Account())throw new InvalidOperationException(Locale.Format("먼저 {0}에 로그인해 주세요.",chat.ProviderName));
   if(editingRequest!=null){var original=editingRequest;try{var fork=await chat.ForkInterrupted(original);loadingHistory=true;RenderConversation(fork,new List<AiTaskRecord>());}catch{if(chat.ThreadId!=original.Thread)RenderConversation(new Dictionary<string,object>(),new List<AiTaskRecord>());throw;}finally{loadingHistory=false;if(chat.ThreadId!=original.Thread)EndEdit();}}
   ClearEditAction();UserMessage(input);BeginActivity();draft.Clear();await chat.Send(input);selectedFiles.Clear();attachments.Children.Clear();
  }catch(OperationCanceledException){if(request!=sendingRequest)return;draft.Text=text;SetActivity(Locale.T("중단됨"));Finish(true);}
  catch(Exception error){if(request!=sendingRequest)return;draft.Text=text;Message(error.Message,false);SetActivity(Locale.T("응답 실패"));Finish(true);}
 }
 void RenderPending(bool flush){bool follow=FollowingEnd();foreach(var reply in new List<ChatText>(pendingText.Keys)){string text=pendingText[reply];int take=flush?text.Length:Math.Min(text.Length,Math.Max(2,(text.Length+7)/8));if(take<text.Length&&take>0&&char.IsHighSurrogate(text[take-1]))take++;reply.Text+=text.Substring(0,take);if(take==text.Length)pendingText.Remove(reply);else pendingText[reply]=text.Substring(take);}if(pendingText.Count==0)typingClock.Stop();if(follow)conversation.ScrollToEnd();}
 void Finish(bool keepStatus=false){foreach(var card in taskOrder){card.Hold(false);if(!string.IsNullOrEmpty(card.Task.TurnId))closedTaskTurns.Add(card.Task.TurnId);}activityClock.Stop();UpdateElapsed(true);foreach(var line in toolEvents.Values)if(Equals(line.Tag,true)){line.Tag=false;line.Foreground=new SolidColorBrush(Color.FromRgb(155,162,172));line.Text=Locale.T("◦ 실행 종료 (결과 미확인)");}RenderPending(true);activity.Visibility=keepStatus?Visibility.Visible:Visibility.Collapsed;activity.Foreground=new SolidColorBrush(Color.FromRgb(155,162,172));busy=false;send.Content=Icon("M12,18 V6 M6,12 L12,6 L18,12",20,true);ApplyConnection();if(keepStatus)SetActivity(activity.Text);ContinueTheaterCapture(keepStatus);}
 static string TurnOutcomeLabel(string status){return status=="interrupted"?Locale.T("중단됨"):status=="failed"?Locale.T("응답 실패"):null;}
 void RenderTurnOutcome(Dictionary<string,object> turn){
  string label=TurnOutcomeLabel(CodexChat.S(turn,"status"));if(label==null)return;
  bool follow=FollowingEnd();PrepareMessages();var line=PanelUi.Text(label,true);line.FontSize=12;line.Margin=ChatLayout.EntryMargin(true);line.LineHeight=ChatLayout.LineHeight;AddChat(line,false);var owner=ActiveCard();if(owner!=null)owner.ShowIssue(label);
  object error;if(turn.TryGetValue("error",out error)&&error!=null){string message=CodexChat.S(CodexChat.Map(error),"message");if(!string.IsNullOrWhiteSpace(message))Message(message,false);}
  if(follow&&!loadingHistory)conversation.ScrollToEnd();
 }
 void OnNotification(string name,Dictionary<string,object> data){
  if(data.ContainsKey("threadId")&&CodexChat.S(data,"threadId")!=chat.ThreadId)return;
  if(name=="turn/started"){currentRequestTurnId=CodexChat.S(CodexChat.Map(data["turn"]),"id");if(!busy){ClearEditAction();PrepareMessages();responseCopy=null;busy=true;send.Content=Icon("M7,7 H17 V17 H7 Z",20,true);aiPanel.SetBusy(true);}if(!activityClock.IsEnabled)BeginActivity();return;}
  if(name=="thread/goal/updated"||name=="thread/goal/cleared"){UpdateExecution(executionTask,new Dictionary<string,object>());if(busy&&chat.TurnId==null&&CodexChat.S(chat.Goal,"status")!="active")Finish();return;}
  if(name=="catheryne/question"){var request=(AiUserInput)data["request"];if(questionCards.ContainsKey(request.Key))return;bool follow=FollowingEnd();PrepareMessages();var card=new AiQuestionCard(request,(answers,skip)=>chat.Answer(request,answers,skip),async()=>{try{await chat.Interrupt();if(chat.TurnId==null)Finish();}catch(Exception error){Message(error.Message,false);}});questionCards[request.Key]=card;body.Children.Add(card);PlaceResponseCopy();if(request.Blocking)SetActivity(Locale.T("응답 필요"));UpdateExecution(executionTask,new Dictionary<string,object>());if(follow)card.BringIntoView();if(request.Blocking&&(!window.IsActive||host.Visibility!=Visibility.Visible)&&InputNotification!=null)InputNotification(request);return;}
  if(name=="catheryne/question/resolved"){AiQuestionCard card;if(questionCards.TryGetValue(CodexChat.S(data,"requestId"),out card)){if(InputResolved!=null)InputResolved(card.Request);card.Resolve(Locale.T(data.ContainsKey("answered")&&Equals(data["answered"],true)?"답변 전송됨":"질문 종료됨"));}UpdateExecution(executionTask,new Dictionary<string,object>());if(busy&&PendingQuestion()==null)SetActivity(Locale.T("생각 중…"));return;}
  if(name=="catheryne/history"){if(historyOpen)RenderHistory(chat.CachedHistory());return;}
  if(name=="thread/tokenUsage/updated"){if(CodexChat.S(data,"threadId")==chat.ThreadId)aiPanel.UpdateUsage(CodexChat.Map(data["tokenUsage"]));return;}
  if((name=="item/started"||name=="item/completed")&&data.ContainsKey("item")){var item=CodexChat.Map(data["item"]);if(CodexChat.S(item,"type")=="agentMessage"&&CodexChat.S(item,"phase")=="final_answer"){foreach(var card in taskOrder.Where(c=>c.Task.TurnId==CodexChat.S(data,"turnId")||c.Task.TurnId==chat.TurnId)){card.Hold(false);if(!string.IsNullOrEmpty(card.Task.TurnId))closedTaskTurns.Add(card.Task.TurnId);}RememberOwner(CodexChat.S(item,"id"),ActiveCard());}if(name=="item/started"&&CodexChat.S(item,"type")=="reasoning")SetActivity(Locale.T("생각 중…"));ToolEvent(item,name=="item/completed");}

  if(name=="account/login/completed"||name=="account/updated"){var check=CheckConnection();return;}
  if(name=="catheryne/task"){bool follow=FollowingEnd();var task=CatheryneTools.Json().Deserialize<AiTaskRecord>(CatheryneTools.Json().Serialize(data));if(task.Thread!=chat.ThreadId)return;PrepareMessages();UpsertCard(task);if(activity.Visibility==Visibility.Visible){string label=activity.Text;SetActivity(label);}if(follow)conversation.ScrollToEnd();var refresh=aiPanel.Refresh();return;}
  if(name=="error"&&data.ContainsKey("willRetry")&&Equals(data["willRetry"],true)){SetActivity(Locale.T("다시 시도 중…"));return;}
  if(name=="client/disconnected"){if(!recovering){var recovery=RecoverConnection();}return;}
  if(name=="client/notice"){Message(CodexChat.S(data,"message"),false);return;}
  if(name=="item/agentMessage/delta"){
   activity.Visibility=Visibility.Collapsed;
   string id=CodexChat.S(data,"itemId");ChatText reply;if(!replies.TryGetValue(id,out reply)){restoringItem=id;try{reply=Message("",false);}finally{restoringItem=null;}replies[id]=reply;}string pending;pendingText.TryGetValue(reply,out pending);pendingText[reply]=(pending??"")+CodexChat.S(data,"delta");typingClock.Start();
  }
  if(name=="turn/completed"){FinishTurn(CodexChat.Map(data["turn"]));var refresh=chat.RefreshHistory();}
 }
 void FinishTurn(Dictionary<string,object> turn){if(CodexChat.S(turn,"status")!="completed"){pendingTheaterCapture=null;pendingTheaterThread=null;}Finish();if(CodexChat.S(turn,"status")=="completed"&&CodexChat.S(chat.Goal,"status")=="active"){busy=true;send.Content=Icon("M7,7 H17 V17 H7 Z",20,true);aiPanel.SetBusy(true);SetActivity(Locale.T("계속 진행 중…"));}var duration=TurnSeconds(turn);if(elapsed!=null&&duration.HasValue)elapsed.Text=ElapsedText(duration,true);RenderTurnOutcome(turn);SetEditable(turn);}
 async System.Threading.Tasks.Task RecoverConnection(){
  recovering=true;bool interrupted=busy;if(interrupted){SetActivity(Locale.T("연결이 끊겨 작업을 중단했습니다"));Finish(true);}
  try{for(int attempt=0;attempt<3&&window.IsLoaded;attempt++){
   await System.Threading.Tasks.Task.Delay(1000*(attempt+1));if(!window.IsLoaded)return;
   try{await chat.Account();ApplyConnection();await aiPanel.LoadModels();return;}catch{}
  }}finally{recovering=false;}
  if(window.IsLoaded){connectionHint=Locale.Format("{0} 연결 다시 시도",chat.ProviderName);ApplyConnection();}
 }
 AiTaskFeed Feed(){if(taskFeed==null||feedThread!=chat.ThreadId){feedThread=chat.ThreadId;taskFeed=new AiTaskFeed(Setup.DataFolder,feedThread);}return taskFeed;}
 AiTaskCard ActiveCard(){string turn=currentRequestTurnId??chat.TurnId;return taskOrder.LastOrDefault(c=>c.IsOpen&&c.Task.TurnId==turn);}
 AiTaskCard FindCard(string owner){AiTaskCard card;return taskCards.TryGetValue(owner??"",out card)?card:taskOrder.FirstOrDefault(c=>c.Request.Key==owner);}
 AiTaskCard ItemOwner(string id){
  string owner;AiTaskCard card;if(Feed().TryOwner(id,out owner))return FindCard(owner);
  if(loadingHistory){owner=AiTaskFeed.OwnerAt(restoredTasks,restoringTime);return owner!=null&&taskCards.TryGetValue(owner,out card)?card:null;}
  return ActiveCard();
 }
 void RememberOwner(string id,AiTaskCard owner){if(!loadingHistory)Feed().Bind(id,owner==null?null:owner.Request.Key);}
 void AddChat(UIElement entry,bool message){var owner=ItemOwner(restoringItem);if(owner==null)body.Children.Add(entry);else{var group=entry as Grid;if(message&&group!=null){var bubble=group.Children.OfType<Border>().FirstOrDefault();if(bubble!=null){bubble.Padding=new Thickness(0);bubble.Margin=new Thickness(0);}group.Margin=new Thickness(0);foreach(var row in group.Children.OfType<StackPanel>())row.Margin=new Thickness(0);}owner.AddEntry(entry,message);}RememberOwner(restoringItem,owner);PlaceResponseCopy();}
 void DetachActivity(){body.Children.Remove(activity);foreach(var card in taskOrder)card.SetActivity(null);}
 void UpsertCard(AiTaskRecord task){
  if(busy&&!loadingHistory&&currentRequestTurnId==null&&task.TurnId==chat.TurnId)currentRequestTurnId=task.TurnId;
  AiTaskCard card;if(!taskCards.TryGetValue(task.Id,out card))card=taskOrder.FirstOrDefault(c=>c.Request.Key==AiTaskRequest.KeyFor(task));
  bool nextTheaterTurn=card!=null&&task.Action=="theater"&&card.Task.TurnId!=task.TurnId;
  if(card==null){card=aiPanel.Card(task);taskOrder.Add(card);body.Children.Add(card);}else card.Update(task);
  if(nextTheaterTurn&&!loadingHistory&&busy){body.Children.Remove(card);body.Children.Add(card);}
  taskCards[task.Id]=card;if(!loadingHistory&&busy&&task.TurnId==(currentRequestTurnId??chat.TurnId)&&!closedTaskTurns.Contains(task.TurnId??""))card.Hold(true);PlaceResponseCopy();
 }
 void RestoreCard(AiTaskRecord task){PrepareMessages();UpsertCard(task);}

 void RestoreItem(Dictionary<string,object> item,List<AiTaskRecord> tasks){
     DateTimeOffset restoreStart;restoringItem=CodexChat.S(item,"id");restoringTime=chat.MessageTime(item);
     foreach(var task in tasks.Where(t=>!taskCards.ContainsKey(t.Id)&&DateTimeOffset.TryParse(t.Started,out restoreStart)&&restoringTime.HasValue&&restoreStart<=restoringTime.Value).OrderBy(t=>t.Started))RestoreCard(task);
     string savedOwner;if(Feed().TryOwner(restoringItem,out savedOwner)&&!string.IsNullOrEmpty(savedOwner))foreach(var task in tasks.Where(t=>AiTaskRequest.KeyFor(t)==savedOwner&&!taskCards.ContainsKey(t.Id)).OrderBy(t=>t.Started))RestoreCard(task);
     string type=CodexChat.S(item,"type");ToolEvent(item,true);
     if(type=="agentMessage")Message(CodexChat.S(item,"text"),false,chat.MessageTime(item));
     else if(type=="userMessage"){object content;if(item.TryGetValue("content",out content))UserMessage(content,chat.MessageTime(item));}
     foreach(var task in tasks)if(!taskCards.ContainsKey(task.Id)&&!string.IsNullOrEmpty(task.RequestId)&&(task.RequestId==CodexChat.S(item,"callId")||task.RequestId==CodexChat.S(item,"id")))RestoreCard(task);
 }
 internal event Action<AiTaskRecord> TaskUpdated;
 async System.Threading.Tasks.Task<bool> Resume(string id){
  if(busy)return false;
  Guid session;if(Guid.TryParse(id,out session)){string provider=await System.Threading.Tasks.Task.Run(()=>System.IO.File.Exists(System.IO.Path.Combine(Setup.DataFolder,"ai-workspace","claude",session.ToString()+".json"))?AiProviders.Claude:AiProviders.Codex);if(provider!=chat.Provider)await SelectProvider(provider);}
  CancelEdit();int request=++conversationRequest;resuming=true;ApplyConnection();
  try{
   currentRequestTurnId=null;loadingHistory=true;pendingText.Clear();typingClock.Stop();var thread=await chat.ReadConversation(id);if(request!=conversationRequest)return false;
   var tasks=await System.Threading.Tasks.Task.Run(()=>new AiTaskStore(Setup.DataFolder).List(id));if(request!=conversationRequest)return false;RenderConversation(thread,tasks);
   close();conversationOpen=true;RefreshVisibility();conversation.ScrollToEnd();return true;
  }
  catch(Exception error){if(request==conversationRequest)Message(error.Message,false);return false;}finally{if(request==conversationRequest){loadingHistory=false;restoringItem=null;restoringTime=null;resuming=false;ApplyConnection();}}
 }
 internal void VerifyQuestionConversation(){
  NewConversation();chat.ThreadId="synthetic-question-chat";chat.TurnId="synthetic-turn";chat.Goal=new Dictionary<string,object>{{"status","active"}};
  OnNotification("turn/started",new Dictionary<string,object>{{"turn",new Dictionary<string,object>{{"id",chat.TurnId}}}});UpdateExecution(null,new Dictionary<string,object>());
  if(!busy||execution.Visibility!=Visibility.Visible||executionTitle.Text!=Locale.T("AI 작업 진행 중"))throw new Exception("Native continuation must display an active stoppable chat");
  chat.TurnId=null;FinishTurn(new Dictionary<string,object>{{"id","synthetic-turn"},{"status","completed"}});if(!busy)throw new Exception("Ending an answer must not finish an active native goal");
  chat.Goal=new Dictionary<string,object>{{"status","paused"}};OnNotification("thread/goal/updated",new Dictionary<string,object>());if(busy)throw new Exception("Paused native goal must release chat activity");
  int notices=0;var notify=InputNotification;InputNotification=message=>notices++;
  var question=new AiUserInput("synthetic-question",new Dictionary<string,object>{{"threadId",chat.ThreadId},{"turnId","synthetic-turn"},{"isBlocking",true},{"questions",new object[]{new Dictionary<string,object>{{"id","path"},{"header","Path"},{"question","Which available path?"}}}}});
  var payload=new Dictionary<string,object>{{"request",question}};OnNotification("catheryne/question",payload);OnNotification("catheryne/question",payload);
  if(questionCards.Count!=1||executionTitle.Text!=Locale.T("응답 필요")||!HasPendingQuestion||notices>1)throw new Exception("Required input must be visible exactly once in chat and header");
  OnNotification("catheryne/question/resolved",new Dictionary<string,object>{{"requestId",question.Key}});if(HasPendingQuestion||!questionCards[question.Key].Resolved||execution.Visibility!=Visibility.Collapsed)throw new Exception("Resolved request must clear its attention marker");
  InputNotification=notify;NewConversation();
 }
 internal void VerifyTaskConversation(){
  string id=Guid.NewGuid().ToString();chat.ThreadId=id;chat.TurnId="turn-one";busy=true;PrepareMessages();Message("Change settings",true);BeginActivity();
  var first=new AiTaskRecord{Id="first-operation",Thread=id,TurnId=chat.TurnId,Title="FPS",Tool="catheryne_launcher",State="running",Started=DateTime.UtcNow.ToString("o")};UpsertCard(first);if(!(elapsed.Parent is Border)||!body.Children.Contains((Border)elapsed.Parent))throw new Exception("Task registration removed the independent response duration");SetActivity("Thinking");
  OnNotification("item/agentMessage/delta",new Dictionary<string,object>{{"itemId","update-one"},{"delta","Applying settings"}});RenderPending(true);
  ToolEvent(new Dictionary<string,object>{{"id","tool-one"},{"type","dynamicToolCall"},{"tool","catheryne_launcher"}},false);
  if(body.Children.OfType<AiTaskCard>().Count()!=1||body.Children.Contains(toolEvents["tool-one"])||activity.Parent==body)throw new Exception("Active task leaks chat or tools outside card");
  first.State="completed";first.Ended=DateTime.UtcNow.ToString("o");UpsertCard(first);
  if(ActiveCard()==null)throw new Exception("An intermediate tool result prematurely closed the user request");
  var second=new AiTaskRecord{Id="second-operation",Thread=id,TurnId=chat.TurnId,Title="Display",Tool="catheryne_launcher",State="completed",Started=first.Started,Ended=first.Ended};UpsertCard(second);
  if(taskOrder.Count!=1||taskCards[first.Id]!=taskCards[second.Id])throw new Exception("One request produced multiple boxes");
  ToolEvent(new Dictionary<string,object>{{"id","tool-one"},{"type","dynamicToolCall"},{"tool","catheryne_launcher"},{"success",true}},true);
  OnNotification("item/started",new Dictionary<string,object>{{"turnId",chat.TurnId},{"item",new Dictionary<string,object>{{"id","final-one"},{"type","agentMessage"},{"phase","final_answer"}}}});
  int before=TranscriptEntryCount();OnNotification("item/agentMessage/delta",new Dictionary<string,object>{{"itemId","final-one"},{"delta","Settings applied"}});RenderPending(true);
  if(ActiveCard()!=null||TranscriptEntryCount()!=before+1)throw new Exception("Final answer did not resume below completed request");turnStarted=DateTime.UtcNow.AddSeconds(-125.25);Finish();if(!(elapsed.Parent is Border)||!body.Children.Contains((Border)elapsed.Parent)||elapsed.Text!=ElapsedText(125,true))throw new Exception("Completed response duration must remain above its work cards");
  UpdateExecution(first,new Dictionary<string,object>());if(execution.Parent!=window.FindName("HeaderIndicators")||execution.Visibility!=Visibility.Visible||execution.ContextMenu.Items.Count!=2)throw new Exception("Global task pill must retain navigation and controls outside chat layout");UpdateExecution(null,new Dictionary<string,object>());if(execution.Visibility!=Visibility.Collapsed)throw new Exception("Inactive task indicator must be hidden");
  if(body.Children.OfType<ChatResponseCopy>().Count()!=1||responseCopy.Messages.Count!=2||ResponseText(responseCopy)!="Applying settings"+Environment.NewLine+Environment.NewLine+"Settings applied")throw new Exception("Response copy must include intermediate and final messages once");
  var userActions=body.Children.OfType<Grid>().SelectMany(group=>group.Children.OfType<ChatMessageActions>()).First();
  foreach(var actions in new[]{userActions,responseCopy.Actions}){var row=(StackPanel)actions.Children[0];if(row.Opacity!=0||row.IsHitTestVisible||row.Children.OfType<Button>().Count()!=1||row.Children.OfType<TextBlock>().Count()!=1||string.IsNullOrEmpty(row.Children.OfType<TextBlock>().Single().Text))throw new Exception("User and response actions must share hidden copy and time controls");}
  pendingText[responseCopy.Messages[1]]=" live";if(!ResponseText(responseCopy).EndsWith(" live"))throw new Exception("Copy omitted pending streamed text");pendingText.Clear();
  responseCopy=null;
  chat.TurnId="turn-two";currentRequestTurnId=chat.TurnId;busy=true;var third=new AiTaskRecord{Id="third-operation",Thread=id,TurnId=chat.TurnId,Title="Story",Action="game_control",Operation="story",State="running",Started=DateTime.UtcNow.ToString("o")};UpsertCard(third);if(taskOrder.Count!=2)throw new Exception("Next request must have a separate box");
  Finish();currentRequestTurnId="unrelated-question";int count=TranscriptEntryCount();Message("A separate answer",false);if(TranscriptEntryCount()!=count+1)throw new Exception("An earlier background task captured an unrelated request");third.State="cancelled";third.Ended=DateTime.UtcNow.ToString("o");UpsertCard(third);
  loadingHistory=true;restoredTasks=new List<AiTaskRecord>{first,second,third};hasMessages=false;body.Children.Clear();taskCards.Clear();taskOrder.Clear();taskFeed=null;
  RestoreItem(new Dictionary<string,object>{{"id","update-one"},{"type","agentMessage"},{"text","Applying settings"}},restoredTasks);
  if(taskOrder.Count!=1||TranscriptEntryCount()!=1)throw new Exception("Saved ownership failed to restore a timestamp-free update inside its box");
  RestoreItem(new Dictionary<string,object>{{"id","final-one"},{"type","agentMessage"},{"text","Settings applied"}},restoredTasks);
  if(TranscriptEntryCount()!=2)throw new Exception("Post-task answer moved inside the box after restoration");
  if(body.Children.OfType<ChatResponseCopy>().Count()!=1||responseCopy.Messages.Count!=2||body.Children[body.Children.Count-1]!=responseCopy)throw new Exception("Restored response must have one footer after all its messages");
  hasMessages=false;var historyTurn=new Dictionary<string,object>{{"id","past"},{"status","completed"},{"durationMs",125000L},{"items",new object[]{new Dictionary<string,object>{{"id","past-user"},{"type","userMessage"},{"content",new object[]{new Dictionary<string,object>{{"type","text"},{"text","Question"}}}}},new Dictionary<string,object>{{"id","past-answer"},{"type","agentMessage"},{"text","Answer"}}}}};
  RestoreTurn(historyTurn,new List<AiTaskRecord>());var timing=body.Children.OfType<Border>().Single();if(((TextBlock)timing.Child).Text!=ElapsedText(125,true)||body.Children.IndexOf(timing)!=1)throw new Exception("Restored response duration must follow the user message");
  historyTurn["id"]="older";historyTurn.Remove("durationMs");historyTurn["startedAt"]=1000L;historyTurn["completedAt"]=1061L;RestoreTurn(historyTurn,new List<AiTaskRecord>());if(body.Children.OfType<Border>().Count()!=2||TurnSeconds(historyTurn)!=61)throw new Exception("Each historical turn needs independent timing");
  historyTurn.Remove("startedAt");historyTurn.Remove("completedAt");if(TurnSeconds(historyTurn)!=null||ElapsedText(null,true)!=Locale.T("작업 시간 미기록"))throw new Exception("Missing response timing must not be invented");
  loadingHistory=false;restoringItem=null;restoringTime=null;busy=false;NewConversation();
 }

}


// One spacing rule for transcript entries; metadata and controls have their own
// internal spacing. Hover controls occupy the existing gap instead of a blank row.
internal static class ChatLayout {
 internal const double MaxWidth=880,Gap=24,ToolGap=6,SectionGap=16,Inset=18,LineHeight=23;
 internal static Thickness EntryMargin(bool inset){return new Thickness(inset?Inset:0,0,inset?Inset:0,Gap);}
 internal static void SpaceHistory(StackPanel panel){for(int i=0;i<panel.Children.Count;i++){var item=panel.Children[i] as FrameworkElement;if(item!=null)item.Margin=new Thickness(0,0,0,i==panel.Children.Count-1&&!(item is Grid)?0:Gap);}CompactTools(panel);}
 internal static void CompactTools(Panel panel){for(int i=0;i<panel.Children.Count-1;i++){var line=panel.Children[i] as ChatToolLine;if(line!=null){var margin=line.Margin;margin.Bottom=panel.Children[i+1] is ChatToolLine?ToolGap:Gap;line.Margin=margin;}}}
}

internal sealed class ChatResponseCopy : Grid {
 internal readonly List<ChatText> Messages=new List<ChatText>();
 internal ChatMessageActions Actions;
 internal ChatResponseCopy(){Height=ChatMessageActions.HoverHeight;Margin=new Thickness(0,-ChatLayout.Gap-ChatMessageActions.HoverHeight,0,ChatLayout.Gap);}
}

// User messages and whole assistant responses share one action row and hover behavior.
internal sealed class ChatMessageActions : Grid {
 internal const double RowHeight=20,HoverHeight=ChatLayout.Gap;
 readonly TextBlock time=new TextBlock{FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(135,141,150)),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,6,0)};
 readonly HashSet<FrameworkElement> targets=new HashSet<FrameworkElement>();
 readonly StackPanel row;
 internal ChatMessageActions(Button copy,DateTimeOffset? when,bool user,bool canCopy=true){
  copy.Visibility=canCopy?Visibility.Visible:Visibility.Collapsed;
  // The transparent bridge keeps hover continuous across the gap above the controls.
  Height=HoverHeight;Background=Brushes.Transparent;RenderTransform=new TranslateTransform(0,ChatLayout.Gap);row=new StackPanel{Orientation=Orientation.Horizontal,Height=RowHeight,Margin=new Thickness(user?60:ChatLayout.Inset,HoverHeight-RowHeight,0,0),HorizontalAlignment=user?HorizontalAlignment.Right:HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Opacity=0,IsHitTestVisible=false};
  if(user){row.Children.Add(time);row.Children.Add(copy);}else{row.Children.Add(copy);row.Children.Add(time);}Children.Add(row);SetTime(when);Watch(this);
 }
 internal void SetEdit(Button edit){foreach(var previous in row.Children.OfType<Button>().Skip(1).ToArray())row.Children.Remove(previous);if(edit!=null)row.Children.Add(edit);}
 internal void SetTime(DateTimeOffset? when){time.Text=when.HasValue?when.Value.ToLocalTime().ToString(Locale.IsEnglish?"h:mm tt":"tt h:mm",Locale.Culture):Locale.T("시간 미기록");time.ToolTip=when.HasValue?when.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):null;}
 internal void Watch(FrameworkElement target){if(!targets.Add(target))return;target.MouseEnter+=(s,e)=>Refresh();target.MouseLeave+=(s,e)=>Refresh();target.IsKeyboardFocusWithinChanged+=(s,e)=>Refresh();}
 void Refresh(){bool show=targets.Any(target=>target.IsMouseOver||target.IsKeyboardFocusWithin);row.IsHitTestVisible=show;row.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(show?1:0,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?120:0)));}
}

internal sealed class ChatToolLine : TextBlock {}
