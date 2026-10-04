using System;
using System.IO;
internal static class LocalDataTests {
 internal static void Run(){
  int saves=0;using(var auto=new SettingsAutoSave(()=>saves++)){auto.Request();auto.Request();auto.Flush();auto.Flush();}if(saves!=1)throw new Exception("Autosave coalescing failed");
  var scope=new System.Windows.Controls.StackPanel();var fold=new System.Windows.Controls.Primitives.ToggleButton();var option=new System.Windows.Controls.CheckBox();scope.Children.Add(fold);scope.Children.Add(option);
  using(var auto=new SettingsAutoSave(()=>saves++)){auto.Attach(scope,()=>true);fold.IsChecked=true;auto.Flush();if(saves!=1)throw new Exception("Disclosure toggling saved settings");option.IsChecked=true;auto.Flush();if(saves!=2)throw new Exception("Checkbox no longer saves settings");fold.IsChecked=false;auto.Flush();if(saves!=2)throw new Exception("Disclosure collapse saved settings");}
  PanelUi.Shell(new System.Windows.Controls.TextBlock(),new System.Windows.Controls.Grid(),new System.Windows.Controls.StackPanel(),()=>{},false);
  var catalog=AchievementCatalog.Parse("{\"1\":{\"name\":\"Theme\",\"achievements\":[{\"id\":1,\"name\":\"One\",\"desc\":\"Condition\"},[{\"id\":2,\"name\":\"Tier\",\"desc\":\"First\"},{\"id\":3,\"name\":\"Tier\",\"desc\":\"Second\"}]]}}");
  if(catalog.Count!=3||catalog[2].Id!=3||catalog[2].Description!="Second"||catalog[2].Tier!=2||catalog[2].Tiers!=2)throw new Exception("Catalog tiers flattened incorrectly");
  string translated=Locale.Xaml("<Window xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><TextBlock x:Name='Title' Text='원신'/><Setter Property='ToolTip' Value='닫기'/></Window>",true);
  if(!translated.Contains("Genshin Impact")||!translated.Contains("Close")||!translated.Contains("Title"))throw new Exception("English resource translation");
  string root=Path.Combine(Path.GetTempPath(),"catheryne-data-"+Guid.NewGuid().ToString("N"));
  try {
   int fetches=0;Action fetch=()=>{System.Threading.Interlocked.Increment(ref fetches);using(var db=new LocalDataService(root))db.Observe("cache-test","read",new{value=42});};
   System.Threading.Tasks.Parallel.For(0,8,i=>ObservationRefresh.Run(root,"cache-test","read",TimeSpan.FromMinutes(5),false,fetch));if(fetches!=1)throw new Exception("Read refresh was not coalesced");
   ObservationRefresh.Run(root,"cache-test","read",TimeSpan.FromMinutes(5),true,fetch);if(fetches!=2)throw new Exception("Explicit refresh did not bypass cache");
   int failures=0;for(int i=0;i<2;i++)try{ObservationRefresh.Run(root,"cache-test","failure",TimeSpan.FromMinutes(5),false,()=>{failures++;throw new IOException("synthetic offline");});}catch(Exception){}if(failures!=1)throw new Exception("Refresh failure retry was not bounded");
   if(ObservationRefresh.Fresh(root,"other-profile","read",TimeSpan.FromMinutes(5)))throw new Exception("Refresh cache crossed profiles");
   using(var store=new LocalDataService(root)){
    store.Observe("default","attendance",new {state="unknown",label="한글 O'Brien"});
    if(Convert.ToString(new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(store.Recent("default","attendance")[0]["payload"])["label"])!="한글 O'Brien")throw new Exception("SQLite Unicode roundtrip");
    if(store.Recent("other","attendance").Count!=0)throw new Exception("Profile isolation");
    store.SetSecret("hoyolab","synthetic-test-secret");
    if(store.GetSecret("hoyolab")!="synthetic-test-secret")throw new Exception("DPAPI roundtrip");
    if(File.ReadAllText(Path.Combine(root,"secrets","hoyolab.dpapi")).Contains("synthetic-test-secret"))throw new Exception("Plaintext credential");
    store.Backup(Path.Combine(root,"backup.db"));
    bool refused=false;try{store.Backup(Path.Combine(root,"backup.db"));}catch(IOException){refused=true;}if(!refused)throw new Exception("Backup overwrite allowed");
    store.DeleteSecret("hoyolab");if(store.GetSecret("hoyolab")!=null)throw new Exception("Disconnect retained token");
    store.Execute("PRAGMA user_version=3");
   }
   string legacy=Path.Combine(root,"legacy");Directory.CreateDirectory(legacy);File.Copy(Path.Combine(root,"backup.db"),Path.Combine(legacy,"catheryne.db"));
   using(var old=new LocalDataService(legacy)){old.Execute("DROP TABLE achievements; PRAGMA user_version=1");}
   using(var migrated=new LocalDataService(legacy)){if(migrated.Recent("default","attendance").Count!=1||migrated.Query("SELECT * FROM achievements").Count!=0)throw new Exception("Schema migration lost data");}
   bool newer=false;try{using(var store=new LocalDataService(root)){store.Recent("default","attendance");} }catch(InvalidDataException){newer=true;}if(!newer)throw new Exception("Future schema accepted");
   string restored=Path.Combine(root,"restored");Directory.CreateDirectory(restored);
   File.Copy(Path.Combine(root,"backup.db"),Path.Combine(restored,"catheryne.db"));
   using(var store=new LocalDataService(restored)){if(store.Recent("default","attendance").Count!=1)throw new Exception("Backup persistence");}

  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
