using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

// Game fields and Windows HDR are distinct projections of one display preset.
internal sealed class DisplayPresets {
 internal const int AiWidth=1920,AiHeight=1080;
 internal static readonly string[] Fields={"FPSTarget","Fullscreen","PopupWindow","UseCustomRes","CustomResX","CustomResY","IsExclusiveFullscreen","UseHDR","UsePowerSave","MonitorNum"};
 readonly string folder;internal readonly WindowsHdr Hdr;internal readonly WindowsAutoHdr AutoHdr;
 internal DisplayPresets(string folder,WindowsHdr hdr=null,WindowsAutoHdr autoHdr=null){this.folder=folder;Hdr=hdr??new WindowsHdr();AutoHdr=autoHdr??new WindowsAutoHdr();}
 static Dictionary<string,object> Select(Dictionary<string,object> input){return Fields.ToDictionary(k=>k,k=>input[k]);}
 static Dictionary<string,object> Settings(Dictionary<string,object> current,Dictionary<string,object> selected){var result=new Dictionary<string,object>(current);foreach(string key in Fields)result[key]=selected[key];UnlockerOptions.Validate(result);return result;}
 Dictionary<string,object> Read(LocalDataService db){var rows=db.Recent("default","display-presets");return rows.Count==0?new Dictionary<string,object>():new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(rows[0]["payload"]);}
 static Dictionary<string,object> Map(Dictionary<string,object> state,string key){object raw;return state.TryGetValue(key,out raw)?CodexChat.Map(raw):new Dictionary<string,object>();}
 static bool Matches(Dictionary<string,object> saved,Dictionary<string,object> current){return Fields.All(k=>saved.ContainsKey(k)&&Convert.ToString(saved[k])==Convert.ToString(current[k]));}
 static Dictionary<string,object> Capture(WindowsHdrState value){if(value.Error.Length>0)throw new InvalidOperationException(value.Error);return new Dictionary<string,object>{{"monitor",value.Monitor},{"identity",value.Identity},{"supported",value.Supported},{"enabled",value.Enabled}};}
 static bool HdrMatches(Dictionary<string,object> saved,WindowsHdrState current){return saved.Count==0||current!=null&&current.Error.Length==0&&Convert.ToInt32(saved["monitor"])==current.Monitor&&Convert.ToString(saved["identity"])==current.Identity&&Equals(saved["enabled"],current.Enabled);}
 static Dictionary<string,object> CaptureAuto(WindowsAutoHdrState value){if(value.Error.Length>0)throw new InvalidOperationException(value.Error);return new Dictionary<string,object>{{"supported",value.Supported},{"enabled",value.Enabled}};}
 static bool AutoMatches(Dictionary<string,object> saved,WindowsAutoHdrState current){return saved.Count==0||current!=null&&current.Error.Length==0&&Equals(saved["supported"],current.Supported)&&Equals(saved["enabled"],current.Enabled);}
 internal Dictionary<string,object> Snapshot(Dictionary<string,object> current,WindowsHdrState hdr=null,WindowsAutoHdrState autoHdr=null){
  using(var db=new LocalDataService(folder)){var state=Read(db);var personal=Map(state,"personal");var ai=Map(state,"ai");
   return new Dictionary<string,object>{{"current",Select(current)},{"personal",personal.Count==0?null:personal},{"personal_windows_hdr",Map(state,"personal_windows_hdr")},{"personal_windows_auto_hdr",Map(state,"personal_windows_auto_hdr")},{"matches_personal",Matches(personal,current)&&HdrMatches(Map(state,"personal_windows_hdr"),hdr)&&AutoMatches(Map(state,"personal_windows_auto_hdr"),autoHdr)},{"matches_ai",Matches(ai,current)&&HdrMatches(Map(state,"ai_windows_hdr"),hdr)&&AutoMatches(Map(state,"ai_windows_auto_hdr"),autoHdr)}};
  }
 }
 internal bool HasPersonal(){using(var db=new LocalDataService(folder))return Read(db).ContainsKey("personal");}
 internal void SavePersonal(Dictionary<string,object> current){UnlockerOptions.Validate(current);var hdr=Capture(Hdr.Read(Convert.ToInt32(current["MonitorNum"])));var auto=CaptureAuto(AutoHdr.Read());using(var mutex=PresetMutex()){Hold(mutex);try{using(var db=new LocalDataService(folder)){var state=Read(db);state["personal"]=Select(current);state["personal_windows_hdr"]=hdr;state["personal_windows_auto_hdr"]=auto;db.Observe("default","display-presets",state);}}finally{mutex.ReleaseMutex();}}}
 Mutex PresetMutex(){return new Mutex(false,"Local\\Catheryne.DisplayPreset."+EndgameKnowledge.Hash(folder).Substring(0,24));}
 static void Hold(Mutex mutex){bool held;try{held=mutex.WaitOne(5000);}catch(AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException(Locale.T("프리셋을 변경하는 중입니다."));}
 internal Dictionary<string,object> Apply(Dictionary<string,object> current,bool ai,Func<Dictionary<string,object>,bool> save){
  UnlockerOptions.Validate(current);using(var mutex=PresetMutex()){Hold(mutex);try{
   Dictionary<string,object> state;using(var db=new LocalDataService(folder))state=Read(db);
   var before=Hdr.Read(Convert.ToInt32(current["MonitorNum"]));var priorHdr=ai?Capture(before):null;var beforeAuto=AutoHdr.Read();var priorAuto=ai?CaptureAuto(beforeAuto):null;Dictionary<string,object> chosen,target,targetAuto;
   if(ai){
    bool alreadyAi=Matches(Map(state,"ai"),current)&&HdrMatches(Map(state,"ai_windows_hdr"),before)&&AutoMatches(Map(state,"ai_windows_auto_hdr"),beforeAuto);
    if(!alreadyAi||!state.ContainsKey("personal")){state["personal"]=Select(current);state["personal_windows_hdr"]=priorHdr;state["personal_windows_auto_hdr"]=priorAuto;}
    else if(!state.ContainsKey("personal_windows_hdr"))state["personal_windows_hdr"]=Capture(Hdr.Read(Convert.ToInt32(Map(state,"personal")["MonitorNum"])));
    if(!state.ContainsKey("personal_windows_auto_hdr"))state["personal_windows_auto_hdr"]=priorAuto;
    chosen=Select(current);chosen["Fullscreen"]=false;chosen["PopupWindow"]=true;chosen["UseCustomRes"]=true;chosen["UseHDR"]=false;chosen["UsePowerSave"]=false;chosen["IsExclusiveFullscreen"]=false;chosen["FPSTarget"]=60;chosen["CustomResX"]=AiWidth;chosen["CustomResY"]=AiHeight;
    target=new Dictionary<string,object>(priorHdr);target["enabled"]=false;state["ai"]=chosen;state["ai_windows_hdr"]=target;targetAuto=new Dictionary<string,object>(priorAuto);targetAuto["enabled"]=false;state["ai_windows_auto_hdr"]=targetAuto;
   }else{chosen=Map(state,"personal");if(chosen.Count==0)throw new InvalidOperationException(Locale.T("저장된 내 프리셋이 없습니다."));target=Map(state,"personal_windows_hdr");targetAuto=Map(state,"personal_windows_auto_hdr");}
   var next=Settings(current,chosen);WindowsHdrState rollback=null;bool changed=false,autoChanged=false,configSaved=false;WindowsAutoHdrState rollbackAuto=null;
   try{
    if(target.Count>0&&Equals(target["supported"],true)){
     int monitor=Convert.ToInt32(target["monitor"]);bool desired=Convert.ToBoolean(target["enabled"]);rollback=Hdr.Read(monitor);
     if(rollback.Error.Length>0)throw new InvalidOperationException(rollback.Error);
     if(rollback.Identity!=Convert.ToString(target["identity"]))throw new InvalidOperationException(Locale.T("프리셋에 저장된 HDR 모니터가 변경되었습니다."));
     if(rollback.Enabled!=desired){changed=true;Hdr.Set(monitor,desired,rollback.Identity);}
    }
    if(targetAuto.Count>0&&Equals(targetAuto["supported"],true)){rollbackAuto=AutoHdr.Read();if(rollbackAuto.Error.Length>0)throw new InvalidOperationException(rollbackAuto.Error);bool? desired=targetAuto["enabled"]==null?(bool?)null:Convert.ToBoolean(targetAuto["enabled"]);if(rollbackAuto.Enabled!=desired){autoChanged=true;AutoHdr.Set(desired);}}
    if(!save(next))throw new IOException(Locale.T("프리셋 설정을 저장하지 못했습니다."));configSaved=true;
    using(var db=new LocalDataService(folder))db.Observe("default","display-presets",state);return next;
   }catch(Exception error){
    Exception recovery=null;try{if(configSaved&&!save(current))throw new IOException(Locale.T("이전 화면 설정을 복원하지 못했습니다."));}catch(Exception e){recovery=e;}
    try{if(autoChanged)AutoHdr.Set(rollbackAuto.Enabled);}catch(Exception e){recovery=e;}
    try{if(changed)Hdr.Set(rollback.Monitor,rollback.Enabled,rollback.Identity);}catch(Exception e){recovery=e;}
    if(recovery!=null)throw new InvalidOperationException(error.Message+" "+recovery.Message,error);throw;
   }
  }finally{mutex.ReleaseMutex();}}
 }
 static void Wait(System.Threading.Tasks.Task task){while(!task.IsCompleted){var frame=new System.Windows.Threading.DispatcherFrame();var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);}task.GetAwaiter().GetResult();}
 internal static void Test(string folder){bool? autoEnabled=null;var fakeAuto=new WindowsAutoHdr((write,enabled)=>{if(write)autoEnabled=enabled;return new WindowsAutoHdrState{Supported=true,Enabled=autoEnabled};});bool hdrEnabled=true;var fakeHdr=new WindowsHdr((monitor,enabled,identity)=>{if(enabled.HasValue)hdrEnabled=enabled.Value;return new WindowsHdrState{Monitor=monitor,Identity="fixture-"+monitor,Supported=true,Enabled=hdrEnabled};});var presets=new DisplayPresets(folder,fakeHdr,fakeAuto);var mine=UnlockerOptions.Defaults();mine["FPSTarget"]=144;mine["UseHDR"]=true;var ai=presets.Apply(mine,true,v=>true);if(Convert.ToInt32(ai["CustomResX"])!=AiWidth||Convert.ToInt32(ai["CustomResY"])!=AiHeight)throw new Exception("Automation preset must support map recognition");ai=presets.Apply(ai,true,v=>true);ai["AdditionalCommandLine"]="unchanged";if(autoEnabled!=false)throw new Exception("AI preset did not disable Auto HDR");if(hdrEnabled)throw new Exception("AI preset did not switch Windows HDR off");var restored=presets.Apply(ai,false,v=>true);if(autoEnabled!=null)throw new Exception("Personal preset did not restore default Auto HDR preference");if(!hdrEnabled)throw new Exception("Personal preset did not restore Windows HDR");if(Convert.ToInt32(restored["FPSTarget"])!=144||!(bool)restored["UseHDR"]||Convert.ToString(restored["AdditionalCommandLine"])!="unchanged")throw new Exception("Preset restore failed");presets.SavePersonal(ai);if(Convert.ToInt32(presets.Apply(mine,false,v=>true)["FPSTarget"])!=60)throw new Exception("Explicit preset save failed");var reopened=new DisplayPresets(folder,fakeHdr,fakeAuto).Snapshot(ai,fakeHdr.Read(1),fakeAuto.Read());if(!(bool)reopened["matches_personal"]||((Dictionary<string,object>)reopened["personal"]).Count!=Fields.Length)throw new Exception("Persisted preset status failed");
  var owner=(System.Windows.Window)System.Windows.Markup.XamlReader.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Main.xaml")));
  UnlockerOptionsPanel panel=null;var saved=UnlockerOptions.Defaults();int writes=0;bool succeed=true;
  panel=new UnlockerOptionsPanel(owner,message=>{},()=>{writes++;if(succeed)saved=panel.Read();return succeed;},new DisplayPresets(folder,fakeHdr,fakeAuto));
  panel.Load(mine);panel.SetEditable(false);Wait(panel.ApplyAiPreset());if(writes!=0||Convert.ToInt32(panel.Read()["FPSTarget"])!=144)throw new Exception("Busy preset changed UI");
  panel.SetEditable(true);Wait(panel.ApplyAiPreset());if(writes!=1||Convert.ToInt32(saved["FPSTarget"])!=60)throw new Exception("Preset selection not committed");
  Wait(panel.RestorePersonalPreset());if(writes!=2||Convert.ToInt32(saved["FPSTarget"])!=144)throw new Exception("Preset restore not committed");
  succeed=false;Wait(panel.ApplyAiPreset());if(Convert.ToInt32(panel.Read()["FPSTarget"])!=144)throw new Exception("Failed preset save changed UI");
  Wait(panel.SavePersonalPreset());if(((System.Windows.Controls.ContentControl)owner.FindName("PresetSummary")).Content==null||!(bool)new DisplayPresets(folder,fakeHdr,fakeAuto).Snapshot(mine,fakeHdr.Read(1),fakeAuto.Read())["matches_personal"])throw new Exception("Preset summary/save failed");
  if(!hdrEnabled)throw new Exception("Failed preset save did not restore Windows HDR");
  var snapshotBefore=presets.Snapshot(mine,fakeHdr.Read(1),fakeAuto.Read());var failHdr=new WindowsHdr((monitor,enabled,identity)=>{if(enabled.HasValue)throw new IOException("fixture set failure");return new WindowsHdrState{Monitor=monitor,Identity="fixture-"+monitor,Supported=true,Enabled=true};});bool rejected=false;try{new DisplayPresets(folder,failHdr,fakeAuto).Apply(mine,true,v=>true);}catch(IOException){rejected=true;}if(!rejected||!Equals(snapshotBefore["matches_personal"],true))throw new Exception("Failed HDR switch changed the preset");
  var unsupported=new WindowsHdr((monitor,enabled,identity)=>{if(enabled.HasValue)throw new Exception("Unsupported display was changed");return new WindowsHdrState{Monitor=monitor,Identity="sdr",Supported=false};});var sdrPresets=new DisplayPresets(Path.Combine(folder,"sdr"),unsupported);sdrPresets.Apply(mine,true,v=>true);sdrPresets.Apply(ai,false,v=>true);
  var legacyFolder=Path.Combine(folder,"legacy");using(var db=new LocalDataService(legacyFolder))db.Observe("default","display-presets",new{personal=Select(mine)});int legacyWrites=0;var unavailable=new WindowsHdr((monitor,enabled,identity)=>{legacyWrites++;throw new IOException("unavailable fixture");});var legacy=new DisplayPresets(legacyFolder,unavailable).Apply(ai,false,v=>true);if(Convert.ToInt32(legacy["FPSTarget"])!=144||legacyWrites!=1)throw new Exception("Legacy preset unexpectedly changed Windows HDR");
  var missing=new WindowsHdr((monitor,enabled,identity)=>new WindowsHdrState{Monitor=monitor,Identity="different",Supported=true,Enabled=true});bool wrongMonitor=false;try{new DisplayPresets(folder,missing,fakeAuto).Apply(ai,false,v=>true);}catch(InvalidOperationException){wrongMonitor=true;}if(!wrongMonitor)throw new Exception("Preset used another monitor");
  if(!Equals(new DisplayPresets(folder,fakeHdr,fakeAuto).Snapshot(mine,fakeHdr.Read(1),fakeAuto.Read())["matches_personal"],true))throw new Exception("Failed HDR operation overwrote personal preset");
  // Explicit on/off, defaults, repeat selection, reopen, failures and legacy restoration.
  foreach(bool? preference in new bool?[]{true,false,null}){autoEnabled=preference;hdrEnabled=true;var fixture=new DisplayPresets(Path.Combine(folder,"auto-"+Convert.ToString(preference)),fakeHdr,fakeAuto);fixture.SavePersonal(mine);var selected=fixture.Apply(mine,true,v=>true);fixture.Apply(selected,true,v=>true);new DisplayPresets(Path.Combine(folder,"auto-"+Convert.ToString(preference)),fakeHdr,fakeAuto).Apply(selected,false,v=>true);if(autoEnabled!=preference||!hdrEnabled)throw new Exception("Independent HDR preference restoration failed");}
  autoEnabled=true;hdrEnabled=true;var failure=new DisplayPresets(Path.Combine(folder,"auto-failure"),fakeHdr,fakeAuto);failure.SavePersonal(mine);try{failure.Apply(mine,true,v=>false);throw new Exception("Failed save accepted");}catch(IOException){}if(autoEnabled!=true||!hdrEnabled)throw new Exception("Failed config save did not roll back both HDR settings");
  var failAuto=new WindowsAutoHdr((write,enabled)=>{if(write)throw new IOException("auto fixture failure");return new WindowsAutoHdrState{Supported=true,Enabled=true};});try{new DisplayPresets(Path.Combine(folder,"auto-set-failure"),fakeHdr,failAuto).Apply(mine,true,v=>true);throw new Exception("Failed Auto HDR write accepted");}catch(IOException){}if(!hdrEnabled)throw new Exception("Auto HDR failure did not roll back monitor HDR");
  autoEnabled=true;new DisplayPresets(legacyFolder,fakeHdr,fakeAuto).Apply(ai,false,v=>true);if(autoEnabled!=true)throw new Exception("Legacy preset changed uncaptured Auto HDR");
  owner.Close();}
}
