using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

internal static class UnlockerOptions {
 // Canonical editable fields for upstream v3.5.0. GamePath is owned by installation
 // setup, AutoStart by the launch contract, LastVersionNotify by the engine.
 internal static readonly string[] Booleans={"AutoClose","Fullscreen","PopupWindow","UseCustomRes","IsExclusiveFullscreen","StartMinimized","UsePowerSave","SuspendLoad","UseMobileUI","UseHDR"};
 internal static readonly string[] WindowModes={"창 모드","테두리 없는 창","테두리 없는 전체화면","독점 전체화면"};
 internal static readonly string[] DisplayFlags={"Fullscreen","PopupWindow","IsExclusiveFullscreen","UseCustomRes"};
 internal static int WindowMode(Dictionary<string,object> values){return Convert.ToBoolean(values["Fullscreen"])?(Convert.ToBoolean(values["IsExclusiveFullscreen"])?3:2):Convert.ToBoolean(values["PopupWindow"])?1:0;}
 internal static void SetWindowMode(Dictionary<string,object> values,int mode){if(mode<0||mode>=WindowModes.Length)throw new ArgumentException("화면 모드를 선택해 주세요.");values["Fullscreen"]=mode>=2;values["PopupWindow"]=mode==1;values["IsExclusiveFullscreen"]=mode==3;}
 internal static void FitMonitor(Dictionary<string,object> values){int number=Number(values["MonitorNum"],Locale.T("모니터 번호"),1,100);var screens=System.Windows.Forms.Screen.AllScreens.OrderByDescending(screen=>screen.Primary).ToArray();if(number>screens.Length)throw new ArgumentException(Locale.T("선택한 모니터가 연결되어 있지 않습니다."));var bounds=screens[number-1].Bounds;values["UseCustomRes"]=true;values["CustomResX"]=bounds.Width;values["CustomResY"]=bounds.Height;Validate(values);}
 internal static string GameArguments(Dictionary<string,object> values){Validate(values);return (Convert.ToBoolean(values["PopupWindow"])?"-popupwindow ":"")+(Convert.ToBoolean(values["UseCustomRes"])?"-screen-width "+values["CustomResX"]+" -screen-height "+values["CustomResY"]+" ":"")+"-screen-fullscreen "+(Convert.ToBoolean(values["Fullscreen"])?"1 -window-mode "+(Convert.ToBoolean(values["IsExclusiveFullscreen"])?"exclusive":"borderless"):"0")+" -monitor "+values["MonitorNum"]+" "+Convert.ToString(values["AdditionalCommandLine"]);}
 internal static Dictionary<string,object> Defaults() {
  var result=Booleans.ToDictionary(k=>k,k=>(object)(k=="AutoClose"||k=="Fullscreen"));
  result["FPSTarget"]=120;result["CustomResX"]=1920;result["CustomResY"]=1080;result["MonitorNum"]=1;result["Priority"]=3;
  result["AdditionalCommandLine"]="";result["DllList"]=new string[0];return result;
 }
 internal static Dictionary<string,object> WithDefaults(Dictionary<string,object> config) {
  var result=Defaults();foreach(var pair in config)result[pair.Key]=pair.Value;return result;
 }
 internal static int Number(object value,string label,int min,int max) {
  int number;if(!int.TryParse(Convert.ToString(value),out number)||number<min||number>max)throw new ArgumentException(Locale.Format("{0}은(는) {1}~{2} 사이의 정수로 입력해 주세요.",Locale.T(label),min,max));return number;
 }
 internal static string[] DllPaths(object value) {
  var sequence=value as IEnumerable;
  if(sequence==null||value is string)throw new ArgumentException(Locale.T("DLL 목록 형식이 잘못되었습니다."));
  return sequence.Cast<object>().Select(item=>{if(!(item is string))throw new ArgumentException(Locale.T("DLL 경로가 잘못되었습니다."));return (string)item;}).ToArray();
 }
 internal static void Validate(Dictionary<string,object> values) {
  foreach(string key in Booleans)if(!(values[key] is bool))throw new ArgumentException(Locale.Format("{0} 설정 형식이 잘못되었습니다.",key));
  Number(values["FPSTarget"],"FPS",1,1000);
  Number(values["CustomResX"],Locale.T("가로 해상도"),200,7680);Number(values["CustomResY"],Locale.T("세로 해상도"),200,4320);
  Number(values["MonitorNum"],Locale.T("모니터 번호"),1,100);Number(values["Priority"],Locale.T("우선순위"),0,5);
  if((bool)values["Fullscreen"]&&(bool)values["PopupWindow"])throw new ArgumentException(Locale.T("전체화면과 테두리 없는 창 모드는 함께 사용할 수 없습니다."));
  var arguments=values["AdditionalCommandLine"] as string;
  if(arguments==null||arguments.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new ArgumentException(Locale.T("추가 실행 인수는 한 줄로 입력해 주세요."));
  DllPaths(values["DllList"]);
 }
 internal static void ValidateDll(string path) {
  if(!File.Exists(path)||!Path.GetExtension(path).Equals(".dll",StringComparison.OrdinalIgnoreCase))throw new ArgumentException(Locale.T("DLL 파일을 선택해 주세요."));
  try {
   using(var file=File.OpenRead(path))using(var reader=new BinaryReader(file)) {
    if(file.Length<64||reader.ReadUInt16()!=0x5a4d)throw new InvalidDataException();
    file.Position=0x3c;int offset=reader.ReadInt32();
    if(offset<64||offset>file.Length-264)throw new InvalidDataException();
    file.Position=offset;if(reader.ReadUInt32()!=0x4550||reader.ReadUInt16()!=0x8664)throw new InvalidDataException();
    file.Position=offset+20;int optionalSize=reader.ReadUInt16();int flags=reader.ReadUInt16();
    if(optionalSize<240||offset+24L+optionalSize>file.Length||(flags&0x2000)==0||reader.ReadUInt16()!=0x20b)throw new InvalidDataException();
    file.Position=offset+24+108;uint directories=reader.ReadUInt32();
    if(directories>14){file.Position=offset+24+112+14*8;if(reader.ReadUInt32()!=0||reader.ReadUInt32()!=0)throw new InvalidDataException();}
   }
  }catch(EndOfStreamException){throw new ArgumentException(Locale.T("네이티브 x64 DLL만 사용할 수 있습니다."));}
   catch(InvalidDataException){throw new ArgumentException(Locale.T("네이티브 x64 DLL만 사용할 수 있습니다."));}
 }
}

internal sealed class UnlockerOptionsPanel {
 readonly Window window;
 readonly Action<string> status;
 readonly Func<bool> save;
 internal bool Loading {get{return loading;}}
 bool loading,editable,availabilityInitialized,applying,requestedEditable;
 readonly WindowsHdrPanel windowsHdr;
 T Get<T>(string name)where T:class{return window.FindName(name) as T;}
 TextBox Field(string key){return Get<TextBox>(key=="FPSTarget"?"FPS":key);}
 internal UnlockerOptionsPanel(Window owner,Action<string> onStatus,Func<bool> saveSettings,DisplayPresets presetStore=null) {
  window=owner;status=onStatus;save=saveSettings;presets=presetStore??new DisplayPresets(Setup.DataFolder);windowsHdr=new WindowsHdrPanel(owner,presets.Hdr,presets.AutoHdr,status,RefreshPresets);
  Get<Button>("FitMonitor").Click+=(s,e)=>{if(!editable)return;try{var prior=Read();var fitted=new Dictionary<string,object>(prior);UnlockerOptions.FitMonitor(fitted);Load(fitted);if(!save())Load(prior);}catch(Exception error){status(error.Message);}};
  Get<ComboBox>("WindowMode").SelectionChanged+=(s,e)=>Availability();
  Get<ComboBox>("ResolutionMode").SelectionChanged+=(s,e)=>Availability();
  Get<Button>("AddDll").Click+=(s,e)=>{
   var picker=new Microsoft.Win32.OpenFileDialog{Title=Locale.T("DLL 추가"),Filter=Locale.T("네이티브 x64 DLL|*.dll"),Multiselect=true,CheckFileExists=true};
   if(picker.ShowDialog(window)!=true)return;
   try {
    foreach(string path in picker.FileNames)UnlockerOptions.ValidateDll(path);
    foreach(string path in picker.FileNames)if(!Get<ListBox>("DllList").Items.Cast<string>().Contains(path,StringComparer.OrdinalIgnoreCase))Get<ListBox>("DllList").Items.Add(path);
   }catch(Exception error){status(error.Message);}
  };
  Get<Button>("RemoveDll").Click+=(s,e)=>{var list=Get<ListBox>("DllList");if(list.SelectedIndex>=0)list.Items.RemoveAt(list.SelectedIndex);};
  Get<Button>("DllUp").Click+=(s,e)=>MoveDll(-1);Get<Button>("DllDown").Click+=(s,e)=>MoveDll(1);
 }
 void MoveDll(int direction){var list=Get<ListBox>("DllList");int index=list.SelectedIndex,target=index+direction;if(index<0||target<0||target>=list.Items.Count)return;var item=list.Items[index];list.Items.RemoveAt(index);list.Items.Insert(target,item);list.SelectedIndex=target;}
 internal void Load(Dictionary<string,object> config) {
  var values=UnlockerOptions.WithDefaults(config);loading=true;
  try {
   foreach(string key in UnlockerOptions.Booleans.Except(UnlockerOptions.DisplayFlags))Get<CheckBox>(key).IsChecked=Convert.ToBoolean(values[key]);
   Get<ComboBox>("WindowMode").SelectedIndex=UnlockerOptions.WindowMode(values);Get<ComboBox>("ResolutionMode").SelectedIndex=Convert.ToBoolean(values["UseCustomRes"])?1:0;
   foreach(string key in new[]{"FPSTarget","CustomResX","CustomResY","MonitorNum","AdditionalCommandLine"})Field(key).Text=Convert.ToString(values[key]);
   Get<ComboBox>("Priority").SelectedIndex=Convert.ToInt32(values["Priority"]);
   Get<ListBox>("DllList").Items.Clear();foreach(string path in UnlockerOptions.DllPaths(values["DllList"]))Get<ListBox>("DllList").Items.Add(path);
  }finally{loading=false;Availability();}
 }
 internal Dictionary<string,object> Read() {
  var values=UnlockerOptions.Defaults();
  foreach(string key in UnlockerOptions.Booleans.Except(UnlockerOptions.DisplayFlags))values[key]=Get<CheckBox>(key).IsChecked==true;
  UnlockerOptions.SetWindowMode(values,Get<ComboBox>("WindowMode").SelectedIndex);values["UseCustomRes"]=Get<ComboBox>("ResolutionMode").SelectedIndex==1;
  values["FPSTarget"]=UnlockerOptions.Number(Field("FPSTarget").Text,"FPS",1,1000);
  values["CustomResX"]=UnlockerOptions.Number(Field("CustomResX").Text,Locale.T("가로 해상도"),200,7680);
  values["CustomResY"]=UnlockerOptions.Number(Field("CustomResY").Text,Locale.T("세로 해상도"),200,4320);
  values["MonitorNum"]=UnlockerOptions.Number(Field("MonitorNum").Text,Locale.T("모니터 번호"),1,100);
  values["Priority"]=Get<ComboBox>("Priority").SelectedIndex;
  values["AdditionalCommandLine"]=Field("AdditionalCommandLine").Text;
  values["DllList"]=Get<ListBox>("DllList").Items.Cast<string>().ToArray();
  UnlockerOptions.Validate(values);return values;
 }
 readonly DisplayPresets presets;
 internal System.Threading.Tasks.Task ApplyAiPreset(){return ApplyPreset(true);}
 internal System.Threading.Tasks.Task RestorePersonalPreset(){return ApplyPreset(false);}
 async System.Threading.Tasks.Task ApplyPreset(bool ai){
  if(!editable){status(Locale.T("게임 실행이 끝난 뒤 변경해 주세요."));return;}
  var previous=Read();applying=true;SetEditable(requestedEditable);Get<Button>("SavePersonalPreset").IsEnabled=false;
  try{await System.Threading.Tasks.Task.Run(()=>presets.Apply(previous,ai,values=>window.Dispatcher.Invoke(new Func<bool>(()=>{Load(values);return save();}))));}catch(Exception error){Load(previous);status(error.Message);}
  finally{applying=false;SetEditable(requestedEditable);Get<Button>("SavePersonalPreset").IsEnabled=true;}await windowsHdr.Refresh();RefreshPresets();
 }
 internal async System.Threading.Tasks.Task SavePersonalPreset(){if(applying)return;applying=true;SetEditable(requestedEditable);Get<Button>("SavePersonalPreset").IsEnabled=false;try{var values=Read();await System.Threading.Tasks.Task.Run(()=>presets.SavePersonal(values));await windowsHdr.Refresh();RefreshPresets();status(Locale.T("내 프리셋을 저장했습니다."));}catch(Exception error){status(error.Message);}finally{applying=false;SetEditable(requestedEditable);Get<Button>("SavePersonalPreset").IsEnabled=true;}}
 internal void RefreshPresets(){
  var snapshot=presets.Snapshot(Read(),windowsHdr.State,windowsHdr.AutoState);var personal=snapshot["personal"] as Dictionary<string,object>;
  var body=new StackPanel();body.Children.Add(new TextBlock{Text=Locale.T((bool)snapshot["matches_personal"]?"현재 설정: 내 프리셋":(bool)snapshot["matches_ai"]?"현재 설정: AI 프리셋":"현재 설정: 사용자 지정"),Foreground=System.Windows.Media.Brushes.LightGray,Margin=new Thickness(0,0,0,12)});
  if(personal==null)body.Children.Add(new TextBlock{Text=Locale.T("저장된 내 프리셋 없음"),Foreground=System.Windows.Media.Brushes.Gray});
  else{
   var details=new StackPanel{Margin=new Thickness(0,12,0,0)};
   details.Children.Add(PanelUi.Row(Locale.T("화면 모드"),PanelUi.Text(Locale.T(UnlockerOptions.WindowModes[UnlockerOptions.WindowMode(personal)]),true)));
   details.Children.Add(PanelUi.Row(Locale.T("해상도"),PanelUi.Text(Convert.ToBoolean(personal["UseCustomRes"])?personal["CustomResX"]+" × "+personal["CustomResY"]:Locale.T("원신 설정 따르기"),true)));
   string[] keys={"FPSTarget","UseHDR","UsePowerSave","MonitorNum"};string[] labels={"프레임 제한","게임 HDR (언락커)","백그라운드 절전","모니터"};
   for(int i=0;i<keys.Length;i++){var value=personal[keys[i]];string text=value is bool?Locale.T((bool)value?"켜짐":"꺼짐"):Convert.ToString(value);if(i==0)text+=" FPS";details.Children.Add(PanelUi.Row(Locale.T(labels[i]),new TextBlock{Text=text,Foreground=System.Windows.Media.Brushes.LightGray}));}
   var savedHdr=CodexChat.Map(snapshot["personal_windows_hdr"]);details.Children.Add(PanelUi.Row(Locale.T("Windows HDR"),PanelUi.Text(Locale.T(savedHdr.Count==0?"미확인":!Equals(savedHdr["supported"],true)?"미지원":Equals(savedHdr["enabled"],true)?"켜짐":"꺼짐"),true)));
   var savedAuto=CodexChat.Map(snapshot["personal_windows_auto_hdr"]);details.Children.Add(PanelUi.Row(Locale.T("Windows 자동 HDR"),PanelUi.Text(Locale.T(savedAuto.Count==0?"미확인":!Equals(savedAuto["supported"],true)?"미지원":savedAuto["enabled"]==null?"Windows 기본값":Equals(savedAuto["enabled"],true)?"켜짐":"꺼짐"),true)));
   var expander=new Expander{Header=Locale.T("저장된 내 프리셋"),Content=details};expander.SetResourceReference(FrameworkElement.StyleProperty,"OptionsExpander");
   var old=Get<ContentControl>("PresetSummary").Content as StackPanel;if(old!=null&&old.Children.Count>1&&old.Children[1] is Expander)expander.IsExpanded=((Expander)old.Children[1]).IsExpanded;
   body.Children.Add(expander);
  }
  Get<ContentControl>("PresetSummary").Content=body;
  Get<Button>("PersonalPreset").IsEnabled=editable&&personal!=null;
 }
 internal void SetEditable(bool value){requestedEditable=value;value=value&&!applying;windowsHdr.SetEditable(value);if(availabilityInitialized&&editable==value)return;availabilityInitialized=true;editable=value;Get<StackPanel>("AdvancedOptions").IsEnabled=value;foreach(string name in new[]{"UseHDR","UsePowerSave"})Get<CheckBox>(name).IsEnabled=value;Field("MonitorNum").IsEnabled=value;Get<Button>("AiPreset").IsEnabled=value;Get<Button>("PersonalPreset").IsEnabled=value&&presets.HasPersonal();Get<ComboBox>("WindowMode").IsEnabled=value;Get<ComboBox>("ResolutionMode").IsEnabled=value;Get<Button>("FitMonitor").IsEnabled=value;Get<CheckBox>("UseUnlockerLaunch").IsEnabled=value;Availability();}
 void Availability(){bool custom=Get<ComboBox>("ResolutionMode").SelectedIndex==1;Get<Grid>("CustomResolution").Visibility=custom?Visibility.Visible:Visibility.Collapsed;Field("CustomResX").IsEnabled=editable&&custom;Field("CustomResY").IsEnabled=editable&&custom;}
}
