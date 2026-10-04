using AkashaScanner.Core.DataFiles;

namespace AkashaScanner.Core.Achievements
{
    public class AchievementDataFileRepository : DataFileRepository<AchievementOutput>
    {
        public override int CurrentVersion => 1;

        public AchievementDataFileRepository() : base("achievements") { }
        public override List<IDataFile<AchievementOutput>> List() {
            var files=base.List();
            var folder=Path.Combine(Utils.ExecutableDirectory,"ScannedData");
            if(Directory.Exists(folder))foreach(var path in Directory.GetFiles(folder,"progress_*.json")) {
                try {var data=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
                    if((int?)data["Version"]!=1||data["Data"] is not Newtonsoft.Json.Linq.JObject items)continue;
                    files.Add(new DataFile<AchievementOutput>(path,File.GetLastWriteTime(path),items.Count,1));
                } catch(Newtonsoft.Json.JsonException) { } catch(IOException) { }
            }
            return files.OrderByDescending(f=>f.CreatedAt).ToList();
        }
    }
}
