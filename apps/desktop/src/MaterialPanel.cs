using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

internal static class MaterialPanel {
 internal sealed class ViewData {internal Dictionary<string,object> Snapshot;internal GoalState Goals;internal bool Stale;internal string Signature;}
 internal static ViewData Read(string root){Dictionary<string,object> pointer;var snapshot=new CatheryneTools(root).AccountSnapshot(out pointer);var goals=new GoalStore(root).Read();return new ViewData{Snapshot=snapshot,Goals=goals,Stale=HoyoMaterialService.PlanStale(root,snapshot,goals.Revision),Signature=CatheryneTools.Json().Serialize(pointer)+"|"+goals.Revision+"|"+Locale.LanguageCode};}
 internal static FrameworkElement View(FrameworkElement hutao,Func<ViewData> read=null){
  var rows=new StackPanel();rows.Children.Add(PanelUi.Text(Locale.T("불러오는 중…"),true));var view=PanelUi.Section(Locale.T("육성 재료"),rows,PanelUi.Details(Locale.T("외부 육성 계획"),hutao));view.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,System.Windows.Media.Brushes.White);
  bool reading=false;int generation=0;string signature=null;var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
  Func<System.Threading.Tasks.Task> refresh=null;refresh=async()=>{if(reading||!view.IsVisible)return;reading=true;int request=generation;try{var data=await System.Threading.Tasks.Task.Run(read??(()=>Read(Setup.DataFolder)));if(request!=generation||!view.IsVisible||signature==data.Signature)return;signature=data.Signature;Render(rows,data);}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.DataRefreshFailure,error);if(request==generation&&view.IsVisible){signature=null;rows.Children.Clear();rows.Children.Add(PanelUi.Text(Locale.T("육성 재료를 불러오지 못했습니다."),true));}}finally{reading=false;if(request!=generation&&view.IsVisible){var pending=refresh();}}};
  timer.Tick+=async(s,e)=>await refresh();view.IsVisibleChanged+=async(s,e)=>{++generation;if(view.IsVisible){await refresh();if(view.IsVisible)timer.Start();}else timer.Stop();};view.Unloaded+=(s,e)=>{++generation;timer.Stop();};return view;
 }
 internal static void Render(StackPanel rows,ViewData data){
  rows.Children.Clear();var snapshot=data.Snapshot;var plan=MaterialInventory.Map(MaterialInventory.Map(snapshot,"catheryne"),"materialPlan");var state=data.Goals;
  object raw;var materials=MaterialInventory.Rows(snapshot).ToDictionary(x=>CodexChat.S(x,"itemId"),x=>x);
  if(data.Stale)rows.Children.Add(PanelUi.Text(Locale.T("목표가 바뀌었습니다. 내 계정에서 최신화해 주세요."),true));
  else if(plan.TryGetValue("requirements",out raw)&&CodexChat.Items(raw).Any())foreach(var item in CodexChat.Items(raw)){
   Dictionary<string,object> stock;materials.TryGetValue(CodexChat.S(item,"id"),out stock);string owned=stock==null?Locale.T("미확인"):MaterialInventory.CountText(stock);long needed=MaterialInventory.Number(item["num"],"required");object lack;string shortage=item.TryGetValue("lack_num",out lack)?Locale.Format("부족 {0:N0}",Math.Max(0,MaterialInventory.Number(lack,"shortage",true))):Locale.T("부족량 미확인");rows.Children.Add(PanelUi.Row(GameCatalog.MaterialName(CodexChat.S(item,"id"),CodexChat.S(item,"name")),PanelUi.Text(owned+" / "+needed.ToString("N0",Locale.Culture)+" · "+shortage,true)));
  }else rows.Children.Add(PanelUi.Text(Locale.T("계산된 육성 재료가 없습니다."),true));
  if(plan.TryGetValue("unsupported",out raw)&&CodexChat.Items(raw).Any())rows.Children.Add(PanelUi.Details(Locale.T("계산하지 못한 목표"),PanelUi.Text(string.Join("\n",CodexChat.Items(raw).Select(x=>{var goal=state.Goals.FirstOrDefault(g=>g.Id==CodexChat.S(x,"goal_id"));return (goal==null?"":goal.Title)+" · "+Reason(CodexChat.S(x,"reason"));})),true)));
 }
 internal static FrameworkElement Detail(Dictionary<string,object> item,string name){
  var body=new StackPanel();var image=new Image{Height=128,Stretch=System.Windows.Media.Stretch.Uniform,Margin=new Thickness(0,0,0,16)};AccountIdentity.Portrait(image,item,256);body.Children.Add(image);body.Children.Add(PanelUi.Text(name));body.Children.Add(PanelUi.Text(Locale.T("보유 수량"),true));
  var quantity=new TextBlock{Text=MaterialInventory.CountText(item),FontSize=32,FontWeight=FontWeights.SemiBold,Foreground=System.Windows.Media.Brushes.White,Margin=new Thickness(0,12,0,16)};body.Children.Add(quantity);
  body.Children.Add(PanelUi.Row(Locale.T(item.ContainsKey("quantity")&&item["quantity"]!=null?"수량 확인":"정보 확인"),PanelUi.Text(DateTimeOffset.Parse(CodexChat.S(item,"observedAt")).ToLocalTime().ToString("g",Locale.Culture),true)));
  if(!item.ContainsKey("quantity")||item["quantity"]==null)body.Children.Add(PanelUi.Text(Locale.T("최신화 → 사용자 설정에서 재료를 스캔하거나 수량이 포함된 결과 파일을 불러오세요."),true));
  return body;
 }
 static string Reason(string code){return Locale.T(code=="character_not_synced"?"캐릭터 동기화 필요":code=="talent_target_requires_skill_mapping"?"특성 기준 확인 필요":code=="target_missing"?"육성 목표 미지정":"지원 범위를 벗어난 목표 레벨");}
}
