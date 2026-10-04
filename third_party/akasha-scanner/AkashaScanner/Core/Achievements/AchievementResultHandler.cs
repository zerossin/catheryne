using AkashaScanner.Core.DataFiles;
using AkashaScanner.Core.ResultHandler;
using Newtonsoft.Json;

namespace AkashaScanner.Core.Achievements
{
    public class AchievementResultHandler : IResultHandler<Achievement>
    {
        private readonly IDataFileRepository<AchievementOutput> DataFileRepository;
        private readonly AchievementOutput Dict = new();
        private readonly HashSet<int> ReusedIds = new();
        internal Dictionary<int,int> FreshData => Dict.Where(pair=>!ReusedIds.Contains(pair.Key)).ToDictionary(pair=>pair.Key,pair=>pair.Value);
        private string ProgressPath = "";
        internal string ProgressDirectory=Path.Combine(Utils.ExecutableDirectory,"ScannedData");
        private DateTime Started;
        public AchievementResultHandler(IDataFileRepository<AchievementOutput> repository) { DataFileRepository = repository; }
        public void Init() {
            Dict.Clear(); ReusedIds.Clear(); Started = DateTime.UtcNow;
            ProgressPath = Path.Combine(ProgressDirectory, "progress_" + Guid.NewGuid().ToString("N") + ".json");
            if(CatheryneScanning.ScanBridge.Active) {
                string resume=Path.Combine(CatheryneScanning.ScanBridge.Folder,"resume.json");
                if(File.Exists(resume)) {
                    var saved=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(resume));
                    if((int?)saved["Version"]!=1||saved["Data"] is not Newtonsoft.Json.Linq.JObject data)throw new InvalidDataException("Invalid achievement checkpoint");
                    foreach(var pair in data){if(!int.TryParse(pair.Key,out int id)||id<=0||pair.Value?.Type!=Newtonsoft.Json.Linq.JTokenType.Integer||(int)pair.Value<0)throw new InvalidDataException("Invalid checkpoint ID");Dict[id]=(int)pair.Value;}
                }
            }
        }
        internal bool Observed(int id)=>Dict.ContainsKey(id);
        public void Add(Achievement item, int order) { Dict[item.Id] = item.CategoryId; ReusedIds.Remove(item.Id); }
        internal void Reuse(Achievement item, int order) { Add(item,order); ReusedIds.Add(item.Id); }
        public void Checkpoint(string status, int searched, int reused, List<int> unresolved) {
            CatheryneScanning.ScanBridge.Write("running","업적 확인",searched);
            Directory.CreateDirectory(Path.GetDirectoryName(ProgressPath)!);
            var text = JsonConvert.SerializeObject(new {Version=1,Data=Dict,Scan=new {Status=status,StartedUtc=Started,UpdatedUtc=DateTime.UtcNow,Searched=searched,Reused=reused,ObservedUnknownIds=unresolved}}, Formatting.Indented);
            CatheryneScanning.ScanBridge.Publish(ProgressPath,text);
            if(CatheryneScanning.ScanBridge.Active)CatheryneScanning.ScanBridge.Publish(Path.Combine(CatheryneScanning.ScanBridge.Folder,"checkpoint.json"),JsonConvert.SerializeObject(new {Version=1,Data=FreshData,Scan=new {Status=status,Searched=searched,Reused=reused,UpdatedUtc=DateTime.UtcNow}}));
        }
        public void Save() { DataFileRepository.Create(Dict.Count).Write(Dict); if(CatheryneScanning.ScanBridge.Active)CatheryneScanning.ScanBridge.Result(new {Version=1,Data=FreshData}); }
    }
}
