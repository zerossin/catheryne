using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

internal static class SharedReadTests {
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 internal static void Run(){
  string root=Path.Combine(Path.GetTempPath(),"catheryne-shared-read-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var cache=new FileReadCache<string>(2,8);int reads=0;Func<string,string> load=f=>{reads++;return File.ReadAllText(f);};
   string a=Path.Combine(root,"a"),b=Path.Combine(root,"b"),c=Path.Combine(root,"c");File.WriteAllText(a,"one");File.WriteAllText(b,"two");File.WriteAllText(c,"third");
   Check(cache.Read(a,load)=="one"&&cache.Read(a,load)=="one"&&reads==1,"Unchanged files must be parsed once");
   cache.Read(b,load);cache.Read(a,load);cache.Read(c,load);Check(cache.Count==2&&cache.Bytes<=8,"Read projections must respect count and byte budgets");cache.Read(b,load);Check(reads==4,"Least recently used file must be rebuilt");
   File.WriteAllText(a,"changed");Check(cache.Read(a,load)=="changed","External in-place writes must invalidate projections");File.Delete(a);Check(cache.Read(a,load)==null,"Deleted files must not return old data");File.WriteAllText(a,"new");Check(cache.Read(a,load)=="new","Recreated files must be read again");
   cache.Invalidate(a);bool rejected=false;try{cache.Read(a,f=>{throw new IOException("fixture");});}catch(IOException){rejected=true;}Check(rejected&&cache.Read(a,load)=="new","A failed parse must not poison retry");
   File.WriteAllText(a,new string('x',20));int count=cache.Count;cache.Read(a,load);Check(cache.Count<=count&&cache.Bytes<=8,"Oversized data must not stay in the cache");
   string settingsRoot=Path.Combine(root,"settings");AppPreferences.Set("fixture.nested",new Dictionary<string,object>{{"array",new object[]{new Dictionary<string,object>{{"value",1}}}}},settingsRoot);
   var prefs=AppPreferences.Read(settingsRoot);var rows=CodexChat.Items(CodexChat.Map(prefs["fixture.nested"])["array"]).ToArray();rows[0]["value"]=999;
   Check(Convert.ToInt32(CodexChat.Items(CodexChat.Map(AppPreferences.Read(settingsRoot)["fixture.nested"])["array"]).Single()["value"])==1,"Returned settings must not mutate canonical cached data");
   var tasks=Enumerable.Range(0,24).Select(i=>Task.Run(()=>AppPreferences.Set("fixture.concurrent."+i,i,settingsRoot))).ToArray();Task.WaitAll(tasks);prefs=AppPreferences.Read(settingsRoot);Check(Enumerable.Range(0,24).All(i=>prefs.ContainsKey("fixture.concurrent."+i)),"Concurrent setting writes must preserve all keys");
   string file=Path.Combine(settingsRoot,"settings.json");DateTime stamp=File.GetLastWriteTimeUtc(file);AppPreferences.Set("fixture.concurrent.0",0,settingsRoot);Check(File.GetLastWriteTimeUtc(file)==stamp,"Identical settings must avoid disk rewrites");AtomicFile.Write(file,"{\"fixture.external\":true}");Check(AppPreferences.Read(settingsRoot).ContainsKey("fixture.external"),"Another process's atomic settings update must be visible");
   string dbRoot=Path.Combine(root,"credentials");using(var db=new LocalDataService(dbRoot)){Check(db.GetSecret("hoyolab")==null&&!Directory.Exists(dbRoot),"Credential presence checks must not open or create the database");db.Observe("fixture","read",new{ok=true});Check(db.Recent("fixture","read").Count==1,"First actual query must initialize and preserve the database");}
   var hero=new Dictionary<string,object>{{"key","Xingqiu"}};var criterion=BuildCriteriaCatalog.Criterion(root,hero);Check(criterion!=null,"Bundled criteria must be available");string original=CatheryneTools.Json().Serialize(criterion);criterion.Useful[0]="fixture";criterion.Talents["burst"]=0;criterion.MainWeights["sands"].Clear();criterion.WeaponFit.Clear();criterion.SetAffinities.Clear();Check(CatheryneTools.Json().Serialize(BuildCriteriaCatalog.Criterion(root,hero))==original,"Mutable criterion copies must not contaminate reused projections");
   var source=BuildCriteriaCatalog.Read(root);source.Profiles.Clear();source.Rules.Weapons.Clear();Check(BuildCriteriaCatalog.Read(root).Profiles.Count>=90&&BuildCriteriaCatalog.Read(root).Rules.Weapons.Count>=200,"Returned source data must be independent of the canonical projection");
   var valid=BuildCriteriaCatalog.Read(root);valid.Revision="fixture-source";string criteriaFile=Path.Combine(root,"cache","build-criteria.json");Directory.CreateDirectory(Path.GetDirectoryName(criteriaFile));string serialized=CatheryneTools.Json().Serialize(valid);AtomicFile.Write(criteriaFile,serialized);
   Check(BuildCriteriaCatalog.Read(root).Revision=="fixture-source","External criteria publication must invalidate the source projection");valid.Rules.Weapons[valid.Rules.Weapons.Keys.First()]=null;AtomicFile.Write(criteriaFile,CatheryneTools.Json().Serialize(valid));Check(BuildCriteriaCatalog.Read(root).Rules.Weapons.Values.All(x=>x!=null),"Malformed nested rule data must use validated bundled criteria");
   AtomicFile.Write(criteriaFile,serialized);using(var held=new FileStream(criteriaFile,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(BuildCriteriaCatalog.Read(root).Revision!="fixture-source","A temporary read failure must fall back without caching the failure");Check(BuildCriteriaCatalog.Read(root).Revision=="fixture-source","Criteria must recover immediately after a temporary file lock ends");

  }finally{Directory.Delete(root,true);}
 }
}
