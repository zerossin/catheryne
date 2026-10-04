using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class EndgameCandidate {
 public int Slot {get;set;} public string[] Team {get;set;} public string[] Names {get;set;}
 public double UsageRate {get;set;} public double SlotShare {get;set;} public double Used {get;set;} public double Owned {get;set;}
 public double? BuildScore {get;set;} public double BuildKnown {get;set;} public double Coverage {get;set;} public double Rank {get;set;} public string[] Gaps {get;set;}
 public Dictionary<string,object>[] Members {get;set;} public string[] Equipment {get;set;} public bool EquipmentVerified {get;set;}
}
internal static class EndgamePlanning {
 internal const int CandidateLimit=100;
 internal static double DevelopmentFactor(double known,double coverage){return .4+.6*Math.Max(0,Math.Min(100,known+.5*(100-coverage)))/100;}
 internal static Dictionary<string,object> Copy(Dictionary<string,object> value){return CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(value)));}
 internal static string GameId(Dictionary<string,object> character){string id=CodexChat.S(character,"gameId");return id.Length>0?id:GameCatalog.CharacterField(AccountIdentity.Key(character),"gameId");}
 internal static string CanonicalCharacter(string identity){return identity.StartsWith("Traveler")||identity=="game:10000005"||identity=="game:10000007"?"Traveler":identity;}
 internal static Dictionary<string,object>[] Freeze(string root,Dictionary<string,object> snapshot,string[] team){
  var chars=AccountMerge.Inventory(snapshot,"characters");var rows=new List<Dictionary<string,object>>();
  foreach(string key in team){var character=chars.FirstOrDefault(x=>AccountIdentity.Key(x)==key);if(character==null)throw new ArgumentException("보유 자료에서 캐릭터를 확인하지 못했습니다.");
   var member=new Dictionary<string,object>{{"identity",key},{"name",AccountIdentity.Name(character)},{"character",Copy(character.Where(p=>new[]{"identity","key","gameId","names","icon","element","rarity","level","ascension","constellation","talent","talents","stats","attributes"}.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value))}};
   var score=CharacterBuild.Evaluate(root,snapshot,character);member["buildScore"]=score.Total;member["buildKnown"]=score.Known;member["buildCoverage"]=score.Coverage;
   foreach(string section in new[]{"weapons","artifacts"})member[section]=AccountMerge.Equipped(snapshot,section,key).Select(x=>{var item=Copy(x);item["itemIdentity"]=CodexChat.S(x,"id");item["identityVerified"]=CodexChat.S(x,"id").Length>0&&CodexChat.S(x,"inventoryMatch")!="unresolved";return item;}).ToArray();rows.Add(member);
  }return rows.ToArray();
 }
 internal static string[] Gear(Dictionary<string,object>[] members){return members.SelectMany(m=>new[]{"weapons","artifacts"}.SelectMany(s=>CodexChat.Items(m[s]).Where(x=>Equals(x["identityVerified"],true)).Select(x=>s+":"+CodexChat.S(x,"itemIdentity")))).ToArray();}
 internal static bool GearVerified(Dictionary<string,object>[] members){return members.All(m=>CodexChat.Items(m["weapons"]).Count()==1&&CodexChat.Items(m["artifacts"]).Count()==5&&new[]{"weapons","artifacts"}.SelectMany(s=>CodexChat.Items(m[s])).All(x=>Equals(x["identityVerified"],true)));}
 internal static EndgameCandidate[][] Candidates(string root,Dictionary<string,object> snapshot,Dictionary<string,object> season,string mode){
  int slots=mode=="abyss"?2:3;var lists=Enumerable.Range(0,slots).Select(x=>new List<EndgameCandidate>()).ToArray();var chars=AccountMerge.Inventory(snapshot,"characters");var frozen=new Dictionary<string,Dictionary<string,object>>();
  // Exact official English names, not fuzzy guesses or upstream interoperability keys.
  foreach(var stat in CodexChat.Items(season["teams"])){
   var names=EndgameKnowledge.Strings(stat["members"]);var team=new List<string>();bool valid=true;
   foreach(string name in names){var found=chars.Where(c=>AccountIdentity.Normalize(AccountIdentity.Name(c,null,"en"))==AccountIdentity.Normalize(name)).ToArray();if(found.Length!=1){valid=false;break;}team.Add(AccountIdentity.Key(found[0]));}
   if(!valid||team.Select(CanonicalCharacter).Distinct().Count()!=4)continue;
   foreach(string key in team)if(!frozen.ContainsKey(key))frozen[key]=Freeze(root,snapshot,new[]{key})[0];var members=PartyRoles.Order(team.Select(k=>frozen[k]).ToArray());team=members.Select(m=>Convert.ToString(m["identity"])).ToList();
   var gaps=new List<string>();foreach(var m in members){var c=CodexChat.Map(m["character"]);int level;bool known=int.TryParse(CodexChat.S(c,"level"),out level);if(!known||level<70)gaps.Add(Convert.ToString(m["name"])+": "+Locale.T(known?"레벨 육성 필요":"레벨 미확인"));if(Convert.ToDouble(m["buildCoverage"])<100)gaps.Add(Convert.ToString(m["name"])+": "+Locale.T("육성 자료 미확인"));}
   var gear=Gear(members);if(gear.Distinct().Count()!=gear.Length)continue;bool verified=GearVerified(members);double coverage=members.Average(m=>Convert.ToDouble(m["buildCoverage"])),knownPoints=members.Average(m=>Convert.ToDouble(m["buildKnown"]));double? build=coverage>=100?members.Average(m=>Convert.ToDouble(m["buildScore"])):(double?)null;
   var shares=((System.Collections.IEnumerable)stat["shares"]).Cast<object>().Select(Convert.ToDouble).ToArray();double used=Convert.ToDouble(stat["used"]),owned=Convert.ToDouble(stat["owned"]),rate=Convert.ToDouble(stat["usageRate"]);
   for(int slot=0;slot<slots;slot++){if(shares[slot]<=0)continue;double readiness=DevelopmentFactor(knownPoints,coverage);double levelFactor=members.Average(m=>{int n;return int.TryParse(CodexChat.S(CodexChat.Map(m["character"]),"level"),out n)?Math.Max(.1,Math.Min(1,n/80.0)):.5;});double rank=Math.Log(((used+.5)/(owned+1))*shares[slot]/100)+Math.Log(readiness*levelFactor);
    lists[slot].Add(new EndgameCandidate{Slot=slot+1,Team=team.ToArray(),Names=members.Select(m=>Convert.ToString(m["name"])).ToArray(),Members=members,UsageRate=rate,SlotShare=shares[slot],Used=used,Owned=owned,BuildScore=build,BuildKnown=knownPoints,Coverage=coverage,Rank=rank,Gaps=gaps.Distinct().ToArray(),Equipment=gear,EquipmentVerified=verified});}
  }
  return lists.Select(l=>l.OrderByDescending(c=>c.Rank).ThenByDescending(c=>c.Used).ThenBy(c=>string.Join(",",c.Team)).Take(CandidateLimit).ToArray()).ToArray();
 }
 internal static List<EndgameCandidate[]> Assign(EndgameCandidate[][] candidates,bool unique,int count=3){
  var result=new List<EndgameCandidate[]>();if(candidates.Any(x=>x.Length==0))return result;Action<int,List<EndgameCandidate>,double> search=null;
  search=(slot,chosen,score)=>{if(slot==candidates.Length){result.Add(chosen.ToArray());result=result.OrderByDescending(x=>x.Sum(c=>c.Rank)).ThenBy(x=>string.Join(";",x.Select(c=>string.Join(",",c.Team)))).Take(count).ToList();return;}
   double upper=score+candidates.Skip(slot).Sum(x=>x[0].Rank);if(result.Count==count&&upper<result.Last().Sum(c=>c.Rank))return;
   var used=new HashSet<string>(chosen.SelectMany(c=>c.Team.Select(CanonicalCharacter)));var gear=new HashSet<string>(chosen.SelectMany(c=>c.Equipment));
   foreach(var next in candidates[slot]){double optimistic=score+next.Rank+candidates.Skip(slot+1).Sum(x=>x[0].Rank);if(result.Count==count&&optimistic<result.Last().Sum(c=>c.Rank))break;if(unique&&(next.Team.Select(CanonicalCharacter).Any(used.Contains)||next.Equipment.Any(gear.Contains)))continue;chosen.Add(next);search(slot+1,chosen,score+next.Rank);chosen.RemoveAt(chosen.Count-1);}
  };search(0,new List<EndgameCandidate>(),0);return result;
 }
 internal static string[] Conflicts(Dictionary<string,object>[] slots,bool unique){var conflicts=new List<string>();var chars=new HashSet<string>();var gear=new HashSet<string>();foreach(var slot in slots){var ownChars=new HashSet<string>();var ownGear=new HashSet<string>();foreach(string id in EndgameKnowledge.Strings(slot["team"])){string key=CanonicalCharacter(id);if(!ownChars.Add(key)||unique&&!chars.Add(key))conflicts.Add("character:"+key);}foreach(string id in Gear(CodexChat.Items(slot["members"]).ToArray()))if(!ownGear.Add(id)||unique&&!gear.Add(id))conflicts.Add(id);}return conflicts.Distinct().ToArray();}
}
