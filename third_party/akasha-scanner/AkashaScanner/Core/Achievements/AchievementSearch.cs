using System.Text.RegularExpressions;
namespace AkashaScanner.Core.Achievements {
 // Exact ASCII substrings only: never transliterate a title into a different title.
 internal sealed class AchievementSearch {
  readonly string[] names;
  internal AchievementSearch(IEnumerable<string> names){this.names=names.ToArray();}
  internal string? Query(int index){
   var runs=Regex.Matches(names[index],"[A-Za-z0-9 ]+").Cast<Match>().Select(m=>m.Value.Trim()).Where(s=>s.Length>=2).ToArray();
   int max=runs.Length==0?0:runs.Max(s=>s.Length);
   for(int length=2;length<=max;length++)foreach(string run in runs)
    for(int start=0;start+length<=run.Length;start++){
     string candidate=run.Substring(start,length);
     if(candidate[0]==' '||candidate[^1]==' ')continue;
     bool unique=true;
     for(int other=0;other<names.Length;other++)if(other!=index&&names[other].Contains(candidate,StringComparison.OrdinalIgnoreCase)){unique=false;break;}
     if(unique)return candidate;
    }
   return null;
  }
  internal static void CheckCatalog(string source,string destination){
   var data=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(source));
   var entries=data["Data"]!.SelectMany(c=>c["Achievements"]!).ToArray();
   var names=entries.Select(e=>(string)e["Name"]!).ToArray();var planner=new AchievementSearch(names);
   var watch=System.Diagnostics.Stopwatch.StartNew();
   var queries=names.Select((name,index)=>planner.Query(index)).ToArray();watch.Stop();
   for(int i=0;i<names.Length;i++)if(queries[i] is string q && (!names[i].Contains(q,StringComparison.OrdinalIgnoreCase)||names.Where((n,j)=>i!=j).Any(n=>n.Contains(q,StringComparison.OrdinalIgnoreCase))))throw new Exception("Catalog collision");
   File.WriteAllText(destination,Newtonsoft.Json.JsonConvert.SerializeObject(new {entries=names.Length,resolved=queries.Count(q=>q!=null),unresolved=queries.Count(q=>q==null),planningMs=watch.ElapsedMilliseconds,originalCharacters=names.Where((n,i)=>queries[i]!=null).Sum(n=>n.Length),queryCharacters=queries.Where(q=>q!=null).Sum(q=>q!.Length),unicode=entries.Select((e,i)=>new {entry=e,index=i}).Where(x=>names[x.index].Any(c=>c>127)).Select(x=>new {name=names[x.index],ids=x.entry["Ids"],query=queries[x.index]})},Newtonsoft.Json.Formatting.Indented));
  }
  internal static void Test(){
   var names=new[]{"Bon Appétit", "Déjà Vu!", "Le Déluge", "Explorer", "Explorer", "The Long Road Home", "The Long Road Away"};
   var planner=new AchievementSearch(names);
   for(int i=0;i<names.Length;i++){
    string? q=planner.Query(i);
    if(i==3||i==4){if(q!=null)throw new Exception("Ambiguous title accepted");continue;}
    if(q==null||q.Any(c=>c>127)||!names[i].Contains(q,StringComparison.OrdinalIgnoreCase)||names.Where((n,j)=>j!=i).Any(n=>n.Contains(q,StringComparison.OrdinalIgnoreCase)))throw new Exception("Unsafe search query");
   }
  }
 }
}
