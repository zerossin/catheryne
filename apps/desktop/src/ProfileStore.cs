using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

internal sealed class ProfileStore {
 readonly string root;
 internal ProfileStore(string root){this.root=root;}
 internal static string ImportKind(Dictionary<string,object> value){if(value==null)throw new InvalidDataException(Locale.T("JSON 객체가 필요합니다."));string format=CodexChat.S(value,"format");if(format==AccountIdentity.Format||format=="GOOD"){AccountIdentity.Import(value);return "account";}AchievementImport.Read(value);return "achievements";}
 internal string ImportDetected(string source){return Import(null,source,false);}
 internal string Import(string kind,string source) {return Import(kind,source,false);}
 internal string Import(string kind,string source,bool verified) {
  string key;using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToLowerInvariant()))).Replace("-","");
  using(var mutex=new System.Threading.Mutex(false,"Local\\Catheryne.Profile."+key)){bool held=false;try{try{held=mutex.WaitOne(TimeSpan.FromSeconds(30));}catch(System.Threading.AbandonedMutexException){held=true;}if(!held)throw new IOException(Locale.T("다른 자료를 저장하고 있습니다. 잠시 후 다시 시도해 주세요."));return ImportCore(kind,source,verified);}finally{if(held)mutex.ReleaseMutex();}}
 }
 string ImportCore(string kind,string source,bool verified) {
  if(kind!=null&&kind!="account"&&kind!="achievements")throw new ArgumentException(Locale.T("지원하지 않는 자료입니다."));
  var bytes=File.ReadAllBytes(source);
  var text=System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
  var json=new JavaScriptSerializer {MaxJsonLength=16000000};
  var value=json.Deserialize<Dictionary<string,object>>(text);
  if(value==null)throw new InvalidDataException(Locale.T("JSON 객체가 필요합니다."));if(kind==null){string format=CodexChat.S(value,"format");kind=format==AccountIdentity.Format||format=="GOOD"?"account":"achievements";}
  Dictionary<string,object> previousPointer;var before=new CatheryneTools(root).AccountSnapshot(out previousPointer);var beforeAchievementIds=new HashSet<int>();if(kind=="achievements")using(var db=new LocalDataService(root))foreach(var row in db.Query("SELECT id FROM achievements WHERE profile='default'"))beforeAchievementIds.Add(int.Parse(row["id"]));
  var previousChecks=CollectionHistory.Checks(root);
  var observed=value;
  string summary;
  Dictionary<string,int> achievements=null;
  if(kind=="account") {
   value=AccountIdentity.Import(value);observed=value;
   summary=AccountSummary(value)+"\n"+Locale.T("가져온 범위만 표시합니다. 없는 항목을 미보유로 판단하지 않습니다.");
  } else {
   achievements=AchievementImport.Read(value);
   summary="";

  }
  if(kind=="account")bytes=System.Text.Encoding.UTF8.GetBytes(json.Serialize(value));
  string digest;using(var hash=SHA256.Create())digest=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
  string folder=Path.Combine(root,"profiles","default",kind,"snapshots");Directory.CreateDirectory(folder);
  string target=Path.Combine(folder,digest+".json");
  if(!File.Exists(target))using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write))output.Write(bytes,0,bytes.Length);
  if(kind=="achievements") {
   using(var db=new LocalDataService(root)) {
    db.Execute("BEGIN IMMEDIATE");
    try {
     foreach(var item in achievements) {
      int id=int.Parse(item.Key),category=Convert.ToInt32(item.Value);
      var existing=db.Query("SELECT category FROM achievements WHERE profile='default' AND id="+id);
      if(existing.Count>0&&int.Parse(existing[0]["category"])!=category)throw new InvalidDataException(Locale.Format("업적 카테고리가 기존 기록과 다릅니다: {0}",id));
      db.Execute("INSERT INTO achievements(profile,id,category,verified,snapshot,updated_at) VALUES('default',"+id+","+category+","+(verified?1:0)+","+LocalDataService.Sql(digest)+","+LocalDataService.Sql(DateTime.UtcNow.ToString("o"))+") ON CONFLICT(profile,id) DO UPDATE SET verified=MAX(achievements.verified,excluded.verified),snapshot=CASE WHEN excluded.verified>=achievements.verified THEN excluded.snapshot ELSE achievements.snapshot END,updated_at=excluded.updated_at");
     }
     db.Execute("COMMIT");
    } catch {db.Execute("ROLLBACK");throw;}
   }
   CollectionHistory.Record(root,before,observed,achievements.Keys.Count(id=>!beforeAchievementIds.Contains(int.Parse(id))),previousChecks);
   return AchievementSummary();
  }
  string pointerPath=Path.Combine(root,"profiles","default","account","current.json");
  if(File.Exists(pointerPath)){
   var pointer=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(pointerPath));string name=Convert.ToString(pointer["snapshot"]);
   if(name==digest+".json"){CollectionHistory.Record(root,before,observed,null,previousChecks);return Convert.ToString(pointer["summary"]);}
   if(Path.GetFileName(name)!=name)throw new InvalidDataException("Invalid snapshot path");
   value=AccountMerge.Merge(json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,name))),value);
   bytes=System.Text.Encoding.UTF8.GetBytes(json.Serialize(value));
   using(var hash=SHA256.Create())digest=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
   target=Path.Combine(folder,digest+".json");if(!File.Exists(target))File.WriteAllBytes(target,bytes);
   summary=AccountSummary(value);
  }
  AtomicFile.Write(Path.Combine(root,"profiles","default",kind,"current.json"),json.Serialize(new{schemaVersion=1,snapshot=digest+".json",importedAt=DateTime.UtcNow.ToString("o"),summary=summary}));
  CollectionHistory.Record(root,before,observed,null,previousChecks);return summary;
 }
 static string AccountSummary(Dictionary<string,object> value){return Locale.Format("캐릭터 {0} · 무기 {1} · 성유물 {2}",Count(value,"characters"),Count(value,"weapons"),Count(value,"artifacts"));}
 static string Count(Dictionary<string,object> value,string key){object item;if(!value.TryGetValue(key,out item)||item==null)return Locale.T("미수집");var list=item as ICollection;if(list==null)throw new InvalidDataException(Locale.Format("{0} 목록 형식이 올바르지 않습니다.",key));return list.Count.ToString();}
 internal Dictionary<string,string> CollectionImports(){
  var result=new Dictionary<string,string>();string pointerPath=Path.Combine(root,"profiles","default","account","current.json");
  if(File.Exists(pointerPath)){var pointer=StoryClient.Read(pointerPath);string name=CodexChat.S(pointer,"snapshot");if(Path.GetFileName(name)!=name)throw new InvalidDataException("Invalid snapshot path");var value=StoryClient.Read(Path.Combine(Path.GetDirectoryName(pointerPath),"snapshots",name));foreach(string area in CatheryneScanning.AccountScanOptions.Areas)if(value.ContainsKey(area=="materials"?"materialInventory":area)&&value[area=="materials"?"materialInventory":area]!=null)result[area]=CodexChat.S(pointer,"importedAt");}
  using(var db=new LocalDataService(root)){var rows=db.Query("SELECT MAX(updated_at) AS updated FROM achievements WHERE profile='default'");if(rows.Count>0&&!string.IsNullOrEmpty(rows[0]["updated"]))result["achievements"]=rows[0]["updated"];}
  return result;
 }
 internal string Summary(string kind){if(kind=="achievements"){MigrateAchievements();return AchievementSummary();}string path=Path.Combine(root,"profiles","default",kind,"current.json");if(!File.Exists(path))return Locale.T("아직 가져온 자료가 없습니다.");var value=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));return Convert.ToString(value["summary"]);}
 void MigrateAchievements(){
  string pointer=Path.Combine(root,"profiles","default","achievements","current.json");
  if(!File.Exists(pointer))return;
  using(var db=new LocalDataService(root)){if(db.Query("SELECT id FROM achievements WHERE profile='default' LIMIT 1").Count>0)return;}
  var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(pointer));
  string name=Convert.ToString(data["snapshot"]);
  if(Path.GetFileName(name)!=name)throw new InvalidDataException("Invalid snapshot path");
  Import("achievements",Path.Combine(Path.GetDirectoryName(pointer),"snapshots",name));
 }
 string AchievementSummary(){using(var db=new LocalDataService(root)){
  var row=db.Query("SELECT COUNT(*) AS total,COALESCE(SUM(verified),0) AS verified FROM achievements WHERE profile='default'")[0];
  int total=int.Parse(row["total"]),verified=int.Parse(row["verified"]);
  return total==0?Locale.T("아직 가져온 자료가 없습니다."):Locale.Format("확인 완료 {0}개 · 검증 대기 {1}개\n로컬 DB에 저장되었습니다. 미수집 업적은 미달성으로 판단하지 않습니다.",verified,total-verified);
 }}
 internal void Export(string kind,string path){if(kind=="achievements"){ExportAchievements(path);return;}if(kind!="account")throw new ArgumentException("Unknown kind");string folder=Path.Combine(root,"profiles","default","account");var pointer=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"current.json")));string name=Convert.ToString(pointer["snapshot"]);if(Path.GetFileName(name)!=name)throw new InvalidDataException("Invalid snapshot path");AtomicFile.Write(path,CatheryneTools.Json().Serialize(AccountIdentity.Export(StoryClient.Read(Path.Combine(folder,"snapshots",name)))));}
 internal Dictionary<int,int> AchievementStates(){MigrateAchievements();var result=new Dictionary<int,int>();using(var db=new LocalDataService(root))foreach(var row in db.Query("SELECT id,verified FROM achievements WHERE profile='default'"))result.Add(int.Parse(row["id"]),row["verified"]=="1"?2:1);return result;}
 internal void ExportAchievements(string path){ExportAchievements(path,false);}
 internal void ExportAchievements(string path,bool canonical){
  MigrateAchievements();var data=new Dictionary<string,int>();
  using(var db=new LocalDataService(root))foreach(var row in db.Query("SELECT id,category FROM achievements WHERE profile='default' AND verified=1 ORDER BY id"))data.Add(row["id"],int.Parse(row["category"]));
  object output;
  if(canonical){var records=new List<object>();foreach(var item in data)records.Add(new {id=int.Parse(item.Key),category=item.Value});output=new {format="Catheryne.Achievements",version=1,completed=records};}
  else output=new {Version=1,Data=data};
  AtomicFile.Write(path,new JavaScriptSerializer().Serialize(output));
 }
}

// All providers translate to this positive-only ID/category collection before persistence.
internal static class AchievementImport {
 internal static Dictionary<string,int> Read(Dictionary<string,object> value){
  var result=new Dictionary<string,int>();object format,data;
  if(value.TryGetValue("format",out format)){
   object version;if(Convert.ToString(format)!="Catheryne.Achievements"||!value.TryGetValue("version",out version)||Convert.ToString(version)!="1"||!value.TryGetValue("completed",out data)||!(data is IList))throw new InvalidDataException(Locale.T("지원하지 않는 업적 자료 형식입니다."));
   foreach(object item in (IList)data){var row=item as Dictionary<string,object>;object id,category;if(row==null||!row.TryGetValue("id",out id)||!row.TryGetValue("category",out category))throw new InvalidDataException(Locale.T("업적 ID와 카테고리가 필요합니다."));Add(result,id,category);}
  } else {
   object version;if(!value.TryGetValue("Version",out version)||Convert.ToString(version)!="1"||!value.TryGetValue("Data",out data)||!(data is Dictionary<string,object>))throw new InvalidDataException(Locale.T("지원하지 않는 업적 자료 형식입니다."));
   foreach(var item in (Dictionary<string,object>)data)Add(result,item.Key,item.Value);
  }
  return result;
 }
 static void Add(Dictionary<string,int> result,object rawId,object rawCategory){int id,category;if(!int.TryParse(Convert.ToString(rawId),out id)||id<=0||!int.TryParse(Convert.ToString(rawCategory),out category)||category<0)throw new InvalidDataException(Locale.T("업적 ID 또는 카테고리가 올바르지 않습니다."));string key=id.ToString(System.Globalization.CultureInfo.InvariantCulture);if(result.ContainsKey(key))throw new InvalidDataException(Locale.T("중복 업적 ID입니다."));result.Add(key,category);}
}

// GOOD IDs from OCR exports are scan order, never persistent inventory identity.
internal static class AccountMerge {
 static Dictionary<string,object> Map(object value){return value as Dictionary<string,object>??new Dictionary<string,object>();}
 static List<Dictionary<string,object>> Rows(object value){var list=value as IEnumerable;var result=new List<Dictionary<string,object>>();if(list!=null)foreach(var item in list){var row=item as Dictionary<string,object>;if(row==null)throw new InvalidDataException(Locale.T("자료 목록 형식이 올바르지 않습니다."));result.Add(row);}return result;}
 static string Text(Dictionary<string,object> row,string key){object value;return row.TryGetValue(key,out value)?Convert.ToString(value):"";}
 static string Fingerprint(Dictionary<string,object> row){return new JavaScriptSerializer().Serialize(row.Where(x=>x.Key!="id"&&x.Key!="gameId"&&x.Key!="names"&&x.Key!="icon"&&x.Key!="hoyolab"&&x.Key!="setNames"&&x.Key!="setGameId"&&(x.Key!="rarity"||row.ContainsKey("slotKey"))&&x.Key!="weaponType"&&x.Key!="observedSkills").OrderBy(x=>x.Key).ToDictionary(x=>x.Key,x=>x.Value));}
 static string EquipmentState(Dictionary<string,object> item){
  var fields=item.ContainsKey("slotKey")?new[]{"slotKey","level","rarity","mainStatKey","substats"}:new[]{"level","ascension","refinement"};
  var value=new Dictionary<string,object>();foreach(string field in fields){object raw;if(!item.TryGetValue(field,out raw))return "";if(field=="substats")raw=Rows(raw).OrderBy(x=>Text(x,"key")).Select(x=>new{key=Text(x,"key"),value=Convert.ToDouble(x["value"])}).ToArray();value[field]=raw;}return CatheryneTools.Json().Serialize(value);
 }
 static bool SameType(Dictionary<string,object> a,Dictionary<string,object> b){
  string key=a.ContainsKey("slotKey")?"setKey":"key";if(Text(a,key).Length>0&&Text(b,key).Length>0)return Text(a,key)==Text(b,key)&&Text(a,"slotKey")==Text(b,"slotKey");
  string id=Text(a,"gameId");if(id.Length>0&&Text(b,"gameId").Length>0)return id==Text(b,"gameId")&&Text(a,"slotKey")==Text(b,"slotKey");
  string field=a.ContainsKey("slotKey")?"setKey":"key";return Text(a,field).Length>0&&Text(a,field)==Text(b,field)&&Text(a,"slotKey")==Text(b,"slotKey");
 }
 static void Enrich(Dictionary<string,object> target,Dictionary<string,object> source){foreach(string field in new[]{"gameId","names","icon","key","setKey","setGameId","setNames","hoyolab"})if(!target.ContainsKey(field)&&source.ContainsKey(field))target[field]=source[field];}
 static List<Dictionary<string,object>> MergeEquipped(List<Dictionary<string,object>> old,List<Dictionary<string,object>> added,IEnumerable<string> observed,out List<Dictionary<string,object>> uncertain){
  var remaining=old.Select(x=>new Dictionary<string,object>(x)).ToList();var result=new List<Dictionary<string,object>>();uncertain=new List<Dictionary<string,object>>();
  // Match all exact observations before considering new types, preserving multiplicity.
  foreach(var item in added){string state=EquipmentState(item);var match=remaining.Where(x=>SameType(x,item)&&state!=""&&EquipmentState(x)==state).OrderByDescending(x=>Text(x,"location")==Text(item,"location")).FirstOrDefault();if(match!=null){remaining.Remove(match);foreach(var field in item)match[field.Key]=field.Value;result.Add(match);}else uncertain.Add(item);}
  foreach(var item in uncertain.ToArray())if(!old.Any(x=>SameType(x,item))){result.Add(item);uncertain.Remove(item);}
  var owners=new HashSet<string>(observed);
  foreach(var item in remaining){if(owners.Contains(Text(item,"location")))item["location"]=null;result.Add(item);}
  return result;
 }
 // One projection for character detail, build analysis and AI. Pending observations are
 // equipped facts, not additional owned instances; inventory counts remain conservative.
 // All account clients show the same current observations, while owned counts remain explicit.
 internal static Dictionary<string,object>[] Inventory(Dictionary<string,object> snapshot,string section){
  if(section=="materials")return MaterialInventory.Rows(snapshot);
  object raw;var records=snapshot.TryGetValue(section,out raw)?Rows(raw):new List<Dictionary<string,object>>();if(section=="characters")return records.ToArray();
  var meta=snapshot.TryGetValue("catheryne",out raw)?Map(raw):new Dictionary<string,object>();var pending=meta.TryGetValue("pendingEquipment",out raw)?Map(raw):new Dictionary<string,object>();var current=pending.TryGetValue(section,out raw)?Rows(raw):new List<Dictionary<string,object>>();
  return current.Select(x=>{var row=new Dictionary<string,object>(x);row["inventoryMatch"]="unresolved";return row;}).Concat(records.Where(x=>!current.Any(y=>Text(y,"location").Length>0&&Text(y,"location")==Text(x,"location")&&(section=="weapons"||Text(x,"slotKey")==Text(y,"slotKey"))))).ToArray();
 }
 internal static Dictionary<string,object>[] Equipped(Dictionary<string,object> snapshot,string section,string character){
  if(new[]{"TravelerAnemo","TravelerGeo","TravelerElectro","TravelerDendro","TravelerHydro","TravelerPyro","TravelerCryo"}.Contains(character))character="Traveler";
  object raw;var records=snapshot.TryGetValue(section,out raw)?Rows(raw):new List<Dictionary<string,object>>();
  var meta=snapshot.TryGetValue("catheryne",out raw)?Map(raw):new Dictionary<string,object>();var pending=meta.TryGetValue("pendingEquipment",out raw)?Map(raw):new Dictionary<string,object>();
  var current=pending.TryGetValue(section,out raw)?Rows(raw).Where(x=>Text(x,"location")==character).ToList():new List<Dictionary<string,object>>();
  return current.Concat(records.Where(x=>Text(x,"location")==character&&!current.Any(y=>section=="weapons"||Text(y,"slotKey")==Text(x,"slotKey")))).ToArray();
 }
 static List<Dictionary<string,object>> RemapOwners(List<Dictionary<string,object>> rows,Dictionary<string,string> changes){return rows.Select(x=>{var copy=new Dictionary<string,object>(x);string owner;if(changes.TryGetValue(Text(x,"location"),out owner))copy["location"]=owner;return copy;}).ToList();}
 internal static Dictionary<string,object> Merge(Dictionary<string,object> previous,Dictionary<string,object> incoming){
  previous=AccountIdentity.Import(previous);incoming=AccountIdentity.Import(incoming);var result=new Dictionary<string,object>(previous);object raw;var metadata=incoming.TryGetValue("catheryne",out raw)?Map(raw):new Dictionary<string,object>();var coverage=metadata.TryGetValue("coverage",out raw)?Map(raw):new Dictionary<string,object>();
  var oldMetadata=previous.TryGetValue("catheryne",out raw)?Map(raw):new Dictionary<string,object>();
  if(Text(oldMetadata,"uid")!=""&&Text(metadata,"uid")!=""&&(Text(oldMetadata,"uid")!=Text(metadata,"uid")||Text(oldMetadata,"server")!=Text(metadata,"server")))throw new InvalidOperationException(Locale.T("저장된 자료와 HoYoLAB 계정이 다릅니다. 기존 계정으로 다시 연결해 주세요."));
  DateTime oldAt,newAt;if(DateTime.TryParse(Text(oldMetadata,"scannedAt"),out oldAt)&&DateTime.TryParse(Text(metadata,"scannedAt"),out newAt)&&newAt<oldAt)throw new InvalidDataException(Locale.T("현재 자료보다 오래된 수집 결과입니다. 원본은 보존되며 최신 자료를 덮어쓰지 않습니다."));
  // A newly available GOOD key changes the projection, never the game identity.
  var ownerChanges=new Dictionary<string,string>();object priorChars,nextChars;
  if(previous.TryGetValue("characters",out priorChars)&&incoming.TryGetValue("characters",out nextChars))foreach(var character in Rows(nextChars)){var old=Rows(priorChars).FirstOrDefault(x=>SameType(x,character));if(old!=null&&AccountIdentity.Key(old)!=AccountIdentity.Key(character))ownerChanges[AccountIdentity.Key(old)]=AccountIdentity.Key(character);}
  if(ownerChanges.Count>0){foreach(string section in new[]{"weapons","artifacts"})if(previous.TryGetValue(section,out raw))previous[section]=RemapOwners(Rows(raw),ownerChanges);object oldPending;if(oldMetadata.TryGetValue("pendingEquipment",out oldPending)){var updated=new Dictionary<string,object>();foreach(var section in Map(oldPending))updated[section.Key]=RemapOwners(Rows(section.Value),ownerChanges);oldMetadata=new Dictionary<string,object>(oldMetadata);oldMetadata["pendingEquipment"]=updated;}result=new Dictionary<string,object>(previous);}
  var pending=new Dictionary<string,object>();
  foreach(string section in new[]{"characters","weapons","artifacts"}){
   if(!incoming.TryGetValue(section,out raw)||raw==null)continue;var added=Rows(raw);object oldRaw;var old=previous.TryGetValue(section,out oldRaw)?Rows(oldRaw):new List<Dictionary<string,object>>();
   if(section=="characters"){
    var mergedRows=old.Select(x=>new Dictionary<string,object>(x)).ToList();var seen=new HashSet<string>();
    foreach(var item in added){string identity=AccountIdentity.Key(item);if(!seen.Add(identity))throw new InvalidDataException("Duplicate character identity");
     int index=mergedRows.FindIndex(x=>SameType(x,item));var merged=index>=0?new Dictionary<string,object>(mergedRows[index]):new Dictionary<string,object>();
     foreach(var field in item)if(field.Value!=null){if(field.Key=="talent"&&Text(metadata,"provider")!="hoyolab"&&merged.ContainsKey("talent")){var talents=new Dictionary<string,object>(Map(merged["talent"]));foreach(var talent in Map(field.Value))talents[talent.Key]=talent.Value;merged[field.Key]=talents;}else merged[field.Key]=field.Value;}
     if(!item.ContainsKey("ascension")&&merged.ContainsKey("ascension")&&merged.ContainsKey("level")){int asc=Convert.ToInt32(merged["ascension"]),level=Convert.ToInt32(merged["level"]);if(asc<0||asc>6||level>new[]{20,40,50,60,70,80,100}[asc])merged.Remove("ascension");}
     if(index>=0)mergedRows[index]=merged;else mergedRows.Add(merged);
    }result[section]=mergedRows;

   }else if(Text(coverage,section)=="equipped"){
    List<Dictionary<string,object>> uncertain;
    var observed=metadata.TryGetValue("equipmentObserved",out raw)?Map(raw):new Dictionary<string,object>();
    IEnumerable<string> owners=observed.TryGetValue(section,out raw)?((IEnumerable)raw).Cast<object>().Select(Convert.ToString):added.Select(x=>Text(x,"location"));
    result[section]=MergeEquipped(old,added,owners,out uncertain);
    pending[section]=uncertain;
   }else if(old.Count==0||Text(coverage,section)=="full"){foreach(var item in added){var same=old.FirstOrDefault(x=>SameType(x,item));if(same!=null)Enrich(item,same);}result[section]=added;}
   else{
    var remaining=new List<Dictionary<string,object>>(old);var merged=new List<Dictionary<string,object>>(old);var uncertain=new List<Dictionary<string,object>>();
    foreach(var item in added){string fingerprint=Fingerprint(item);int match=remaining.FindIndex(x=>Fingerprint(x)==fingerprint);if(match>=0){remaining.RemoveAt(match);continue;}if(old.Any(x=>SameType(x,item)))uncertain.Add(item);else merged.Add(item);}
    result[section]=merged;if(uncertain.Count>0)pending[section]=uncertain;
   }
  }
  var combined=new Dictionary<string,object>(oldMetadata);foreach(var field in metadata)if(field.Key!="coverage"&&field.Key!="customCharacters")combined[field.Key]=field.Value;
  if(metadata.TryGetValue("customCharacters",out raw)){object oldCustom;var custom=oldMetadata.TryGetValue("customCharacters",out oldCustom)?Rows(oldCustom):new List<Dictionary<string,object>>();foreach(var row in Rows(raw)){int index=custom.FindIndex(x=>Text(x,"identity")==Text(row,"identity"));if(index<0)custom.Add(row);else{var updated=new Dictionary<string,object>(custom[index]);foreach(var field in row)if(field.Value!=null)updated[field.Key]=field.Value;custom[index]=updated;}}combined["customCharacters"]=custom;}
  var combinedCoverage=oldMetadata.TryGetValue("coverage",out raw)?new Dictionary<string,object>(Map(raw)):new Dictionary<string,object>();foreach(var field in coverage)combinedCoverage[field.Key]=field.Value;combined["coverage"]=combinedCoverage;
  var pendingCombined=oldMetadata.TryGetValue("pendingEquipment",out raw)?new Dictionary<string,object>(Map(raw)):new Dictionary<string,object>();
  foreach(string section in new[]{"weapons","artifacts"}){
   if(Text(coverage,section)=="full")pendingCombined.Remove(section);
   else if(Text(coverage,section)=="equipped"&&pendingCombined.TryGetValue(section,out raw)){
    var prior=Rows(raw);object observations;var observed=metadata.TryGetValue("equipmentObserved",out observations)?Map(observations):new Dictionary<string,object>();
    var owners=observed.TryGetValue(section,out observations)?new HashSet<string>(((IEnumerable)observations).Cast<object>().Select(Convert.ToString)):new HashSet<string>();
    var retained=prior.Where(x=>!owners.Contains(Text(x,"location"))).ToList();pendingCombined.Remove(section);if(retained.Count>0)pendingCombined[section]=retained;
   }
  }
  foreach(var section in pending){object retained;var items=pendingCombined.TryGetValue(section.Key,out retained)?Rows(retained):new List<Dictionary<string,object>>();foreach(var row in Rows(section.Value)){items.RemoveAll(x=>Text(x,"location")==Text(row,"location")&&(section.Key=="weapons"||Text(x,"slotKey")==Text(row,"slotKey")));items.Add(row);}if(items.Count>0)pendingCombined[section.Key]=items;}
  if(pendingCombined.Count>0)combined["pendingEquipment"]=pendingCombined;else combined.Remove("pendingEquipment");
  if(incoming.ContainsKey("materialInventory"))result["materialInventory"]=MaterialInventory.Merge(previous,incoming);
  result["catheryne"]=combined;return result;
 }
}
