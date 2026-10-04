using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tesseract;
using CatheryneScanning;
namespace InventoryKamera {
 internal static class Headless {
  // Reuse the upstream builder; publish only after the complete staged update succeeds.
  static void RefreshCatalog(){
   string folder=Path.GetFullPath("inventorylists"),checkedFile=Path.Combine(folder,"last-checked.txt");
   if(File.Exists(checkedFile)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(checkedFile)<TimeSpan.FromDays(1))return;
   string staging=Path.Combine(Path.GetDirectoryName(folder),"catalog-update-"+Guid.NewGuid().ToString("N"));
   bool publishing=false;string[] files={"characters.json","weapons.json","artifacts.json","materials.json","version.txt"};
   try{
    ScanBridge.Check();ScanBridge.Write("running","인식 목록 확인 중");
    var database=new DatabaseManager();
    if(database.UpdateAvailable()){
     Directory.CreateDirectory(staging);foreach(string file in files)File.Copy(Path.Combine(folder,file),Path.Combine(staging,file));
     database.ListsDir=staging+Path.DirectorySeparatorChar;
     ScanBridge.Write("running","인식 목록 갱신 중");
     if(database.UpdateGameData().ToString()=="Fail")throw new InvalidDataException("인식 목록을 갱신하지 못했습니다.");
     ScanBridge.Check();foreach(string file in files.Where(x=>x.EndsWith(".json")))JObject.Parse(File.ReadAllText(Path.Combine(staging,file)));
     publishing=true;foreach(string file in files)ScanBridge.Publish(Path.Combine(folder,file),File.ReadAllText(Path.Combine(staging,file)));
    }
    ScanBridge.Publish(checkedFile,DateTime.UtcNow.ToString("o"));
   }catch(OperationCanceledException){throw;}
   catch(Exception error){if(publishing)throw;NLog.LogManager.GetCurrentClassLogger().Warn(error,"Catalogue refresh failed; keeping existing recognition data");ScanBridge.Write("running","기존 인식 목록 사용");}
   finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
  }
  internal static int Run(){InventoryKamera data=null;try{
   ScanBridge.Write("running","준비 중");ScanBridge.Check();
   string optionsPath=Path.Combine(ScanBridge.Folder,"options.json");var options=AccountScanOptions.Read(File.Exists(optionsPath)?Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string,object>>(File.ReadAllText(optionsPath)):new Dictionary<string,object>());
   var settings=Properties.Settings.Default;settings.ScanCharacters=options.Includes("characters");settings.ScanWeapons=options.Includes("weapons");settings.ScanArtifacts=options.Includes("artifacts");settings.ScanMaterials=options.Includes("materials");settings.ScanCharDevItems=options.Includes("materials");settings.MinimumWeaponRarity=options.WeaponRarity;settings.MinimumWeaponLevel=options.WeaponLevel;settings.MinimumArtifactRarity=options.ArtifactRarity;settings.MinimumArtifactLevel=options.ArtifactLevel;settings.EquipWeapons=true;settings.EquipArtifacts=true;settings.LogScreenshots=false;
   RefreshCatalog();CustomCharacters.Load();Navigation.Initialize();var aspect=Navigation.GetAspectRatio();if(aspect!=new Size(16,9)&&aspect!=new Size(8,5))throw new InvalidOperationException("16:9 또는 16:10 화면이 필요합니다.");
   ScanBridge.Check(true);Navigation.SetDelay(1);data=new InventoryKamera();data.GatherData();ScanBridge.Check();var result=JObject.FromObject(new GOOD(data));result["catheryne"]=new JObject{["scannedAt"]=DateTime.UtcNow.ToString("o"),["coverage"]=JObject.FromObject(options.Coverage()),["scanOptions"]=JObject.FromObject(options.Parameters()),["characterAliases"]=JObject.FromObject(CustomCharacters.Aliases),["customCharacters"]=JArray.FromObject(CustomCharacters.Observed)};foreach(string section in AccountScanOptions.Areas){if(!options.Includes(section))result.Remove(section);else if(result[section]==null||result[section].Type==JTokenType.Null)result[section]=section=="materials"?(JToken)new JObject():new JArray();}ScanBridge.Result(result);if(UserInterface.HeadlessErrors>0)throw new InvalidOperationException("인식 오류가 있어 결과를 자동 반영하지 않았습니다. 수집 원본은 보존했습니다.");ScanBridge.Write("completed","수집 완료");return 0;
  }catch(OperationCanceledException){ScanBridge.Write("cancelled","수집 중단");return 2;}catch(Exception e){ScanBridge.Write("failed","수집 실패",error:e.Message);return 1;}finally{if(data!=null)data.StopImageProcessorWorkers();RecognitionCache.Flush();ScanBridge.Publish(Path.Combine(ScanBridge.Folder,"recognition.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{reused=RecognitionCache.Hits,recognized=RecognitionCache.Misses}));}}
 }
}

namespace InventoryKamera {
 // Custom names are account data supplied by the launcher, never source defaults.
 internal static class CustomCharacters {
  internal static readonly Dictionary<string,string> Aliases=new Dictionary<string,string>();
  internal static readonly List<object> Observed=new List<object>();
  static TesseractEngine korean;static readonly object OcrGate=new object();
  internal static void Load(){string path=Path.Combine(ScanBridge.Folder,"names.json");if(File.Exists(path)){var names=JObject.Parse(File.ReadAllText(path));foreach(var pair in names)if(new[]{"Traveler","Wanderer","Manequin1","Manequin2"}.Contains(pair.Key))Aliases[pair.Key]=(string)pair.Value;}}
  internal static void Apply(){
   foreach(var key in new[]{"Manequin1","Manequin2"})if(!GenshinProcesor.Characters.ContainsKey(key.ToLower()))GenshinProcesor.Characters[key.ToLower()]=new JObject{["GOOD"]=key,["Element"]=new JArray("anemo","geo","electro","dendro","hydro","pyro","cryo"),["WeaponType"]=0};
   foreach(var pair in Aliases)if(!string.IsNullOrWhiteSpace(pair.Value))GenshinProcesor.UpdateCharacterName(pair.Key,pair.Value);
  }
  static string Normalize(string value){return System.Text.RegularExpressions.Regex.Replace(value??"",@"[^\p{L}\p{N}]","").ToLowerInvariant();}
  internal static string Match(Bitmap image){
   if(!ScanBridge.Active||Aliases.Count==0||!File.Exists(Path.Combine("tessdata","kor.traineddata")))return null;
   lock(OcrGate){if(korean==null)korean=new TesseractEngine("tessdata","kor",EngineMode.LstmOnly);
   using(var page=korean.Process(image,PageSegMode.SingleLine)){string text=page.GetText();int slash=Math.Max(text.LastIndexOf('/'),text.LastIndexOf(':'));if(slash>=0)text=text.Substring(slash+1);text=Normalize(text);string match=null;foreach(var pair in Aliases)if(text==Normalize(pair.Value)){if(match!=null)return null;match=pair.Key;}return match;}}
  }
  internal static bool Extended(string name,string element){return name=="Manequin1"||name=="Manequin2"||(name=="Traveler"&&!GenshinProcesor.CharacterMatchesElement(name,element));}
  internal static void Record(string name,string element,int level,bool ascended){string alias;Aliases.TryGetValue(name,out alias);Observed.Add(new{identity=name,alias=alias,element=element,level=level,ascended=ascended,observedAt=DateTime.UtcNow.ToString("o"),coverage="identity_level"});}
 }
}
