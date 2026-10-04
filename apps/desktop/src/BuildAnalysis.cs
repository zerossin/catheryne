using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal sealed class ArtifactScore {
 public string Status {get;set;} public double? CritValue {get;set;} public int? RollMinimum {get;set;} public int? RollMaximum {get;set;} public int? UsefulMinimum {get;set;} public int? UsefulMaximum {get;set;} public string[] UsefulStats {get;set;} public int Rarity {get;set;}
}
internal static class BuildAnalysis {
 internal const string ScoreGuide="https://keqingmains.com/misc/artifacts/";
 static Dictionary<string,object> Scores {get{return GameDataCatalog.Read(Setup.DataFolder).Scores;}}
 internal static readonly string[] StatKeys={"hp","hp_","atk","atk_","def","def_","eleMas","enerRech_","critRate_","critDMG_"};
 internal static string[] Useful(string root,string character,Dictionary<string,object> record=null){
  if(record==null){Dictionary<string,object> pointer;var snapshot=new CatheryneTools(root).AccountSnapshot(out pointer);object rows;if(snapshot.TryGetValue("characters",out rows))record=CodexChat.Items(rows).FirstOrDefault(x=>AccountIdentity.Key(x)==character);}
  if(record!=null){var criterion=CharacterBuild.Read(root,record);if(criterion!=null)return criterion.Useful;}
  object value;return AppPreferences.Read(root).TryGetValue("artifactUsefulStats."+character,out value)&&value is IEnumerable?((IEnumerable)value).Cast<object>().Select(Convert.ToString).Where(StatKeys.Contains).Distinct().ToArray():new string[0];
 }

 internal static ArtifactScore Score(Dictionary<string,object> artifact,string[] useful){
  var result=new ArtifactScore{Status="unknown",UsefulStats=useful};object raw;int rarity;if(!int.TryParse(CodexChat.S(artifact,"rarity"),out rarity)||!artifact.TryGetValue("substats",out raw)||raw==null)return result;result.Rarity=rarity;
  var stats=CodexChat.Items(raw).ToArray();if(stats.Length==0||stats.Length>4||stats.Select(x=>CodexChat.S(x,"key")).Distinct().Count()!=stats.Length)return result;
  var table=Scores.ContainsKey("rollValue")?CodexChat.Map(CodexChat.Map(Scores["rollValue"]).ContainsKey(rarity.ToString())?CodexChat.Map(Scores["rollValue"])[rarity.ToString()]:null):new Dictionary<string,object>();
  double cv=0;int minimum=0,maximum=0,usefulMinimum=0,usefulMaximum=0;bool valid=true;
  foreach(var stat in stats){string key=CodexChat.S(stat,"key");double value;if(!StatKeys.Contains(key)||!double.TryParse(CodexChat.S(stat,"value"),NumberStyles.Float,CultureInfo.InvariantCulture,out value)||double.IsNaN(value)||double.IsInfinity(value)||value<0)return result;
   if(key=="critRate_")cv+=value*2;else if(key=="critDMG_")cv+=value;
   object variants;var values=table.ContainsKey(key)?CodexChat.Map(table[key]):new Dictionary<string,object>();string display=value.ToString(key.EndsWith("_")?"0.0":"0",CultureInfo.InvariantCulture);
   if(!values.TryGetValue(display,out variants)){valid=false;continue;}var range=((IEnumerable)variants).Cast<object>().Select(Convert.ToInt32).ToArray();minimum+=range[0];maximum+=range[1];if(useful.Contains(key)){usefulMinimum+=range[0];usefulMaximum+=range[1];}
  }
  int level;if(!int.TryParse(CodexChat.S(artifact,"level"),out level)||level<0||level>rarity*4||minimum>(Math.Min(4,rarity-1)+level/4)*100)valid=false;
  result.CritValue=Math.Round(cv,1);result.Status=valid?"calculated":"unverified_rolls";
  if(valid){result.RollMinimum=minimum;result.RollMaximum=maximum;if(useful.Length>0){result.UsefulMinimum=usefulMinimum;result.UsefulMaximum=usefulMaximum;}}return result;
 }
 internal static Dictionary<string,object> Get(string root,string character,bool refresh=false,bool network=true){
  if(string.IsNullOrWhiteSpace(character))throw new ArgumentException("비교할 캐릭터를 지정해 주세요.");
  if(network)BuildCriteriaCatalog.Load(root,refresh);Dictionary<string,object> reference;var snapshot=new CatheryneTools(root).AccountSnapshot(out reference);object raw;
  var selected=snapshot.TryGetValue("characters",out raw)?CodexChat.Items(raw).SingleOrDefault(x=>AccountIdentity.Key(x)==character):null;if(selected==null)throw new InvalidOperationException("저장된 캐릭터를 찾을 수 없습니다.");
  var weapons=snapshot.TryGetValue("weapons",out raw)&&raw!=null?AccountMerge.Equipped(snapshot,"weapons",character):null;
  bool collected=snapshot.TryGetValue("artifacts",out raw)&&raw!=null;var artifacts=collected?AccountMerge.Equipped(snapshot,"artifacts",character):new Dictionary<string,object>[0];var development=CharacterBuild.Evaluate(root,snapshot,selected);var useful=development.Criterion==null?Useful(root,character,selected):development.Criterion.Useful;var scores=artifacts.Select(x=>Score(x,useful)).ToArray();
  var sources=network?BuildReferences.Load(root,refresh):BuildReferences.Read(root);string korean=AccountIdentity.Name(selected,null,"ko");
  var documents=sources==null?new object[0]:sources.Sheets.SelectMany(sheet=>sheet.Characters.Where(x=>x.Character==korean).Select(block=>(object)new{source=sheet.Name,url=sheet.Url+"&range=A"+block.First+":X"+block.Last,version=sheet.Version,conditions=sheet.Conditions,annotation=block.Annotation,rows=block.Rows.ToArray()})).ToArray();
  return new Dictionary<string,object>{{"character",selected},{"development_score",development},{"equipped_weapons",weapons},{"equipped_artifacts",artifacts},{"snapshot",reference},{"artifacts_collected",collected},{"equipped_artifact_count",artifacts.Length},{"complete_artifact_set",artifacts.Length==5&&artifacts.Select(x=>CodexChat.S(x,"slotKey")).Distinct().Count()==5},{"artifact_scores",scores},{"artifact_score_method",new{cv="2 * CRIT Rate + CRIT DMG, substats only",rv="Sum of 70/80/90/100% rolls using Genshin Optimizer display-value lookup and rounding corrections",source=Scores.ContainsKey("source")?Scores["source"]:null,guide=ScoreGuide,useful_stats=useful,meaning="RV describes rolls, not damage or a universal build grade."}},{"references",documents},{"reference_fetched_utc",sources==null?null:sources.FetchedUtc},{"reference_hash",sources==null?null:sources.Hash},{"reference_error",sources==null?"기준표를 아직 불러오지 않았습니다.":sources.Error},{"comparison_state",selected.ContainsKey("finalStats")&&CodexChat.Map(selected["finalStats"]).Count>0?"conditions_not_verified":"final_stats_not_collected"},{"comparison_note","Use observed finalStats when present. Do not infer a pass/fail against a sheet without matching weapon, constellation, set and team/buff conditions."},{"guide_search","https://keqingmains.com/?s="+Uri.EscapeDataString(AccountIdentity.Name(selected,null,"en"))}};
 }
 internal static FrameworkElement ScoreView(ArtifactScore score){
  var panel=new StackPanel();if(!score.CritValue.HasValue){panel.Children.Add(PanelUi.Text(Locale.T("부옵션 미수집"),true));return panel;}
  panel.Children.Add(PanelUi.Meter(Locale.T("치명타 CV"),score.CritValue.Value.ToString("0.0"),score.Rarity==5?(double?)(score.CritValue.Value/54.4*100):null,Color.FromRgb(113,178,211),Locale.T("부옵션 치명타 확률 × 2 + 치명타 피해. 캐릭터의 전체 성능 점수가 아닙니다.")));
  if(score.RollMinimum.HasValue){double max=score.Rarity==5?900:score.Rarity==4?700:500;panel.Children.Add(PanelUi.Meter(Locale.T("부옵 RV"),Range(score.RollMinimum.Value,score.RollMaximum.Value),score.RollMinimum.Value/max*100,Color.FromRgb(107,182,150),Locale.T("강화 수치의 합입니다. 반올림으로 결과가 하나로 정해지지 않으면 범위를 표시합니다.")));if(score.UsefulStats.Length>0&&score.UsefulMinimum.HasValue)panel.Children.Add(PanelUi.Meter(Locale.T("선택 옵션 RV"),Range(score.UsefulMinimum.Value,score.UsefulMaximum.Value),score.UsefulMinimum.Value/max*100,Color.FromRgb(178,151,213)));}
  else panel.Children.Add(PanelUi.Text(Locale.T("강화 수치 확인 필요"),true));return panel;
 }
 static string Range(int low,int high){return low==high?low+"%":low+"–"+high+"%";}
}

internal sealed class BuildReview {
 sealed class ReviewData {
  internal string Name,Korean,English;internal bool Collected;internal CharacterBuildScore Development;internal string[] Useful;internal Dictionary<string,object>[] Gear;internal ArtifactScore[] Scores;
 }
 readonly string root,key;readonly DetailPanel detail;readonly Action<string> ask;readonly StackPanel body=new StackPanel(),referenceBody=new StackPanel(),scores=new StackPanel();string selectedReference="";readonly TextBlock status=PanelUi.Text("",true);bool closed;int scoreRevision;readonly System.Threading.SemaphoreSlim saveGate=new System.Threading.SemaphoreSlim(1,1);
 internal BuildReview(string root,string key,DetailPanel detail,Action<string> ask){this.root=root;this.key=key;this.detail=detail;this.ask=ask;}
 bool Current {get{return !closed&&body.Parent!=null&&detail.IsShowing("character-build-"+key);}}
 ReviewData ReadScores(Func<Dictionary<string,object>> reader=null){
  Dictionary<string,object> pointer;var snapshot=reader==null?new CatheryneTools(root).AccountSnapshot(out pointer):reader();object raw;var character=snapshot.TryGetValue("characters",out raw)?CodexChat.Items(raw).FirstOrDefault(x=>AccountIdentity.Key(x)==key):null;
  var development=character==null?null:CharacterBuild.Evaluate(root,snapshot,character);var useful=development!=null&&development.Criterion!=null?development.Criterion.Useful:BuildAnalysis.Useful(root,key,character);bool collected=snapshot.TryGetValue("artifacts",out raw)&&raw!=null;var gear=collected?AccountMerge.Equipped(snapshot,"artifacts",key):new Dictionary<string,object>[0];
  return new ReviewData{Name=AccountIdentity.CharacterName(snapshot,key),Korean=AccountIdentity.CharacterName(snapshot,key,null,"ko"),English=AccountIdentity.CharacterName(snapshot,key,null,"en"),Development=development,Useful=useful,Collected=collected,Gear=gear,Scores=gear.Select(x=>BuildAnalysis.Score(x,useful)).ToArray()};
 }
 void Open(string name){detail.Show("character-build-"+key,Locale.Format("{0} 육성 비교",name),body,closed:()=>closed=true,back:()=>PanelNavigation.Open(Window.GetWindow(body),"Characters"));}
 internal async void Show(){await ShowAsync();}
 // The opening snapshot is read once on a worker; reference filtering never reads account files.
 internal async Task ShowAsync(Func<Dictionary<string,object>> snapshotReader=null,Func<BuildReferenceData> referenceReader=null){
  body.Children.Add(scores);body.Children.Add(status);body.Children.Add(referenceBody);status.Text=Locale.T("기준표 불러오는 중…");Open(GameCatalog.Name(key));
  try{
   var data=await Task.Run(()=>ReadScores(snapshotReader));if(!Current)return;
   var advice=PanelUi.Button(Locale.T("AI 조언"));advice.Click+=(s,e)=>{if(ask!=null)ask(Locale.Format("{0} (계정 식별자: {1})를 catheryne_query의 build_analysis에서 조회해 줘. {2} 이잘키/올인원 원본 기준, 실제 수치와 성유물 점수, 조건과 갱신 시각을 함께 확인하고 KQM 공략도 검토해서 개선 순서를 알려줘. 미수집 스탯이나 조건 불일치는 추측하지 마.",data.Name,key,selectedReference));};
   var guide=Link(Locale.T("KQM 공략"),"https://keqingmains.com/?s="+Uri.EscapeDataString(data.English));body.Children.Insert(0,PanelUi.Actions(advice,guide));RenderScores(data);
   var options=new WrapPanel();var selected=new HashSet<string>(data.Useful);foreach(string stat in BuildAnalysis.StatKeys){string value=stat;var check=new CheckBox{Content=GameCatalog.Name(stat),IsChecked=selected.Contains(stat),Margin=new Thickness(0,0,16,10)};RoutedEventHandler save=async (s,e)=>{if(check.IsChecked==true)selected.Add(value);else selected.Remove(value);var chosen=selected.ToArray();int revision=++scoreRevision;await saveGate.WaitAsync();try{
    var updated=await Task.Run(()=>{Dictionary<string,object> pointer;var snapshot=new CatheryneTools(root).AccountSnapshot(out pointer);object rows;var character=snapshot.TryGetValue("characters",out rows)?CodexChat.Items(rows).FirstOrDefault(x=>AccountIdentity.Key(x)==key):null;var criterion=character==null?null:CharacterBuild.Read(root,character);if(criterion!=null){criterion.Useful=chosen;CharacterBuild.Save(root,key,criterion);}else AppPreferences.Set("artifactUsefulStats."+key,chosen,root);return ReadScores(()=>snapshot);});if(Current&&revision==scoreRevision)RenderScores(updated);
   }catch(Exception error){Failure(error);}finally{saveGate.Release();}};check.Checked+=save;check.Unchecked+=save;options.Children.Add(check);}body.Children.Insert(2,PanelUi.Details(Locale.T("점수에 포함할 옵션"),options));Open(data.Name);await Refresh(data.Korean,referenceReader);
  }catch(Exception error){Failure(error);}
 }
 void Failure(Exception error){new AppDiagnostics(root).Write(DiagnosticEvent.DataRefreshFailure,error);if(Current)status.Text=error.Message;}
 void RenderScores(ReviewData data){
  scores.Children.Clear();if(data.Development!=null)scores.Children.Add(CharacterBuild.View(data.Development));if(!data.Collected){scores.Children.Add(PanelUi.Text(Locale.T("성유물 미수집"),true));return;}
  if(data.Gear.Length==0){scores.Children.Add(PanelUi.Text(Locale.T("확인된 장착 성유물 없음"),true));return;}var row=new WrapPanel();for(int i=0;i<data.Gear.Length;i++){var card=PanelUi.Section(AccountIdentity.Name(data.Gear[i]),BuildAnalysis.ScoreView(data.Scores[i]));card.Width=230;card.Margin=new Thickness(0,0,12,12);row.Children.Add(card);}scores.Children.Add(row);
 }
 async Task Refresh(string korean,Func<BuildReferenceData> reader){
  var data=await Task.Run(()=>reader==null?BuildReferences.Load(root):reader());if(!Current)return;referenceBody.Children.Clear();status.Text=data.Error??"";var available=data.Sheets.Where(sheet=>sheet.Characters.Any(c=>c.Character==korean)).ToArray();if(available.Length==0){referenceBody.Children.Add(PanelUi.Text(Locale.T("원본에서 이 캐릭터의 기준을 확인하지 못했습니다."),true));foreach(var sheet in data.Sheets)referenceBody.Children.Add(Link(sheet.Name,sheet.Url));return;}
  var select=new ComboBox{ItemsSource=available,DisplayMemberPath="Name",Margin=new Thickness(0,0,0,12)};select.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");referenceBody.Children.Add(select);var content=new StackPanel();referenceBody.Children.Add(content);select.SelectionChanged+=(s,e)=>RenderReference(content,(BuildReferenceSheet)select.SelectedItem,data.FetchedUtc,korean);select.SelectedIndex=0;
 }
 void RenderReference(StackPanel target,BuildReferenceSheet sheet,string fetched,string korean){
  target.Children.Clear();var blocks=sheet.Characters.Where(c=>c.Character==korean).ToArray();var block=blocks[0];var variants=new ComboBox{Margin=new Thickness(0,0,0,12)};variants.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");foreach(var row in blocks.SelectMany(c=>c.Rows)){string weapon=row.Values.Where(x=>x.Key.Contains("무기")).Select(x=>x.Value).FirstOrDefault();variants.Items.Add(new ComboBoxItem{Content=new TextBlock{Text=weapon??Locale.Format("세팅 {0}",variants.Items.Count+1),TextTrimming=TextTrimming.CharacterEllipsis},ToolTip=weapon,Tag=row});}target.Children.Add(variants);var values=new StackPanel();target.Children.Add(values);variants.SelectionChanged+=(s,e)=>{values.Children.Clear();var item=variants.SelectedItem as ComboBoxItem;if(item==null)return;var row=(BuildReferenceRow)item.Tag;selectedReference=Locale.Format("선택한 원본: {0}. ",sheet.Url+"&range=A"+row.Row+":X"+row.Row);foreach(var field in row.Values){var text=PanelUi.Text(field.Value);text.Margin=new Thickness(0,0,0,12);values.Children.Add(new TextBlock{Text=field.Key,Foreground=Brushes.LightGray,FontSize=12,Margin=new Thickness(0,0,0,4)});values.Children.Add(text);}if(row.Notes.Count>0)values.Children.Add(PanelUi.Details(Locale.T("세팅 조건"),PanelUi.Text(string.Join("\n",row.Notes.Select(x=>x.Key+"\n"+x.Value)),true)));};if(variants.Items.Count>0)variants.SelectedIndex=0;
  target.Children.Add(PanelUi.Details(Locale.T("기준과 갱신 정보"),PanelUi.Text(sheet.Version+"\n"+sheet.Conditions+"\n"+Locale.Format("확인: {0}",fetched),true)));target.Children.Add(Link(Locale.T("원본 기준표"),sheet.Url+"&range=A"+block.First+":X"+block.Last));
 }
 static Button Link(string title,string url){var button=PanelUi.Button(title);button.Click+=(s,e)=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});}catch(Exception error){button.ToolTip=error.Message;}};return button;}
}
