using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

internal sealed class StoryPanel {
 internal readonly StackPanel View=new StackPanel();
 internal readonly Grid Footer;
 FrameworkElement failureRow,controlSection;
 readonly Window window;readonly DetailPanel detail;
 readonly ComboBox plans=new ComboBox{MinHeight=36};readonly Button newPlan=PanelUi.Button(Locale.T("계획 만들기"));
 readonly StoryClient client=new StoryClient(Setup.DataFolder);
 readonly TextBlock state=Text(Locale.T("관제 상태 확인 중…"),18), details=Text("",14), progress=Text("",14), evidence=Text("",13), message=Text("",13);
 readonly Button start=Button(Locale.T("관제 시작")), choose=Button(Locale.T("임무 계획 선택")), stop=Button(Locale.T("입력 중지")), take=Button(Locale.T("내가 직접 조작")), resume=Button(Locale.T("AI에 다시 맡기기")), end=Button(Locale.T("관제 종료"));
 readonly CheckBox input=new CheckBox {IsChecked=false,Margin=new Thickness(0)};
 readonly ComboBox combat=new ComboBox {Width=180,HorizontalAlignment=HorizontalAlignment.Left};
 readonly TextBox failures=new TextBox {Text="2",Width=70,HorizontalAlignment=HorizontalAlignment.Left};
 readonly CheckBox notifications=new CheckBox {Content=Locale.T("알림 설정"),IsChecked=false};
 readonly SettingsAutoSave settingsSave;
 bool rendering;
 readonly DispatcherTimer timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
 bool polling,command,visible,pageVisible,detailVisible,dirty,connected;
 string currentControl;bool inputOwned;
 long cursor;
 long revision;
 internal StoryPanel(Window window,DetailPanel detail=null){
  this.window=window;this.detail=detail;PanelUi.PrimaryAction(start,true);combat.Style=(Style)window.FindResource("OptionsCombo");
  state.FontSize=14;state.FontWeight=FontWeights.SemiBold;
  plans.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");plans.SelectionChanged+=(s,e)=>{var selected=plans.SelectedItem as ComboBoxItem;if(selected==null||connected)return;try{client.SetPlan(Convert.ToString(selected.Tag));message.Text="";SetConnected(connected);}catch(Exception ex){message.Text=ex.Message;}};newPlan.Click+=(s,e)=>EditPlan();
  var prepare=new StackPanel();prepare.Children.Add(PanelUi.InputField(Locale.T("임무 계획"),plans));prepare.Children.Add(PanelUi.Actions(newPlan,choose));View.Children.Add(PanelUi.Section(Locale.T("계획"),prepare));
  var missionCard=PanelUi.Section(Locale.T("현재 임무"),state,details,progress);View.Children.Add(missionCard);
  var controls=new StackPanel();foreach(var button in new[]{stop,take,resume}){button.HorizontalAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(0,0,0,8);controls.Children.Add(button);}resume.Margin=new Thickness(0);
  controlSection=PanelUi.Section(Locale.T("조작"),controls);View.Children.Add(controlSection);
  combat.Items.Add(Locale.T("자동"));combat.Items.Add(Locale.T("직접 수행"));combat.Items.Add(Locale.T("실패 시 직접 수행"));combat.SelectedIndex=0;
  failureRow=PanelUi.Row(Locale.T("실패 횟수"),failures,Locale.T("관제가 실패로 기록한 전투 시도가 이 횟수에 도달하면 사용자에게 조작권을 넘깁니다. 화면 판독 없이 패배를 자동 판단하는 기능은 아닙니다."));failureRow.Visibility=Visibility.Collapsed;
  View.Children.Add(PanelUi.Section(Locale.T("수행 방식"),PanelUi.Row(Locale.T("전투"),combat,Locale.T("자동은 연결된 AI와 도구가 수행합니다. 직접 수행은 전투 관측 시 조작권을 넘깁니다.")),failureRow));
  var advanced=new StackPanel();advanced.Children.Add(PanelUi.Row(Locale.T("게임 입력 허용"),input,Locale.T("다음 관제 시작부터 적용됩니다. 해제하면 기록과 상태만 확인하며 게임에는 입력하지 않습니다.")));
  View.Children.Add(PanelUi.Details(Locale.T("고급 설정"),advanced));
  if(detail!=null){var inspect=PanelUi.Button(Locale.T("실행 현황 자세히"));inspect.Click+=(s,e)=>{var body=new StackPanel();body.Children.Add(PanelUi.Section(Locale.T("현재 임무"),DetailPanel.Mirror(state)));body.Children.Add(PanelUi.Section(Locale.T("실행 현황"),DetailPanel.Mirror(details),DetailPanel.Mirror(progress)));body.Children.Add(PanelUi.Section(Locale.T("진행 근거"),DetailPanel.Mirror(evidence)));body.Children.Add(PanelUi.Section(Locale.T("최근 안내"),DetailPanel.Mirror(message)));detail.Show("story-runtime",Locale.T("스토리 실행 현황"),body,()=>{detailVisible=false;Visible(pageVisible);},back:()=>PanelNavigation.Open(window,"Story"));detailVisible=true;Visible(pageVisible);};((StackPanel)missionCard.Child).Children.Add(PanelUi.Actions(inspect));}
  else View.Children.Add(PanelUi.Details(Locale.T("진행 근거"),evidence));
  View.Children.Add(message);
  Footer=PanelUi.FooterActions(new[]{1,1},start,end);
  choose.Content=Locale.T("계획 불러오기");choose.Click+=(s,e)=>{Choose();LoadPlans();};start.Click+=async(s,e)=>await Start();
  stop.Click+=async(s,e)=>await Send("set_control",new{target="user",reason="user_stop"});
  take.Click+=async(s,e)=>await Send("set_control",new{target="user",reason="user_request"});
  resume.Click+=async(s,e)=>{if(await GameRequirement.Ensure(window))await Send("set_control",new{target="agent",reason="user_request"});};
  end.Click+=async(s,e)=>await Send("shutdown",new{});
  combat.SelectionChanged+=(s,e)=>{dirty=true;failureRow.Visibility=combat.SelectedIndex==2?Visibility.Visible:Visibility.Collapsed;};failures.TextChanged+=(s,e)=>dirty=true;
  settingsSave=new SettingsAutoSave(async()=>{if(!connected||!dirty||command)return;int count;if(!int.TryParse(failures.Text,out count)||count<1||count>10){message.Text=Locale.T("실패 횟수는 1~10으로 입력해 주세요.");return;}await Send("configure",new{combat=new[]{"auto","manual","on_failure"}[combat.SelectedIndex],failure_limit=count,notifications=notifications.IsChecked==true});});
  combat.SelectionChanged+=(s,e)=>{if(!rendering)settingsSave.Request();};failures.TextChanged+=(s,e)=>{if(!rendering)settingsSave.Request();};

  timer.Tick+=async(s,e)=>await Poll();window.Closed+=(s,e)=>{settingsSave.Dispose();timer.Stop();};SetConnected(false);
 }
 static TextBlock Text(string value,int size){return PanelUi.Text(value,true);}
 static Button Button(string value){return PanelUi.Button(value);}
 internal void Visible(bool value){pageVisible=value;visible=pageVisible||detailVisible;if(visible&&window.IsVisible){LoadPlans();timer.Start();var task=Poll();}else timer.Stop();}
 void SetConnected(bool value){connected=value;controlSection.Visibility=value?Visibility.Visible:Visibility.Collapsed;plans.IsEnabled=newPlan.IsEnabled=!value&&!command;choose.IsEnabled=!value&&!command;start.IsEnabled=!value&&!command&&plans.SelectedItem!=null;end.IsEnabled=value&&!command;stop.IsEnabled=value&&!command;take.IsEnabled=value&&!command&&currentControl!="user";resume.IsEnabled=value&&!command&&currentControl!="agent"&&!inputOwned;combat.IsEnabled=value&&!command;failures.IsEnabled=value&&!command;}
 void LoadPlans(){
  if(connected)return;string folder=Path.Combine(Setup.DataFolder,"plans");var selected=client.Config();string current=selected.ContainsKey("story_plan")?Convert.ToString(selected["story_plan"]):"";var files=Directory.Exists(folder)?Directory.GetFiles(folder,"*.json").Where(x=>!Path.GetFileName(x).StartsWith("ai-")).ToList():new List<string>();if(File.Exists(current)&&!Path.GetFileName(current).StartsWith("ai-")&&!files.Contains(current))files.Add(current);
  plans.Items.Clear();foreach(string path in files){try{var plan=StoryClient.Read(path);var item=new ComboBoxItem{Content=Convert.ToString(plan["title"]),Tag=path};plans.Items.Add(item);if(path==current)plans.SelectedItem=item;}catch{}}SetConnected(connected);
 }
 void EditPlan(){if(detail==null)return;var body=new StackPanel();var title=new TextBox();var milestones=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=100};var boundary=new TextBox{Text=Locale.T("이동과 대화. 재화 사용과 보상 수령 제외.")};var error=PanelUi.Text("",true);body.Children.Add(PanelUi.InputField(Locale.T("임무 이름"),title));body.Children.Add(PanelUi.InputField(Locale.T("완료할 단계 (한 줄에 하나)"),milestones));body.Children.Add(PanelUi.InputField(Locale.T("수행 범위"),boundary));body.Children.Add(PanelUi.Help(Locale.T("계획은 기존 관제에 전달됩니다. 계획 저장이나 관제 시작만으로 게임이 자동 진행되지는 않으며, 실제 조작에는 연결된 실행자가 필요합니다.")));var save=PanelUi.Button(Locale.T("계획 저장"));save.Click+=(s,e)=>{try{var steps=milestones.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray();if(string.IsNullOrWhiteSpace(title.Text)||string.IsNullOrWhiteSpace(boundary.Text)||steps.Length==0)throw new ArgumentException("임무 이름, 완료할 단계와 수행 범위를 입력해 주세요.");string id="plan-"+Guid.NewGuid().ToString("N"),folder=Path.Combine(Setup.DataFolder,"plans");Directory.CreateDirectory(folder);string path=Path.Combine(folder,id+".json");AtomicFile.Write(path,CatheryneTools.Json().Serialize(new{id=id,title=title.Text.Trim(),draft=false,scope=boundary.Text.Trim(),sources=new object[0],steps=steps.Select((x,i)=>new{id="step-"+i,title=x,modes=new[]{"navigation","interaction","dialogue","paused"},tools=new[]{"sequence","computer_use"},completion=x,checkpoint="현재 화면에서 결과 확인",failure_cost="중단 후 새 화면 확인",sources=new object[0]}).ToArray()}));client.SetPlan(path);detail.Hide();PanelNavigation.Open(window,"Story");LoadPlans();}catch(Exception ex){error.Text=ex.Message;}};body.Children.Add(error);body.Children.Add(save);detail.Show("story-plan-create","임무 계획",body,back:()=>PanelNavigation.Open(window,"Story"));}
 void Choose(){var picker=new Microsoft.Win32.OpenFileDialog {Title=Locale.T("임무 계획 선택"),Filter=Locale.T("임무 계획|*.json")};if(picker.ShowDialog(window)!=true)return;try{client.SetPlan(picker.FileName);cursor=0;message.Text=Locale.T("임무 계획을 선택했습니다. 관제를 시작하면 AI와 같은 진행 기록을 사용합니다.");}catch(Exception){message.Text=Locale.T("임무 계획을 읽지 못했습니다. JSON 형식을 확인해 주세요.");}}
 async Task Start(){if(plans.SelectedItem==null)return;command=true;SetConnected(connected);try{bool enabled=input.IsChecked==true;if(enabled&&!await GameRequirement.Ensure(window))return;await Task.Run(()=>client.Start(enabled));message.Text=Locale.T("관제를 시작하고 있습니다. AI 연결은 같은 작업 기록을 사용합니다.");}catch(Exception e){message.Text=e.Message;}finally{command=false;SetConnected(connected);}await Poll();}
 async Task Send(string method,object parameters){revision++;command=true;SetConnected(connected);try{var result=await Task.Run(()=>client.Call(method,parameters));message.Text=method=="shutdown"?Locale.T("관제를 종료했습니다. 진행 기록은 보존됩니다."):method=="configure"?Locale.T("전투 설정을 저장했습니다."):Locale.T("요청을 반영했습니다. AI 재인계는 새 화면 확인 후 진행됩니다.");if(method=="configure")dirty=false;if(method=="shutdown"){SetConnected(false);state.Text=Locale.T("관제 종료됨");}else Render(result);}catch(Exception e){message.Text=e.Message;}finally{command=false;SetConnected(connected);} }
 async Task Poll(){if(polling||command||!visible)return;polling=true;try{long observedRevision=revision;long after=cursor;var result=await Task.Run(()=>client.Call("status",new{after_sequence=after}));if(visible&&observedRevision==revision)Render(result);}catch(Exception e){if(visible){SetConnected(false);state.Text=plans.SelectedItem==null?Locale.T("계획 미선택"):e.Message;details.Text=Locale.T("임무 계획을 선택해 주세요.");try{var config=client.Config();if(config.ContainsKey("story_plan")&&!Path.GetFileName(Convert.ToString(config["story_plan"])).StartsWith("ai-")){var plan=StoryClient.Read(Convert.ToString(config["story_plan"]));details.Text=Locale.T("선택된 임무: ")+Convert.ToString(plan["title"]);}}catch{};progress.Text="";evidence.Text="";}}finally{polling=false;}}
 static string S(Dictionary<string,object> d,string key){object value;return d.TryGetValue(key,out value)?Convert.ToString(value):"";}
 static bool B(Dictionary<string,object> d,string key){object value;return d.TryGetValue(key,out value)&&value is bool&&(bool)value;}
 void Render(Dictionary<string,object> d){
  if(!d.ContainsKey("mode"))return;currentControl=S(d,"control");inputOwned=!string.IsNullOrEmpty(S(d,"owner"));SetConnected(true);if(d.ContainsKey("cursor"))cursor=Convert.ToInt64(d["cursor"]);
  state.Text=S(d,"plan_title")+"  "+S(d,"stage_title");
  var modes=new Dictionary<string,string>{{"idle",Locale.T("관측 대기")},{"navigation",Locale.T("이동")},{"dialogue",Locale.T("대화")},{"interaction",Locale.T("상호작용")},{"puzzle",Locale.T("퍼즐")},{"combat",Locale.T("전투")},{"paused",Locale.T("일시정지")},{"cutscene",Locale.T("영상")},{"escape",Locale.T("탈출")},{"stealth",Locale.T("잠입")},{"recovery",Locale.T("회복")}};
  string mode=S(d,"mode"),control=S(d,"control");
  details.Text=Locale.T("현재: ")+(modes.ContainsKey(mode)?modes[mode]:mode)+"  "+(B(d,"handoff_pending")?Locale.T("입력 해제 대기"):control=="user"?Locale.T("사용자 조작"):B(d,"stopped")?Locale.T("실행 대기 / 중단"):Locale.T("AI 관측·판단"))+"  "+(B(d,"input_enabled")?Locale.T("입력 허용"):Locale.T("무입력 모드"));
  if(d.ContainsKey("session_seconds"))details.Text+=Locale.T("\n경과 ")+TimeSpan.FromSeconds(Convert.ToDouble(d["session_seconds"])).ToString(@"hh\:mm\:ss");
  if(d.ContainsKey("supervisor_wait_seconds")&&d["supervisor_wait_seconds"]!=null)details.Text+=Locale.T("\nAI 응답 대기 ")+Math.Floor(Convert.ToDouble(d["supervisor_wait_seconds"]))+Locale.T("초");
  if(d.ContainsKey("observation_wall")&&d["observation_wall"]!=null)details.Text+=Locale.T("\n마지막 관측 ")+Math.Max(0,Math.Floor((DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds-Convert.ToDouble(d["observation_wall"])))+Locale.T("초 전");
  if(d.ContainsKey("progress")){var p=(Dictionary<string,object>)d["progress"];progress.Text=Locale.T("확인된 완료 단계 ")+S(p,"completed")+" / "+S(p,"total");}
  string alert=S(d,"alert");evidence.Text=S(d,"evidence")+(string.IsNullOrEmpty(alert)?"":"\n"+StoryAlerts.Text(alert));
  resume.IsEnabled=!command&&control!="agent"&&string.IsNullOrEmpty(S(d,"owner"));take.IsEnabled=!command&&control!="user";
  if(!dirty&&d.ContainsKey("settings")){rendering=true;try{var p=(Dictionary<string,object>)d["settings"];string policy=S(p,"combat");combat.SelectedIndex=policy=="manual"?1:policy=="on_failure"?2:0;failures.Text=S(p,"failure_limit");notifications.IsChecked=B(p,"notifications");dirty=false;}finally{rendering=false;}}
 }
}
