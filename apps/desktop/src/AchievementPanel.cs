using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

internal sealed class AchievementRow {
 public int Id,Category,State,Tier,Tiers; public int? Reward; public string Version,Name,English,Description,Theme;
 public string Label {get{return (State==2?"✓ ":State==1?"◌ ":"○ ")+Name+(Tiers>1?"  "+Tier+"/"+Tiers:"");}}
 public string Status {get{return Locale.T(State==2?"확인 완료":State==1?"검증 대기":"미확인");}}
 public string StatusLabel {get{return (State==2?"✓  ":State==1?"◌  ":"○  ")+Status;}}
 public Brush StatusForeground {get{return new SolidColorBrush(State==2?Color.FromRgb(141,216,191):State==1?Color.FromRgb(245,193,109):Color.FromRgb(180,189,203));}}
 public Brush StatusBackground {get{return new SolidColorBrush(State==2?Color.FromRgb(39,69,60):State==1?Color.FromRgb(73,59,38):Color.FromRgb(48,55,65));}}
 public string DisplayName {get{return Name+(Tiers>1?"  "+Tier+"/"+Tiers:"");}}
 public string StateContext {get{return Locale.T(State==2?"개별 업적 기록에서 달성이 확인되었습니다.":State==1?"달성 기록의 검증을 기다리고 있습니다.":"개별 달성 기록이 없습니다. 미달성을 뜻하지 않습니다.");}}
}
internal static class AchievementCatalog {
 const string Source="https://raw.githubusercontent.com/MadeBaruna/paimon-moe/main/src/data/achievement/";
 internal static List<AchievementRow> Parse(string text){
  var data=new JavaScriptSerializer{MaxJsonLength=16000000}.Deserialize<Dictionary<string,object>>(text);var rows=new List<AchievementRow>();
  foreach(var pair in data){var cat=(Dictionary<string,object>)pair.Value;int category=int.Parse(pair.Key);
   foreach(var item in (IEnumerable)cat["achievements"]){var one=item as Dictionary<string,object>;var group=one!=null?new[]{one}:((IEnumerable)item).Cast<Dictionary<string,object>>();
    var tiers=group.ToList();int tier=0;foreach(var entry in tiers)rows.Add(new AchievementRow{Reward=entry.ContainsKey("reward")&&entry["reward"]!=null?(int?)Convert.ToInt32(entry["reward"]):null,Version=entry.ContainsKey("ver")?Convert.ToString(entry["ver"]):null,Tier=++tier,Tiers=tiers.Count,Id=Convert.ToInt32(entry["id"]),Category=category,Name=Convert.ToString(entry["name"]),English=Convert.ToString(entry["name"]),Description=Convert.ToString(entry["desc"]),Theme=Convert.ToString(cat["name"])});
   }
  }
  if(rows.Count==0||rows.Any(x=>x.Id<=0)||rows.Select(x=>x.Id).Distinct().Count()!=rows.Count)throw new InvalidDataException("Invalid achievement catalog");return rows;
 }
 internal static string GuideUrl(AchievementRow row){return "https://genshin-impact.fandom.com/wiki/Special:Search?query="+Uri.EscapeDataString(row.English);}
 internal static void ExportContext(string root,int id,string destination){
  var row=Load(root,false).SingleOrDefault(x=>x.Id==id);if(row==null)throw new InvalidDataException("Unknown achievement ID");
  int state;new ProfileStore(root).AchievementStates().TryGetValue(id,out state);
  AtomicFile.Write(destination,new JavaScriptSerializer().Serialize(new {id=row.Id,name=row.Name,englishName=row.English,category=row.Category,theme=row.Theme,tier=row.Tier,tiers=row.Tiers,condition=row.Description,status=state==2?"verified":state==1?"unverified":"unknown",catalogSource=Source+"en.json",guideLookup=new {kind="search",url=GuideUrl(row)},guideVerified=false}));
 }
 internal static List<AchievementRow> Merge(string english,string korean){
  var rows=Parse(english);var translated=Parse(korean).ToDictionary(x=>x.Id);
  foreach(var row in rows){AchievementRow local;if(translated.TryGetValue(row.Id,out local)){if(row.Category!=local.Category||row.Reward!=local.Reward||row.Tier!=local.Tier||row.Tiers!=local.Tiers)throw new InvalidDataException("Achievement locale identity mismatch");if(!Locale.IsEnglish){row.Name=local.Name;row.Description=local.Description;row.Theme=local.Theme;}}}return rows;
 }
 internal static List<AchievementRow> Load(string root,bool online){
  string folder=Path.Combine(root,"catalog","achievements"),file=Path.Combine(folder,"bundle.json");Directory.CreateDirectory(folder);
  Dictionary<string,object> bundle=null;try{if(File.Exists(file)){bundle=StoryClient.Read(file);Merge(CodexChat.S(bundle,"en"),CodexChat.S(bundle,"ko"));}}catch{bundle=null;}
  using(var mutex=new System.Threading.Mutex(false,GameDataCatalog.Scope("Achievements",root))){bool held=false;try{try{held=mutex.WaitOne(0);}catch(System.Threading.AbandonedMutexException){held=true;}
   if(held&&online){DateTime checkedAt;if(bundle==null||!DateTime.TryParse(CodexChat.S(bundle,"checkedAt"),out checkedAt)||DateTime.UtcNow<checkedAt.ToUniversalTime()||DateTime.UtcNow-checkedAt.ToUniversalTime()>=TimeSpan.FromDays(1)){
    try{var commit=CatheryneTools.Json().Deserialize<Dictionary<string,object>>(PrimogemService.Download("https://api.github.com/repos/MadeBaruna/paimon-moe/commits/HEAD"));string revision=CodexChat.S(commit,"sha");if(!System.Text.RegularExpressions.Regex.IsMatch(revision,"^[a-f0-9]{40}$"))throw new InvalidDataException("Invalid achievement revision");string source=Source.Replace("/main/","/"+revision+"/");string en=PrimogemService.Download(source+"en.json"),ko=PrimogemService.Download(source+"ko.json");var rows=Merge(en,ko);
     if(bundle!=null&&!new HashSet<int>(rows.Select(x=>x.Id)).IsSupersetOf(Parse(CodexChat.S(bundle,"en")).Select(x=>x.Id)))throw new InvalidDataException("Achievement coverage declined");
     bundle=new Dictionary<string,object>{{"schema",1},{"revision",revision},{"en",en},{"ko",ko},{"checkedAt",DateTime.UtcNow.ToString("o")}};AtomicFile.Write(file,CatheryneTools.Json().Serialize(bundle));
    }catch{if(bundle==null&&!File.Exists(Path.Combine(folder,"en.json")))throw;}
   }}
  }finally{if(held)mutex.ReleaseMutex();}}
  if(bundle!=null)return Merge(CodexChat.S(bundle,"en"),CodexChat.S(bundle,"ko"));
  // Preserve the previous cache until a complete pair can be activated.
  var fallback=Parse(File.ReadAllText(Path.Combine(folder,"en.json")));if(Locale.IsEnglish)return fallback;
  try{return Merge(File.ReadAllText(Path.Combine(folder,"en.json")),File.ReadAllText(Path.Combine(folder,"ko.json")));}catch{return fallback;}
 }

}

// Public catalog IDs and official theme aggregates stay separate.
internal sealed class AchievementTheme {
 public int Id {get;set;}
 public string Name {get;set;}
 public string Icon {get;set;}
 public int CatalogCount {get;set;}
 public int Verified {get;set;}
 public int? OfficialFinish {get;set;}
 public double? OfficialPercent {get;set;}
 public bool ShowPercent {get;set;}
 public bool HasIcon {get{return !string.IsNullOrEmpty(Icon);}}
 public string Completion {get{return OfficialFinish.HasValue?(ShowPercent&&OfficialPercent.HasValue?OfficialPercent.Value.ToString("0.#")+"%":Locale.Format("{0}개 달성",OfficialFinish.Value.ToString("N0"))):Verified+" / "+CatalogCount;}}
 public double Percent {get{return OfficialPercent.HasValue?Math.Max(0,Math.Min(100,OfficialPercent.Value)):CatalogCount==0?0:100.0*Verified/CatalogCount;}}
 public Visibility MeterVisibility {get{return ShowPercent?Visibility.Visible:Visibility.Collapsed;}}
 public string Context {get{return OfficialFinish.HasValue?Locale.T("HoYoLAB 달성")+" "+OfficialFinish.Value+"\n"+Locale.T("개별 확인")+" "+Verified+" / "+CatalogCount:Locale.T("개별 확인")+" "+Verified+" / "+CatalogCount;}}
}
internal static class AchievementProgress {
 internal static Dictionary<string,object> Summary(Dictionary<string,object> account){return MaterialInventory.Map(MaterialInventory.Map(account,"catheryne"),"achievementSummary");}
 internal static int? Total(Dictionary<string,object> account){int count;return int.TryParse(CodexChat.S(Summary(account),"achievement_num"),out count)&&count>=0?(int?)count:null;}
 internal static List<AchievementTheme> Themes(Dictionary<string,object> account,IEnumerable<AchievementRow> rows){
  var themes=rows.GroupBy(x=>x.Category).Select(g=>new AchievementTheme{Id=g.Key,Name=g.First().Theme,Icon="",CatalogCount=g.Count(),Verified=g.Count(x=>x.State==2)}).ToList();
  object raw;if(!Summary(account).TryGetValue("list",out raw))return themes;
  var seen=new HashSet<int>();foreach(var entry in CodexChat.Items(raw)){
   int id,finish;double percent;if(!int.TryParse(CodexChat.S(entry,"id"),out id)||id<0||!seen.Add(id)||!int.TryParse(CodexChat.S(entry,"finish_num"),out finish)||finish<0)continue;
   var theme=themes.FirstOrDefault(x=>x.Id==id);if(theme==null){theme=new AchievementTheme{Id=id,Name=CodexChat.S(entry,"name")};themes.Add(theme);}
   if(string.IsNullOrEmpty(theme.Name))theme.Name=CodexChat.S(entry,"name");theme.Icon=CodexChat.S(entry,"icon");theme.OfficialFinish=finish;
   if(double.TryParse(CodexChat.S(entry,"percentage"),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out percent)&&!double.IsNaN(percent)&&!double.IsInfinity(percent)&&percent>=0&&percent<=100)theme.OfficialPercent=percent;
   theme.ShowPercent=CodexChat.S(entry,"show_percent").Equals("True",StringComparison.OrdinalIgnoreCase);
  }return themes;
 }
}
internal sealed class AchievementPanel {
 internal readonly Grid View=new Grid();
 readonly TextBlock summary=new TextBlock{FontSize=26,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center};
 readonly FrameworkElement attention=PanelUi.Attention();
 readonly ListBox themes=ThemeList(),list=AchievementList();
 readonly Grid panes=new Grid(),themePane=new Grid(),listPane=new Grid(),detailPane=new Grid();
 readonly Border themeSurface,listSurface,detailSurface;
 readonly StackPanel overall=new StackPanel{Margin=new Thickness(0,0,0,20)};
 readonly TextBlock listTitle=PaneTitle("업적 목록");
 readonly TextBox search=new TextBox{Height=40};
 readonly ComboBox filter=new ComboBox{Height=40};
 readonly TextBlock selectedTitle=new TextBlock{FontSize=20,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)};
 readonly TextBlock selectedCondition=PanelUi.Text("",true);
 readonly Border selectedState=StateBadge();
 readonly TextBlock selectedReward=PanelUi.Text("",true);
 readonly StackPanel selection=new StackPanel();
 readonly Button back=PanelUi.Button(Locale.T("테마"));
 readonly Window window;readonly string root;List<AchievementRow> rows=new List<AchievementRow>();bool loading,binding,again;int theme=-1,stage;ScrollViewer viewport;internal bool Active;
 internal AchievementPanel(Window window,string root){
  this.window=window;this.root=root;
  View.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});View.RowDefinitions.Add(new RowDefinition());
  var heading=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,20)};heading.Children.Add(summary);heading.Children.Add(attention);
  panes.ColumnDefinitions.Add(new ColumnDefinition());panes.ColumnDefinitions.Add(new ColumnDefinition());panes.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(panes,1);View.Children.Add(panes);
  themeSurface=Pane(themePane);panes.Children.Add(themeSurface);listSurface=Pane(listPane);panes.Children.Add(listSurface);detailSurface=Pane(detailPane);panes.Children.Add(detailSurface);
  themePane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});themePane.RowDefinitions.Add(new RowDefinition());themePane.Children.Add(PaneTitle("테마"));Grid.SetRow(themes,1);themePane.Children.Add(themes);
  listPane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});listPane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});listPane.RowDefinitions.Add(new RowDefinition());
  listPane.Children.Add(listTitle);var toolbar=new Grid{Margin=new Thickness(0,0,0,12)};toolbar.ColumnDefinitions.Add(new ColumnDefinition());toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(146)});
  search.Margin=new Thickness(0,0,8,0);toolbar.Children.Add(PanelUi.SearchInput(search,Locale.T("업적 검색")));filter.Margin=new Thickness(0);Grid.SetColumn(filter,1);filter.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");toolbar.Children.Add(filter);Grid.SetRow(toolbar,1);listPane.Children.Add(toolbar);Grid.SetRow(list,2);listPane.Children.Add(list);
  selection.Children.Add(selectedState);selection.Children.Add(selectedTitle);selection.Children.Add(selectedCondition);selection.Children.Add(selectedReward);var guide=PanelUi.Button(Locale.T("공략 ↗"));guide.Click+=(s,e)=>{var row=list.SelectedItem as AchievementRow;if(row!=null)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AchievementCatalog.GuideUrl(row)){UseShellExecute=true});};selection.Children.Add(guide);
  detailPane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});detailPane.RowDefinitions.Add(new RowDefinition());detailPane.Children.Add(PaneTitle("업적 상세"));var detailScroll=new ScrollViewer{Content=PanelUi.ScrollContent(selection),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(detailScroll,1);detailPane.Children.Add(detailScroll);
  var top=new DockPanel{Margin=new Thickness(0,0,0,20)};back.MinWidth=0;back.Margin=new Thickness(0,0,12,0);back.ToolTip=Locale.T("뒤로가기");DockPanel.SetDock(back,Dock.Left);top.Children.Add(back);heading.Margin=new Thickness(0);top.Children.Add(heading);overall.Children.Add(top);View.Children.Insert(0,overall);
  back.Click+=(s,e)=>{stage=Math.Max(0,stage-1);Layout();};
  foreach(string name in new[]{"전체","미확인","확인 완료","검증 대기"})filter.Items.Add(Locale.T(name));filter.SelectedIndex=0;
  filter.SelectionChanged+=(s,e)=>{if(!binding)RenderList();};search.TextChanged+=(s,e)=>{if(!binding)RenderList();};
  themes.SelectionChanged+=(s,e)=>{if(binding)return;var item=themes.SelectedItem as AchievementTheme;if(item==null)return;theme=item.Id;stage=1;listTitle.Text=item.Name;listTitle.ToolTip=item.Name;RenderList();Layout();};
  list.SelectionChanged+=(s,e)=>OpenAchievement(list.SelectedItem as AchievementRow);
  themes.PreviewMouseLeftButtonUp+=(s,e)=>{if(stage==0&&ItemHit(e.OriginalSource)){var item=themes.SelectedItem as AchievementTheme;if(item!=null){theme=item.Id;stage=1;listTitle.Text=item.Name;listTitle.ToolTip=item.Name;RenderList();Layout();}}};
  list.PreviewMouseLeftButtonUp+=(s,e)=>{if(stage==1&&ItemHit(e.OriginalSource))OpenAchievement(list.SelectedItem as AchievementRow);};
  themes.PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter&&themes.SelectedItem!=null){stage=1;Layout();e.Handled=true;}};
  list.PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter){OpenAchievement(list.SelectedItem as AchievementRow);e.Handled=true;}};

  View.SizeChanged+=(s,e)=>{Fit();Layout();};View.Loaded+=(s,e)=>{DependencyObject node=View;while((node=VisualTreeHelper.GetParent(node))!=null){viewport=node as ScrollViewer;if(viewport!=null)break;}if(viewport!=null)viewport.SizeChanged+=ViewportChanged;Fit();Layout();};View.Unloaded+=(s,e)=>{if(viewport!=null)viewport.SizeChanged-=ViewportChanged;viewport=null;};
  selection.Visibility=Visibility.Collapsed;Layout();
 }
 void OpenAchievement(AchievementRow row){selection.Visibility=row==null?Visibility.Collapsed:Visibility.Visible;if(row==null)return;selectedTitle.Text=row.DisplayName;selectedCondition.Text=row.Description;selectedState.DataContext=row;selectedReward.Text=row.Reward.HasValue?Locale.T("원석")+" "+row.Reward.Value:"";selectedReward.Visibility=row.Reward.HasValue?Visibility.Visible:Visibility.Collapsed;stage=2;Layout();}
 static bool ItemHit(object source){var node=source as DependencyObject;while(node!=null){if(node is ListBoxItem)return true;if(!(node is Visual))return false;node=VisualTreeHelper.GetParent(node);}return false;}
 void ViewportChanged(object sender,SizeChangedEventArgs e){Fit();}
 void Fit(){if(viewport==null)return;double height=Math.Max(240,viewport.ActualHeight);if(Math.Abs(View.Height-height)>.5||double.IsNaN(View.Height))View.Height=height;}
 void Layout(){
  bool wide=View.ActualWidth>=780;back.Visibility=wide||stage==0?Visibility.Collapsed:Visibility.Visible;back.Content=Locale.T(stage==2?"업적 목록":"테마");
  themeSurface.Visibility=wide||stage==0?Visibility.Visible:Visibility.Collapsed;listSurface.Visibility=wide||stage==1?Visibility.Visible:Visibility.Collapsed;detailSurface.Visibility=wide||stage==2?Visibility.Visible:Visibility.Collapsed;
  panes.ColumnDefinitions[0].Width=new GridLength(1,GridUnitType.Star);panes.ColumnDefinitions[1].Width=wide?new GridLength(1,GridUnitType.Star):new GridLength(0);panes.ColumnDefinitions[2].Width=wide?new GridLength(1,GridUnitType.Star):new GridLength(0);
  Grid.SetColumn(themeSurface,0);Grid.SetColumn(listSurface,wide?1:0);Grid.SetColumn(detailSurface,wide?2:0);themeSurface.Margin=wide?new Thickness(0,0,8,0):new Thickness(0);listSurface.Margin=wide?new Thickness(4,0,4,0):new Thickness(0);detailSurface.Margin=wide?new Thickness(8,0,0,0):new Thickness(0);
 }
 static Border Pane(Grid content){return new Border{Background=new SolidColorBrush(Color.FromRgb(38,43,51)),BorderBrush=new SolidColorBrush(Color.FromRgb(57,65,75)),BorderThickness=new Thickness(1),CornerRadius=PanelUi.Corners,Padding=new Thickness(14),Child=content};}
 static TextBlock PaneTitle(string title){return new TextBlock{Text=Locale.T(title),FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,0,0,16)};}
 static FrameworkElementFactory BadgeTemplate(){var badge=new FrameworkElementFactory(typeof(Border));badge.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));badge.SetValue(Border.PaddingProperty,new Thickness(9,5,9,5));badge.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left);badge.SetBinding(Border.BackgroundProperty,new Binding("StatusBackground"));badge.SetBinding(FrameworkElement.ToolTipProperty,new Binding("StateContext"));var text=BoundText("StatusLabel",12,null);text.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);text.SetBinding(TextBlock.ForegroundProperty,new Binding("StatusForeground"));badge.AppendChild(text);return badge;}
 static Border StateBadge(){var presenter=new ContentControl{ContentTemplate=new DataTemplate{VisualTree=BadgeTemplate()}};presenter.SetBinding(ContentControl.ContentProperty,new Binding());return new Border{Child=presenter,Margin=new Thickness(0,0,0,16)};}
 static ListBox BaseList(){var control=PanelUi.CardList();control.Foreground=Brushes.White;ScrollViewer.SetHorizontalScrollBarVisibility(control,ScrollBarVisibility.Disabled);ScrollViewer.SetVerticalScrollBarVisibility(control,ScrollBarVisibility.Auto);VirtualizingStackPanel.SetVirtualizationMode(control,VirtualizationMode.Recycling);var style=new Style(typeof(ListBoxItem));style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,0,4,8)));style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(33,38,46))));style.Setters.Add(new Setter(Control.BorderBrushProperty,Brushes.Transparent));style.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'><Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8' Padding='12'><ContentPresenter HorizontalAlignment='Stretch'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#303742'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='Surface' Property='Background' Value='#363E49'/><Setter TargetName='Surface' Property='BorderBrush' Value='#BEA979'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#D1BD8D'/></Trigger></ControlTemplate.Triggers></ControlTemplate>")));control.ItemContainerStyle=style;return control;}
 static ListBox ThemeList(){
  var control=BaseList();var dock=new FrameworkElementFactory(typeof(DockPanel));dock.SetValue(FrameworkElement.MinHeightProperty,52.0);
  var image=new FrameworkElementFactory(typeof(Image));image.SetValue(FrameworkElement.WidthProperty,28.0);image.SetValue(FrameworkElement.HeightProperty,28.0);image.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,10,0));image.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);image.SetValue(DockPanel.DockProperty,Dock.Left);image.AddHandler(FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,args)=>{var view=(Image)sender;view.DataContextChanged-=ThemeArtworkChanged;view.DataContextChanged+=ThemeArtworkChanged;ThemeArtwork(view);}));dock.AppendChild(image);
  var body=new FrameworkElementFactory(typeof(StackPanel));body.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);var line=new FrameworkElementFactory(typeof(DockPanel));var completion=BoundText("Completion",13,new SolidColorBrush(Color.FromRgb(213,190,139)));completion.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);completion.SetValue(FrameworkElement.MarginProperty,new Thickness(10,0,0,0));completion.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);completion.SetValue(DockPanel.DockProperty,Dock.Right);line.AppendChild(completion);var title=BoundText("Name",14,Brushes.White);title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);title.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);line.AppendChild(title);body.AppendChild(line);
  var meter=new FrameworkElementFactory(typeof(ProgressBar));meter.SetValue(FrameworkElement.HeightProperty,4.0);meter.SetValue(Control.BorderThicknessProperty,new Thickness(0));meter.SetValue(FrameworkElement.MarginProperty,new Thickness(0,8,0,0));meter.SetValue(System.Windows.Controls.Primitives.RangeBase.MaximumProperty,100.0);meter.SetValue(Control.ForegroundProperty,new SolidColorBrush(Color.FromRgb(213,190,139)));meter.SetValue(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(54,60,68)));meter.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty,new Binding("Percent"){Mode=BindingMode.OneWay});meter.SetBinding(UIElement.VisibilityProperty,new Binding("MeterVisibility"));body.AppendChild(meter);dock.SetBinding(FrameworkElement.ToolTipProperty,new Binding("Context"));dock.AppendChild(body);control.ItemTemplate=new DataTemplate{VisualTree=dock};return control;
 }
 static void ThemeArtworkChanged(object sender,DependencyPropertyChangedEventArgs args){ThemeArtwork((Image)sender);}
 static void ThemeArtwork(Image view){view.Source=GameUiIcons.Image("Achievements",true);var theme=view.DataContext as AchievementTheme;if(theme!=null&&theme.HasIcon)GameCatalog.Portrait(view,"achievement-theme:"+theme.Id,64,theme.Icon);else view.Tag=null;}
 static FrameworkElementFactory BoundText(string path,double size,Brush color){var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding(path));text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetValue(TextBlock.FontSizeProperty,size);if(color!=null)text.SetValue(TextBlock.ForegroundProperty,color);return text;}
 static ListBox AchievementList(){var control=BaseList();var body=new FrameworkElementFactory(typeof(StackPanel));var title=BoundText("DisplayName",14,Brushes.White);title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);title.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,0,10));body.AppendChild(title);body.AppendChild(BadgeTemplate());control.ItemTemplate=new DataTemplate{VisualTree=body};return control;}
 internal async void Refresh(){
  if(loading){again=true;return;}loading=true;if(rows.Count==0)summary.Text=Locale.T("불러오는 중…");
  try{
   bool online=window.IsVisible;var loaded=await Task.Run(()=>{var catalog=AchievementCatalog.Load(root,online);var states=new ProfileStore(root).AchievementStates();foreach(var row in catalog){int state;row.State=states.TryGetValue(row.Id,out state)?state:0;}Dictionary<string,object> reference;var account=new CatheryneTools(root).AccountSnapshot(out reference);return Tuple.Create(catalog,account);});
   rows=loaded.Item1;int done=rows.Count(x=>x.State==2);var total=AchievementProgress.Total(loaded.Item2);summary.Text=total.HasValue?Locale.Format("달성 {0}개",total.Value.ToString("N0")):Locale.Format("개별 확인 {0}개",done.ToString("N0"));
   if(overall.Children.Count>1)overall.Children.RemoveAt(1);int completed=total??done;double? percent=rows.Count>0&&completed<=rows.Count?(double?)(100.0*completed/rows.Count):null;var progress=PanelUi.Meter(Locale.Format("전체 목록 {0}개",rows.Count.ToString("N0")),percent.HasValue?percent.Value.ToString("0.#")+"%":null,percent,Color.FromRgb(213,190,139),Locale.T("전체 개수는 공개 업적 목록 기준입니다. HoYoLAB 자료와 목록의 버전이 다를 수 있습니다."));progress.Margin=new Thickness(0);overall.Children.Add(progress);
   string context=(total.HasValue?Locale.T("최근 HoYoLAB 자료와 개별 업적 기록의 개수가 다릅니다."):Locale.T("HoYoLAB의 총 달성 개수가 아직 수집되지 않았습니다."))+"\n"+Locale.T("개별 확인")+" "+done.ToString("N0")+" / "+rows.Count.ToString("N0")+"\n"+Locale.T("HoYoLAB은 총개수와 테마별 달성 현황을 제공합니다. 개별 완료 목록은 업적 수집 또는 파일 불러오기로 확인할 수 있습니다. 미확인은 미달성을 뜻하지 않습니다.");PanelUi.AttentionContext(attention,context);attention.Visibility=!total.HasValue||total.Value!=done?Visibility.Visible:Visibility.Collapsed;
   binding=true;try{var groups=AchievementProgress.Themes(loaded.Item2,rows);themes.ItemsSource=groups;themes.SelectedItem=groups.FirstOrDefault(x=>x.Id==theme)??groups.FirstOrDefault();var selected=themes.SelectedItem as AchievementTheme;if(selected!=null){theme=selected.Id;listTitle.Text=selected.Name;listTitle.ToolTip=selected.Name;}}finally{binding=false;}RenderList();Fit();Layout();
  }catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.CollectionFailure,error,root);PanelUi.AttentionContext(attention,Locale.T("업적 목록을 불러오지 못했습니다."));attention.Visibility=Visibility.Visible;}finally{loading=false;if(again){again=false;Refresh();}}
 }
 internal static List<AchievementRow> Scope(IEnumerable<AchievementRow> rows,int theme,string query){return rows.Where(x=>(theme<0||x.Category==theme)&&(query.Length==0||(x.Name+" "+x.English+" "+x.Description+" "+x.Id).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).ToList();}
 void RenderList(){
  if(binding)return;int priorStage=stage,chosen=Math.Max(0,filter.SelectedIndex);var previous=list.SelectedItem as AchievementRow;var scope=Scope(rows,theme,search.Text.Trim());
  binding=true;try{filter.Items.Clear();filter.Items.Add(Locale.T("전체")+"  "+scope.Count);filter.Items.Add(Locale.T("미확인")+"  "+scope.Count(x=>x.State==0));filter.Items.Add(Locale.T("확인 완료")+"  "+scope.Count(x=>x.State==2));filter.Items.Add(Locale.T("검증 대기")+"  "+scope.Count(x=>x.State==1));filter.SelectedIndex=chosen;}finally{binding=false;}
  var found=scope.Where(x=>chosen==0||x.State==(chosen==1?0:chosen==2?2:1)).ToList();list.ItemsSource=found;if(previous!=null)list.SelectedItem=found.FirstOrDefault(x=>x.Id==previous.Id);stage=list.SelectedItem==null?Math.Min(priorStage,1):priorStage;Layout();
 }
}
