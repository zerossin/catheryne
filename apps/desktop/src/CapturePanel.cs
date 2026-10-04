using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class CapturePanel {
 internal static void Show(Window window,DetailPanel detail,CaptureController controller,Action<string> attach){
  var store=new CaptureStore(Setup.DataFolder);var body=new StackPanel();body.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,Brushes.White);var status=PanelUi.Text(controller.Error??"",true);status.Margin=new Thickness(0,0,0,16);
  Action<string> setStatus=text=>{status.Text=text;status.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;};setStatus(status.Text);
  var capture=PanelUi.Button(Locale.T("게임 화면 캡처"));PanelUi.PrimaryAction(capture,true);var folder=PanelUi.Button(Locale.T("저장 폴더 열기"));
  var actions=PanelUi.Actions(capture);actions.Margin=new Thickness(0,0,0,16);body.Children.Add(actions);body.Children.Add(status);
  var settings=new StackPanel();var enabled=PanelUi.Toggle(controller.Enabled,Locale.T("캡처 단축키"));var shortcut=PanelUi.Button(controller.Shortcut);shortcut.MinWidth=180;shortcut.Margin=new Thickness(12,0,0,0);shortcut.VerticalAlignment=VerticalAlignment.Center;enabled.Margin=new Thickness(0);shortcut.ToolTip=Locale.T("새 단축키를 눌러 변경합니다.");
  var hotkey=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};hotkey.Children.Add(enabled);hotkey.Children.Add(shortcut);settings.Children.Add(PanelUi.Row(Locale.T("캡처 단축키"),hotkey,responsive:true));
  bool binding=false;
  Action apply=()=>{if(binding)return;try{controller.Configure(enabled.IsChecked==true,Convert.ToString(shortcut.Content));setStatus("");}catch(Exception error){setStatus(error.Message);binding=true;enabled.IsChecked=controller.Enabled;shortcut.Content=controller.Shortcut;binding=false;}};
  enabled.Checked+=(s,e)=>apply();enabled.Unchecked+=(s,e)=>apply();
  shortcut.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Tab)return;e.Handled=true;string value=CaptureShortcut.FromKey(e.Key==Key.System?e.SystemKey:e.Key,Keyboard.Modifiers);uint modifiers,key;if(!CaptureShortcut.Parse(value,out modifiers,out key))return;shortcut.Content=value;apply();};
  var location=PanelUi.Button(Locale.T("저장 위치 변경"));var path=PanelUi.Text(store.Folder,true);path.Margin=new Thickness(0);path.TextTrimming=TextTrimming.CharacterEllipsis;path.TextWrapping=TextWrapping.NoWrap;path.ToolTip=store.Folder;var storageActions=PanelUi.FooterActions(new[]{1,1},folder,location);storageActions.Width=240;var storage=PanelUi.Row(Locale.T("저장 위치"),storageActions,responsive:true);storage.Margin=new Thickness(0,0,0,8);settings.Children.Add(storage);settings.Children.Add(path);
  location.Click+=(s,e)=>{using(var picker=new System.Windows.Forms.FolderBrowserDialog{SelectedPath=store.Folder})if(picker.ShowDialog(new WindowHandle(window))==System.Windows.Forms.DialogResult.OK){try{AppPreferences.Set("captureFolder",picker.SelectedPath);path.Text=store.Folder;path.ToolTip=path.Text;setStatus("");}catch(Exception error){setStatus(error.Message);}}};
  body.Children.Add(PanelUi.Details(Locale.T("캡처 설정"),PanelUi.SectionHelp(Locale.T("캡처 설정"),Locale.T(GameEnvironment.Selected(Setup.DataFolder)?"분리 실행 중에는 게임 창을 띄우지 않아도 캡처 단축키를 사용할 수 있습니다. HDR 화면은 SDR PNG로 저장하며, 환상극 관제 중에는 해당 채팅에도 전달됩니다.":"HDR 화면은 밝기와 하이라이트를 보정한 SDR PNG로 저장합니다. 단축키는 원신이 전면에 있고 캣서린이 실행 중일 때 동작합니다. 환상극 관제를 진행 중인 채팅에서는 캡처가 해당 채팅으로 전달됩니다. 그 외에는 저장만 합니다."),settings)));
  var gallery=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Margin=new Thickness(-6,0,-6,0)};body.Children.Add(PanelUi.Text(Locale.T("최근 캡처")));body.Children.Add(gallery);
  gallery.SizeChanged+=(s,e)=>gallery.Columns=Math.Max(1,Math.Min(4,(int)(gallery.ActualWidth/210)));
  SavedCapture selected=null;int generation=0;bool closed=false;var selection=PanelUi.Text("",true);selection.Margin=new Thickness(0,4,0,12);body.Children.Add(selection);
  var chat=PanelUi.Button(Locale.T("채팅에 첨부"));var copy=PanelUi.Button(Locale.T("이미지 복사"));var open=PanelUi.Button(Locale.T("이미지 열기"));body.Children.Add(PanelUi.Actions(chat,copy,open));
  Action<SavedCapture> select=shot=>{selected=shot;selection.Text=shot==null?"":Caption(shot);chat.IsEnabled=copy.IsEnabled=open.IsEnabled=shot!=null;foreach(var card in gallery.Children.OfType<Button>())card.Background=new SolidColorBrush(ReferenceEquals(card.Tag,shot)?Color.FromRgb(54,64,75):Color.FromRgb(34,38,45));};select(null);
  Action redraw=async()=>{
   int request=++generation;
   try{
    var shots=await Task.Run(()=>store.Recent().Take(8).ToArray());if(closed||request!=generation)return;string previous=selected==null?null:selected.Path;gallery.Children.Clear();
    if(shots.Length==0){gallery.Children.Add(PanelUi.Text(Locale.T("저장한 캡처가 없습니다."),true));select(null);return;}
    foreach(var shot in shots){var content=new StackPanel();var image=new Image{Height=116,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,0,8)};content.Children.Add(image);var name=PanelUi.Text(Caption(shot),true);name.FontSize=12;name.Margin=new Thickness(0);name.TextAlignment=TextAlignment.Center;content.Children.Add(name);
     var card=PanelUi.Button("");card.Tag=shot;card.Content=content;card.MinWidth=0;card.Height=164;card.Padding=new Thickness(10);card.HorizontalAlignment=HorizontalAlignment.Stretch;card.HorizontalContentAlignment=HorizontalAlignment.Stretch;card.Margin=new Thickness(6,0,6,12);card.ToolTip=Caption(shot);System.Windows.Automation.AutomationProperties.SetName(card,Caption(shot));card.Click+=(s,e)=>select(shot);gallery.Children.Add(card);LoadPreview(image,shot.Path);
    }select(shots.FirstOrDefault(s=>s.Path==previous)??shots[0]);
   }catch(Exception error){if(!closed&&request==generation)setStatus(error.Message);}
  };
  chat.Click+=(s,e)=>{if(selected!=null)attach(selected.Path);};copy.Click+=async(s,e)=>{if(selected==null)return;string selectedPath=selected.Path;copy.IsEnabled=false;try{var original=await Task.Run(()=>Image(selectedPath,0));if(closed)return;Clipboard.SetImage(original);setStatus(Locale.T("이미지를 복사했습니다."));}catch(Exception error){if(!closed)setStatus(error.Message);}finally{if(!closed)copy.IsEnabled=selected!=null;}};open.Click+=(s,e)=>{if(selected==null)return;try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(selected.Path){UseShellExecute=true});}catch(Exception error){setStatus(error.Message);}};
  capture.Click+=async(s,e)=>{capture.IsEnabled=false;setStatus(Locale.T("화면 캡처 중…"));try{await controller.Capture();setStatus(Locale.T("캡처를 저장했습니다."));}catch(Exception error){setStatus(error.Message);}finally{capture.IsEnabled=true;}};
  folder.Click+=(s,e)=>{try{Directory.CreateDirectory(store.Folder);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(store.Folder){UseShellExecute=true});}catch(Exception error){setStatus(error.Message);}};
  location.Click+=(s,e)=>redraw();Action<SavedCapture> changed=shot=>redraw();controller.Captured+=changed;
  try{redraw();}catch(Exception error){setStatus(error.Message);}
  detail.Show("Capture",Locale.T("캡처"),body,()=>{closed=true;++generation;controller.Captured-=changed;},owner:"Capture");
 }
 static string Caption(SavedCapture shot){DateTime created;return DateTime.TryParse(shot.Created,out created)?created.ToLocalTime().ToString("MM-dd HH:mm:ss"):Path.GetFileName(shot.Path);}
 static BitmapSource Image(string path,int width){var bitmap=new BitmapImage();using(var stream=File.OpenRead(path)){bitmap.BeginInit();bitmap.StreamSource=stream;bitmap.DecodePixelWidth=width;bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.EndInit();bitmap.Freeze();}return bitmap;}
 static async void LoadPreview(Image image,string path){try{var source=await Task.Run(async()=>{var file=new FileInfo(path);string key="capture:"+file.FullName+":"+file.Length+":"+file.LastWriteTimeUtc.Ticks;return await BitmapCache.Shared.Get(key,()=>Task.Run(()=>Image(path,400)));});image.Source=source;}catch(Exception){image.ToolTip=Locale.T("이미지를 읽지 못했습니다.");}}
 sealed class WindowHandle : System.Windows.Forms.IWin32Window {readonly Window window;internal WindowHandle(Window window){this.window=window;}public IntPtr Handle{get{return new System.Windows.Interop.WindowInteropHelper(window).Handle;}}}
}
