using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// A read-only projection of the same inventory, build criteria and roll tables used elsewhere.
internal sealed class ArtifactDecision {
 public Dictionary<string,object> Artifact {get;set;}
 public string Status {get;set;} public string Reason {get;set;} public string SuggestedUse {get;set;}
 public double? UsefulRolls {get;set;} public double? PotentialRolls {get;set;}
 public string[] Characters {get;set;}
 public Dictionary<string,object>[] Alternatives {get;set;}
 internal Dictionary<string,double> Stats;internal bool Complete;internal string Bucket;
}
internal sealed class ArtifactReviewReport {
 public ArtifactDecision[] Items {get;set;} public string Error {get;set;}
}
internal static class ArtifactReview {
 internal static readonly string[] States={"candidate","level","keep","protected","review"};
 internal const string Help="장착·잠금·저장 빌드는 보호합니다. 정리 후보는 같은 세트·부위·주옵션에서 모든 부옵션이 같거나 더 좋고, 적어도 한 옵션이 더 좋은 대체품을 두 개 이상 확인한 항목입니다. 보관·강화 확인은 현재 역할 기준에서 유효 옵션 4롤을 참고선으로 사용합니다. 미강화품은 남은 강화의 최대 가능성까지 비교하며 성공 확률은 계산하지 않습니다. 유효 롤은 피해량이 아닙니다. 새로운 캐릭터·특수 조합의 활용까지 보장하지 않으므로 실제 소비 전에 게임에서 현재 상태를 확인하세요.";
 internal static string Label(string state){return Locale.T(state=="candidate"?"정리 후보":state=="level"?"강화 확인":state=="keep"?"보관":state=="protected"?"보호":"판단 보류");}
 internal static Color Color(string state){return state=="candidate"?System.Windows.Media.Color.FromRgb(235,173,117):state=="level"?System.Windows.Media.Color.FromRgb(123,184,235):state=="keep"||state=="protected"?System.Windows.Media.Color.FromRgb(107,182,150):System.Windows.Media.Color.FromRgb(155,167,186);}
 static string S(Dictionary<string,object> row,string key){return CodexChat.S(row,key);}
 internal static string Fingerprint(Dictionary<string,object> row){
  object raw;var subs=row.TryGetValue("substats",out raw)?CodexChat.Items(raw).OrderBy(x=>S(x,"key")).Select(x=>new{key=S(x,"key"),value=S(x,"value")}).ToArray():null;
  return CatheryneTools.Json().Serialize(new{set=S(row,"setKey"),game=S(row,"setGameId"),slot=S(row,"slotKey"),rarity=S(row,"rarity"),level=S(row,"level"),main=S(row,"mainStatKey"),subs});
 }
 static bool MainFits(BuildCriterion criterion,Dictionary<string,object> item){
  string slot=S(item,"slotKey"),main=S(item,"mainStatKey");if(slot=="flower"||slot=="plume")return true;
  return (slot=="sands"?criterion.Sands:slot=="goblet"?criterion.Goblet:criterion.Circlet).Contains(main);
 }
 internal static ArtifactReviewReport Load(string root,Dictionary<string,object> snapshot,Dictionary<string,object>[] artifacts=null){
  artifacts=artifacts??AccountMerge.Inventory(snapshot,"artifacts");var criteria=new List<KeyValuePair<string,BuildCriterion>>();
  foreach(var character in AccountMerge.Inventory(snapshot,"characters")){var c=CharacterBuild.Evaluate(root,snapshot,character).Criterion;if(c!=null)criteria.Add(new KeyValuePair<string,BuildCriterion>(AccountIdentity.Name(character),c));}
  // Include maintained defaults for unowned characters and off-pieces; absence from today's roster is not disposal evidence.
  var catalog=BuildCriteriaCatalog.Read(root);
  if(catalog!=null)foreach(string id in catalog.Profiles.Keys){var c=BuildCriteriaCatalog.Criterion(root,new Dictionary<string,object>{{"gameId",id}});if(c!=null)criteria.Add(new KeyValuePair<string,BuildCriterion>("",c));}
  Dictionary<string,object>[] saved;
  try{saved=new EndgameService(root).ReservedArtifacts(snapshot);}catch(Exception error){new AppDiagnostics(root).Write(DiagnosticEvent.DataRefreshFailure,error);return Assess(artifacts,criteria.ToArray(),new Dictionary<string,object>[0],true);}
  return Assess(artifacts,criteria.ToArray(),saved);
 }
 internal static ArtifactReviewReport Assess(Dictionary<string,object>[] artifacts,KeyValuePair<string,BuildCriterion>[] criteria,Dictionary<string,object>[] reserved,bool protectionUnknown=false){
  var ids=new HashSet<string>(reserved.Select(x=>S(x,"id")).Where(x=>x.Length>0));
  var signatures=new HashSet<string>(reserved.Select(Fingerprint));
  var report=new ArtifactReviewReport{Error=protectionUnknown?"저장 빌드를 확인하지 못해 정리 판단을 보류했습니다.":null};
  var items=artifacts.Select(a=>new ArtifactDecision{Artifact=a,Status="review",Reason="판단에 필요한 자료가 부족합니다.",Characters=new string[0],Alternatives=new Dictionary<string,object>[0]}).ToArray();report.Items=items;
  foreach(var item in items){
   var a=item.Artifact;object raw;
   if(S(a,"location").Length>0){item.Status="protected";item.Reason="현재 장착 중입니다.";continue;}
   if(a.TryGetValue("lock",out raw)&&Equals(raw,true)){item.Status="protected";item.Reason="게임에서 잠근 성유물입니다.";continue;}
   if(ids.Contains(S(a,"id"))||signatures.Contains(Fingerprint(a))){item.Status="protected";item.Reason="저장한 빌드에 사용한 성유물입니다.";continue;}
   if(protectionUnknown){item.Reason=report.Error;continue;}
   int rarity,level;
   if(!a.ContainsKey("location")||a["location"]==null||!a.TryGetValue("lock",out raw)||!(raw is bool)||S(a,"inventoryMatch")=="unresolved")continue;
   if(!int.TryParse(S(a,"rarity"),out rarity)||rarity!=5){item.Reason="낮은 등급의 전용 세트 활용은 직접 확인하세요.";continue;}
   if(!int.TryParse(S(a,"level"),out level)||level<0||level>20||S(a,"setKey").Length==0||!a.TryGetValue("substats",out raw)||raw==null)continue;
   string slot=S(a,"slotKey"),main=S(a,"mainStatKey");
   string[] allowed=slot=="flower"?new[]{"hp"}:slot=="plume"?new[]{"atk"}:slot=="sands"?new[]{"atk_","hp_","def_","eleMas","enerRech_"}:slot=="goblet"?new[]{"atk_","hp_","def_","eleMas","physical_dmg_","pyro_dmg_","hydro_dmg_","cryo_dmg_","electro_dmg_","anemo_dmg_","geo_dmg_","dendro_dmg_"}:slot=="circlet"?new[]{"atk_","hp_","def_","eleMas","critRate_","critDMG_","heal_"}:new string[0];
   var subs=CodexChat.Items(raw).ToArray();var score=BuildAnalysis.Score(a,new string[0]);
   if(!allowed.Contains(main)||subs.Any(x=>S(x,"key")==main)||subs.Length<(level>=4?4:3)||score.Status!="calculated"||score.RollMaximum<(3+level/4)*70){item.Reason="강화 수치 또는 옵션을 확인하지 못했습니다.";continue;}
   item.Complete=true;item.Stats=subs.ToDictionary(x=>S(x,"key"),x=>Convert.ToDouble(x["value"],CultureInfo.InvariantCulture));item.Bucket=S(a,"setKey")+"|"+slot+"|"+main;
   var compatible=criteria.Where(c=>MainFits(c.Value,a)).ToArray();
   var rolls=subs.ToDictionary(x=>S(x,"key"),x=>(BuildAnalysis.Score(a,new[]{S(x,"key")}).UsefulMaximum??0)/100.0);
   var values=new Dictionary<string,Tuple<double,double>>();
   foreach(var c in compatible){
    string key=string.Join(",",c.Value.Useful.OrderBy(x=>x));if(values.ContainsKey(key))continue;
    double current=c.Value.Useful.Sum(stat=>rolls.ContainsKey(stat)?rolls[stat]:0);
    bool future=subs.Any(x=>c.Value.Useful.Contains(S(x,"key")))||subs.Length<4&&c.Value.Useful.Any(x=>x!=main);
    values[key]=Tuple.Create(current,current+(future?(5-level/4):0));
   }
   if(values.Count>0){
    item.UsefulRolls=values.Values.Max(x=>x.Item1);item.PotentialRolls=values.Values.Max(x=>x.Item2);
    item.Characters=compatible.Where(c=>c.Key.Length>0&&values[string.Join(",",c.Value.Useful.OrderBy(x=>x))].Item2>=4).Select(c=>c.Key).Distinct().Take(3).ToArray();
    if(level<20&&item.PotentialRolls>=4){item.Status="level";item.Reason=subs.Length<4?"네 번째 옵션과 남은 강화 결과를 확인할 가치가 있습니다.":"남은 강화에서 유효 옵션이 늘어날 수 있습니다.";}
    else if(item.UsefulRolls>=4){item.Status="keep";item.Reason="현재 기준에서 활용 가능한 유효 옵션이 있습니다.";}
    else item.Reason="보유 장비와 운용을 비교한 뒤 결정하세요.";
   }
  }
  // Each candidate must have two distinct alternatives that dominate even its best possible remaining rolls.
  foreach(var group in items.Where(x=>x.Complete).GroupBy(x=>x.Bucket)){
   var alternatives=group.Where(x=>S(x.Artifact,"level")=="20").ToArray();
   foreach(var item in group.Where(x=>x.Status!="protected")){
    var replacements=alternatives.Where(other=>other!=item&&Dominates(other,item)).GroupBy(x=>S(x.Artifact,"id").Length>0?"id:"+S(x.Artifact,"id"):"stats:"+Fingerprint(x.Artifact)).Select(x=>x.First()).Take(2).ToArray();
    if(replacements.Length<2)continue;
    item.Status="candidate";item.Reason="같은 세트·부위·주옵션에서 부옵션이 같거나 더 좋은 대체품 두 개를 확인했습니다.";item.SuggestedUse="enhancement_material";item.Alternatives=replacements.Select(x=>x.Artifact).ToArray();
   }
  }
  return report;
 }
 static bool Dominates(ArtifactDecision better,ArtifactDecision item){
  int remaining=5-int.Parse(S(item.Artifact,"level"))/4;if(remaining>0)return false; // Unknown roll magnitudes cannot be replaced by a damage estimate.
  bool stronger=better.Stats.Keys.Any(key=>!item.Stats.ContainsKey(key));foreach(var stat in item.Stats){double left;better.Stats.TryGetValue(stat.Key,out left);if(left+1e-9<stat.Value)return false;if(left>stat.Value+1e-9)stronger=true;}return stronger;
 }
 internal static object Query(string root,int offset,string query){
  Dictionary<string,object> pointer;var snapshot=new CatheryneTools(root).AccountSnapshot(out pointer);var report=Load(root,snapshot);var rows=report.Items.AsEnumerable();
  if(!string.IsNullOrWhiteSpace(query)){rows=rows.Where(x=>x.Status==query||S(x.Artifact,"id")==query||AccountIdentity.Name(x.Artifact).IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0);}
  var filtered=rows.ToArray();return new{available=snapshot.ContainsKey("artifacts")&&snapshot["artifacts"]!=null,total=filtered.Length,items=filtered.Skip(offset).Take(30).ToArray(),next_offset=offset+30<filtered.Length?(object)(offset+30):null,snapshot=pointer,error=report.Error,method=Help,guide=BuildAnalysis.ScoreGuide,automatic_disposal=false};
 }
 internal static FrameworkElement Detail(ArtifactDecision item,Action<string> ask,Action<Dictionary<string,object>> select){
  var body=new StackPanel();body.Children.Add(PanelUi.Text(Locale.T(item.Reason)));
  if(item.UsefulRolls.HasValue){body.Children.Add(PanelUi.Row(Locale.T("유효 롤"),PanelUi.Text(item.UsefulRolls.Value.ToString("0.0"))));if(S(item.Artifact,"level")!="20")body.Children.Add(PanelUi.Row(Locale.T("강화 후 최대"),PanelUi.Text(item.PotentialRolls.Value.ToString("0.0"))));}
  if(item.SuggestedUse!=null)body.Children.Add(PanelUi.Row(Locale.T("권장 용도"),PanelUi.Text(Locale.T("강화 재료")),Locale.T("이미 강화한 성유물은 합성보다 강화 재료로 활용하는 것을 우선 검토하세요.")));
  if(item.Characters.Length>0)body.Children.Add(PanelUi.Text(string.Join(", ",item.Characters),true));
  if(item.Alternatives.Length>0){var alternatives=new StackPanel();foreach(var a in item.Alternatives){var other=a;var button=PanelUi.Button(AccountIdentity.Name(a)+"  +"+S(a,"level"));button.Click+=(s,e)=>select(other);alternatives.Children.Add(button);object raw;if(a.TryGetValue("substats",out raw)){var stats=PanelUi.Text(string.Join("   ",CodexChat.Items(raw).Select(stat=>GameCatalog.Name(S(stat,"key"))+" "+S(stat,"value")+(S(stat,"key").EndsWith("_")?"%":""))),true);stats.Margin=new Thickness(0,0,0,12);alternatives.Children.Add(stats);}}body.Children.Add(PanelUi.Details(Locale.T("대체 성유물"),alternatives));}
  if(ask!=null){var consult=PanelUi.Button(Locale.T("AI 상담"));consult.Click+=(s,e)=>ask(Locale.Format("성유물 정리를 도와줘. catheryne_query의 artifact_review에서 query={0}로 확인하고, 현재 선택한 성유물은 {1}야. 판정 근거, 다른 캐릭터와 비세트 활용, 남은 강화와 대체품, 저장 빌드를 확인해서 보관/추가 강화/합성 또는 강화 재료 사용 중 무엇이 적절할지 설명해줘. 미확인 자료나 새로운 캐릭터의 활용을 추측하지 말고 실제 성유물을 소비하거나 잠금을 변경하지 마.",(S(item.Artifact,"id").Length>0?S(item.Artifact,"id"):AccountIdentity.Name(item.Artifact)),CatheryneTools.Json().Serialize(item.Artifact)));body.Children.Add(PanelUi.Actions(consult));}
  return PanelUi.SectionHelp(Label(item.Status),Locale.T(Help),body);
 }
}
