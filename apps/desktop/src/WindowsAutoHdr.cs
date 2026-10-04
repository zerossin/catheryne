using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

// Windows' global game preference; it is independent of a monitor's HDR output.
internal sealed class WindowsAutoHdrState {
 public bool Supported;public bool? Enabled;public string Error="";
 internal bool CanSet {get{return Supported&&Error.Length==0;}}
}
internal sealed class WindowsAutoHdr {
 internal const string RegistryPath=@"Software\Microsoft\DirectX\UserGpuPreferences",ValueName="DirectXUserGlobalSettings";
 internal static Func<bool,bool?,WindowsAutoHdrState> TestBackend;
 readonly Func<bool,bool?,WindowsAutoHdrState> backend;
 internal WindowsAutoHdr(Func<bool,bool?,WindowsAutoHdrState> backend=null){this.backend=backend??TestBackend??Native;}
 static readonly Regex Token=new Regex(@"(^|;)\s*AutoHDREnable\s*=([^;]*)(?=;|$)",RegexOptions.IgnoreCase);
 internal static bool? Parse(string value){
  var matches=Token.Matches(value??"");if(matches.Count==0)return null;
  string choice=matches[0].Groups[2].Value.Trim();
  if(matches.Count!=1||(choice!="0"&&choice!="1"))throw new InvalidDataException(Locale.T("Windows 자동 HDR 설정 형식을 확인하지 못했습니다."));
  return choice=="1";
 }
 internal static string Update(string value,bool? enabled){
  value=value??"";Parse(value);var match=Token.Match(value);
  if(match.Success){
   // Remove only this entry, retaining unrelated entries and their order.
   int start=match.Index+match.Groups[1].Length,end=match.Index+match.Length;bool separator=end<value.Length&&value[end]==';';if(separator)end++;
   string replacement=enabled.HasValue?"AutoHDREnable="+(enabled.Value?"1":"0")+(separator?";":""):"";
   return value.Substring(0,start)+replacement+value.Substring(end);
  }
  return !enabled.HasValue?value:value+(value.Length>0&&!value.EndsWith(";")?";":"")+"AutoHDREnable="+(enabled.Value?"1":"0")+";";
 }
 static WindowsAutoHdrState Native(bool write,bool? enabled){
  int build;using(var version=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
   if(version==null||!int.TryParse(Convert.ToString(version.GetValue("CurrentBuildNumber")),out build)||build<22000)return new WindowsAutoHdrState();
  using(var key=write?Registry.CurrentUser.CreateSubKey(RegistryPath):Registry.CurrentUser.OpenSubKey(RegistryPath)){
   object raw=key==null?null:key.GetValue(ValueName);if(raw!=null&&(key.GetValueKind(ValueName)!=RegistryValueKind.String||!(raw is string)))throw new InvalidDataException(Locale.T("Windows 자동 HDR 설정 형식을 확인하지 못했습니다."));
   string before=raw as string;bool? preference=Parse(before);
   if(write&&preference!=enabled){string next=Update(before,enabled);if(next.Length==0)key.DeleteValue(ValueName,false);else key.SetValue(ValueName,next,RegistryValueKind.String);key.Flush();preference=Parse(key.GetValue(ValueName) as string);}
   return new WindowsAutoHdrState{Supported=true,Enabled=preference};
  }
 }
 internal WindowsAutoHdrState Read(){try{return backend(false,null);}catch(Exception error){return new WindowsAutoHdrState{Error=error.Message};}}
 internal WindowsAutoHdrState Set(bool? enabled){
  using(var mutex=new Mutex(false,"Local\\Catheryne.WindowsAutoHdr")){bool held=false;try{
   try{held=mutex.WaitOne(5000);}catch(AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException(Locale.T("Windows 자동 HDR을 변경하는 중입니다."));
   var before=backend(false,null);if(!before.CanSet)throw new InvalidOperationException(before.Error.Length>0?before.Error:Locale.T("Windows 자동 HDR을 변경할 수 없습니다."));
   if(before.Enabled==enabled)return before;backend(true,enabled);var result=backend(false,null);
   if(!result.CanSet||result.Enabled!=enabled)throw new InvalidOperationException(Locale.T("Windows 자동 HDR 설정 저장을 확인하지 못했습니다."));return result;
  }finally{if(held)mutex.ReleaseMutex();}}
 }
 internal static void Test(){
  if(Parse(null)!=null||Parse("")!=null||Parse("AutoHDREnable=0;")!=false||Parse("x=3;AutoHDREnable=1;VRROptimizeEnable=1;")!=true)throw new Exception("Auto HDR preference parsing failed");
  string mixed="SwapEffectUpgradeEnable=0; AutoHDREnable = 1;Unknown=value;VRROptimizeEnable=1;";
  if(Update(mixed,false)!="SwapEffectUpgradeEnable=0;AutoHDREnable=0;Unknown=value;VRROptimizeEnable=1;"||Update(mixed,null)!="SwapEffectUpgradeEnable=0;Unknown=value;VRROptimizeEnable=1;")throw new Exception("Auto HDR changed unrelated global preferences");
  if(Update("Unknown=value",true)!="Unknown=value;AutoHDREnable=1;"||Update("AutoHDREnable=0",null)!="")throw new Exception("Auto HDR token boundary failed");
  foreach(string invalid in new[]{"AutoHDREnable=2097;","AutoHDREnable=1;AutoHDREnable=0;","AutoHDREnable=broken;"}){bool rejected=false;try{Update(invalid,true);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("Unknown Auto HDR format was overwritten");}
  bool? saved=null;var fake=new WindowsAutoHdr((write,enabled)=>{if(write)saved=enabled;return new WindowsAutoHdrState{Supported=true,Enabled=saved};});fake.Set(false);fake.Set(true);fake.Set(null);if(saved!=null)throw new Exception("Windows default Auto HDR preference was not restored");
 }
}
