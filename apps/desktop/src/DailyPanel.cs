using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class AppPreferences {
 static readonly FileReadCache<Dictionary<string,object>> cache=new FileReadCache<Dictionary<string,object>>();
 internal static Dictionary<string,object> Read(string root=null){string file=Path.Combine(root??Setup.DataFolder,"settings.json");return JsonCopy.Map(cache.Read(file,StoryClient.Read)??new Dictionary<string,object>());}
 internal static void Set(string key,object value,string root=null){
  string file=Path.GetFullPath(Path.Combine(root??Setup.DataFolder,"settings.json"));root=Path.GetDirectoryName(file);DailySettings.Validate(key,value);string identity;using(var hash=System.Security.Cryptography.SHA256.Create())identity=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(file.ToUpperInvariant()))).Replace("-","").Substring(0,24);
  using(var mutex=PrivateIpc.Mutex("Global\\Catheryne.Settings."+identity)){bool held=false;try{try{held=mutex.WaitOne(5000);}catch(System.Threading.AbandonedMutexException){held=true;}if(!held)throw new IOException(Locale.T("설정 저장이 진행 중입니다."));
   cache.Invalidate(file);var prefs=Read(root);var json=CatheryneTools.Json();object prior;if(prefs.TryGetValue(key,out prior)&&json.Serialize(prior)==json.Serialize(value))return;prefs[key]=value;Directory.CreateDirectory(root);AtomicFile.Write(file,json.Serialize(prefs));cache.Invalidate(file);
  }finally{if(held)mutex.ReleaseMutex();}}
 }
 internal static bool Flag(string key,bool defaultValue=false){object value;return Read().TryGetValue(key,out value)?value is bool&&(bool)value:defaultValue;}
}
internal static class HoyoClient {
 const string Reward="https://sg-hk4e-api.hoyolab.com/event/sol/";
 internal static Dictionary<string,object> Request(string url,string cookie,bool post=false){

  var request=HttpTransport.Create(url);request.AllowAutoRedirect=false;request.Timeout=15000;request.ReadWriteTimeout=15000;request.UserAgent="Catheryne";request.Referer="https://act.hoyolab.com/";request.Headers["Cookie"]=cookie;request.Headers["x-rpc-signgame"]="hk4e";
  if(post){request.Method="POST";request.ContentType="application/json";byte[] bytes=Encoding.UTF8.GetBytes("{\"act_id\":\"e202102251931481\"}");using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);}
  using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){
   var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
   if(Convert.ToInt32(data["retcode"])!=0)throw new InvalidOperationException("HoYoLAB response "+Convert.ToString(data["retcode"]));
   return (Dictionary<string,object>)data["data"];
  }
 }
 internal static Dictionary<string,object> Account(string cookie,string root){
  var prefs=AppPreferences.Read(root);if(!string.IsNullOrEmpty(CodexChat.S(prefs,"resinUid"))&&!string.IsNullOrEmpty(CodexChat.S(prefs,"resinServer")))return prefs;
  var result=Request("https://api-account-os.hoyolab.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_global",cookie);object raw;
  var roles=result.TryGetValue("list",out raw)?CodexChat.Items(raw).Where(r=>CodexChat.S(r,"game_biz")=="hk4e_global").ToList():new List<Dictionary<string,object>>();
  if(roles.Count!=1)throw new InvalidOperationException(roles.Count==0?Locale.T("연결된 원신 계정을 찾지 못했습니다."):"상단 계정에서 사용할 원신 계정을 선택해 주세요.");
  AppPreferences.Set("resinUid",CodexChat.S(roles[0],"game_uid"),root);AppPreferences.Set("resinServer",CodexChat.S(roles[0],"region"),root);return AppPreferences.Read(root);
 }
 internal static Dictionary<string,object> Attendance(string cookie){return Request(Reward+"info?act_id=e202102251931481&lang="+(Locale.IsEnglish?"en-us":"ko-kr"),cookie);}
 internal static Dictionary<string,object> Claim(string cookie){var before=Attendance(cookie);if(Convert.ToBoolean(before["is_sign"]))return before;Request(Reward+"sign?act_id=e202102251931481&lang="+(Locale.IsEnglish?"en-us":"ko-kr"),cookie,true);var after=Attendance(cookie);if(!Convert.ToBoolean(after["is_sign"]))throw new InvalidOperationException("Check-in requires confirmation on HoYoLAB.");return after;}
}
internal sealed class DailyPanel : IDisposable {
 internal static readonly System.Windows.Input.RoutedUICommand LoginCommand=new System.Windows.Input.RoutedUICommand("HoYoLAB 로그인","HoyoLogin",typeof(DailyPanel));
 internal readonly StackPanel View=new StackPanel();
 readonly Window owner;
 readonly ResinPanel resin=new ResinPanel();
 readonly RedemptionPanel redemption;
 readonly DailyNotifications notifications=new DailyNotifications();
 readonly TextBlock status=PanelUi.Text(Locale.T("HoYoLAB에 연결하면 출석 현황을 확인합니다."),true);
 readonly TextBlock connection=PanelUi.Text(Locale.T("로그인 상태 확인 전"),true);
 readonly FrameworkElement connectionCheck=PanelUi.VerifiedMark(),attendanceCheck=PanelUi.VerifiedMark();
 readonly Button claim=PanelUi.Button(Locale.T("오늘 출석하기"));
 readonly DispatcherTimer timer=new DispatcherTimer();
 bool busy;
 DateTime nextCheck=DateTime.UtcNow.AddMinutes(1);
 internal DailyPanel(Window owner){
  this.owner=owner;redemption=new RedemptionPanel(owner);owner.CommandBindings.Add(new System.Windows.Input.CommandBinding(LoginCommand,async(s,e)=>{e.Handled=true;await AccountConnections.For(owner).Connect("hoyolab");}));
  try{using(var db=new LocalDataService(Setup.DataFolder)){var recent=db.Recent("default","attendance",1);if(recent.Count>0){var previous=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(recent[0]["payload"]);status.Text=Locale.T("마지막 확인: ")+DateTime.Parse(recent[0]["observed_at"]).ToLocalTime().ToString("g",Locale.Culture)+"  "+Convert.ToString(previous["total_sign_day"])+Locale.T("회");}}}catch{status.Text=Locale.T("저장된 현황을 읽지 못했습니다.");}

  claim.Click+=async(s,e)=>{if(await AccountConnections.For(owner).Ensure("hoyolab"))await Run(true);};
  var automatic=new CheckBox {Content=Locale.T("자동 출석"),IsChecked=AppPreferences.Flag("automaticCheckIn")};bool bindingAutomatic=false;automatic.Checked+=async(s,e)=>{if(bindingAutomatic)return;if(await AccountConnections.For(owner).Ensure("hoyolab"))AppPreferences.Set("automaticCheckIn",true);else{bindingAutomatic=true;automatic.IsChecked=false;bindingAutomatic=false;}};automatic.Unchecked+=(s,e)=>{if(!bindingAutomatic)AppPreferences.Set("automaticCheckIn",false);};

  var notify=new CheckBox {Content=Locale.T("출석 완료 알림"),IsChecked=AppPreferences.Flag("attendanceNotifications")};notify.Checked+=(s,e)=>AppPreferences.Set("attendanceNotifications",true);notify.Unchecked+=(s,e)=>AppPreferences.Set("attendanceNotifications",false);
  var background=new CheckBox {Content=Locale.T("창을 닫아도 일상 관리 계속 (Windows 로그인 시 시작)"),IsChecked=AppPreferences.Flag("backgroundDaily")};background.Checked+=(s,e)=>DailyAgent.Configure(true);background.Unchecked+=(s,e)=>DailyAgent.Configure(false);
  AccountConnections.For(owner).Changed+=()=>{bindingAutomatic=true;automatic.IsChecked=AppPreferences.Flag("automaticCheckIn");bindingAutomatic=false;};
  View.Children.Add(PanelUi.Section(Locale.T("출석"),WithCheck(attendanceCheck,status),PanelUi.Actions(claim),automatic,notify));
  View.Children.Add(redemption.View);
  View.Children.Add(resin.View);
  background.Content=new TextBlock {Text=Locale.T("창을 닫아도 일상 관리 계속 (Windows 로그인 시 시작)"),TextWrapping=TextWrapping.Wrap};
  View.Children.Add(PanelUi.Section(Locale.T("백그라운드 실행"),background));
  View.IsVisibleChanged+=async(s,e)=>{if(View.IsVisible){bindingAutomatic=true;automatic.IsChecked=AppPreferences.Flag("automaticCheckIn");bindingAutomatic=false;notify.IsChecked=AppPreferences.Flag("attendanceNotifications");background.IsChecked=AppPreferences.Flag("backgroundDaily");if(!busy)await Run(false);}};
  timer.Interval=TimeSpan.FromMinutes(1);timer.Tick+=async(s,e)=>{if(!busy&&AppPreferences.Flag("automaticCheckIn")&&DateTime.UtcNow>=nextCheck)await Run(true);else if(!busy&&View.IsVisible)await Run(false);await RefreshCalendar();await RefreshPrimogems();};timer.Start();
 }
 bool calendarBusy;
 bool primogemsBusy;
 async Task RefreshPrimogems(){if(primogemsBusy)return;primogemsBusy=true;try{using(var db=new LocalDataService(Setup.DataFolder))if(string.IsNullOrEmpty(db.GetSecret("hoyolab")))return;await Task.Run(()=>new PrimogemService(Setup.DataFolder).Refresh(false));}catch(Exception error){System.Diagnostics.Trace.TraceWarning("Primogem refresh: "+error.Message);}finally{primogemsBusy=false;}}
 async Task RefreshCalendar(){if(calendarBusy)return;calendarBusy=true;try{using(var db=new LocalDataService(Setup.DataFolder))if(string.IsNullOrEmpty(db.GetSecret("hoyolab")))return;await new CalendarStore(Setup.DataFolder).Refresh(false);}catch(Exception error){System.Diagnostics.Trace.TraceWarning("Calendar refresh: "+error.Message);}finally{calendarBusy=false;}}
 static FrameworkElement WithCheck(FrameworkElement mark,TextBlock text){var row=new DockPanel();DockPanel.SetDock(mark,Dock.Left);row.Children.Add(mark);row.Children.Add(text);return row;}
 async Task Run(bool sign){
  if(busy)return;busy=true;claim.IsEnabled=false;if(connectionCheck.Visibility!=Visibility.Visible)connection.Text=Locale.T("로그인 확인 중…");
  try {
   string cookie;using(var db=new LocalDataService(Setup.DataFolder))cookie=db.GetSecret("hoyolab");
   if(string.IsNullOrEmpty(cookie)){connection.Text=Locale.T("로그인 필요");status.Text=Locale.T("HoYoLAB 로그인이 필요합니다.");nextCheck=DateTime.UtcNow.AddHours(1);return;}
   var data=await Task.Run(()=>{ObservationRefresh.Run(Setup.DataFolder,"default","attendance",TimeSpan.FromSeconds(Math.Max(1,Math.Min(300,DateTime.UtcNow.AddHours(8).TimeOfDay.TotalSeconds))),sign,()=>{var observed=sign?HoyoClient.Claim(cookie):HoyoClient.Attendance(cookie);using(var db=new LocalDataService(Setup.DataFolder))db.Observe("default","attendance",observed);});using(var db=new LocalDataService(Setup.DataFolder))return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(db.Recent("default","attendance",1)[0]["payload"]);});
   connection.Text=Locale.T("HoYoLAB 연결됨");connectionCheck.Visibility=Visibility.Visible;attendanceCheck.Visibility=Convert.ToBoolean(data["is_sign"])?Visibility.Visible:Visibility.Collapsed;
   string observedAt;using(var db=new LocalDataService(Setup.DataFolder))observedAt=db.Recent("default","attendance",1)[0]["observed_at"];
   status.Text=(Convert.ToBoolean(data["is_sign"])?Locale.T("오늘 출석 완료"):Locale.T("오늘 출석 전"))+Locale.T("\n이번 달 ")+Convert.ToString(data["total_sign_day"])+Locale.T("회\n")+DateTime.Parse(observedAt).ToLocalTime().ToString("g",Locale.Culture)+Locale.T(" 확인");
   if(sign&&Convert.ToBoolean(data["is_sign"])&&AppPreferences.Flag("attendanceNotifications"))await notifications.Send("attendance:"+Convert.ToString(data["today"]),Locale.T("오늘 HoYoLAB 출석을 확인했습니다."));
   nextCheck=DateTime.UtcNow.AddHours(1);
  }catch{connection.Text=Locale.T("연결 확인 필요");connectionCheck.Visibility=Visibility.Collapsed;attendanceCheck.Visibility=Visibility.Collapsed;connection.ToolTip=Locale.T("로그인 만료나 추가 인증 여부를 확인해 주세요.");nextCheck=DateTime.UtcNow.AddHours(1);}
  finally{busy=false;claim.IsEnabled=true;}
 }
 public void Dispose(){timer.Stop();redemption.Dispose();resin.Dispose();}
}
