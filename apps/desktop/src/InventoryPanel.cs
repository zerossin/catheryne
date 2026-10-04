using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal sealed class InventoryPanel {
 internal readonly Grid View=new Grid();
 internal Action<string> Plan;
 internal Action<string> Ask;
 internal Action<string> Review;
 readonly WrapPanel filters=new WrapPanel{Margin=new Thickness(0,0,0,12)};
 readonly Dictionary<string,ComboBox> selectors=new Dictionary<string,ComboBox>();
 bool bindingFilters;
 Dictionary<string,ArtifactDecision> artifactReviews=new Dictionary<string,ArtifactDecision>();
 readonly WrapPanel cards=new WrapPanel();
 readonly StackPanel details=new StackPanel();
 readonly TextBox search=new TextBox{MinHeight=36,Margin=new Thickness(0,0,0,16)};
 readonly TextBlock count=PanelUi.Text("",true);
 readonly Button previous=PanelUi.Button(Locale.T("이전")),next=PanelUi.Button(Locale.T("다음"));
 readonly Grid inventory=new Grid{VerticalAlignment=VerticalAlignment.Top};
 readonly Border detailFrame;readonly Grid listFrame=new Grid();readonly ScrollViewer detailScroll;Action arrange;double detailWidth=-1;
 Dictionary<string,object> snapshot=new Dictionary<string,object>();
 Dictionary<string,object> aliases=new Dictionary<string,object>();
 List<Dictionary<string,object>> filtered=new List<Dictionary<string,object>>();
 sealed class BrowseState {internal string Search="",Selected="";internal int Offset;internal Dictionary<string,string> Filters=new Dictionary<string,string>();}
 readonly Dictionary<string,BrowseState> states=new Dictionary<string,BrowseState>();BrowseState browse;bool restoring;string selectedIdentity="";
 Dictionary<Dictionary<string,object>,string> identities=new Dictionary<Dictionary<string,object>,string>();
 string Identity(Dictionary<string,object> item){string value;if(!identities.TryGetValue(item,out value)){value=CatheryneTools.Json().Serialize(item);identities[item]=value;}return value;}
 void SaveBrowse(){if(browse==null||restoring)return;browse.Search=search.Text;browse.Offset=offset;browse.Selected=selectedIdentity;browse.Filters=selectors.ToDictionary(x=>x.Key,x=>Convert.ToString(x.Value.SelectedItem));}
 readonly System.Windows.Threading.DispatcherTimer updates=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};DateTime accountRevision,criteriaRevision,catalogRevision;
 DateTime AccountRevision(){string path=System.IO.Path.Combine(Setup.DataFolder,"profiles","default","account","current.json");return System.IO.File.GetLastWriteTimeUtc(path);}
 List<Dictionary<string,object>> records=new List<Dictionary<string,object>>();
 string section="characters"; int offset,generation,pageSize=30;
 internal InventoryPanel(){
  details.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,Brushes.White);
  updates.Tick+=(s,e)=>{if(!View.IsVisible||restoring)return;var revision=AccountRevision();if(revision!=accountRevision||criteriaRevision!=BuildCriteriaCatalog.Revision(Setup.DataFolder)||catalogRevision!=GameDataCatalog.Revision(Setup.DataFolder))Show(section);};View.IsVisibleChanged+=(s,e)=>{if(View.IsVisible)updates.Start();else updates.Stop();};View.Unloaded+=(s,e)=>updates.Stop();
  View.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});View.RowDefinitions.Add(new RowDefinition());
  search.ToolTip=Locale.T("이름 검색");System.Windows.Automation.AutomationProperties.SetName(search,Locale.T("이름 검색"));var header=new StackPanel();header.Children.Add(PanelUi.SearchInput(search,Locale.T("이름 검색")));header.Children.Add(filters);View.Children.Add(header);
  foreach(var field in new[]{new[]{"element","원소"},new[]{"weapon","무기 종류"},new[]{"rarity","등급"},new[]{"location","장착 캐릭터"},new[]{"setKey","성유물 종류"},new[]{"slotKey","부위"},new[]{"mainStatKey","주옵션"},new[]{"level","레벨"},new[]{"refinement","재련"},new[]{"review","정리 판단"}}){
   var combo=new ComboBox{MinWidth=110,Margin=new Thickness(0,0,8,0)};combo.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");combo.ToolTip=Locale.T(field[1]);System.Windows.Automation.AutomationProperties.SetName(combo,Locale.T(field[1]));selectors.Add(field[0],combo);filters.Children.Add(combo);combo.SelectionChanged+=(s,e)=>{if(!bindingFilters){offset=0;Filter();}};
  }
  inventory.ColumnDefinitions.Add(new ColumnDefinition());inventory.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  listFrame.RowDefinitions.Add(new RowDefinition());listFrame.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});inventory.Children.Add(listFrame);
  var cardScroll=new ScrollViewer{Content=cards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};listFrame.Children.Add(cardScroll);
  var detailBody=new Grid();detailBody.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});detailBody.RowDefinitions.Add(new RowDefinition());
  var close=new Button{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,8),ToolTip=Locale.T("닫기")};close.SetResourceReference(FrameworkElement.StyleProperty,"PanelCloseButton");System.Windows.Automation.AutomationProperties.SetName(close,Locale.T("닫기"));close.Click+=(s,e)=>CloseDetail();detailBody.Children.Add(close);
  detailScroll=new ScrollViewer{Content=PanelUi.ScrollContent(details),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(detailScroll,1);detailBody.Children.Add(detailScroll);
  detailFrame=new Border{Child=detailBody,Background=new SolidColorBrush(Color.FromRgb(34,38,44)),CornerRadius=PanelUi.Corners,Padding=new Thickness(18),Width=0,Visibility=Visibility.Collapsed,ClipToBounds=true};Grid.SetColumn(detailFrame,1);inventory.Children.Add(detailFrame);
  Grid.SetRow(inventory,1);View.Children.Add(inventory);
  var paging=new DockPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(previous,Dock.Left);DockPanel.SetDock(next,Dock.Right);paging.Children.Add(previous);paging.Children.Add(next);count.HorizontalAlignment=HorizontalAlignment.Center;count.VerticalAlignment=VerticalAlignment.Center;count.Margin=new Thickness(0);paging.Children.Add(count);Grid.SetRow(paging,1);listFrame.Children.Add(paging);
  ScrollViewer viewport=null;SizeChangedEventHandler resize=(s,e)=>{
   inventory.Height=viewport==null?420:Math.Max(180,viewport.ActualHeight-header.ActualHeight);
   LayoutDetail(false);
  };arrange=()=>resize(null,null);View.SizeChanged+=resize;View.Loaded+=(s,e)=>{DependencyObject node=View;while((node=VisualTreeHelper.GetParent(node))!=null){viewport=node as ScrollViewer;if(viewport!=null)break;}if(viewport!=null)viewport.SizeChanged+=resize;resize(null,null);};View.Unloaded+=(s,e)=>{if(viewport!=null)viewport.SizeChanged-=resize;viewport=null;};
  var capacityTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(140)};capacityTimer.Tick+=(s,e)=>{capacityTimer.Stop();if(restoring||!View.IsVisible)return;int capacity=Capacity(cardScroll.ViewportWidth,cardScroll.ViewportHeight);if(capacity==pageSize)return;pageSize=capacity;offset=Math.Min(offset,Math.Max(0,((filtered.Count-1)/pageSize)*pageSize));Render();};cardScroll.SizeChanged+=(s,e)=>{capacityTimer.Stop();capacityTimer.Start();};View.Unloaded+=(s,e)=>capacityTimer.Stop();
  View.PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape&&selectedIdentity.Length>0){CloseDetail();e.Handled=true;}};
  search.TextChanged+=(s,e)=>{offset=0;Filter();};previous.Click+=(s,e)=>{offset=Math.Max(0,offset-pageSize);Render();};next.Click+=(s,e)=>{offset+=pageSize;Render();};
 }
 internal async void Show(string value){
  GameDataCatalog.QueueRefresh(Setup.DataFolder);if(value=="materials")MaterialCatalog.QueueRefresh(Setup.DataFolder);else BuildCriteriaCatalog.QueueRefresh(Setup.DataFolder);criteriaRevision=BuildCriteriaCatalog.Revision(Setup.DataFolder);SaveBrowse();accountRevision=AccountRevision();restoring=true;section=value;if(!states.TryGetValue(value,out browse)){browse=new BrowseState();states[value]=browse;}offset=browse.Offset;selectedIdentity=browse.Selected;int request=++generation;snapshot=new Dictionary<string,object>();artifactReviews.Clear();search.Text=browse.Search;filters.Visibility=Visibility.Visible;foreach(var selector in selectors)selector.Value.Visibility=Visibility.Collapsed;cards.Children.Clear();details.Children.Clear();LayoutDetail(false);count.Text=Locale.T("불러오는 중…");
  try{
   var result=await Task.Run(()=>{Dictionary<string,object> reference;var revision=GameDataCatalog.Revision(Setup.DataFolder);var data=new CatheryneTools(Setup.DataFolder).AccountSnapshot(out reference);object metadata,raw;var names=data.TryGetValue("catheryne",out metadata)&&CodexChat.Map(metadata).TryGetValue("characterAliases",out raw)?CodexChat.Map(raw):new Dictionary<string,object>();var rows=InventoryOrder.Sort(value,AccountMerge.Inventory(data,value),item=>value=="materials"?CodexChat.S(item,"name"):AccountIdentity.Name(item,names)).ToList();var ids=new Dictionary<Dictionary<string,object>,string>();foreach(var row in rows)ids[row]=CatheryneTools.Json().Serialize(row);var review=value=="artifacts"?ArtifactReview.Load(Setup.DataFolder,data,rows.ToArray()):null;return Tuple.Create(data,rows,ids,revision,names,review);});
   if(request!=generation)return;snapshot=result.Item1;records=result.Item2;identities=result.Item3;catalogRevision=result.Item4;
   if(result.Item6!=null)foreach(var item in result.Item6.Items)artifactReviews[Identity(item.Artifact)]=item;
   aliases=result.Item5;BindFilters();offset=browse.Offset;restoring=false;Filter();
  }catch(Exception error){if(request==generation){restoring=false;count.Text=error.Message;previous.IsEnabled=next.IsEnabled=false;}}
 }
 internal static string AllFilter(string field){switch(field){case "element":return Locale.T("전체 원소");case "weapon":return Locale.T("전체 무기 종류");case "rarity":return Locale.T("전체 등급");case "location":return Locale.T("전체 장착 캐릭터");case "setKey":return Locale.T("전체 성유물 종류");case "slotKey":return Locale.T("전체 부위");case "mainStatKey":return Locale.T("전체 주옵션");case "level":return Locale.T("전체 레벨");case "refinement":return Locale.T("전체 재련");case "review":return Locale.T("전체 정리 판단");default:throw new ArgumentException("Unknown inventory filter: "+field);}}
 internal static int Capacity(double width,double height){return Math.Max(1,(int)Math.Floor(Math.Max(0,width)/126))*Math.Max(1,(int)Math.Floor(Math.Max(0,height)/180));}
 IEnumerable<Dictionary<string,object>> Records(string kind){return records;}
 string Key(Dictionary<string,object> item){return section=="materials"?CodexChat.S(item,"itemId"):AccountIdentity.Key(item);}
 string Name(Dictionary<string,object> item){return section=="materials"?GameCatalog.MaterialName(CodexChat.S(item,"itemId"),CodexChat.S(item,"name")):AccountIdentity.Name(item,aliases);}
 string Field(Dictionary<string,object> item,string field){
  if(section=="characters"&&(field=="element"||field=="weapon"||field=="rarity"))return AccountIdentity.CharacterField(item,field);
  if(section=="weapons"&&field=="rarity")return InventoryOrder.Rarity(section,item);if(section=="weapons"&&field=="weapon")return GameCatalog.WeaponField(Key(item),"weaponType");
  if(field=="review"){ArtifactDecision review;return artifactReviews.TryGetValue(Identity(item),out review)?ArtifactReview.Label(review.Status):ArtifactReview.Label("review");}
  if(field=="setKey")return AccountIdentity.SetName(item);
  string value=CodexChat.S(item,field);if(field=="location")return !item.ContainsKey(field)||item[field]==null?Locale.T("미확인"):value.Length==0?Locale.T("미장착"):AccountIdentity.CharacterName(snapshot,value,aliases);return value.Length==0?Locale.T("미확인"):GameCatalog.Name(value);
 }
 void BindFilters(){bindingFilters=true;try{foreach(var selector in selectors){bool visible=section=="materials"?selector.Key=="rarity":section=="characters"?new[]{"element","weapon","rarity","level"}.Contains(selector.Key):section=="weapons"?new[]{"weapon","rarity","location","level","refinement"}.Contains(selector.Key):new[]{"rarity","location","setKey","slotKey","mainStatKey","level","review"}.Contains(selector.Key);selector.Value.Visibility=visible?Visibility.Visible:Visibility.Collapsed;if(!visible)continue;var choices=selector.Key=="review"?ArtifactReview.States.Select(ArtifactReview.Label).Where(label=>Records(section).Any(x=>Field(x,"review")==label)).ToList():Records(section).Select(x=>Field(x,selector.Key)).Distinct().OrderBy(x=>x).ToList();choices.Insert(0,AllFilter(selector.Key));selector.Value.ItemsSource=choices;string saved;selector.Value.SelectedItem=browse.Filters.TryGetValue(selector.Key,out saved)&&choices.Contains(saved)?saved:choices[0];}}finally{bindingFilters=false;}}
 bool Matches(Dictionary<string,object> item){foreach(var selector in selectors){if(selector.Value.Visibility!=Visibility.Visible||selector.Value.SelectedIndex<=0)continue;if(Field(item,selector.Key)!=Convert.ToString(selector.Value.SelectedItem))return false;}return true;}
 void Filter(){if(restoring)return;string query=search.Text.Trim();filtered=Records(section).Where(item=>Matches(item)&&(query.Length==0||Name(item).IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0||Key(item).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).ToList();offset=Math.Min(offset,Math.Max(0,((filtered.Count-1)/pageSize)*pageSize));Render();}
 void Render(){
  if(arrange!=null)arrange();
  cards.Children.Clear();details.Children.Clear();previous.IsEnabled=offset>0;next.IsEnabled=offset+pageSize<filtered.Count;
  object collected;if(!snapshot.TryGetValue(section=="materials"?"materialInventory":section,out collected)||collected==null){count.Text=Locale.T("미수집");return;}
  count.Text=filtered.Count==0?Locale.T("저장된 항목이 없습니다."):(offset+1)+"–"+Math.Min(offset+pageSize,filtered.Count)+" / "+filtered.Count;
  foreach(var item in filtered.Skip(offset).Take(pageSize)){
   ArtifactDecision review;artifactReviews.TryGetValue(Identity(item),out review);var button=InventoryCard.Button(section,item,Name(item),snapshot,aliases,()=>Select(item),review:review);cards.Children.Add(button);
  }
  var selected=selectedIdentity.Length==0?null:filtered.FirstOrDefault(x=>Identity(x)==selectedIdentity);if(selected!=null)Select(selected,false);else CloseDetail(false);SaveBrowse();
 }
 void Select(Dictionary<string,object> item,bool animate=true){
  bool changed=selectedIdentity!=Identity(item);selectedIdentity=Identity(item);SaveBrowse();LayoutDetail(animate);if(changed)detailScroll.ScrollToTop();
  foreach(Button card in cards.Children)card.Background=new SolidColorBrush(Identity((Dictionary<string,object>)card.Tag)==selectedIdentity?Color.FromRgb(58,78,72):Color.FromRgb(45,50,58));
  details.Children.Clear();
  if(section=="characters"){details.Children.Add(CharacterInventory.Detail(snapshot,item,Name(item),Plan,Ask,Review,()=>Select(item)));return;}
  ArtifactDecision decision;if(section=="artifacts"&&artifactReviews.TryGetValue(Identity(item),out decision))details.Children.Add(ArtifactReview.Detail(decision,Ask,a=>Select(a)));
  details.Children.Add(section=="materials"?MaterialPanel.Detail(item,Name(item)):EquipmentInventory.Detail(section,snapshot,item,Name(item),aliases));
 }

 void CloseDetail(bool animate=true){selectedIdentity="";SaveBrowse();foreach(Button card in cards.Children)card.Background=new SolidColorBrush(Color.FromRgb(45,50,58));LayoutDetail(animate);}
 void LayoutDetail(bool animate){
  bool open=selectedIdentity.Length>0;bool narrow=View.ActualWidth<680;double target=open?Math.Max(0,narrow?View.ActualWidth:(View.ActualWidth-16)/2):0;
  listFrame.Visibility=open&&narrow?Visibility.Collapsed:Visibility.Visible;detailFrame.Margin=open&&!narrow?new Thickness(16,0,0,0):new Thickness(0);
  if(Math.Abs(target-detailWidth)<0.5)return;detailWidth=target;double from=detailFrame.ActualWidth;detailFrame.BeginAnimation(FrameworkElement.WidthProperty,null);detailFrame.Width=target;detailFrame.Visibility=open?Visibility.Visible:Visibility.Collapsed;
  if(!animate||!View.IsLoaded||!SystemParameters.ClientAreaAnimation)return;
  detailFrame.Visibility=Visibility.Visible;
  var motion=new System.Windows.Media.Animation.DoubleAnimation(from,target,TimeSpan.FromMilliseconds(180)){EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut},FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop};
  motion.Completed+=(s,e)=>{if(selectedIdentity.Length==0&&detailWidth==0)detailFrame.Visibility=Visibility.Collapsed;};detailFrame.BeginAnimation(FrameworkElement.WidthProperty,motion);
 }


}


// One showcase card surface; each inventory supplies its own meaningful badges.
internal static class InventoryCard {
 internal static Button Button(string section,Dictionary<string,object> item,string name,Dictionary<string,object> snapshot,Dictionary<string,object> aliases,Action selected=null,bool stretch=false,ArtifactDecision review=null){
  var button=PanelUi.Button("");button.Width=stretch?double.NaN:116;button.MinWidth=stretch?0:button.MinWidth;button.Height=170;button.BorderThickness=new Thickness(0);button.VerticalAlignment=VerticalAlignment.Top;button.Padding=new Thickness(2);button.HorizontalAlignment=stretch?HorizontalAlignment.Stretch:HorizontalAlignment.Left;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Stretch;button.Margin=new Thickness(0,0,10,10);button.Content=Create(section,item,name,snapshot,aliases,review:review);button.Tag=item;button.ToolTip=name+(CodexChat.S(item,"inventoryMatch")=="unresolved"?"\n"+Locale.T("최신 관측 정보입니다. 기존 장비와의 대응 및 보유 수는 미확인입니다."):"");System.Windows.Automation.AutomationProperties.SetName(button,name);if(stretch){button.SizeChanged+=(s,e)=>{double height=button.ActualWidth+50;if(button.ActualWidth>0&&Math.Abs(button.Height-height)>.5)button.Height=height;};}if(selected!=null)button.Click+=(s,e)=>selected();return button;
 }
 internal static System.Windows.Controls.Primitives.UniformGrid Party(Dictionary<string,object>[] characters,Action<Dictionary<string,object>> selected=null){
  if(characters.Length!=4)throw new ArgumentException("파티마다 다른 캐릭터 4명을 입력해 주세요.");var row=new System.Windows.Controls.Primitives.UniformGrid{Columns=4,Rows=1,MaxWidth=512,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(-4,0,-4,12)};var snapshot=new Dictionary<string,object>{{"characters",characters}};
  for(int i=0;i<characters.Length;i++){var character=characters[i];var card=Button("characters",character,AccountIdentity.Name(character),snapshot,null,selected==null?(Action)null:()=>selected(character),true);card.Margin=new Thickness(4,0,4,0);row.Children.Add(card);}return row;
 }
 internal static string GradeColor(string rarity){return rarity=="5"?"#AF7947":rarity=="4"?"#776495":rarity=="3"?"#557AA1":rarity=="2"?"#568B74":"#656A76";}
 static Brush Brush(string hex){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));}
 static Border Badge(string text,VerticalAlignment vertical,Brush fill){return new Border{Child=new TextBlock{Text=text,FontSize=12,Foreground=Brushes.White,TextTrimming=TextTrimming.CharacterEllipsis},MaxWidth=94,Background=fill,CornerRadius=PanelUi.Corners,Padding=new Thickness(5,2,5,2),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=vertical,Margin=new Thickness(5)};}
 internal static FrameworkElement Create(string section,Dictionary<string,object> item,string name,Dictionary<string,object> snapshot,Dictionary<string,object> aliases,bool elementIcon=false,ArtifactDecision review=null){
  bool character=section=="characters",artifact=section=="artifacts";string rarity=InventoryOrder.Rarity(section,item);
  string color=GradeColor(rarity);
  var body=new Grid();body.RowDefinitions.Add(new RowDefinition());body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(26)});body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(artifact?44:24)});
  var imageArea=new Grid{Background=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(color),Color.FromRgb(59,63,76),90),ClipToBounds=true};var image=new Image{Stretch=character?Stretch.UniformToFill:Stretch.Uniform,Margin=new Thickness(character?0:8)};AccountIdentity.Portrait(image,item);RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);imageArea.Children.Add(image);
  string top=character?AccountIdentity.CharacterField(item,"element"):section=="artifacts"?GameCatalog.Name(CodexChat.S(item,"slotKey")):"";
  if(top.Length>0){if(character&&elementIcon){var symbol=GameCatalog.ElementIcon(top,20);imageArea.Children.Add(new Border{Child=symbol,Background=Brush("#B51E242B"),Padding=new Thickness(3),CornerRadius=PanelUi.Corners,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(5)});}else imageArea.Children.Add(Badge(top,VerticalAlignment.Top,character?new SolidColorBrush(CharacterInventory.Element(item)):Brush("#B51E242B")));}
  string lower=character?CodexChat.S(item,"constellation"):section=="weapons"&&CodexChat.S(item,"refinement").Length>0?"R"+CodexChat.S(item,"refinement"):"";
  if(lower.Length>0)imageArea.Children.Add(Badge(lower,VerticalAlignment.Bottom,Brush("#B51E242B")));
  if(!character){object rows;string location=CodexChat.S(item,"location");var owner=location.Length>0&&snapshot.TryGetValue("characters",out rows)?CodexChat.Items(rows).FirstOrDefault(x=>AccountIdentity.Key(x)==location||CodexChat.S(x,"key")==location):null;if(owner!=null){var face=new Image{Width=32,Height=32,Stretch=Stretch.Uniform};GameCatalog.SidePortrait(face,owner);RenderOptions.SetBitmapScalingMode(face,BitmapScalingMode.HighQuality);imageArea.Children.Add(new Border{Child=face,Width=36,Height=36,CornerRadius=new CornerRadius(18),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(4),ToolTip=AccountIdentity.Name(owner,aliases)});}}
  if(review!=null){var mark=Badge(ArtifactReview.Label(review.Status),VerticalAlignment.Bottom,Brush("#E01E242B"));((TextBlock)mark.Child).Foreground=new SolidColorBrush(ArtifactReview.Color(review.Status));mark.ToolTip=Locale.T(review.Reason);imageArea.Children.Add(mark);}
  body.Children.Add(imageArea);
  var level=new Border{Background=Brush("#DDD4C5"),Child=new TextBlock{Text=section=="materials"?MaterialInventory.CountText(item):elementIcon&&CodexChat.S(item,"level").Length==0?Locale.T("육성 미확인"):(section=="artifacts"?"+":"Lv. ")+CodexChat.S(item,"level"),Foreground=Brush("#3D434E"),FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};Grid.SetRow(level,1);body.Children.Add(level);
  var title=new TextBlock{Text=name,FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,HorizontalAlignment=HorizontalAlignment.Stretch,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center};var caption=new StackPanel{VerticalAlignment=VerticalAlignment.Center};caption.Children.Add(title);if(artifact){string stat=GameCatalog.ArtifactMainStat(item);caption.Children.Add(new TextBlock{Text=stat,ToolTip=stat,FontSize=11,Foreground=Brush("#E7C170"),TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center,Margin=new Thickness(3,2,3,0)});}Grid.SetRow(caption,2);body.Children.Add(caption);return body;
 }
}

// Card color and ordering use the same resolved item grade.
internal static class InventoryOrder {
 internal static string Rarity(string section,Dictionary<string,object> item){string value=section=="characters"?AccountIdentity.CharacterField(item,"rarity"):section=="weapons"?GameCatalog.WeaponField(AccountIdentity.Key(item),"rarity"):CodexChat.S(item,"rarity");return value.Length>0?value:CodexChat.S(item,"rarity");}
 static int Number(string value){int number;return int.TryParse(value,out number)?number:-1;}
 static int Slot(Dictionary<string,object> item){int rank=Array.IndexOf(new[]{"flower","plume","sands","goblet","circlet"},CodexChat.S(item,"slotKey"));return rank<0?5:rank;}
 internal static IEnumerable<Dictionary<string,object>> Sort(string section,IEnumerable<Dictionary<string,object>> items,Func<Dictionary<string,object>,string> name){var ordered=items.OrderByDescending(x=>Number(Rarity(section,x)));if(section=="materials")return ordered.ThenBy(name,StringComparer.CurrentCulture).ThenBy(x=>CodexChat.S(x,"itemId"),StringComparer.Ordinal);if(section=="artifacts")return ordered.ThenBy(x=>GameCatalog.Name(CodexChat.S(x,"setKey")),StringComparer.CurrentCulture).ThenBy(Slot).ThenByDescending(x=>Number(CodexChat.S(x,"level"))).ThenBy(x=>CodexChat.S(x,"mainStatKey"),StringComparer.Ordinal);return ordered.ThenByDescending(x=>Number(CodexChat.S(x,"level"))).ThenBy(name,StringComparer.CurrentCulture).ThenByDescending(x=>Number(CodexChat.S(x,section=="weapons"?"refinement":"constellation"))).ThenBy(AccountIdentity.Key,StringComparer.Ordinal);}
}


// Weapon and artifact details share a hero, stat tiles and owner presentation.
internal static class EquipmentInventory {
 static TextBlock Text(string value,double size=14,bool muted=false){return new TextBlock{Text=value,FontSize=size,Foreground=muted?new SolidColorBrush(Color.FromRgb(174,181,190)):Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6)};}
 static Border Tile(string label,string value,bool useful=false){var body=new StackPanel();body.Children.Add(Text(label,12,true));var number=Text(value,22);if(useful)number.Foreground=new SolidColorBrush(Color.FromRgb(108,222,182));body.Children.Add(number);return new Border{Child=body,Padding=new Thickness(12),Margin=new Thickness(0,0,8,8),Background=new SolidColorBrush(Color.FromRgb(41,49,56)),CornerRadius=PanelUi.Corners};}
 static string Number(Dictionary<string,object> item,string field,string prefix=""){string value=CodexChat.S(item,field);return value.Length==0?Locale.T("미수집"):prefix+value;}
 static string StatValue(Dictionary<string,object> stat){string value=CodexChat.S(stat,"value"),key=CodexChat.S(stat,"key");return value.Length==0?Locale.T("미수집"):value+(key.EndsWith("_")?"%":"");}
 internal static FrameworkElement Detail(string section,Dictionary<string,object> snapshot,Dictionary<string,object> item,string name,Dictionary<string,object> aliases){
  bool artifact=section=="artifacts";var body=new StackPanel();var hero=new Grid{Margin=new Thickness(0,0,0,16)};hero.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(128)});hero.ColumnDefinitions.Add(new ColumnDefinition());string rarity=InventoryOrder.Rarity(section,item);var image=new Image{Width=116,Height=116,Stretch=Stretch.Uniform};AccountIdentity.Portrait(image,item,256);RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);hero.Children.Add(new Border{Child=image,Height=128,CornerRadius=PanelUi.Corners,VerticalAlignment=VerticalAlignment.Top,Background=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(InventoryCard.GradeColor(rarity)),Color.FromRgb(41,49,56),90)});
  var title=new StackPanel{Margin=new Thickness(16,0,0,0)};title.Children.Add(Text(name,24));int grade;if(int.TryParse(rarity,out grade)&&grade>=1&&grade<=5){var stars=Text(new string('★',grade),15);stars.Foreground=new SolidColorBrush(Color.FromRgb(231,193,112));title.Children.Add(stars);}if(artifact){title.Children.Add(Text(AccountIdentity.SetName(item),13,true));title.Children.Add(Text(GameCatalog.Name(CodexChat.S(item,"slotKey")),13,true));}Grid.SetColumn(title,1);hero.Children.Add(title);body.Children.Add(hero);
  var metrics=new System.Windows.Controls.Primitives.UniformGrid{Columns=artifact?2:3};metrics.Children.Add(Tile(Locale.T("레벨"),Number(item,"level",artifact?"+":"Lv. ")));if(artifact)metrics.Children.Add(Tile(Locale.T("주옵션"),GameCatalog.ArtifactMainStat(item)));else{metrics.Children.Add(Tile(Locale.T("돌파"),Number(item,"ascension")));metrics.Children.Add(Tile(Locale.T("재련"),Number(item,"refinement","R")));}body.Children.Add(metrics);
  object raw;var owners=snapshot.TryGetValue("characters",out raw)?CodexChat.Items(raw).ToArray():new Dictionary<string,object>[0];string location=CodexChat.S(item,"location");var owner=owners.FirstOrDefault(x=>AccountIdentity.Key(x)==location||CodexChat.S(x,"key")==location);var ownerRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,16)};if(owner!=null){var portrait=new Image{Width=36,Height=36,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,10,0)};GameCatalog.SidePortrait(portrait,owner);ownerRow.Children.Add(portrait);}var ownerText=new StackPanel();ownerText.Children.Add(Text(Locale.T("장착 캐릭터"),12,true));ownerText.Children.Add(Text(!item.ContainsKey("location")||item["location"]==null?Locale.T("미수집"):location.Length==0?Locale.T("미장착"):AccountIdentity.CharacterName(snapshot,location,aliases),14));ownerRow.Children.Add(ownerText);body.Children.Add(ownerRow);
  if(artifact){string[] useful=owner==null?new string[0]:BuildAnalysis.Useful(Setup.DataFolder,AccountIdentity.Key(owner),owner);if(item.TryGetValue("substats",out raw)&&raw!=null){var tiles=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};foreach(var stat in CodexChat.Items(raw))tiles.Children.Add(Tile(GameCatalog.Name(CodexChat.S(stat,"key")),StatValue(stat),useful.Contains(CodexChat.S(stat,"key"))));body.Children.Add(PanelUi.Section(Locale.T("부옵션"),tiles));}else body.Children.Add(PanelUi.Text(Locale.T("부옵션 미수집"),true));body.Children.Add(PanelUi.Section(Locale.T("성유물 점수"),BuildAnalysis.ScoreView(BuildAnalysis.Score(item,useful))));}
  else{
   var values=GameCatalog.WeaponDetails(item);var properties=new StackPanel();string attack=CodexChat.S(values,"attack"),stat=CodexChat.S(values,"statName"),number=CodexChat.S(values,"statValue");
   if(attack.Length>0)properties.Children.Add(PanelUi.Row(Locale.T("기초 공격력"),Text(attack,18)));if(stat.Length>0&&number.Length>0)properties.Children.Add(PanelUi.Row(stat,Text(number,18)));
   if(properties.Children.Count==0&&item.TryGetValue("hoyolab",out raw)){var data=CodexChat.Map(raw);foreach(string field in new[]{"base_atk","main_property","sub_property"}){object value;if(!data.TryGetValue(field,out value)||value==null)continue;var property=value as Dictionary<string,object>;if(property!=null){string propertyName=CodexChat.S(property,"name"),propertyValue=CodexChat.S(property,"value");if(propertyName.Length>0&&propertyValue.Length>0)properties.Children.Add(PanelUi.Row(propertyName,Text(propertyValue,18)));}else if(field=="base_atk")properties.Children.Add(PanelUi.Row(Locale.T("기초 공격력"),Text(Convert.ToString(value),18)));}}
   if(properties.Children.Count>0)body.Children.Add(PanelUi.Section(Locale.T("능력치"),properties));string effect=CodexChat.S(values,"effect");if(effect.Length>0)body.Children.Add(PanelUi.Section(CodexChat.S(values,"effectName"),PanelUi.Text(effect)));
  }
  if(item.TryGetValue("lock",out raw)&&raw!=null)body.Children.Add(PanelUi.Row(Locale.T("잠금"),Text(Locale.T(Equals(raw,true)?"잠김":"해제"),13)));if(CodexChat.S(item,"inventoryMatch")=="unresolved")body.Children.Add(PanelUi.Text(Locale.T("최신 관측 · 보유 수 미확인"),true));return body;
 }
}
