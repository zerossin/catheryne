using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Game text is imported verbatim. GOOD keys and saved account data are never translated.
internal static class GameCatalog {
 static readonly Dictionary<string,object> elementIcons=Load("elements.json");
 static readonly SemaphoreSlim downloads=new SemaphoreSlim(3);
 static Dictionary<string,object> Load(string name="game.json"){string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog",name);return File.Exists(file)?CatheryneTools.Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(file)):new Dictionary<string,object>();}
 internal static string EnemyName(Dictionary<string,object> enemy,Dictionary<string,object> catalog=null,string language=null){
  string source=CodexChat.S(enemy,"enemy_name");if(source.Length==0)source=CodexChat.S(enemy,"name");
  catalog=catalog??GameDataCatalog.Read(Setup.DataFolder).Game;object raw;
  var names=catalog.TryGetValue("enemies",out raw)?CodexChat.Map(raw):new Dictionary<string,object>();
  string id=CodexChat.S(enemy,"id");object found;
  if(!names.TryGetValue(id,out found)){
   string asset=CodexChat.S(enemy,"asset");foreach(string prefix in new[]{"UI_MonsterIcon_","UI_Img_LeyLineChallenge_"})if(asset.StartsWith(prefix,StringComparison.Ordinal))asset=asset.Substring(prefix.Length);
   var assets=catalog.TryGetValue("enemyAssets",out raw)?CodexChat.Map(raw):new Dictionary<string,object>();
   id=CodexChat.S(assets,asset);if(!names.TryGetValue(id,out found)){
    // Only match a complete base name; retain challenge qualifiers in the tooltip.
    string baseName=source.StartsWith("Battle-Hardened ",StringComparison.Ordinal)?source.Substring(16):source;
    var matches=names.Values.Select(CodexChat.Map).Where(row=>baseName==CodexChat.S(row,"en")||baseName.StartsWith(CodexChat.S(row,"en")+": ",StringComparison.Ordinal)||baseName.StartsWith(CodexChat.S(row,"en")+" - ",StringComparison.Ordinal)).ToArray();
    found=matches.Length==1?matches[0]:null;
   }
  }
  string translated=CodexChat.S(CodexChat.Map(found),language??(Locale.IsEnglish?"en":"ko"));return translated.Length>0?translated:source;
 }
 internal static string Name(string key,string language=null){var catalog=GameDataCatalog.Read(Setup.DataFolder).Game;
  if(string.IsNullOrEmpty(key))return key??"";
  object raw;if(catalog.TryGetValue("locales",out raw)){var locale=CodexChat.Map(CodexChat.Map(raw)[language??(Locale.IsEnglish?"en":"ko")]);object value;if(locale.TryGetValue(key,out value))return Convert.ToString(value);}
  if(catalog.TryGetValue("normalized",out raw)){var locale=CodexChat.Map(CodexChat.Map(raw)[language??(Locale.IsEnglish?"en":"ko")]);object value;string normalized=System.Text.RegularExpressions.Regex.Replace(key.ToLowerInvariant(),"[^a-z0-9:]","");if(locale.TryGetValue(normalized,out value))return Convert.ToString(value);}
  return key;
 }
 internal static string MaterialName(string id,string fallback){object raw;var rows=GameDataCatalog.Read(Setup.DataFolder).Materials;if(!rows.TryGetValue(id??"",out raw))return fallback;string name=CodexChat.S(CodexChat.Map(raw),Locale.IsEnglish?"en":"ko");return name.Length>0?name:fallback;}
 internal static string ElementName(string element){
  if(Locale.IsEnglish)return element??"";
  var en=new[]{"Anemo","Hydro","Pyro","Electro","Cryo","Dendro","Geo"};var ko=new[]{"바람","물","불","번개","얼음","풀","바위"};
  int index=Array.FindIndex(en,x=>x.Equals(element,StringComparison.OrdinalIgnoreCase));return index>=0?ko[index]:element??"";
 }
 internal static System.Windows.FrameworkElement ElementIcon(string element,double size=24){
  if(!elementIcons.ContainsKey("elements"))return new TextBlock{Text=ElementName(element),ToolTip=ElementName(element)};
  var rows=CodexChat.Map(elementIcons["elements"]);string key=rows.Keys.FirstOrDefault(x=>x.Equals(element,StringComparison.OrdinalIgnoreCase)||ElementName(x)==element);
  if(key==null)return new TextBlock{Text=element,ToolTip=element};var row=CodexChat.Map(rows[key]);
  var canvas=new System.Windows.Controls.Canvas{Width=24,Height=24};canvas.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("F1 "+CodexChat.S(row,"path")),Fill=(Brush)new BrushConverter().ConvertFromString(CodexChat.S(row,"color"))});
  var icon=new System.Windows.Controls.Viewbox{Width=size,Height=size,Child=canvas,ToolTip=ElementName(key)};System.Windows.Automation.AutomationProperties.SetName(icon,ElementName(key));return icon;
 }
 internal static IEnumerable<string> CharacterKeys(){var catalog=GameDataCatalog.Read(Setup.DataFolder).Game;object raw;return catalog.TryGetValue("images",out raw)?CodexChat.Map(raw).Keys.Where(x=>CharacterField(x,"rarity")!="").OrderBy(x=>Name(x)):Enumerable.Empty<string>();}
 internal static string AccountName(string key,Dictionary<string,object> aliases,string slot=""){
  string name=Name(key+(string.IsNullOrEmpty(slot)?"":":"+slot));object alias;
  return aliases!=null&&aliases.TryGetValue(key,out alias)?Convert.ToString(alias)+" ("+name+")":name;
 }
 internal static string CharacterField(string key,string field){var catalog=GameDataCatalog.Read(Setup.DataFolder).Game;
  object raw,entry;string normalized=System.Text.RegularExpressions.Regex.Replace((key??"").ToLowerInvariant(),"[^a-z0-9]","");
  if(!catalog.TryGetValue("characters",out raw)||!CodexChat.Map(raw).TryGetValue(normalized,out entry))return "";
  var record=CodexChat.Map(entry);if(field=="rarity"||field=="gameId")return CodexChat.S(record,field);
  object locale;return record.TryGetValue(Locale.IsEnglish?"en":"ko",out locale)?CodexChat.S(CodexChat.Map(locale),field):"";
 }
 internal static Dictionary<string,object> Weapon(string key){var catalog=GameDataCatalog.Read(Setup.DataFolder).Game;
  object table,row,ids,mapped;string resolved=key??"";if(resolved.StartsWith("game:")&&catalog.TryGetValue("weaponIds",out ids)&&CodexChat.Map(ids).TryGetValue(resolved.Substring(5),out mapped))resolved=Convert.ToString(mapped);
  string normalized=System.Text.RegularExpressions.Regex.Replace(resolved.ToLowerInvariant(),"[^a-z0-9]","");return catalog.TryGetValue("weapons",out table)&&CodexChat.Map(table).TryGetValue(normalized,out row)?CodexChat.Map(row):new Dictionary<string,object>();
 }
 internal static string WeaponField(string key,string field){var row=Weapon(key);object locale;return field=="weaponType"&&row.TryGetValue(Locale.IsEnglish?"en":"ko",out locale)?CodexChat.S(CodexChat.Map(locale),field):CodexChat.S(row,field);}
 internal static Dictionary<string,object> WeaponDetails(Dictionary<string,object> item){
  var row=Weapon(AccountIdentity.Key(item));object raw;var locale=row.TryGetValue(Locale.IsEnglish?"en":"ko",out raw)?CodexChat.Map(raw):new Dictionary<string,object>();var result=new Dictionary<string,object>();
  int refinement;if(int.TryParse(CodexChat.S(item,"refinement"),out refinement)&&refinement>=1&&refinement<=5&&locale.TryGetValue("effects",out raw)){result["effectName"]=CodexChat.S(locale,"effectName");result["effect"]=CodexChat.S(CodexChat.Map(raw),refinement.ToString());}
  int level;if(!int.TryParse(CodexChat.S(item,"level"),out level)||!row.TryGetValue("stats",out raw))return result;
  var stats=CodexChat.Map(raw);var basic=CodexChat.Map(stats["base"]);var curveKeys=CodexChat.Map(stats["curve"]);var promotions=CodexChat.Items(stats["promotion"]).ToArray();var catalog=GameDataCatalog.Read(Setup.DataFolder).Game;
  if(level<1||level>Convert.ToInt32(promotions.Last()["maxlevel"])||!CodexChat.Map(catalog["weaponCurves"]).TryGetValue(level.ToString(),out raw))return result;var curve=CodexChat.Map(raw);
  int phase=Array.FindIndex(promotions,p=>level<=Convert.ToInt32(p["maxlevel"]));int ascension;bool known=int.TryParse(CodexChat.S(item,"ascension"),out ascension);
  bool boundary=phase<promotions.Length-1&&level==Convert.ToInt32(promotions[phase]["maxlevel"]);
  if(!boundary||known&&ascension>=phase&&ascension<=phase+1){if(boundary&&ascension>phase)phase++;result["attack"]=Math.Round(Convert.ToDouble(basic["attack"])*Convert.ToDouble(curve[CodexChat.S(curveKeys,"attack")])+Convert.ToDouble(promotions[phase]["attack"]),0,MidpointRounding.AwayFromZero).ToString("0");}
  if(CodexChat.S(stats,"specialized").Length>0){double value=Convert.ToDouble(basic["specialized"])*Convert.ToDouble(curve[CodexChat.S(curveKeys,"specialized")]);bool percent=CodexChat.S(stats,"specialized")!="FIGHT_PROP_ELEMENT_MASTERY";result["statName"]=CodexChat.S(locale,"statName");result["statValue"]=(value*(percent?100:1)).ToString(percent?"0.0":"0",System.Globalization.CultureInfo.CurrentCulture)+(percent?"%":"");}return result;
 }
 internal static string ArtifactMainStat(Dictionary<string,object> item){
  string key=CodexChat.S(item,"mainStatKey"),name=Name(key);var data=GameDataCatalog.Read(Setup.DataFolder).Scores;object raw,row;int level;
  if(!int.TryParse(CodexChat.S(item,"level"),out level)||level<0||!data.TryGetValue("mainStatValue",out raw)||!CodexChat.Map(raw).TryGetValue(CodexChat.S(item,"rarity"),out row)||!CodexChat.Map(row).TryGetValue(key,out raw))return name;
  var values=((System.Collections.IEnumerable)raw).Cast<object>().ToArray();if(level>=values.Length)return name;bool percent=key.EndsWith("_");return name+" "+(Convert.ToDouble(values[level])*(percent?100:1)).ToString(percent?"0.0":"0",System.Globalization.CultureInfo.CurrentCulture)+(percent?"%":"");
 }
 internal static void SidePortrait(Image image,Dictionary<string,object> character){var catalog=GameDataCatalog.Read(Setup.DataFolder).Game;string key=AccountIdentity.Key(character),side=key+":side";object images;if(catalog.TryGetValue("images",out images)&&CodexChat.Map(images).ContainsKey(side))Portrait(image,side,64);else AccountIdentity.Portrait(image,character,64);}
 internal static IEnumerable<string> AssetKeys(){var catalog=GameDataCatalog.Read(Setup.DataFolder).Game;object raw;return catalog.TryGetValue("images",out raw)?CodexChat.Map(raw).Keys.Where(x=>!x.EndsWith(":side")):Enumerable.Empty<string>();}
 sealed class PortraitSource {internal string Url,Hash,GitBlob;}
 static IEnumerable<PortraitSource> PortraitSources(string key,string officialUrl){var catalog=GameDataCatalog.Read(Setup.DataFolder).Game;
  Uri icon;if(Uri.TryCreate(officialUrl,UriKind.Absolute,out icon)&&icon.Scheme=="https"&&new[]{"upload-os-bbs.hoyolab.com","act-webstatic.hoyoverse.com","fastcdn.hoyoverse.com","upload-static.hoyoverse.com"}.Contains(icon.Host)&&icon.UserInfo.Length==0&&icon.IsDefaultPort){string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(icon.AbsoluteUri))).Replace("-","").ToLowerInvariant();yield return new PortraitSource{Url=icon.AbsoluteUri,Hash=hash};}
  object images,raw;if(!catalog.TryGetValue("images",out images)||!CodexChat.Map(images).TryGetValue(key,out raw))yield break;var entry=CodexChat.Map(raw);string blob=CodexChat.S(entry,"gitBlob"),path=CodexChat.S(entry,"path"),revision=CodexChat.S(catalog,"revision");
  if(System.Text.RegularExpressions.Regex.IsMatch(blob,"^[a-f0-9]{40}$")&&System.Text.RegularExpressions.Regex.IsMatch(revision,"^[a-f0-9]{40}$")&&path.StartsWith("libs/gi/assets/src/gen/")&&!path.Contains(".."))yield return new PortraitSource{Url="https://raw.githubusercontent.com/frzyc/genshin-optimizer/"+revision+"/"+path,Hash=blob,GitBlob=blob};
 }
 internal static string[] PortraitUrls(string key,string officialUrl){return PortraitSources(key,officialUrl).Select(x=>x.Url).ToArray();}
 internal static async void Portrait(Image view,string key,int decodeWidth=160,string fallbackUrl=null){
  object requestId=new object();view.Tag=requestId;var sources=PortraitSources(key,fallbackUrl).ToArray();string root=Setup.DataFolder;int width=Math.Max(32,Math.Min(640,decodeWidth));
  foreach(var source in sources){try{
   var bitmap=await BitmapCache.Shared.Get("portrait|"+root+"|"+source.Hash+"|"+width,()=>LoadPortrait(source,root,width));
   if(bitmap!=null&&ReferenceEquals(view.Tag,requestId))view.Source=bitmap;return;
  }catch(Exception error){System.Diagnostics.Trace.TraceWarning("Portrait: "+error.Message);}}
 }
 internal static async void EnemyPortrait(Image view,string asset,int width=80){
  if(!System.Text.RegularExpressions.Regex.IsMatch(asset??"",@"^UI_[A-Za-z0-9_]{1,180}$"))return;string url="https://api.lightkeepers.moe/genshin/ui/"+asset+".webp";string root=Setup.DataFolder;var source=new PortraitSource{Url=url,Hash=EndgameKnowledge.Hash(url)};object request=new object();view.Tag=request;
  try{var bitmap=await BitmapCache.Shared.Get("enemy|"+root+"|"+source.Hash+"|"+width,()=>LoadPortrait(source,root,width));if(ReferenceEquals(view.Tag,request))view.Source=bitmap;}catch(Exception error){System.Diagnostics.Trace.TraceWarning("Enemy image: "+error.Message);}
 }
 static Task<BitmapSource> LoadPortrait(PortraitSource source,string root,int width){return Task.Run<BitmapSource>(async()=>{
  await downloads.WaitAsync();try{
   string folder=Path.Combine(root,"cache","portraits"),file=Path.Combine(folder,source.Hash+".png");byte[] bytes;
   if(File.Exists(file))bytes=File.ReadAllBytes(file);else{var request=HttpTransport.Create(source.Url);request.AllowAutoRedirect=false;request.UserAgent="Catheryne";request.Timeout=12000;request.ReadWriteTimeout=12000;using(var response=request.GetResponse())using(var stream=response.GetResponseStream())using(var output=new MemoryStream()){var buffer=new byte[8192];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){if(output.Length+count>2*1024*1024)throw new InvalidDataException("Portrait too large");output.Write(buffer,0,count);}bytes=output.ToArray();}}
   if(source.GitBlob!=null)using(var sha=SHA1.Create())using(var blob=new MemoryStream()){var header=Encoding.ASCII.GetBytes("blob "+bytes.Length+"\0");blob.Write(header,0,header.Length);blob.Write(bytes,0,bytes.Length);if(BitConverter.ToString(sha.ComputeHash(blob.ToArray())).Replace("-","").ToLowerInvariant()!=source.GitBlob)throw new InvalidDataException("Portrait integrity mismatch");}
   var image=GameImage.Decode(bytes,width);
   if(!File.Exists(file)){Directory.CreateDirectory(folder);string staged=file+"."+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(staged,bytes);try{File.Move(staged,file);}catch(IOException){if(!File.Exists(file))throw;}}finally{if(File.Exists(staged))File.Delete(staged);}}
   return image;
  }catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.ImageFailure,error,root);throw;}finally{downloads.Release();}
 });}
}
