using AkashaScanner.Core.DataCollections;
using AkashaScanner.Core.Navigation.Achievement;
using AkashaScanner.Core.ProcessControl;
using AkashaScanner.Core.ResultHandler;
using AkashaScanner.Core.Scrappers;
using AkashaScanner.Core.ScrapPlans;
using AkashaScanner.Core.Screenshot;
using AkashaScanner.Core.Suspender;
using AkashaScanner.Core.TextRecognition;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace AkashaScanner.Core.Achievements
{
    public class AchievementScrapper : BaseScrapper<Achievement, IAchievementConfig>
    {
        // BGR: 212, 242, 254
        private static readonly Scalar StarColorLower = new(202, 232, 244);
        private static readonly Scalar StarColorUpper = new(222, 252, 255);

        private readonly IAchievementNavigation Navigation;
        private readonly IAchievementCollection Achievements;

        private Rectangle StarsRect;
        private Rectangle CompletionRect;
        private int StarMinWidth;
        private int StarMaxWidth;
        private int StarMinHeight;
        private int StarMaxHeight;

        public AchievementScrapper(
            ILogger<AchievementScrapper> logger,
            ISuspender suspender,
            GameWindow win,
            IProcessControl control,
            ITextRecognitionService ocr,
            IScreenshotProvider screenshots,
            IResultHandler<Achievement> resultHandler,
            IAchievementNavigation navigation,
            IScrapPlanManager<IAchievementConfig, Achievement> scrapPlan,
            IAchievementCollection achievements)
        {
            Logger = logger;
            Suspender = suspender;
            Win = win;
            Control = control;
            ResultHandler = resultHandler;
            ScrapPlan = scrapPlan;
            Ocr = ocr;
            Screenshots = screenshots;
            Navigation = navigation;
            Achievements = achievements;
        }

        protected override void Init()
        {
            base.Init();
            Navigation.Init();
            if(CatheryneScanning.ScanBridge.Active)AchievementNavigation.Open(Win,Screenshots,Suspender);
            StarsRect = Win.GetRectangle(520, 180, 560, 225);
            CompletionRect = Win.GetRectangle(1115, 190, 1210, 220);
            StarMinWidth = Win.Scale(9);
            StarMaxWidth = Win.Scale(14);
            StarMinHeight = Win.Scale(9);
            StarMaxHeight = Win.Scale(14);
        }

        protected override void Execute(IAchievementConfig config)
        {
            int searched=0,reused=0,order=0;string outcome="failed";
            var unresolved=new List<int>();
            var handler=ResultHandler as AchievementResultHandler;
            var entries=Achievements.SelectMany(category=>category.Achievements).ToArray();
            var search=new AchievementSearch(entries.Select(entry=>entry.Name));
            var queries=entries.Select((entry,index)=>new {entry,query=search.Query(index)}).ToDictionary(x=>x.entry,x=>x.query);
            StartMonitoringProcess(); ScrapPlan.Activate();
            // Seed all verified positives before the first input. An interrupted scan must
            // not lose earlier records from categories it has not reached yet.
            foreach(var category in Achievements)foreach(var entry in category.Achievements)
                foreach(var id in entry.Ids)if(config.AchievementOverrides.TryGetValue(id,out var done)&&done) {
                    var known=new Achievement {Id=id,CategoryId=category.Id};
                    if(handler!=null)handler.Reuse(known,++order);else ResultHandler.Add(known,++order);reused++;
                }
            handler?.Checkpoint("running",searched,reused,unresolved);
            try {
                foreach(var category in Achievements) {
                    if(ShouldStop())break;
                    foreach(var entry in category.Achievements) {
                        if(ShouldStop())break;
                        if(entry.Ids.All(id=>config.AchievementOverrides.ContainsKey(id)||handler?.Observed(id)==true))continue;
                        if(queries[entry]==null) {
                            unresolved.AddRange(entry.Ids.Where(id=>!config.AchievementOverrides.ContainsKey(id)));continue;
                        }
                        Navigation.ClearSearch();if(ShouldStop())break;
                        Navigation.Search(queries[entry]!);if(ShouldStop())break;
                        using var screenshot=Screenshots.Capture(StarsRect);
                        int stars=ReadStars(screenshot,entry.Ids.Count);searched++;
                        if(stars==0){
                            unresolved.AddRange(entry.Ids.Where(id=>!config.AchievementOverrides.ContainsKey(id)));
                            handler?.Checkpoint("running",searched,reused,unresolved);continue;
                        }
                        // Re-sample positives before recording, and retain zero as unverified.
                        Suspender.Sleep(80);if(ShouldStop())break;
                        using var confirmation=Screenshots.Capture(StarsRect);
                        if(stars!=ReadStars(confirmation,entry.Ids.Count)) {
                            unresolved.AddRange(entry.Ids.Where(id=>!config.AchievementOverrides.ContainsKey(id)));
                        } else {
                            for(int i=0;i<stars;i++)if(!config.AchievementOverrides.ContainsKey(entry.Ids[i]))
                                ResultHandler.Add(new Achievement {Id=entry.Ids[i],CategoryId=category.Id},++order);
                            // Unobserved tiers stay candidates for a later scan, never cached false.
                            unresolved.AddRange(entry.Ids.Skip(stars).Where(id=>!config.AchievementOverrides.ContainsKey(id)));
                        }
                        handler?.Checkpoint("running",searched,reused,unresolved);
                    }
                }
                if(!Interrupted)ResultHandler.Save();
                outcome=Interrupted?"interrupted":"scan_finished";
            } catch(OperationCanceledException) {
                Interrupted=true;outcome="interrupted";throw;
            } finally {
                StopMonitoringProcess();
                handler?.Checkpoint(outcome,searched,reused,unresolved);
            }
        }

        internal static int ConfirmedStars(int stars,int tiers,string status)
        {
            stars=Math.Min(stars,tiers);
            if(stars<=0||stars<tiers)return stars;
            // A gold ornament can resemble one star. The last tier also needs
            // its visible completion/claim label; an unreadable label stays unknown.
            return System.Text.RegularExpressions.Regex.IsMatch(status ?? "",@"\b(?:Completed|Claim)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase)?stars:0;
        }

        private int ReadStars(Bitmap image,int tiers)
        {
            int stars=Math.Min(GetStars(image),tiers);
            if(stars<=0||stars<tiers)return stars;
            using var completion=Screenshots.Capture(CompletionRect);
            return ConfirmedStars(stars,tiers,Ocr.FindText(completion));
        }
        private int GetStars(Bitmap image)
        {
            using var src = image.ToMat();
            using var filtered = new Mat();
            Cv2.InRange(src, StarColorLower, StarColorUpper, filtered);
            using var ret = new Mat();
            Cv2.Threshold(filtered, ret, 128, 255, ThresholdTypes.Binary);
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var nLabels = Cv2.ConnectedComponentsWithStats(ret, labels, stats, centroids);
            int stars = 0;
            for (int i = 0; i < nLabels; ++i)
            {
                var width = stats.Get<int>(i, 2);
                var height = stats.Get<int>(i, 3);
                if (StarMinWidth <= width && width <= StarMaxWidth && StarMinHeight <= height && height <= StarMaxHeight)
                    ++stars;
            }
            return stars;
        }
    }
}
