using System.Collections.Generic;using System.Linq;
internal static class MaterialCatalog {
 internal static Dictionary<string,object> Match(string root,string key){string normalized=AccountIdentity.Normalize(key);var matches=GameDataCatalog.Read(root).Materials.Values.Select(CodexChat.Map).Where(x=>{object aliases;return x.TryGetValue("aliases",out aliases)&&((System.Collections.IEnumerable)aliases).Cast<object>().Any(a=>System.Convert.ToString(a)==normalized);}).ToArray();return matches.Length==1?matches[0]:null;}
 internal static void QueueRefresh(string root){GameDataCatalog.QueueRefresh(root);}
}
