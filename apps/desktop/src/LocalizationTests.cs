using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class LocalizationTests {
 static void Check(bool value,string message){if(!value)throw new Exception("Localization: "+message);}
 static IEnumerable<DependencyObject> Nodes(DependencyObject node){yield return node;foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())foreach(var nested in Nodes(child))yield return nested;}
 internal static void Run(){
  bool english=Locale.IsEnglish;
  Check(EndgamePanel.GoalDisplay("12층 9별")==Locale.T("12층 9별"),"saved Abyss preset is projected in the selected language");
  Check(EndgamePanel.GoalDisplay("안정적인 클리어")==Locale.T("안정적인 클리어"),"saved Onslaught preset is projected in the selected language");
  foreach(string preset in new[]{"12층 9별","안정적인 클리어"})Check(EndgamePanel.GoalDisplay(Locale.T(preset,true))==Locale.T(preset),"English presets also return to Korean");
  foreach(string note in new[]{"11층 8별, 천천히", "My own goal", "", "12층 9별 + 내 메모"})Check(EndgamePanel.GoalDisplay(note)==note,"custom goal notes stay verbatim");
  Check(GameCatalog.MaterialName("112138","old cached name")== (english?"Aberrant Core of the Deep Shadow":"어둠의 핵"),"cached material names follow catalog language");
  Check(GameCatalog.MaterialName("999999999","custom fallback")=="custom fallback","unknown material names remain available");
  var account=new Dictionary<string,object>{{"catheryne",new Dictionary<string,object>{{"achievementSummary",new Dictionary<string,object>{{"list",new[]{new Dictionary<string,object>{{"id",7},{"name","저장된 한국어 테마"},{"finish_num",3}}}}}}}}};
  Check(AchievementProgress.Themes(account,new[]{new AchievementRow{Category=7,Theme="Catalog theme",State=2}}).Single().Name=="Catalog theme","official cached names cannot overwrite the selected catalog language");
  foreach(string key in new[]{"부위","치명타 CV","부옵 RV","선택 옵션 RV","잠김","해제","계정 자료","업적 자료","일반 공격","원소전투 스킬","원소폭발","확인 중…"})Check(!Regex.IsMatch(Locale.T(key,true),"[가-힣]"),"English label: "+key);
  var ui=new StackPanel{Margin=new Thickness(24)};var filters=new WrapPanel();ui.Children.Add(filters);
  foreach(string field in new[]{"element","weapon","rarity","location","setKey","slotKey","mainStatKey","level","refinement","review"}){string label=InventoryPanel.AllFilter(field);Check(!english||!Regex.IsMatch(label,"[가-힣]"),"English filter: "+field);var combo=new ComboBox{MinWidth=140,Margin=new Thickness(0,0,8,8)};combo.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");combo.Items.Add(label);combo.SelectedIndex=0;filters.Children.Add(combo);}
  ui.Children.Add(BuildAnalysis.ScoreView(new ArtifactScore{Rarity=5,CritValue=35.4,RollMinimum=650,RollMaximum=670,UsefulStats=new[]{"critRate_","critDMG_"},UsefulMinimum=480,UsefulMaximum=500}));
  ui.Children.Add(CalendarPanel.EntryCard(new CalendarEntry{Title="Fixture event",Source="manual",Due=DateTime.UtcNow.AddHours(5),Observed=DateTime.UtcNow}));
  if(english)foreach(var node in Nodes(ui)){var text=node as TextBlock;if(text!=null)Check(!Regex.IsMatch(text.Text??"","[가-힣]"),"rendered English text: "+text.Text);var element=node as FrameworkElement;if(element!=null&&element.ToolTip is string)Check(!Regex.IsMatch((string)element.ToolTip,"[가-힣]"),"rendered English tooltip");}
  var shell=(Window)System.Windows.Markup.XamlReader.Parse(Locale.Xaml(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Main.xaml"))));
  if(english)foreach(string name in new[]{"Home","Graphics","Story","Characters","Achievements","Settings"})Check(!Regex.IsMatch(System.Windows.Automation.AutomationProperties.GetName((DependencyObject)shell.FindName(name)),"[가-힣]"),"English navigation accessibility name: "+name);
  ui.Margin=new Thickness(0);var surface=new Border{Child=ui,Padding=new Thickness(24),Background=new SolidColorBrush(Color.FromRgb(32,37,43)),Resources=shell.Resources};System.Windows.Documents.TextElement.SetForeground(surface,Brushes.White);
  foreach(int width in new[]{780,1240}){surface.Measure(new Size(width,double.PositiveInfinity));surface.Arrange(new Rect(0,0,width,surface.DesiredSize.Height));surface.UpdateLayout();Check(surface.DesiredSize.Width<=width,"localized surface fits width "+width);var image=new RenderTargetBitmap(width,(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(Path.GetTempPath(),"catheryne-localization-"+Locale.LanguageCode+"-"+width+".png")))png.Save(output);}
 }
}
