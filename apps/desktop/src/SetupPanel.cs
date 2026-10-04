using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

// First run and later installation changes are states of the existing launcher window.
internal sealed class SetupPanel {
 readonly Window window;
 readonly Action<Installation> completed;
 readonly Action canceled;
 Installation existing;
 string selectedEngine="";
 readonly Dictionary<ComponentSpec,CheckBox> selections=new Dictionary<ComponentSpec,CheckBox>();
 readonly Dictionary<ComponentSpec,TextBlock> states=new Dictionary<ComponentSpec,TextBlock>();
 readonly CheckBox unlocker=new CheckBox {Content=Locale.T("프레임 제한 해제"),IsChecked=true};
 bool basePrepared;

 int generation;
 internal bool Preparing {get;private set;}
 internal bool BlocksNavigation {get{return IsOpen&&(existing==null||Preparing);}}
 internal bool IsOpen {get{return Get<Border>("SetupPanel").Visibility==Visibility.Visible;}}
 T Get<T>(string name) where T:class{return window.FindName(name) as T;}
 internal SetupPanel(Window owner,Action<Installation> onCompleted,Action onCanceled) {
  window=owner;completed=onCompleted;canceled=onCanceled;
  var host=Get<Border>("SetupPanel");host.IsVisibleChanged+=(s,e)=>{if(host.Visibility!=Visibility.Visible)++generation;};var old=(Grid)host.Child;var title=Get<TextBlock>("SetupTitle");var fields=Get<StackPanel>("SetupFields");var scroll=(ScrollViewer)fields.Parent;var footer=(StackPanel)old.Children[2];
  scroll.Content=null;old.Children.Remove(title);old.Children.Remove(footer);host.Child=null;title.Margin=new Thickness(0);fields.Margin=new Thickness(0);
  fields.Children.Add(footer);host.Child=PanelUi.Shell(title,fields,new StackPanel(),()=>{if(!BlocksNavigation)canceled();});
  foreach(var spec in Components.Catalog()){
   var selected=new CheckBox {Content=Locale.T(spec.Title),IsChecked=spec.Id!="bettergi"&&spec.Id!="hutao"};var state=PanelUi.Text("",true);selections[spec]=selected;states[spec]=state;
   selected.Content=Locale.T("함께 준비");
   var prepare=PanelUi.Button(Locale.T("준비 및 업데이트"));prepare.Click+=async(s,e)=>await PrepareOne(spec);
   var website=PanelUi.Button(Locale.T("원본 페이지"));website.Click+=(s,e)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(spec.Website){UseShellExecute=true});
   var extra=new StackPanel();extra.Children.Add(prepare);extra.Children.Add(PanelUi.Details(Locale.T("구성요소 정보"),PanelUi.Text(spec.Description+"\n"+spec.Version,true),website));
   if(ExternalTools.Catalog.Any(x=>x.Id==spec.Id)){
    var original=PanelUi.Button(Locale.T("원본 앱 열기"));original.Click+=async(s,e)=>{try{await Task.Run(()=>new ExternalTools(Setup.DataFolder).Open(spec.Id));}catch(Exception error){state.Text=error.Message;}};
    var connect=PanelUi.Button(Locale.T("기존 설치 연결"));connect.Click+=(s,e)=>{var info=ExternalTools.Spec(spec.Id);var picker=new OpenFileDialog{Filter=info.Title+"|"+info.Executable};if(picker.ShowDialog(window)==true){new ExternalTools(Setup.DataFolder).Connect(spec.Id,picker.FileName);RefreshState(spec);}};
    extra.Children.Add(PanelUi.Details(Locale.T("고급 관리"),PanelUi.Actions(connect,original)));
   }
   if(spec.Id!="hoyolab"&&spec.Id!="hutao"){var remove=PanelUi.Button(Locale.T("보관 후 제거"));remove.Margin=new Thickness(0,10,0,0);remove.Click+=async(s,e)=>{if(Preparing)return;if(MessageBox.Show(window,Locale.T("활성 설치를 해제하고 구성요소 폴더를 보관함으로 옮깁니다. 스캔 결과와 기존 파일은 보존되며 디스크 공간은 줄어들지 않습니다. 계속하시겠습니까?"),Locale.T("구성요소 제거"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;SetPreparing(true);try{await Task.Run(()=>Components.Remove(spec));selections[spec].IsChecked=false;RefreshState(spec);Get<TextBlock>("SetupStatus").Text=Locale.T("보관 후 제거했습니다. 다시 준비하면 재설치됩니다.");}catch(Exception error){Get<TextBlock>("SetupStatus").Text=error.Message;}finally{SetPreparing(false);}};extra.Children.Add(remove);}
   Get<StackPanel>("SetupComponents").Children.Add(PanelUi.SectionHelp(Locale.T(spec.Title),Locale.T(spec.Description),state,selected,extra));
  }
  var automatic=new CheckBox {Content=new TextBlock {Text=Locale.T("검증된 호환 버전 자동 준비"),TextWrapping=TextWrapping.Wrap},IsChecked=AppPreferences.Flag("componentsAutoUpdate",true)};automatic.Checked+=(s,e)=>AppPreferences.Set("componentsAutoUpdate",true);automatic.Unchecked+=(s,e)=>AppPreferences.Set("componentsAutoUpdate",false);Get<StackPanel>("SetupComponents").Children.Add(PanelUi.SectionHelp(Locale.T("업데이트 방식"),Locale.T("호환 버전을 사용하며 이전 기록은 보존합니다."),automatic));
  var latest=new CheckBox {Content=Locale.T("스캐너 최신 정식 버전 사용 (미검증)"),IsChecked=AppPreferences.Read().ContainsKey("scannerApprovedRelease")&&AppPreferences.Read()["scannerApprovedRelease"]!=null};bool changing=false;
  var selectedVersion=PanelUi.Text(Locale.T("선택 버전: ")+Components.ScannerVersion,true);
  latest.Checked+=async(s,e)=>{if(changing)return;latest.IsEnabled=false;try{var release=await Task.Run(()=>Components.LatestScanner());string version=Convert.ToString(release["scannerVersion"]);var verified=StoryClient.Read(Path.Combine(Components.Root,"components.json"));if(version==Convert.ToString(verified["scannerVersion"])&&Convert.ToString(release["scannerSha256"])==Convert.ToString(verified["scannerSha256"])){Get<TextBlock>("SetupStatus").Text=Locale.T("검증 버전이 최신 정식 버전입니다.");changing=true;latest.IsChecked=false;changing=false;AppPreferences.Set("scannerApprovedRelease",null);return;}if(MessageBox.Show(window,Locale.T("스캐너 ")+version+Locale.T(" 버전은 이 캣서린 배포본과의 호환성을 확인하지 않았습니다. 진행 오류가 발생할 수 있습니다. 이 버전만 사용하시겠습니까? 선택을 해제하면 검증 버전으로 돌아갑니다."),Locale.T("미검증 버전 선택"),MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes){AppPreferences.Set("scannerApprovedRelease",release);selectedVersion.Text=Locale.T("선택 버전: ")+version;foreach(var spec in selections.Keys)if(spec.Id=="scanner"){selections[spec].IsChecked=true;states[spec].Text=Locale.T("선택 버전 준비 필요");}}else{changing=true;latest.IsChecked=false;changing=false;}}catch(Exception error){Get<TextBlock>("SetupStatus").Text=error.Message;changing=true;latest.IsChecked=false;changing=false;}finally{latest.IsEnabled=true;}};
  latest.Unchecked+=(s,e)=>{if(changing)return;AppPreferences.Set("scannerApprovedRelease",null);selectedVersion.Text=Locale.T("선택 버전: ")+Components.ScannerVersion;foreach(var spec in selections.Keys)if(spec.Id=="scanner"){selections[spec].IsChecked=true;states[spec].Text=Locale.T("검증 버전 준비 필요");}};
  Get<StackPanel>("SetupComponents").Children.Add(PanelUi.Section(Locale.T("버전 예외"),latest,selectedVersion));
  var unlockerSite=PanelUi.Button(Locale.T("원본 페이지"));unlockerSite.Click+=(s,e)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/34736384/genshin-fps-unlock"){UseShellExecute=true});Get<StackPanel>("SetupUnlocker").Children.Add(unlockerSite);
  Get<StackPanel>("SetupUnlocker").Children.Insert(1,unlocker);
  unlocker.Checked+=(s,e)=>Get<StackPanel>("SetupEngineOptions").IsEnabled=true;unlocker.Unchecked+=(s,e)=>Get<StackPanel>("SetupEngineOptions").IsEnabled=false;
  Get<Button>("SetupContinue").Click+=(s,e)=>{if(!Preparing&&basePrepared)completed(Setup.FindExisting());};
  Get<Button>("SetupBrowse").Click+=(s,e)=>{
   var picker=new OpenFileDialog{Title=Locale.T("원신 실행 파일 선택"),Filter=Locale.T("원신 실행 파일|GenshinImpact.exe;YuanShen.exe"),CheckFileExists=true};
   if(picker.ShowDialog(window)==true){Get<TextBox>("SetupGamePath").Text=picker.FileName;Get<ListBox>("SetupCandidates").Visibility=Visibility.Collapsed;}
  };
  Get<TextBox>("SetupGamePath").TextChanged+=(s,e)=>Validate();
  Get<ListBox>("SetupCandidates").SelectionChanged+=(s,e)=>{var value=Get<ListBox>("SetupCandidates").SelectedItem as string;if(value!=null)Get<TextBox>("SetupGamePath").Text=value;};
  Get<Button>("SetupAuto").Click+=(s,e)=>ChooseEngine("");
  Get<Button>("SetupExisting").Click+=(s,e)=>{
   var picker=new OpenFileDialog{Title=Locale.T("기존 FPS 언락커 선택"),Filter=Locale.T("FPS 언락커|unlockfps_nc.exe;unlockfps_nc_signed.exe"),CheckFileExists=true};
   if(picker.ShowDialog(window)==true)ChooseEngine(picker.FileName);
  };
  Get<Button>("SetupCancel").Click+=(s,e)=>{if(!Preparing)canceled();};
  Get<Button>("SetupFinish").Click+=async(s,e)=>await Finish();
 }
 internal async void Open(Installation installation,bool canCancel) {
  existing=installation;basePrepared=false;Get<Expander>("SetupAdvanced").IsExpanded=false;Get<TextBlock>("SetupErrorDetails").Text="";Get<Button>("SetupFinish").Content=Locale.T("자동으로 준비하고 시작");Get<Button>("SetupContinue").Visibility=Visibility.Collapsed;int request=++generation;
  foreach(var spec in selections.Keys){bool available=spec.Available();RefreshState(spec);selections[spec].IsChecked=!available&&spec.Id!="bettergi"&&spec.Id!="hutao";}
  if(installation!=null){var prefs=Path.Combine(installation.Data,"launcher-settings.json");var values=File.Exists(prefs)?StoryClient.Read(prefs):new Dictionary<string,object>();unlocker.IsChecked=!values.ContainsKey("UseUnlockerLaunch")||Convert.ToBoolean(values["UseUnlockerLaunch"]);}

  Get<TextBlock>("SetupTitle").Text=Locale.T(canCancel?"설치 관리":"Catheryne 시작하기");
  LauncherWindowLayout.Primary(window,Get<Border>("SetupPanel"),false);DrawerMotion.Show(Get<Border>("SetupPanel"));
  Get<Button>("SetupCancel").Visibility=canCancel?Visibility.Visible:Visibility.Collapsed;
  Get<TextBlock>("SetupStatus").Text="";
  Get<TextBox>("SetupGamePath").Text="";
  Get<ListBox>("SetupCandidates").Visibility=Visibility.Collapsed;
  Get<Button>("CheckUpdate").IsEnabled=installation!=null;ChooseEngine(installation==null?"":installation.Engine);
  if(installation!=null&&File.Exists(installation.Config))try {
   var config=new ConfigStore(installation.Config).Read();
   if(config!=null&&config.ContainsKey("GamePath"))Get<TextBox>("SetupGamePath").Text=Convert.ToString(config["GamePath"]);
  }catch(ArgumentException){}catch(IOException){}
  if(!Setup.ValidGame(Get<TextBox>("SetupGamePath").Text))Get<TextBlock>("SetupGameHint").Text=Locale.T("설치 위치를 찾고 있습니다…");
  try {
   var found=await Task.Run(()=>Setup.Detect());
   if(request!=generation||!IsOpen)return;
   if(Get<TextBox>("SetupGamePath").Text.Length==0) {
    if(found.Count==1)Get<TextBox>("SetupGamePath").Text=found[0];
    else if(found.Count>1){Get<ListBox>("SetupCandidates").ItemsSource=found;Get<ListBox>("SetupCandidates").Visibility=Visibility.Visible;}
   }
   Validate();
  }catch(Exception error){if(request==generation&&IsOpen){Validate();Get<TextBlock>("SetupStatus").Text=Locale.T("자동 탐지 실패 · 찾아보기로 선택해 주세요.\n")+error.Message;}}
 }
 void RefreshState(ComponentSpec spec){states[spec].Text=Locale.T(spec.Available()?"준비됨":"설치 또는 준비 필요");}
 void SetPreparing(bool value){Preparing=value;Get<StackPanel>("SetupFields").IsEnabled=!value;Get<Button>("SetupCancel").IsEnabled=!value;Get<Button>("SetupContinue").IsEnabled=!value;Get<ProgressBar>("SetupProgress").Visibility=value?Visibility.Visible:Visibility.Collapsed;Validate();}
 async Task PrepareOne(ComponentSpec spec){if(Preparing)return;SetPreparing(true);states[spec].Text=Locale.T("준비 중…");try{await Task.Run(()=>Components.Prepare(spec));if(!spec.Available())throw new IOException(Locale.T("준비 상태를 확인하지 못했습니다."));Components.Remember(spec);selections[spec].IsChecked=false;RefreshState(spec);Get<TextBlock>("SetupStatus").Text=Locale.T("준비 완료");}catch(Exception error){states[spec].Text=Locale.T("실패 (재시도 가능)");Get<TextBlock>("SetupStatus").Text=error.Message;}finally{SetPreparing(false);}}
 internal void Hide(){++generation;DrawerMotion.Hide(Get<Border>("SetupPanel"));}
 void Validate() {
  bool valid=Setup.ValidGame(Get<TextBox>("SetupGamePath").Text);
  Get<TextBlock>("SetupGameHint").Text=valid?Locale.T("설치 위치 확인됨"):Locale.T("원신 실행 파일을 선택해 주세요.");
  Get<TextBlock>("SetupGameHint").Foreground=new SolidColorBrush(valid?Color.FromRgb(142,218,188):Color.FromRgb(169,178,192));
  Get<Button>("SetupFinish").IsEnabled=valid&&!Preparing;
 }
 void ChooseEngine(string path) {
  selectedEngine=path;
  bool automatic=path.Length==0;
  Get<Button>("SetupAuto").Background=new SolidColorBrush(automatic?Color.FromRgb(54,73,77):Color.FromRgb(42,46,53));
  Get<Button>("SetupExisting").Background=new SolidColorBrush(!automatic?Color.FromRgb(54,73,77):Color.FromRgb(42,46,53));
  Get<TextBlock>("SetupEnginePath").Text=automatic?Locale.T("공식 최신 버전"):path;
  Get<TextBlock>("SetupEngineHint").Visibility=Visibility.Collapsed;Get<TextBlock>("SetupEnginePath").ToolTip=automatic?Locale.T("다운로드 후 검증"):Locale.T("기존 설정 유지");
 }
 async Task Finish() {
  if(Preparing)return;
  Preparing=true;Get<StackPanel>("SetupFields").IsEnabled=false;Get<Button>("SetupCancel").IsEnabled=false;
  Get<Button>("SetupFinish").Content=Locale.T("준비 중…");Get<Button>("SetupContinue").IsEnabled=false;Validate();
  Get<TextBlock>("SetupErrorDetails").Text="";
  Get<TextBlock>("SetupStatus").Text=Locale.T("게임 실행을 준비하고 있습니다…");
  Get<ProgressBar>("SetupProgress").Visibility=Visibility.Visible;
  try {
   var failures=new List<string>();Installation installation=null;
   try{installation=await Setup.Prepare(Get<TextBox>("SetupGamePath").Text,selectedEngine,existing,unlocker.IsChecked==true);basePrepared=true;string prefsPath=Path.Combine(installation.Data,"launcher-settings.json");var prefs=StoryClient.Read(prefsPath);prefs["AutoUpdateUnlocker"]=Get<CheckBox>("AutoUpdate").IsChecked==true;AtomicFile.Write(prefsPath,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(prefs));if(unlocker.IsChecked==true)await Task.Run(()=>Components.UnlockerRuntime());}catch(Exception error){failures.Add(Locale.T("게임 실행")+": "+error.Message);}
   foreach(var spec in selections.Keys){if(selections[spec].IsChecked!=true)continue;Get<TextBlock>("SetupStatus").Text=Locale.T(spec.Title)+Locale.T(" 준비 중…");states[spec].Text=Locale.T("준비 중…");try{await Task.Run(()=>Components.Prepare(spec));if(!spec.Available())throw new IOException(Locale.T("준비 상태를 확인하지 못했습니다."));Components.Remember(spec);states[spec].Text=Locale.T("준비됨");selections[spec].IsChecked=false;}catch(Exception error){states[spec].Text=Locale.T("실패 (재시도 가능)");failures.Add(Locale.T(spec.Title)+": "+error.Message);}}
   if(failures.Count==0&&basePrepared){completed(installation);return;}
   Get<TextBlock>("SetupStatus").Text=Locale.T("일부 준비를 마치지 못했습니다. 다시 시도하거나 준비된 기능부터 사용하실 수 있습니다.");
   Get<TextBlock>("SetupErrorDetails").Text=string.Join("\n\n",failures);
   if(basePrepared)Get<Button>("SetupContinue").Visibility=Visibility.Visible;

  }catch(Exception error){Get<TextBlock>("SetupStatus").Text=error.Message;}
  finally {
   Preparing=false;Get<Button>("SetupContinue").IsEnabled=true;Get<StackPanel>("SetupFields").IsEnabled=true;Get<Button>("SetupCancel").IsEnabled=true;
   Get<Button>("SetupFinish").Content=Locale.T("다시 준비하기");Get<ProgressBar>("SetupProgress").Visibility=Visibility.Collapsed;Validate();
  }
 }
}
