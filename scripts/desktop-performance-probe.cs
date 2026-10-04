using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
internal static class DesktopPerformanceProbe {
 static readonly Dictionary<string,object> results=new Dictionary<string,object>();
 static void Measure(string name,int count,Action action){action();double[] times=new double[3];for(int r=0;r<3;r++){GC.Collect();GC.WaitForPendingFinalizers();var watch=Stopwatch.StartNew();for(int i=0;i<count;i++)action();watch.Stop();times[r]=watch.Elapsed.TotalMilliseconds/count;}results[name]=new{iterations=count,median_ms=times.OrderBy(x=>x).ElementAt(1)};}
 [STAThread] static int Main(string[] args){string root=Path.Combine(Path.GetTempPath(),"catheryne-whole-probe-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Environment.SetEnvironmentVariable("CATHERYNE_TOOL_DATA",root);try{
  var fixture=new Dictionary<string,object>();for(int i=0;i<200;i++)fixture["fixture."+i]=new string('x',100);AtomicFile.Write(Path.Combine(root,"settings.json"),CatheryneTools.Json().Serialize(fixture));
  Measure("settings_read",200,()=>AppPreferences.Read(root));Measure("criteria_read",20,()=>BuildCriteriaCatalog.Read(root));var automatic=new Dictionary<string,object>{{"key","Xingqiu"}};Measure("automatic_criterion",20,()=>BuildCriteriaCatalog.Criterion(root,automatic));
  AppPreferences.Set("fixture.noop",true,root);Measure("settings_noop_write",40,()=>AppPreferences.Set("fixture.noop",true,root));
  Measure("credential_check",100,()=>{using(var db=new LocalDataService(root))db.GetSecret("hoyolab");});
  var inventory=new InventoryPanel();var identity=typeof(InventoryPanel).GetMethod("Identity",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var items=Enumerable.Range(0,1500).Select(i=>new Dictionary<string,object>{{"key","FixtureSet"},{"slotKey","flower"},{"level",20},{"identity",i},{"substats",new[]{new{key="critRate_",value=3.1},new{key="critDMG_",value=6.2}}}}).ToArray();
  Measure("inventory_identity_scan",20,()=>{foreach(var item in items)identity.Invoke(inventory,new object[]{item});});
  using(var shell=new Launcher(null,false,true,false,false,connectAi:false)){
   var character=new Dictionary<string,object>{{"key","ProbeCharacter"},{"level",90},{"talent",new Dictionary<string,object>{{"auto",1},{"skill",9},{"burst",9}}}};var snapshot=new Dictionary<string,object>{{"characters",new[]{character}},{"weapons",new object[0]},{"artifacts",new object[0]}};var criterion=CharacterBuild.Read(root,new Dictionary<string,object>{{"key","Faruzan"}});CharacterBuild.Save(root,"ProbeCharacter",criterion);
   Measure("character_detail",10,()=>CharacterInventory.Detail(snapshot,character,"Fixture",null,null,null,()=>{}));
   Measure("launcher_idle_tick",100,()=>{var tick=typeof(Launcher).GetMethod("Tick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);tick.Invoke(shell,null);});
  }
  File.WriteAllText(args[0],CatheryneTools.Json().Serialize(results));return 0;
 }catch(Exception error){Console.WriteLine(error);return 1;}finally{Directory.Delete(root,true);}}
}