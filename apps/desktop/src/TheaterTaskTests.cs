using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class TheaterTaskTests {
 static Dictionary<string,object> D(string json){return CatheryneTools.Json().Deserialize<Dictionary<string,object>>(json);}
 static void Check(bool condition,string message){if(!condition)throw new Exception("Theater task: "+message);}
 internal static Dictionary<string,object> Fixture(string phase="party",bool unknown=false){
  var state=D("{\"totalActs\":10,\"completedActs\":4,\"act\":5,\"participantsConfirmed\":true,\"partySize\":4,\"flowers\":75,\"phase\":\""+phase+"\",\"target\":\"적의 보호막에 대응하고 후반 출전 기회를 남기기\",\"members\":[{\"identity\":\"Fischl\",\"name\":\"피슬\",\"available\":true,\"vigor\":2},{\"identity\":\"Nahida\",\"name\":\"나히다\",\"available\":true,\"vigor\":1},{\"identity\":\"Barbara\",\"name\":\"바바라\",\"available\":true,\"vigor\":2},{\"identity\":\"Xingqiu\",\"name\":\"행추\",\"available\":true,\"vigor\":1}],\"cards\":[{\"id\":\"card\",\"name\":\"시험용 보유 카드\",\"description\":\"효과는 관측된 자료를 표시합니다.\"}],\"reactions\":[{\"id\":\"reaction\",\"name\":\"시험용 반응 강화\",\"level\":2}]}");
  if(phase=="choice")state["choices"]=D("{\"items\":[{\"id\":\"left\",\"kind\":\"card\",\"position\":\"왼쪽\",\"cost\":40,\"text\":\"원소 반응 강화\",\"description\":\"시험용 선택지\"},{\"id\":\"middle\",\"kind\":\"card\",\"position\":\"가운데\",\"text\":\"캐릭터 영입 기회\"},{\"id\":\"right\",\"kind\":\"card\",\"position\":\"오른쪽\",\"text\":\"출전 기회 회복\"}]}")["items"];
  if(unknown)state["choices"]=D("{\"items\":[{\"id\":\"unreadable\",\"kind\":\"character\",\"position\":\"왼쪽 후보\",\"text\":\"이름이 잘 보이지 않는 후보\"},{\"id\":\"known\",\"kind\":\"character\",\"identity\":\"Fischl\",\"position\":\"오른쪽 후보\",\"text\":\"피슬\"}]}")["items"];
  var journal=new Dictionary<string,object>{{"id",Guid.NewGuid().ToString("N")},{"thread","fixture-thread"},{"goal","10막 완주"},{"createdAt",DateTime.UtcNow.AddMinutes(-18).ToString("o")},{"events",new object[]{new{kind="observe",data=state,at=DateTime.UtcNow.ToString("o")}}}};
  foreach(var member in CodexChat.Items(state["members"])){member["source"]="owned";member["level"]=90;member["constellation"]=member["identity"].ToString()=="Fischl"?6:0;}
  var plan=phase=="choice"?D("{\"choice_id\":\"left\",\"reason\":\"현재 파티와 반응 강화에 맞는 선택입니다.\",\"futurePlan\":\"후반 전투를 위해 마지막 출전 기회는 보존합니다.\",\"alternatives\":\"출전 기회가 부족해지면 회복 선택을 우선합니다.\"}"):D("{\"team\":[\"Fischl\",\"Nahida\",\"Barbara\",\"Xingqiu\"],\"reason\":\"원소 부착과 회복을 확보하고 남은 출전 기회를 분산합니다.\",\"rotation\":[{\"identity\":\"Nahida\",\"action\":\"원소전투 스킬로 적에게 표식을 남기기\"},{\"identity\":\"Xingqiu\",\"action\":\"원소폭발 → 원소전투 스킬\"},{\"identity\":\"Fischl\",\"action\":\"원소전투 스킬로 오즈 소환\"},{\"identity\":\"Barbara\",\"action\":\"필요할 때 회복하고 일반 공격으로 물 부착\"}],\"futurePlan\":\"나히다와 행추의 마지막 출전은 다음 필수 전투와 비교합니다.\",\"alternatives\":\"원소폭발이 준비되지 않았다면 먼저 에너지를 확보합니다.\"}");
  plan["assessment"]=TheaterPlanningTests.Assessment();plan["battlePlan"]=TheaterPlanningTests.Remaining(5,10);
  if(!unknown)journal["events"]=((object[])journal["events"]).Concat(new object[]{new{kind="recommend",data=plan,at=DateTime.UtcNow.ToString("o")}}).ToArray();
  if(phase=="completed")journal["events"]=((object[])journal["events"]).Concat(new object[]{new{kind="observe",data=(object)new{completedActs=10},at=DateTime.UtcNow.ToString("o")},new{kind="finish",data=(object)new{},at=DateTime.UtcNow.ToString("o")}}).ToArray();
  return D(CatheryneTools.Json().Serialize(journal));
 }
 internal static void Run(string root){
  var journal=Fixture();var task=TheaterTask.Project(journal);Check(task.State=="waiting"&&task.Ended==null&&AiTaskFeed.Open(task),"between decisions is waiting, not completed or blocked");
  Check(TaskProgress.Fraction(task,DateTime.UtcNow)==.4&&TaskProgress.Fraction(task,DateTime.UtcNow.AddDays(10))==.4,"act progress must not rise with time");
  Check(TheaterTaskView.Availability(D("{\"available\":false,\"vigor\":0}")).Contains("0")&&TheaterTaskView.Availability(D("{}")).Contains(Locale.T("출전 여부 미확인")),"unavailable and unknown cast keep distinct meanings and known zero uses");
  Check(!TheaterService.IdentifiedChoice(D("{\"participantsConfirmed\":true,\"members\":{\"game:99999999\":{\"identity\":\"game:99999999\"}}}"),D("{\"identity\":\"game:99999999\",\"text\":\"game:99999999\"}")),"an unresolved internal identity is not a visible character name");
  var key=AiTaskRequest.KeyFor(task);task.TurnId="next";Check(AiTaskRequest.KeyFor(task)==key,"one run keeps the same box across turns");
  var store=new AiTaskStore(root);store.Save(task);store.Recover();Check(store.Find(task.Id).State=="waiting","waiting does not own a dead worker");
  Check(TheaterKnowledge.PeriodStart(new DateTime(2026,9,30,19,59,59,DateTimeKind.Utc),"os_asia")==new DateTime(2026,8,31,20,0,0,DateTimeKind.Utc),"September begins at Korean September 1 05:00");
  foreach(var element in new[]{"Anemo","Hydro","Pyro","Electro","Cryo","Dendro","Geo"})Check(GameCatalog.ElementIcon(element) is Viewbox,"original vector icon must be bundled for "+element);
 }
 static IEnumerable<DependencyObject> Descendants(DependencyObject owner){yield return owner;for(int i=0;i<VisualTreeHelper.GetChildrenCount(owner);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(owner,i)))yield return child;}
 internal static void Render(Window styleOwner){
  foreach(string variant in new[]{"choice","party","unknown","complete","narrow","evidence"}){
   var task=TheaterTask.Project(Fixture(variant=="choice"||variant=="unknown"?"choice":variant=="complete"?"completed":"party",variant=="unknown"));
   var card=new AiTaskCard(task,()=>{},stop:()=>{});int judgments=0;card.SetJudge(t=>judgments++,()=>true,(t,choice,identity)=>{});
   var surface=new Border{Background=new SolidColorBrush(Color.FromRgb(21,27,34)),Padding=new Thickness(24),Child=card,Resources=styleOwner.Resources};surface.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,Brushes.White);surface.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty,styleOwner.FontFamily);
   double width=variant=="narrow"?440:840;surface.Measure(new Size(width,double.PositiveInfinity));surface.Arrange(new Rect(0,0,width,surface.DesiredSize.Height));surface.UpdateLayout();
   var frame=new DispatcherFrame();var wait=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1700)};wait.Tick+=(s,e)=>{wait.Stop();frame.Continue=false;};wait.Start();Dispatcher.PushFrame(frame);
   surface.Measure(new Size(width,double.PositiveInfinity));surface.Arrange(new Rect(0,0,width,surface.DesiredSize.Height));surface.UpdateLayout();
   var button=Descendants(surface).OfType<Button>().FirstOrDefault(b=>Convert.ToString(b.Content)==Locale.T("판단"));Check(variant=="complete"?button==null||button.Visibility==Visibility.Collapsed:button!=null&&button.IsEnabled,"decision action availability");
   if(variant=="choice"){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(judgments==1,"one click requests one judgment");card.Hold(true);Check(!button.IsEnabled,"busy decision is disabled");card.Hold(false);}
   if(variant=="evidence"){var detail=Descendants(surface).OfType<Expander>().FirstOrDefault(e=>Convert.ToString(e.Header).Contains(Locale.T("남은 계획")));if(detail!=null)detail.IsExpanded=true;var expansion=new DispatcherFrame();var settled=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};settled.Tick+=(sender,args)=>{settled.Stop();expansion.Continue=false;};settled.Start();Dispatcher.PushFrame(expansion);surface.Measure(new Size(width,double.PositiveInfinity));surface.Arrange(new Rect(0,0,width,surface.DesiredSize.Height));surface.UpdateLayout();}
   var dots=Descendants(surface).OfType<System.Windows.Shapes.Ellipse>().Where(e=>e.ToolTip is string&&((string)e.ToolTip).Contains(Locale.IsEnglish?"Act":"막")).ToArray();if(variant!="complete")Check(dots.Skip(4).All(e=>((SolidColorBrush)e.Stroke).Color!=Color.FromRgb(81,113,255)),"unreached acts never have the blue completed stroke");
   Check(Descendants(surface).OfType<TextBlock>().All(t=>!t.Text.StartsWith("theater:")),"internal IDs never shown");
   var bitmap=new RenderTargetBitmap((int)width,(int)Math.Ceiling(surface.DesiredSize.Height),96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(Path.GetTempPath(),"catheryne-theater-"+variant+".png")))png.Save(file);
  }
 }
}
