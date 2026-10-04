using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Game IDs identify types; GOOD keys are optional interoperability metadata.
internal static class AccountIdentity {
 internal const string Format="Catheryne.Account";
 internal static string Key(Dictionary<string,object> row){string identity=CodexChat.S(row,"identity");if(identity.Length>0)return identity;string key=CodexChat.S(row,row.ContainsKey("setKey")?"setKey":"key");return key.Length>0?key:"game:"+CodexChat.S(row,"gameId");}
 internal static void Portrait(System.Windows.Controls.Image view,Dictionary<string,object> row,int width=160){GameCatalog.Portrait(view,Key(row)+(row.ContainsKey("slotKey")?":"+CodexChat.S(row,"slotKey"):""),width,CodexChat.S(row,"icon"));}
 internal static string SetName(Dictionary<string,object> row){object raw;string name="";if(row.TryGetValue("setNames",out raw)){var names=CodexChat.Map(raw);name=CodexChat.S(names,Locale.IsEnglish?"en":"ko");if(name.Length==0)name=CodexChat.S(names,"en");}if(name.Length==0)name=GameCatalog.Name(CodexChat.S(row,"setKey"));return name.Length>0?name:Locale.T("미확인");}
 internal static string CharacterField(Dictionary<string,object> row,string field){string value=GameCatalog.CharacterField(Key(row),field);if(value.Length>0)return value;value=CodexChat.S(row,field);if(field=="element")return GameCatalog.ElementName(value);return value;}
 internal static string Normalize(string name){return new string((name??"").Where(c=>c>='A'&&c<='Z'||c>='a'&&c<='z'||c>='0'&&c<='9').Select(char.ToLowerInvariant).ToArray());}
 internal static string Name(Dictionary<string,object> row,Dictionary<string,object> aliases=null,string language=null){
  string key=Key(row),name="";object raw;string lang=language??(Locale.IsEnglish?"en":"ko");
  if(row.TryGetValue("names",out raw)){var names=CodexChat.Map(raw);name=CodexChat.S(names,lang);if(name.Length==0)name=CodexChat.S(names,"en");}
  if(name.Length==0)name=GameCatalog.Name(key+(row.ContainsKey("slotKey")?":"+CodexChat.S(row,"slotKey"):""),lang);
  object alias;return aliases!=null&&aliases.TryGetValue(key,out alias)?Convert.ToString(alias)+" ("+name+")":name;
 }
 internal static string CharacterName(Dictionary<string,object> snapshot,string identity,Dictionary<string,object> aliases=null,string language=null){object rows;var row=snapshot.TryGetValue("characters",out rows)?CodexChat.Items(rows).FirstOrDefault(x=>Key(x)==identity):null;return row==null?GameCatalog.AccountName(identity,aliases):Name(row,aliases,language);}
 internal static string CurrentName(string root,string identity,string language=null){Dictionary<string,object> pointer;return CharacterName(new CatheryneTools(root).AccountSnapshot(out pointer),identity,null,language);}
 internal static string ExistingKey(Dictionary<string,object> existing,string section,string id,string name){
  object raw;if(existing==null||!existing.TryGetValue(section,out raw))return "";var rows=CodexChat.Items(raw).ToArray();
  var ids=rows.Where(x=>CodexChat.S(x,"gameId")==id&&CodexChat.S(x,"key").Length>0).Select(x=>CodexChat.S(x,"key")).Distinct().ToArray();if(ids.Length==1)return ids[0];
  if(string.IsNullOrEmpty(name))return "";var matches=rows.Select(x=>CodexChat.S(x,"key")).Where(k=>k.Length>0&&Normalize(k)==Normalize(name)).Distinct().ToArray();return matches.Length==1?matches[0]:"";
 }
 internal static Dictionary<string,object> Import(Dictionary<string,object> value){
  string format=CodexChat.S(value,"format");if(format!="GOOD"&&format!=Format)throw new System.IO.InvalidDataException("GOOD 형식의 계정 자료를 선택하세요.");
  var result=new Dictionary<string,object>(value);object raw;var meta=result.TryGetValue("catheryne",out raw)?new Dictionary<string,object>(CodexChat.Map(raw)):new Dictionary<string,object>();
  if(meta.TryGetValue("nativeItems",out raw)){foreach(var section in CodexChat.Map(raw)){if(!new[]{"characters","weapons","artifacts"}.Contains(section.Key))throw new ArgumentException("Invalid native section");object known;var items=result.TryGetValue(section.Key,out known)&&known!=null?CodexChat.Items(known).Cast<object>().ToList():new List<object>();items.AddRange(CodexChat.Items(section.Value));result[section.Key]=items;}meta.Remove("nativeItems");result["catheryne"]=meta;}
  foreach(string section in new[]{"characters","weapons","artifacts"})if(result.TryGetValue(section,out raw)&&raw!=null){
   var list=raw as IEnumerable;if(list==null||raw is string||raw is IDictionary)throw new ArgumentException("Invalid account list");var identities=new HashSet<string>();
   foreach(var item in list){var row=item as Dictionary<string,object>;if(row==null)throw new ArgumentException("Invalid account row");string key=CodexChat.S(row,section=="artifacts"?"setKey":"key");long id;if(key.Length==0&&(!long.TryParse(CodexChat.S(row,"gameId"),out id)||id<=0))throw new ArgumentException("Account item identity missing");if(section=="characters"&&!identities.Add(Key(row)))throw new ArgumentException("Duplicate character identity");}
  }
  if(format=="GOOD"&&result.ContainsKey("materials")&&!result.ContainsKey("materialInventory"))result["materialInventory"]=MaterialInventory.FromScan(result);result.Remove("materials");MaterialInventory.Rows(result);result["format"]=Format;result["version"]=1;return result;
 }
 static bool Compatible(Dictionary<string,object> row,string section){return CodexChat.S(row,section=="artifacts"?"setKey":"key").Length>0&&!CodexChat.S(row,"location").StartsWith("game:",StringComparison.Ordinal);}
 internal static Dictionary<string,object> Export(Dictionary<string,object> value){
  var result=new Dictionary<string,object>(value);if(value.ContainsKey("materialInventory"))result["materials"]=MaterialInventory.Rows(value).Where(x=>x.ContainsKey("goodKey")&&x.ContainsKey("quantity")&&x["quantity"]!=null).ToDictionary(x=>CodexChat.S(x,"goodKey"),x=>x["quantity"]);var native=new Dictionary<string,object>();object raw;
  foreach(string section in new[]{"characters","weapons","artifacts"})if(value.TryGetValue(section,out raw)&&raw!=null){var rows=CodexChat.Items(raw).ToArray();result[section]=rows.Where(x=>Compatible(x,section)).ToArray();var unknown=rows.Where(x=>!Compatible(x,section)).ToArray();if(unknown.Length>0)native[section]=unknown;}
  var meta=value.TryGetValue("catheryne",out raw)?new Dictionary<string,object>(CodexChat.Map(raw)):new Dictionary<string,object>();if(native.Count>0)meta["nativeItems"]=native;result["catheryne"]=meta;result["format"]="GOOD";result["version"]=1;return result;
 }
}
