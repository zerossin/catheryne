using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

// Canonical refresh plan for UI and AI. A completed section is committed before
// advancing; interruption never promotes a partial equipment list to full coverage.
internal static class CollectionRefresh {
 internal static readonly string[] Areas={"characters","weapons","artifacts","materials","achievements"};
 internal static string Label(string area){return Locale.T(area=="characters"?"캐릭터":area=="weapons"?"무기":area=="artifacts"?"성유물":area=="materials"?"재료":"업적");}
 internal static string Kind(Dictionary<string,object> p){string kind=CodexChat.S(p,"kind");if(!new[]{"auto","all","account","achievements"}.Contains(kind))throw new ArgumentException(Locale.T("자료 종류를 확인해 주세요."));return kind;}
 internal static Dictionary<string,object> Read(string path){return File.Exists(path)?StoryClient.Read(path):new Dictionary<string,object>();}
 internal static Dictionary<string,object> Dates(string root){return Read(Path.Combine(root,"collection-refresh.json"));}
 internal static bool Due(string area,Dictionary<string,object> dates,DateTime now){DateTime when;return !DateTime.TryParse(CodexChat.S(dates,area),null,System.Globalization.DateTimeStyles.RoundtripKind,out when)||now.ToUniversalTime()-when.ToUniversalTime()>=TimeSpan.FromDays(area=="characters"||area=="weapons"?7:1);}
 internal static string[] Plan(string kind,CatheryneScanning.AccountScanOptions options,Dictionary<string,object> dates,DateTime now){return kind=="auto"?Areas.Where(x=>Due(x,dates,now)).ToArray():kind=="all"?options.Sections.Concat(new[]{"achievements"}).ToArray():kind=="achievements"?new[]{"achievements"}:options.Sections.ToArray();}
 static string PendingPath(string root){return Path.Combine(root,"scan-jobs","pending-refresh.json");}
 internal static Dictionary<string,object> PendingRequest(string root){return Read(PendingPath(root));}
 static string Signature(string kind,CatheryneScanning.AccountScanOptions options){return kind+CatheryneTools.Json().Serialize(options.Parameters());}
 internal static bool Pending(string root,string kind,CatheryneScanning.AccountScanOptions options){return CodexChat.S(PendingRequest(root),"signature")==Signature(kind,options);}
 internal static string Run(string root,string kind,string id,Action<string> progress){
  if(GameEnvironment.Remote(root))throw new InvalidOperationException(Locale.T("분리 실행 환경에서 수집을 시작해 주세요."));
  using(var lease=GameInputLease.Acquire(root)){
    string control=CollectionScanner.Folder(root,id);Directory.CreateDirectory(control);
    var options=CatheryneScanning.AccountScanOptions.Read(Read(Path.Combine(control,"options.json")));
    string pending=PendingPath(root);var pointer=Read(pending);string sessionId=CodexChat.S(pointer,"signature")==Signature(kind,options)?CodexChat.S(pointer,"id"):"";
    string session=string.IsNullOrEmpty(sessionId)?control:CollectionScanner.Folder(root,sessionId);
    string journalPath=Path.Combine(session,"refresh.json");var journal=Read(journalPath);
    string[] plan;
    if(journal.ContainsKey("plan"))plan=((System.Collections.IEnumerable)journal["plan"]).Cast<object>().Select(Convert.ToString).ToArray();
    else {plan=Plan(kind,options,Dates(root),DateTime.UtcNow);journal["plan"]=plan;}
    if(plan.Any(x=>!Areas.Contains(x)))throw new InvalidDataException(Locale.T("수집 작업의 범위가 올바르지 않습니다."));
    if(plan.Length==0)return Locale.T("자료가 최신 상태입니다.");
    if(!GameRequirement.Running())throw new InvalidOperationException(Locale.T("원신을 실행해 주세요."));
    Directory.CreateDirectory(session);AtomicFile.Write(journalPath,CatheryneTools.Json().Serialize(journal));AtomicFile.Write(pending,CatheryneTools.Json().Serialize(new{id=Path.GetFileName(session),kind=kind,options=options.Parameters(),signature=Signature(kind,options)}));
    var completedAt=new Dictionary<string,object>();foreach(string area in plan)completedAt[area]=CodexChat.S(journal,area+"DoneAt");foreach(string area in plan)if(Due(area,completedAt,DateTime.UtcNow))journal.Remove(area+"Done");
    Execute(plan,journal,()=>File.Exists(Path.Combine(control,"stop")),(area,index)=>{
     string attempt=Path.Combine(session,area,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(attempt);
     string previous=CodexChat.S(journal,area+"Attempt");Guid parsed;
     if(Guid.TryParse(previous,out parsed)){
      string checkpoint=Path.Combine(session,area,parsed.ToString("N"),"checkpoint.json");
      if(!File.Exists(checkpoint))checkpoint=Path.Combine(session,area,parsed.ToString("N"),"resume.json");if(File.Exists(checkpoint))File.Copy(checkpoint,Path.Combine(attempt,"resume.json"));
     }
     journal[area+"Attempt"]=Path.GetFileName(attempt);AtomicFile.Write(journalPath,CatheryneTools.Json().Serialize(journal));
     var stageOptions=options.Parameters();stageOptions["sections"]=new[]{area};
     if(area!="achievements")AtomicFile.Write(Path.Combine(attempt,"options.json"),CatheryneTools.Json().Serialize(stageOptions));
     progress(Label(area)+"  "+(index+1)+" / "+plan.Length);
     CollectionScanner.RunStage(root,area=="achievements"?area:"account",attempt,control,message=>progress(Label(area)+"  "+(index+1)+" / "+plan.Length+"\n"+message));
     var dates=Dates(root);if(area=="characters"||area=="achievements"||Equals(options.Coverage()[area],"full")){dates[area]=DateTime.UtcNow.ToString("o");AtomicFile.Write(Path.Combine(root,"collection-refresh.json"),CatheryneTools.Json().Serialize(dates));}
    },()=>AtomicFile.Write(journalPath,CatheryneTools.Json().Serialize(journal)));
    File.Delete(pending);return Locale.T("선택한 자료를 최신화했습니다.");
  }
 }
 // Testable transaction boundary: process success, persistent completion, next stage.
 internal static void Execute(string[] plan,Dictionary<string,object> journal,Func<bool> stopped,Action<string,int> run,Action save){
  for(int i=0;i<plan.Length;i++){if(stopped())throw new OperationCanceledException();string key=plan[i]+"Done";object done;if(journal.TryGetValue(key,out done)&&Equals(done,true))continue;run(plan[i],i);journal[key]=true;journal[plan[i]+"DoneAt"]=DateTime.UtcNow.ToString("o");save();}
 }
}