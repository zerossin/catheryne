using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Threading.Tasks;

// A declaration is not an execution. TaskPlan steps reference these stable IDs.
internal sealed class GoalRequest {
 public string Id {get;set;}
 public string Title {get;set;}
 public string Category {get;set;}
 public bool Paused {get;set;}
 public bool Completed {get;set;}
 public string CharacterKey {get;set;}
 public int? TargetLevel {get;set;}
 public int? TargetTalent {get;set;}
 public string CreatedUtc {get;set;}
}
internal sealed class GoalState {
 public int Schema {get;set;}
 public int Revision {get;set;}
 public int? ResinLimit {get;set;}
 public List<GoalRequest> Goals {get;set;}
 public GoalState(){Schema=1;Goals=new List<GoalRequest>();}
}
internal sealed class GoalStore {
 readonly string root;
 internal GoalStore(string root){this.root=root;}
 internal static GoalState Read(LocalDataService db){
  var rows=db.Recent("default","goal-state");
  var value=rows.Count==0?new GoalState():new JavaScriptSerializer().Deserialize<GoalState>(rows[0]["payload"]);
  if(value==null||value.Schema!=1||value.Goals==null)throw new InvalidDataException("Unsupported goal data");
  return value;
 }
 internal GoalState Read(){using(var db=new LocalDataService(root))return Read(db);}
 internal GoalState Change(Action<GoalState> change){
  using(var db=new LocalDataService(root)){
   db.Execute("BEGIN IMMEDIATE");
   try{var state=Read(db);change(state);state.Revision++;db.Observe("default","goal-state",state);db.Execute("COMMIT");return state;}
   catch{db.Execute("ROLLBACK");throw;}
  }
 }
 internal void Add(string title,string category){
  title=(title??"").Trim();if(title.Length==0||title.Length>160)throw new ArgumentException(Locale.T("목표를 1~160자로 입력해 주세요."));
  if(!new[]{"character","story","achievement","daily"}.Contains(category))throw new ArgumentException("Invalid category");
  Save(new GoalRequest{Title=title,Category=category});
 }
 internal void Save(GoalRequest value){
  value.Title=(value.Title??"").Trim();if(value.Title.Length==0||value.Title.Length>160)throw new ArgumentException("목표를 1~160자로 입력해 주세요.");
  if(!new[]{"character","story","achievement","daily"}.Contains(value.Category))throw new ArgumentException("분류를 확인해 주세요.");
  if(value.TargetLevel.HasValue&&(value.TargetLevel<1||value.TargetLevel>100)||value.TargetTalent.HasValue&&(value.TargetTalent<1||value.TargetTalent>10))throw new ArgumentException("목표 레벨은 1~100, 특성은 1~10으로 입력해 주세요.");
  Change(s=>{var old=s.Goals.SingleOrDefault(x=>x.Id==value.Id);if(old==null){if(!string.IsNullOrEmpty(value.Id))throw new ArgumentException("목표를 찾을 수 없습니다.");value.Id=Guid.NewGuid().ToString("N");value.CreatedUtc=DateTime.UtcNow.ToString("o");s.Goals.Add(value);}else{old.Title=value.Title;old.Category=value.Category;old.CharacterKey=value.CharacterKey;old.TargetLevel=value.TargetLevel;old.TargetTalent=value.TargetTalent;}});
 }
 internal void Complete(string id,bool done){Change(s=>s.Goals.Single(x=>x.Id==id).Completed=done);}
 internal void Remove(string id){Change(s=>{var g=s.Goals.Single(x=>x.Id==id);s.Goals.Remove(g);});}
 internal void Pause(string id){Change(s=>{var g=s.Goals.Single(x=>x.Id==id);g.Paused=!g.Paused;});}
 internal void MoveFirst(string id){Change(s=>{var g=s.Goals.Single(x=>x.Id==id);s.Goals.Remove(g);s.Goals.Insert(0,g);});}
 internal void SetBudget(int? value){if(value.HasValue&&(value<0||value>10000))throw new ArgumentOutOfRangeException("value");Change(s=>s.ResinLimit=value);}
}

// Pure admission calculation for future TaskPlan adapters, never an input executor.
// All resin uses (boss/domain/mora/XP) share one budget. Unknown != zero.
internal sealed class GoalPlanInput {
 internal string TaskId;
 internal string GoalId;
 internal int? Resin;
 internal DateTimeOffset? NotBefore;
 internal int[] ServerDays;
 internal bool InventoryKnown;
 internal bool ExecutorAvailable;
}
internal sealed class GoalPlanRow {
 internal string TaskId;
 internal string Status;
 internal int ReservedResin;
}
internal static class GoalPlanner {
 internal static List<GoalPlanRow> Preview(IEnumerable<GoalPlanInput> steps,int? observedResin,int? limit,DateTimeOffset now,DayOfWeek? serverDay){
  if(observedResin<0||limit<0)throw new ArgumentOutOfRangeException("resin");
  int? remaining=observedResin.HasValue&&limit.HasValue?(int?)Math.Min(observedResin.Value,limit.Value):null;
  var seen=new HashSet<string>();var rows=new List<GoalPlanRow>();
  foreach(var step in steps){
   if(string.IsNullOrEmpty(step.TaskId)||string.IsNullOrEmpty(step.GoalId)||step.Resin<0)throw new ArgumentException("Invalid plan step");
   if(!seen.Add(step.TaskId))throw new ArgumentException("Duplicate task ID; shared rewards must reference one task");
   string status;
   if(step.NotBefore.HasValue&&step.NotBefore.Value>now)status="waiting_respawn";
   else if(step.ServerDays!=null&&step.ServerDays.Length>0&&(!serverDay.HasValue||!step.ServerDays.Contains((int)serverDay.Value)))status=serverDay.HasValue?"waiting_weekday":"needs_server_clock";
   else if(!step.InventoryKnown)status="needs_inventory";
   else if(!step.Resin.HasValue)status="needs_cost";
   else if(step.Resin.Value>0&&!remaining.HasValue)status="needs_resin";
   else if(!step.ExecutorAvailable)status="needs_executor";
   else if(step.Resin.Value>0&&step.Resin.Value>remaining.Value)status="waiting_resin";
   else status="planned";
   int reserved=status=="planned"?step.Resin.Value:0;if(remaining.HasValue)remaining-=reserved;
   rows.Add(new GoalPlanRow {TaskId=step.TaskId,Status=status,ReservedResin=reserved});
  }
  return rows;
 }
}

internal sealed class GoalPanel {
 readonly GoalStore store=new GoalStore(Setup.DataFolder);
 readonly DetailPanel detail;
 Action<GoalState> refreshPlan;int refreshGeneration;bool refreshing,refreshAgain;
 readonly StackPanel list=new StackPanel();
 readonly TextBlock message=PanelUi.Text("",true);
 internal Action OpenGoals;
 internal readonly StackPanel View=new StackPanel();
 internal GoalPanel(DetailPanel detail){
  this.detail=detail;
  var add=PanelUi.Button(Locale.T("목표 추가"));var today=PanelUi.Button(Locale.T("오늘 계획"));
  add.Click+=(s,e)=>Edit();today.Click+=(s,e)=>Plan();
  View.Children.Add(PanelUi.Actions(add,today));
  View.Children.Add(message);View.Children.Add(list);
  View.IsVisibleChanged+=(s,e)=>{if(View.IsVisible)Refresh();};
 }
 void Run(Action action){try{action();message.Text="";}catch(Exception){message.Text=Locale.T("목표 기록을 처리하지 못했습니다. 기존 기록은 보존됩니다.");}}
 internal async void Refresh(){int request=++refreshGeneration;if(refreshing){refreshAgain=true;return;}refreshing=true;string account=CalendarStore.AccountScope(Setup.DataFolder);try{var data=await Task.Run(()=>new{State=store.Read(),Urgent=new CalendarStore(Setup.DataFolder).Urgent()});if(request!=refreshGeneration||!View.IsVisible&&!detail.IsShowing("today-plan"))return;if(account!=CalendarStore.AccountScope(Setup.DataFolder)){refreshAgain=true;return;}var state=data.State;message.Text="";if(detail.IsShowing("today-plan")&&refreshPlan!=null)refreshPlan(state);list.Children.Clear();
  var urgent=data.Urgent;if(urgent.Count>0){var rows=new StackPanel();foreach(var item in urgent.Take(5))rows.Children.Add(CalendarPanel.EntryCard(item));var open=PanelUi.Button(Locale.T("캘린더"));open.Click+=(s,e)=>{if(detail.NavigateMenu!=null)detail.NavigateMenu("캘린더");};rows.Children.Add(open);list.Children.Add(PanelUi.Section(Locale.T("곧 마감"),rows));}
  if(state.Goals.Count==0){list.Children.Add(PanelUi.Section(Locale.T("등록된 목표가 없습니다"),PanelUi.Text(Locale.T("예: 벤티 90레벨 재료 확보. 재료 확보와 실제 레벨 올리기는 별도 목표입니다."),true)));return;}
  foreach(var goal in state.Goals){var g=goal;var pause=PanelUi.Button(Locale.T(g.Paused?"다시 계획에 포함":"계획에서 제외"));var first=PanelUi.Button(Locale.T("우선 처리"));
   pause.Click+=(s,e)=>Run(()=>{store.Pause(g.Id);Refresh();});first.Click+=(s,e)=>Run(()=>{store.MoveFirst(g.Id);Refresh();});
   var change=PanelUi.Button(Locale.T("수정"));change.Click+=(s,e)=>EditGoal(g,()=>{detail.Hide();if(OpenGoals!=null)OpenGoals();Refresh();});
   var done=PanelUi.Button(Locale.T(g.Completed?"완료 취소":"완료"));done.Click+=(s,e)=>Run(()=>{store.Complete(g.Id,!g.Completed);Refresh();});
   var remove=PanelUi.Button(Locale.T("삭제"));remove.Click+=(s,e)=>{if(MessageBox.Show(Locale.T("이 목표를 삭제할까요?"),Locale.T("목표 삭제"),MessageBoxButton.YesNo)==MessageBoxResult.Yes)Run(()=>{store.Remove(g.Id);Refresh();});};
   first.IsEnabled=pause.IsEnabled=!g.Completed;
   list.Children.Add(PanelUi.Section(g.Title,PanelUi.Text(Locale.T(g.Completed?"완료":g.Paused?"보류 중":"진행 중"),true),PanelUi.Actions(first,done,PanelUi.ActionMenu(pause,change,remove))));
  }
 }catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.DataRefreshFailure,error);if(request==refreshGeneration)message.Text=Locale.T("목표 기록을 처리하지 못했습니다. 기존 기록은 보존됩니다.");}finally{refreshing=false;if(refreshAgain){refreshAgain=false;Refresh();}}}
 internal void Edit(string initialTitle=""){EditGoal(new GoalRequest{Title=initialTitle,Category="character"},()=>{detail.Hide();if(OpenGoals!=null)OpenGoals();Refresh();});}
 internal void Cultivation(string character=null){
  var body=new StackPanel();var add=PanelUi.Button(Locale.T("육성 목표 추가"));add.Click+=(s,e)=>EditGoal(new GoalRequest{Title=string.IsNullOrEmpty(character)?"":AccountIdentity.CurrentName(Setup.DataFolder,character)+" 육성",Category="character",CharacterKey=character},()=>Cultivation(character),string.IsNullOrEmpty(character)?"Cultivation":"Characters");body.Children.Add(add);
  foreach(var value in store.Read().Goals.Where(x=>x.Category=="character"&&(string.IsNullOrEmpty(character)||x.CharacterKey==character))){var g=value;var change=PanelUi.Button(Locale.T("목표 열기"));change.Click+=(s,e)=>EditGoal(g,()=>Cultivation(character),string.IsNullOrEmpty(character)?"Cultivation":"Characters");var done=PanelUi.Button(Locale.T(g.Completed?"완료 취소":"완료"));done.Click+=(s,e)=>{store.Complete(g.Id,!g.Completed);Cultivation(character);};body.Children.Add(PanelUi.Section(g.Title,PanelUi.Text(Locale.T(g.Completed?"완료":g.Paused?"보류 중":"진행 중"),true),PanelUi.Actions(change,done)));}
  body.Children.Add(PanelUi.Details(Locale.T("육성 재료"),()=>IntegratedWorkflows.Cultivation()));detail.Show("cultivation","육성",body,back:string.IsNullOrEmpty(character)?(Action)null:()=>PanelNavigation.Open(Window.GetWindow(body),"Characters"),owner:string.IsNullOrEmpty(character)?"Cultivation":"Characters");
 }
 void EditGoal(GoalRequest goal,Action saved,string owner="Today"){
  var body=new StackPanel();var title=new TextBox{MaxLength=160,Text=goal.Title??""};string[] categories={"character","story","achievement","daily"};var type=new ComboBox{ItemsSource=Locale.Options("육성","스토리","업적","일상"),SelectedIndex=Math.Max(0,Array.IndexOf(categories,goal.Category))};
  body.Children.Add(PanelUi.InputField(Locale.T("목표"),title));body.Children.Add(PanelUi.InputField(Locale.T("분류"),type));
  var level=new TextBox{Text=goal.TargetLevel.HasValue?goal.TargetLevel.ToString():""};var talent=new TextBox{Text=goal.TargetTalent.HasValue?goal.TargetTalent.ToString():""};var targets=new StackPanel();var character=new ComboBox();character.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");character.Items.Add(new ComboBoxItem{Content=Locale.T("캐릭터 미지정"),Tag=""});Dictionary<string,object> reference;var account=new CatheryneTools(Setup.DataFolder).AccountSnapshot(out reference);object owned;var choices=GameCatalog.CharacterKeys().Concat(account.TryGetValue("characters",out owned)?CodexChat.Items(owned).Select(AccountIdentity.Key):new string[0]).Distinct();foreach(var key in choices){var item=new ComboBoxItem{Content=AccountIdentity.CharacterName(account,key),Tag=key};character.Items.Add(item);if(key==goal.CharacterKey)character.SelectedItem=item;}if(character.SelectedItem==null&&!string.IsNullOrEmpty(goal.CharacterKey)){var item=new ComboBoxItem{Content=AccountIdentity.CharacterName(account,goal.CharacterKey),Tag=goal.CharacterKey};character.Items.Add(item);character.SelectedItem=item;}if(character.SelectedItem==null)character.SelectedIndex=0;targets.Children.Add(PanelUi.InputField(Locale.T("캐릭터"),character));targets.Children.Add(PanelUi.InputField(Locale.T("목표 레벨"),level));targets.Children.Add(PanelUi.InputField(Locale.T("목표 특성 레벨"),talent));body.Children.Add(targets);Action update=()=>targets.Visibility=type.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;type.SelectionChanged+=(s,e)=>update();update();
  var error=PanelUi.Text("",true);var save=PanelUi.Button(Locale.T(string.IsNullOrEmpty(goal.Id)?"목표 등록":"저장"));save.Click+=(s,e)=>{try{int n;int? l=null,t=null;if(type.SelectedIndex==0){if(level.Text.Trim()!=""){if(!int.TryParse(level.Text,out n))throw new ArgumentException("목표 레벨을 숫자로 입력해 주세요.");l=n;}if(talent.Text.Trim()!=""){if(!int.TryParse(talent.Text,out n))throw new ArgumentException("특성 레벨을 숫자로 입력해 주세요.");t=n;}}store.Save(new GoalRequest{Id=goal.Id,Title=title.Text,Category=categories[type.SelectedIndex],CharacterKey=type.SelectedIndex==0?Convert.ToString(((ComboBoxItem)character.SelectedItem).Tag):null,TargetLevel=l,TargetTalent=t});saved();}catch(Exception ex){error.Text=ex.Message;}};body.Children.Add(error);body.Children.Add(save);detail.Show("goal-add",string.IsNullOrEmpty(goal.Id)?"목표 추가":"목표 수정",body,back:saved,owner:owner);
 }
 void Plan(){Run(()=>{
  var state=store.Read();var body=new StackPanel();
  var budget=new TextBox {Text=state.ResinLimit.HasValue?state.ResinLimit.Value.ToString():"",MaxLength=5};var saved=PanelUi.Text("",true);bool bindingBudget=false;
  var autosave=new SettingsAutoSave(()=>{int amount;int? value=null;if(budget.Text.Trim()!=""){if(!int.TryParse(budget.Text,out amount)||amount<0||amount>10000){saved.Text=Locale.T("0~10000 사이의 정수를 입력해 주세요.");return;}value=amount;}try{store.SetBudget(value);saved.Text="";Refresh();}catch(Exception){saved.Text=Locale.T("저장하지 못했습니다. 다시 시도해 주세요.");}});
  budget.TextChanged+=(s,e)=>{if(!bindingBudget)autosave.Request();};body.Unloaded+=(s,e)=>autosave.Dispose();
  body.Children.Add(PanelUi.SectionHelp(Locale.T("공유 레진 한도"),Locale.T("한 번의 오늘 계획에 적용할 상한입니다. 모라·경험치 지맥, 보스, 비경이 함께 사용합니다. 빈칸은 미설정이며 사용 허가가 아닙니다."),PanelUi.InputField(Locale.T("레진 한도"),budget),saved));
  body.Children.Add(IntegratedWorkflows.Cultivation());body.Children.Add(IntegratedWorkflows.Routines());
  var plannedGoals=new StackPanel();body.Children.Add(plannedGoals);
  Action<GoalState> render=current=>{
   if(!autosave.Pending){bindingBudget=true;try{budget.Text=current.ResinLimit.HasValue?current.ResinLimit.Value.ToString():"";}finally{bindingBudget=false;}}
   plannedGoals.Children.Clear();
   foreach(var g in current.Goals.Where(x=>!x.Paused&&!x.Completed))plannedGoals.Children.Add(PanelUi.Section(g.Title,PanelUi.Text(Locale.T("진행 중"),true)));
   if(!current.Goals.Any(x=>!x.Paused&&!x.Completed))plannedGoals.Children.Add(PanelUi.Text(Locale.T("활성 목표가 없습니다. 레진을 임의로 사용하지 않습니다."),true));
  };
  detail.Show("today-plan",Locale.T("오늘 계획"),body,()=>refreshPlan=null,back:()=>{if(OpenGoals!=null)OpenGoals();});refreshPlan=render;render(state);
 });}
}
