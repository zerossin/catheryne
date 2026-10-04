using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// Quantities are observations, not item instances. Missing and capped quantities are explicit.
internal static class MaterialInventory {
 internal static Dictionary<string,object> Map(Dictionary<string,object> value,string key){object raw;return value.TryGetValue(key,out raw)?CodexChat.Map(raw):new Dictionary<string,object>();}
 internal static long Number(object raw,string field,bool signed=false){long n;decimal d;if(raw==null||raw is bool||!decimal.TryParse(Convert.ToString(raw,CultureInfo.InvariantCulture),NumberStyles.Number,CultureInfo.InvariantCulture,out d)||d!=decimal.Truncate(d)||d>long.MaxValue||d<long.MinValue||(!signed&&d<0))throw new InvalidDataException("Invalid material "+field);n=(long)d;return n;}
 internal static Dictionary<string,object>[] Rows(Dictionary<string,object> snapshot){object raw;if(!snapshot.TryGetValue("materialInventory",out raw))return new Dictionary<string,object>[0];if(!(raw is IList))throw new InvalidDataException("Invalid material inventory");var rows=CodexChat.Items(raw).ToArray();var seen=new HashSet<string>();foreach(var row in rows){string id=CodexChat.S(row,"itemId");if((!Regex.IsMatch(id,@"^\d+$")||Number(id,"ID")==0)&&!Regex.IsMatch(id,@"^good:[A-Za-z0-9]+$")||!seen.Add(id))throw new InvalidDataException("Invalid or duplicate material ID");object count;if(row.TryGetValue("quantity",out count)&&count!=null)Number(count,"quantity");if(row.TryGetValue("lowerBound",out count))Number(count,"lowerBound");DateTimeOffset at;if(!DateTimeOffset.TryParse(CodexChat.S(row,"observedAt"),out at)||CodexChat.S(row,"source").Length==0)throw new InvalidDataException("Material provenance missing");}return rows;}
 internal static Dictionary<string,object>[] Merge(Dictionary<string,object> previous,Dictionary<string,object> incoming){
  var merged=Rows(previous).ToDictionary(x=>CodexChat.S(x,"itemId"),x=>x);
  foreach(var row in Rows(incoming)){
   string id=CodexChat.S(row,"itemId");Dictionary<string,object> old;merged.TryGetValue(id,out old);
   bool preserveCount=old!=null&&row["quantity"]==null&&old.ContainsKey("quantity")&&old["quantity"]!=null&&CodexChat.S(old,"source")!="hoyolab.calculator";
   var updated=new Dictionary<string,object>(preserveCount?old:row);
   if(old!=null&&!preserveCount&&DateTimeOffset.Parse(CodexChat.S(row,"observedAt"))<DateTimeOffset.Parse(CodexChat.S(old,"observedAt")))continue;
   foreach(string field in new[]{"name","icon","rarity","goodKey"}){string value=CodexChat.S(row,field);if(value.Length>0)updated[field]=row[field];else if(old!=null&&old.ContainsKey(field))updated[field]=old[field];}
   merged[id]=updated;
  }
  return merged.Values.OrderBy(x=>CodexChat.S(x,"itemId"),StringComparer.Ordinal).ToArray();
 }
 internal static Dictionary<string,object>[] FromCalculator(Dictionary<string,object> data,string observedAt){
  object flag;if(!data.TryGetValue("has_user_info",out flag)||!(flag is bool)||!(bool)flag)throw new InvalidOperationException(Locale.T("HoYoLAB 육성 계산기의 계정 동기화를 확인해 주세요. 기존 재료 기록은 유지됩니다."));
  var result=new Dictionary<string,Dictionary<string,object>>();object raw;
  // v3 can allocate and synthesize stock. Cost-minus-shortage and remaining materials
  // are plan projections, never a raw backpack count.
  foreach(string field in new[]{"overall_consume","available_material"})if(data.TryGetValue(field,out raw)&&raw is IList)foreach(var item in CodexChat.Items(raw)){var row=Observation(item,observedAt);row["quantity"]=null;row["measurement"]="calculator_only";result[CodexChat.S(row,"itemId")]=row;}
  return Rows(new Dictionary<string,object>{{"materialInventory",result.Values.ToArray()}});
 }
 internal static Dictionary<string,object>[] FromScan(Dictionary<string,object> snapshot){
  object raw;if(!snapshot.TryGetValue("materials",out raw)||!(raw is Dictionary<string,object>))throw new InvalidDataException("Invalid scanned materials");var meta=Map(snapshot,"catheryne");string at=CodexChat.S(meta,"scannedAt");DateTimeOffset when;if(!DateTimeOffset.TryParse(at,out when))at=DateTime.UtcNow.ToString("o");var rows=new List<Dictionary<string,object>>();
  foreach(var pair in (Dictionary<string,object>)raw){long count=Number(pair.Value,"quantity");var match=MaterialCatalog.Match(Setup.DataFolder,pair.Key);string id=match==null?"good:"+pair.Key:CodexChat.S(match,"id");string name=match==null?pair.Key:CodexChat.S(match,Locale.IsEnglish?"en":"ko");if(name.Length==0)name=pair.Key;rows.Add(new Dictionary<string,object>{{"itemId",id},{"goodKey",pair.Key},{"name",name},{"quantity",count},{"measurement","observed"},{"source",CodexChat.S(snapshot,"source").Length==0?"GOOD.import":CodexChat.S(snapshot,"source")},{"observedAt",at}});}
  return Rows(new Dictionary<string,object>{{"materialInventory",rows.ToArray()}});
 }
 static Dictionary<string,object> Observation(Dictionary<string,object> item,string at){if(!item.ContainsKey("id"))throw new InvalidDataException("Material ID missing");long id=Number(item["id"],"ID");return new Dictionary<string,object>{{"itemId",id.ToString(CultureInfo.InvariantCulture)},{"name",CodexChat.S(item,"name")},{"rarity",CodexChat.S(item,"level")},{"icon",CodexChat.S(item,"icon_url").Length>0?CodexChat.S(item,"icon_url"):CodexChat.S(item,"icon")},{"observedAt",at},{"source","hoyolab.calculator"}};}
 internal static object Page(string root,int offset,string query){Dictionary<string,object> pointer;var snapshot=new CatheryneTools(root).AccountSnapshot(out pointer);var rows=Rows(snapshot).Where(x=>string.IsNullOrEmpty(query)||(CodexChat.S(x,"name")+" "+CodexChat.S(x,"itemId")).IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0).ToArray();return new{collected=snapshot.ContainsKey("materialInventory"),scope="observed_items_only",missing="unknown",total=rows.Length,items=rows.Skip(offset).Take(30).ToArray(),next_offset=offset+30<rows.Length?(object)(offset+30):null};}
 internal static string CountText(Dictionary<string,object> row){object count;return row.TryGetValue("quantity",out count)&&count!=null?Number(count,"quantity").ToString("N0",Locale.Culture):row.ContainsKey("lowerBound")?Locale.Format("{0:N0}개 이상",Number(row["lowerBound"],"lower bound")):Locale.T("수량 미확인");}
}

internal sealed class HoyoCalculatorException : InvalidOperationException {internal readonly int Code;internal HoyoCalculatorException(int code,string message):base(message){Code=code;}}

internal sealed class HoyoMaterialService {
 internal delegate Dictionary<string,object> Fetch(string cookie,string uid,string server,string endpoint,Dictionary<string,object> parameters);
 readonly string root;readonly Fetch fetch;
 internal HoyoMaterialService(string root,Fetch fetch=null){this.root=root;this.fetch=fetch??Request;}
 internal static Dictionary<string,object> Request(string cookie,string uid,string server,string endpoint,Dictionary<string,object> parameters){
  if(!Regex.IsMatch(uid??"",@"^\d{9,10}$")||!Regex.IsMatch(server??"",@"^os_(asia|usa|euro|cht)$"))throw new InvalidOperationException(Locale.T("HoYoLAB에서 원신 계정을 선택해 주세요."));
  if(!new[]{"sync/avatar/list","sync/avatar/detail","batch_compute"}.Contains(endpoint))throw new ArgumentException("Invalid calculator endpoint");
  bool detail=endpoint=="sync/avatar/detail";string version=endpoint=="batch_compute"?"v3":"v1";var body=new Dictionary<string,object>(parameters??new Dictionary<string,object>());body["uid"]=uid;body["region"]=server;if(!body.ContainsKey("lang"))body["lang"]=Locale.IsEnglish?"en-us":"ko-kr";
  string url="https://sg-public-api.hoyolab.com/event/e20200928calculate/"+version+"/"+endpoint;if(detail)url+="?"+string.Join("&",body.Select(x=>Uri.EscapeDataString(x.Key)+"="+Uri.EscapeDataString(Convert.ToString(x.Value,CultureInfo.InvariantCulture))));
  var req=HttpTransport.Create(url);req.Timeout=20000;req.ReadWriteTimeout=20000;req.AllowAutoRedirect=false;req.UserAgent="Catheryne";req.Referer="https://act.hoyolab.com/";req.Headers["Cookie"]=cookie;
  if(!detail){req.Method="POST";req.ContentType="application/json";byte[] bytes=Encoding.UTF8.GetBytes(CatheryneTools.Json().Serialize(body));using(var stream=req.GetRequestStream())stream.Write(bytes,0,bytes.Length);}
  using(var response=req.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){var value=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());object code;if(!value.TryGetValue("retcode",out code)||Convert.ToInt32(code)!=0)throw new HoyoCalculatorException(code==null?0:Convert.ToInt32(code),Locale.Format("HoYoLAB 재료 조회에 실패했습니다 ({0}, {1}). 육성 계산기 연결을 확인해 주세요.",code??"unknown",endpoint+": "+CodexChat.S(value,"message")));if(!value.ContainsKey("data"))throw new InvalidDataException("Calculator data missing");return CodexChat.Map(value["data"]);}
 }
 internal static string PlanDigest(Dictionary<string,object> snapshot){object raw;var characters=snapshot.TryGetValue("characters",out raw)?CodexChat.Items(raw).OrderBy(AccountIdentity.Key).Select(x=>new{identity=AccountIdentity.Key(x),level=CodexChat.S(x,"level"),talent=MaterialInventory.Map(x,"talent")}).ToArray():null;using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(CatheryneTools.Json().Serialize(characters))));}
 internal static bool PlanStale(string root,Dictionary<string,object> snapshot,int? goalRevision=null){var plan=MaterialInventory.Map(MaterialInventory.Map(snapshot,"catheryne"),"materialPlan");return plan.Count>0&&(Convert.ToInt32(plan["goalRevision"])!=(goalRevision??new GoalStore(root).Read().Revision)||CodexChat.S(plan,"accountDigest")!=PlanDigest(snapshot));}
 static string NormalizeName(string value){return System.Net.WebUtility.HtmlDecode(Regex.Replace(value??"","<[^>]+>","")).Trim();}
 internal object Refresh(){
  using(var gate=new System.Threading.Mutex(false,"Local\\Catheryne.MaterialRefresh")){bool held=false;try{try{held=gate.WaitOne(0);}catch(System.Threading.AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException(Locale.T("재료를 조회하는 중입니다."));
   string cookie;using(var db=new LocalDataService(root))cookie=db.GetSecret("hoyolab");if(string.IsNullOrWhiteSpace(cookie))throw new InvalidOperationException(Locale.T("HoYoLAB 로그인이 필요합니다."));var prefs=AppPreferences.Read(root);string uid=CodexChat.S(prefs,"resinUid"),server=CodexChat.S(prefs,"resinServer");Dictionary<string,object> pointer;var snapshot=new CatheryneTools(root).AccountSnapshot(out pointer);var meta=MaterialInventory.Map(snapshot,"catheryne");if(CodexChat.S(meta,"uid").Length>0&&(CodexChat.S(meta,"uid")!=uid||CodexChat.S(meta,"server")!=server))throw new InvalidOperationException(Locale.T("저장된 자료와 HoYoLAB 계정이 다릅니다."));
   var list=fetch(cookie,uid,server,"sync/avatar/list",new Dictionary<string,object>{{"page",1},{"size",200},{"element_attr_ids",new int[0]},{"weapon_cat_ids",new int[0]}});object raw;if(!list.TryGetValue("list",out raw)||!(raw is IList))throw new InvalidDataException("Calculator character list missing");var characters=CodexChat.Items(raw).ToArray();if(characters.Length==0||characters.Length>=200)throw new InvalidOperationException(Locale.T("육성 계산기의 캐릭터 목록을 확인해 주세요."));
   var probes=characters.Select(x=>new Dictionary<string,object>{{"avatar_id",x["id"]},{"avatar_level_current",1},{"avatar_level_target",x.ContainsKey("max_level")?x["max_level"]:90},{"element_attr_id",x.ContainsKey("element_attr_id")?x["element_attr_id"]:0},{"from_user_sync",false}}).ToArray();
   string at=DateTime.UtcNow.ToString("o");var inventoryRows=new Dictionary<string,Dictionary<string,object>>();var unavailable=new List<object>();
   Action<Dictionary<string,object>> collect=response=>{foreach(var row in MaterialInventory.FromCalculator(response,at)){string id=CodexChat.S(row,"itemId");Dictionary<string,object> prior;if(inventoryRows.TryGetValue(id,out prior)){if(prior["quantity"]!=null&&row["quantity"]==null)continue;}inventoryRows[id]=row;}};
   for(int start=0;start<probes.Length;start+=10){var batch=probes.Skip(start).Take(10).ToArray();try{collect(fetch(cookie,uid,server,"batch_compute",new Dictionary<string,object>{{"items",batch}}));}catch(HoyoCalculatorException error){if(error.Code!=-500001)throw;foreach(var probe in batch)try{collect(fetch(cookie,uid,server,"batch_compute",new Dictionary<string,object>{{"items",new[]{probe}}}));}catch(HoyoCalculatorException itemError){if(itemError.Code!=-500001)throw;unavailable.Add(new{character_id=probe["avatar_id"],code=itemError.Code});}}}
   var observed=inventoryRows.Values.ToArray();if(observed.Length==0)throw new InvalidOperationException(Locale.T("확인된 재료 수량이 없습니다. 기존 기록은 유지됩니다."));
   var goals=new GoalStore(root).Read();var planItems=new List<object>();var included=new List<string>();var unsupported=new List<object>();
   foreach(var group in goals.Goals.Where(x=>x.Category=="character"&&!x.Paused&&!x.Completed).GroupBy(x=>x.CharacterKey??"")){
    var groupGoals=group.ToArray();object owned;var character=snapshot.TryGetValue("characters",out owned)?CodexChat.Items(owned).FirstOrDefault(x=>AccountIdentity.Key(x)==group.Key):null;var sync=character==null?null:characters.FirstOrDefault(x=>CodexChat.S(x,"id")==CodexChat.S(character,"gameId"));
    if(sync==null){foreach(var goal in groupGoals)unsupported.Add(new{goal_id=goal.Id,reason="character_not_synced"});continue;}
    long current=MaterialInventory.Number(sync["level_current"],"character level");int target=groupGoals.Where(x=>x.TargetLevel.HasValue).Select(x=>x.TargetLevel.Value).DefaultIfEmpty((int)current).Max();
    if(target<current||target>MaterialInventory.Number(sync.ContainsKey("max_level")?sync["max_level"]:90,"maximum level")){foreach(var goal in groupGoals)unsupported.Add(new{goal_id=goal.Id,reason="level_target_outside_calculator_range"});continue;}
    var item=new Dictionary<string,object>{{"avatar_id",sync["id"]},{"avatar_level_current",current},{"avatar_level_target",target},{"element_attr_id",sync.ContainsKey("element_attr_id")?sync["element_attr_id"]:0},{"from_user_sync",true}};
    int talentTarget=groupGoals.Where(x=>x.TargetTalent.HasValue).Select(x=>x.TargetTalent.Value).DefaultIfEmpty(0).Max();
    if(talentTarget>0){var criterion=CharacterBuild.Read(root,character);var rules=MaterialInventory.Map(MaterialInventory.Map(HoyoAccountConverter.Catalog(),"talents"),CodexChat.S(sync,"id"));
     if(criterion==null||criterion.Talents==null||criterion.Talents.Keys.Any(k=>criterion.Talents[k]>0&&!rules.ContainsKey(k))){foreach(var goal in groupGoals.Where(x=>x.TargetTalent.HasValue))unsupported.Add(new{goal_id=goal.Id,reason="talent_target_requires_skill_mapping"});}
     else {var detail=fetch(cookie,uid,server,"sync/avatar/detail",new Dictionary<string,object>{{"avatar_id",sync["id"]},{"lang","en-us"}});var skills=new List<object>();bool complete=true;object skillRows;
      if(!detail.TryGetValue("skill_list",out skillRows))complete=false;
      else foreach(var rule in criterion.Talents.Where(x=>x.Value>0)){string expected=CodexChat.S(CodexChat.Map(rules[rule.Key]),"name");var matches=CodexChat.Items(skillRows).Where(x=>NormalizeName(CodexChat.S(x,"name"))==NormalizeName(expected)).ToArray();if(matches.Length!=1||!matches[0].ContainsKey("group_id")||!matches[0].ContainsKey("level_current")){complete=false;break;}var skill=matches[0];long level=MaterialInventory.Number(skill["level_current"],"talent level");if(level<1||level>10){complete=false;break;}if(talentTarget>level)skills.Add(new Dictionary<string,object>{{"id",skill["group_id"]},{"level_current",level},{"level_target",talentTarget}});}
      if(complete)item["skill_list"]=skills;else foreach(var goal in groupGoals.Where(x=>x.TargetTalent.HasValue))unsupported.Add(new{goal_id=goal.Id,reason="talent_target_requires_skill_mapping"});
     }
    }
    if(target==current&&(!item.ContainsKey("skill_list")||!((System.Collections.IEnumerable)item["skill_list"]).Cast<object>().Any()))continue;
    planItems.Add(item);included.AddRange(groupGoals.Select(x=>x.Id));
   }
   Dictionary<string,object> plan=null;if(planItems.Count>20)throw new InvalidOperationException(Locale.T("동시에 계산할 육성 캐릭터는 20명까지 지원합니다."));if(planItems.Count>0){plan=fetch(cookie,uid,server,"batch_compute",new Dictionary<string,object>{{"items",planItems}});var planObserved=MaterialInventory.FromCalculator(plan,at);observed=MaterialInventory.Merge(new Dictionary<string,object>{{"materialInventory",observed}},new Dictionary<string,object>{{"materialInventory",planObserved}});}
   var latest=AppPreferences.Read(root);if(uid!=CodexChat.S(latest,"resinUid")||server!=CodexChat.S(latest,"resinServer")||goals.Revision!=new GoalStore(root).Read().Revision)throw new InvalidOperationException(Locale.T("계정이나 육성 목표가 바뀌었습니다. 다시 조회해 주세요."));
   var payload=new Dictionary<string,object>{{"format",AccountIdentity.Format},{"version",1},{"source","Catheryne.HoYoLAB.Calculator"},{"materialInventory",observed},{"catheryne",new Dictionary<string,object>{{"uid",uid},{"server",server},{"materialObservedAt",at},{"materialUnavailable",unavailable},{"coverage",new Dictionary<string,object>{{"materialInventory","partial"}}},{"materialPlan",new{observedAt=at,goalRevision=goals.Revision,accountDigest=PlanDigest(snapshot),goalIds=included,unsupported=unsupported,requirements=plan==null?new object[0]:(object)(plan.ContainsKey("overall_consume")?plan["overall_consume"]:new object[0]),source="hoyolab.calculator",crafting_included=true}}}}};
   string folder=Path.Combine(root,"account-imports");Directory.CreateDirectory(folder);string file=Path.Combine(folder,Guid.NewGuid().ToString("N")+".json");try{AtomicFile.Write(file,CatheryneTools.Json().Serialize(payload));new ProfileStore(root).Import("account",file);}finally{if(File.Exists(file))File.Delete(file);}
   return new{observed_items=observed.Length,exact_items=observed.Count(x=>x["quantity"]!=null),unverified_items=observed.Count(x=>x["quantity"]==null),scope="observed_items_only",plan_goals=included.Count,unsupported_goals=unsupported.Count,unavailable_characters=unavailable.Count,game_input=false};
  }finally{if(held)gate.ReleaseMutex();}}
 }
}
