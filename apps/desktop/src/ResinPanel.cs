using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Web.Script.Serialization;

internal sealed class ResinPanel : IDisposable {
 internal readonly StackPanel View=new StackPanel();
 readonly TextBox uid=new TextBox(),threshold=new TextBox{Text="160"};
 readonly ComboBox server=new ComboBox {ItemsSource=new[]{"os_asia","os_usa","os_euro","os_cht"},SelectedIndex=0};
 readonly PasswordBox webhook=new PasswordBox();
 readonly TextBlock status=PanelUi.Text(Locale.T("아직 확인하지 않았습니다."),true);
 readonly CheckBox monitor=new CheckBox{Content=Locale.T("레진 알림 사용")},discord=new CheckBox{Content=Locale.T("Discord 알림 사용")};
 readonly SettingsAutoSave settingsSave;
 readonly DispatcherTimer timer=new DispatcherTimer();
 readonly DailyNotifications notifications=new DailyNotifications();
 bool busy,binding;
 internal ResinPanel(){
  var p=AppPreferences.Read();if(p.ContainsKey("resinUid"))uid.Text=Convert.ToString(p["resinUid"]);if(p.ContainsKey("resinServer"))server.SelectedItem=Convert.ToString(p["resinServer"]);if(p.ContainsKey("resinThreshold"))threshold.Text=Convert.ToString(p["resinThreshold"]);
  monitor.IsChecked=AppPreferences.Flag("resinMonitor");discord.IsChecked=AppPreferences.Flag("discordNotifications");
  View.Children.Add(PanelUi.Section(Locale.T("레진"),status));
  var delivery=new StackPanel();delivery.Children.Add(discord);delivery.Children.Add(PanelUi.InputField(Locale.T("Discord 웹훅"),webhook));webhook.ToolTip=Locale.T("변경할 때만 입력합니다. 암호화해 저장합니다.");
  View.Children.Add(PanelUi.Section(Locale.T("알림 설정"),PanelUi.InputField(Locale.T("알림 레진"),threshold),monitor,PanelUi.Details("Discord",delivery)));
  settingsSave=new SettingsAutoSave(Save);settingsSave.Attach(View,()=>!binding);
  View.IsVisibleChanged+=async(s,e)=>{if(!View.IsVisible)return;binding=true;try{var saved=AppPreferences.Read();uid.Text=CodexChat.S(saved,"resinUid");server.SelectedItem=CodexChat.S(saved,"resinServer");threshold.Text=saved.ContainsKey("resinThreshold")?Convert.ToString(saved["resinThreshold"]):"160";monitor.IsChecked=AppPreferences.Flag("resinMonitor");discord.IsChecked=AppPreferences.Flag("discordNotifications");}finally{binding=false;}status.Text=HoyoNotes.SavedStatus(Setup.DataFolder,uid.Text,Convert.ToString(server.SelectedItem));await Refresh(false);};
  timer.Interval=TimeSpan.FromMinutes(10);timer.Tick+=async(s,e)=>{if(View.IsVisible||AppPreferences.Flag("resinMonitor"))await Refresh(false);};timer.Start();
 }
 void Save(){
  int n;if(!int.TryParse(threshold.Text,out n)||n<1||n>200){status.Text=Locale.T("1~200 사이의 알림 기준을 확인해 주세요.");return;}
  if(webhook.Password.Length>0){Uri uri;if(!Uri.TryCreate(webhook.Password,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="discord.com"||!uri.AbsolutePath.StartsWith("/api/webhooks/")||uri.UserInfo.Length>0){status.Text=Locale.T("Discord 웹훅 주소를 확인해 주세요.");return;}using(var db=new LocalDataService(Setup.DataFolder))db.SetSecret("discord",webhook.Password);webhook.Clear();}
  AppPreferences.Set("resinThreshold",n);AppPreferences.Set("resinMonitor",monitor.IsChecked==true);AppPreferences.Set("discordNotifications",discord.IsChecked==true);status.Text=Locale.T("저장됨");
 }
 async Task Refresh(bool force=true){
  if(busy)return;busy=true;
  try {
   var p=AppPreferences.Read();
   string cookie;using(var db=new LocalDataService(Setup.DataFolder))cookie=db.GetSecret("hoyolab");if(string.IsNullOrEmpty(cookie)){status.Text=Locale.T("HoYoLAB 로그인이 필요합니다.");return;}
   p=await Task.Run(()=>HoyoClient.Account(cookie,Setup.DataFolder));string id=Convert.ToString(p["resinUid"]),region=Convert.ToString(p["resinServer"]);binding=true;try{uid.Text=id;server.SelectedItem=region;}finally{binding=false;}
   var summary=await Task.Run(()=>HoyoNotes.Refresh(Setup.DataFolder,cookie,id,region,force));int current=Convert.ToInt32(summary["current"]),maximum=Convert.ToInt32(summary["maximum"]);
   status.Text=HoyoNotes.SavedStatus(Setup.DataFolder,id,region);
   int limit=p.ContainsKey("resinThreshold")?Convert.ToInt32(p["resinThreshold"]):160;
   if(AppPreferences.Flag("resinMonitor")&&current>=limit)await notifications.Send("resin:"+region+":"+id,Locale.T("레진이 ")+current+" / "+maximum+Locale.T("입니다."));else if(current<limit)using(var db=new LocalDataService(Setup.DataFolder))db.Execute("DELETE FROM notification_receipts WHERE event_key="+LocalDataService.Sql("resin:"+region+":"+id));
  }catch{status.Text=HoyoNotes.SavedStatus(Setup.DataFolder,uid.Text,Convert.ToString(server.SelectedItem))+"\n"+Locale.T("갱신하지 못했습니다. 계정 연결을 확인해 주세요.");}
  finally{busy=false;}
 }
 public void Dispose(){settingsSave.Dispose();timer.Stop();}
}

internal static class HoyoNotes {
 internal static Dictionary<string,object> Refresh(string root,string cookie,string uid,string server,bool force=true){ObservationRefresh.Run(root,uid,"daily-note",TimeSpan.FromMinutes(5),force,()=>Save(root,uid,server,Record(cookie,uid,server,"dailyNote")));return (Dictionary<string,object>)Latest(root,uid,server)["value"];}
 internal static Dictionary<string,object> Save(string root,string uid,string server,Dictionary<string,object> notes){
  var now=DateTime.UtcNow;var summary=new Dictionary<string,object>{{"current",notes["current_resin"]},{"maximum",notes["max_resin"]},{"fullAt",now.AddSeconds(Convert.ToInt32(notes["resin_recovery_time"])).ToString("o")},{"server",server}};
  using(var db=new LocalDataService(root)){db.Execute("BEGIN IMMEDIATE");try{db.Observe(uid,"daily-note",notes);db.Observe(uid,"resin",summary);db.Execute("COMMIT");}catch{db.Execute("ROLLBACK");throw;}}
  return summary;
 }
 internal static Dictionary<string,object> Latest(string root,string uid,string server){
  using(var db=new LocalDataService(root)){
   var rows=db.Query("SELECT observed_at,payload FROM observations WHERE profile="+LocalDataService.Sql(uid)+" AND kind='resin' AND json_extract(payload,'$.server')="+LocalDataService.Sql(server)+" ORDER BY id DESC LIMIT 1");
   if(rows.Count==0)return null;
   return new Dictionary<string,object>{{"observed_at",rows[0]["observed_at"]},{"value",CatheryneTools.Json().Deserialize<Dictionary<string,object>>(rows[0]["payload"])}};
  }
 }
 internal static string SavedStatus(string root,string uid,string server){
  var latest=Latest(root,uid,server);if(latest==null)return Locale.T("아직 확인하지 않았습니다.");
  var data=(Dictionary<string,object>)latest["value"];
  return Locale.T("레진 ")+Convert.ToString(data["current"])+" / "+Convert.ToString(data["maximum"])+Locale.T("\n충전 완료 예정: ")+DateTime.Parse(Convert.ToString(data["fullAt"])).ToLocalTime().ToString("g",Locale.Culture)+"\n"+DateTime.Parse(Convert.ToString(latest["observed_at"])).ToLocalTime().ToString("g",Locale.Culture)+Locale.T(" 확인");
 }
 internal static Dictionary<string,object> Record(string cookie,string uid,string server,string endpoint){
  return HoyoRecord.Request(cookie,uid,server,endpoint);
 }
}

// Shared delivery path: the receipt is recorded before optional remote delivery.
internal sealed class DailyNotifications {
 internal async Task Send(string key,string message){
  var target=new NotificationTarget(key.StartsWith("resin:",StringComparison.Ordinal)?"Resin":"Daily");
  if(!await AppNotifications.Current.Send(new AppNotification(key,"Catheryne",message,target)))return;
  if(!AppPreferences.Flag("discordNotifications"))return;
  string hook;using(var db=new LocalDataService(Setup.DataFolder))hook=db.GetSecret("discord");
  if(string.IsNullOrEmpty(hook))return;
  try{await Task.Run(()=>{var request=HttpTransport.Create(hook);request.Method="POST";request.AllowAutoRedirect=false;request.Timeout=10000;request.ReadWriteTimeout=10000;request.ContentType="application/json";var body=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new{content=message,allowed_mentions=new{parse=new string[0]}}));using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);using(var response=request.GetResponse()){};});}
  catch{using(var db=new LocalDataService(Setup.DataFolder))db.Observe("default","notification",new {channel="discord",delivered=false,at=DateTime.UtcNow.ToString("o")});}
 }
}
