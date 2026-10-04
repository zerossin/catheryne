using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// The same attachment retains its identity in the composer, sent input and history.
internal sealed class ChatAttachment {
 public string Name {get;set;} public string Source {get;set;} public string OriginalSource {get;set;} public bool IsImage {get;set;}
 public ChatAttachment(){}
 internal ChatAttachment(string name,string source=null,bool image=false){Name=string.IsNullOrEmpty(name)?Locale.T("이미지"):name;Source=source;OriginalSource=source;IsImage=image;}
 internal static ChatAttachment File(string path){return new ChatAttachment(Path.GetFileName(path),path,ChatAttachments.IsImage(path));}
}
internal static class ChatAttachments {
 internal const double ComposerSize=104,MessageSize=96,Gap=8;
 internal static bool IsImage(string path){return new[]{".png",".jpg",".jpeg",".webp"}.Contains(Path.GetExtension(path??"").ToLowerInvariant());}
 internal static string Validate(string file){
  if(!System.IO.File.Exists(file))throw new FileNotFoundException("첨부 파일을 찾을 수 없습니다.",file);
  if(IsImage(file))return "localImage";
  if(!new[]{".txt",".md",".json",".csv"}.Contains(Path.GetExtension(file).ToLowerInvariant()))throw new InvalidOperationException("이미지 또는 TXT, MD, JSON, CSV 파일을 첨부해 주세요.");
  if(new FileInfo(file).Length>512000)throw new InvalidOperationException("텍스트 첨부는 파일당 500KB 이하로 선택해 주세요.");return "text";
 }
 internal static bool CanPaste(IDataObject data){return data!=null&&(data.GetDataPresent(DataFormats.FileDrop,true)||data.GetDataPresent(DataFormats.Bitmap,true)||data.GetDataPresent("PNG",false));}
 internal static string[] ReadPaste(IDataObject data,string root){
  if(!CanPaste(data))return null;
  if(data.GetDataPresent(DataFormats.FileDrop,true)){var files=data.GetData(DataFormats.FileDrop,true) as string[];if(files==null)throw new InvalidOperationException(Locale.T("복사한 파일을 읽을 수 없습니다."));foreach(string file in files)Validate(file);return files;}
  var image=data.GetData(DataFormats.Bitmap,true) as BitmapSource;
  if(image==null&&data.GetDataPresent("PNG",false)){var stream=data.GetData("PNG",false) as Stream;if(stream!=null){if(stream.CanSeek)stream.Position=0;image=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);}}
  if(image==null)throw new InvalidOperationException(Locale.T("복사한 이미지를 읽을 수 없습니다."));
  if(image.PixelWidth>16384||image.PixelHeight>16384||(long)image.PixelWidth*image.PixelHeight>64000000)throw new InvalidOperationException(Locale.T("이미지가 너무 큽니다. 파일로 첨부해 주세요."));
  return new[]{Paste(image,root)};
 }
 internal static string Save(byte[] bytes,string name,string root){
  string key;using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();string folder=Path.Combine(root,"chat-images",key),saved=Path.Combine(folder,Path.GetFileName(name));Directory.CreateDirectory(folder);if(!System.IO.File.Exists(saved))AtomicFile.Write(saved,bytes);return saved;
 }
 internal static string Preserve(string path,string root){return Save(System.IO.File.ReadAllBytes(path),Path.GetFileName(path),root);}
 internal static string Paste(BitmapSource image,string root){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=new MemoryStream()){encoder.Save(stream);return Save(stream.ToArray(),"이미지.png",root);}}
 internal static List<ChatAttachment> Snapshot(object[] inputs,string root){var saved=new List<ChatAttachment>();foreach(var part in CodexChat.Items(inputs))if(CodexChat.S(part,"type")=="localImage"){string path=CodexChat.S(part,"path");saved.Add(new ChatAttachment(Path.GetFileName(path),Preserve(path,root),true){OriginalSource=path});}return saved;}
 internal static void Record(string root,string thread,string turn,List<ChatAttachment> images){if(string.IsNullOrEmpty(turn))throw new InvalidDataException("Missing chat turn");using(var db=new LocalDataService(root))db.Observe(thread,"chat-images:"+turn,images);}
 internal static List<ChatAttachment> ForTurn(string root,string thread,string turn){if(string.IsNullOrEmpty(thread)||string.IsNullOrEmpty(turn))return null;try{using(var db=new LocalDataService(root)){var row=db.Query("SELECT payload FROM observations WHERE profile="+LocalDataService.Sql(thread)+" AND kind="+LocalDataService.Sql("chat-images:"+turn)+" ORDER BY id DESC LIMIT 1").FirstOrDefault();return row==null?null:CatheryneTools.Json().Deserialize<List<ChatAttachment>>(row["payload"]);}}catch(Exception error){System.Diagnostics.Trace.TraceWarning("Chat image history: "+error.Message);return null;}}
 internal static string[] RestoreInput(object input,CodexChat.UserContent content,string root){
  var files=new List<string>();int index=0;
  foreach(var part in CodexChat.Items(input)){
   string text=CodexChat.S(part,"text");int newline=text.IndexOf('\n');
   if(index>0&&CodexChat.S(part,"type")=="text"&&text.StartsWith("Attached file: ",StringComparison.Ordinal)&&newline>15){string name=text.Substring(15,newline-15).TrimEnd('\r');if(name!=Path.GetFileName(name)||string.IsNullOrWhiteSpace(name))throw new InvalidDataException(Locale.T("요청 내용을 불러오지 못했습니다."));string saved=Save(System.Text.Encoding.UTF8.GetBytes(text.Substring(newline+1)),name,root);Validate(saved);files.Add(saved);}index++;
  }
  foreach(var image in content.Attachments.Where(a=>a.IsImage)){
   string path=image.Source;
   if(path!=null&&path.StartsWith("data:image/",StringComparison.OrdinalIgnoreCase)){using(var stream=Open(path)){var bitmap=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);path=Paste(bitmap,root);}}
   else{Uri uri;if(Uri.TryCreate(path,UriKind.Absolute,out uri)){if(!uri.IsFile)throw new InvalidDataException(Locale.T("이미지 원본을 찾을 수 없습니다."));path=uri.LocalPath;}}
   Validate(path);files.Add(Preserve(path,root));
  }
  return files.ToArray();
 }
 internal static void Inherit(string root,string source,string target){
  using(var db=new LocalDataService(root))foreach(var row in db.Query("SELECT kind,payload FROM observations WHERE profile="+LocalDataService.Sql(source)+" AND kind LIKE 'chat-images:%' ORDER BY id"))db.Observe(target,row["kind"],CatheryneTools.Json().Deserialize<List<ChatAttachment>>(row["payload"]));
 }
 static Stream Open(string source){
  if(string.IsNullOrEmpty(source))throw new FileNotFoundException(Locale.T("이미지 원본을 찾을 수 없습니다."));
  if(source.StartsWith("data:image/",StringComparison.OrdinalIgnoreCase)){int comma=source.IndexOf(',');if(comma<0||!source.Substring(0,comma).EndsWith(";base64",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException();return new MemoryStream(Convert.FromBase64String(source.Substring(comma+1)));}
  Uri uri;if(Uri.TryCreate(source,UriKind.Absolute,out uri)&&!uri.IsFile)throw new InvalidDataException(Locale.T("이 이미지의 미리보기를 불러올 수 없습니다."));
  return System.IO.File.OpenRead(uri!=null&&uri.IsFile?uri.LocalPath:source);
 }
 internal static BitmapSource Decode(string source,int maximum){
  using(var stream=Open(source)){
   var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);var frame=decoder.Frames[0];int width=frame.PixelWidth,height=frame.PixelHeight;stream.Position=0;
   var image=new BitmapImage();image.BeginInit();image.StreamSource=stream;image.CacheOption=BitmapCacheOption.OnLoad;
   if(Math.Max(width,height)>maximum){if(width>=height)image.DecodePixelWidth=maximum;else image.DecodePixelHeight=maximum;}image.EndInit();image.Freeze();return image;
  }
 }
 internal static FrameworkElement Thumbnail(ChatAttachment attachment,Window owner,bool composing,Action remove=null){
  double size=composing?ComposerSize:MessageSize;var area=new Grid{Width=size,Height=size,Margin=new Thickness(0,0,Gap,Gap)};
  var image=new Image{Stretch=Stretch.Uniform};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
  var fallback=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8)};fallback.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M2,2 H22 V22 H2 Z M3,18 L9,11 L14,16 L18,12 L22,17 M16,7 A2,2 0 1 0 20,7 A2,2 0 1 0 16,7"),Stroke=new SolidColorBrush(Color.FromRgb(160,171,186)),StrokeThickness=1.4,Width=24,Height=24,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center});fallback.Children.Add(new TextBlock{Text=attachment.Name,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center,FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(170,181,197)),Margin=new Thickness(0,6,0,0)});
  var picture=new Grid{Clip=new RectangleGeometry(new Rect(0,0,size-2,size-2),PanelUi.SurfaceRadius,PanelUi.SurfaceRadius)};picture.Children.Add(image);picture.Children.Add(fallback);
  var preview=new Button{Content=picture,Padding=new Thickness(0),Background=new SolidColorBrush(Color.FromRgb(26,30,36)),BorderBrush=new SolidColorBrush(Color.FromRgb(65,74,86)),BorderThickness=new Thickness(1),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,ToolTip=attachment.Name};
  preview.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='{DynamicResource SurfaceCorners}'><ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Stretch'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#A7B9C9'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#A7B9C9'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
  System.Windows.Automation.AutomationProperties.SetName(preview,attachment.Name);preview.Click+=(s,e)=>Show(attachment,owner);area.Children.Add(preview);
  bool started=false;area.Loaded+=async(s,e)=>{if(started)return;started=true;try{image.Source=await Task.Run(()=>Decode(attachment.Source,(int)(size*2)));fallback.Visibility=Visibility.Collapsed;}catch(Exception){preview.ToolTip=attachment.Name+"\n"+Locale.T("이 이미지의 미리보기를 불러올 수 없습니다.");}};
  if(remove!=null){var dismiss=PanelUi.Button("×");dismiss.Width=24;dismiss.Height=24;dismiss.MinWidth=0;dismiss.FontSize=18;dismiss.Padding=new Thickness(0);dismiss.Margin=new Thickness(4);dismiss.HorizontalAlignment=HorizontalAlignment.Right;dismiss.VerticalAlignment=VerticalAlignment.Top;dismiss.Background=new SolidColorBrush(Color.FromArgb(225,36,41,49));dismiss.ToolTip=Locale.T("첨부 제거");System.Windows.Automation.AutomationProperties.SetName(dismiss,Locale.T("첨부 제거"));dismiss.Click+=(s,e)=>{e.Handled=true;remove();};area.Children.Add(dismiss);}return area;
 }
 internal static FrameworkElement Composer(ChatAttachment attachment,Window owner,Action remove){if(attachment.IsImage)return Thumbnail(attachment,owner,true,remove);var chip=PanelUi.Button(attachment.Name+" ×");chip.ToolTip=attachment.Source;chip.Click+=(s,e)=>remove();return chip;}
 static async void Show(ChatAttachment attachment,Window owner){
  try{var bitmap=await Task.Run(()=>Decode(attachment.Source,2048));var image=new Image{Source=bitmap,Stretch=Stretch.Uniform,Margin=new Thickness(16)};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);var viewer=new Window{Title=attachment.Name,Content=image,Background=new SolidColorBrush(Color.FromRgb(23,26,31)),Width=Math.Min(960,SystemParameters.WorkArea.Width*.85),Height=Math.Min(760,SystemParameters.WorkArea.Height*.85),MinWidth=320,MinHeight=240,WindowStartupLocation=WindowStartupLocation.CenterOwner};if(owner!=null&&owner.IsVisible){viewer.Owner=owner;viewer.Icon=owner.Icon;}viewer.KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape)viewer.Close();};viewer.ShowDialog();}catch(Exception){MessageBox.Show(owner,Locale.T("이미지 원본을 찾을 수 없거나 표시할 수 없습니다."),Locale.T("이미지"));}
 }
}

// Both WPF drops and Explorer's native file drops feed the existing attachment reader.
// Native drops are needed when the game-control app runs above Explorer's integrity level.
internal sealed class ChatAttachmentDrop {
 const int DropFiles=0x0233,CopyGlobalData=0x0049;
 readonly Window window;readonly Border surface;readonly Func<IDataObject,bool> insert;readonly Action<Exception> error;
 readonly Brush normal;readonly Brush highlight=new SolidColorBrush(Color.FromRgb(90,164,153));
 System.Windows.Interop.HwndSource source;
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct NativePoint {public int X,Y;}
 [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern void DragAcceptFiles(IntPtr window,bool accept);
 [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern bool DragQueryPoint(IntPtr drop,out NativePoint point);
 [System.Runtime.InteropServices.DllImport("shell32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern uint DragQueryFile(IntPtr drop,uint index,System.Text.StringBuilder file,uint size);
 [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern void DragFinish(IntPtr drop);
 [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)] static extern bool ChangeWindowMessageFilterEx(IntPtr window,uint message,uint action,IntPtr change);
 internal ChatAttachmentDrop(Window window,Border surface,Func<IDataObject,bool> insert,Action<Exception> error){
  this.window=window;this.surface=surface;this.insert=insert;this.error=error;normal=surface.BorderBrush;surface.AllowDrop=true;
  surface.PreviewDragEnter+=Feedback;surface.PreviewDragOver+=Feedback;
  surface.PreviewDragLeave+=(s,e)=>Reset();surface.IsVisibleChanged+=(s,e)=>{if(!surface.IsVisible)Reset();};
  surface.PreviewDrop+=(s,e)=>{Reset();if(!Files(e.Data))return;e.Handled=true;e.Effects=DragDropEffects.None;if((e.AllowedEffects&DragDropEffects.Copy)!=0&&Receive(e.Data))e.Effects=DragDropEffects.Copy;};
  window.SourceInitialized+=(s,e)=>Ready();window.Closed+=(s,e)=>{if(source!=null){DragAcceptFiles(source.Handle,false);source.RemoveHook(Message);source=null;}};
  if(new System.Windows.Interop.WindowInteropHelper(window).Handle!=IntPtr.Zero)Ready();
 }
 static bool Files(IDataObject data){return data!=null&&data.GetDataPresent(DataFormats.FileDrop,true);}
 void Reset(){surface.BorderBrush=normal;}
 void Feedback(object sender,DragEventArgs e){
  if(!Files(e.Data)){Reset();return;}e.Handled=true;e.Effects=DragDropEffects.None;
  if((e.AllowedEffects&DragDropEffects.Copy)!=0)try{ChatAttachments.ReadPaste(e.Data,null);e.Effects=DragDropEffects.Copy;}catch(Exception){}
  surface.BorderBrush=e.Effects==DragDropEffects.Copy?highlight:normal;
 }
 bool Receive(IDataObject data){try{return insert(data);}catch(Exception issue){error(issue);return false;}}
 void Ready(){
  if(source!=null)return;var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
  source=System.Windows.Interop.HwndSource.FromHwnd(handle);source.AddHook(Message);
  // Only the two shell file-transfer messages are allowed on this window. No process-wide filter.
  bool files=ChangeWindowMessageFilterEx(handle,DropFiles,1,IntPtr.Zero),transfer=ChangeWindowMessageFilterEx(handle,CopyGlobalData,1,IntPtr.Zero);
  if(!files||!transfer)System.Diagnostics.Trace.TraceWarning("Explorer file drop filter: "+System.Runtime.InteropServices.Marshal.GetLastWin32Error());
  DragAcceptFiles(handle,true);
 }
 IntPtr Message(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
  if(message!=DropFiles)return IntPtr.Zero;handled=true;Reset();string[] files=null;Exception failure=null;
  try{
   NativePoint point;if(!DragQueryPoint(wParam,out point)||!surface.IsVisible)return IntPtr.Zero;
   var location=source.CompositionTarget.TransformFromDevice.Transform(new Point(point.X,point.Y));
   var hit=window.InputHitTest(location) as DependencyObject;if(hit==null||(hit!=surface&&!surface.IsAncestorOf(hit)))return IntPtr.Zero;
   uint count=DragQueryFile(wParam,uint.MaxValue,null,0);files=new string[checked((int)count)];
   for(uint i=0;i<count;i++){uint size=DragQueryFile(wParam,i,null,0);var path=new System.Text.StringBuilder(checked((int)size+1));DragQueryFile(wParam,i,path,(uint)path.Capacity);files[i]=path.ToString();}
  }catch(Exception issue){files=null;failure=issue;}finally{DragFinish(wParam);}
  if(failure!=null)error(failure);
  if(files!=null&&files.Length>0){var data=new DataObject();data.SetData(DataFormats.FileDrop,files);Receive(data);}return IntPtr.Zero;
 }
}
