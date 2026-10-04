using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal sealed class RedemptionPanel : IDisposable {
 internal readonly FrameworkElement View;
 readonly Window owner;
 readonly RedemptionService service=new RedemptionService(Setup.DataFolder);
 readonly TextBlock status=PanelUi.Text("",true);
 readonly StackPanel rows=new StackPanel();
 readonly Button redeem=PanelUi.Button(Locale.T("미등록 코드 모두 등록"));
 readonly CheckBox automatic=new CheckBox{Content=Locale.T("새 리딤코드 자동 등록")};
 readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};
 readonly CancellationTokenSource cancellation=new CancellationTokenSource();
 bool busy,binding,disposed;

 internal RedemptionPanel(Window owner){
  this.owner=owner;
  redeem.Click+=async(s,e)=>{if(await AccountConnections.For(owner).Ensure("redemption"))await Run(true);};
  automatic.Checked+=async(s,e)=>{if(!binding){if(await AccountConnections.For(owner).Ensure("redemption"))AppPreferences.Set("automaticRedeem",true);else{binding=true;automatic.IsChecked=false;binding=false;}}};automatic.Unchecked+=(s,e)=>{if(!binding)AppPreferences.Set("automaticRedeem",false);};
  View=PanelUi.SectionHelp(Locale.T("리딤코드"),Locale.T("공개 코드 목록을 30분마다 확인합니다. 받을 수 있는지는 공식 등록 결과로 확인하며 이미 받은 코드는 건너뜁니다. 앱이 실행 중일 때 동작하며, 창을 닫은 뒤에도 실행하려면 아래 백그라운드 실행을 켜세요. 로그인 만료와 추가 인증은 공식 페이지에서 다시 확인해야 합니다. 코드 제공: hoyo-codes (커뮤니티)."),status,PanelUi.Actions(redeem),automatic,PanelUi.Details(Locale.T("코드와 등록 결과"),rows));
  AccountConnections.For(owner).Changed+=Bind;Bind();View.IsVisibleChanged+=async(s,e)=>{if(View.IsVisible){Bind();await Run(false,true);}};
  timer.Tick+=async(s,e)=>{if(busy||disposed)return;if(View.IsVisible)Bind();if(AppPreferences.Flag("automaticRedeem"))await Run(true,true);else if(View.IsVisible)await Run(false,true);};timer.Start();
 }
 void Bind(){if(disposed)return;binding=true;try{automatic.IsChecked=AppPreferences.Flag("automaticRedeem");var saved=service.Saved();Draw(saved);redeem.IsEnabled=!busy;automatic.IsEnabled=true;}catch{status.Text=Locale.T("저장된 현황을 읽지 못했습니다.");}finally{binding=false;}}
 void Draw(RedeemReport report){
  rows.Children.Clear();foreach(var code in report.Codes){var title=PanelUi.Text(code.Code);title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,0,4);rows.Children.Add(title);if(!string.IsNullOrEmpty(code.Rewards)){var rewards=PanelUi.Text(code.Rewards,true);rewards.Margin=new Thickness(0,0,0,4);rows.Children.Add(rewards);}var result=PanelUi.Text(RedemptionService.Label(code.State),true);if(code.CheckedAt!=null)result.ToolTip=DateTime.Parse(code.CheckedAt).ToLocalTime().ToString("g",Locale.Culture)+(code.Retcode.HasValue?" ("+code.Retcode+")":"");rows.Children.Add(result);}
  int claimed=report.Codes.Count(c=>c.State=="redeemed"||c.State=="already");status.Text=report.CheckedAt==null?Locale.T("아직 확인하지 않았습니다."):Locale.Format("공개 코드 {0}개 / 등록 확인 {1}개",report.Codes.Count,claimed)+"\n"+DateTime.Parse(report.CheckedAt).ToLocalTime().ToString("g",Locale.Culture);
  if(new[]{"login","blocked","cooldown","network"}.Contains(report.Status))status.Text+="\n"+RedemptionService.Label(report.Status);
  if(report.Status=="busy")status.Text+="\n"+Locale.T("리딤코드 작업이 이미 실행 중입니다.");
  if(report.Status=="stopped")status.Text+="\n"+Locale.T("리딤코드 등록을 중단했습니다.");
 }
 async Task Run(bool register,bool auto=false){
  if(busy||disposed)return;busy=true;redeem.IsEnabled=false;
  try{if(auto&&await Task.Run(()=>register?!service.Due():ObservationRefresh.Fresh(Setup.DataFolder,"public","redeem-feed",TimeSpan.FromMinutes(30))))return;if(!auto)status.Text=Locale.T("리딤코드 확인 중…");var report=await Task.Run(()=>service.Run(register,register&&auto,cancellation.Token));if(!disposed)Draw(report);}
  catch(OperationCanceledException){}
  catch{if(!disposed)status.Text=Locale.T("조회 또는 등록을 완료하지 못했습니다. 연결 상태와 리딤 계정을 확인해 주세요.");}
  finally{busy=false;if(!disposed){redeem.IsEnabled=true;}}
 }
 public void Dispose(){AccountConnections.For(owner).Changed-=Bind;disposed=true;cancellation.Cancel();timer.Stop();}
}
