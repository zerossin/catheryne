using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

// One login owner per window. Feature panels only request a prerequisite, never own credentials or login windows.
internal sealed class AccountConnections {
 static readonly ConditionalWeakTable<Window,AccountConnections> instances=new ConditionalWeakTable<Window,AccountConnections>();
 internal static AccountConnections For(Window window){return instances.GetValue(window,w=>new AccountConnections(w));}
 internal static readonly string[] Providers={"chatgpt","hoyolab","redemption"};
 internal static string Name(string provider){return provider=="chatgpt"?"ChatGPT":provider=="hoyolab"?"HoYoLAB":Locale.T("리딤 계정");}
 readonly Window owner;Window login;TaskCompletionSource<bool> completion;
 internal Func<bool> ChatConnected;internal Action ChatLogin;internal Func<Task> ChatLogout;
 internal event Action Changed;
 AccountConnections(Window owner){this.owner=owner;}
 internal bool Connected(string provider){if(!Providers.Contains(provider))throw new ArgumentException("Unknown account provider");if(provider=="chatgpt")return ChatConnected!=null&&ChatConnected();if(provider=="redemption")return new RedemptionService(Setup.DataFolder).Account()!=null;using(var db=new LocalDataService(Setup.DataFolder))return !string.IsNullOrWhiteSpace(db.GetSecret("hoyolab"));}
 internal async Task Disconnect(string provider){if(provider=="chatgpt"){if(ChatLogout==null)throw new InvalidOperationException(Locale.T("ChatGPT 연결을 확인해 주세요."));await ChatLogout();}else if(provider=="hoyolab")DailySettings.Disconnect(Setup.DataFolder);else if(provider=="redemption")new RedemptionService(Setup.DataFolder).Disconnect();else throw new ArgumentException("Unknown disconnect provider");if(Changed!=null)Changed();}
 internal async Task<bool> Ensure(string provider,bool reconnect=false){
  if(!reconnect&&Connected(provider))return true;
  if(MessageBox.Show(owner,Locale.Format("{0} 계정이 연결되어 있지 않습니다. 연결하시겠습니까?",Name(provider)),Locale.T("계정 연결"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return false;
  return await Connect(provider);
 }
 sealed class Choice {internal string Uid,Server,Nickname;internal RedeemAccount Redemption;public override string ToString(){return Nickname+"  "+Uid+"  "+Server;}}
 internal async Task<bool> Connect(string provider){
  if(!Providers.Contains(provider))throw new ArgumentException("Unknown account provider");
  if(provider=="chatgpt"){if(ChatLogin!=null)ChatLogin();return Connected(provider);}
  if(login!=null){login.Activate();await completion.Task;return Connected(provider);}
  bool redemption=provider=="redemption",accepted=false;var done=new TaskCompletionSource<bool>();completion=done;
  var popup=new Window{Title=Name(provider),Width=1000,Height=760,Owner=owner,Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(23,25,29))};login=popup;
  var web=new WebView2();var finish=PanelUi.Button(Locale.T("로그인 확인"));finish.IsEnabled=false;
  var hint=PanelUi.Text(Locale.T("로그인 후 계정을 선택해 주세요."),true);var choices=new ComboBox{Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,12)};
  var footer=new StackPanel{Margin=new Thickness(16)};footer.Children.Add(hint);footer.Children.Add(choices);footer.Children.Add(finish);var body=new DockPanel();DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);body.Children.Add(web);popup.Content=body;
  popup.Loaded+=async(s,e)=>{try{var environment=await CoreWebView2Environment.CreateAsync(null,System.IO.Path.Combine(Setup.DataFolder,"webview",redemption?"redemption":"hoyolab"));if(login!=popup)return;await web.EnsureCoreWebView2Async(environment);if(login!=popup)return;web.CoreWebView2.Settings.AreDevToolsEnabled=false;web.Source=new Uri(redemption?RedemptionService.Gift:"https://www.hoyolab.com/");finish.IsEnabled=true;}catch{hint.Text=Locale.T("로그인 창을 열지 못했습니다. Microsoft Edge WebView2 Runtime을 확인해 주세요.");}};
  finish.Click+=async(s,e)=>{
   finish.IsEnabled=false;
   try{
    var cookies=await web.CoreWebView2.CookieManager.GetCookiesAsync(redemption?"https://genshin.hoyoverse.com/":"https://www.hoyolab.com/");
    var allowed=redemption?new[]{"cookie_token_v2","account_id_v2","account_mid_v2","cookie_token","account_id"}:new[]{"ltoken_v2","ltuid_v2","ltmid_v2","ltoken","ltuid","cookie_token_v2","account_id_v2","account_mid_v2"};
    string cookie=string.Join("; ",cookies.Where(c=>allowed.Contains(c.Name)).Select(c=>c.Name+"="+c.Value));
    var roles=await Task.Run(()=>{
     if(redemption)return RedemptionService.Accounts(cookie).Select(r=>new Choice{Uid=r.Uid,Server=r.Server,Nickname=r.Name,Redemption=r}).ToList();
     HoyoClient.Attendance(cookie);var result=HoyoClient.Request("https://api-account-os.hoyolab.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_global",cookie);
     return CodexChat.Items(result["list"]).Where(r=>CodexChat.S(r,"game_biz")=="hk4e_global").Select(r=>new Choice{Uid=CodexChat.S(r,"game_uid"),Server=CodexChat.S(r,"region"),Nickname=CodexChat.S(r,"nickname")}).ToList();
    });if(login!=popup)return;
    var selected=choices.SelectedItem as Choice;var chosen=selected==null?(roles.Count==1?roles[0]:null):roles.SingleOrDefault(x=>x.Uid==selected.Uid&&x.Server==selected.Server);
    if(chosen==null){choices.ItemsSource=roles;choices.Visibility=Visibility.Visible;hint.Text=Locale.T("사용할 원신 계정을 선택해 주세요.");finish.IsEnabled=true;return;}
    if(redemption)new RedemptionService(Setup.DataFolder).Connect(chosen.Redemption);
    else{using(var db=new LocalDataService(Setup.DataFolder))db.SetSecret("hoyolab",cookie);AppPreferences.Set("resinUid",chosen.Uid);AppPreferences.Set("resinServer",chosen.Server);}
    accepted=true;popup.Close();if(Changed!=null)Changed();
   }catch{if(login==popup){hint.Text=Locale.T("로그인 확인에 실패했습니다. 로그인을 완료한 뒤 다시 확인해 주세요.");finish.IsEnabled=true;}}
  };
  popup.Closed+=(s,e)=>{login=null;web.Dispose();done.TrySetResult(accepted);};popup.Show();return await done.Task;
 }
}
