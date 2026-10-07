using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal sealed class GameEnvironmentPanel : IDisposable {
 readonly string root;readonly ComboBox mode=new ComboBox();readonly TextBlock status=PanelUi.Text("",true);readonly StackPanel content=new StackPanel(),connection=new StackPanel();
 readonly Button connect=PanelUi.Button(Locale.T("연결")),preview=PanelUi.Button(Locale.T("게임 화면")),manual=PanelUi.Button(Locale.T("직접 조작")),stop=PanelUi.Button(Locale.T("실행 환경 종료"));
 readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};bool busy,refreshing,disposed,binding;string actionError;
 internal GameEnvironmentPanel(Window owner,StackPanel host,string root){
  this.root=root;mode.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");mode.Items.Add(Locale.T("일반 실행"));mode.Items.Add(Locale.T("분리 실행"));mode.SelectedIndex=GameEnvironment.Selected(root)?1:0;
  content.Children.Add(PanelUi.Row(Locale.T("자동화 실행"),mode,responsive:true));content.Children.Add(status);connection.Children.Add(PanelUi.Actions(connect,preview,manual,stop));content.Children.Add(connection);var advanced=PanelUi.Details(Locale.T("고급 설정"),content);advanced.IsExpanded=GameEnvironment.Selected(root);host.Children.Add(advanced);
  mode.SelectionChanged+=(s,e)=>{if(binding)return;bool selected=mode.SelectedIndex==1;try{if(ProcessGuard.Busy())throw new InvalidOperationException(Locale.T("게임 실행이 끝난 뒤 변경해 주세요."));AppPreferences.Set("gameExecution",selected?"isolated":"desktop",root);actionError=null;Refresh();}catch(Exception error){actionError=error.Message;binding=true;mode.SelectedIndex=GameEnvironment.Selected(root)?1:0;binding=false;Refresh();}};
  connect.Click+=async(s,e)=>await Run(()=>GameEnvironment.Invoke(root,"status",new Dictionary<string,object>(),true,interactiveAuthentication:true));
  preview.Click+=async(s,e)=>await Run(()=>{GameEnvironment.ShowView(root,false);return null;});
  manual.Click+=async(s,e)=>await Run(()=>{GameEnvironment.ShowView(root,true);return null;});
  stop.Click+=async(s,e)=>{if(MessageBox.Show(owner,Locale.T("게임 실행 환경과 그 안의 작업을 종료할까요?"),Locale.T("실행 환경 종료"),MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes)await CloseEnvironment();};
  content.IsVisibleChanged+=(s,e)=>{if(content.IsVisible){Refresh();timer.Start();}else timer.Stop();};timer.Tick+=(s,e)=>Refresh();owner.Closed+=(s,e)=>Dispose();Refresh();
 }
 async Task CloseEnvironment(){stop.IsEnabled=false;try{await Task.Run(()=>GameEnvironment.Stop(root));actionError=null;}catch(Exception error){actionError=error.GetBaseException().Message;status.Text=actionError;}finally{Refresh();}}
 async Task Run(Func<object> action){if(busy||disposed)return;busy=true;actionError=null;status.Text=Locale.T("연결 중");Availability("starting");try{await Task.Run(action);}catch(OperationCanceledException){actionError=null;}catch(Exception error){actionError=error.GetBaseException().Message;status.Text=actionError;}finally{busy=false;Refresh();}}
 async void Refresh(){if(disposed)return;bool selected=mode.SelectedIndex==1;connection.Visibility=selected?Visibility.Visible:Visibility.Collapsed;status.Visibility=selected||actionError!=null?Visibility.Visible:Visibility.Collapsed;if(!selected){status.Text=actionError??"";Availability("off");return;}if(refreshing)return;refreshing=true;try{var state=await Task.Run(()=>GameEnvironment.Status(root));if(disposed)return;string value=CodexChat.S(state,"state"),failure=CodexChat.S(state,"failure_kind");status.Text=Locale.T(value=="ready"?"실행 중":value=="starting"?(GameEnvironment.WaitingForSignIn(state)?"로그인 대기 중":"연결 중"):value=="failed"?(failure=="authentication"?"인증 필요":failure=="worker"?"실행기 복구 필요":"연결 오류"):"꺼짐");if(actionError!=null)status.Text=actionError;else if((value=="failed"||value=="starting")&&!string.IsNullOrWhiteSpace(CodexChat.S(state,"error")))status.Text+="\n"+CodexChat.S(state,"error");Availability(value);}catch(Exception error){if(!disposed)status.Text=error.Message;}finally{refreshing=false;}}
 void Availability(string state){bool selected=mode.SelectedIndex==1;connect.IsEnabled=selected&&!busy&&state!="starting"&&state!="ready";preview.IsEnabled=manual.IsEnabled=selected&&!busy&&state=="ready";stop.IsEnabled=selected&&(state=="ready"||state=="starting"||state=="failed");mode.IsEnabled=!busy;}
 public void Dispose(){disposed=true;timer.Stop();}
}
