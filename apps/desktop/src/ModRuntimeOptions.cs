using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

// One presentation of the runtime and engine choice, shared by its summary and settings.
internal sealed class ModRuntimeOptions {
 readonly string root;readonly Window owner;readonly Func<Func<Task>,Task> run;readonly Action<ModProgress> progress;readonly Func<bool> editable;readonly Action<string> begin,error;
 readonly Button prepare,connect,open;
 readonly TextBlock status=PanelUi.Text(Locale.T("확인 중…"),true);readonly Button apply=PanelUi.Button(Locale.T("적용")),restore=PanelUi.Button(Locale.T("원본 복원"));
 internal readonly TextBlock Summary=PanelUi.Text(Locale.T("확인 중…"),true);
 ModEngineStatus state;bool loading,ready,connected;int revision;StackPanel view;Panel engineActions,managementActions;FrameworkElement setupActions;
 internal ModRuntimeOptions(Window owner,string root,Func<Func<Task>,Task> run,Action<ModProgress> progress,Func<bool> editable,Action<string> begin,Button prepare,Button connect,Button open,Action<string> error){
  this.owner=owner;this.root=root;this.run=run;this.progress=progress;this.editable=editable;this.begin=begin;this.error=error;this.prepare=prepare;this.connect=connect;this.open=open;
  Summary.Margin=new Thickness(0);Summary.TextWrapping=TextWrapping.NoWrap;Summary.TextTrimming=TextTrimming.CharacterEllipsis;status.Margin=new Thickness(0);Refresh();
  apply.Click+=async(s,e)=>{
   if(state==null||!editable())return;
   if(!state.HasConsent&&MessageBox.Show(owner,Locale.T("XXMI 원본 소스를 직접 빌드하고 비보안 모드를 사용합니다. 필요하면 C++ 빌드 도구도 설치합니다(수 GB). 4001 해결은 보장하지 않으며 원본 복원이 가능합니다. 적용하시겠습니까?"),Locale.T("4001 대응"),MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
   await run(async()=>{begin("4001 대응 준비 중…");await Task.Run(()=>ModIntegration.SetEngine4001(root,true,progress));});
  };
  restore.Click+=async(s,e)=>{if(!editable())return;await run(async()=>{begin("원본 엔진 복원 중…");await Task.Run(()=>ModIntegration.SetEngine4001(root,false,progress));});};
 }
 internal StackPanel View {get{
  if(view!=null)return view;view=new StackPanel();setupActions=PanelUi.Actions(prepare);view.Children.Add(setupActions);
  engineActions=(Panel)PanelUi.Actions(apply);var row=PanelUi.Row(status,engineActions,responsive:true);row.Margin=new Thickness(0);view.Children.Add(PanelUi.SectionHelp(Locale.T("4001 대응"),Locale.T(ModEngine4001.Help),row));
  view.Children.Add(PanelUi.InlineDetails(Locale.T("고급 관리"),()=>{
   var body=new StackPanel();managementActions=(Panel)PanelUi.Actions(connect);body.Children.Add(managementActions);
   body.Children.Add(PanelUi.LinkRow(Locale.T("원본 소스"),"XXMI","https://github.com/SpectrumQT/XXMI-Libs-Package",ShowError));
   body.Children.Add(PanelUi.LinkRow(Locale.T("대응 방법"),"Nahida Desktop","https://desktop.nahida.live/features/mod-tools/dll-builder",ShowError));
   body.Children.Add(PanelUi.LinkRow(Locale.T("빌드 도구"),Locale.T("설치 안내"),ModEngineBuild.ToolsUrl,ShowError));
   var log=PanelUi.Button(Locale.T("빌드 로그 열기"));log.Click+=(s,e)=>{try{string runtime=ModIntegration.RuntimeRoot(root,false);if(runtime==null)throw new System.IO.FileNotFoundException(Locale.T("빌드 기록이 없습니다."));string file=System.IO.Path.Combine(runtime,"catheryne-4001-build.log");if(!System.IO.File.Exists(file))file=System.IO.Path.Combine(runtime,"catheryne-4001","build.log");if(!System.IO.File.Exists(file))throw new System.IO.FileNotFoundException(Locale.T("빌드 기록이 없습니다."));System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file){UseShellExecute=true});}catch(Exception error){ShowError(error);}};body.Children.Add(PanelUi.Actions(log));Present();return body;
  }));Present();return view;
 }}
 void ShowError(Exception failure){error(Locale.T(failure.Message));}
 void Present(){
  if(setupActions!=null)setupActions.Visibility=ready?Visibility.Collapsed:Visibility.Visible;
  if(engineActions!=null){engineActions.Children.Clear();engineActions.Children.Add(apply);if(state!=null&&state.CanRestore)engineActions.Children.Add(restore);}
  if(managementActions!=null){managementActions.Children.Clear();managementActions.Children.Add(connect);if(connected)managementActions.Children.Add(open);}
 }
 internal void Availability(){bool editableNow=editable()&&!loading;prepare.IsEnabled=connect.IsEnabled=editableNow;open.IsEnabled=editableNow&&connected;bool enabled=editableNow&&state!=null;apply.IsEnabled=enabled;restore.IsEnabled=enabled&&state.CanRestore;}
 internal async void Refresh(){
  int current=++revision;loading=true;Availability();
  try{var result=await Task.Run(()=>{string runtime=ModIntegration.RuntimeRoot(root,false);ModEngineStatus engine=null;Exception error=null;try{engine=ModEngine4001.Status(runtime);}catch(Exception e){error=e;}return new{Engine=engine,Error=error,Ready=ModRuntimePackages.Ready(runtime),Connected=runtime!=null};});if(current!=revision)return;state=result.Engine;ready=result.Ready;connected=result.Connected;if(result.Error!=null)throw result.Error;status.Text=Locale.T(state.Message);if(!string.IsNullOrEmpty(state.Version))status.Text+=" · "+state.Version;Summary.Text=ready?Locale.T("XXMI 준비됨")+" · "+Locale.T(state.Message):Locale.T("실행 환경 준비 필요");Summary.ToolTip=Summary.Text;apply.Content=Locale.T(state.Selected?"다시 적용":"적용");Present();}
  catch(Exception error){if(current==revision){state=null;status.Text=Locale.T(error.Message);Summary.Text=Locale.T("실행 환경 확인 필요");Summary.ToolTip=status.Text;Present();}}
  finally{if(current==revision){loading=false;Availability();}}
 }
}
