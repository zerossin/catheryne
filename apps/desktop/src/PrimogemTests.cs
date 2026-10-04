using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class PrimogemTests {
 static void Check(bool value,string message){if(!value)throw new Exception("Primogems: "+message);}
 static Dictionary<string,object> Map(string json){return CatheryneTools.Json().Deserialize<Dictionary<string,object>>(json);}
 static Dictionary<string,object> Page(int month,object[] entries,string uid="800000001"){return new Dictionary<string,object>{{"uid",uid},{"region","os_asia"},{"data_month",month},{"list",entries}};}
 static object Entry(string time,int amount){return new {time=time,num=amount,action_id=4,action="Fixture reward"};}
 static void Reject(Action action,string message){bool threw=false;try{action();}catch{threw=true;}Check(threw,message);}
 internal static void Run(){
  string root=Path.Combine(Path.GetTempPath(),"catheryne-primos-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   AppPreferences.Set("resinUid","800000001",root);AppPreferences.Set("resinServer","os_asia",root);var service=new PrimogemService(root);string account=service.Account();
   Check(!service.Income(account,new DateTime(2026,1,1),new DateTime(2026,1,31)).Amount.HasValue,"missing ledger is unknown");
   var month=new DateTime(2026,1,1);var same=Entry("2026-01-05 10:00:00",5);object[] entries={same,same,Entry("2026-01-06 00:00:00",10)};
   Func<string,string,Dictionary<string,object>> fake=(url,cookie)=>url.Contains("month_detail")?Map(CatheryneTools.Json().Serialize(Page(1,entries))):Map("{\"uid\":800000001,\"region\":\"os_asia\",\"data_month\":1,\"month_data\":{\"current_primogems\":20}}");
   var client=new PrimogemService(root,fake);var snapshot=client.FetchMonth(account,"fixture",month);Check(snapshot.Entries.Count==3&&snapshot.Total==20,"same timestamp and amount retain multiplicity");
   client.StoreMonth(account,snapshot);client.StoreMonth(account,snapshot);var period=client.Income(account,month,month.AddMonths(1).AddDays(-1));Check(period.Amount==20&&period.Months.Count==1,"month replacement never appends income");
   Check(client.Income(account,new DateTime(2026,1,5),new DateTime(2026,1,5)).Amount==10,"inclusive day boundary");
   Check(client.Income("os_euro:800000001",month,month).Amount==null,"server account separation");
   var partial=client.Income(account,month,month.AddMonths(1));Check(partial.Amount==20&&partial.MissingMonths.SequenceEqual(new[]{"2026-02"}),"partial period preserves coverage");
   Reject(()=>client.FetchMonth(account,"fixture",new DateTime(2025,1,1)),"same month in wrong year rejected");
   var mismatch=new PrimogemService(root,(u,c)=>u.Contains("month_detail")?Map(CatheryneTools.Json().Serialize(Page(1,new[]{same}))):fake(u,c));Reject(()=>mismatch.FetchMonth(account,"fixture",month),"incomplete pages cannot overwrite ledger");Check(client.Income(account,month,month.AddDays(30)).Amount==20,"failure leaves prior snapshot");
   var wrong=new PrimogemService(root,(u,c)=>Map("{\"uid\":800000002,\"region\":\"os_asia\",\"data_month\":1}"));Reject(()=>wrong.FetchMonth(account,"fixture",month),"account mismatch rejected");
   int pageCalls=0;var paged=new PrimogemService(root,(u,c)=>{
    if(!u.Contains("month_detail"))return Map("{\"uid\":800000001,\"region\":\"os_asia\",\"data_month\":1,\"month_data\":{\"current_primogems\":500}}");
    pageCalls++;return Map(CatheryneTools.Json().Serialize(Page(1,u.Contains("current_page=1&")?Enumerable.Repeat(same,100).ToArray():new object[0])));
   });Check(paged.FetchMonth(account,"fixture",month).Entries.Count==100&&pageCalls==3,"full page requires terminal empty page and stable head");
   var changing=new PrimogemService(root,(u,c)=>{var data=fake(u,c);if(u.Contains("month_detail")&&++pageCalls%2==0)data=Map(CatheryneTools.Json().Serialize(Page(1,new[]{Entry("2026-01-07 10:00:00",20)})));return data;});pageCalls=0;Reject(()=>changing.FetchMonth(account,"fixture",month),"changed page rejected even with equal total");
   var catalog=AchievementCatalog.Parse("{\"1\":{\"name\":\"Theme\",\"achievements\":[[{\"id\":1,\"name\":\"A\",\"desc\":\"x\",\"reward\":5,\"ver\":\"1.0\"},{\"id\":2,\"name\":\"B\",\"desc\":\"x\"}]]}}");Check(catalog[0].Reward==5&&catalog[0].Version=="1.0"&&catalog[1].Reward==null,"catalog preserves unknown amounts and individual tiers");
   string csv=",Commissions / Free BP,Abyss / Theater / Stygian,Paimon's Shop,Version Events,New Permanent Content,Apologems / Web Events / Mail,Other\n7.0,1,2,3,4,5,6,\"quoted, ignored\"\n7.1 est.,1,1,1,1,1,1,\n";var budgets=PrimogemService.ParseBudgets(csv);Check(budgets.Count==2&&budgets[0].Pulls==21&&budgets[1].Estimate,"budget units and estimate flag preserved");Reject(()=>PrimogemService.ParseBudgets(csv.Replace("New Permanent Content","Changed")),"changed public schema rejected");
   string detail="A,B,Primogems,Acquaint,Intertwined\nEvents,Event,420,0,10\nOther New Content,Quest,60,0,0\n\"Web, Mail, Apologems\",Mail,600,0,0\nRepeating Content,Daily Resin/Commissions x42,2520,0,0\n,Abyss / Imaginarium / Stygian,1200,0,0\n,Battle Pass - F2P,60,5,0\n,Battle Pass - Paid Bonus,680,0,4\n,Welkin 42 Days,4200,0,0\n,Total F2P,4860,5,10\n,Total F2P,30.375,5,10";
   var detailed=PrimogemService.ParseDetailedBudget(detail,"7.1",true);Check(detailed.Primogems==4860&&detailed.Days==42&&detailed.Categories["events"]==420&&detailed.Categories.Count==6,"raw primogems exclude fates, paid rewards and conversion duplicates");
   Reject(()=>PrimogemService.ParseDetailedBudget(detail.Replace("4860","4900"),"7.1",true),"budget totals reconcile");
   var projected=PrimogemService.Progress(detailed,new PrimoPeriod{Amount=9999,Categories=new Dictionary<string,int>{{"이벤트 보상",500},{"Unrecognized",9499}}});Check(projected.First().Key=="daily"&&projected.Single(x=>x.Key=="other").Total==null&&projected.Single(x=>x.Key=="events").Received==500,"descending denominators and unknown categories");
   Check(PrimogemService.Progress(detailed,new PrimoPeriod{Amount=null,Categories=new Dictionary<string,int>()}).All(x=>!x.Received.HasValue),"missing income never means zero");
   Check(client.IncomeBetween(account,new DateTime(2026,1,5,11,0,0),new DateTime(2026,1,6,0,0,0)).Amount==0,"version timestamp bounds are exclusive");
   Check(client.IncomeBetween(account,new DateTime(2026,1,5,10,0,0),new DateTime(2026,1,6,0,0,0)).Amount==10,"version starts at exact maintenance time");
   var dates=PrimogemService.ParseVersionDates("# [Version 7.1 Update Details](archive/1.md)\nUpdate maintenance begins <t>2026/09/23 06:00</t>\n# Version unrelated\n2026/10/01 10:00");Check(dates.Count==1&&dates[0].Start=="2026-09-23T06:00:00","only official update maintenance dates used");
   var reward=new PrimoReward{Version="7.1",Name="Fixture event",Currency="primogem",Total=420};client.SaveReward(account,reward);Check(client.Rewards(account,"7.1")[0].Received==null,"unknown receipt is not zero");reward.Received=100;client.SaveReward(account,reward);Check(client.Rewards(account,"7.1").Count==1&&client.Rewards(account,"7.1")[0].Received==100,"receipt edit keeps canonical item");Check(client.Rewards(account,"7.0").Count==0,"receipt version separation");reward.Received=421;Reject(()=>client.SaveReward(account,reward),"cannot claim above budget");reward.Received=100;reward.Deleted=true;client.SaveReward(account,reward);Check(client.Rewards(account,"7.1").Count==0,"deleted receipt excluded");
   Reject(()=>client.SaveReward("os_asia:800000002",new PrimoReward{Version="7.1",Name="x",Currency="primogem",Total=1}),"account switch cannot save old editor");
   var report=client.Read("7.1",month,month.AddDays(30));Check(!report.AchievementCatalogAvailable&&report.Income.Amount==20&&report.Budget==null,"independent missing sources never zero-fill or alter income");
   string broken=Path.Combine(root,"catalog","achievements");Directory.CreateDirectory(broken);File.WriteAllText(Path.Combine(broken,"en.json"),"not json");Check(client.Versions().Length>0&&!client.Read("7.1",month,month.AddDays(30)).AchievementCatalogAvailable,"unavailable optional catalog keeps income readable");
   Check(PanelNavigation.Group("Primogems")=="내 계정","natural account navigation owner");
  }finally{Directory.Delete(root,true);}
 }
}
