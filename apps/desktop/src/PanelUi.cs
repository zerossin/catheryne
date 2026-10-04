using System.Collections.Generic;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class PanelUi {
 internal const double SurfaceRadius=8;
 internal static readonly CornerRadius Corners=new CornerRadius(SurfaceRadius);
 // Scroll thumbs have their own radius: the shared surface radius is too large for a 5 DIP handle.
 static readonly Style ScrollBars=(Style)System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>
  <Setter Property='Width' Value='7'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
   <Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='{TemplateBinding Orientation}'>
    <Track.DecreaseRepeatButton><RepeatButton x:Name='Decrease' Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
    <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border x:Name='Handle' Background='#555B65' CornerRadius='2.5' Width='5' HorizontalAlignment='Center' Margin='0,1'/><ControlTemplate.Triggers><DataTrigger Binding='{Binding Orientation, RelativeSource={RelativeSource AncestorType=ScrollBar}}' Value='{x:Static Orientation.Horizontal}'><Setter TargetName='Handle' Property='Width' Value='Auto'/><Setter TargetName='Handle' Property='Height' Value='5'/><Setter TargetName='Handle' Property='HorizontalAlignment' Value='Stretch'/><Setter TargetName='Handle' Property='VerticalAlignment' Value='Center'/><Setter TargetName='Handle' Property='Margin' Value='1,0'/></DataTrigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
    <Track.IncreaseRepeatButton><RepeatButton x:Name='Increase' Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
   </Track>
   <ControlTemplate.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/><Setter TargetName='Decrease' Property='Command' Value='{x:Static ScrollBar.PageLeftCommand}'/><Setter TargetName='Increase' Property='Command' Value='{x:Static ScrollBar.PageRightCommand}'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
  <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter Property='Width' Value='Auto'/><Setter Property='Height' Value='7'/></Trigger><Trigger Property='Maximum' Value='0'><Setter Property='Opacity' Value='0'/><Setter Property='IsHitTestVisible' Value='False'/></Trigger></Style.Triggers>
 </Style>");
 internal static void ApplyGeometry(Window window){window.Resources["AppHeaderHeight"]=LauncherWindowLayout.HeaderHeight;window.Resources["SurfaceCorners"]=Corners;window.Resources["SurfaceRadius"]=SurfaceRadius;window.Resources[SystemParameters.VerticalScrollBarWidthKey]=8.0;window.Resources[SystemParameters.HorizontalScrollBarHeightKey]=8.0;window.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=ScrollBars;}
 static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBox,Grid> Inputs=new System.Runtime.CompilerServices.ConditionalWeakTable<TextBox,Grid>();
 internal static CheckBox Toggle(bool enabled,string name,bool outlined=false){
  var toggle=new CheckBox{BorderBrush=outlined?new SolidColorBrush(Color.FromArgb(150,224,230,239)):Brushes.Transparent,BorderThickness=new Thickness(outlined?1:0),IsChecked=enabled,Width=46,Height=26,Margin=new Thickness(8,8,0,8),VerticalAlignment=VerticalAlignment.Center};
  string duration=SystemParameters.ClientAreaAnimation?"0:0:0.18":"0:0:0";
  toggle.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'><Border x:Name='Track' Background='#515763' CornerRadius='13' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'><Border x:Name='Knob' Background='#E6E6E8' Width='20' Height='20' CornerRadius='10' Margin='3' HorizontalAlignment='Left'><Border.RenderTransform><TranslateTransform X='0'/></Border.RenderTransform></Border></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='20' Duration='"+duration+"'><DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation><ColorAnimation Storyboard.TargetName='Track' Storyboard.TargetProperty='(Border.Background).(SolidColorBrush.Color)' To='#438A76' Duration='"+duration+"'/></Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='0' Duration='"+duration+"'><DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation><ColorAnimation Storyboard.TargetName='Track' Storyboard.TargetProperty='(Border.Background).(SolidColorBrush.Color)' To='#515763' Duration='"+duration+"'/></Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Track' Property='BorderBrush' Value='#B9E6D8'/><Setter TargetName='Track' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
  System.Windows.Automation.AutomationProperties.SetName(toggle,name);return toggle;
 }
 internal static Grid SearchInput(TextBox input,string hint){hint=Locale.T(hint);input.Padding=new Thickness(12,4,12,4);input.FontSize=13;input.MinHeight=38;input.VerticalContentAlignment=VerticalAlignment.Center;return TextInput(input,hint);}
 internal static Grid TextInput(TextBox input,string hint){
  Grid existing;if(Inputs.TryGetValue(input,out existing)){if(existing.Parent is Panel)((Panel)existing.Parent).Children.Remove(existing);else if(existing.Parent is Border)((Border)existing.Parent).Child=null;if(input.Parent==null)existing.Children.Insert(0,input);return existing;}
  var area=new Grid();area.Children.Add(input);Inputs.Add(input,area);
  var watermark=new TextBox{Text=hint,IsReadOnly=true,IsHitTestVisible=false,Focusable=false,IsTabStop=false,Background=Brushes.Transparent,Foreground=new SolidColorBrush(Color.FromRgb(150,155,163))};
  foreach(var property in new[]{Control.TemplateProperty,Control.PaddingProperty,Control.BorderThicknessProperty,Control.BorderBrushProperty,Control.FontFamilyProperty,Control.FontSizeProperty,Control.FontWeightProperty,Control.VerticalContentAlignmentProperty,FrameworkElement.MarginProperty,FrameworkElement.MinHeightProperty,FrameworkElement.HeightProperty,FrameworkElement.VerticalAlignmentProperty,TextBox.TextWrappingProperty})System.Windows.Data.BindingOperations.SetBinding(watermark,property,new System.Windows.Data.Binding{Source=input,Path=new PropertyPath(property)});
  area.Children.Add(watermark);Action update=()=>watermark.Visibility=input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;input.TextChanged+=(s,e)=>update();update();System.Windows.Automation.AutomationProperties.SetName(input,hint);return area;
 }
 internal static void InputHint(TextBox input,string hint){Grid area;if(!Inputs.TryGetValue(input,out area))return;foreach(var child in area.Children){var watermark=child as TextBox;if(watermark!=null&&watermark!=input)watermark.Text=hint;}System.Windows.Automation.AutomationProperties.SetName(input,hint);}
 internal static ContextMenu Menu(){
  var menu=new ContextMenu();menu.Foreground=Brushes.White;menu.Background=new SolidColorBrush(Color.FromRgb(38,41,46));
  menu.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContextMenu'><Border Background='#26292E' BorderBrush='#41454A' BorderThickness='1' CornerRadius='{DynamicResource SurfaceCorners}' Padding='4'><ScrollViewer MaxHeight='340' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate>");
  var item=new Style(typeof(MenuItem));item.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'><Border x:Name='Surface' Background='Transparent' CornerRadius='{DynamicResource SurfaceCorners}' Padding='12,8'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions><Path x:Name='Check' Visibility='Collapsed' Width='12' Height='12' Margin='0,0,8,0' Data='M1,6 L4.5,9.5 L11,2.5' Stroke='#8DD8BF' StrokeThickness='2' Stretch='Uniform'/><ContentPresenter Grid.Column='1' ContentSource='Header'/></Grid></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Check' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Surface' Property='Background' Value='#354047'/></Trigger></ControlTemplate.Triggers></ControlTemplate>")));menu.ItemContainerStyle=item;return menu;
 }
 internal static FrameworkElement Chevron(){var box=new Canvas{Width=20,Height=20};box.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M5,8 L10,13 L15,8"),Stroke=new SolidColorBrush(Color.FromRgb(174,181,190)),StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round});return box;}
 internal static FrameworkElement Meter(string label,string value,double? percent,Color color,string detail=null,string labelHelp=null){var body=new StackPanel{Margin=new Thickness(0,0,0,14)};var heading=new DockPanel();var number=new TextBlock{Text=value,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(number,Dock.Right);heading.Children.Add(number);var labelText=new TextBlock{Text=label,Foreground=new SolidColorBrush(Color.FromRgb(174,181,190)),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};if(labelHelp==null)heading.Children.Add(labelText);else{var labels=new Grid{HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,12,0)};labels.ColumnDefinitions.Add(new ColumnDefinition());labels.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});labels.Children.Add(labelText);var help=Help(labelHelp);help.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(help,1);labels.Children.Add(help);heading.Children.Add(labels);}if(label!=null||value!=null)body.Children.Add(heading);var track=new Grid{Height=5,Margin=new Thickness(0,8,0,0),ClipToBounds=true,Background=new SolidColorBrush(Color.FromRgb(65,70,80))};if(percent.HasValue){track.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(Math.Max(0,Math.Min(100,percent.Value)),GridUnitType.Star)});track.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(Math.Max(0,100-percent.Value),GridUnitType.Star)});track.Children.Add(new Border{Background=new SolidColorBrush(color)});}body.Children.Add(track);body.ToolTip=detail;return body;}

 internal static RadioButton ChoiceCard(string label){
  var card=new RadioButton{Content=label,Foreground=Brushes.White,FontSize=14,MinHeight=76,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,Cursor=System.Windows.Input.Cursors.Hand};
  card.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RadioButton'><Border x:Name='Surface' Background='#292D34' BorderBrush='Transparent' BorderThickness='1' CornerRadius='{DynamicResource SurfaceCorners}' Padding='12'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' IsHitTestVisible='False'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#354047'/></Trigger><Trigger Property='IsChecked' Value='True'><Setter TargetName='Surface' Property='Background' Value='#2D4843'/><Setter TargetName='Surface' Property='BorderBrush' Value='#6FC9AB'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Surface' Property='Opacity' Value='0.5'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#B9E6D8'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
  System.Windows.Automation.AutomationProperties.SetName(card,label);return card;
 }
 internal static void NavigationLabel(Button row,string label,bool attention=false){var grid=row.Content as Grid;if(grid!=null)foreach(var child in grid.Children){var title=child as TextBlock;if(title!=null){title.Inlines.Clear();title.Inlines.Add(new System.Windows.Documents.Run(label));if(attention){var icon=WarningIcon();icon.Margin=new Thickness(8,0,0,0);title.Inlines.Add(new System.Windows.Documents.InlineUIContainer(icon){BaselineAlignment=BaselineAlignment.Center});}break;}}}
 internal static Button NavigationRow(string label,object icon,Action action){
  var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(20)});
  var iconHost=new ContentControl{Content=icon,Width=22,Height=22,VerticalAlignment=VerticalAlignment.Center,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=20};row.Children.Add(iconHost);
  var title=new TextBlock{Text=label,FontSize=14,Margin=new Thickness(14,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(title,1);row.Children.Add(title);
  var chevron=new System.Windows.Shapes.Path{Data=Geometry.Parse("M1,1 L6,6 L1,11"),Stroke=new SolidColorBrush(Color.FromRgb(145,154,166)),StrokeThickness=1.6,Width=8,Height=12,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetColumn(chevron,2);row.Children.Add(chevron);
  var button=new Button{Content=row,Height=60,Padding=new Thickness(14,0,14,0),Margin=new Thickness(0,0,0,6),Background=new SolidColorBrush(Color.FromArgb(60,65,73,84)),HorizontalContentAlignment=HorizontalAlignment.Stretch};
  button.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border x:Name='Surface' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='{DynamicResource SurfaceCorners}' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#354047'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
  button.Click+=(s,e)=>action();return button;
 } internal static FrameworkElement VerifiedMark(){var mark=new Grid {Width=16,Height=16,Margin=new Thickness(0,2,7,0),VerticalAlignment=VerticalAlignment.Top,Visibility=Visibility.Collapsed};mark.Children.Add(new System.Windows.Shapes.Ellipse {Fill=new SolidColorBrush(Color.FromRgb(58,166,230))});mark.Children.Add(new System.Windows.Shapes.Path {Data=Geometry.Parse("M4,8 L7,11 L12,5"),Stroke=Brushes.White,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round});return mark;}
 internal static TextBlock Text(string text,bool secondary=false){return new TextBlock {Text=text,FontSize=14,LineHeight=21,TextWrapping=TextWrapping.Wrap,Foreground=secondary?new SolidColorBrush(Color.FromRgb(169,178,192)):Brushes.White,Margin=new Thickness(0,0,0,14)};}
 internal static TextBlock LinkRow(string label,string text,string target,Action<Exception> failed){var row=Text(label+": ",true);var link=new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(text+" ↗")){Foreground=new SolidColorBrush(Color.FromRgb(142,190,230)),NavigateUri=new Uri(target)};link.RequestNavigate+=(s,e)=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});}catch(Exception error){failed(error);}e.Handled=true;};row.Inlines.Add(link);return row;}
 internal static Button HeaderPill(){var button=Button("");button.Height=30;button.MinWidth=0;button.Padding=new Thickness(12,0,12,0);button.Margin=new Thickness(0);button.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border CornerRadius='15' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");return button;}
 internal static Button Button(string label,bool outlined=false){var button=new Button {Content=label,ContentTemplateSelector=ActionLabelSelector.Instance,MinWidth=96,Height=40,FontSize=13,Padding=new Thickness(14,0,14,0),Margin=new Thickness(0,0,8,8),HorizontalAlignment=HorizontalAlignment.Left,Background=new SolidColorBrush(Color.FromRgb(45,50,58))};button.SetResourceReference(Control.BackgroundProperty,"ControlSurface");button.SetResourceReference(Control.BorderBrushProperty,"SurfaceBorder");button.BorderThickness=new Thickness(outlined?1:0);return button;}

 internal static Button ActionMenu(params Button[] actions){
  var button=Button("⋯");button.MinWidth=40;button.ToolTip=Locale.T("추가 작업");System.Windows.Automation.AutomationProperties.SetName(button,Locale.T("추가 작업"));
  var menu=Menu();menu.PlacementTarget=button;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;button.ContextMenu=menu;
  foreach(var action in actions){var item=new MenuItem{Header=action.Content};item.SetBinding(MenuItem.IsEnabledProperty,new System.Windows.Data.Binding("IsEnabled"){Source=action});item.Click+=(s,e)=>action.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));menu.Items.Add(item);}
  button.Click+=(s,e)=>menu.IsOpen=true;return button;
 }
 internal static FrameworkElement WarningIcon(){
  var icon=new Grid{Width=18,Height=18,VerticalAlignment=VerticalAlignment.Center};
  icon.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M9,1 L17,16 H1 Z"),Fill=new SolidColorBrush(Color.FromRgb(245,166,75)),StrokeLineJoin=PenLineJoin.Round});
  icon.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M9,6 V10 M9,13 V13.1"),Stroke=new SolidColorBrush(Color.FromRgb(45,35,25)),StrokeThickness=1.9,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});return icon;
 }
 internal static FrameworkElement Attention(){
  var button=new Button{Content=WarningIcon(),Width=24,Height=24,MinWidth=0,Padding=new Thickness(0),Background=Brushes.Transparent,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
  button.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='Surface' Background='Transparent' CornerRadius='4'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#44392B'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='Background' Value='#44392B'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
  var tip=new ToolTip{Background=new SolidColorBrush(Color.FromRgb(32,35,41)),Foreground=Brushes.White,PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Right,MaxWidth=340};button.ToolTip=tip;ToolTipService.SetInitialShowDelay(button,200);button.Click+=(s,e)=>tip.IsOpen=true;button.GotKeyboardFocus+=(s,e)=>tip.IsOpen=true;button.LostKeyboardFocus+=(s,e)=>tip.IsOpen=false;System.Windows.Automation.AutomationProperties.SetName(button,Locale.T("확인 필요한 정보"));return button;
 }
 internal static void AttentionContext(FrameworkElement element,string message){var text=Text(message);text.Margin=new Thickness(0);text.MaxWidth=320;((ToolTip)element.ToolTip).Content=text;}
 internal static void PrimaryAction(Button button,bool compact=false){button.Height=compact?40:52;button.FontSize=compact?13:16;button.FontWeight=FontWeights.SemiBold;button.Background=new SolidColorBrush(Color.FromRgb(59,116,95));button.Foreground=Brushes.White;button.BorderBrush=new SolidColorBrush(Color.FromRgb(126,195,169));button.BorderThickness=new Thickness(1);}
 internal static FrameworkElement Help(string text){var tip=new ToolTip {Content=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,MaxWidth=280}};var button=new Button {Content="?",Margin=new Thickness(7,0,0,0),ToolTip=tip};button.SetResourceReference(FrameworkElement.StyleProperty,"HelpButton");System.Windows.Automation.AutomationProperties.SetName(button,Locale.T("도움말"));ToolTipService.SetPlacement(button,System.Windows.Controls.Primitives.PlacementMode.Right);tip.PlacementTarget=button;tip.Placement=System.Windows.Controls.Primitives.PlacementMode.Right;tip.Background=new SolidColorBrush(Color.FromRgb(32,35,41));tip.Foreground=Brushes.White;button.Click+=(s,e)=>tip.IsOpen=true;button.GotKeyboardFocus+=(s,e)=>tip.IsOpen=true;button.LostKeyboardFocus+=(s,e)=>tip.IsOpen=false;return button;}
 internal static FrameworkElement Heading(TextBlock title,FrameworkElement help=null,bool wrap=false){if(wrap){if(help!=null){string name=title.Text;title.Text="";title.Inlines.Add(new System.Windows.Documents.Run(name));title.Inlines.Add(new System.Windows.Documents.InlineUIContainer(help){BaselineAlignment=BaselineAlignment.Center});}return title;}var row=new StackPanel {Orientation=Orientation.Horizontal};row.Children.Add(title);if(help!=null)row.Children.Add(help);return row;}
 internal static Expander Details(string title,Func<UIElement> create){var expander=Details(title,new UIElement[0]);bool created=false;expander.Expanded+=(s,e)=>{if(e.Source==expander&&!created){var content=create();expander.Content=content;created=true;}};return expander;}
 internal static FrameworkElement Completion(string label){var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M1,6 L4.5,9.5 L11,2.5"),Width=12,Height=12,Stretch=Stretch.Uniform,Stroke=new SolidColorBrush(Color.FromRgb(141,216,191)),StrokeThickness=2,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center});row.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center});return row;}
 internal static Border SeasonHeader(TextBlock title,FrameworkElement icons,FrameworkElement timing){title.FontSize=26;title.LineHeight=34;title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,0,8);var body=new StackPanel();body.Children.Add(title);icons.Margin=new Thickness(0,0,0,8);body.Children.Add(icons);body.Children.Add(timing);return Section(Locale.T("이번 시즌"),body);}
 internal static Expander Details(string title,params UIElement[] children){UIElement content;if(children.Length==1)content=children[0];else{var body=new StackPanel();foreach(var child in children)body.Children.Add(child);content=body;}var expander=new Expander {Header=title,Content=content,Margin=new Thickness(0,0,0,16)};expander.SetResourceReference(FrameworkElement.StyleProperty,"OptionsExpander");return expander;}
 // Compact presentation of the same Details template: same vector and arrow motion.
 internal static Expander InlineDetails(string title,UIElement content){return InlineDetails(Details(title,content));}
 internal static Expander InlineDetails(string title,Func<UIElement> create){return InlineDetails(Details(title,create));}
 static Expander InlineDetails(Expander expander){
  expander.Tag="inline";int revision=0;
  RoutedEventHandler animate=(sender,args)=>{
   if(args.OriginalSource!=expander||!expander.IsLoaded)return;
   expander.ApplyTemplate();var area=expander.Template.FindName("SectionBody",expander) as Border;if(area==null)return;
   int version=++revision;double from=area.ActualHeight;bool open=expander.IsExpanded;
   area.BeginAnimation(FrameworkElement.HeightProperty,null);area.BeginAnimation(UIElement.OpacityProperty,null);area.Visibility=Visibility.Visible;area.Height=double.NaN;
   area.Measure(new Size(Math.Max(1,expander.ActualWidth),double.PositiveInfinity));double target=open?area.DesiredSize.Height:0;
   if(!SystemParameters.ClientAreaAnimation){area.Height=double.NaN;area.Opacity=1;area.Visibility=open?Visibility.Visible:Visibility.Collapsed;return;}
   var ease=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut};
   var height=new System.Windows.Media.Animation.DoubleAnimation(from,target,TimeSpan.FromMilliseconds(200)){EasingFunction=ease};
   height.Completed+=(s,e)=>{if(version!=revision)return;area.BeginAnimation(FrameworkElement.HeightProperty,null);area.Height=double.NaN;area.Visibility=expander.IsExpanded?Visibility.Visible:Visibility.Collapsed;};
   area.BeginAnimation(FrameworkElement.HeightProperty,height);area.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(open?0:1,open?1:0,TimeSpan.FromMilliseconds(160)));
  };
  expander.Expanded+=animate;expander.Collapsed+=animate;return expander;
 }
 internal static Grid Row(string label,FrameworkElement value,string help=null,bool responsive=false){var labels=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};labels.Children.Add(new TextBlock {Text=label,FontSize=13,VerticalAlignment=VerticalAlignment.Center});if(help!=null)labels.Children.Add(Help(help));return Row(labels,value,responsive);}
 internal static Grid Row(FrameworkElement labels,FrameworkElement value,bool responsive=false){var row=new Grid {Margin=new Thickness(0,0,0,16)};row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});labels.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(labels);value.VerticalAlignment=VerticalAlignment.Center;value.Margin=new Thickness(value.Margin.Left,0,value.Margin.Right,0);Grid.SetColumn(value,1);row.Children.Add(value);
  if(responsive){
   row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});bool? stacked=null;
   row.SizeChanged+=(s,e)=>{labels.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));value.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));bool next=e.NewSize.Width<labels.DesiredSize.Width-labels.Margin.Left-labels.Margin.Right+value.DesiredSize.Width-value.Margin.Left-value.Margin.Right+16;if(stacked==next)return;stacked=next;Grid.SetColumnSpan(labels,next?2:1);Grid.SetColumn(value,next?0:1);Grid.SetColumnSpan(value,next?2:1);Grid.SetRow(value,next?1:0);labels.Margin=new Thickness(0,0,next?0:16,next?8:0);value.HorizontalAlignment=HorizontalAlignment.Right;};
  }
  return row;}
 internal static FrameworkElement InputField(string label,Control input){var field=new StackPanel {Margin=new Thickness(0,0,0,16)};field.Children.Add(new TextBlock {Text=label,FontSize=13,Margin=new Thickness(0,0,0,8)});input.HorizontalAlignment=HorizontalAlignment.Stretch;input.MinHeight=Math.Max(36,input.MinHeight);input.FontSize=13;if(input is ComboBox)input.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");if(input is PasswordBox){input.Background=new SolidColorBrush(Color.FromRgb(17,19,21));input.Foreground=Brushes.White;input.BorderBrush=new SolidColorBrush(Color.FromRgb(66,69,75));input.Padding=new Thickness(12,9,12,9);}field.Children.Add(input);return field;}
 internal static Grid FooterActions(int[] weights,params Button[] buttons){if(weights.Length!=buttons.Length)throw new ArgumentException("Action weights mismatch");var row=new Grid();for(int i=0;i<buttons.Length;i++){row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(weights[i],GridUnitType.Star)});var button=buttons[i];button.HorizontalAlignment=HorizontalAlignment.Stretch;button.MinWidth=0;button.Height=40;button.Padding=new Thickness(2,0,2,0);button.Margin=new Thickness(i==0?0:4,0,i==buttons.Length-1?0:4,0);Grid.SetColumn(button,i);row.Children.Add(button);}return row;}
 internal static FrameworkElement Actions(params Button[] buttons){var group=new ActionGrid {Margin=new Thickness(0,0,0,12)};foreach(var button in buttons){button.HorizontalAlignment=HorizontalAlignment.Stretch;button.MinWidth=0;group.Children.Add(button);}return group;}
 sealed class ActionLabelSelector:DataTemplateSelector {
  internal static readonly ActionLabelSelector Instance=new ActionLabelSelector();
  readonly DataTemplate label=CreateLabel();
  static DataTemplate CreateLabel(){var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding());text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Center);return new DataTemplate{VisualTree=text};}
  public override DataTemplate SelectTemplate(object item,DependencyObject container){return item is string?label:null;}
 }
 sealed class ActionGrid:System.Windows.Controls.Primitives.UniformGrid {
  protected override Size MeasureOverride(Size available){
   double minimum=0;
   foreach(FrameworkElement child in Children){child.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));minimum=Math.Max(minimum,child.DesiredSize.Width-child.Margin.Left-child.Margin.Right);}
   int columns=Children.Count;
   if(!double.IsInfinity(available.Width)&&minimum>0)columns=Math.Min(columns,Math.Max(1,(int)Math.Floor((available.Width+8)/(minimum+8))));
   // Balance rows so four actions never leave a lone fourth button.
   int rows=columns==0?1:(int)Math.Ceiling((double)Children.Count/Math.Max(1,columns));
   Columns=Math.Max(1,(int)Math.Ceiling((double)Children.Count/rows));
   for(int i=0;i<Children.Count;i++){var child=(FrameworkElement)Children[i];child.Margin=new Thickness(i%Columns==0?0:4,0,i%Columns==Columns-1?0:4,i/Columns<(Children.Count-1)/Columns?8:0);}
   return base.MeasureOverride(available);
  }
 }
 internal static Border Section(string heading,params UIElement[] children){return SectionHelp(heading,null,children);}
 internal static Border SectionHelp(string heading,string help,params UIElement[] children){var body=new StackPanel();var title=new TextBlock {Text=heading,TextWrapping=TextWrapping.Wrap};title.SetResourceReference(FrameworkElement.StyleProperty,"SectionHeading");if(help==null){if(!string.IsNullOrEmpty(heading))body.Children.Add(title);}else{title.Margin=new Thickness(0);var header=Heading(title,Help(help),wrap:true);header.Margin=new Thickness(0,0,0,16);body.Children.Add(header);}foreach(var child in children)body.Children.Add(child);var card=new Border {Child=body};card.SetResourceReference(FrameworkElement.StyleProperty,"SectionCard");return card;}
 internal static ListBox CardList(){var list=new ListBox{Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};VirtualizingStackPanel.SetIsVirtualizing(list,true);VirtualizingStackPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);VirtualizingPanel.SetScrollUnit(list,ScrollUnit.Pixel);ScrollViewer.SetCanContentScroll(list,true);ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Visible);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);var style=new Style(typeof(ListBoxItem));style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));style.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'><ContentPresenter HorizontalAlignment='Stretch'/></ControlTemplate>")));list.ItemContainerStyle=style;return list;}
 internal static Border ItemCard(string name,FrameworkElement action,string help=null,UIElement detail=null){
  var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  var label=new Grid{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};label.ColumnDefinitions.Add(new ColumnDefinition());label.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  label.Children.Add(new TextBlock{Text=name,FontSize=14,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center});
  if(help!=null){var info=Help(help);Grid.SetColumn(info,1);label.Children.Add(info);}header.Children.Add(label);Grid.SetColumn(action,1);header.Children.Add(action);
  var body=new StackPanel();body.Children.Add(header);if(detail!=null){var element=detail as FrameworkElement;if(element!=null)element.Margin=new Thickness(0,12,0,0);body.Children.Add(detail);}
  var card=new Border{Child=body};card.SetResourceReference(FrameworkElement.StyleProperty,"SectionCard");return card;
 }
 internal static void Detach(FrameworkElement element){if(element.Parent is Border)((Border)element.Parent).Child=null;else if(element.Parent is Panel)((Panel)element.Parent).Children.Remove(element);}
 internal static Border ScrollContent(UIElement content){return new Border{Padding=new Thickness(0,0,14,0),Child=content};}
 internal static Grid Shell(TextBlock title,UIElement body,UIElement footer,Action close,bool scrollBody=true,TextBlock helpSource=null,Action back=null){var root=new Grid {Margin=new Thickness(20,24,20,24)};root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});var header=new DockPanel {Margin=new Thickness(0,0,0,20)};var exit=new Button();exit.SetResourceReference(FrameworkElement.StyleProperty,"PanelCloseButton");exit.Click+=(s,e)=>close();DockPanel.SetDock(exit,Dock.Right);header.Children.Add(exit);if(back!=null){var previous=Button("");previous.Width=32;previous.MinWidth=0;previous.Height=32;previous.Padding=new Thickness(0);previous.Margin=new Thickness(0,0,12,0);previous.Background=Brushes.Transparent;previous.ToolTip=Locale.T("뒤로가기");System.Windows.Automation.AutomationProperties.SetName(previous,Locale.T("뒤로가기"));previous.Content=new System.Windows.Shapes.Path{Data=Geometry.Parse("M15,5 L8,12 L15,19 M8,12 H22"),Stroke=Brushes.White,StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=24,Height=24};previous.Click+=(s,e)=>back();DockPanel.SetDock(previous,Dock.Left);header.Children.Add(previous);}FrameworkElement info=null;if(helpSource!=null){info=Help("");var helpText=DetailPanel.Mirror(helpSource);helpText.MaxWidth=280;helpText.Margin=new Thickness(0);((ToolTip)info.ToolTip).Content=helpText;}title.FontSize=21;title.FontWeight=FontWeights.SemiBold;title.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(Heading(title,info));root.Children.Add(header);var paddedBody=ScrollContent(body);var scroll=new ScrollViewer {Content=paddedBody,VerticalScrollBarVisibility=ScrollBarVisibility.Visible,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,0,16)};if(!scrollBody)scroll.Content=null;FrameworkElement bodyHost=scrollBody?(FrameworkElement)scroll:paddedBody;Grid.SetRow(bodyHost,1);root.Children.Add(bodyHost);Grid.SetRow(footer,2);root.Children.Add(footer);return root;}
}

// One secondary surface, owned by the current menu. It never starts its own worker.
// All panels at the same navigation depth are mutually exclusive.
internal static class PanelNavigation {
 sealed class NavigationState {internal Action<string> Ensure;internal Action<string> Open;internal readonly Dictionary<Border,string> Owners=new Dictionary<Border,string>();}
 static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window,NavigationState> States=new System.Runtime.CompilerServices.ConditionalWeakTable<Window,NavigationState>();
 internal static void Register(Window window,Action<string> ensure,Action<string> open=null){var state=States.GetOrCreateValue(window);state.Ensure=ensure;state.Open=open;}
 internal static void Open(Window window,string route){var state=States.GetOrCreateValue(window);if(state.Open!=null)state.Open(route);}
 internal static void Menu(Window window,string group){var state=States.GetOrCreateValue(window);CloseFeatures(window);if(state.Ensure!=null)state.Ensure(group);}
 internal static string Group(string route){
  if((route??"").StartsWith("character-build-"))return "내 계정";
  if((route??"").StartsWith("scan-"))return "내 계정";
  if(route=="Collection"||route=="Characters"||route=="Weapons"||route=="Artifacts"||route=="Materials"||route=="Achievements"||route=="Primogems"||route=="account-records")return "내 계정";
  if(route=="Graphics"||route=="GraphicsPanel"||route=="Mods"||route=="mods"||route=="Capture")return "화면·성능";
  if(route=="ExternalLinks"||route=="SettingsPage"||route=="SettingsPanel"||route=="SetupPanel"||route=="Installation")return "설정";
  if(route=="Today"||route=="goal-add"||route=="today-plan")return "목표";
  if((route??"").StartsWith("calendar"))return "캘린더";
  return "플레이";
 }
 internal static void Owner(Border panel,string route){var window=Window.GetWindow(panel);if(window!=null){var state=States.GetOrCreateValue(window);state.Owners[panel]=Group(route);if(state.Ensure!=null)state.Ensure(Group(route));}}
 internal static readonly string[] Names={"WorkspaceMenu","CompanionPage","GraphicsPanel","SettingsPanel","SetupPanel","DetailPanel"};
 internal static bool Primary(Border panel){return panel.Name=="WorkspaceMenu"||Equals(panel.Tag,"primary");}
 internal static void Activate(Border panel){
  var window=Window.GetWindow(panel);if(window==null||Array.IndexOf(Names,panel.Name)<0)return;
  bool primary=Primary(panel);
  if(!primary){var state=States.GetOrCreateValue(window);string owner;if(!state.Owners.TryGetValue(panel,out owner))owner=Group(panel.Name);if(state.Ensure!=null)state.Ensure(owner);}
  LauncherWindowLayout.Primary(window,panel,primary);
  foreach(string name in Names){var other=window.FindName(name) as Border;if(other!=null&&other!=panel&&Primary(other)==primary)DrawerMotion.Collapse(other);}
 }
 internal static void CloseFeatures(Window window){foreach(string name in Names){var panel=window.FindName(name) as Border;if(panel!=null&&!Primary(panel))DrawerMotion.Collapse(panel);}}
 internal static void Reframe(Window window,string name,string title,Action close){
  var host=(Border)window.FindName(name);var old=(Grid)host.Child;var scroll=(ScrollViewer)old.Children[1];var body=(FrameworkElement)scroll.Content;scroll.Content=null;body.Margin=new Thickness(0);var footer=(FrameworkElement)old.Children[2];old.Children.Remove(footer);host.Child=PanelUi.Shell(new TextBlock{Text=Locale.T(title)},body,footer,close);
 }
}
internal sealed class DetailPanel {
 readonly Border host;TextBlock heading=new TextBlock();readonly Border contentHost=new Border(),footerHost=new Border();
 string current;bool scrolling=true;internal Action<string> NavigateMenu;Action onClose;Action returnTo;bool hasBack;
 internal DetailPanel(Window window){host=(Border)window.FindName("DetailPanel");host.IsVisibleChanged+=(s,e)=>{if(host.Visibility!=Visibility.Visible)Reset();};}
 void Reset(){current=null;var callback=onClose;onClose=null;if(callback!=null)callback();}
 internal void Show(string key,string title,UIElement content,Action closed=null,bool scrollBody=true,Action back=null,string owner=null,UIElement footer=null){
  title=Locale.T(title);
  PanelNavigation.Owner(host,owner??key);
  bool same=current==key&&host.Visibility==Visibility.Visible;
  if(!same)Reset();
  if(contentHost.Child!=content){var element=content as FrameworkElement;if(element!=null)PanelUi.Detach(element);contentHost.Child=content;}
  if(footerHost.Child!=footer){var element=footer as FrameworkElement;if(element!=null)PanelUi.Detach(element);footerHost.Child=footer;}
  current=key;onClose=closed;returnTo=back;heading.Text=title;
  if(host.Child==null||contentHost.Parent==null||scrolling!=scrollBody||hasBack!=(back!=null)){PanelUi.Detach(contentHost);PanelUi.Detach(footerHost);heading=new TextBlock{Text=title};host.Child=PanelUi.Shell(heading,contentHost,footerHost,Hide,scrollBody,back:back==null?(Action)null:()=>{var parent=returnTo;Reset();if(parent!=null)parent();});scrolling=scrollBody;hasBack=back!=null;}
  DrawerMotion.Show(host,key);
 }
 internal bool IsShowing(string key){return current==key&&host.Visibility==Visibility.Visible;}
 internal void Hide(){Reset();DrawerMotion.Hide(host);}
 internal static TextBlock Mirror(TextBlock source){var text=PanelUi.Text("",true);text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Text"){Source=source});return text;}
}

// One discrete target, animated visual travel, and one persistence event per gesture.
internal sealed class PointerSlider : System.Windows.Controls.Slider {
 internal bool Dragging {get;private set;}
 internal bool Animating {get;private set;}
 internal int TargetIndex {get;private set;}
 internal event System.Action Committed;
 double pointerOffset;
 void MovePointer(System.Windows.Input.MouseEventArgs e){
  double width=System.Math.Max(1,ActualWidth-30);
  double raw=Minimum+System.Math.Max(0,System.Math.Min(1,(e.GetPosition(this).X-15-pointerOffset)/width))*(Maximum-Minimum);
  int target=(int)System.Math.Round(raw);if(target==TargetIndex)return;TargetIndex=target;
  double from=Value;Animating=true;BeginAnimation(ValueProperty,null);Value=target;
  var motion=new System.Windows.Media.Animation.DoubleAnimation(from,target,System.TimeSpan.FromMilliseconds(140)){EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut},FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop};
  motion.Completed+=(s,a)=>Animating=false;BeginAnimation(ValueProperty,motion);
 }
 protected override void OnPreviewMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e){
  Focus();TargetIndex=(int)System.Math.Round(Value);double center=15+(ActualWidth-30)*(Value-Minimum)/System.Math.Max(1,Maximum-Minimum);double x=e.GetPosition(this).X;pointerOffset=System.Math.Abs(x-center)<=15?x-center:0;
  Dragging=true;CaptureMouse();MovePointer(e);e.Handled=true;
 }
 protected override void OnPreviewMouseMove(System.Windows.Input.MouseEventArgs e){if(Dragging){MovePointer(e);e.Handled=true;}else base.OnPreviewMouseMove(e);}
 protected override void OnPreviewMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e){if(!Dragging){base.OnPreviewMouseLeftButtonUp(e);return;}MovePointer(e);ReleaseMouseCapture();e.Handled=true;}
 protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e){base.OnLostMouseCapture(e);if(!Dragging)return;Dragging=false;if(Committed!=null)Committed();}
}

// Shared window geometry and the single narrow/wide detail layout rule.
internal sealed class LauncherWindowLayout {
 readonly Window window;bool ready;WindowState lastState=WindowState.Normal;
 internal static double NavigationWidth(double width){return width>=1380?320:240;}
 internal const double HeaderHeight=40;
 internal const double ChatTop=HeaderHeight+14;
 internal static bool Compact(Window window){return window.ActualWidth<1180;}
 internal static void Primary(Window window,Border panel,bool primary){panel.Tag=primary?"primary":"feature";Position(window,panel);}
 static void Position(Window window,Border panel){bool primary=PanelNavigation.Primary(panel);panel.Height=double.NaN;panel.Background=new SolidColorBrush(Color.FromRgb(27,29,34));panel.BorderBrush=new SolidColorBrush(Color.FromRgb(54,56,62));panel.BorderThickness=primary?new Thickness(0,0,1,0):new Thickness(1);panel.Width=primary?NavigationWidth(window.ActualWidth):double.NaN;panel.HorizontalAlignment=primary?HorizontalAlignment.Left:HorizontalAlignment.Stretch;panel.Margin=primary?new Thickness(0,HeaderHeight,0,0):new Thickness(Compact(window)?12:NavigationWidth(window.ActualWidth)+12,HeaderHeight,12,12);panel.CornerRadius=primary?new CornerRadius(0):PanelUi.Corners;Panel.SetZIndex(panel,primary?4:5);}
 internal LauncherWindowLayout(Window window){
  this.window=window;
  new WindowCaptionControls(window);
  var surface=(Border)window.Content;surface.Background=window.Background;window.Background=Brushes.Transparent;
  foreach(string name in new[]{"Home","ToggleHomeChat","HeaderIndicators"})System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome((UIElement)window.FindName(name),true);
  window.SourceInitialized+=(s,e)=>{System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(window).Handle).AddHook(WindowMessage);Restore();};
  window.SizeChanged+=(s,e)=>Layout();
  window.StateChanged+=(s,e)=>{if(window.WindowState!=WindowState.Minimized)lastState=window.WindowState;};
  window.Closing+=(s,e)=>{if(!ready||e.Cancel)return;var bounds=window.WindowState==WindowState.Normal?new Rect(window.Left,window.Top,window.Width,window.Height):window.RestoreBounds;if(bounds.IsEmpty)return;AppPreferences.Set("windowPlacement",new {left=bounds.Left,top=bounds.Top,width=bounds.Width,height=bounds.Height,maximized=lastState==WindowState.Maximized});};
 }
 void Restore(){
  try{object raw;var prefs=AppPreferences.Read();if(prefs.TryGetValue("windowPlacement",out raw)){var data=CodexChat.Map(raw);double left=Convert.ToDouble(data["left"]),top=Convert.ToDouble(data["top"]),width=Convert.ToDouble(data["width"]),height=Convert.ToDouble(data["height"]);var source=System.Windows.PresentationSource.FromVisual(window);var toDevice=source.CompositionTarget.TransformToDevice;var fromDevice=source.CompositionTarget.TransformFromDevice;var point=toDevice.Transform(new Point(left,top));var screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X,(int)point.Y));var work=screen.WorkingArea;var origin=fromDevice.Transform(new Point(work.Left,work.Top));var end=fromDevice.Transform(new Point(work.Right,work.Bottom));window.MinWidth=System.Math.Min(780,end.X-origin.X);window.MinHeight=System.Math.Min(600,end.Y-origin.Y);window.Width=System.Math.Max(window.MinWidth,System.Math.Min(width,end.X-origin.X));window.Height=System.Math.Max(window.MinHeight,System.Math.Min(height,end.Y-origin.Y));window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=System.Math.Max(origin.X,System.Math.Min(left,end.X-window.Width));window.Top=System.Math.Max(origin.Y,System.Math.Min(top,end.Y-window.Height));if(data.ContainsKey("maximized")&&Equals(data["maximized"],true))window.WindowState=WindowState.Maximized;}}catch{/* A stale placement must not prevent opening the launcher. */}ready=true;Layout();
 }
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct NativePoint {public int X,Y;}
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct MinMax {public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
 IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){if(message==0x24){var info=(MinMax)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam,typeof(MinMax));var screen=System.Windows.Forms.Screen.FromHandle(hwnd);var work=screen.WorkingArea;var bounds=screen.Bounds;info.MaxPosition.X=work.Left-bounds.Left;info.MaxPosition.Y=work.Top-bounds.Top;info.MaxSize.X=work.Width;info.MaxSize.Y=work.Height;System.Runtime.InteropServices.Marshal.StructureToPtr(info,lParam,false);handled=true;}return IntPtr.Zero;}
 bool? compact;double navigationWidth;
 void Layout(){
  bool next=Compact(window);double width=NavigationWidth(window.ActualWidth);if(compact==next&&navigationWidth==width)return;navigationWidth=width;
  bool animate=compact.HasValue&&SystemParameters.ClientAreaAnimation;compact=next;
  foreach(string name in PanelNavigation.Names){
   var panel=window.FindName(name) as Border;if(panel==null)continue;
   var from=panel.Margin;panel.BeginAnimation(FrameworkElement.MarginProperty,null);Position(window,panel);
   if(animate&&panel.IsVisible&&!PanelNavigation.Primary(panel))panel.BeginAnimation(FrameworkElement.MarginProperty,new System.Windows.Media.Animation.ThicknessAnimation(from,panel.Margin,System.TimeSpan.FromMilliseconds(180)){EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut},FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop});
  }
 }
}




// Visible only while work is running or when the user needs an error.
internal sealed class PanelWorkIndicator {
 internal readonly StackPanel View=new StackPanel{Visibility=Visibility.Collapsed,Margin=new Thickness(0,12,0,0)};
 readonly TextBlock text=PanelUi.Text("",true);readonly ProgressBar bar=new ProgressBar{BorderThickness=new Thickness(0),BorderBrush=Brushes.Transparent,Height=4,Minimum=0,Maximum=1,Margin=new Thickness(0,8,0,0),Foreground=new SolidColorBrush(Color.FromRgb(111,201,171)),Background=new SolidColorBrush(Color.FromRgb(49,53,61))};
 readonly System.Diagnostics.Stopwatch elapsed=new System.Diagnostics.Stopwatch();readonly System.Windows.Threading.DispatcherTimer timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
 string stage="";long downloaded,total;bool running;
 readonly bool reserved;
 internal PanelWorkIndicator(bool reserveSpace=false){reserved=reserveSpace;if(reserved){View.Height=30;View.Margin=new Thickness(0);View.Visibility=Visibility.Hidden;text.FontSize=12;text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;bar.Margin=new Thickness(0,5,0,0);} text.Margin=new Thickness(0);View.Children.Add(text);View.Children.Add(bar);timer.Tick+=(s,e)=>Refresh();View.Unloaded+=(s,e)=>timer.Stop();View.IsVisibleChanged+=(s,e)=>{if(View.IsVisible&&running)timer.Start();else timer.Stop();};}
 internal void Begin(string message){running=true;elapsed.Restart();stage=message;downloaded=total=0;View.Visibility=Visibility.Visible;bar.Visibility=Visibility.Visible;bar.IsIndeterminate=SystemParameters.ClientAreaAnimation;Refresh();if(View.IsVisible)timer.Start();}
 internal void Update(string message,long bytes=0,long length=0){if(!running)return;stage=message;downloaded=bytes;total=length;bar.IsIndeterminate=length<=0&&SystemParameters.ClientAreaAnimation;if(length>0)bar.Value=Math.Min(1,(double)bytes/length);Refresh();}
 void Refresh(){text.Text=Locale.T(stage);if(total>0)text.Text+="  "+Math.Min(100,(int)(100.0*downloaded/total))+"%";if(elapsed.Elapsed.TotalSeconds>=5)text.Text+=" · "+Locale.T("{0}초 경과").Replace("{0}",((int)elapsed.Elapsed.TotalSeconds).ToString());}
 internal void Finish(string error=null){running=false;elapsed.Stop();timer.Stop();bar.IsIndeterminate=false;bar.Visibility=Visibility.Collapsed;text.Text=error==null?"":Locale.T(error);text.ToolTip=error==null?null:new TextBlock{Text=Locale.T(error),TextWrapping=TextWrapping.Wrap,MaxWidth=320};View.Visibility=error==null?(reserved?Visibility.Hidden:Visibility.Collapsed):Visibility.Visible;}
}

// Inventory symbols follow one weight and silhouette, inspired by the game's menu.
internal static class GameUiIcons {
 internal static ImageSource Image(string page,bool gold=false){var box=Create(page,24,gold) as Viewbox;if(box==null)return null;var group=new DrawingGroup();foreach(System.Windows.Shapes.Path shape in ((Canvas)box.Child).Children)group.Children.Add(new GeometryDrawing(shape.Fill,shape.Stroke==null?null:new Pen(shape.Stroke,shape.StrokeThickness),shape.Data));var image=new System.Windows.Media.DrawingImage(group);image.Freeze();return image;}
 internal static FrameworkElement Create(string page,double size=24,bool gold=false){
  string shape,detail="";switch(page){
   case "Characters":shape="M8,7 A4,4 0 1 0 16,7 A4,4 0 1 0 8,7 M4,21 C4,15 8,13 12,13 C16,13 20,15 20,21 Z";detail="M8,14 L12,18 L16,14";break;
   case "Weapons":shape="M5,17 L16,3 L21,2 L20,7 L9,20 Z M3,14 L12,21 L11,23 L2,16 Z";detail="M4,22 L7,18 M9,16 L18,5";break;
   case "Artifacts":shape="M12,10 C4,5 9,0 12,4 C16,0 21,5 12,10 M14,11 C17,3 25,8 20,11 C26,16 20,20 14,11 M13,14 C21,16 17,24 13,20 C8,25 3,19 13,14 M9,13 C4,22 -1,15 4,12 C0,6 7,2 9,13 M10,12 A2,2 0 1 0 14,12 A2,2 0 1 0 10,12";break;
   case "Materials":shape="M5,8 L9,3 L13,8 L11,18 L7,18 Z M13,12 L18,6 L22,11 L19,20 L13,20 Z M2,16 L5,13 L8,21 L2,21 Z";detail="M9,4 V15 M18,8 L16,18";break;
   case "Achievements":shape="M12,2 L14,7 L19,5 L17,10 L22,12 L17,14 L19,19 L14,17 L12,22 L10,17 L5,19 L7,14 L2,12 L7,10 L5,5 L10,7 Z";detail="M12,7 L16,12 L12,17 L8,12 Z";break;
   case "Primogems":shape="M12,2 L16,8 L22,12 L16,16 L12,22 L8,16 L2,12 L8,8 Z";detail="M12,5 V19 M5,12 H19 M8,8 L16,16 M16,8 L8,16";break;
   default:return null;
  }
  var color=new SolidColorBrush(gold?Color.FromRgb(213,190,139):Color.FromRgb(193,196,203));var canvas=new Canvas{Width=24,Height=24};canvas.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(shape),Fill=color,Stroke=color,StrokeThickness=.65,StrokeLineJoin=PenLineJoin.Round});if(detail.Length>0)canvas.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(detail),Stroke=new SolidColorBrush(Color.FromRgb(47,54,65)),StrokeThickness=1.2,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});return new Viewbox{Child=canvas,Width=size,Height=size};
 }
}
