using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

// One bounded, public statistics feed for both preparation screens. No roster is sent.
internal sealed class EndgameKnowledge {
 internal const string Url="https://lightkeepers.moe/api/static";
 internal const string FieldOrder="top,middle,bottom";
 internal const string Source="https://lightkeepers.moe/";
 readonly string root;readonly Func<string> fetch;
 internal EndgameKnowledge(string root,Func<string> fetch=null){this.root=root;this.fetch=fetch??Fetch;}
 internal static string Fetch(){var req=HttpTransport.Create(Url);req.AllowAutoRedirect=false;req.Timeout=20000;req.ReadWriteTimeout=20000;req.UserAgent="Catheryne";using(var response=req.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){var text=new StringBuilder();var buffer=new char[8192];int count;while((count=reader.Read(buffer,0,buffer.Length))>0){if(text.Length+count>3000000)throw new InvalidDataException("통계 자료가 너무 큽니다.");text.Append(buffer,0,count);}return text.ToString();}}
 internal static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
 internal static Dictionary<string,object> Map(object value){return CodexChat.Map(value);}
 internal static string S(Dictionary<string,object> d,string key){return CodexChat.S(d,key);}
 internal static double Number(Dictionary<string,object> d,string key,double max){double n;object v;if(!d.TryGetValue(key,out v)||v==null||!double.TryParse(Convert.ToString(v,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out n)||double.IsNaN(n)||double.IsInfinity(n)||n<0||n>max)throw new ArgumentException("통계 수치의 형식을 확인하지 못했습니다.");return n;}
 internal static string[] Strings(object value){var a=value as System.Collections.IEnumerable;if(a==null||value is string||value is System.Collections.IDictionary)throw new ArgumentException("목록 형식이 올바르지 않습니다.");return a.Cast<object>().Select(Convert.ToString).ToArray();}
 internal static Dictionary<string,object> Parse(string json){
  if(json.Length>3000000)throw new ArgumentException("통계 자료가 너무 큽니다.");var raw=Map(CatheryneTools.Json().DeserializeObject(json));var modes=new Dictionary<string,object>();
  foreach(string mode in new[]{"abyss","stygian"}){
   string suffix=mode=="abyss"?"Abyss":"Stygian";var version=Map(raw.ContainsKey("latest"+suffix+"Version")?raw["latest"+suffix+"Version"]:null);int id=(int)Number(version,"version_number",100000);string label=S(version,"version_name");if(label.Length<1||label.Length>80)throw new ArgumentException("통계 시즌을 확인하지 못했습니다.");
   object items;if(!raw.TryGetValue("allTeams"+suffix,out items))throw new ArgumentException("파티 통계가 없습니다.");var teams=new List<object>();int nonFull=0;
   foreach(var row in CodexChat.Items(items).Take(2000)){
    if((int)Number(row,"version_number",100000)!=id)throw new ArgumentException("다른 시즌 통계가 섞여 있습니다.");
    string[] members=Strings(row.ContainsKey("members_names")?row["members_names"]:null);if(members.Length<1||members.Length>4||members.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>100)||members.Distinct().Count()!=members.Length)throw new ArgumentException("파티 통계의 캐릭터를 확인하지 못했습니다.");
    double used=Number(row,"usage_total",100000000),owned=Number(row,"has_total",100000000),rate=Number(row,"usage_rate",100);if(used>owned||owned<=0||Math.Abs(rate-100*used/owned)>.15)throw new ArgumentException("픽률의 분모와 사용 수가 일치하지 않습니다.");
    var shares=(mode=="abyss"?new[]{1,2}:new[]{1,3,2}).Select(i=>Number(row,"field_"+i+"_rate",100)).ToArray();if(shares.Sum()>101)throw new ArgumentException("전장별 사용 비중을 확인하지 못했습니다.");
    if(members.Length<4){nonFull++;continue;}teams.Add(new Dictionary<string,object>{{"members",members},{"usageRate",rate},{"used",used},{"owned",owned},{"shares",shares}});
   }
   if(teams.Count==0)throw new ArgumentException("현재 시즌 파티 통계가 비어 있습니다.");
   object enemies;raw.TryGetValue(mode+"Enemies",out enemies);var enemyMap=Map(enemies);var schedule=mode=="stygian"?Map(raw.ContainsKey("stygianSchedule")?raw["stygianSchedule"]:null):enemyMap;
   string start=S(schedule,"openTime"),end=S(schedule,"closeTime");DateTimeOffset parsed;
   if(!DateTimeOffset.TryParse(start,CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed))throw new ArgumentException("통계 시즌 시작일을 확인하지 못했습니다.");
   bool estimated=mode=="abyss"&&end.Length==0;if(estimated)end=parsed.DateTime.AddMonths(1).ToString("o",CultureInfo.InvariantCulture);if(!DateTimeOffset.TryParse(end,CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed))throw new ArgumentException("통계 시즌 종료일을 확인하지 못했습니다.");
   DateTime began=DateTimeOffset.Parse(start,CultureInfo.InvariantCulture).DateTime;if(parsed.DateTime<=began||parsed.DateTime-began>TimeSpan.FromDays(90))throw new ArgumentException("Invalid season interval");
   modes[mode]=new Dictionary<string,object>{{"fieldOrder",mode=="stygian"?FieldOrder:"top,bottom"},{"version",label},{"versionNumber",id},{"start",start},{"end",end},{"endEstimated",estimated},{"scheduleAuthority","community_reference"},{"sourceCheckedAt",DateTime.UtcNow.ToString("o")},{"scheduleId",S(schedule,"scheduleId")},{"teams",teams},{"enemies",enemyMap},{"difficulty",null},{"server",null},{"sampleSize",null},{"nonFullTeamsExcluded",nonFull},{"truncated",CodexChat.Items(items).Count()>2000}};
  }
  return new Dictionary<string,object>{{"schema",2},{"source",Source},{"statisticsUrl",Url},{"upstream","YShelper, via Lightkeepers"},{"checkedAt",DateTime.UtcNow.ToString("o")},{"modes",modes},{"rateMeaning","usage_total / has_total: use among sampled owners of the complete team; field rates are that team's battlefield distribution. Not win probability. Overall sample size, difficulty, investment and server cohort are unconfirmed."}};
 }
 string Pointer {get{return Path.Combine(root,"endgame","knowledge.json");}}
 internal Dictionary<string,object> Read(string reference=null){var p=File.Exists(Pointer)?StoryClient.Read(Pointer):new Dictionary<string,object>();if(reference==null)reference=S(p,"reference");if(reference.Length==0)return p;if(!System.Text.RegularExpressions.Regex.IsMatch(reference,@"^[a-f0-9]{64}$"))throw new ArgumentException("자료 참조가 올바르지 않습니다.");var file=Path.Combine(root,"endgame","references",reference+".json");return File.Exists(file)?StoryClient.Read(file):new Dictionary<string,object>();}
 internal Dictionary<string,object> Prepare(bool force=false){
  using(var gate=new System.Threading.Mutex(false,GameDataCatalog.Scope("EndgameKnowledge",root))){
   bool held=false;try{try{held=gate.WaitOne(0);}catch(System.Threading.AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException("파티 통계를 가져오는 중입니다.");
    var old=Read();string server=CodexChat.S(AppPreferences.Read(root),"resinServer");if(!force&&Fresh(old,DateTime.UtcNow,server))return old;
    string retryFile=Path.Combine(root,"endgame","refresh-check.json");var retry=File.Exists(retryFile)?StoryClient.Read(retryFile):new Dictionary<string,object>();DateTime retryAt;if(!force&&DateTime.TryParse(S(retry,"retryAt"),out retryAt)&&DateTime.UtcNow<retryAt.ToUniversalTime())throw new InvalidOperationException(Locale.T("통계 자료를 갱신하지 못했습니다. 잠시 후 자동으로 다시 확인합니다."));
    try{
    var data=Parse(fetch());string hash=Hash(CatheryneTools.Json().Serialize(data));data["reference"]=hash;TheaterStorage.Write(Path.Combine(root,"endgame","references",hash+".json"),CatheryneTools.Json().Serialize(data));TheaterStorage.Write(Pointer,CatheryneTools.Json().Serialize(new{reference=hash}));TheaterStorage.Write(retryFile,"{}");return data;
    }catch{TheaterStorage.Write(retryFile,CatheryneTools.Json().Serialize(new{retryAt=DateTime.UtcNow.AddMinutes(2).ToString("o")}));throw;}
   }finally{if(held)gate.ReleaseMutex();}
  }
 }
 internal static bool Fresh(Dictionary<string,object> data,DateTime utc,string server){DateTime at;if(S(data,"schema")!="2"||!DateTime.TryParse(S(data,"checkedAt"),out at)||utc<at.ToUniversalTime())return false;bool current=true;foreach(string mode in new[]{"abyss","stygian"}){var season=Mode(data,mode);bool now=Current(season,utc,server);if(Current(season,at.ToUniversalTime(),server)!=now)return false;current&=now;}return utc-at.ToUniversalTime()<TimeSpan.FromMinutes(current?60:5);}
 internal static Dictionary<string,object> Mode(Dictionary<string,object> data,string mode){object raw;return data.TryGetValue("modes",out raw)&&Map(raw).ContainsKey(mode)?Map(Map(raw)[mode]):new Dictionary<string,object>();}
 internal static int ServerOffset(string server){return server=="os_usa"?-5:server=="os_euro"?1:8;}
 // Community timestamps encode a server wall clock even when their string includes +00:00.
 internal static bool SeasonWindow(Dictionary<string,object> mode,string server,out DateTime start,out DateTime end){
  start=end=DateTime.MinValue;DateTimeOffset first,last;if(!DateTimeOffset.TryParse(S(mode,"start"),CultureInfo.InvariantCulture,DateTimeStyles.None,out first))return false;
  DateTime finish;if(Equals(mode.ContainsKey("endEstimated")?mode["endEstimated"]:null,true))finish=first.DateTime.AddMonths(1);else {if(!DateTimeOffset.TryParse(S(mode,"end"),CultureInfo.InvariantCulture,DateTimeStyles.None,out last))return false;finish=last.DateTime;}
  int offset=ServerOffset(server);start=DateTime.SpecifyKind(first.DateTime,DateTimeKind.Utc).AddHours(-offset);end=DateTime.SpecifyKind(finish,DateTimeKind.Utc).AddHours(-offset);return end>start;
 }
 internal static bool Current(Dictionary<string,object> mode,DateTime utc,string server){DateTime start,end;return SeasonWindow(mode,server,out start,out end)&&utc.ToUniversalTime()>=start&&utc.ToUniversalTime()<end;}
}
