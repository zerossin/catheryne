using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class TheaterPanel {
 static readonly string[][] Materials={new[]{"rules","규칙","난이도·출전 조건"},new[]{"events","카드·강화","카드·반응 강화 효과"},new[]{"season","시즌 구성","원소·개막·특별 초청"},new[]{"battles","전투 구성",""},new[]{"account","내 계정",""},new[]{"combat","파티·반응","기본·달·별 반응"},new[]{"statistics","파티 통계","참고용 · 표본 조건 미확인"}};
 static string Amount(Dictionary<string,object> summary,string key,string unit){return summary[key]==null?Locale.T("미확인"):Locale.Format(unit,summary[key]);}
 internal const string StartQuestion="환상극 관제를 시작하거나 진행 중인 도전을 이어가 주세요. 먼저 이번 시즌 자료·판단 원칙·최신 파티 및 기본/달/별 반응 자료의 준비 상태를 확인해 주세요. 새 도전은 start의 자동 계정 최신화가 완료된 뒤 최신 스냅샷으로 시작해 주세요. 새 도전이면 목표와 난이도를 먼저 확인하고, 이미 알려 준 조건은 다시 묻지 마세요. 시즌 참가 조건, 개막·체험·친구 지원 캐릭터, 내 캐릭터 육성과 장비를 비교해 전체 출전 후보와 후반에 남길 역할을 검토해 주세요. 남은 기본 막과 목표에 포함된 추가 도전 각각의 기믹·생존·화력·운용·반응 근거, 편성, 영입 의존과 대체안을 계획하세요. 기믹 대응보다 캐릭터 보존을 우선하지 말고 미검증 편성을 확정적으로 안내하지 마세요. 게임의 준비·출전 화면을 확인한 뒤 도전을 시작하고, 첫 선택과 출전 조합을 추천해 주세요. 진행 중이면 저장된 목표와 실제 상태부터 복원하세요. 전투와 게임 선택은 제가 합니다. 선택·결과 화면 캡처마다 확인된 변화만 증분 갱신하고 다음 계획과 대안을 제시해 주세요.";
 internal static void Show(Window window,DetailPanel detail,Func<string> currentThread,Action<string,string> openChat,bool autoPrepare=true,string dataRoot=null){
  string root=dataRoot??Setup.DataFolder;var service=new TheaterService(root);
  var body=new StackPanel();
  var season=PanelUi.Text("");
  var elements=new WrapPanel();elements.Margin=new Thickness(0,0,0,8);
  var deadline=new ContentControl();deadline.Margin=new Thickness(0);
  var materialRows=new StackPanel();var details=new StackPanel();
  var progress=PanelUi.Text("",true);var status=PanelUi.Text("",true);status.Visibility=Visibility.Collapsed;
  var start=PanelUi.Button(Locale.T("채팅에서 시작"));PanelUi.PrimaryAction(start,true);
  body.Children.Add(PanelUi.SeasonHeader(season,elements,deadline));
  body.Children.Add(PanelUi.SectionHelp(Locale.T("준비 자료"),Locale.T("시즌과 규칙 자료를 자동으로 준비합니다. 파티와 기본·달·별 반응도 공개 원문을 준비합니다. 공개 자료가 없는 항목은 게임 화면으로 확인합니다. 새 도전에서는 계정 최신화 작업이 먼저 표시됩니다. 시작 후 채팅에서 목표·난이도와 출전 후보를 검토합니다. 전투와 선택은 직접 진행하고, 캡처 단축키로 선택·결과 화면을 보내면 다음 계획을 이어갑니다."),materialRows));
  body.Children.Add(progress);
  var actions=PanelUi.FooterActions(new[]{1},start);actions.Margin=new Thickness(0,0,0,16);body.Children.Add(actions);body.Children.Add(status);
  body.Children.Add(PanelUi.Details(Locale.T("자료 상세"),details));
  bool loading=false;int renderGeneration=0;
  Func<Task> render=async()=>{
   int request=++renderGeneration;string thread=currentThread()??"theater-preview";Dictionary<string,object> data;try{data=await Task.Run(()=>{var snapshot=service.Status(thread);snapshot["resumeTarget"]=service.ResumeTarget(thread);return snapshot;});}catch(Exception error){if(request==renderGeneration){status.Text=error.Message;status.Visibility=Visibility.Visible;}return;}if(request!=renderGeneration||!detail.IsShowing("Theater"))return;var ready=CodexChat.Map(data["readiness"]);var summary=CodexChat.Map(data["preparedData"]);
   DateTime month;season.Text=DateTime.TryParseExact(CodexChat.S(ready,"period"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out month)?Locale.Format("{0}년 {1}월 환상극",month.Year,month.Month):Locale.T("환상극");
   var facts=data.ContainsKey("seasonFacts")?CodexChat.Map(data["seasonFacts"]):new Dictionary<string,object>();
   string rawElements=CodexChat.S(facts,"elements");elements.Children.Clear();
   if(rawElements.Length==0)elements.Children.Add(PanelUi.Text(Locale.T("시즌 자료 확인 필요"),true));
   else foreach(string item in rawElements.Split(';')){var icon=GameCatalog.ElementIcon(item.Trim(),26);icon.Margin=new Thickness(0,0,12,0);elements.Children.Add(icon);}
   deadline.Content=CalendarPanel.Timing(CatheryneTools.Json().Deserialize<CalendarEntry>(CatheryneTools.Json().Serialize(data["schedule"])));
   materialRows.Children.Clear();details.Children.Clear();
   for(int i=0;i<Materials.Length;i++){
    var item=Materials[i];bool available=Equals(ready[item[0]],true);string description=Locale.T(item[2]);
    if(item[0]=="battles")description=Locale.Format("기본 {0}막 · 추가 전투 {1}개",summary["acts"],summary["additionalBattles"]);
    if(item[0]=="account")description=Locale.Format("캐릭터 {0}명",summary["characters"]);
    var value=PanelUi.Text(available?description:Locale.T("확인 필요"),true);value.FontSize=13;value.Margin=new Thickness(0);value.MaxWidth=360;value.TextAlignment=TextAlignment.Right;
    if(!available)value.Foreground=new SolidColorBrush(Color.FromRgb(240,190,128));
    var row=PanelUi.Row(Locale.T(item[1]),value);row.Margin=new Thickness(0,0,0,i==Materials.Length-1?0:12);materialRows.Children.Add(row);
   }
   foreach(var reference in CodexChat.Items(data["references"])){
    string topic=CodexChat.S(reference,"topic");var item=Materials.FirstOrDefault(x=>x[0]==topic);string label=item!=null?item[1]:topic=="teams"?"파티 구성":topic=="reactions"?"격변 반응":topic=="amplifying"?"증폭 반응":topic=="additive"?"격화 반응":topic=="gauge"?"원소 부착":topic=="damage"?"피해 계산":topic=="lunar"?"달 반응":topic=="stellar"?"별 반응":topic=="guides"?"캐릭터 공략":"";if(label.Length==0)continue;DateTime at;
    details.Children.Add(PanelUi.Section(Locale.T(label),PanelUi.Text(Locale.T("공개 공략 자료")+(CodexChat.S(reference,"version").Length>0?" · "+CodexChat.S(reference,"version"):""),true),PanelUi.Text(CodexChat.S(reference,"source"),true),PanelUi.Text(DateTime.TryParse(CodexChat.S(reference,"checkedAt"),out at)?Locale.Format("{0} 확인",at.ToLocalTime().ToString("g",Locale.Culture)):Locale.T("확인 시각 미상"),true)));
   }
   details.Children.Add(PanelUi.Text(Locale.Format("계정 자료: 캐릭터 {0} · 무기 {1} · 성유물 {2}",Amount(summary,"characters","{0}명"),Amount(summary,"weapons","{0}개"),Amount(summary,"artifacts","{0}개")),true));
   var journal=data["resumeTarget"] as Dictionary<string,object>;var state=journal==null?new Dictionary<string,object>():TheaterService.Project(journal);
   bool ongoing=journal!=null;start.Content=Locale.T(ongoing?"채팅에서 이어가기":"채팅에서 시작");progress.Visibility=ongoing?Visibility.Visible:Visibility.Collapsed;
   progress.Text=ongoing?Locale.T("진행 중인 도전")+(state.ContainsKey("act")&&Convert.ToInt32(state["act"])>0?" · "+state["act"]+Locale.T("막"):"")+" · "+CodexChat.S(journal,"goal"):"";
   var errors=CodexChat.Map(ready["errors"]);status.Text=string.Join("\n",errors.Values.Select(x=>Locale.T(Convert.ToString(x))).Distinct().ToArray());status.Visibility=status.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
  };
  Func<bool,Task> prepare=async force=>{
   if(loading)return;loading=true;start.IsEnabled=false;status.Text=Locale.T("자료 준비 중…");status.Visibility=Visibility.Visible;
   try{await Task.Run(()=>new TheaterKnowledge(root).Prepare(force));await render();}
   catch(Exception error){status.Text=error.Message;status.Visibility=Visibility.Visible;}
   finally{loading=false;start.IsEnabled=true;}
  };
  var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};timer.Tick+=async(s,e)=>{if(!body.IsVisible||!detail.IsShowing("Theater"))return;try{await render();if(autoPrepare)await prepare(false);}catch(Exception error){status.Text=error.Message;status.Visibility=Visibility.Visible;}};body.Loaded+=(s,e)=>timer.Start();body.Unloaded+=(s,e)=>timer.Stop();body.IsVisibleChanged+=(s,e)=>{if(body.IsVisible)timer.Start();else timer.Stop();};
  start.Click+=async(s,e)=>{await prepare(false);var active=service.ResumeTarget(currentThread()??"theater-preview");openChat(StartQuestion,active==null?null:CodexChat.S(active,"thread"));};
  detail.Show("Theater",Locale.T("환상극"),body,owner:"Theater");var initial=render();if(autoPrepare){var task=prepare(false);}
 }
}
