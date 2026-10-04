using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

// Live Windows state is queried off the UI thread, and never becomes an unlocker field.
internal sealed class WindowsHdrPanel {
 readonly CheckBox autoToggle;readonly TextBlock autoNotice;readonly WindowsAutoHdr autoHdr;readonly CheckBox toggle;readonly TextBlock notice;readonly TextBox monitor;readonly WindowsHdr hdr;readonly Action<string> status;readonly Action changed;
 readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};bool loading,busy,editable;int generation;
 internal WindowsHdrState State {get;private set;}
 internal WindowsAutoHdrState AutoState {get;private set;}
 internal WindowsHdrPanel(Window window,WindowsHdr hdr,WindowsAutoHdr autoHdr,Action<string> status,Action changed){
  this.hdr=hdr;this.autoHdr=autoHdr;autoToggle=(CheckBox)window.FindName("WindowsAutoHDR");autoNotice=(TextBlock)window.FindName("WindowsAutoHdrStatus");ToolTipService.SetShowOnDisabled(autoToggle,true);this.status=status;this.changed=changed;toggle=(CheckBox)window.FindName("WindowsHDR");notice=(TextBlock)window.FindName("WindowsHdrStatus");monitor=(TextBox)window.FindName("MonitorNum");ToolTipService.SetShowOnDisabled(toggle,true);notice.Text=Locale.T("불러오는 중…");
  RoutedEventHandler apply=async(s,e)=>{if(loading||busy||!editable||State==null)return;bool enabled=toggle.IsChecked==true;var before=State;busy=true;Availability();try{await Task.Run(()=>hdr.Set(before.Monitor,enabled,before.Identity));}catch(Exception error){status(error.Message);}finally{busy=false;}await Refresh();};toggle.Checked+=apply;toggle.Unchecked+=apply;
  RoutedEventHandler applyAuto=async(s,e)=>{if(loading||busy||!editable||AutoState==null)return;bool enabled=autoToggle.IsChecked==true;busy=true;Availability();try{await Task.Run(()=>autoHdr.Set(enabled));}catch(Exception error){status(error.Message);}finally{busy=false;}await Refresh();};autoToggle.Checked+=applyAuto;autoToggle.Unchecked+=applyAuto;
  var panel=(FrameworkElement)window.FindName("GraphicsPanel");panel.IsVisibleChanged+=async(s,e)=>{if(panel.IsVisible){timer.Start();await Refresh();}else timer.Stop();};monitor.TextChanged+=(s,e)=>{State=null;Availability();};
  window.Activated+=async(s,e)=>{if(panel.IsVisible)await Refresh();};window.Closed+=(s,e)=>timer.Stop();timer.Tick+=async(s,e)=>await Refresh();Availability();
 }
 internal void SetEditable(bool value){editable=value;Availability();}
 void Availability(){toggle.IsEnabled=editable&&!busy&&State!=null&&State.CanSet;autoToggle.IsEnabled=editable&&!busy&&AutoState!=null&&AutoState.CanSet;}
 internal async Task Refresh(){
  if(busy)return;int number;if(!int.TryParse(monitor.Text,out number)||number<1)return;int request=++generation;
  var values=await Task.Run(()=>new Tuple<WindowsHdrState,WindowsAutoHdrState>(hdr.Read(number),autoHdr.Read()));var value=values.Item1;var auto=values.Item2;int selected;if(request!=generation||!int.TryParse(monitor.Text,out selected)||selected!=number)return;
  bool same=State!=null&&State.Monitor==value.Monitor&&State.Identity==value.Identity&&State.Supported==value.Supported&&State.Enabled==value.Enabled&&State.Blocked==value.Blocked&&State.Error==value.Error;same=same&&AutoState!=null&&AutoState.Enabled==auto.Enabled&&AutoState.Supported==auto.Supported&&AutoState.Error==auto.Error;State=value;AutoState=auto;if(same){Availability();return;}loading=true;try{autoToggle.IsChecked=auto.Enabled;autoNotice.Text=auto.Error.Length>0?Locale.T("확인 실패"):!auto.Supported?Locale.T("미지원"):!auto.Enabled.HasValue?Locale.T("Windows 기본값"):"";autoNotice.ToolTip=auto.Error;autoToggle.ToolTip=auto.Error.Length>0?auto.Error:Locale.T("Windows 전체 게임의 자동 HDR 설정입니다. Windows HDR이 켜진 지원 게임에서 다음 실행부터 적용됩니다. 기본값은 켜짐·꺼짐으로 추측하지 않고 그대로 저장합니다.");toggle.IsChecked=value.Enabled;notice.Text=value.Error.Length>0?Locale.T("확인 실패"):!value.Supported?Locale.T("미지원"):value.Blocked?Locale.T("사용 불가"):"";notice.ToolTip=value.Error;toggle.ToolTip=value.Error.Length>0?value.Error:Locale.T("선택한 모니터의 Windows HDR에 즉시 적용합니다. 자동 HDR 설정은 유지됩니다.");}finally{loading=false;Availability();}changed();
 }
}
