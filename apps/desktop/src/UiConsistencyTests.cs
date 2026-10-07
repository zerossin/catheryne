using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

internal static class UiConsistencyTests {
 static void Check(bool value,string message){if(!value)throw new Exception("UI consistency: "+message);}
 static Dictionary<string,object> Item(string json){return CatheryneTools.Json().Deserialize<Dictionary<string,object>>(json);}
 // A translated label is not a stable long-label fixture: "Include again" fits
 // a 106 DIP cell in Segoe UI, but not in Malgun Gothic. Keep the long-label
 // contract independent of OS message-font defaults, then check real labels too.
 internal static void Actions(Window window){
  foreach(string font in new[]{"Segoe UI","Malgun Gothic","Arial"})foreach(bool localized in new[]{false,true}){
   var shortAction=PanelUi.Button(localized?Locale.T("우선 처리"):"Prioritize");
   var longAction=PanelUi.Button(localized?Locale.T("다시 계획에 포함"):"Include this task in the plan again");
   var actionRow=PanelUi.Actions(shortAction,longAction);actionRow.Resources=window.Resources;
   TextElement.SetFontFamily(actionRow,new FontFamily(font));
   actionRow.Language=window.Language;
   foreach(double width in new[]{220.0,600.0,220.0}){
    actionRow.Measure(new Size(width,double.PositiveInfinity));actionRow.Arrange(new Rect(0,0,width,actionRow.DesiredSize.Height));actionRow.UpdateLayout();
    var first=shortAction.TranslatePoint(new Point(),actionRow);var second=longAction.TranslatePoint(new Point(),actionRow);
    bool stacked=second.Y>first.Y;
    string context=Locale.LanguageCode+" / "+font+" / "+width+" / "+longAction.Content+" (width="+longAction.ActualWidth+", y="+first.Y+"/"+second.Y+")";
    if(!localized&&width==220)Check(stacked&&longAction.ActualWidth>=180,"Narrow actions must retain readable labels on separate rows: "+context);
    if(width==600)Check(Math.Abs(first.Y-second.Y)<=0.1,"Wide actions must share a row: "+context);
    Check(Math.Abs(shortAction.ActualWidth-longAction.ActualWidth)<=0.1,"actions have equal widths: "+context);
    Check(stacked?second.Y-first.Y-shortAction.ActualHeight>=8:second.X-first.X-shortAction.ActualWidth>=8,"action spacing: "+context);
    foreach(var action in new[]{shortAction,longAction}){
     Check(action.TranslatePoint(new Point(),actionRow).X+action.ActualWidth<=width+0.1,"action fits its row: "+context);
     var label=new TextBlock{Text=(string)action.Content,FontFamily=action.FontFamily,FontSize=action.FontSize,FontWeight=action.FontWeight,FontStyle=action.FontStyle,FontStretch=action.FontStretch,Language=action.Language,TextWrapping=TextWrapping.Wrap};
     label.Measure(new Size(action.ActualWidth-action.Padding.Left-action.Padding.Right-action.BorderThickness.Left-action.BorderThickness.Right,double.PositiveInfinity));
     Check(label.DesiredSize.Height<=action.ActualHeight-action.Padding.Top-action.Padding.Bottom-action.BorderThickness.Top-action.BorderThickness.Bottom+0.1,"action label fits without clipping: "+context);
    }
   }
  }
 }
 internal static void Run(){
  foreach(string element in new[]{"Anemo","Hydro","Pyro","Electro","Cryo","Dendro","Geo"}){var traveler=new Dictionary<string,object>{{"key","Traveler"+element},{"element",element}};Check(AccountIdentity.CharacterField(traveler,"element")==GameCatalog.ElementName(element),"traveler fallback shares localized element: "+element);}Check(AccountIdentity.CharacterField(Item("{\"key\":\"TravelerCryo\",\"element\":\"Cryo\"}"),"element")==AccountIdentity.CharacterField(Item("{\"key\":\"Kaeya\"}"),"element"),"traveler and catalogued Cryo character share one filter value");
  var rows=new[]{new AchievementRow{Id=1,Category=7,State=2,Name="첫 번째",English="First",Description="조건"},new AchievementRow{Id=2,Category=7,State=0,Name="두 번째",English="Second",Description="조건"},new AchievementRow{Id=3,Category=8,State=2,Name="세 번째",English="Third",Description="별도 조건"}};
  var scope=AchievementPanel.Scope(rows,7,"");Check(scope.Count==2&&scope.Count(x=>x.State==2)==1,"theme counts exclude other themes");Check(AchievementPanel.Scope(rows,7,"Second").Single().Id==2,"search and theme share one count scope");Check(AchievementPanel.Scope(rows,7,"Third").Count==0,"search cannot cross theme boundaries");
  var weapon=Item("{\"key\":\"FavoniusWarbow\",\"level\":90,\"ascension\":6,\"refinement\":5}");var stats=GameCatalog.WeaponDetails(weapon);Check(CodexChat.S(stats,"attack")=="454"&&CodexChat.S(stats,"statValue").Replace(',','.')=="61.3%","GOOD weapon uses actual level in public curves");Check(CodexChat.S(stats,"effect").Contains("100%"),"R5 passive is selected");weapon["refinement"]=1;Check(CodexChat.S(GameCatalog.WeaponDetails(weapon),"effect").Contains("60%"),"R1 does not reuse R5");weapon["level"]=20;weapon["ascension"]=0;Check(CodexChat.S(GameCatalog.WeaponDetails(weapon),"attack")=="99","level 20 before ascension");weapon["ascension"]=1;Check(CodexChat.S(GameCatalog.WeaponDetails(weapon),"attack")=="125","level 20 after ascension");weapon.Remove("ascension");Check(CodexChat.S(GameCatalog.WeaponDetails(weapon),"attack")=="","unknown ascension at a boundary is not guessed");weapon.Remove("refinement");Check(CodexChat.S(GameCatalog.WeaponDetails(weapon),"effect")=="","unknown refinement is not guessed");weapon["key"]="game:15401";weapon["level"]=90;Check(CodexChat.S(GameCatalog.WeaponDetails(weapon),"attack")=="454","numeric game IDs resolve through canonical metadata");
  string main=GameCatalog.ArtifactMainStat(Item("{\"mainStatKey\":\"critDMG_\",\"rarity\":5,\"level\":20}"));Check(main.Replace(',','.').Contains("62.2%"),"artifact main stat uses grade and level");
  var buttons=Enumerable.Range(1,4).Select(i=>{var button=PanelUi.Button(i.ToString());button.Width=96;return button;}).ToArray();var grid=PanelUi.Actions(buttons);grid.Measure(new Size(360,double.PositiveInfinity));Check(((System.Windows.Controls.Primitives.UniformGrid)grid).Columns==2,"four actions use two balanced rows");int created=0;var details=PanelUi.Details("test",()=>{created++;return new TextBlock();});Check(created==0,"collapsed content is lazy");details.IsExpanded=true;details.IsExpanded=false;details.IsExpanded=true;Check(created==1,"expanded content is retained");
  var text=new ChatText();text.Text="**「목표」**를 확인합니다.\n\n`**원문**`";var rendered=new TextRange(text.Document.ContentStart,text.Document.ContentEnd).Text;Check(rendered.Contains("「목표」를")&&!rendered.Contains("**「목표」**"),"Korean punctuation emphasis renders");Check(rendered.Contains("**원문**"),"inline code remains literal");Check(text.Document.Blocks.OfType<Paragraph>().First().Inlines.OfType<Span>().Any(x=>x.FontWeight==FontWeights.SemiBold),"emphasis produces a bold inline");
  string preview="catheryne_endgame의 mode=abyss, command=status로 현재 추천을 확인";Check(WorkspaceHome.HistoryTitle(Item("{\"name\":\"catheryne_endgame의 mode=abyss\"}").Concat(new[]{new KeyValuePair<string,object>("preview",preview)}).ToDictionary(x=>x.Key,x=>x.Value))==Locale.T("나선 비경 · 편성 상담"),"app-generated technical titles have a display projection");Check(WorkspaceHome.HistoryTitle(Item("{\"name\":\"내가 정한 제목\",\"preview\":\"catheryne_endgame의 mode=abyss, command=status로\"}"))=="내가 정한 제목","human titles are preserved");
 }
}
