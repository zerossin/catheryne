using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

// Source discovery and prose live in the same immutable knowledge cache.
// Official account skills remain in the existing pinned account snapshot.
internal static class TheaterCombatReference {
 internal static readonly string[][] Sources={
  new[]{"teams","https://keqingmains.com/misc/team-building/"},
  new[]{"reactions","https://library.keqingmains.com/combat-mechanics/elemental-effects/transformative-reactions"},
  new[]{"amplifying","https://library.keqingmains.com/combat-mechanics/elemental-effects/amplifying-reactions"},
  new[]{"additive","https://library.keqingmains.com/combat-mechanics/elemental-effects/additive-reactions"},
  new[]{"gauge","https://library.keqingmains.com/combat-mechanics/elemental-effects/elemental-gauge-theory"},
  new[]{"damage","https://library.keqingmains.com/combat-mechanics/damage/damage-formula"},
  new[]{"lunar","https://keqingmains.com/misc/lunar-reactions/"},
  new[]{"stellar","https://keqingmains.com/misc/stellar/"},
  new[]{"guides","https://keqingmains.com/"},
  new[]{"statistics",TheaterStatistics.Url}
 };
 internal static string CleanHtml(string html){
  html=Regex.Replace(html,@"<(script|style|nav|header|footer)\b[^>]*>.*?</\1\s*>","",RegexOptions.IgnoreCase|RegexOptions.Singleline);
  var content=Regex.Match(html,@"<(?:main|article)\b[^>]*>(.*?)</(?:main|article)\s*>",RegexOptions.IgnoreCase|RegexOptions.Singleline);if(content.Success)html=content.Groups[1].Value;
  html=Regex.Replace(html,@"<(?:br|/p|/li|/h[1-6]|/tr|/div)\b[^>]*>","\n",RegexOptions.IgnoreCase);html=WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]+>"," "));
  return string.Join("\n",html.Split('\n').Select(x=>Regex.Replace(x,@"\s+"," ").Trim()).Where(x=>x.Length>0));
 }
 internal static Dictionary<string,object> GuideIndex(string html){
  var result=new Dictionary<string,object>();foreach(Match match in Regex.Matches(html,"href=[\"'](?<url>(?:https://keqingmains.com)?/q/[a-z0-9-]+-quickguide/)[\"']",RegexOptions.IgnoreCase)){
   string url=match.Groups["url"].Value;if(url.StartsWith("/"))url="https://keqingmains.com"+url;string slug=new Uri(url).Segments.Last().Trim('/').Replace("-quickguide","");result[AccountIdentity.Normalize(slug)]=url;
  }return result;
 }
 internal static Dictionary<string,object> Fetch(string url){
  var uri=new Uri(url);if(uri.Scheme!="https"||!new[]{"keqingmains.com","library.keqingmains.com","genshin-builds.com"}.Contains(uri.Host))throw new ArgumentException("Invalid combat reference host");
  var request=HttpTransport.Create(url);request.Timeout=8000;request.ReadWriteTimeout=8000;request.AllowAutoRedirect=true;request.UserAgent="Catheryne/0.2 reference-cache";
  using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){
   string html=reader.ReadToEnd();if(html.Length>3000000)throw new InvalidDataException("Combat reference response too large");if(response.ResponseUri.Scheme!="https"||!new[]{"keqingmains.com","library.keqingmains.com","feelcrafting.com","genshin-builds.com"}.Contains(response.ResponseUri.Host))throw new InvalidDataException("Unexpected reference redirect");
   if(url==TheaterStatistics.Url)return TheaterStatistics.Parse(html);
   string text=CleanHtml(html);if(text.Length<100||text.Length>500000)throw new InvalidDataException("Incomplete combat reference");string version="";var stamp=Regex.Match(text,@"(?:Updated for|Version:)\s*(?:Version\s*)?([0-9]+\.[0-9]+)",RegexOptions.IgnoreCase);if(stamp.Success)version=stamp.Groups[1].Value;
   var document=new Dictionary<string,object>{{"source",url},{"checkedAt",DateTime.UtcNow.ToString("o")},{"authority","primary_theorycrafting"},{"version",version},{"text",text}};if(url=="https://keqingmains.com/")document["links"]=GuideIndex(html);return document;
  }
 }
 internal static string GuideUrl(Dictionary<string,object> index,Dictionary<string,object> character){
  string name=AccountIdentity.Name(character,null,"en"),key=AccountIdentity.Key(character);foreach(string value in new[]{name,key}){string normalized=AccountIdentity.Normalize(value);if(index.ContainsKey(normalized))return CodexChat.S(index,normalized);}
  string last=name.Split(' ').Last();var suffix=AccountIdentity.Normalize(last);return suffix.Length>2&&index.ContainsKey(suffix)?CodexChat.S(index,suffix):"";
 }
 internal static object[] OfficialSkills(Dictionary<string,object> character,string field){
  object raw;var official=character.TryGetValue("hoyolab",out raw)?CodexChat.Map(raw):new Dictionary<string,object>();if(!official.TryGetValue(field,out raw))return new object[0];
  return CodexChat.Items(raw).Select(row=>(object)row.Where(p=>new[]{"id","name","level","desc","description","skill_type","is_actived"}.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value is string?(object)CleanHtml((string)p.Value):p.Value)).ToArray();
 }
}
