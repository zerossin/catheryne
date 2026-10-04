using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal sealed class PrimogemPanel {
 internal readonly StackPanel View=new StackPanel();
 readonly Window window;
 readonly string root;
 readonly PrimogemService service;
 readonly ComboBox versions=new ComboBox{MinWidth=140};
 readonly TextBlock status=PanelUi.Text("",true);
 readonly StackPanel content=new StackPanel(),rewardList=new StackPanel();
 readonly TextBox rewardName=new TextBox(),total=new TextBox(),received=new TextBox();
 readonly ComboBox currency=new ComboBox();
 readonly Button apply=PanelUi.Button(Locale.T("기록 반영")),remove=PanelUi.Button(Locale.T("기록 삭제"));
 string VersionValue {get{return Convert.ToString(versions.SelectedItem);}}
 Expander editor,categoryDetails;string renderedVersion,renderedAccount;string selectedId,editorAccount,editorVersion;bool loading,binding;int generation,reportGeneration;
 internal PrimogemPanel(Window window,string root){
  this.window=window;this.root=root;service=new PrimogemService(root);
  versions.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");versions.MinHeight=36;
  View.Children.Add(PanelUi.Row(Locale.T("버전"),versions));
  versions.SelectionChanged+=async(s,e)=>{if(!binding){SelectionChanged();await RefreshSelected(false);}};
  View.Children.Add(content);View.Children.Add(status);
  foreach(string name in new[]{"원석","뒤얽힌 인연","만남의 인연","창세의 결정"})currency.Items.Add(Locale.T(name));currency.SelectedIndex=0;
  var fields=new StackPanel();fields.Children.Add(PanelUi.InputField(Locale.T("보상 이름"),rewardName));fields.Children.Add(PanelUi.InputField(Locale.T("재화"),currency));fields.Children.Add(PanelUi.InputField(Locale.T("전체 수량"),total));fields.Children.Add(PanelUi.InputField(Locale.T("받은 수량 (미확인이면 비워 두기)"),received));
  apply.Click+=(s,e)=>Save(false);remove.Click+=(s,e)=>Save(true);remove.Visibility=Visibility.Collapsed;fields.Children.Add(PanelUi.Actions(apply,remove));editor=PanelUi.Details(Locale.T("보상 추가"),fields);
  editor.Expanded+=(s,e)=>{if(selectedId==null){editorAccount=service.Account();editorVersion=VersionValue.Trim();}};
  var add=PanelUi.Button(Locale.T("새 보상"));add.Click+=(s,e)=>{ResetEditor();editor.IsExpanded=true;};
  View.Children.Add(PanelUi.Details(Locale.T("직접 기록한 보상"),PanelUi.SectionHelp(Locale.T("직접 기록한 보상"),Locale.T("등록한 항목만 집계합니다. 받은 수량은 직접 확인한 값이며 일기 수입에 다시 더하지 않습니다. 공월·유료 기행·상점 교환은 조건에 맞는 항목을 따로 기록하세요."),rewardList,PanelUi.Actions(add),editor)));
  View.IsVisibleChanged+=async(s,e)=>{++generation;if(View.IsVisible){LoadVersions();Render();await Refresh(false);}};
 }
 internal void Open(){LoadVersions();Render();}
 void LoadVersions(){
  binding=true;try{string chosen=VersionValue;var sorted=service.Versions();versions.ItemsSource=sorted;versions.SelectedItem=sorted.Contains(chosen)?chosen:service.CurrentVersion()??sorted.FirstOrDefault();
  }finally{binding=false;}
 }
 void SelectionChanged(){if(binding)return;++generation;try{PrimogemService.ValidateVersion(VersionValue.Trim());AppPreferences.Set("primoVersion",VersionValue.Trim(),root);ResetEditor();Render();}catch(ArgumentException){status.Text=Locale.T("버전은 7.1처럼 입력해 주세요.");}}
 async Task RefreshSelected(bool force){string selected=VersionValue;if(string.IsNullOrEmpty(selected))return;int request=generation;try{await Task.Run(()=>service.RefreshBudget(selected,force));if(request==generation&&selected==VersionValue)Render();}catch{if(request==generation)status.Text=Locale.T("공개 명세서를 갱신하지 못했습니다.");}}
 internal async Task Refresh(bool force){
  if(loading)return;loading=true;int request=generation;status.Text=Locale.T("원석 기록을 불러오는 중…");
  try{
   string errors=await Task.Run(()=>{var messages=new System.Collections.Generic.List<string>();try{service.RefreshCatalog(force);}catch{messages.Add(Locale.T("공개 명세서를 갱신하지 못했습니다."));}try{AchievementCatalog.Load(root,true);}catch{messages.Add(Locale.T("업적 목록을 불러오지 못했습니다."));}try{service.Refresh(force);}catch(Exception e){messages.Add(Locale.T(e.Message));}return string.Join("\n",messages);});
   if(request!=generation||!View.IsVisible)return;LoadVersions();string selected=VersionValue;try{if(!string.IsNullOrEmpty(selected))await Task.Run(()=>service.RefreshBudget(selected,force));}catch{errors+=(errors.Length>0?"\n":"")+Locale.T("공개 명세서를 갱신하지 못했습니다.");}if(request!=generation||!View.IsVisible)return;Render();status.Text=errors;
  }catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.DataRefreshFailure,error,root);if(request==generation)status.Text=Locale.T(error.Message);}finally{loading=false;if(request!=generation&&View.IsVisible){var pending=Refresh(false);}}
 }
 void ResetEditor(){selectedId=null;rewardName.Text=total.Text=received.Text="";currency.SelectedIndex=0;editor.Header=Locale.T("보상 추가");remove.Visibility=Visibility.Collapsed;editor.IsExpanded=false;editorAccount=service.Account();editorVersion=VersionValue.Trim();}
 static readonly string[] Currencies={"primogem","intertwined","acquaint","crystal"};
 internal static string Currency(string name){return Locale.T(name=="primogem"?"원석":name=="intertwined"?"뒤얽힌 인연":name=="acquaint"?"만남의 인연":"창세의 결정");}
 void Save(bool deleted){try{int amount,claimed;if(!int.TryParse(total.Text,out amount)||(!string.IsNullOrWhiteSpace(received.Text)&&!int.TryParse(received.Text,out claimed)))throw new ArgumentException("보상 이름과 수량을 확인해 주세요.");var reward=new PrimoReward{Id=selectedId,Version=editorVersion,Name=rewardName.Text.Trim(),Currency=Currencies[currency.SelectedIndex],Total=amount,Received=string.IsNullOrWhiteSpace(received.Text)?(int?)null:int.Parse(received.Text),Deleted=deleted};service.SaveReward(editorAccount,reward);ResetEditor();Render();status.Text="";}catch(Exception e){status.Text=Locale.T(e.Message);}}
 void Edit(PrimoReward row){selectedId=row.Id;editorAccount=renderedAccount;editorVersion=row.Version;rewardName.Text=row.Name;currency.SelectedIndex=Array.IndexOf(Currencies,row.Currency);total.Text=row.Total.ToString();received.Text=row.Received.HasValue?row.Received.Value.ToString():"";editor.Header=Locale.T("보상 수정");remove.Visibility=Visibility.Visible;editor.IsExpanded=true;}
 static TextBlock Large(string value){var text=PanelUi.Text(value);text.FontSize=26;text.LineHeight=34;return text;}
 internal void Render(){var pending=RenderAsync();}
 internal async Task RenderAsync(Func<string,PrimoReport> read=null){
  string version=VersionValue,account=service.Account(),selection=CalendarStore.AccountScope(root);int request=++reportGeneration,owner=generation;if(string.IsNullOrEmpty(version))return;
  try{var report=await Task.Run(()=>read==null?service.ReadVersion(version):read(version));if(request==reportGeneration&&owner==generation&&version==VersionValue&&View.IsVisible){if(account!=service.Account()||report.Account!=account||selection!=CalendarStore.AccountScope(root)){Render();return;}RenderReport(report);}}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.DataRefreshFailure,error,root);if(request==reportGeneration&&owner==generation)status.Text=Locale.T(error.Message);}
 }
 internal static double? Percent(int? received,decimal? total){return received.HasValue&&total.HasValue&&total.Value>0?(double?)(100m*received.Value/total.Value):null;}
 internal static string CategoryName(string key){return Locale.T(key=="events"?"이벤트":key=="mail"?"웹·이메일":key=="daily"?"일일 의뢰":key=="endgame"?"주기 갱신 도전":key=="battlepass"?"무료 기행":key=="permanent"?"임무·탐험·업적":"기타 수입");}
 internal static string CategoryHelp(string key){
  switch(key){
   case "events":return Locale.T("기간 한정 이벤트의 원석 보상입니다. 예상량은 공개 명세서의 이벤트 보상, 수입은 여행자 일기의 이벤트 분류를 기준으로 합니다.");
   case "mail":return Locale.T("웹 이벤트·우편·점검 보상 등의 예상 원석입니다. 수입은 여행자 일기의 이메일 보상으로 집계하므로, 일기에서 다른 분류로 기록된 웹 보상은 해당 분류에 포함됩니다.");
   case "daily":return Locale.T("버전 기간의 일일 의뢰 보상입니다. 예상량은 명세서의 버전 일수 기준이며, 받은 양은 여행자 일기의 일일 의뢰 수입입니다.");
   case "endgame":return Locale.T("나선 비경·현실 속 환상극 등 주기적으로 보상이 갱신되는 도전입니다. 임무·탐험처럼 한 번 받는 보상과 구분합니다.");
   case "battlepass":return Locale.T("무료 기행의 원석 보상만 비교합니다. 유료 기행과 인연은 예상량에서 제외하며, 일기에서 기행으로 확인된 수입을 집계합니다.");
   case "permanent":return Locale.T("임무·상자·탐험·워프 해금·업적 등의 원석입니다. 예상량은 이번 버전에 추가된 콘텐츠 기준이지만, 수입에는 예전 콘텐츠에서 이번 기간에 받은 원석도 포함되어 100%를 넘을 수 있습니다. 업적은 일기에 기록된 수입만 포함하며, 아래 업적 달성 보상을 다시 더하지 않습니다.");
   default:return Locale.T("위 항목으로 확실하게 분류하지 못한 일기 수입입니다. 전체 수입에는 포함하지만 비교할 예상량은 미확인으로 둡니다.");
  }
 }
 internal static Color CategoryColor(string key){return (Color)ColorConverter.ConvertFromString(key=="events"?"#B69AED":key=="mail"?"#F0B675":key=="daily"?"#76CDB3":key=="endgame"?"#F28EA3":key=="battlepass"?"#C3C874":key=="permanent"?"#79BFE0":"#9AA7B8");}
 internal void RenderReport(PrimoReport report){
  renderedAccount=report.Account;bool expanded=renderedVersion==report.Version&&categoryDetails!=null&&categoryDetails.IsExpanded;renderedVersion=report.Version;
  content.Children.Clear();rewardList.Children.Clear();
  string help=Locale.T("버전 기간에 여행자 일기로 확인한 수입과 해당 버전의 무료 원석 예상량을 비교합니다. 이전 콘텐츠나 유료 보상에서 얻은 원석도 수입에 포함되어 예상량을 넘을 수 있습니다. 남은 수령 가능량을 뜻하지 않으며, 인연은 원석으로 환산하지 않습니다.");
  var overall=new StackPanel();overall.Children.Add(PanelUi.Heading(PanelUi.Text(Locale.T("전체")),PanelUi.Help(help)));
  string amount=report.Income.Amount.HasValue?report.Income.Amount.Value.ToString("N0"):Locale.T("미확인");string maximum=report.Budget!=null&&report.Budget.Primogems.HasValue?report.Budget.Primogems.Value.ToString("N0"):Locale.T("미확인");
  overall.Children.Add(Large(Locale.Format("{0} / {1} 원석",amount,maximum)));
  overall.Children.Add(PanelUi.Meter(null,null,Percent(report.Income.Amount,report.Budget==null?null:report.Budget.Primogems),Color.FromRgb(100,178,250),help));
  var bars=new StackPanel();foreach(var row in report.Progress??PrimogemService.Progress(report.Budget,report.Income))bars.Children.Add(PanelUi.Meter(CategoryName(row.Key),Locale.Format("{0} / {1} 원석",row.Received.HasValue?row.Received.Value.ToString("N0"):Locale.T("미확인"),row.Total.HasValue?row.Total.Value.ToString("N0"):Locale.T("미확인")),Percent(row.Received,row.Total),CategoryColor(row.Key),CategoryHelp(row.Key),CategoryHelp(row.Key)));
  categoryDetails=PanelUi.Details(Locale.T("항목별 보기"),bars);categoryDetails.IsExpanded=expanded;overall.Children.Add(categoryDetails);content.Children.Add(overall);
  if(!report.PeriodKnown)content.Children.Add(PanelUi.Text(Locale.T("이 버전의 기간을 확인하지 못했습니다."),true));
  if(report.Income.MissingMonths.Length>0)content.Children.Add(PanelUi.Text(Locale.Format("미수집: {0}",string.Join(", ",report.Income.MissingMonths)),true));
  if(report.SyncState=="partial")content.Children.Add(PanelUi.Text(Locale.T("최근 조회 일부 실패: 저장된 기록 표시"),true));
  var details=new StackPanel();if(report.PeriodKnown)details.Children.Add(PanelUi.Text(report.Income.Start+" – "+report.Income.End+" (UTC+8)",true));
  if(report.EndEstimated)details.Children.Add(PanelUi.Text(Locale.T("종료일은 공개 명세서의 버전 일수 기준입니다."),true));
  details.Children.Add(PanelUi.Text(help,true));
  details.Children.Add(PanelUi.Text(Locale.T("연결된 일기는 실행 중 6시간마다 자동 보관합니다. 최근 약 3개월만 새로 조회할 수 있고, 이전에 보관한 달은 계속 열람할 수 있습니다. 최신 수입은 일기 반영까지 늦을 수 있습니다."),true));
  var source=PanelUi.Button(Locale.T("원본 명세서 ↗"));source.Click+=(s,e)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PrimogemService.BudgetUrl){UseShellExecute=true});details.Children.Add(source);
  content.Children.Add(PanelUi.Details(Locale.T("집계 기준"),details));
  var achievements=report.Achievements;var summary=new StackPanel();
  if(!report.AchievementCatalogAvailable)summary.Children.Add(PanelUi.Text(Locale.T("업적 목록 미수집"),true));
  else if(achievements.Count==0)summary.Children.Add(PanelUi.Text(Locale.T("이 버전에 등록된 업적이 없습니다."),true));
  else{
   summary.Children.Add(Large(Locale.Format("달성 확인 {0} / {1}개",achievements.Count(a=>a.State==2),achievements.Count)));
   int known=achievements.Where(a=>a.State==2&&a.Reward.HasValue).Sum(a=>a.Reward.Value);summary.Children.Add(PanelUi.Text(Locale.Format("달성 보상 {0} / 목록 보상 {1} 원석",known.ToString("N0"),achievements.Where(a=>a.Reward.HasValue).Sum(a=>a.Reward.Value).ToString("N0"))));if(achievements.Any(a=>!a.Reward.HasValue))summary.Children.Add(PanelUi.Text(Locale.T("보상량이 없는 업적은 합계에서 제외됩니다."),true));
   var rows=new StackPanel();foreach(var row in achievements.OrderBy(a=>a.State).ThenBy(a=>a.Id))rows.Children.Add(PanelUi.Text(row.Name+"\n"+row.Status+"   "+(row.Reward.HasValue?row.Reward.Value+" "+Locale.T("원석"):Locale.T("보상 미확인")),true));summary.Children.Add(PanelUi.Details(Locale.T("업적별 현황"),rows));
  }
  var open=PanelUi.Button(Locale.T("업적 열기"));open.Click+=(s,e)=>PanelNavigation.Open(window,"Achievements");summary.Children.Add(open);
  content.Children.Add(PanelUi.Details(Locale.T("버전별 업적"),PanelUi.SectionHelp(Locale.T("버전별 업적"),Locale.T("현재 수집된 로컬 업적 기록입니다. HoYoLAB 계정과 자동 대조하지 않습니다. 달성 확인은 원석 수령이나 획득 시점 확인이 아닙니다. 미확인은 미달성을 뜻하지 않습니다."),summary)));
  var days=new StackPanel();foreach(var pair in report.Income.Days)days.Children.Add(PanelUi.Text(pair.Key+"   "+pair.Value.ToString("N0")));if(days.Children.Count>0)content.Children.Add(PanelUi.Details(Locale.T("일별 수입"),days));
  if(report.Rewards.Count==0)rewardList.Children.Add(PanelUi.Text(Locale.T("등록한 보상이 없습니다."),true));
  foreach(var group in report.Rewards.GroupBy(r=>r.Currency)){int claimed=group.Sum(r=>r.Received??0),remaining=group.Where(r=>r.Received.HasValue).Sum(r=>r.Total-r.Received.Value),unknown=group.Count(r=>!r.Received.HasValue);rewardList.Children.Add(PanelUi.Text(Currency(group.Key)+"\n"+(group.All(r=>!r.Received.HasValue)?Locale.Format("수령량 미확인 {0}항목",unknown):Locale.Format("수령 기록 {0}   남음 {1}   미확인 {2}항목",claimed,remaining,unknown))));}
  foreach(var row in report.Rewards){var value=row;var button=PanelUi.Button("");button.Content=PanelUi.Text(row.Name+"\n"+(row.Received.HasValue?row.Received+" / "+row.Total:Locale.T("미확인")+" / "+row.Total)+" "+Currency(row.Currency));((TextBlock)button.Content).Margin=new Thickness(0);button.Height=double.NaN;button.Padding=new Thickness(12);button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Click+=(s,e)=>Edit(value);rewardList.Children.Add(button);}
 }
}
