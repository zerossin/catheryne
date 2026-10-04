using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class PrimoIncome { public string Time,Category; public int CategoryId,Amount; }
internal sealed class PrimoMonth { public string Month,ObservedAt; public int Total; public List<PrimoIncome> Entries=new List<PrimoIncome>(); }
internal sealed class PrimoBudget { public string Version; public decimal Pulls; public bool Estimate; public decimal? Primogems; public int Days; public Dictionary<string,decimal> Categories=new Dictionary<string,decimal>(); }
internal sealed class PrimoVersion { public string Version,Start,Source; }
internal sealed class PrimoProgress { public string Key; public decimal? Total; public int? Received; }
internal sealed class PrimoReward {
 public string Id,Version,Name,Currency; public int Total; public int? Received; public bool Deleted;
}
internal sealed class PrimoPeriod {
 public string Start,End; public int? Amount; public string[] MissingMonths; public List<PrimoMonth> Months;
 public Dictionary<string,int> Categories,Days;
}
internal sealed class PrimoReport {
 public string Account,Version,SyncAt,SyncState; public PrimoPeriod Income; public PrimoBudget Budget;
 public List<PrimoReward> Rewards; public List<AchievementRow> Achievements;
 public bool AchievementCatalogAvailable; public List<PrimoProgress> Progress; public bool PeriodKnown,EndEstimated;
}

// All UI and AI projections use these same account-scoped snapshots. Ledger rows have no unique
// transaction ID: preserve each occurrence and replace a reconciled month, never append/deduplicate rows.
internal sealed class PrimogemService {
 internal const string BudgetUrl="https://docs.google.com/spreadsheets/d/1l9HPu2cAzTckdXtr7u-7D8NSKzZNUqOuvbmxERFZ_6w/edit";
 const string BudgetCsv="https://docs.google.com/spreadsheets/d/1l9HPu2cAzTckdXtr7u-7D8NSKzZNUqOuvbmxERFZ_6w/export?format=csv&gid=955728278";
 readonly string root;
 readonly Func<string,string,Dictionary<string,object>> request;
 internal PrimogemService(string root,Func<string,string,Dictionary<string,object>> request=null){this.root=root;this.request=request??((url,cookie)=>HoyoClient.Request(url,cookie));}
 internal static string Download(string url){var web=HttpTransport.Create(url);web.Timeout=15000;web.ReadWriteTimeout=15000;using(var response=web.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){var buffer=new char[2000000];int count=0,n;while(count<buffer.Length&&(n=reader.Read(buffer,count,buffer.Length-count))>0)count+=n;if(count==buffer.Length)throw new InvalidDataException("Public response too large");return new string(buffer,0,count);}}
 internal List<PrimoVersion> VersionDates(){
  string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog","versions.json");var dates=File.Exists(file)?CatheryneTools.Json().Deserialize<List<PrimoVersion>>(File.ReadAllText(file)):new List<PrimoVersion>();
  using(var db=new LocalDataService(root)){var latest=Latest<List<PrimoVersion>>(db,"public","primo-versions");if(latest!=null)dates.AddRange(latest);}return dates.GroupBy(x=>x.Version).Select(g=>g.Last()).OrderBy(x=>x.Start).ToList();
 }
 internal static List<PrimoVersion> ParseVersionDates(string markdown,IEnumerable<PrimoVersion> known=null){
  var anchors=(known??Enumerable.Empty<PrimoVersion>()).ToArray();
  var result=new List<PrimoVersion>();foreach(string section in Regex.Split(markdown,@"(?m)^# ")){
   string heading=section.Split('\n')[0];var version=Regex.Match(heading,@"Version [""“]?(?:(\d+\.\d+)|[A-Za-z]+ ([IVX]+))[^\n]*Update (?:Details|Maintenance)");
   var date=Regex.Match(section,@"(?:Update maintenance begins|begin (?:performing )?update maintenance (?:on|at))[^\d]{0,180}(20\d\d/\d\d/\d\d) (\d\d:\d\d)");
   if(!version.Success||!date.Success)continue;string name=version.Groups[1].Success?version.Groups[1].Value:null;
   DateTime parsed;if(!DateTime.TryParseExact(date.Groups[1].Value+" "+date.Groups[2].Value,"yyyy/MM/dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed))continue;
   string start=parsed.ToString("yyyy-MM-ddTHH:mm:ss");if(name==null){var explicitNumber=Regex.Match(section,@"\bVersion (\d+\.\d+)\b");if(explicitNumber.Success)name=explicitNumber.Groups[1].Value;else{var matches=anchors.Where(x=>x.Start==start).Select(x=>x.Version).Distinct().ToArray();if(matches.Length==1)name=matches[0];else continue;}}
   ValidateVersion(name);result.Add(new PrimoVersion{Version=name,Start=start,Source="https://github.com/KQM-git/GINews/blob/master/readme.md"});
  }if(result.GroupBy(x=>x.Version).Any(g=>g.Select(x=>x.Start).Distinct().Count()!=1))throw new InvalidDataException("Conflicting version dates");return result.GroupBy(x=>x.Version).Select(g=>g.First()).ToList();
 }
 internal string CurrentVersion(){var current=VersionDates().LastOrDefault(v=>string.CompareOrdinal(v.Start,DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-ddTHH:mm:ss"))<=0);return current==null?null:current.Version;}
 internal PrimoBudget DetailedBudget(string version){using(var db=new LocalDataService(root))return Latest<PrimoBudget>(db,"public","primo-version-budget:"+version);}
 internal void RefreshBudget(string version,bool force){
  ValidateVersion(version);var summary=Budgets().FirstOrDefault(x=>x.Version==version);if(summary==null)return;
  ObservationRefresh.Run(root,"public","primo-version-budget:"+version,TimeSpan.FromDays(1),force,()=>{
   string sheet=version+(summary.Estimate?" est.":"");var budget=ParseDetailedBudget(Download(BudgetUrl.Replace("/edit","/gviz/tq?tqx=out:csv&sheet=")+Uri.EscapeDataString(sheet)),version,summary.Estimate);
   using(var db=new LocalDataService(root))db.Observe("public","primo-version-budget:"+version,budget);
  });
 }
 internal static PrimoBudget ParseDetailedBudget(string csv,string version,bool estimate){
  var rows=Csv(csv);int header=rows.FindIndex(r=>r.Any(c=>c=="Gacha Primogems"||c=="Primogems"));if(header<0)throw new InvalidDataException("Primogem column missing");int column=rows[header].FindIndex(c=>c=="Gacha Primogems"||c=="Primogems");
  var budget=new PrimoBudget{Version=version,Estimate=estimate};string section="";bool totalFound=false;
  foreach(var row in rows.Skip(header+1)){
   if(row.Count<=Math.Max(1,column))continue;string name=row[1].Trim();if(row[0].Trim().Length>0)section=row[0].Trim();
   decimal amount=0;if(row[column].Trim().Length>0&&!decimal.TryParse(row[column].Replace(",",""),NumberStyles.Float,CultureInfo.InvariantCulture,out amount))throw new InvalidDataException("Invalid primogem amount");
   if(amount<0||amount>1000000)throw new InvalidDataException("Invalid primogem amount");
   if(name=="Total F2P"){budget.Primogems=amount;totalFound=true;break;}
   if(name.Contains("Paid Bonus")||name.StartsWith("Welkin"))continue;
   var days=Regex.Match(name,@"Daily Resin/Commissions x(\d+)");if(days.Success)budget.Days=int.Parse(days.Groups[1].Value);
   if(amount==0)continue;string key=section=="Events"?"events":section=="Other New Content"?"permanent":section=="Web, Mail, Apologems"?"mail":days.Success?"daily":name.StartsWith("Abyss")?"endgame":name=="Battle Pass - F2P"?"battlepass":null;
   if(key==null)throw new InvalidDataException("Unrecognized primogem budget category");if(!budget.Categories.ContainsKey(key))budget.Categories[key]=0;budget.Categories[key]+=amount;
  }
  if(!totalFound||!budget.Primogems.HasValue||Math.Abs(budget.Categories.Values.Sum()-budget.Primogems.Value)>0.1m||budget.Days<1||budget.Days>90)throw new InvalidDataException("Incomplete primogem budget");return budget;
 }
 internal static string Category(string label){
  // Category IDs change across diary versions. Match verified labels; never guess unknown income.
  switch(label){
   case "이벤트 보상":case "Event Rewards":return "events";
   case "이메일 보상":case "Mail Rewards":return "mail";
   case "일일 의뢰 보상":case "Daily Commission Rewards":return "daily";
   case "나선 비경 보상":case "Spiral Abyss Rewards":case "Imaginarium Theater Rewards":case "Stygian Onslaught Rewards":case "현실 속 환상극 보상":case "지맥 제압전 보상":return "endgame";
   case "기행 보상":case "Battle Pass Rewards":return "battlepass";
   case "워프 포인트 개방 보상":case "보물상자 보상":case "튜토리얼 읽기 보상":case "임무":case "모험":case "업적 보상":case "Quest Rewards":case "Achievement Rewards":case "Chest Rewards":case "Unlocking Teleport Waypoints":return "permanent";
   default:return "other";
  }
 }
 internal static List<PrimoProgress> Progress(PrimoBudget budget,PrimoPeriod income){
  var totals=budget==null?new Dictionary<string,decimal>():budget.Categories;var received=income.Categories.GroupBy(x=>Category(x.Key)).ToDictionary(g=>g.Key,g=>g.Sum(x=>x.Value));
  return totals.Keys.Union(received.Keys).Select(k=>new PrimoProgress{Key=k,Total=totals.ContainsKey(k)?totals[k]:(decimal?)null,Received=income.Amount.HasValue?(int?)(received.ContainsKey(k)?received[k]:0):null}).OrderByDescending(x=>x.Total.HasValue).ThenByDescending(x=>x.Total).ThenBy(x=>x.Key).ToList();
 }
 internal PrimoReport ReadVersion(string version){
  if(string.IsNullOrEmpty(version))version=CurrentVersion();ValidateVersion(version);var dates=VersionDates();var current=dates.FirstOrDefault(x=>x.Version==version);var budget=DetailedBudget(version);DateTime start,end;
  if(current==null||!DateTime.TryParseExact(current.Start,"yyyy-MM-ddTHH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out start)){
   var unknown=Read(version,DiaryToday,DiaryToday);unknown.Budget=budget;unknown.Income=new PrimoPeriod{Amount=null,MissingMonths=new string[0],Months=new List<PrimoMonth>(),Categories=new Dictionary<string,int>(),Days=new Dictionary<string,int>()};unknown.Progress=Progress(budget,unknown.Income);return unknown;
  }
  var next=dates.FirstOrDefault(x=>string.CompareOrdinal(x.Start,current.Start)>0);DateTime nextStart;bool knownEnd=next!=null&&DateTime.TryParse(next.Start,out nextStart)&&(nextStart-start).TotalDays<=(budget!=null&&budget.Days>0?budget.Days+7:49);
  if(knownEnd)end=DateTime.Parse(next.Start,CultureInfo.InvariantCulture);else if(budget!=null&&budget.Days>0)end=start.AddDays(budget.Days);else end=DateTime.UtcNow.AddHours(8);
  DateTime until=end<DateTime.UtcNow.AddHours(8)?end:DateTime.UtcNow.AddHours(8);var report=Read(version,start.Date,until<start?start.Date:until.Date);report.Budget=budget;report.PeriodKnown=true;report.EndEstimated=!knownEnd;
  report.Income=IncomeBetween(report.Account,start,until<start?start:until);report.Income.End=end.ToString("yyyy-MM-dd HH:mm:ss");if(start>DateTime.UtcNow.AddHours(8))report.Income.Amount=null;report.Progress=Progress(budget,report.Income);return report;
 }
 internal static DateTime DiaryToday {get{return DateTime.UtcNow.AddHours(8).Date;}}
 internal string Account(){var prefs=AppPreferences.Read(root);string uid=CodexChat.S(prefs,"resinUid"),server=CodexChat.S(prefs,"resinServer");return Regex.IsMatch(uid,@"^\d{9,10}$")&&new[]{"os_asia","os_euro","os_usa","os_cht"}.Contains(server)?server+":"+uid:null;}
 internal static void ValidateVersion(string version){if(!Regex.IsMatch(version??"",@"^\d{1,2}\.\d{1,2}$"))throw new ArgumentException("버전은 7.1처럼 입력해 주세요.");}
 static string MonthKey(DateTime month){return "primo-month:"+month.ToString("yyyy-MM",CultureInfo.InvariantCulture);}
 static T Latest<T>(LocalDataService db,string profile,string kind) where T:class {var rows=db.Recent(profile,kind,1);return rows.Count==0?null:CatheryneTools.Json().Deserialize<T>(rows[0]["payload"]);}
 internal List<PrimoBudget> Budgets(){using(var db=new LocalDataService(root))return Latest<List<PrimoBudget>>(db,"public","primo-budget")??new List<PrimoBudget>();}
 internal string[] Versions(){
  var values=Budgets().Select(b=>b.Version).ToList();values.AddRange(VersionDates().Select(v=>v.Version));try{values.AddRange(AchievementCatalog.Load(root,false).Select(a=>a.Version));}catch{/* An unavailable optional catalog must not hide the ledger or public budgets. */}
  return values.Where(v=>Regex.IsMatch(v??"",@"^\d{1,2}\.\d{1,2}$")).Distinct().OrderByDescending(v=>new Version(v)).ToArray();
 }
 internal void RefreshCatalog(bool force){
  ObservationRefresh.Run(root,"public","primo-budget",TimeSpan.FromDays(1),force,()=>{
   string csv=Download(BudgetCsv);
   var budget=ParseBudgets(csv);using(var db=new LocalDataService(root))db.Observe("public","primo-budget",budget);
  });
  ObservationRefresh.Run(root,"public","primo-versions",TimeSpan.FromDays(1),force,()=>{var dates=ParseVersionDates(Download("https://raw.githubusercontent.com/KQM-git/GINews/master/readme.md"),VersionDates());if(dates.Count==0)throw new InvalidDataException("Version dates unavailable");using(var db=new LocalDataService(root))db.Observe("public","primo-versions",dates);});
 }
 internal static List<PrimoBudget> ParseBudgets(string csv){
  // The public table is a pulls estimate, not a list of claimable primogem rewards.
  var rows=Csv(csv);int header=rows.FindIndex(r=>r.Contains("Commissions / Free BP")&&r.Contains("New Permanent Content"));
  if(header<0)throw new InvalidDataException("Budget columns changed");
  string[] names={"Commissions / Free BP","Abyss / Theater / Stygian","Paimon's Shop","Version Events","New Permanent Content","Apologems / Web Events / Mail"};
  var indexes=names.Select(n=>rows[header].IndexOf(n)).ToArray();if(indexes.Any(i=>i<0))throw new InvalidDataException("Budget columns missing");
  var result=new List<PrimoBudget>();foreach(var row in rows.Skip(header+1)){
   if(row.Count<=indexes.Max())continue;var match=Regex.Match(row[0].Trim(),@"^(\d{1,2}\.\d{1,2})(\s+est\.)?$");if(!match.Success)continue;
   decimal total=0;foreach(int i in indexes){decimal value;if(!decimal.TryParse(row[i],NumberStyles.Float,CultureInfo.InvariantCulture,out value)||value<0||value>10000)throw new InvalidDataException("Invalid budget amount");total+=value;}
   result.Add(new PrimoBudget{Version=match.Groups[1].Value,Pulls=total,Estimate=match.Groups[2].Success});
  }
  if(result.Count==0||result.Select(x=>x.Version).Distinct().Count()!=result.Count)throw new InvalidDataException("Invalid budget versions");return result;
 }
 static List<List<string>> Csv(string text){var rows=new List<List<string>>();var row=new List<string>();var cell=new StringBuilder();bool quoted=false;
  for(int i=0;i<text.Length;i++){char c=text[i];if(c=='"'){if(quoted&&i+1<text.Length&&text[i+1]=='"'){cell.Append('"');i++;}else quoted=!quoted;}else if(!quoted&&(c==','||c=='\n')){row.Add(cell.ToString().TrimEnd('\r'));cell.Clear();if(c=='\n'){rows.Add(row);row=new List<string>();}}else cell.Append(c);}
  if(quoted)throw new InvalidDataException("Incomplete CSV");if(cell.Length>0||row.Count>0){row.Add(cell.ToString().TrimEnd('\r'));rows.Add(row);}return rows;
 }
 internal List<AchievementRow> Achievements(string version,out bool available){
  try{var rows=AchievementCatalog.Load(root,false);available=true;var states=new ProfileStore(root).AchievementStates();foreach(var row in rows){int state;row.State=states.TryGetValue(row.Id,out state)?state:0;}return rows.Where(x=>x.Version==version).ToList();}catch{available=false;return new List<AchievementRow>();}
 }
 internal void Refresh(bool force){
  string cookie;using(var db=new LocalDataService(root))cookie=db.GetSecret("hoyolab");if(string.IsNullOrEmpty(cookie))throw new InvalidOperationException("상단 계정에서 HoYoLAB을 연결해 주세요.");
  HoyoClient.Account(cookie,root);string account=Account();if(account==null)throw new InvalidOperationException("상단 계정에서 사용할 원신 계정을 선택해 주세요.");
  ObservationRefresh.Run(root,account,"primo-sync",TimeSpan.FromHours(6),force,()=>{
   if(!force&&ObservationRefresh.Fresh(root,account,"primo-attempt",TimeSpan.FromHours(6)))return;
   // Shared with the daily background host; a second process cannot replace a month mid-refresh.
   Directory.CreateDirectory(root);FileStream lease;try{lease=new FileStream(Path.Combine(root,"primogems.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){throw new InvalidOperationException("원석 기록을 갱신 중입니다.");}
   using(lease){using(var db=new LocalDataService(root))db.Observe(account,"primo-attempt",new{state="running"});
    var errors=new List<string>();DateTime today=DiaryToday;
    for(int i=2;i>=0;i--){DateTime month=new DateTime(today.Year,today.Month,1).AddMonths(-i);
     try{if(Account()!=account)throw new InvalidOperationException("Account changed");var snapshot=FetchMonth(account,cookie,month);StoreMonth(account,snapshot);}
     catch{errors.Add(month.ToString("yyyy-MM"));}
    }
    using(var db=new LocalDataService(root))db.Observe(account,"primo-sync",new{state=errors.Count==0?"ok":"partial",missing=errors.ToArray()});
    if(errors.Count>0)throw new InvalidOperationException(Locale.Format("일기를 불러오지 못한 달: {0}. 기존 기록은 유지됩니다. HoYoLAB 연결을 확인해 주세요.",string.Join(", ",errors)));
   }
  });
 }
 string Url(string account,DateTime month,bool detail,int page){var parts=account.Split(':');return "https://sg-hk4e-api.hoyolab.com/event/ysledgeros/"+(detail?"month_detail":"month_info")+"?uid="+parts[1]+"&region="+parts[0]+"&month="+month.Month+"&lang="+(Locale.IsEnglish?"en-us":"ko-kr")+(detail?"&type=1&current_page="+page+"&page_size=100":"");}
 static int Integer(Dictionary<string,object> data,string key){object value;int number;if(!data.TryGetValue(key,out value)||value==null||!int.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.Integer,CultureInfo.InvariantCulture,out number)||number<0)throw new InvalidDataException("Invalid ledger number");return number;}
 static void Identity(Dictionary<string,object> data,string account,DateTime month){var parts=account.Split(':');if(CodexChat.S(data,"uid")!=parts[1]||CodexChat.S(data,"region")!=parts[0]||Integer(data,"data_month")!=month.Month)throw new InvalidDataException("Ledger account or month mismatch");}
 internal PrimoMonth FetchMonth(string account,string cookie,DateTime month){
  var watch=System.Diagnostics.Stopwatch.StartNew();var summary=request(Url(account,month,false,0),cookie);Identity(summary,account,month);int total=Integer(CodexChat.Map(summary["month_data"]),"current_primogems");
  var result=new PrimoMonth{Month=month.ToString("yyyy-MM"),Total=total,ObservedAt=DateTime.UtcNow.ToString("o")};string firstPage=null;
  for(int page=1;page<=100;page++){
   if(watch.Elapsed>TimeSpan.FromSeconds(90))throw new InvalidDataException("Ledger timed out");
   var data=request(Url(account,month,true,page),cookie);Identity(data,account,month);object raw;if(!data.TryGetValue("list",out raw)||!(raw is System.Collections.IEnumerable)||raw is string)throw new InvalidDataException("Ledger list missing");
   var array=raw as System.Collections.IList;if(array==null||array.Cast<object>().Any(x=>!(x is Dictionary<string,object>)))throw new InvalidDataException("Invalid ledger list");var entries=CodexChat.Items(raw).ToList();if(entries.Count>100)throw new InvalidDataException("Ledger page too large");if(page==1)firstPage=CatheryneTools.Json().Serialize(raw);
   foreach(var entry in entries){DateTime at;if(!DateTime.TryParseExact(CodexChat.S(entry,"time"),"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out at)||at.Year!=month.Year||at.Month!=month.Month)throw new InvalidDataException("Ledger timestamp mismatch");string category=CodexChat.S(entry,"action");if(string.IsNullOrWhiteSpace(category)||category.Length>200)throw new InvalidDataException("Ledger category missing");result.Entries.Add(new PrimoIncome{Time=at.ToString("yyyy-MM-dd HH:mm:ss"),Category=category,CategoryId=Integer(entry,"action_id"),Amount=Integer(entry,"num")});}
   if(entries.Count<100)break;if(page==100)throw new InvalidDataException("Ledger page limit");
  }
  var after=request(Url(account,month,false,0),cookie);Identity(after,account,month);
  if(Integer(CodexChat.Map(after["month_data"]),"current_primogems")!=total||result.Entries.Sum(e=>(long)e.Amount)!=total)throw new InvalidDataException("Ledger totals changed or incomplete");
  var head=request(Url(account,month,true,1),cookie);Identity(head,account,month);if(!head.ContainsKey("list")||CatheryneTools.Json().Serialize(head["list"])!=firstPage)throw new InvalidDataException("Ledger pages changed");
  return result;
 }
 internal void StoreMonth(string account,PrimoMonth month){using(var db=new LocalDataService(root))db.Observe(account,"primo-month:"+month.Month,month);}
 internal PrimoPeriod Income(string account,DateTime start,DateTime end){
  return IncomeBetween(account,start.Date,end.Date.AddDays(1));
 }
 internal PrimoPeriod IncomeBetween(string account,DateTime start,DateTime end){
  if(end<start||(end-start).TotalDays>367)throw new ArgumentException("조회 기간은 1년 이내로 선택해 주세요.");
  var months=new List<PrimoMonth>();var missing=new List<string>();using(var db=new LocalDataService(root))for(var month=new DateTime(start.Year,start.Month,1);month<end;month=month.AddMonths(1)){var found=account==null?null:Latest<PrimoMonth>(db,account,MonthKey(month));if(found==null)missing.Add(month.ToString("yyyy-MM"));else months.Add(found);}
  string from=start.ToString("yyyy-MM-dd HH:mm:ss"),to=end.ToString("yyyy-MM-dd HH:mm:ss");var entries=months.SelectMany(m=>m.Entries).Where(e=>string.CompareOrdinal(e.Time,from)>=0&&string.CompareOrdinal(e.Time,to)<0).ToList();
  return new PrimoPeriod{Start=from,End=end.ToString("yyyy-MM-dd"),Amount=months.Count==0?(int?)null:entries.Sum(e=>e.Amount),MissingMonths=missing.ToArray(),Months=months,Categories=entries.GroupBy(e=>e.Category).ToDictionary(g=>g.Key,g=>g.Sum(e=>e.Amount)),Days=entries.GroupBy(e=>e.Time.Substring(0,10)).OrderByDescending(g=>g.Key).ToDictionary(g=>g.Key,g=>g.Sum(e=>e.Amount))};
 }
 internal List<PrimoReward> Rewards(string account,string version){if(account==null)return new List<PrimoReward>();using(var db=new LocalDataService(root))return db.Query("SELECT payload FROM observations WHERE profile="+LocalDataService.Sql(account)+" AND kind='primo-reward' AND id IN (SELECT MAX(id) FROM observations WHERE profile="+LocalDataService.Sql(account)+" AND kind='primo-reward' GROUP BY json_extract(payload,'$.Id')) ORDER BY id").Select(r=>CatheryneTools.Json().Deserialize<PrimoReward>(r["payload"])).Where(r=>!r.Deleted&&r.Version==version).ToList();}
 internal void SaveReward(string account,PrimoReward reward){
  if(account==null||account!=Account())throw new InvalidOperationException("상단 계정에서 사용할 원신 계정을 선택해 주세요.");ValidateVersion(reward.Version);
  if(string.IsNullOrWhiteSpace(reward.Name)||reward.Name.Length>100||!new[]{"primogem","intertwined","acquaint","crystal"}.Contains(reward.Currency)||reward.Total<1||reward.Total>1000000||reward.Received<0||reward.Received>reward.Total)throw new ArgumentException("보상 이름과 수량을 확인해 주세요.");
  if(string.IsNullOrEmpty(reward.Id))reward.Id=Guid.NewGuid().ToString("N");else {Guid id;if(!Guid.TryParseExact(reward.Id,"N",out id)||!Rewards(account,reward.Version).Any(x=>x.Id==reward.Id))throw new ArgumentException("Unknown reward");}
  using(var db=new LocalDataService(root))db.Observe(account,"primo-reward",reward);
 }
 internal PrimoReport Read(string version,DateTime start,DateTime end){
  if(!string.IsNullOrEmpty(version))ValidateVersion(version);string account=Account();bool available;var achievements=Achievements(version,out available);var report=new PrimoReport{Account=account,Version=version,Income=Income(account,start,end),Budget=Budgets().FirstOrDefault(b=>b.Version==version),Rewards=Rewards(account,version),Achievements=achievements,AchievementCatalogAvailable=available};
  using(var db=new LocalDataService(root)){var rows=account==null?new List<Dictionary<string,string>>():db.Recent(account,"primo-sync",1);if(rows.Count>0){report.SyncAt=rows[0]["observed_at"];report.SyncState=CodexChat.S(CatheryneTools.Json().Deserialize<Dictionary<string,object>>(rows[0]["payload"]),"state");}}
  return report;
 }
 internal object Query(string query){var parts=(query??"").Split('|');string version=parts[0].Trim();if(string.IsNullOrEmpty(version))version=CurrentVersion();if(string.IsNullOrEmpty(version))return new{versions=Versions()};if(parts.Length==1)return ReadVersion(version);DateTime start,end;if(parts.Length!=3||!DateTime.TryParseExact(parts[1],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out start)||!DateTime.TryParseExact(parts[2],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out end))throw new ArgumentException("Expected version or version|start|end");return Read(version,start,end);}
}
