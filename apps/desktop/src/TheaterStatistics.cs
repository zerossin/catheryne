using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

// Public, read-only example teams. Missing cohort metadata never becomes a
// combat score or a guessed current-season sample.
internal static class TheaterStatistics {
 internal const string Url="https://genshin-builds.com/en/theater/teams";
 static object Decode(object value,int depth=0){
  if(depth>12)throw new ArgumentException("Statistics nesting too deep");var pair=value as IEnumerable;if(pair==null||value is string||value is IDictionary)throw new ArgumentException("Invalid statistics serialization");var rows=pair.Cast<object>().ToArray();if(rows.Length!=2||!(rows[0] is int))throw new ArgumentException("Invalid statistics value");
  int type=Convert.ToInt32(rows[0]);if(type==0){var map=rows[1] as Dictionary<string,object>;return map==null?rows[1]:map.ToDictionary(p=>p.Key,p=>Decode(p.Value,depth+1));}if(type==1){var list=rows[1] as IEnumerable;if(list==null||rows[1] is string||rows[1] is IDictionary)throw new ArgumentException("Invalid statistics array");return list.Cast<object>().Select(x=>Decode(x,depth+1)).ToArray();}throw new ArgumentException("Unsupported statistics value type");
 }
 internal static Dictionary<string,object> Parse(string html){
  var tag=Regex.Matches(html,@"<astro-island\b[^>]*>",RegexOptions.IgnoreCase).Cast<Match>().FirstOrDefault(m=>Regex.IsMatch(m.Value,"component-url=\"/_astro/Teams\\.[^\"]+\\.js\""));if(tag==null)throw new ArgumentException("Statistics contract changed");
  var attribute=Regex.Match(tag.Value,"props=\"([^\"]+)\"");if(!attribute.Success)throw new ArgumentException("Statistics data missing");var encoded=CatheryneTools.Json().DeserializeObject(WebUtility.HtmlDecode(attribute.Groups[1].Value));var data=CodexChat.Map(Decode(new object[]{0,encoded}));object raw;if(!data.TryGetValue("party",out raw))throw new ArgumentException("Statistics teams missing");
  var rounds=new Dictionary<string,object>();foreach(var round in CodexChat.Map(raw)){
   int act;if(!int.TryParse(round.Key,out act)||act<1||act>40)throw new ArgumentException("Invalid statistics round");var teams=new List<object>();foreach(var row in CodexChat.Items(round.Value).Take(100)){
    string[] ids=CodexChat.S(row,"id").Split(',');double rate;if(ids.Length!=4||ids.Distinct().Count()!=4||ids.Any(id=>!Regex.IsMatch(id,@"^\d{8}$"))||!double.TryParse(CodexChat.S(row,"value"),NumberStyles.Float,CultureInfo.InvariantCulture,out rate)||double.IsNaN(rate)||double.IsInfinity(rate)||rate<0||rate>1)throw new ArgumentException("Invalid public team statistics");
    teams.Add(new{gameIds=ids,reportedRate=rate});
   }rounds[round.Key]=teams;
  }if(rounds.Count==0)throw new ArgumentException("Empty statistics");
  var dates=Regex.Matches(TheaterCombatReference.CleanHtml(html),@"\b[A-Z][a-z]{2} \d{1,2}, 20\d{2}\b").Cast<Match>().Select(m=>m.Value).Distinct().Take(6).ToArray();
  return new Dictionary<string,object>{{"source",Url},{"checkedAt",DateTime.UtcNow.ToString("o")},{"authority","community_statistics"},{"publishedDates",dates},{"period",null},{"difficulty",null},{"sampleSize",null},{"eligibleForRanking",false},{"teamsByRound",rounds},{"text","Public example teams only. Source dates do not establish the sample's season or difficulty. Sample size, recruitment/cast restrictions, constellations and clear-selection bias are unknown. Reported popularity is not win rate or damage. Do not use these rates as ranking weights. Reconcile any candidate with the current season, registered cast, actual recruited availability, builds, enemy mechanics, reactions and vigor. At most the first 100 examples per displayed round are cached."}};
 }
}
