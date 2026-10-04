using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal sealed class BuildRoleMetadata {
 public string Element {get;set;} public string WeaponType {get;set;} public double Damage {get;set;} public double Physical {get;set;} public double Healing {get;set;}
}
internal sealed class BuildCriteriaRules {
 public Dictionary<string,string> Calculations {get;set;} public Dictionary<string,Dictionary<string,string>> Weapons {get;set;} public Dictionary<string,Dictionary<string,string>> Sets {get;set;} public Dictionary<string,string> WeaponIds {get;set;}
}
internal sealed class BuildCriteriaData {
 public Dictionary<string,string> CharacterNames {get;set;} public string IdentityRevision {get;set;} public string OptimizerRevision {get;set;} public Dictionary<string,BuildRoleMetadata> Roles {get;set;} public BuildCriteriaRules Rules {get;set;} public int Schema {get;set;} public string Revision {get;set;} public string CheckedUtc {get;set;} public string Error {get;set;}
 public Dictionary<string,Dictionary<string,double>> Profiles {get;set;}
 public Dictionary<string,string> Keys {get;set;}
}
// Import numeric default weights only. Remote JavaScript is never executed.
internal static class BuildCriteriaCatalog {
 internal const string Source="https://github.com/yoimiya-kokomi/miao-plugin";
 static readonly object sync=new object();static readonly HashSet<string> pending=new HashSet<string>();
 static string FileName(string root){return Path.Combine(root,"cache","build-criteria.json");}
 internal static DateTime Revision(string root){return File.GetLastWriteTimeUtc(FileName(root));}
 internal static BuildCriteriaData Parse(string weights,string characters,string ids,string revision,BuildCriteriaRules rules=null){
  var names=new Dictionary<string,Dictionary<string,double>>();var traits=new Dictionary<string,BuildRoleMetadata>();
  var map=new Dictionary<string,string>{{"hp","hp_"},{"atk","atk_"},{"def","def_"},{"cpct","critRate_"},{"cdmg","critDMG_"},{"mastery","eleMas"},{"recharge","enerRech_"}};
  foreach(Match row in Regex.Matches(weights,@"(?m)^\s*([^\s:,{]+)\s*:\s*\{([^{}]+)\},?\s*$")){
   if(!Regex.IsMatch(row.Groups[2].Value,@"^\s*(?:\w+\s*:\s*\d+\s*,\s*)*\w+\s*:\s*\d+\s*,?\s*$"))throw new InvalidDataException("Artifact weight grammar changed");
   var values=new Dictionary<string,double>();var role=new BuildRoleMetadata();
   foreach(Match field in Regex.Matches(row.Groups[2].Value,@"(\w+)\s*:\s*(\d+)\s*(?:,|$)")){
    string key;double value=int.Parse(field.Groups[2].Value);if(value>100)throw new InvalidDataException("Invalid artifact weight");if(field.Groups[1].Value=="dmg")role.Damage=value/100;if(field.Groups[1].Value=="phy")role.Physical=value/100;if(field.Groups[1].Value=="heal")role.Healing=value/100;
    if(map.TryGetValue(field.Groups[1].Value,out key)&&value>0)values[key]=value/100;
   }
   if(values.Count>0){names.Add(row.Groups[1].Value,values);traits.Add(row.Groups[1].Value,role);}
  }
  var records=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(characters);var keys=CatheryneTools.Json().Deserialize<Dictionary<string,string>>(ids);
  var result=new BuildCriteriaData{Schema=2,Revision=revision,Roles=new Dictionary<string,BuildRoleMetadata>(),Rules=rules,Profiles=new Dictionary<string,Dictionary<string,double>>(),Keys=new Dictionary<string,string>()};
  foreach(var row in records){Dictionary<string,double> profile;string name=CodexChat.S(CodexChat.Map(row.Value),"name");if(name=="空"||name=="荧")continue;if(!names.TryGetValue(name,out profile))continue;result.Profiles[row.Key]=profile;var role=traits[name];role.Element=CodexChat.S(CodexChat.Map(row.Value),"elem");role.WeaponType=CodexChat.S(CodexChat.Map(row.Value),"weapon");result.Roles[row.Key]=role;string key;if(keys.TryGetValue(row.Key,out key)&&key!="Traveler")result.Keys[key]=row.Key;}
  if(result.Profiles.Count<90||result.Keys.Count<80)throw new InvalidDataException("Artifact criteria coverage changed");return result;
 }
 static readonly Lazy<BuildCriteriaData> seed=new Lazy<BuildCriteriaData>(Bootstrap);
 static BuildCriteriaData Bootstrap(){string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog","build-criteria");try{var data=Parse(File.ReadAllText(Path.Combine(dir,"weights.txt")),File.ReadAllText(Path.Combine(dir,"characters.json")),File.ReadAllText(Path.Combine(dir,"ids.json")),File.ReadAllText(Path.Combine(dir,"revision.txt")).Trim(),ParseRules(File.ReadAllText(Path.Combine(dir,"rules.json"))));var names=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(dir,"names.json")));data.IdentityRevision=CodexChat.S(names,"revision");data.CharacterNames=CodexChat.Map(names["names"]).ToDictionary(x=>x.Key,x=>Convert.ToString(x.Value));return data;}catch(Exception){return null;}}
 static bool Valid(BuildCriteriaData data){return data!=null&&data.CharacterNames!=null&&data.CharacterNames.Count>=90&&data.CharacterNames.All(x=>Regex.IsMatch(x.Key,@"^\d{8}$")&&!string.IsNullOrWhiteSpace(x.Value))&&Regex.IsMatch(data.IdentityRevision??"","^[a-f0-9]{40}$")&&data.Schema==2&&data.Roles!=null&&data.Roles.Values.All(x=>x!=null)&&data.Rules!=null&&data.Rules.Calculations!=null&&data.Rules.Calculations.Count>=90&&data.Rules.Calculations.Values.All(x=>x!=null)&&data.Rules.Weapons!=null&&data.Rules.Weapons.Count>=200&&data.Rules.Weapons.Values.All(x=>x!=null)&&data.Rules.Sets!=null&&data.Rules.Sets.Count>=40&&data.Rules.Sets.Values.All(x=>x!=null)&&data.Rules.WeaponIds!=null&&data.Profiles!=null&&data.Keys!=null&&data.Profiles.Count>=90&&data.Keys.Count>=80&&data.Profiles.All(x=>data.Roles.ContainsKey(x.Key)&&data.Roles[x.Key]!=null&&new[]{"sword","claymore","polearm","bow","catalyst"}.Contains(data.Roles[x.Key].WeaponType)&&new[]{data.Roles[x.Key].Damage,data.Roles[x.Key].Healing,data.Roles[x.Key].Physical}.All(v=>v>=0&&v<=1&&!double.IsNaN(v))&&x.Value!=null&&x.Value.Count>0&&x.Value.All(v=>BuildAnalysis.StatKeys.Contains(v.Key)&&v.Value>0&&v.Value<=1&&!double.IsNaN(v.Value)))&&data.Keys.All(x=>data.Profiles.ContainsKey(x.Value));}
 static readonly FileReadCache<BuildCriteriaData> files=new FileReadCache<BuildCriteriaData>();
 sealed class CriteriaProjection {internal readonly Dictionary<string,BuildCriterion> Values=new Dictionary<string,BuildCriterion>();}
 static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BuildCriteriaData,CriteriaProjection> projections=new System.Runtime.CompilerServices.ConditionalWeakTable<BuildCriteriaData,CriteriaProjection>();
 static BuildCriteriaData ReadSource(string root){try{return files.Read(FileName(root),file=>{try{var data=CatheryneTools.Json().Deserialize<BuildCriteriaData>(File.ReadAllText(file));return Valid(data)?data:seed.Value;}catch(ArgumentException){return seed.Value;}catch(InvalidOperationException){return seed.Value;}})??seed.Value;}catch(IOException){return seed.Value;}}
 internal static BuildCriteriaData Read(string root){var data=ReadSource(root);if(data==null)return null;return new BuildCriteriaData{Schema=data.Schema,Revision=data.Revision,IdentityRevision=data.IdentityRevision,OptimizerRevision=data.OptimizerRevision,CheckedUtc=data.CheckedUtc,Error=data.Error,CharacterNames=data.CharacterNames==null?null:new Dictionary<string,string>(data.CharacterNames),Keys=new Dictionary<string,string>(data.Keys),Profiles=data.Profiles.ToDictionary(x=>x.Key,x=>new Dictionary<string,double>(x.Value)),Roles=data.Roles.ToDictionary(x=>x.Key,x=>new BuildRoleMetadata{Element=x.Value.Element,WeaponType=x.Value.WeaponType,Damage=x.Value.Damage,Physical=x.Value.Physical,Healing=x.Value.Healing}),Rules=data.Rules==null?null:new BuildCriteriaRules{Calculations=new Dictionary<string,string>(data.Rules.Calculations),WeaponIds=new Dictionary<string,string>(data.Rules.WeaponIds),Weapons=data.Rules.Weapons.ToDictionary(x=>x.Key,x=>new Dictionary<string,string>(x.Value)),Sets=data.Rules.Sets.ToDictionary(x=>x.Key,x=>new Dictionary<string,string>(x.Value))}};}
 static string CharacterId(BuildCriteriaData data,Dictionary<string,object> character){string id=CodexChat.S(character,"gameId"),key=AccountIdentity.Key(character);if(id.Length==0&&key.StartsWith("game:"))id=key.Substring(5);if(id.Length==0)data.Keys.TryGetValue(key,out id);if(string.IsNullOrEmpty(id)&&data.CharacterNames!=null){var matches=data.CharacterNames.Where(x=>AccountIdentity.Normalize(x.Value)==AccountIdentity.Normalize(key)).Select(x=>x.Key).Distinct().ToArray();if(matches.Length==1)id=matches[0];}if(string.IsNullOrEmpty(id))id=GameCatalog.CharacterField(key,"gameId");return id;}
 static double Weight(Dictionary<string,double> values,string key){double value;return values.TryGetValue(key,out value)?value:0;}
 static string Stat(string key){return (key=="hp"||key=="hpPct")?"hp_":(key=="atk"||key=="atkPct")?"atk_":(key=="def"||key=="defPct")?"def_":key=="cpct"?"critRate_":key=="cdmg"?"critDMG_":key=="mastery"?"eleMas":key=="recharge"?"enerRech_":key;}
 // Scan source text for dependencies; never evaluate downloaded code.
 internal static string[] TalentDependencies(string source,bool normalRelevant){
  var entries=new List<string>();int start=source.IndexOf("export const details",StringComparison.Ordinal);int depth=0,first=-1;char quote='\0';bool escape=false;
  if(start>=0)for(int i=source.IndexOf('[',start);i>=0&&i<source.Length;i++){char ch=source[i];if(quote!='\0'){if(escape)escape=false;else if(ch=='\\')escape=true;else if(ch==quote)quote='\0';continue;}if(ch=='\''||ch=='"'||ch=='`'){quote=ch;continue;}if(ch=='{'){if(depth++==0)first=i;}else if(ch=='}'&&depth>0){if(--depth==0&&first>=0)entries.Add(source.Substring(first,i-first+1));}else if(ch==']'&&depth==0)break;}
  int index=0;var selected=Regex.Match(source,@"defDmgIdx\s*=\s*(\d+)");if(selected.Success)int.TryParse(selected.Groups[1].Value,out index);var key=Regex.Match(source,@"defDmgKey\s*=\s*['""`]([^'""`]+)['""`]");if(key.Success){int found=entries.FindIndex(x=>Regex.IsMatch(x,@"dmgKey\s*:\s*['""`]"+Regex.Escape(key.Groups[1].Value)+@"['""`]"));if(found>=0)index=found;}
  string representative=index>=0&&index<entries.Count?entries[index]:"";var result=new List<string>();
  foreach(string field in new[]{"a","e","q"}){string scan=field=="a"?representative:source;if((field!="a"||normalRelevant)&&Regex.IsMatch(scan,@"\btalent\s*(?:\.\s*"+field+@"\b|\[\s*['""`]"+field+@"['""`]\s*\])"))result.Add(field=="a"?"auto":field=="e"?"skill":"burst");}
  return result.ToArray();
 }
 static double EffectAffinity(string text,Dictionary<string,double> weights,BuildRoleMetadata role,bool normalRelevant){
  double score=0;var labels=new Dictionary<string,string>{{"生命值(?:上限)?","hp_"},{"攻击力","atk_"},{"防御力","def_"},{"元素精通","eleMas"},{"元素充能效率","enerRech_"},{"暴击率","critRate_"},{"暴击伤害","critDMG_"}};
  foreach(var label in labels)if(Regex.IsMatch(text,label.Key+@"(?:额外)?(?:提高|提升|增加|加成)"))score=Math.Max(score,Weight(weights,label.Value));
  var elements=new Dictionary<string,string>{{"火元素","pyro"},{"水元素","hydro"},{"冰元素","cryo"},{"雷元素","electro"},{"风元素","anemo"},{"岩元素","geo"},{"草元素","dendro"}};
  if(text.Contains("治疗"))score=Math.Max(score,role.Healing);
  if(!normalRelevant&&Regex.IsMatch(text,@"普通攻击|重击|下落攻击"))return score;
  bool specific=false;foreach(var element in elements)if(text.Contains(element.Key+"伤害")){specific=true;if(role.Element==element.Value)score=Math.Max(score,role.Damage);}
  if(text.Contains("物理伤害")){specific=true;score=Math.Max(score,role.Physical);}
  if(!specific&&Regex.IsMatch(text,@"伤害(?:提高|提升|增加|加成)|提高.{0,10}伤害|提升.{0,10}伤害"))score=Math.Max(score,role.Damage);
  if(Regex.IsMatch(text,@"队伍.{0,18}(?:提高|提升).{0,10}(?:攻击力|伤害|元素精通)"))score=Math.Max(score,1);
  return Math.Min(1,score);
 }
 internal static BuildCriterion Criterion(string root,Dictionary<string,object> character){
  var data=ReadSource(root);if(data==null)return null;string id=CharacterId(data,character);Dictionary<string,double> weights;BuildRoleMetadata role;if(string.IsNullOrEmpty(id)||!data.Profiles.TryGetValue(id,out weights)||!data.Roles.TryGetValue(id,out role))return null;
  var cache=projections.GetValue(data,d=>new CriteriaProjection());string key=id+"|"+Locale.IsEnglish;lock(cache){BuildCriterion criterion;if(!cache.Values.TryGetValue(key,out criterion)){criterion=Create(data,id,new Dictionary<string,double>(weights),role);cache.Values[key]=criterion;}return CharacterBuild.Copy(criterion);}
 }
 static BuildCriterion Create(BuildCriteriaData data,string id,Dictionary<string,double> weights,BuildRoleMetadata role){
  string calculation;var dependencies=data.Rules.Calculations.TryGetValue(id,out calculation)?TalentDependencies(calculation,Weight(weights,"critRate_")>0||Weight(weights,"critDMG_")>0||role.Physical>0):new[]{"skill","burst"};
  if(dependencies.Length==0&&!(Weight(weights,"eleMas")>0&&role.Damage==0&&role.Healing==0))dependencies=new[]{"skill","burst"};
  var mains=new Dictionary<string,double>(weights);if(role.Damage>0)mains[role.Element+"_dmg_"]=role.Damage;if(role.Physical>0)mains["physical_dmg_"]=role.Physical;if(role.Healing>0)mains["heal_"]=role.Healing;
  var slots=new Dictionary<string,Dictionary<string,double>>();foreach(string slot in new[]{"sands","goblet","circlet"}){var allowed=slot=="sands"?new[]{"atk_","hp_","def_","eleMas","enerRech_"}:slot=="goblet"?new[]{"atk_","hp_","def_","eleMas",role.Element+"_dmg_","physical_dmg_"}:new[]{"atk_","hp_","def_","eleMas","critRate_","critDMG_","heal_"};var options=allowed.ToDictionary(x=>x,x=>Weight(mains,x));double maximum=options.Values.Max();if(maximum==0)options=allowed.ToDictionary(x=>x,x=>1.0);else options=options.ToDictionary(x=>x.Key,x=>x.Value/maximum);slots[slot]=options;}
  var fit=new Dictionary<string,double>();foreach(var row in data.Rules.Weapons){string bonus;row.Value.TryGetValue("bonusKey",out bonus);string type;row.Value.TryGetValue("type",out type);if(type!=role.WeaponType)continue;double value=bonus=="phy"?role.Physical:bonus=="dmg"?role.Damage:string.IsNullOrEmpty(bonus)?Weight(weights,"atk_")*.35:Weight(weights,Stat(bonus));fit["game:"+row.Key]=value;string weaponKey;if(data.Rules.WeaponIds.TryGetValue(row.Key,out weaponKey))fit[weaponKey]=value;}
  var effects=new Dictionary<string,Dictionary<string,double>>();foreach(var set in data.Rules.Sets)effects[set.Key]=set.Value.ToDictionary(x=>x.Key,x=>EffectAffinity(x.Value,weights,role,dependencies.Contains("auto")));
  return new BuildCriterion{Automatic=true,FavoniusCritical=true,Name=Locale.T("자동 역할 기준"),Source=Source,Checked=data.Revision+(string.IsNullOrEmpty(data.Error)?"":"\n"+data.Error),Level=90,Rolls=25,Talents=new[]{"auto","skill","burst"}.ToDictionary(x=>x,x=>dependencies.Contains(x)?9:0),Useful=weights.Keys.ToArray(),UsefulWeights=weights,MainWeights=slots,WeaponFit=fit,SetAffinities=effects,Sands=slots["sands"].Where(x=>x.Value>=.5).Select(x=>x.Key).ToArray(),Goblet=slots["goblet"].Where(x=>x.Value>=.5).Select(x=>x.Key).ToArray(),Circlet=slots["circlet"].Where(x=>x.Value>=.5).Select(x=>x.Key).ToArray(),Sets=effects.Where(x=>x.Value.Any(v=>v.Value>0)&&!x.Key.StartsWith("game:")).Select(x=>x.Key).ToArray(),Weapons=fit.Where(x=>x.Value>0&&!x.Key.StartsWith("game:")).Select(x=>x.Key).ToArray()};
 }

 static string DownloadText(string url){var request=HttpTransport.Create(url);request.UserAgent="Catheryne/1.0";request.Timeout=request.ReadWriteTimeout=15000;using(var response=request.GetResponse())using(var input=response.GetResponseStream())using(var output=new MemoryStream()){var buffer=new byte[8192];int n;while((n=input.Read(buffer,0,buffer.Length))>0){if(output.Length+n>2*1024*1024)throw new InvalidDataException("Criteria response too large");output.Write(buffer,0,n);}return System.Text.Encoding.UTF8.GetString(output.ToArray());}}
 internal static BuildCriteriaRules ParseRules(string text){
  var raw=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(text);var result=new BuildCriteriaRules{Calculations=new Dictionary<string,string>(),Weapons=new Dictionary<string,Dictionary<string,string>>(),Sets=new Dictionary<string,Dictionary<string,string>>(),WeaponIds=new Dictionary<string,string>()};
  foreach(var x in CodexChat.Map(raw["calculations"]))result.Calculations[x.Key]=Convert.ToString(x.Value);
  foreach(var x in CodexChat.Map(raw["weapons"]))result.Weapons[x.Key]=CodexChat.Map(x.Value).ToDictionary(y=>y.Key,y=>Convert.ToString(y.Value));
  foreach(var x in CodexChat.Map(raw["weaponIds"]))result.WeaponIds[x.Key]=Convert.ToString(x.Value);
  var sets=CodexChat.Map(raw["sets"]).Values.Select(CodexChat.Map).ToArray();
  foreach(var x in CodexChat.Map(raw["setNames"])){var set=sets.FirstOrDefault(y=>CodexChat.S(y,"name")==Convert.ToString(x.Value));object skills;if(set!=null&&set.TryGetValue("skills",out skills)){var effects=CodexChat.Map(skills).ToDictionary(y=>y.Key,y=>Convert.ToString(y.Value));result.Sets[x.Key]=effects;result.Sets["game:"+CodexChat.S(set,"id")]=effects;}}
  return result;
 }
 static string SourceRevision(string repository){var commit=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(DownloadText("https://api.github.com/repos/"+repository+"/commits/HEAD"));string sha=CodexChat.S(commit,"sha");if(!Regex.IsMatch(sha,"^[a-f0-9]{40}$"))throw new InvalidDataException("Invalid source revision");return sha;}
 static BuildCriteriaData Download(string root,BuildCriteriaData old){
  var catalog=GameDataCatalog.Read(root);var game=catalog.Game;var sources=CodexChat.Map(catalog.Hoyo["sources"]);string sha=SourceRevision("yoimiya-kokomi/miao-plugin"),identityRevision=CodexChat.S(CodexChat.Map(sources["database"]),"revision"),optimizerRevision=CodexChat.S(game,"revision");
  if(Valid(old)&&old.Revision==sha&&old.IdentityRevision==identityRevision&&old.OptimizerRevision==optimizerRevision)return old;
  var names=CodexChat.Map(catalog.Hoyo["characterNames"]).ToDictionary(x=>x.Key,x=>Convert.ToString(x.Value));
  string license=DownloadText("https://raw.githubusercontent.com/yoimiya-kokomi/miao-plugin/"+sha+"/LICENSE");if(!license.Contains("MIT License")||!license.Contains("Permission is hereby granted"))throw new InvalidDataException("Source license changed");string prefix="https://raw.githubusercontent.com/yoimiya-kokomi/miao-plugin/"+sha+"/resources/meta-gs/";
  string weights=DownloadText(prefix+"artifact/artis-mark.js"),characters=DownloadText(prefix+"character/data.json");var records=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(characters);var calculations=new Dictionary<string,object>();var weapons=new Dictionary<string,object>();
  var jobs=new List<Tuple<string,string,string,string>>();foreach(var row in records){string name=CodexChat.S(CodexChat.Map(row.Value),"name");if(name!="空"&&name!="荧")jobs.Add(Tuple.Create("char",row.Key,"",prefix+"character/"+Uri.EscapeDataString(name)+"/calc.js"));}
  foreach(string type in new[]{"sword","claymore","polearm","bow","catalyst"})foreach(var row in CatheryneTools.Json().Deserialize<Dictionary<string,object>>(DownloadText(prefix+"weapon/"+type+"/data.json")))jobs.Add(Tuple.Create("weapon",row.Key,type,prefix+"weapon/"+type+"/"+Uri.EscapeDataString(CodexChat.S(CodexChat.Map(row.Value),"name"))+"/data.json"));
  Parallel.ForEach(jobs,new ParallelOptions{MaxDegreeOfParallelism=6},job=>{string text;try{text=DownloadText(job.Item4);}catch(System.Net.WebException e){var response=e.Response as System.Net.HttpWebResponse;if(response!=null&&response.StatusCode==System.Net.HttpStatusCode.NotFound){response.Dispose();return;}throw;}lock(calculations){if(job.Item1=="char")calculations[job.Item2]=text;else{var raw=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(text);object attr;weapons[job.Item2]=new Dictionary<string,object>{{"type",job.Item3},{"bonusKey",raw.TryGetValue("attr",out attr)?CodexChat.S(CodexChat.Map(attr),"bonusKey"):""}};}}});
  string go="https://raw.githubusercontent.com/frzyc/genshin-optimizer/"+optimizerRevision+"/";
  var rules=new Dictionary<string,object>{{"calculations",calculations},{"weapons",weapons},{"sets",CatheryneTools.Json().DeserializeObject(DownloadText(prefix+"artifact/data.json"))},{"weaponIds",catalog.Hoyo["weapons"]},{"setNames",CatheryneTools.Json().DeserializeObject(DownloadText(go+"libs/gi/dm-localization/assets/locales/chs/artifactNames_gen.json"))}};
  var result=Parse(weights,characters,CatheryneTools.Json().Serialize(catalog.Hoyo["characters"]),sha,ParseRules(CatheryneTools.Json().Serialize(rules)));result.CharacterNames=names;result.IdentityRevision=identityRevision;result.OptimizerRevision=optimizerRevision;return result;
 }
 internal static BuildCriteriaData Load(string root,bool force=false,Func<BuildCriteriaData> download=null){
  var old=Read(root);var catalog=GameDataCatalog.Read(root);var sources=CodexChat.Map(catalog.Hoyo["sources"]);bool matching=old!=null&&old.IdentityRevision==CodexChat.S(CodexChat.Map(sources["database"]),"revision")&&old.OptimizerRevision==CodexChat.S(catalog.Game,"revision");DateTime checkedAt;if(!force&&matching&&DateTime.TryParse(old.CheckedUtc,out checkedAt)&&DateTime.UtcNow-checkedAt.ToUniversalTime()<TimeSpan.FromHours(string.IsNullOrEmpty(old.Error)?24:1))return old;
  using(var mutex=new Mutex(false,GameDataCatalog.Scope("BuildCriteria",root))){
   bool held=false;try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)return old;
    try{var data=(download??(()=>Download(root,old)))();if(!Valid(data)||old!=null&&data.Profiles.Count<old.Profiles.Count*.8)throw new InvalidDataException("Criteria coverage declined");data.CheckedUtc=DateTime.UtcNow.ToString("o");data.Error=null;Directory.CreateDirectory(Path.GetDirectoryName(FileName(root)));AtomicFile.Write(FileName(root),CatheryneTools.Json().Serialize(data));return data;}
    catch(Exception){if(old==null)return null;old.CheckedUtc=DateTime.UtcNow.ToString("o");old.Error="자동 기준을 갱신하지 못해 마지막 검증 자료를 사용합니다.";Directory.CreateDirectory(Path.GetDirectoryName(FileName(root)));AtomicFile.Write(FileName(root),CatheryneTools.Json().Serialize(old));return old;}
   }finally{if(held)mutex.ReleaseMutex();}
  }
 }
 internal static void QueueRefresh(string root){lock(sync){if(!pending.Add(root))return;}Task.Run(()=>{try{Load(root);}catch(Exception){}finally{lock(sync)pending.Remove(root);}});}
}
