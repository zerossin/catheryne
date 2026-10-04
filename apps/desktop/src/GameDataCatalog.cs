using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Text.RegularExpressions;using System.Threading;using System.Threading.Tasks;
// A single immutable snapshot is atomically published by the bundled public-data importer.
internal sealed class GameDataSnapshot {
 internal Dictionary<string,object> Game,Hoyo,Materials,Scores;
 internal DateTime Revision;
}
internal static class GameDataCatalog {
 sealed class Cached {internal DateTime Stamp,NextRead;internal GameDataSnapshot Data;internal bool Active;}
 static readonly object gate=new object();static readonly Dictionary<string,Cached> loaded=new Dictionary<string,Cached>(StringComparer.OrdinalIgnoreCase);static readonly HashSet<string> pending=new HashSet<string>(StringComparer.OrdinalIgnoreCase);static Timer timer;
 static string FileName(string root){return Path.Combine(root,"cache","game-catalog.json");}
 internal static string Scope(string name,string root){using(var hash=System.Security.Cryptography.SHA256.Create())return "Local\\Catheryne."+name+"."+BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant()))).Replace("-","").Substring(0,16);}
 static Dictionary<string,object> Map(Dictionary<string,object> row,string key){object value;return row.TryGetValue(key,out value)?CodexChat.Map(value):new Dictionary<string,object>();}
 static bool Hash(string value){return Regex.IsMatch(value??"","^[a-f0-9]{40}$");}
 internal static GameDataSnapshot Parse(string text){
  var raw=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(text);var game=Map(raw,"game");var hoyo=Map(raw,"hoyolab");var materials=Map(raw,"materials");var scores=Map(raw,"scores");var sources=Map(hoyo,"sources");string go=CodexChat.S(game,"revision"),db=CodexChat.S(Map(game,"supplement"),"revision");
  if(CodexChat.S(raw,"schema")!="2"||CodexChat.S(game,"schema")!="1"||CodexChat.S(hoyo,"schema")!="2"||CodexChat.S(materials,"schema")!="1"||CodexChat.S(scores,"schema")!="1"||!Hash(go)||!Hash(db)||CodexChat.S(Map(sources,"optimizer"),"revision")!=go||CodexChat.S(Map(sources,"database"),"revision")!=db||CodexChat.S(materials,"revision")!=db||CodexChat.S(scores,"revision")!=go)throw new InvalidDataException("Mixed game catalog revisions");
  if(CodexChat.S(game,"weaponDetailsSchema")!="1"||Map(game,"weaponCurves").Count<90||Map(scores,"mainStatValue").Count<5)throw new InvalidDataException("Missing equipment detail catalog");
  foreach(var section in new[]{"characters","weapons","artifacts","characterNames","talents"}){var rows=Map(hoyo,section);int minimum=section=="weapons"?200:section=="artifacts"?500:section=="talents"?85:90;if(rows.Count<minimum||rows.Keys.Any(x=>!Regex.IsMatch(x,@"^[1-9]\d*$")))throw new InvalidDataException("Incomplete game identities");}
  var locales=Map(game,"locales");var en=Map(locales,"en");var ko=Map(locales,"ko");var ids=Map(game,"weaponIds");
  foreach(string section in new[]{"characters","weapons"})foreach(var row in Map(hoyo,section)){string key=Convert.ToString(row.Value);if(key!="Traveler"&&(!en.ContainsKey(key)||!ko.ContainsKey(key)))throw new InvalidDataException("Missing translated identity");if(section=="weapons"&&CodexChat.S(ids,row.Key)!=key)throw new InvalidDataException("Weapon identity mismatch");}
  var rowsMaterials=Map(materials,"materials");if(rowsMaterials.Count<500||Map(hoyo,"travelerElements").Count==0||Map(scores,"rollValue").Count<3)throw new InvalidDataException("Incomplete public data");
  foreach(var row in rowsMaterials){var item=CodexChat.Map(row.Value);object aliases;if(CodexChat.S(item,"id")!=row.Key||!Regex.IsMatch(row.Key,@"^[1-9]\d*$")||CodexChat.S(item,"en").Length==0||!item.TryGetValue("aliases",out aliases)||!(aliases is System.Collections.IEnumerable)||aliases is string||aliases is System.Collections.IDictionary)throw new InvalidDataException("Invalid material identity");}
  {var enemies=Map(game,"enemies");if(enemies.Count<300||enemies.Any(row=>!Regex.IsMatch(row.Key,@"^[1-9]\d*$")||CodexChat.S(CodexChat.Map(row.Value),"id")!=row.Key||CodexChat.S(CodexChat.Map(row.Value),"en").Length==0||CodexChat.S(CodexChat.Map(row.Value),"ko").Length==0)||Map(game,"enemyAssets").Any(row=>!enemies.ContainsKey(Convert.ToString(row.Value))))throw new InvalidDataException("Invalid enemy identities");}
  return new GameDataSnapshot{Game=game,Hoyo=hoyo,Materials=rowsMaterials,Scores=scores};
 }
 static readonly Lazy<GameDataSnapshot> seed=new Lazy<GameDataSnapshot>(()=>{
  string bundle=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog","game-catalog.json");if(File.Exists(bundle))return Parse(File.ReadAllText(bundle));
  Func<string,Dictionary<string,object>> read=name=>{string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog",name);return File.Exists(path)?StoryClient.Read(path):new Dictionary<string,object>();};
  return new GameDataSnapshot{Game=read("game.json"),Hoyo=read("hoyolab.json"),Materials=Map(read("materials.json"),"materials"),Scores=read("artifact-scores.json")};
 });
 internal static GameDataSnapshot Read(string root,bool force=false){
  root=Path.GetFullPath(root);lock(gate){Cached cached;if(!loaded.TryGetValue(root,out cached)){cached=new Cached{Data=seed.Value};loaded[root]=cached;}if(!force&&DateTime.UtcNow<cached.NextRead)return cached.Data;cached.NextRead=DateTime.UtcNow.AddSeconds(1);string file=FileName(root);DateTime stamp=File.GetLastWriteTimeUtc(file);if(!force&&stamp==cached.Stamp)return cached.Data;
   try{if(File.Exists(file)){if(new FileInfo(file).Length>16000000)throw new InvalidDataException("Game catalog too large");var data=Parse(File.ReadAllText(file));data.Revision=stamp;cached.Data=data;cached.Active=true;}else cached.Active=false;}catch(InvalidDataException){cached.Active=false;}catch(IOException){cached.Active=false;}catch(ArgumentException){cached.Active=false;}catch(InvalidOperationException){cached.Active=false;}cached.Stamp=stamp;return cached.Data;
  }
 }
 internal static DateTime Revision(string root){return Read(root).Revision;}
 static bool Active(string root){Read(root);lock(gate){return loaded[Path.GetFullPath(root)].Active;}}
 internal static object Status(string root){var data=Read(root);var check=ReadCheck(Path.Combine(root,"cache","game-catalog-check.json"));return new{available=data.Game.Count>0,authority="community_game_extract",cacheHealthy=Active(root),origin=data.Revision==DateTime.MinValue?"bundled":"validated_runtime_bundle",checkedAt=CodexChat.S(check,"checkedAt"),refreshState=CodexChat.S(check,"state"),sources=data.Game.ContainsKey("supplement")?data.Game["supplement"]:null,optimizerRevision=CodexChat.S(data.Game,"revision"),characters=Map(data.Hoyo,"characters").Count,weapons=Map(data.Hoyo,"weapons").Count,materials=data.Materials.Count,policy="Atomic coherent revisions; unknown IDs are preserved; source failure retains the last validated bundle."};}
 static Dictionary<string,object> ReadCheck(string file){try{return File.Exists(file)?StoryClient.Read(file):new Dictionary<string,object>();}catch(InvalidDataException){}catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}return new Dictionary<string,object>();}
 internal static void Start(string root){QueueRefresh(root);timer=new Timer(x=>QueueRefresh(root),null,TimeSpan.FromHours(1),TimeSpan.FromHours(1));}
 internal static void QueueRefresh(string root){root=Path.GetFullPath(root);lock(gate){if(!pending.Add(root))return;}Task.Run(()=>{try{Refresh(root);}finally{lock(gate)pending.Remove(root);}});}
 internal static void Refresh(string root){
  using(var mutex=new Mutex(false,Scope("GameCatalog",root))){bool held=false;try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)return;
   string check=Path.Combine(root,"cache","game-catalog-check.json");var previous=ReadCheck(check);DateTime at;if(Read(root).Revision>DateTime.MinValue&&DateTime.TryParse(CodexChat.S(previous,"checkedAt"),out at)&&DateTime.UtcNow>=at.ToUniversalTime()&&DateTime.UtcNow-at.ToUniversalTime()<TimeSpan.FromHours(CodexChat.S(previous,"state")=="ready"?24:1)&&(CodexChat.S(previous,"state")!="ready"||Active(root))){if(CodexChat.S(previous,"state")=="ready")BuildCriteriaCatalog.QueueRefresh(root);return;}
   string state="unavailable";try{
    string directory=AppDomain.CurrentDomain.BaseDirectory,script=Path.Combine(directory,"integrations","ai","game_catalog.py");if(!File.Exists(script))script=Path.GetFullPath(Path.Combine(directory,"..","..","integrations","ai","game_catalog.py"));
    string python=new[]{Path.Combine(directory,"integrations","runtime","python.exe"),Path.Combine(root,"runtime","Scripts","python.exe"),Path.Combine(root,"components","ai","Scripts","python.exe")}.FirstOrDefault(File.Exists);if(python==null||!File.Exists(script))return;
    Directory.CreateDirectory(Path.GetDirectoryName(check));using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(python,StoryClient.Quote(script)+" "+StoryClient.Quote(FileName(root))+" --seed-dir "+StoryClient.Quote(Path.Combine(directory,"catalog"))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true})){var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(180000)){process.Kill();throw new IOException("Public catalog timeout");}Task.WaitAll(output,errors);if(process.ExitCode!=0)throw new InvalidDataException("Public catalog refresh failed");}
    Read(root,true);if(!Active(root))throw new InvalidDataException("Invalid activated public catalog");state="ready";BuildCriteriaCatalog.QueueRefresh(root);
   }catch(Exception){state="failed";}finally{Directory.CreateDirectory(Path.GetDirectoryName(check));AtomicFile.Write(check,CatheryneTools.Json().Serialize(new{checkedAt=DateTime.UtcNow.ToString("o"),state=state}));}
  }catch(InvalidDataException){}catch(IOException){}catch(ArgumentException){}finally{if(held)mutex.ReleaseMutex();}}
 }
}
