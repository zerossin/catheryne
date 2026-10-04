using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

internal static class RedemptionTests {
 static void Check(bool value,string name){if(!value)throw new Exception("Redemption: "+name);}
 static RedeemAccount Account(string id="800000001"){return new RedeemAccount{Uid=id,Server="os_asia",Name="Fixture",Cookie="cookie_token_v2=fixture; account_id_v2=fixture"};}
 static List<RedeemCode> Feed(){return new[]{"TEST1","TEST2","TEST3"}.Select(c=>new RedeemCode{Code=c,Rewards="Fixture",State="unverified"}).ToList();}
 internal static void Run(){
  string root=Path.Combine(Path.GetTempPath(),"catheryne-redemption-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var parsed=RedemptionService.ParseFeed("{\"game\":\"genshin\",\"codes\":[{\"game\":\"genshin\",\"status\":\"OK\",\"code\":\"test1\",\"rewards\":\"x\"},{\"game\":\"genshin\",\"status\":\"OK\",\"code\":\"TEST1\"},{\"game\":\"genshin\",\"status\":\"EXPIRED\",\"code\":\"TEST2\"}]}");
   Check(parsed.Count==1&&parsed[0].Code=="TEST1","actual feed envelope, normalize and deduplicate");
   bool rejected=false;try{RedemptionService.ParseFeed("{\"codes\":[]}");}catch{rejected=true;}Check(rejected,"malformed feed rejected");
   Check(RedemptionService.State(-2017)=="already"&&RedemptionService.State(-2018)=="already"&&RedemptionService.State(-2001)=="expired"&&RedemptionService.State(-1071)=="login"&&RedemptionService.State(-2016)=="cooldown"&&RedemptionService.State(99999)=="blocked","official outcomes never inferred as success");
   Check(!RedemptionService.HasToken("ltoken_v2=fixture; ltuid_v2=fixture"),"attendance cookies cannot redeem");
   int calls=0,fetches=0;var service=new RedemptionService(root,()=>{fetches++;return Feed();},(a,c)=>{calls++;return c=="TEST1"?0:c=="TEST2"?-2017:-2001;},t=>{});
   service.Run(false);Check(calls==0,"anonymous refresh has no writes");service.Connect(Account());
   var result=service.Run(true);Check(calls==3&&result.Codes.Select(x=>x.State).SequenceEqual(new[]{"redeemed","already","expired"}),"persist official results");
   service.Run(true);Check(calls==3,"repeat run skips completed codes");
   Check(!File.ReadAllText(Path.Combine(root,"secrets",RedemptionService.Secret+".dpapi")).Contains("fixture"),"credentials protected at rest");
   service.Connect(Account("800000002"));service.Run(true);Check(calls==6,"history is scoped by UID and server");
   service.Connect(Account("800000003"));int blockedCalls=0;var blocked=new RedemptionService(root,Feed,(a,c)=>{blockedCalls++;return -1071;},t=>{});Check(blocked.Run(true).Status=="login"&&blockedCalls==1,"invalid login stops batch");Check(blocked.Saved().Status=="login","authentication failure stays visible after reopening");
   var failing=new RedemptionService(root,()=>{throw new IOException("private details");},(a,c)=>0,t=>{});try{failing.Run(false);}catch(IOException){}Check(service.Saved().Codes.Count==3,"feed failure retains previous list");
   AppPreferences.Set("automaticRedeem",true,root);blocked.Run(true,true);Check(!blocked.Due(),"automatic cadence survives service restart");int before=blockedCalls;blocked.Run(true,true);Check(blockedCalls==before,"automatic retries respect cadence");
   service.Connect(Account("800000004"));Check(!Equals(AppPreferences.Read(root)["automaticRedeem"],true),"switching account requires opting in again");using(var lease=new FileStream(Path.Combine(root,"redemption.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){Check(service.Run(true).Status=="busy"&&calls==6,"cross-process lease prevents duplicate exchange");}
   int cancelCalls=0;var cancel=new RedemptionService(root,Feed,(a,c)=>{cancelCalls++;return 0;},t=>{service.Disconnect();});cancel.Run(true);Check(cancelCalls==1,"disconnect during pacing prevents next exchange");
   Check(service.Account()==null&&!Equals(AppPreferences.Read(root)["automaticRedeem"],true),"disconnect removes secret and disables automation");
   service.Connect(Account("800000005"));var token=new CancellationTokenSource();token.Cancel();bool cancelled=false;try{service.Run(true,false,token.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"shutdown cancellation prevents network");
   var serialized=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(service.Saved());Check(!serialized.Contains("Cookie")&&!serialized.Contains("cookie_token"),"query result excludes authentication material");
  }finally{Directory.Delete(root,true);}
 }
}
