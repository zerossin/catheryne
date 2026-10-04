using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

internal sealed class RedeemCode {
 public string Code {get;set;}
 public string Rewards {get;set;}
 public string State {get;set;}
 public int? Retcode {get;set;}
 public string CheckedAt {get;set;}
}
internal sealed class RedeemAccount {
 public string Cookie {get;set;}
 public string Uid {get;set;}
 public string Server {get;set;}
 public string Name {get;set;}
 internal string Key {get{return Server+":"+Uid;}}
 public override string ToString(){return Name+"  "+Uid+"  "+Server;}
}
internal sealed class RedeemReport {
 public string Account {get;set;}
 public string Status {get;set;}
 public string CheckedAt {get;set;}
 public List<RedeemCode> Codes {get;set;}
}
// The feed is anonymous. Credentials are only sent to fixed HoYoverse endpoints.
internal sealed class RedemptionService {
 internal const string Feed="https://hoyo-codes.seria.moe/codes?game=genshin";
 internal const string Gift="https://genshin.hoyoverse.com/ko/gift";
 internal const string Secret="hoyo-redemption";
 readonly string root;
 readonly Func<List<RedeemCode>> fetch;
 readonly Func<RedeemAccount,string,int> exchange;
 readonly Action<CancellationToken> pause;
 static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=524288};}
 internal RedemptionService(string root,Func<List<RedeemCode>> fetch=null,Func<RedeemAccount,string,int> exchange=null,Action<CancellationToken> pause=null){this.root=root;this.fetch=fetch??Fetch;this.exchange=exchange??Exchange;this.pause=pause??(token=>{if(token.WaitHandle.WaitOne(6000))token.ThrowIfCancellationRequested();});}
 static string Read(HttpWebRequest request){request.AllowAutoRedirect=false;request.Timeout=15000;request.ReadWriteTimeout=15000;using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){var buffer=new char[524289];int count=0,n;while(count<buffer.Length&&(n=reader.Read(buffer,count,buffer.Length-count))>0)count+=n;if(count>524288)throw new InvalidDataException();return new string(buffer,0,count);}}
 internal static List<RedeemCode> ParseFeed(string text){
  var data=Json().Deserialize<Dictionary<string,object>>(text);object raw;
  if(CodexChat.S(data,"game")!="genshin"||!data.TryGetValue("codes",out raw)||!(raw is object[])&&!(raw is System.Collections.ArrayList))throw new InvalidDataException("Invalid code feed");
  var entries=((System.Collections.IEnumerable)raw).Cast<object>().ToList();if(entries.Any(x=>!(x is Dictionary<string,object>)))throw new InvalidDataException("Invalid code entry");
  var rows=entries.Cast<Dictionary<string,object>>().ToList();if(rows.Count>200)throw new InvalidDataException("Code feed too large");
  var result=new List<RedeemCode>();foreach(var row in rows){string code=CodexChat.S(row,"code").ToUpperInvariant();if(CodexChat.S(row,"game")!="genshin"||CodexChat.S(row,"status")!="OK")continue;if(!Regex.IsMatch(code,@"^[A-Z0-9]{4,40}$"))throw new InvalidDataException("Invalid code");string rewards=CodexChat.S(row,"rewards");if(rewards.Length>1000)throw new InvalidDataException();if(!result.Any(x=>x.Code==code))result.Add(new RedeemCode{Code=code,Rewards=rewards,State="unverified"});}return result;
 }
 static List<RedeemCode> Fetch(){return ParseFeed(Read(HttpTransport.Create(Feed)));}
 internal static bool HasToken(string cookie){return Regex.IsMatch(cookie??"",@"(?:^|;\s*)cookie_token(?:_v2)?=[^;\s]+")&&Regex.IsMatch(cookie??"",@"(?:^|;\s*)account_id(?:_v2)?=[^;\s]+");}
 internal static void Validate(RedeemAccount account){if(account==null||!HasToken(account.Cookie)||!Regex.IsMatch(account.Uid??"",@"^\d{9,10}$")||!Regex.IsMatch(account.Server??"",@"^os_(asia|usa|euro|cht)$"))throw new InvalidOperationException("리딤 계정 연결이 필요합니다.");}
 internal static List<RedeemAccount> Accounts(string cookie){
  if(!HasToken(cookie))throw new InvalidOperationException("리딤 계정 연결이 필요합니다.");
  var request=HttpTransport.Create("https://api-account-os.hoyoverse.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_global");request.Headers["Cookie"]=cookie;request.Referer=Gift;
  var response=Json().Deserialize<Dictionary<string,object>>(Read(request));if(!response.ContainsKey("retcode")||Convert.ToInt32(response["retcode"])!=0)throw new InvalidOperationException("리딤 계정 연결이 필요합니다.");
  var data=CodexChat.Map(response["data"]);object raw;if(!data.TryGetValue("list",out raw))throw new InvalidDataException();
  var accounts=CodexChat.Items(raw).Where(x=>CodexChat.S(x,"game_biz")=="hk4e_global").Select(x=>new RedeemAccount{Cookie=cookie,Uid=CodexChat.S(x,"game_uid"),Server=CodexChat.S(x,"region"),Name=CodexChat.S(x,"nickname")}).ToList();foreach(var account in accounts)Validate(account);return accounts;
 }
 static int Exchange(RedeemAccount account,string code){
  Validate(account);if(!Regex.IsMatch(code??"",@"^[A-Z0-9]{4,40}$"))throw new ArgumentException();
  string url="https://public-operation-hk4e.hoyoverse.com/common/apicdkey/api/webExchangeCdkey?uid="+account.Uid+"&region="+account.Server+"&lang="+(Locale.IsEnglish?"en":"ko")+"&cdkey="+Uri.EscapeDataString(code)+"&game_biz=hk4e_global";
  var request=HttpTransport.Create(url);request.Headers["Cookie"]=account.Cookie;request.Headers["Origin"]="https://genshin.hoyoverse.com";request.Referer=Gift;
  var response=Json().Deserialize<Dictionary<string,object>>(Read(request));if(!response.ContainsKey("retcode"))throw new InvalidDataException();return Convert.ToInt32(response["retcode"]);
 }
 internal static string State(int code){switch(code){case 0:return "redeemed";case -2017:case -2018:return "already";case -2001:return "expired";case -1065:case -2003:case -2004:case -2006:return "invalid";case -2008:return "region";case -2011:case -2021:return "level";case -2014:return "inactive";case -2016:case -110:case 1028:return "cooldown";case -100:case -1071:case 10001:return "login";default:return "blocked";}}
 internal static string Label(string state){switch(state){case "redeemed":return Locale.T("등록 완료");case "already":return Locale.T("이미 사용한 코드");case "expired":return Locale.T("기간 만료");case "invalid":return Locale.T("사용할 수 없는 코드");case "region":return Locale.T("서버 제한");case "level":return Locale.T("모험 등급 조건 미충족");case "inactive":return Locale.T("아직 활성화되지 않은 코드");case "cooldown":return Locale.T("잠시 후 다시 시도");case "login":return Locale.T("리딤 계정 연결이 필요합니다.");case "blocked":return Locale.T("공식 페이지에서 추가 확인이 필요합니다.");case "network":return Locale.T("연결 실패. 다음 조회 때 다시 시도합니다.");default:return Locale.T("등록 여부 미확인");}}
 internal RedeemAccount Account(){using(var db=new LocalDataService(root)){string value=db.GetSecret(Secret);return string.IsNullOrEmpty(value)?null:Json().Deserialize<RedeemAccount>(value);}}
 internal void Connect(RedeemAccount account){Validate(account);var prior=Account();if(prior==null||prior.Key!=account.Key)AppPreferences.Set("automaticRedeem",false,root);using(var db=new LocalDataService(root))db.SetSecret(Secret,Json().Serialize(account));}
 internal void Disconnect(){AppPreferences.Set("automaticRedeem",false,root);using(var db=new LocalDataService(root))db.DeleteSecret(Secret);}
 internal RedeemReport Saved(){var account=Account();using(var db=new LocalDataService(root)){var rows=db.Recent("public","redeem-feed",1);var report=new RedeemReport{Account=account==null?null:account.ToString(),Status="saved",Codes=rows.Count==0?new List<RedeemCode>():Json().Deserialize<List<RedeemCode>>(rows[0]["payload"]),CheckedAt=rows.Count==0?null:rows[0]["observed_at"]};if(account!=null)foreach(var code in report.Codes){var history=db.Recent(account.Key,"redeem:"+code.Code,1);if(history.Count>0){var result=Json().Deserialize<RedeemCode>(history[0]["payload"]);code.State=result.State;code.Retcode=result.Retcode;code.CheckedAt=result.CheckedAt;}}var issue=report.Codes.Where(c=>new[]{"login","blocked","cooldown","network"}.Contains(c.State)).OrderByDescending(c=>c.CheckedAt).FirstOrDefault();if(issue!=null)report.Status=issue.State;return report;}}
 internal bool Due(){using(var db=new LocalDataService(root)){var rows=db.Recent("default","redeem-attempt",1);return rows.Count==0||DateTime.UtcNow-DateTime.Parse(rows[0]["observed_at"]).ToUniversalTime()>=TimeSpan.FromMinutes(30);}}
 internal RedeemReport Run(bool redeem,bool automatic=false,CancellationToken token=default(CancellationToken)){
  Directory.CreateDirectory(root);FileStream lease;try{lease=new FileStream(Path.Combine(root,"redemption.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){var busy=Saved();busy.Status="busy";return busy;}
  using(lease){
   if(automatic&&(!Due()||!Enabled()))return Saved();
   var account=Account();if(redeem)Validate(account);
   if(automatic)using(var db=new LocalDataService(root))db.Observe("default","redeem-attempt",new{started=true});
   token.ThrowIfCancellationRequested();var codes=fetch();using(var db=new LocalDataService(root))db.Observe("public","redeem-feed",codes);
   var report=Saved();report.Status="refreshed";if(!redeem)return report;
   bool first=true;foreach(var code in report.Codes){
    token.ThrowIfCancellationRequested();if(automatic&&!Enabled()){report.Status="stopped";break;}
    var current=Account();if(current==null||current.Key!=account.Key||current.Cookie!=account.Cookie){report.Status="stopped";break;}
    if(new[]{"redeemed","already","expired","invalid","region"}.Contains(code.State))continue;
    if(automatic&&code.State=="level"&&!string.IsNullOrEmpty(code.CheckedAt)&&DateTime.UtcNow-DateTime.Parse(code.CheckedAt).ToUniversalTime()<TimeSpan.FromDays(1))continue;
    if(!first)pause(token);first=false;token.ThrowIfCancellationRequested();
    // Recheck after the pacing delay: turning automation off must stop the next request.
    current=Account();if((automatic&&!Enabled())||current==null||current.Key!=account.Key||current.Cookie!=account.Cookie){report.Status="stopped";break;}
    try{code.Retcode=exchange(account,code.Code);code.State=State(code.Retcode.Value);}catch(OperationCanceledException){throw;}catch{code.Retcode=null;code.State="network";}
    code.CheckedAt=DateTime.UtcNow.ToString("o");using(var db=new LocalDataService(root))db.Observe(account.Key,"redeem:"+code.Code,code);
    if(new[]{"login","blocked","cooldown","network"}.Contains(code.State)){report.Status=code.State;break;}
   }
   if(report.Status=="refreshed")report.Status="checked";return report;
  }
 }
 bool Enabled(){object enabled;return AppPreferences.Read(root).TryGetValue("automaticRedeem",out enabled)&&Equals(enabled,true);}
}
