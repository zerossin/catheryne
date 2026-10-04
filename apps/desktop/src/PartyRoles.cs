using System;
using System.Collections.Generic;
using System.Linq;

// Default seats, not a combat rotation. Flexible drivers lead only without a carry.
// Role references: KQM character guides (https://keqingmains.com/).
internal static class PartyRoles {
 static readonly Dictionary<string,object> roles=Load();
 static Dictionary<string,object> Load(){string file=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog","party-seats.json");return System.IO.File.Exists(file)?CodexChat.Map(StoryClient.Read(file)["roles"]):new Dictionary<string,object>();}
 static bool Has(string key,string role){object value;return roles.TryGetValue(key,out value)&&((System.Collections.IEnumerable)value).Cast<object>().Any(x=>Convert.ToString(x)==role);}
 static string Key(Dictionary<string,object> member){return AccountIdentity.Normalize(AccountIdentity.Name(CodexChat.Map(member["character"]),null,"en"));}
 internal static Dictionary<string,object>[] Order(Dictionary<string,object>[] members){
  // A newly released or flexible role must not be silently classified as support.
  if(members.Any(m=>!roles.ContainsKey(Key(m))))return members;
  bool hasCarry=members.Any(m=>Has(Key(m),"carry"));return members.OrderBy(m=>{string key=Key(m);return Has(key,"carry")||!hasCarry&&Has(key,"driver")?0:Has(key,"offField")?1:Has(key,"sustain")?3:2;}).ToArray();
 }

 internal static string Difficulty(int level){string[] names={"보통","숙련","어려움","위험","극한","절망"};return Locale.Format("난이도 {0} · {1}",level,level>=1&&level<=6?Locale.T(names[level-1]):Locale.T("미확인"));}
}
