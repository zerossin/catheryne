using Newtonsoft.Json.Linq;
namespace AkashaScanner.Core.Achievements {
 internal static class CheckpointTests {
  internal static void Run(){
   AchievementSearch.Test();
   if(AchievementScrapper.ConfirmedStars(1,1,"0/1")!=0||AchievementScrapper.ConfirmedStars(1,1,"")!=0||AchievementScrapper.ConfirmedStars(1,1,"Completed")!=1||AchievementScrapper.ConfirmedStars(3,3,"Claim")!=3||AchievementScrapper.ConfirmedStars(2,3,"7/10")!=2)throw new Exception("Final achievement tier requires visible completion; partial tiers remain valid");
   string root=Path.Combine(Path.GetTempPath(),"catheryne-checkpoint-"+Guid.NewGuid().ToString("N"));
   try{var handler=new AchievementResultHandler(new AchievementDataFileRepository()){ProgressDirectory=root};handler.Init();handler.Add(new Achievement {Id=1,CategoryId=2},1);handler.Checkpoint("running",1,0,new List<int>{3});handler.Add(new Achievement {Id=1,CategoryId=2},2);handler.Add(new Achievement {Id=4,CategoryId=2},3);handler.Checkpoint("interrupted",2,0,new List<int>{3});var path=Directory.GetFiles(root,"*.json").Single();var data=JObject.Parse(File.ReadAllText(path));if(((JObject)data["Data"]!).Count!=2||(string?)data["Scan"]!["Status"]!="interrupted")throw new Exception("Checkpoint/dedup failed");var imported=new DataFiles.DataFile<AchievementOutput>(path,DateTime.Now,2,1);if(!imported.Read(out var output)||output.Count!=2||output[4]!=2)throw new Exception("Resume format failed");if(Directory.GetFiles(root,"*.tmp").Length!=0)throw new Exception("Uncommitted checkpoint");
    handler.Reuse(new Achievement {Id=1,CategoryId=2},4);
    if(handler.FreshData.ContainsKey(1)||handler.FreshData.Count!=1||handler.FreshData[4]!=2)throw new Exception("Reused records must not be imported as fresh observations");
    handler.Add(new Achievement {Id=1,CategoryId=2},5);if(handler.FreshData.Count!=2)throw new Exception("Fresh observation cannot remain marked reused");
    string known=Path.Combine(root,"known-achievements.json");File.WriteAllText(known,"{\"Version\":1,\"Data\":{\"1\":0}}");
    if(!Headless.Known(known).TryGetValue(1,out bool completed)||!completed||Headless.Known(known+".missing").Count!=0)throw new Exception("Known achievement loading failed");
    CatheryneScanning.ScanBridge.Folder=root;
    File.WriteAllText(Path.Combine(root,"resume.json"),"{\"Version\":1,\"Data\":{\"12\":3}}");
    var resumed=new AchievementResultHandler(new AchievementDataFileRepository()){ProgressDirectory=root};resumed.Init();
    if(!resumed.Observed(12)||resumed.FreshData[12]!=3)throw new Exception("Resume lost unverified positive observations");
    resumed.Checkpoint("interrupted",1,0,new List<int>());
    var checkpoint=JObject.Parse(File.ReadAllText(Path.Combine(root,"checkpoint.json")));if((int?)checkpoint["Data"]?["12"]!=3)throw new Exception("Job checkpoint missing");
    CatheryneScanning.ScanBridge.Folder=null;
    string status=Path.Combine(root,"status.txt");File.WriteAllText(status,"before");
    System.Threading.Tasks.Task writer;
    using(var reader=new FileStream(status,FileMode.Open,FileAccess.Read,FileShare.Read)){
     writer=System.Threading.Tasks.Task.Run(()=>CatheryneScanning.ScanBridge.Publish(status,"after"));
     System.Threading.Thread.Sleep(120);
     if(File.ReadAllText(status)!="before")throw new Exception("Locked snapshot was changed");
    }
    if(!writer.Wait(3000)||File.ReadAllText(status)!="after")throw new Exception("Atomic publication did not recover");
    if(Directory.GetFiles(root,"*.tmp").Length!=0)throw new Exception("Publication left temporary files");}
   finally{if(Directory.Exists(root))Directory.Delete(root,true);}
  }
 }
}
