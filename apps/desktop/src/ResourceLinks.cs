using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Web.Script.Serialization;
using System.Threading.Tasks;

internal static class ResourceLinks {
 static readonly string[] Groups={"sites","tools","references","mods","development"};
 static List<Dictionary<string,object>> Read(){
  var links=new List<Dictionary<string,object>>();var seen=new HashSet<string>(StringComparer.Ordinal);
  Action<string,string,string,string> add=(group,name,url,icon)=>{
   Uri uri;if(!Groups.Contains(group)||string.IsNullOrWhiteSpace(name)||!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.UserInfo.Length!=0)throw new InvalidDataException("Invalid resource link");
   if(seen.Add(uri.AbsoluteUri.TrimEnd('/')))links.Add(new Dictionary<string,object>{{"group",group},{"name",name},{"url",uri.AbsoluteUri},{"icon",icon??"\uE774"}});
  };
  string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"resources.json");
  var entries=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));object project;
  if(entries.TryGetValue("projectUrl",out project)&&!string.IsNullOrWhiteSpace(Convert.ToString(project)))add("development","Catheryne · GitHub",Convert.ToString(project),"\uE943");
  foreach(Dictionary<string,object> item in (IEnumerable)entries["links"]){object glyph;add(Convert.ToString(item["group"]),Convert.ToString(item["name"]),Convert.ToString(item["url"]),item.TryGetValue("icon",out glyph)?Convert.ToString(glyph):null);}
  foreach(var component in Components.Catalog()){
   var tool=ExternalTools.Catalog.FirstOrDefault(x=>x.Id==component.Id);
   string name=tool!=null?tool.Title:component.Id=="xxmi"?"XXMI Launcher":component.Id=="kamera"?"Inventory Kamera":component.Id=="scanner"?"Akasha Scanner":component.Id=="ai"?"Model Context Protocol · Python SDK":component.Id=="hoyolab"?"Microsoft Edge WebView2":component.Title;
   add(component.Id=="ai"||component.Id=="hoyolab"?"development":"tools",name,component.Website,null);
  }
  add("references",Locale.T("캐릭터 육성 기준표"),BuildReferences.Workbook,null);
  add("references",Locale.T("KQM 성유물 평가 기준"),BuildAnalysis.ScoreGuide,null);
  add("references",Locale.T("Miao · 자동 육성 기준"),BuildCriteriaCatalog.Source,null);
  add("references",Locale.T("원석 수입 예상표"),PrimogemService.BudgetUrl,null);
  add("references",Locale.T("Lightkeepers · 나선·지맥 통계"),EndgameKnowledge.Source,null);
  add("references",Locale.T("환상극 조합 통계"),TheaterStatistics.Url,null);
  add("sites",Locale.T("리딤 코드 교환"),RedemptionService.Gift,null);
  var combatNames=new Dictionary<string,string>{{"teams",Locale.T("KQM · 팀 구성")},{"reactions",Locale.T("KQM · 격변 반응")},{"amplifying",Locale.T("KQM · 증폭 반응")},{"additive",Locale.T("KQM · 촉진·발산")},{"gauge",Locale.T("KQM · 원소 부착량")},{"damage",Locale.T("KQM · 피해 계산")},{"lunar",Locale.T("KQM · 달 반응")},{"stellar","KQM · Stellar"},{"guides","KQM"},{"statistics",Locale.T("환상극 조합 통계")}};
  foreach(var source in TheaterCombatReference.Sources)add("references",combatNames[source[0]],source[1],null);
  foreach(string repository in new[]{ModRuntimePackages.SharedRepository,ModRuntimePackages.UnlockerRepository,ModRuntimePackages.GimiRepository})add("mods",repository.Split('/')[1],"https://github.com/"+repository,null);
  foreach(var mod in ModBuiltins.Catalog){
   add("mods",Locale.T(mod.Name),mod.Source,null);
   if(mod.Packages!=null)foreach(var package in mod.Packages)add("mods",package.Folder,package.Source,null);
  }
  return links;
 }
 static StackPanel Rows(Window window,IEnumerable<Dictionary<string,object>> links){
  var rows=new StackPanel();foreach(var item in links){string url=Convert.ToString(item["url"]),name=Locale.T(Convert.ToString(item["name"]));
   var button=PanelUi.NavigationRow(name,Convert.ToString(item["icon"]),()=>{try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception error){MessageBox.Show(window,error.Message,"Catheryne");}});
   button.ToolTip=name+"\n"+new Uri(url).Host;System.Windows.Automation.AutomationProperties.SetName(button,name);rows.Children.Add(button);
  }return rows;
 }
 internal static async void Show(Window window,DetailPanel detail){
  var body=new StackPanel();detail.Show("ExternalLinks",Locale.T("외부 링크"),body);
  try{
   var links=await Task.Run(()=>Read());if(!detail.IsShowing("ExternalLinks"))return;
   string[] names={Locale.T("사이트"),Locale.T("실행 도구"),Locale.T("공략과 자료"),Locale.T("모드 배포처"),Locale.T("개발 자료")};
   var search=new TextBox();body.Children.Add(PanelUi.SearchInput(search,Locale.T("링크 검색")));var content=new StackPanel();body.Children.Add(content);
   var expanded=new HashSet<string>(Groups.Take(2));
   Action render=()=>{
    content.Children.Clear();string query=search.Text.Trim();
    for(int i=0;i<Groups.Length;i++){
     string key=Groups[i];var items=links.Where(x=>Convert.ToString(x["group"])==key&&(query.Length==0||(Locale.T(Convert.ToString(x["name"]))+" "+Convert.ToString(x["url"])+" "+names[i]).IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0)).ToArray();if(items.Length==0)continue;
     var group=PanelUi.Details(names[i],()=>Rows(window,items));content.Children.Add(group);group.IsExpanded=query.Length>0||expanded.Contains(key);
     group.Expanded+=(s,e)=>{if(e.Source==group&&search.Text.Trim().Length==0)expanded.Add(key);};group.Collapsed+=(s,e)=>{if(e.Source==group&&search.Text.Trim().Length==0)expanded.Remove(key);};
    }
    if(content.Children.Count==0)content.Children.Add(PanelUi.Text(Locale.T("검색 결과가 없습니다."),true));
   };search.TextChanged+=(s,e)=>render();render();
  }catch(Exception error){if(detail.IsShowing("ExternalLinks"))body.Children.Add(PanelUi.Text(error.Message,true));}
 }
}
