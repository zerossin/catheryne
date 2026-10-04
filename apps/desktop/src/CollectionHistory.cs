using System;using System.Collections.Generic;using System.IO;using System.Linq;

// All collection providers publish the same observed scope, freshness and change journal.
internal static class CollectionHistory {
 internal static event Action<string> Changed;
 static string PathFor(string root){return Path.Combine(root,"collection-history.json");}
 internal static Dictionary<string,object> Read(string root){return CollectionRefresh.Read(PathFor(root));}
 internal static Dictionary<string,object> Checks(string root){
  var checks=MaterialInventory.Map(Read(root),"checks");var imported=new ProfileStore(root).CollectionImports();var scanned=CollectionRefresh.Dates(root);
  foreach(string area in CollectionRefresh.Areas){string at=CodexChat.S(scanned,area);if(at.Length==0)imported.TryGetValue(area,out at);if(!checks.ContainsKey(area)&&!string.IsNullOrEmpty(at))checks[area]=new Dictionary<string,object>{{"observedAt",at},{"scope",scanned.ContainsKey(area)?"full":"imported"}};}
  Dictionary<string,object> pointer;var snapshot=new CatheryneTools(root).AccountSnapshot(out pointer);var meta=MaterialInventory.Map(snapshot,"catheryne");string hoyo=CodexChat.S(meta,"hoyolabObservedAt");
  if(hoyo.Length>0){foreach(string area in new[]{"characters","weapons","artifacts","achievements"})Choose(checks,area,hoyo,area=="characters"?"characters":area=="achievements"?"summary":"equipped");}
  string material=CodexChat.S(meta,"materialObservedAt");if(material.Length>0)Choose(checks,"materials",material,"calculator");return checks;
 }
 static void Choose(Dictionary<string,object> checks,string area,string at,string scope){DateTime before,after;if(!DateTime.TryParse(at,out after))return;var prior=MaterialInventory.Map(checks,area);if(!DateTime.TryParse(CodexChat.S(prior,"observedAt"),out before)||after.ToUniversalTime()>before.ToUniversalTime())checks[area]=new Dictionary<string,object>{{"observedAt",at},{"scope",scope}};}
 internal static Dictionary<string,object> Dates(string root){return Checks(root).ToDictionary(x=>x.Key,x=>(object)CodexChat.S(CodexChat.Map(x.Value),"observedAt"));}
 internal static string Scope(string scope){return Locale.T(scope=="equipped"?"장착 장비":scope=="summary"?"업적 집계":scope=="individual"?"개별 업적":scope=="calculator"?"육성 계산 · 수량 미확인":scope=="full"?"전체 수집":scope=="characters"?"보유 캐릭터":scope=="partial"?"선택 범위":"가져온 범위");}
 internal static string[] Due(string root,DateTime now){var dates=Dates(root);return CollectionRefresh.Areas.Where(a=>CollectionRefresh.Due(a,dates,now)).ToArray();}
 static string EqualValue(object value){var map=value as Dictionary<string,object>;return CatheryneTools.Json().Serialize(map==null?value:map.OrderBy(x=>x.Key).ToDictionary(x=>x.Key,x=>x.Value));}
 static string Value(Dictionary<string,object> row,string key){object value;return row.TryGetValue(key,out value)&&value!=null?Convert.ToString(value):Locale.T("미확인");}
 internal static List<Dictionary<string,object>> Changes(Dictionary<string,object> before,Dictionary<string,object> incoming){
  var entries=new List<Dictionary<string,object>>();Action<string,string> add=(area,text)=>entries.Add(new Dictionary<string,object>{{"area",area},{"text",text}});
  foreach(string area in new[]{"characters","weapons","artifacts","materials"}){
   string field=area=="materials"?"materialInventory":area;object raw;if(!incoming.TryGetValue(field,out raw)||raw==null)continue;var old=AccountMerge.Inventory(before,area);
   foreach(var item in CodexChat.Items(raw)){
    bool material=area=="materials",character=area=="characters";string name=material?CodexChat.S(item,"name"):AccountIdentity.Name(item);string id=material?CodexChat.S(item,"itemId"):AccountIdentity.Key(item);string location=CodexChat.S(item,"location");
    var candidates=old.Where(x=>material?CodexChat.S(x,"itemId")==id:character?AccountIdentity.Key(x)==id:location.Length>0?CodexChat.S(x,"location")==location&&CodexChat.S(x,"slotKey")==CodexChat.S(item,"slotKey"):AccountIdentity.Key(x)==id&&CodexChat.S(x,"slotKey")==CodexChat.S(item,"slotKey")).ToArray();
    Dictionary<string,object> previous=candidates.Length==1?candidates[0]:null;
    if(previous==null){if(candidates.Length==0)add(area,name+Locale.T(" · 새 정보 확인")+(material?" · "+MaterialInventory.CountText(item):""));else if(!candidates.Any(x=>new[]{"level","refinement","mainStatKey","substats","location"}.All(f=>{object v,w;item.TryGetValue(f,out v);x.TryGetValue(f,out w);return v==null||EqualValue(v)==EqualValue(w);})))add(area,name+Locale.T(" · 새 장비 관측 · 기존 장비와의 대응 미확인"));continue;}
    var lines=new List<string>();if(!material&&!character&&AccountIdentity.Key(previous)!=id)lines.Add(Locale.T("장착 변경")+" "+AccountIdentity.Name(previous)+" → "+name);
    foreach(var pair in material?new[]{new[]{"quantity","수량"}}:character?new[]{new[]{"level","레벨"},new[]{"ascension","돌파"},new[]{"constellation","별자리"}}:new[]{new[]{"level","레벨"},new[]{"ascension","돌파"},new[]{"refinement","재련"},new[]{"mainStatKey","주옵션"}}){object current,prior;if(!item.TryGetValue(pair[0],out current)||current==null)continue;previous.TryGetValue(pair[0],out prior);if(EqualValue(current)!=EqualValue(prior))lines.Add(Locale.T(pair[1])+" "+Value(previous,pair[0])+" → "+Value(item,pair[0]));}
    if(character){var talents=MaterialInventory.Map(item,"talent");var past=MaterialInventory.Map(previous,"talent");foreach(var pair in talents){object prior;past.TryGetValue(pair.Key,out prior);if(EqualValue(prior)!=EqualValue(pair.Value))lines.Add(Locale.T(pair.Key=="auto"?"일반 공격":pair.Key=="skill"?"원소전투 스킬":"원소폭발")+" "+(prior==null?Locale.T("미확인"):Convert.ToString(prior))+" → "+Convert.ToString(pair.Value));}}
    if(!material&&!character&&previous.ContainsKey("location")&&item.ContainsKey("location")&&CodexChat.S(previous,"location")!=location)lines.Add(Locale.T("장착 캐릭터")+" "+(CodexChat.S(previous,"location")==""?Locale.T("미장착"):AccountIdentity.CharacterName(before,CodexChat.S(previous,"location")))+" → "+(location==""?Locale.T("미장착"):AccountIdentity.CharacterName(incoming,location)));
    object subs,priorSubs;if(area=="artifacts"&&item.TryGetValue("substats",out subs)&&previous.TryGetValue("substats",out priorSubs)&&EqualValue(CodexChat.Items(subs).OrderBy(x=>CodexChat.S(x,"key")).ToArray())!=EqualValue(CodexChat.Items(priorSubs).OrderBy(x=>CodexChat.S(x,"key")).ToArray()))lines.Add(Locale.T("부옵션 변경"));
    if(lines.Count>0)add(area,name+" · "+string.Join(" · ",lines));
   }
  }
  var priorSummary=MaterialInventory.Map(MaterialInventory.Map(before,"catheryne"),"achievementSummary");var summary=MaterialInventory.Map(MaterialInventory.Map(incoming,"catheryne"),"achievementSummary");if(summary.ContainsKey("achievement_num")&&CodexChat.S(summary,"achievement_num")!=CodexChat.S(priorSummary,"achievement_num"))add("achievements",Locale.T("업적 달성 집계")+" "+Value(priorSummary,"achievement_num")+" → "+Value(summary,"achievement_num"));return entries;
 }
 static string ObservedAt(Dictionary<string,object> incoming,string area,string fallback){var meta=MaterialInventory.Map(incoming,"catheryne");string at=CodexChat.S(meta,area=="materials"?"materialObservedAt":"hoyolabObservedAt");if(at.Length==0)at=CodexChat.S(meta,"scannedAt");DateTime parsed;if(!DateTime.TryParse(at,out parsed)||parsed.ToUniversalTime()>DateTime.UtcNow.AddMinutes(5))return fallback;return parsed.ToUniversalTime().ToString("o");}
 internal static void Record(string root,Dictionary<string,object> before,Dictionary<string,object> incoming,int? newAchievements=null,Dictionary<string,object> priorChecks=null){
  var scopes=new Dictionary<string,object>();var meta=MaterialInventory.Map(incoming,"catheryne");var coverage=MaterialInventory.Map(meta,"coverage");string source=CodexChat.S(incoming,"source"),at=DateTime.UtcNow.ToString("o");
  foreach(string area in CatheryneScanning.AccountScanOptions.Areas){string field=area=="materials"?"materialInventory":area;if(incoming.ContainsKey(field)&&incoming[field]!=null)scopes[area]=area=="materials"&&CodexChat.S(meta,"materialObservedAt").Length>0?"calculator":coverage.ContainsKey(field)?coverage[field]:coverage.ContainsKey(area)?coverage[area]:"imported";}
  if(meta.ContainsKey("achievementSummary"))scopes["achievements"]="summary";if(newAchievements.HasValue)scopes["achievements"]="individual";if(scopes.Count==0)return;
  var changes=Changes(before,incoming);if(newAchievements.HasValue&&newAchievements.Value>0)changes.Add(new Dictionary<string,object>{{"area","achievements"},{"text",Locale.Format("개별 업적 {0}개 추가 확인",newAchievements.Value)}});
  using(var gate=new System.Threading.Mutex(false,"Local\\Catheryne.CollectionHistory")){bool held=false;try{try{held=gate.WaitOne(TimeSpan.FromSeconds(15));}catch(System.Threading.AbandonedMutexException){held=true;}if(!held)throw new IOException("최신화 기록을 저장하는 중입니다.");var history=Read(root);var checks=MaterialInventory.Map(history,"checks");if(priorChecks!=null)foreach(var prior in priorChecks)if(!checks.ContainsKey(prior.Key))checks[prior.Key]=prior.Value;foreach(var pair in scopes)checks[pair.Key]=new Dictionary<string,object>{{"observedAt",ObservedAt(incoming,pair.Key,at)},{"scope",pair.Value},{"source",source}};object raw;var events=history.TryGetValue("events",out raw)?CodexChat.Items(raw).ToList():new List<Dictionary<string,object>>();events.Add(new Dictionary<string,object>{{"at",at},{"scopes",scopes},{"changes",changes.ToArray()}});history["checks"]=checks;history["events"]=events.Skip(Math.Max(0,events.Count-50)).ToArray();AtomicFile.Write(PathFor(root),CatheryneTools.Json().Serialize(history));}finally{if(held)gate.ReleaseMutex();}}
  var changed=Changed;if(changed!=null)changed(root);
 }
}
