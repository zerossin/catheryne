using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

// Only item ownership is stored here. Message bodies remain in the conversation.
internal sealed class AiTaskFeed {
 readonly string root,thread;readonly Dictionary<string,string> owners=new Dictionary<string,string>();
 internal AiTaskFeed(string root,string thread){this.root=root;this.thread=thread;if(string.IsNullOrEmpty(thread))return;using(var db=new LocalDataService(root))foreach(var row in db.Query("SELECT payload FROM observations WHERE profile="+LocalDataService.Sql(thread)+" AND kind='chat-task-item' ORDER BY id")){var value=CodexChat.Map(CatheryneTools.Json().DeserializeObject(row["payload"]));owners[CodexChat.S(value,"item")]=CodexChat.S(value,"task");}}
 internal bool TryOwner(string item,out string task){task=null;return !string.IsNullOrEmpty(item)&&owners.TryGetValue(item,out task);}
 internal void Bind(string item,string task){if(string.IsNullOrEmpty(item)||string.IsNullOrEmpty(thread)||owners.ContainsKey(item))return;using(var db=new LocalDataService(root))db.Observe(thread,"chat-task-item",new{item=item,task=task??""});owners[item]=task??"";}
 internal static bool Open(AiTaskRecord task){return task.State=="running"||task.State=="waiting"||task.State=="blocked";}
 internal static string OwnerAt(IEnumerable<AiTaskRecord> tasks,DateTimeOffset? at){
  if(!at.HasValue)return null;
  return tasks.Where(t=>{DateTimeOffset start,end;return DateTimeOffset.TryParse(t.Started,out start)&&start<=at.Value&&(Open(t)||DateTimeOffset.TryParse(t.Ended,out end)&&at.Value<end);}).OrderByDescending(t=>t.Started,StringComparer.Ordinal).Select(t=>t.Id).FirstOrDefault();
 }
}

// One user request can invoke several domain operations. This projection never
// changes those operations' lifecycle or treats tool success as game completion.
internal sealed class AiTaskRequest {
 readonly List<AiTaskRecord> operations=new List<AiTaskRecord>();
 internal static string KeyFor(AiTaskRecord task){return !string.IsNullOrEmpty(task.GroupId)?"group:"+task.Thread+":"+task.GroupId:string.IsNullOrEmpty(task.TurnId)?"task:"+task.Id:"turn:"+task.Thread+":"+task.TurnId;}
 internal readonly string Key;
 internal AiTaskRequest(AiTaskRecord task){Key=KeyFor(task);Update(task);}
 internal IEnumerable<AiTaskRecord> Operations {get{return operations;}}
 internal IEnumerable<AiTaskRecord> StatusOperations {get{return operations.Where(t=>t.State=="running"||string.IsNullOrEmpty(t.ParentTaskId)||!operations.Any(parent=>parent.Id==t.ParentTaskId));}}
 internal void Update(AiTaskRecord task){int i=operations.FindIndex(t=>t.Id==task.Id);if(i<0)operations.Add(task);else operations[i]=task;}
 internal AiTaskRecord Primary {get{return operations.FirstOrDefault(t=>t.Action=="game_control")??operations.FirstOrDefault(t=>AiTaskFeed.Open(t))??operations[0];}}
 internal AiTaskRecord Summary(){
  var main=Primary;var summary=StatusOperations.ToList();var last=summary.Last();string state=new[]{"running","blocked","waiting","failed","interrupted","cancelled","completed"}.FirstOrDefault(value=>summary.Any(t=>t.State==value))??last.State;
  return new AiTaskRecord{Id=operations[0].Id,GroupId=main.GroupId,TurnId=main.TurnId,Thread=main.Thread,Tool=main.Tool,Action=main.Action,Operation=main.Operation,Title=operations.Count==1||main.Action=="game_control"?main.Title:Locale.T(Theme()),State=state,Reason=(summary.FirstOrDefault(t=>t.State==state)??last).Reason,Started=operations.Select(t=>t.Started).Where(t=>!string.IsNullOrEmpty(t)).OrderBy(t=>t,StringComparer.Ordinal).FirstOrDefault(),Ended=state=="running"||state=="waiting"?null:operations.Select(t=>t.Ended).OrderByDescending(t=>t,StringComparer.Ordinal).FirstOrDefault(),Result=main.Result,Plan=main.Plan,ExpectedSeconds=main.ExpectedSeconds,EstimatedProgress=main.EstimatedProgress,ProgressEvidence=main.ProgressEvidence,ProgressUpdated=main.ProgressUpdated,ProgressSamples=main.ProgressSamples};
 }
 internal string Theme(){if(operations.Any(t=>t.Action=="game_control"))return AiTaskCard.Kind(Primary);var kinds=operations.Select(AiTaskCard.Kind).Distinct().ToArray();return kinds.Length==1?kinds[0]:"작업";}
}

// The same retained surface is used by chat and work history for every task type.
internal sealed class AiTaskCard : Border {
 internal AiTaskRecord Task {get;private set;}
 internal readonly AiTaskRequest Request;bool holding;
 internal bool IsOpen {get{return holding||Request.StatusOperations.Any(AiTaskFeed.Open);}}
 internal void Hold(bool value){if(holding==value)return;holding=value;Refresh();}
 readonly TextBlock heading=PanelUi.Text("",false),state=PanelUi.Text("",true),title=PanelUi.Text("",false),time=PanelUi.Text("",true),reason=PanelUi.Text("",true),mode=PanelUi.Text("",true),issue=PanelUi.Text("",true);
 readonly TheaterTaskView theaterView=new TheaterTaskView();
 Action<AiTaskRecord> judge;Func<bool> canJudge;Button judgeButton;
 internal void SetJudge(Action<AiTaskRecord> action,Func<bool> available,Action<AiTaskRecord,string,string> confirm=null){judge=action;canJudge=available;theaterView.Confirm=confirm==null?null:(Action<string,string>)((choice,identity)=>confirm(Task,choice,identity));theaterView.Invalidate();theaterView.Update(Task);if(judgeButton==null){judgeButton=ActionButton("판단",()=>judge(Task));PanelUi.PrimaryAction(judgeButton,true);judgeButton.ToolTip=Locale.T("현재 게임 화면을 캡처하고 다음 판단을 요청합니다.");}Tick();}
 readonly StackPanel history=new StackPanel(),current=new StackPanel(),plan=new StackPanel();
 readonly Expander previous, executionDetails;
 readonly StackPanel executionRows=new StackPanel();
 string executionDetailsKey;
 readonly HashSet<string> failedTools=new HashSet<string>();
 readonly Border modeBadge=new Border{CornerRadius=new CornerRadius(12),Padding=new Thickness(12,2,12,2),BorderThickness=new Thickness(1),VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(12,0,0,0)};
 readonly ContentControl activityHost=new ContentControl{VerticalAlignment=VerticalAlignment.Center};
 readonly WrapPanel actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
 readonly Button stopButton,loginButton,handoffButton,dismissButton;
 readonly DispatcherTimer clock=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
 Grid progressTrack;ScaleTransform progressFill;TaskProgressCat progressCat;double displayedProgress;
 readonly TextBlock movementProgress=PanelUi.Text("",true);
 int currentIndex;string planKey;Dictionary<string,object> execution;
 internal AiTaskCard(AiTaskRecord task,Action open,Action login=null,Action stop=null,string openLabel="작업 기록",Action dismiss=null,Action handoff=null){
  Request=new AiTaskRequest(task);var body=new StackPanel();var header=new DockPanel{LastChildFill=true,Margin=new Thickness(0,0,0,16)};state.Margin=new Thickness(16,0,0,0);state.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(state,Dock.Right);header.Children.Add(state);heading.Margin=new Thickness(0);heading.FontSize=22;heading.LineHeight=30;heading.FontWeight=FontWeights.SemiBold;header.Children.Add(heading);body.Children.Add(header);
  body.Children.Add(new Border{Height=1,Background=new SolidColorBrush(Color.FromRgb(61,68,77)),Margin=new Thickness(0,0,0,16)});
  var subject=new DockPanel();mode.Margin=new Thickness(0);mode.VerticalAlignment=VerticalAlignment.Center;modeBadge.Child=mode;DockPanel.SetDock(modeBadge,Dock.Right);subject.Children.Add(modeBadge);title.Margin=new Thickness(0);title.FontSize=16;title.LineHeight=24;title.FontWeight=FontWeights.SemiBold;subject.Children.Add(title);body.Children.Add(subject);time.Margin=new Thickness(0,8,0,0);body.Children.Add(time);body.Children.Add(plan);movementProgress.Margin=new Thickness(0,8,0,0);movementProgress.Visibility=Visibility.Collapsed;body.Children.Add(movementProgress);
  body.Children.Add(theaterView);
  previous=PanelUi.InlineDetails(Locale.T("이전 현황"),history);previous.Margin=new Thickness(0,ChatLayout.SectionGap,0,ChatLayout.SectionGap);previous.Visibility=Visibility.Collapsed;previous.Expanded+=(sender,args)=>LayoutHistory();previous.Collapsed+=(sender,args)=>LayoutHistory();body.Children.Add(previous);body.Children.Add(current);reason.Margin=new Thickness(0,8,0,0);body.Children.Add(reason);issue.Margin=new Thickness(0,ChatLayout.Gap,0,0);issue.Foreground=new SolidColorBrush(Color.FromRgb(240,154,128));issue.Visibility=Visibility.Collapsed;body.Children.Add(issue);
  executionDetails=PanelUi.InlineDetails(Locale.T("실행 계획"),executionRows);executionDetails.Margin=new Thickness(0,ChatLayout.Gap,0,0);executionDetails.Visibility=Visibility.Collapsed;body.Children.Add(executionDetails);
  var footer=new Grid{Margin=new Thickness(0,ChatLayout.Gap,0,0)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});footer.Children.Add(activityHost);Grid.SetColumn(actions,1);footer.Children.Add(actions);footer.SizeChanged+=(sender,args)=>actions.MaxWidth=Math.Max(100,args.NewSize.Width*(Task!=null&&Task.Action=="theater"?1:.65));body.Children.Add(footer);
  loginButton=ActionButton("HoYoLAB 로그인",login);handoffButton=ActionButton("직접 조작",handoff);stopButton=ActionButton("중단",stop);if(stopButton!=null)stopButton.Foreground=new SolidColorBrush(Color.FromRgb(240,120,113));
  if(open!=null)ActionButton(openLabel,open);dismissButton=ActionButton("알림 해제",dismiss);if(dismissButton!=null)dismissButton.ToolTip=Locale.T("작업과 기록은 유지하고 이 알림만 해제합니다.");
  Child=body;Padding=new Thickness(20);Margin=ChatLayout.EntryMargin(false);CornerRadius=PanelUi.Corners;Background=new SolidColorBrush(Color.FromRgb(31,37,44));BorderBrush=new SolidColorBrush(Color.FromRgb(53,63,75));BorderThickness=new Thickness(1);
  clock.Tick+=(s,e)=>Tick();IsVisibleChanged+=(s,e)=>UpdateClock();Unloaded+=(s,e)=>clock.Stop();Update(task);
 }
 Button ActionButton(string label,Action action){if(action==null)return null;var button=PanelUi.Button(Locale.T(label));button.MinWidth=80;button.Margin=new Thickness(8,0,0,8);button.Click+=(s,e)=>action();actions.Children.Add(button);return button;}
 static void Visible(UIElement element,bool show){if(element!=null)element.Visibility=show?Visibility.Visible:Visibility.Collapsed;}
 internal static string Kind(AiTaskRecord task){if(task.Action=="theater")return "환상극";if(task.Action=="game_control")return task.Operation=="story"?"스토리 진행":"게임 작업";if(task.Action=="scanner")return "최신화";if(task.Action=="bettergi")return "반복 작업";if((task.Action??"").StartsWith("display."))return "설정 변경";if((task.Action??"").StartsWith("hutao"))return "육성";if(task.Action=="launcher.launch")return "게임 실행";if(task.Action=="launcher.close")return "게임 종료";if(task.Tool=="catheryne_launcher"||(task.Action??"").StartsWith("launcher")||(task.Action??"").EndsWith(".settings"))return "설정 변경";return "작업";}
 internal void Update(AiTaskRecord task){Request.Update(task);Refresh();}
 void Refresh(){Task=Request.Summary();theaterView.Update(Task);DrawExecutionDetails();heading.Text=Locale.T(Request.Theme());title.Text=Task.Title;Visible(title,Task.Title!=heading.Text);reason.Text=Task.Reason;reason.Margin=new Thickness(0,current.Children.Count>0?ChatLayout.Gap:8,0,0);Visible(reason,!string.IsNullOrEmpty(Task.Reason)&&(current.Children.Count==0||Task.State=="blocked"||Task.State=="failed"||Task.State=="interrupted"));Visible(loginButton,Request.Operations.Any(t=>t.Action=="hoyolab_login"&&AiTaskFeed.Open(t)));Visible(stopButton,Request.Operations.Any(CanStop));Visible(handoffButton,Request.Operations.Any(t=>t.Action=="game_control"&&t.State=="running"));Visible(dismissButton,true);if(!IsOpen){execution=null;SetActivity(null);}DrawPlan();Tick();UpdateClock();}
 internal static bool CanStop(AiTaskRecord task){return task.State=="blocked"||task.State=="waiting"||task.State=="running";}
 void UpdateClock(){if(IsVisible&&IsOpen)clock.Start();else clock.Stop();}
 internal void SetExecution(Dictionary<string,object> value){execution=value;DrawPlan();}
 internal void SetActivity(UIElement value){var element=value as FrameworkElement;if(element!=null)element.Margin=new Thickness(0);activityHost.Content=value;}
 internal void SetToolFailure(string id,bool failed){if(failed)failedTools.Add(id);else failedTools.Remove(id);previous.Header=failedTools.Count==0?Locale.T("이전 현황"):Locale.Format("이전 현황 · 도구 실패 {0}건",failedTools.Count);}
 internal void ShowIssue(string text){issue.Text=text;Visible(issue,!string.IsNullOrEmpty(text));}
 internal void AddEntry(UIElement entry,bool message){
  if(message){if(current.Children.Count>0){var old=current.Children[0];current.Children.Clear();history.Children.Insert(Math.Min(currentIndex,history.Children.Count),old);}currentIndex=history.Children.Count;current.Children.Add(entry);reason.Margin=new Thickness(0,ChatLayout.Gap,0,0);Visible(reason,!string.IsNullOrEmpty(Task.Reason)&&(Task.State=="blocked"||Task.State=="failed"||Task.State=="interrupted"));}
  else history.Children.Add(entry);
  LayoutHistory();var visible=entry as FrameworkElement;if(message&&visible!=null)visible.Margin=new Thickness(0);
  previous.Visibility=history.Children.Count>0?Visibility.Visible:Visibility.Collapsed;
 }
 void DrawExecutionDetails(){
  var saved=Request.Operations.Where(t=>t.ExecutionPlan!=null).ToArray();
  string key=CatheryneTools.Json().Serialize(saved.Select(t=>new{t.Id,t.Title,t.ExecutionPlan}).ToArray());
  if(key==executionDetailsKey)return;executionDetailsKey=key;executionRows.Children.Clear();
  foreach(var task in saved){
   var definition=CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(task.ExecutionPlan)));
   var rows=CodexChat.Items(ExternalTools.Value(definition,"projects")).ToArray();
   if(rows.Length==0)continue;
   var heading=PanelUi.Text(task.Title,false);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,executionRows.Children.Count==0?0:12,0,8);executionRows.Children.Add(heading);
   for(int i=0;i<rows.Length;i++){
    string name=CodexChat.S(rows[i],"name");
    var row=PanelUi.Text((i+1)+". "+Locale.T(name),true);row.Margin=new Thickness(0,0,0,4);executionRows.Children.Add(row);
   }
  }
  Visible(executionDetails,executionRows.Children.Count>0);
 }
 void LayoutHistory(){ChatLayout.SpaceHistory(history);bool toolbarAtEnd=history.Children.Count>0&&history.Children[history.Children.Count-1] is Grid;previous.Margin=new Thickness(0,ChatLayout.SectionGap,0,previous.IsExpanded&&toolbarAtEnd?0:ChatLayout.Gap);}
 void DrawPlan(){
  var data=execution??Task.ResultData;object raw;
  var steps=data.TryGetValue("plan_steps",out raw)?CodexChat.Items(raw).ToList():new List<Dictionary<string,object>>();
  if(steps.Count==0&&Task.Plan!=null)steps=Task.Plan.Select((name,index)=>new Dictionary<string,object>{{"id",GameTools.StageId(Task,index)},{"title",name}}).ToList();
  string stage=CodexChat.S(data,"stage"),modeName=CodexChat.S(data,"mode");ApplyMode(modeName,data.ContainsKey("urgent")&&Equals(data["urgent"],true));
  var input=data.ContainsKey("input_progress")?CodexChat.Map(data["input_progress"]):new Dictionary<string,object>();
  bool moving=modeName=="navigation"&&CodexChat.S(data,"owner")=="sequence"&&CodexChat.S(input,"state")=="running";
  Visible(movementProgress,moving);
  if(moving)movementProgress.Text=Locale.Format("이동 구간 {0} / {1}",Math.Max(1,Convert.ToInt32(ExternalTools.Value(input,"current_step"))),ExternalTools.Value(input,"total_steps"));
  var states=data.ContainsKey("steps")?CodexChat.Map(data["steps"]):new Dictionary<string,object>();
  string key=CatheryneTools.Json().Serialize(new{steps,stage,statuses=steps.Select(step=>{string id=CodexChat.S(step,"id");return states.ContainsKey(id)?CodexChat.S(CodexChat.Map(states[id]),"status"):"";}).ToArray(),Task.State});if(key==planKey)return;planKey=key;plan.Children.Clear();
  // Lifecycle endpoints are a visual fallback, never fabricated execution milestones.
  if(steps.Count==0||steps.Count<2&&Task.Action!="theater"){if(Task.Action!="theater")DrawLifecycleTrack();return;}
  var track=new Grid{Height=38,Margin=new Thickness(8,16,8,4)};
  for(int i=0;i<Math.Max(1,steps.Count*2-1);i++)track.ColumnDefinitions.Add(new ColumnDefinition{Width=i%2==0?GridLength.Auto:new GridLength(1,GridUnitType.Star)});
  var blue=new SolidColorBrush(Color.FromRgb(81,113,255));var grey=new SolidColorBrush(Color.FromRgb(107,116,128));
  for(int i=0;i<steps.Count;i++){string id=CodexChat.S(steps[i],"id");var info=states.ContainsKey(id)?CodexChat.Map(states[id]):new Dictionary<string,object>();bool done=CodexChat.S(info,"status")=="completed",active=id==stage&&!done;

   var dot=PlanDot(done,active,(i+1)+". "+CodexChat.S(steps[i],"title")+"  "+Locale.T(done?"완료":active?"진행 중":"대기"),blue,grey);if(Task.Action=="theater"){
    dot.Fill=done?new SolidColorBrush(Color.FromRgb(42,54,82)):Background;dot.Stroke=done?blue:active?new SolidColorBrush(Color.FromRgb(213,218,224)):grey;
    if(i>0){var link=new Border{Height=3,VerticalAlignment=VerticalAlignment.Center,Background=done&&CodexChat.S(states.ContainsKey(CodexChat.S(steps[i-1],"id"))?CodexChat.Map(states[CodexChat.S(steps[i-1],"id")]):new Dictionary<string,object>(),"status")=="completed"?blue:grey};Grid.SetColumn(link,2*i-1);track.Children.Add(link);}
   }
   Grid.SetColumn(dot,2*i);track.Children.Add(dot);
  }
  if(Task.Action!="theater")AddProgress(track);else{progressTrack=null;progressFill=null;}plan.Children.Add(track);string currentTitle=CodexChat.S(data,"stage_title");if(!string.IsNullOrEmpty(currentTitle)&&currentTitle!=Task.Title){var caption=PanelUi.Text(currentTitle,true);caption.Margin=new Thickness(0,4,0,4);plan.Children.Add(caption);}
 }
 System.Windows.Shapes.Ellipse PlanDot(bool done,bool active,string label,Brush blue,Brush grey){
  var dot=new System.Windows.Shapes.Ellipse{Width=active?20:16,Height=active?20:16,Stroke=done||active?blue:grey,StrokeThickness=3,Fill=done||active?new SolidColorBrush(Color.FromRgb(42,54,82)):Background,ToolTip=label};System.Windows.Automation.AutomationProperties.SetName(dot,label);return dot;
 }
 void DrawLifecycleTrack(){
  bool completed=Task.State=="completed";
  var track=new Grid{Height=38,Margin=new Thickness(8,16,8,4)};
  track.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});track.ColumnDefinitions.Add(new ColumnDefinition());track.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  var blue=new SolidColorBrush(Color.FromRgb(81,113,255));var grey=new SolidColorBrush(Color.FromRgb(107,116,128));

  for(int i=0;i<2;i++){var dot=PlanDot(i==0||completed,i==0&&!completed,Locale.T(i==0?"시작":"종료"),blue,grey);Grid.SetColumn(dot,2*i);track.Children.Add(dot);}
  AddProgress(track);plan.Children.Add(track);
 }
 void AddProgress(Grid track){
  progressTrack=track;progressFill=new ScaleTransform(displayedProgress,1);
  var rail=new Border{Height=3,Margin=new Thickness(8,0,8,0),VerticalAlignment=VerticalAlignment.Center,Background=new SolidColorBrush(Color.FromRgb(107,116,128)),Child=new Border{Background=new SolidColorBrush(Color.FromRgb(81,113,255)),RenderTransform=progressFill}};Grid.SetColumnSpan(rail,track.ColumnDefinitions.Count);track.Children.Insert(0,rail);
  progressCat=new TaskProgressCat(Task.State=="running"){HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(-8,-23,0,0),IsHitTestVisible=false};Grid.SetColumnSpan(progressCat,track.ColumnDefinitions.Count);track.Children.Add(progressCat);track.SizeChanged+=(s,e)=>UpdateProgress(false);UpdateProgress(false);
 }
 void UpdateProgress(bool animate){
  if(progressTrack==null)return;double value=TaskProgress.Fraction(Task,DateTime.UtcNow);displayedProgress=value;
  var duration=TimeSpan.FromMilliseconds(animate&&SystemParameters.ClientAreaAnimation?650:0);
  progressFill.BeginAnimation(ScaleTransform.ScaleXProperty,new System.Windows.Media.Animation.DoubleAnimation(value,duration){EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut}});
  progressCat.Offset.BeginAnimation(TranslateTransform.XProperty,new System.Windows.Media.Animation.DoubleAnimation(Math.Max(0,progressTrack.ActualWidth-16)*value,duration){EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut}});
  progressTrack.ToolTip=TaskProgress.Measured(Task)?CodexChat.S(Task.ResultData,"stage_title"):Task.State=="completed"?Locale.T("완료"):Locale.Format("진행 추정 {0}%",(int)Math.Round(value*100))+(string.IsNullOrEmpty(Task.ProgressEvidence)?"":"\n"+Task.ProgressEvidence);
 }
 void ApplyMode(string name,bool urgent){
  string label=Locale.T(ModeLabel(name));mode.Text=urgent?(string.IsNullOrEmpty(label)?Locale.T("긴급"):Locale.Format("긴급: {0}",label)):label;
  Color color=ModeColor(name,urgent);mode.Foreground=new SolidColorBrush(color);modeBadge.BorderBrush=new SolidColorBrush(Color.FromArgb(125,color.R,color.G,color.B));modeBadge.Background=new SolidColorBrush(Color.FromArgb(30,color.R,color.G,color.B));Visible(modeBadge,IsOpen&&(urgent||!string.IsNullOrEmpty(name)));
 }
 static Color ModeColor(string name,bool urgent){
  if(urgent)return Color.FromRgb(255,112,128);
  switch(name){
   case "dialogue":return Color.FromRgb(235,188,105);
   case "navigation":return Color.FromRgb(115,184,255);
   case "combat":return Color.FromRgb(245,145,113);
   case "recovery":return Color.FromRgb(128,207,147);
   case "interaction":return Color.FromRgb(93,207,194);
   case "puzzle":return Color.FromRgb(187,154,243);
   case "cutscene":return Color.FromRgb(228,155,204);
   case "escape":return Color.FromRgb(240,168,79);
   case "stealth":return Color.FromRgb(153,164,230);
   default:return Color.FromRgb(173,182,196);
  }
 }
 static string ModeLabel(string value){switch(value){case "dialogue":return "대화";case "navigation":return "이동";case "interaction":return "상호작용";case "combat":return "전투";case "puzzle":return "기믹";case "cutscene":return "연출";case "paused":return "일시 정지";case "escape":return "탈출";case "stealth":return "잠입";case "recovery":return "복구";default:return value;}}
 internal void Tick(){UpdateProgress(true);Visible(judgeButton,Task.Action=="theater"&&AiTaskFeed.Open(Task));if(judgeButton!=null)judgeButton.IsEnabled=!holding&&judge!=null&&(canJudge==null||canJudge());theaterView.EnableConfirmation(!holding&&(canJudge==null||canJudge()));var names=new Dictionary<string,string>{{"running","진행 중"},{"waiting","다음 판단 대기"},{"completed","완료"},{"blocked","확인 필요"},{"failed","실패"},{"cancelled","중단"},{"interrupted","연결 끊김"}};if(stopButton!=null&&Task.Action=="theater")stopButton.Content=Locale.T("관제 종료");state.Foreground=new SolidColorBrush(Task.State=="failed"?Color.FromRgb(242,126,111):Task.State=="blocked"||Task.State=="interrupted"?Color.FromRgb(245,174,94):Task.State=="completed"?Color.FromRgb(111,201,171):Color.FromRgb(174,181,190));state.Text=Locale.T(holding&&Task.Action=="theater"?"판단 중":holding&&!Request.Operations.Any(AiTaskFeed.Open)?"진행 중":names.ContainsKey(Task.State??"")?names[Task.State]:Task.State??"");DateTime start,end;if(!DateTime.TryParse(Task.Started,null,System.Globalization.DateTimeStyles.RoundtripKind,out start)){time.Visibility=Visibility.Collapsed;return;}if(holding||!DateTime.TryParse(Task.Ended,null,System.Globalization.DateTimeStyles.RoundtripKind,out end))end=DateTime.UtcNow;int seconds=(int)Math.Max(0,(end.ToUniversalTime()-start.ToUniversalTime()).TotalSeconds);time.Text=Locale.T("경과")+" "+TimeSpan.FromSeconds(seconds).ToString(seconds>=3600?@"hh\:mm\:ss":@"mm\:ss");time.Visibility=Visibility.Visible;ToolTip=start.ToLocalTime().ToString("yyyy.MM.dd HH:mm");}
 internal static void Test(){
  var task=new AiTaskRecord{Id="test",Title="Task",State="running",Started=DateTime.UtcNow.ToString("o")};var card=new AiTaskCard(task,null);var first=new TextBlock{Text="first"};var tool=new TextBlock{Text="tool"};var latest=new TextBlock{Text="latest"};card.AddEntry(first,true);card.AddEntry(tool,false);card.AddEntry(latest,true);if(card.current.Children.Count!=1||card.current.Children[0]!=latest||card.history.Children[0]!=first||card.history.Children[1]!=tool||card.previous.IsExpanded)throw new Exception("Task feed order or collapsed history");card.Hold(true);card.previous.IsExpanded=true;card.Hold(false);card.Update(new AiTaskRecord{Id="test",Title="Task",State="completed",Started=task.Started,Ended=DateTime.UtcNow.ToString("o")});if(!card.previous.IsExpanded||card.current.Children[0]!=latest||card.IsOpen)throw new Exception("Task update must retain content and expansion");
  card.SetToolFailure("failed-tool",true);card.SetToolFailure("failed-tool",true);if(card.failedTools.Count!=1||card.issue.Visibility!=Visibility.Collapsed||!Convert.ToString(card.previous.Header).Contains("1"))throw new Exception("Past tool failure must be attached to history without a stale current error");card.SetToolFailure("failed-tool",false);if(card.failedTools.Count!=0)throw new Exception("Corrected tool result must clear its failure marker");
  var planned=new AiTaskRecord{Id="planned",Title="Route",State="completed",Started=task.Started,Ended=DateTime.UtcNow.ToString("o"),ExecutionPlan=new{projects=new[]{new{name="First"},new{name="Second"}}}};var plannedCard=new AiTaskCard(planned,null);if(plannedCard.executionDetails.Visibility!=Visibility.Visible||plannedCard.executionRows.Children.Count!=3||plannedCard.executionDetails.IsExpanded)throw new Exception("Saved execution plan must be available collapsed after completion");plannedCard.executionDetails.IsExpanded=true;plannedCard.Update(planned);if(!plannedCard.executionDetails.IsExpanded||plannedCard.executionRows.Children.Count!=3)throw new Exception("Plan refresh must retain expansion without duplicate rows");
  TaskProgress.Test();var running=new AiTaskCard(new AiTaskRecord{Id="track",Title="Work",State="running",Started=DateTime.UtcNow.ToString("o")},null);if(running.displayedProgress!=0||((Grid)running.plan.Children[0]).Children.OfType<System.Windows.Shapes.Ellipse>().Count()!=2)throw new Exception("Unplanned work starts empty with two endpoints");running.Update(new AiTaskRecord{Id="track",Title="Work",State="completed"});if(running.displayedProgress!=1)throw new Exception("Verified completion fills track");
  var compact=new StackPanel();var one=new ChatToolLine();var two=new ChatToolLine();compact.Children.Add(one);compact.Children.Add(two);compact.Children.Add(new TextBlock());ChatLayout.SpaceHistory(compact);if(one.Margin.Bottom!=ChatLayout.ToolGap||two.Margin.Bottom!=ChatLayout.Gap)throw new Exception("Only consecutive tool records should have compact spacing");

 }
}

// A small vector sprite keeps the progress marker crisp at every display scale.
internal sealed class TaskProgressCat : Canvas {
 internal readonly TranslateTransform Offset=new TranslateTransform();
 readonly TranslateTransform bounce=new TranslateTransform();
 readonly List<RotateTransform> legs=new List<RotateTransform>();
 readonly bool running;
 internal TaskProgressCat(bool running){
  this.running=running;Width=32;Height=32;RenderTransform=Offset;
  var body=new Canvas{Width=32,Height=32,RenderTransform=bounce};Children.Add(body);
  Shape(body,"M 9,20 C 1,19 1,10 6,10 C 2,7 0,12 2,17 C 3,21 6,23 10,23 Z","#9CA3AF");
  for(int i=0;i<4;i++){var leg=Shape(body,"M 0,0 L 3,0 L 4,8 Q 2,10 0,8 Z",i%2==0?"#F7F4F1":"#747681");SetLeft(leg,10+i*4);SetTop(leg,21);var rotate=new RotateTransform(0,2,0);leg.RenderTransform=rotate;legs.Add(rotate);}
  Shape(body,"M 8,17 C 10,13 21,14 25,18 L 25,23 C 21,26 10,25 8,22 Z","#747681");
  Shape(body,"M 12,21 C 17,22 21,20 24,19 L 24,24 C 18,26 13,24 12,21 Z","#F7F4F1");
  Shape(body,"M 17,16 L 17,6 Q 18,5 22,10 Q 26,8 28,10 L 31,6 L 32,17 C 34,23 19,25 17,19 Z","#747681");
  Shape(body,"M 18,8 L 19,13 L 22,11 Z M 29,12 L 31,8 L 31,15 Z","#D9A3AA");
  Shape(body,"M 19,17 Q 21,18 24,14 L 27,17 Q 30,16 32,18 C 34,23 20,25 19,19 Z","#F7F4F1");
  Shape(body,"M 21,15 Q 23,14 23,17 Q 22,19 21,17 Z M 28,14 Q 30,13 30,16 Q 29,18 28,16 Z M 26,19 L 28,19 L 27,20 Z","#343740");
  IsVisibleChanged+=(s,e)=>Animate();Loaded+=(s,e)=>Animate();Unloaded+=(s,e)=>Stop();
 }
 static System.Windows.Shapes.Path Shape(Canvas parent,string data,string color){var shape=new System.Windows.Shapes.Path{Data=Geometry.Parse(data),Fill=(Brush)new BrushConverter().ConvertFromString(color)};parent.Children.Add(shape);return shape;}
 void Animate(){Stop();if(!running||!IsVisible||!SystemParameters.ClientAreaAnimation)return;bounce.BeginAnimation(TranslateTransform.YProperty,new System.Windows.Media.Animation.DoubleAnimation(0,-2,TimeSpan.FromMilliseconds(220)){EasingFunction=new System.Windows.Media.Animation.SineEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut},AutoReverse=true,RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever});for(int i=0;i<legs.Count;i++)legs[i].BeginAnimation(RotateTransform.AngleProperty,new System.Windows.Media.Animation.DoubleAnimation(i%2==0?-28:28,i%2==0?28:-28,TimeSpan.FromMilliseconds(220)){EasingFunction=new System.Windows.Media.Animation.SineEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut},AutoReverse=true,RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever});}
 void Stop(){bounce.BeginAnimation(TranslateTransform.YProperty,null);foreach(var leg in legs)leg.BeginAnimation(RotateTransform.AngleProperty,null);}
}
