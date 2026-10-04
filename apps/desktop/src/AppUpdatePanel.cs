using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal sealed class AppUpdatePanel : IDisposable {
 readonly AppUpdateService service;
 readonly string root;
 readonly Func<bool> canRestart;
 readonly Action close;
 readonly Button action=PanelUi.Button(Locale.T("업데이트 확인"));
 readonly TextBlock version=PanelUi.Text("Catheryne",true),status=PanelUi.Text("",true);
 readonly CheckBox automatic=new CheckBox{Content=Locale.T("앱 자동 업데이트"),ToolTip=Locale.T("새 버전을 다운로드하고, 작업이 없는 다음 실행 시 설치합니다.")};
 readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromHours(1)};
 AppUpdateState state;bool busy,disposed,binding=true;
 internal AppUpdatePanel(Panel host,string app,string data,Func<bool> canRestart,Action close){
  root=data;service=new AppUpdateService(app,data);this.canRestart=canRestart;this.close=close;
  var heading=PanelUi.Text(Locale.T("앱 업데이트"));heading.SetResourceReference(FrameworkElement.StyleProperty,"SectionHeading");host.Children.Add(heading);
  var row=new DockPanel();action.HorizontalAlignment=HorizontalAlignment.Right;action.Margin=new Thickness(16,0,0,0);DockPanel.SetDock(action,Dock.Right);row.Children.Add(action);version.VerticalAlignment=VerticalAlignment.Center;version.FontWeight=FontWeights.Normal;row.Children.Add(version);host.Children.Add(row);
  status.Margin=new Thickness(0,12,0,0);status.Visibility=Visibility.Collapsed;host.Children.Add(status);automatic.Margin=new Thickness(0,12,0,0);automatic.IsEnabled=false;host.Children.Add(automatic);
  action.Click+=async(s,e)=>await Act();
  automatic.Checked+=async(s,e)=>{e.Handled=true;await Configure(true);};automatic.Unchecked+=async(s,e)=>{e.Handled=true;await Configure(false);};
  timer.Tick+=async(s,e)=>await Refresh(false);
 }
 internal async Task Start(){
  try{bool enabled=await Task.Run(()=>{object value;return AppPreferences.Read(root).TryGetValue("appAutoUpdate",out value)&&Equals(value,true);});if(disposed)return;automatic.IsChecked=enabled;automatic.IsEnabled=true;binding=false;}catch{if(!disposed){automatic.IsEnabled=false;binding=false;}}
  if(disposed)return;timer.Start();await Refresh(false);
 }
 async Task Configure(bool enabled){
  if(binding||disposed)return;automatic.IsEnabled=false;
  try{await Task.Run(()=>AppPreferences.Set("appAutoUpdate",enabled,root));}
  catch{if(!disposed){binding=true;automatic.IsChecked=!enabled;binding=false;Show(Locale.T("설정을 저장하지 못했습니다. 다시 시도해 주세요."));}}
  finally{if(!disposed)automatic.IsEnabled=true;}
  if(enabled)await Refresh(false);
 }
 internal async Task Refresh(bool force){
  if(busy||disposed)return;busy=true;action.IsEnabled=false;
  try{
   var result=await Task.Run(()=>new{State=service.Check(force),Version=service.CurrentVersion});if(disposed)return;state=result.State;version.Text="Catheryne "+result.Version;
   if(automatic.IsChecked==true&&state.Available&&!state.Prepared&&!state.Repair){Show(Locale.T("업데이트 다운로드 중…"));state=await Task.Run(()=>service.Download(state.Release,Progress));}
   if(disposed)return;Render();
  }catch{if(!disposed)Show(Locale.T("업데이트를 확인하지 못했습니다. 다시 시도해 주세요."));}
  finally{busy=false;if(!disposed)action.IsEnabled=true;}
 }
 void Progress(double value){action.Dispatcher.BeginInvoke(new Action(()=>{if(!disposed)Show(Locale.Format("업데이트 다운로드 중… {0}%",(int)value));}));}
 void Show(string text){status.Text=text;status.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;}
 void Render(){
  action.Content=Locale.T(state.Prepared?"업데이트 후 다시 시작":state.Available?"업데이트 설치":"업데이트 확인");
  if(state.Repair)Show(Locale.T("업데이트를 완료하지 못했습니다. 다시 시도해 주세요."));
  else if(state.Prepared)Show(Locale.Format("{0} 다운로드 완료",state.Release.Version));
  else if(state.Available)Show(Locale.Format("{0} 업데이트 가능",state.Release.Version));
  else if(state.State=="current")Show(Locale.T("최신 버전입니다."));
  else if(state.State=="unconfigured"||state.State=="unpublished")Show(Locale.T("아직 공개된 앱 업데이트가 없습니다."));
  else Show(Locale.T("업데이트를 확인하지 못했습니다. 다시 시도해 주세요."));
 }
 internal async Task Act(){
  if(busy||disposed)return;
  if(state==null||state.Release==null||!state.Available&&!state.Prepared){await Refresh(true);return;}
  busy=true;action.IsEnabled=false;
  try{
   if(!state.Prepared){Show(Locale.T("업데이트 다운로드 중…"));state=await Task.Run(()=>service.Download(state.Release,Progress));if(disposed)return;Render();}
   if(!canRestart()){Show(Locale.T("대화와 실행 중인 작업을 마친 뒤 업데이트해 주세요."));return;}
   string helper=await Task.Run(()=>service.PrepareHandoff());if(disposed)return;
   if(helper==null){Show(Locale.T("게임과 실행 중인 작업을 종료한 뒤 업데이트해 주세요."));return;}
   // Recheck the composer after background verification, before starting the helper.
   if(!canRestart()){Show(Locale.T("대화와 실행 중인 작업을 마친 뒤 업데이트해 주세요."));return;}
   service.StartHandoff(helper);close();
  }catch{if(!disposed)Show(Locale.T("업데이트를 완료하지 못했습니다. 다시 시도해 주세요."));}
  finally{busy=false;if(!disposed)action.IsEnabled=true;}
 }
 public void Dispose(){disposed=true;timer.Stop();}
}
