using AkashaScanner.Core;
using AkashaScanner.Core.DataCollections;
using AkashaScanner.Core.Scrappers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CatheryneScanning;
namespace AkashaScanner {
 internal static class Headless {
  internal static Dictionary<int,bool> Known(string path){
   var result=new Dictionary<int,bool>();if(!File.Exists(path))return result;
   var value=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
   if((int?)value["Version"]!=1||value["Data"] is not Newtonsoft.Json.Linq.JObject data)throw new InvalidDataException("확인된 업적 자료 형식이 올바르지 않습니다.");
   foreach(var pair in data){if(!int.TryParse(pair.Key,out int id)||id<=0||pair.Value?.Type!=Newtonsoft.Json.Linq.JTokenType.Integer||(int)pair.Value<0)throw new InvalidDataException("확인된 업적 ID와 카테고리가 필요합니다.");result.Add(id,true);}return result;
  }
  internal static int Run(){try{ScanBridge.Check();ScanBridge.Write("running","준비 중");if(!ScanBridge.GameRunning())throw new InvalidOperationException("원신을 실행해 주세요.");
   using var host=Host.CreateDefaultBuilder().ConfigureServices(s=>s.AddCoreServices()).Build();
   var catalog=host.Services.GetRequiredService<IAchievementCollection>();catalog.LoadLocal().GetAwaiter().GetResult();if(!catalog.IsLoaded())catalog.LoadRemote().GetAwaiter().GetResult();if(!catalog.IsLoaded())throw new InvalidOperationException("업적 목록을 준비하지 못했습니다.");
   var config=host.Services.GetRequiredService<IConfig>();config.Load().GetAwaiter().GetResult();config.AchievementOverrides=Known(Path.Combine(ScanBridge.Folder,"known-achievements.json"));
   using var scanner=host.Services.GetRequiredService<IScrapper<IAchievementConfig>>();bool done=scanner.Start(config);ScanBridge.Check();if(!done)throw new OperationCanceledException();if(!File.Exists(Path.Combine(ScanBridge.Folder,"result.json")))throw new InvalidOperationException("수집 결과가 없습니다.");ScanBridge.Write("completed","수집 완료");return 0;
  }catch(OperationCanceledException){ScanBridge.Write("cancelled","수집 중단");return 2;}catch(Exception e){ScanBridge.Write("failed","수집 실패",error:e.Message);return 1;}}
 }
}
