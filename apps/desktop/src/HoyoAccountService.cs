using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

// Read-only official game-record endpoints; shared by notes, calendar and account refresh.
internal static class HoyoRecord {
 internal static Dictionary<string,object> Request(string cookie,string uid,string server,string endpoint,Dictionary<string,object> extra=null,string language=null){
  if(!new[]{"dailyNote","role_combat","hard_challenge","act_calendar","character/list","character/detail","achievement"}.Contains(endpoint))throw new ArgumentException("Invalid endpoint");
  if(!System.Text.RegularExpressions.Regex.IsMatch(uid??"",@"^\d{9,10}$")||!System.Text.RegularExpressions.Regex.IsMatch(server??"",@"^os_(asia|usa|euro|cht)$"))throw new ArgumentException("HoYoLAB에서 원신 계정을 선택해 주세요.");
  bool post=endpoint!="dailyNote"&&endpoint!="role_combat"&&endpoint!="hard_challenge";
  var req=HttpTransport.Create("https://sg-public-api.hoyolab.com/event/game_record/genshin/api/"+endpoint+(post?"":"?role_id="+uid+"&server="+server+((endpoint=="role_combat"||endpoint=="hard_challenge")?"&need_detail=true":"")));
  req.Timeout=20000;req.ReadWriteTimeout=20000;req.AllowAutoRedirect=false;req.UserAgent="Catheryne";req.Referer="https://act.hoyolab.com/";req.Headers["Cookie"]=cookie;
  req.Headers["x-rpc-app_version"]="1.5.0";req.Headers["x-rpc-client_type"]="5";req.Headers["x-rpc-language"]=language??(Locale.IsEnglish?"en-us":"ko-kr");
  string stamp=((long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds).ToString(CultureInfo.InvariantCulture),random=Guid.NewGuid().ToString("N").Substring(0,6),digest;
  using(var md5=MD5.Create())digest=BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes("salt=6s25p5ox5y14umn1p61aqyyvbvvl3lrt&t="+stamp+"&r="+random))).Replace("-","").ToLowerInvariant();req.Headers["DS"]=stamp+","+random+","+digest;
  if(post){var data=new Dictionary<string,object>{{"role_id",uid},{"server",server}};if(extra!=null)foreach(var p in extra){if(p.Key!="character_ids")throw new ArgumentException("Invalid record parameter");data[p.Key]=p.Value;}req.Method="POST";req.ContentType="application/json";byte[] bytes=Encoding.UTF8.GetBytes(CatheryneTools.Json().Serialize(data));using(var stream=req.GetRequestStream())stream.Write(bytes,0,bytes.Length);}
  using(var response=req.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){var result=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());if(Convert.ToInt32(result["retcode"])!=0)throw new InvalidOperationException(Locale.Format("HoYoLAB 조회에 실패했습니다 ({0}). 연결 상태와 전적 공개 설정을 확인해 주세요.",result["retcode"]));return CodexChat.Map(result["data"]);}
 }
}

internal sealed class HoyoAccountService {
 internal delegate Dictionary<string,object> Fetch(string cookie,string uid,string server,string endpoint,Dictionary<string,object> extra,string language);
 readonly string root;readonly Fetch fetch;
 internal HoyoAccountService(string root,Fetch fetch=null){this.root=root;this.fetch=fetch??HoyoRecord.Request;}
 internal static string Summary(string root){
  Dictionary<string,object> reference;var snapshot=new CatheryneTools(root).AccountSnapshot(out reference);object raw;
  if(!snapshot.TryGetValue("catheryne",out raw))return "";var meta=CodexChat.Map(raw);if(CodexChat.S(meta,"hoyolabObservedAt")=="")return "";
  DateTime when;string result=DateTime.TryParse(CodexChat.S(meta,"hoyolabObservedAt"),out when)?Locale.Format("최근 HoYoLAB 조회 {0}",when.ToLocalTime().ToString("g",Locale.Culture)):"";
  if(meta.TryGetValue("achievementSummary",out raw)){string count=CodexChat.S(CodexChat.Map(raw),"achievement_num");if(count.Length>0)result+="\n"+Locale.Format("HoYoLAB 업적 달성 {0}개",count);}
  if(meta.TryGetValue("unmappedCount",out raw)&&Convert.ToInt32(raw)>0)result+="\n"+Locale.Format("외부 도구용 키가 없는 항목 {0}개도 게임 정보로 보관했습니다.",raw);
  if(meta.TryGetValue("pendingEquipment",out raw)&&CodexChat.Map(raw).Values.Any(x=>CodexChat.Items(x).Any()))result+="\n"+Locale.T("장착 상태를 갱신했습니다. 일부 장비의 보유 수는 추가 스캔으로 확인할 수 있습니다.");return result;
 }
 internal string Refresh(bool incremental=false){
  using(var gate=new System.Threading.Mutex(false,GameDataCatalog.Scope("HoyoAccountRefresh",root))){
   bool held=false;try{
    try{held=gate.WaitOne(0);}catch(System.Threading.AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException(Locale.T("HoYoLAB 자료를 가져오는 중입니다."));
    string cookie;using(var db=new LocalDataService(root))cookie=db.GetSecret("hoyolab");if(string.IsNullOrWhiteSpace(cookie))throw new InvalidOperationException(Locale.T("HoYoLAB 로그인이 필요합니다."));
    var account=HoyoClient.Account(cookie,root);string uid=CodexChat.S(account,"resinUid"),server=CodexChat.S(account,"resinServer");
    Dictionary<string,object> reference;var current=new CatheryneTools(root).AccountSnapshot(out reference);object rawMeta;var currentMeta=current.TryGetValue("catheryne",out rawMeta)?CodexChat.Map(rawMeta):new Dictionary<string,object>();
    if(CodexChat.S(currentMeta,"uid")!=""&&(CodexChat.S(currentMeta,"uid")!=uid||CodexChat.S(currentMeta,"server")!=server))throw new InvalidOperationException(Locale.T("저장된 자료와 HoYoLAB 계정이 다릅니다. 기존 계정으로 다시 연결해 주세요."));
    GameDataCatalog.QueueRefresh(root);var catalog=HoyoAccountConverter.Catalog(root);string catalogStamp;using(var hash=SHA256.Create())catalogStamp=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(AccountIdentity.Format+":2:"+CatheryneTools.Json().Serialize(catalog))));
    string snapshot=CodexChat.S(reference,"snapshot");string profile="hoyo-account:"+server+":"+uid;Dictionary<string,object> previous=null;
    using(var db=new LocalDataService(root)){var saved=db.Recent(profile,"refresh",1);if(saved.Count>0){previous=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(saved[0]["payload"]);DateTime at;if(incremental&&snapshot.Length>0&&CodexChat.S(previous,"snapshot")==snapshot&&CodexChat.S(previous,"catalog")==catalogStamp&&DateTime.TryParse(saved[0]["observed_at"],out at)&&DateTime.UtcNow-at.ToUniversalTime()<TimeSpan.FromMinutes(1))return Locale.T("자료가 최신 상태입니다.");}}
    var list=fetch(cookie,uid,server,"character/list",null,"en-us");var ids=CodexChat.Items(list["list"]).Select(x=>Convert.ToInt32(x["id"])).ToArray();
    if(ids.Length==0||ids.Distinct().Count()!=ids.Length)throw new InvalidDataException("HoYoLAB character list is incomplete");
    var details=fetch(cookie,uid,server,"character/detail",new Dictionary<string,object>{{"character_ids",ids}},"en-us");
    var actual=CodexChat.Items(details["list"]).Select(x=>Convert.ToInt32(CodexChat.Map(x["base"])["id"])).ToArray();
    if(!ids.OrderBy(x=>x).SequenceEqual(actual.OrderBy(x=>x)))throw new InvalidDataException("HoYoLAB character details are incomplete");
    var achievements=fetch(cookie,uid,server,"achievement",null,null);
    Dictionary<string,object> localized=null;
    if(!Locale.IsEnglish){var cachedNames=new HashSet<string>();foreach(string section in new[]{"characters","weapons","artifacts"}){object rows;if(current.TryGetValue(section,out rows))foreach(var row in CodexChat.Items(rows)){object names;if(row.TryGetValue("names",out names)&&CodexChat.S(CodexChat.Map(names),"ko").Length>0)cachedNames.Add(CodexChat.S(row,"gameId"));object sets;if(row.TryGetValue("setNames",out sets)&&CodexChat.S(CodexChat.Map(sets),"ko").Length>0)cachedNames.Add("set:"+CodexChat.S(row,"setGameId"));}}
     bool needsNames=CodexChat.Items(details["list"]).Any(d=>{object relics;var rows=new List<Dictionary<string,object>>{CodexChat.Map(d["base"])};if(d.ContainsKey("weapon"))rows.Add(CodexChat.Map(d["weapon"]));if(d.TryGetValue("relics",out relics))rows.AddRange(CodexChat.Items(relics));return rows.Any(x=>CodexChat.S(x,"id").Length>0&&CodexChat.S(x,"id")!="0"&&(!cachedNames.Contains(CodexChat.S(x,"id"))||(x.ContainsKey("set")&&!cachedNames.Contains("set:"+CodexChat.S(CodexChat.Map(x["set"]),"id")))));});
     if(needsNames)try{localized=fetch(cookie,uid,server,"character/detail",new Dictionary<string,object>{{"character_ids",ids}},"ko-kr");if(!ids.OrderBy(x=>x).SequenceEqual(CodexChat.Items(localized["list"]).Select(x=>Convert.ToInt32(CodexChat.Map(x["base"])["id"])).OrderBy(x=>x)))localized=null;}catch{/* Display names may fall back to the verified English response. */}
    }
    var good=HoyoAccountConverter.Convert(details,uid,server,achievements,catalog,current,localized);
    var prefs=AppPreferences.Read(root);if(CodexChat.S(prefs,"resinUid")!=uid||CodexChat.S(prefs,"resinServer")!=server)throw new InvalidOperationException(Locale.T("선택한 계정이 바뀌었습니다. 다시 최신화해 주세요."));
    var fingerprint=CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(good)));var fingerprintMeta=CodexChat.Map(fingerprint["catheryne"]);fingerprintMeta.Remove("scannedAt");fingerprintMeta.Remove("hoyolabObservedAt");string digest;
    using(var hash=SHA256.Create())digest=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(CatheryneTools.Json().Serialize(fingerprint)))).Replace("-","");
    if(previous!=null&&snapshot.Length>0&&CodexChat.S(previous,"snapshot")==snapshot&&CodexChat.S(previous,"digest")==digest){using(var db=new LocalDataService(root))db.Observe(profile,"refresh",new{digest=digest,snapshot=snapshot,catalog=catalogStamp});CollectionHistory.Record(root,current,good);return Locale.T("변경된 자료가 없습니다.");}
    string folder=Path.Combine(root,"account-imports");Directory.CreateDirectory(folder);string file=Path.Combine(folder,Guid.NewGuid().ToString("N")+".json");
    try{AtomicFile.Write(file,CatheryneTools.Json().Serialize(good));new ProfileStore(root).Import("account",file);}finally{if(File.Exists(file))File.Delete(file);}
    new CatheryneTools(root).AccountSnapshot(out reference);snapshot=CodexChat.S(reference,"snapshot");
    using(var db=new LocalDataService(root))db.Observe(profile,"refresh",new{digest=digest,snapshot=snapshot,catalog=catalogStamp});
    var meta=CodexChat.Map(good["catheryne"]);int unknown=Convert.ToInt32(meta["unmappedCount"]);
    return Locale.Format("캐릭터 {0}명과 장착 장비를 최신화했습니다.",CodexChat.Items(good["characters"]).Count())+(unknown>0?"\n"+Locale.Format("외부 도구용 키가 없는 항목 {0}개도 게임 정보로 보관했습니다.",unknown):"");
   }finally{if(held)gate.ReleaseMutex();}
  }
 }
}

internal static class HoyoAccountConverter {
 internal static Dictionary<string,object> Catalog(string root=null){return GameDataCatalog.Read(root??Setup.DataFolder).Hoyo;}
 static Dictionary<string,object> Map(Dictionary<string,object> row,string field){object value;return row.TryGetValue(field,out value)?CodexChat.Map(value):new Dictionary<string,object>();}
 static string Key(Dictionary<string,object> catalog,string kind,object id){return CodexChat.S(Map(catalog,kind),System.Convert.ToString(id,CultureInfo.InvariantCulture));}
 static void Copy(Dictionary<string,object> target,Dictionary<string,object> source,string dest,string origin){object value;if(source.TryGetValue(origin,out value)&&value!=null)target[dest]=value;}
 static readonly Dictionary<string,string> Stats=new Dictionary<string,string>{{"2","hp"},{"3","hp_"},{"5","atk"},{"6","atk_"},{"8","def"},{"9","def_"},{"20","critRate_"},{"22","critDMG_"},{"23","enerRech_"},{"26","heal_"},{"28","eleMas"},{"30","physical_dmg_"},{"40","pyro_dmg_"},{"41","electro_dmg_"},{"42","hydro_dmg_"},{"43","dendro_dmg_"},{"44","anemo_dmg_"},{"45","geo_dmg_"},{"46","cryo_dmg_"}};
 static string Stat(Dictionary<string,object> value){string key;return Stats.TryGetValue(CodexChat.S(value,"property_type"),out key)?key:"";}
 static string SkillName(string value){return System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(value??"","<[^>]+>","")).Trim();}
 static Dictionary<string,object> Names(Dictionary<string,object> row,Dictionary<string,object> localized,Dictionary<string,object> existing,string section,string key){
  string id=CodexChat.S(row,"id"),english=CodexChat.S(row,"name"),korean=CodexChat.S(localized,"name");object raw;
  if(korean.Length==0&&existing!=null&&existing.TryGetValue(section,out raw)){var saved=CodexChat.Items(raw).FirstOrDefault(x=>CodexChat.S(x,"gameId")==id);if(saved!=null)korean=CodexChat.S(Map(saved,"names"),"ko");}
  if(korean.Length==0&&key.Length>0){string name=GameCatalog.Name(key,"ko");if(name!=key)korean=name;}if(english.Length==0&&key.Length>0)english=GameCatalog.Name(key,"en");return new Dictionary<string,object>{{"en",english},{"ko",korean}};
 }
 internal static Dictionary<string,object> Convert(Dictionary<string,object> details,string uid,string server,Dictionary<string,object> achievement,Dictionary<string,object> catalog,Dictionary<string,object> existing=null,Dictionary<string,object> localized=null){
  var characters=new List<object>();var weapons=new List<object>();var artifacts=new List<object>();var observed=new List<string>();var weaponObserved=new List<string>();var artifactObserved=new List<string>();var unmapped=new List<object>();
  foreach(var detail in CodexChat.Items(details["list"])){
   var basis=Map(detail,"base");string id=CodexChat.S(basis,"id"),key=Key(catalog,"characters",id);
   string officialName=CodexChat.S(basis,"name");if(officialName.Length==0)officialName=CodexChat.S(Map(catalog,"characterNames"),id);if(key.Length==0)key=AccountIdentity.ExistingKey(existing,"characters",id,officialName);
   bool traveler=key=="Traveler";if(traveler)key=Key(catalog,"travelerElements",CodexChat.S(basis,"element").ToLowerInvariant());
   var local=localized==null?new Dictionary<string,object>():CodexChat.Items(localized["list"]).FirstOrDefault(x=>CodexChat.S(Map(x,"base"),"id")==id)??new Dictionary<string,object>();
   var character=new Dictionary<string,object>{{"gameId",basis["id"]},{"names",Names(basis,Map(local,"base"),existing,"characters",key)}};if(key.Length>0)character["key"]=key;else unmapped.Add(basis);
   object savedCharacters;if(existing!=null&&existing.TryGetValue("characters",out savedCharacters)){var saved=CodexChat.Items(savedCharacters).FirstOrDefault(x=>CodexChat.S(x,"gameId")==id&&(CodexChat.S(x,"key")==key||CodexChat.S(x,"key").Length==0||key.Length==0));if(saved!=null)character["identity"]=AccountIdentity.Key(saved);}
   string location=traveler?"Traveler":AccountIdentity.Key(character);Copy(character,basis,"level","level");Copy(character,basis,"constellation","actived_constellation_num");
   var talentMap=Map(Map(catalog,"talents"),id);var talents=new Dictionary<string,object>();object skills;
   if(detail.TryGetValue("skills",out skills)&&character.ContainsKey("constellation"))foreach(var field in talentMap){var rule=CodexChat.Map(field.Value);var matches=CodexChat.Items(skills).Where(x=>CodexChat.S(x,"skill_type")=="1"&&SkillName(CodexChat.S(x,"name"))==SkillName(CodexChat.S(rule,"name"))).ToArray();if(matches.Length!=1)continue;int value=System.Convert.ToInt32(matches[0]["level"]),rank=System.Convert.ToInt32(character["constellation"]);foreach(var boost in (System.Collections.IEnumerable)rule["boosts"])if(rank>=System.Convert.ToInt32(boost))value-=3;if(value>=1&&value<=10)talents[field.Key]=value;}
   if(detail.ContainsKey("skills"))character["talent"]=talents;object observedSkills;if(detail.TryGetValue("skills",out observedSkills))character["observedSkills"]=CodexChat.Items(observedSkills).Where(x=>CodexChat.S(x,"skill_type")=="1").Select(x=>new Dictionary<string,object>{{"name",CodexChat.S(x,"name")},{"level",x.ContainsKey("level")?x["level"]:null}}).ToArray();Copy(character,basis,"icon","icon");Copy(character,basis,"rarity","rarity");Copy(character,basis,"element","element");character["hoyolab"]=detail;var finalStats=new Dictionary<string,object>();foreach(string group in new[]{"base_properties","extra_properties","element_properties","selected_properties"}){object properties;if(detail.TryGetValue(group,out properties))foreach(var property in CodexChat.Items(properties)){string stat=CodexChat.S(property,"property_type"),name=stat=="2000"?"hp":stat=="2001"?"atk":stat=="2002"?"def":Stat(property);double number;if(name.Length>0&&double.TryParse(CodexChat.S(property,"final").Replace("%","").Replace(",",""),NumberStyles.Number,CultureInfo.InvariantCulture,out number)&&!double.IsNaN(number)&&!double.IsInfinity(number))finalStats[name]=number;}}if(detail.ContainsKey("selected_properties")||finalStats.Count>0)character["finalStats"]=finalStats;Copy(character,basis,"weaponType","weapon_type");Copy(character,basis,"image","image");Copy(character,basis,"sideIcon","side_icon");characters.Add(character);observed.Add(location);
   var weapon=Map(detail,"weapon");string wk=Key(catalog,"weapons",CodexChat.S(weapon,"id"));if(wk.Length==0)wk=AccountIdentity.ExistingKey(existing,"weapons",CodexChat.S(weapon,"id"),CodexChat.S(weapon,"name"));
   if(CodexChat.S(weapon,"id").Length>0&&CodexChat.S(weapon,"id")!="0"){var good=new Dictionary<string,object>{{"gameId",weapon["id"]},{"names",Names(weapon,Map(local,"weapon"),existing,"weapons",wk)},{"location",location}};if(wk.Length>0)good["key"]=wk;else unmapped.Add(weapon);Copy(good,weapon,"icon","icon");Copy(good,weapon,"level","level");Copy(good,weapon,"ascension","promote_level");Copy(good,weapon,"refinement","affix_level");good["hoyolab"]=weapon;Copy(good,weapon,"rarity","rarity");Copy(good,weapon,"weaponType","type");weapons.Add(good);}
   if(detail.ContainsKey("weapon"))weaponObserved.Add(location);object relics;if(detail.TryGetValue("relics",out relics))foreach(var relic in CodexChat.Items(relics)){
    string set=Key(catalog,"artifacts",CodexChat.S(relic,"id"));int pos=System.Convert.ToInt32(relic["pos"]);string main=Stat(Map(relic,"main_property"));
    if(pos<1||pos>5||main.Length==0){unmapped.Add(relic);continue;}if(set.Length==0)unmapped.Add(relic);
    var substats=new List<object>();bool valid=true;object subs;if(!relic.TryGetValue("sub_property_list",out subs)){unmapped.Add(relic);continue;}
    foreach(var sub in CodexChat.Items(subs)){string stat=Stat(sub);double value;if(stat.Length==0||!double.TryParse(CodexChat.S(sub,"value").Replace("%","").Replace(",",""),NumberStyles.Number,CultureInfo.InvariantCulture,out value)||value<0){valid=false;break;}substats.Add(new Dictionary<string,object>{{"key",stat},{"value",value}});}
    if(!valid){unmapped.Add(relic);continue;}
    var good=new Dictionary<string,object>{{"gameId",relic["id"]},{"names",Names(relic,local.ContainsKey("relics")?CodexChat.Items(local["relics"]).FirstOrDefault(x=>CodexChat.S(x,"id")==CodexChat.S(relic,"id"))??new Dictionary<string,object>():new Dictionary<string,object>(),existing,"artifacts",set)},{"slotKey",new[]{"flower","plume","sands","goblet","circlet"}[pos-1]},{"mainStatKey",main},{"substats",substats},{"location",location}};if(set.Length>0)good["setKey"]=set;Copy(good,relic,"icon","icon");Copy(good,relic,"level","level");Copy(good,relic,"rarity","rarity");good["hoyolab"]=relic;var setInfo=Map(relic,"set");var localRelic=local.ContainsKey("relics")?CodexChat.Items(local["relics"]).FirstOrDefault(x=>CodexChat.S(x,"id")==CodexChat.S(relic,"id")):null;Copy(good,setInfo,"setGameId","id");string setEn=CodexChat.S(setInfo,"name"),setKo=localRelic==null?"":CodexChat.S(Map(localRelic,"set"),"name");if(setKo.Length==0&&set.Length>0)setKo=GameCatalog.Name(set,"ko");if(setKo.Length==0&&existing!=null){object oldRows;if(existing.TryGetValue("artifacts",out oldRows)){var saved=CodexChat.Items(oldRows).FirstOrDefault(x=>CodexChat.S(x,"setGameId")==CodexChat.S(setInfo,"id"));if(saved!=null)setKo=CodexChat.S(Map(saved,"setNames"),"ko");}}if(setEn.Length>0||setKo.Length>0)good["setNames"]=new Dictionary<string,object>{{"en",setEn},{"ko",setKo}};artifacts.Add(good);
   }
   if(detail.ContainsKey("relics")&&CodexChat.Items(detail["relics"]).Count()==artifacts.OfType<Dictionary<string,object>>().Count(x=>CodexChat.S(x,"location")==location))artifactObserved.Add(location);
  }
  if(characters.Count==0)throw new InvalidDataException("HoYoLAB character list is empty");
  return new Dictionary<string,object>{{"format",AccountIdentity.Format},{"version",1},{"source","Catheryne.HoYoLAB"},{"characters",characters},{"weapons",weapons},{"artifacts",artifacts},{"catheryne",new Dictionary<string,object>{{"provider","hoyolab"},{"catalogSources",catalog.ContainsKey("sources")?catalog["sources"]:new Dictionary<string,object>()},{"uid",uid},{"server",server},{"scannedAt",DateTime.UtcNow.ToString("o")},{"coverage",new Dictionary<string,object>{{"characters","characters"},{"weapons","equipped"},{"artifacts","equipped"}}},{"equippedCharacters",observed},{"equipmentObserved",new Dictionary<string,object>{{"weapons",weaponObserved},{"artifacts",artifactObserved}}},{"hoyolabObservedAt",DateTime.UtcNow.ToString("o")},{"achievementSummary",achievement},{"unmappedCount",unmapped.Count},{"unmapped",unmapped}}}};
 }
}
