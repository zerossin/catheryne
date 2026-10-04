using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Covers are presentation data, kept outside the executable mod projection.
internal static class ModArtwork {
 static readonly SemaphoreSlim downloads=new SemaphoreSlim(2);
 static readonly object cacheWrites=new object();
 // A card crops its own view; galleries and cached source images remain intact.
 internal sealed class CardImage : Image {
  readonly Rect region;readonly Point focus;ImageSource painted;ImageBrush brush;
  internal CardImage(BuiltinMod spec){region=spec==null?new Rect(0,0,1,1):spec.PreviewRegion;focus=spec==null?new Point(.5,.5):spec.PreviewFocus;}
  protected override Size MeasureOverride(Size available){return new Size();}
  protected override Size ArrangeOverride(Size finalSize){return finalSize;}
  protected override void OnRender(DrawingContext drawing){
   if(Source==null||RenderSize.Width<=0||RenderSize.Height<=0)return;
   if(!ReferenceEquals(painted,Source)){painted=Source;brush=new ImageBrush(Source){Viewbox=region,ViewboxUnits=BrushMappingMode.RelativeToBoundingBox,Stretch=Stretch.Fill};brush.Freeze();}
   var bounds=CoverBounds(new Size(Source.Width*region.Width,Source.Height*region.Height),RenderSize,focus);
   drawing.PushClip(new RectangleGeometry(new Rect(RenderSize)));drawing.DrawRectangle(brush,null,bounds);drawing.Pop();
  }
 }
 internal static Rect CoverBounds(Size source,Size viewport,Point focus){
  if(source.Width<=0||source.Height<=0||viewport.Width<=0||viewport.Height<=0)return Rect.Empty;
  double scale=Math.Max(viewport.Width/source.Width,viewport.Height/source.Height),width=source.Width*scale,height=source.Height*scale;
  double x=Math.Max(viewport.Width-width,Math.Min(0,viewport.Width/2-width*focus.X)),y=Math.Max(viewport.Height-height,Math.Min(0,viewport.Height/2-height*focus.Y));
  return new Rect(x,y,width,height);
 }
 internal static string Folder(string root){return Path.Combine(root,"mods","covers");}
 static BitmapImage Decode(byte[] bytes,int edge=768){
  if(bytes.Length>4*1024*1024)throw new InvalidDataException("대표 이미지는 4MB 이하로 선택해 주세요.");
  int width,height;using(var stream=new MemoryStream(bytes)){var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnDemand);width=decoder.Frames[0].PixelWidth;height=decoder.Frames[0].PixelHeight;}
  if(width<=0||height<=0||width>16384||height>16384||(long)width*height>64L*1024*1024)throw new InvalidDataException("대표 이미지의 크기가 너무 큽니다.");
  var bitmap=new BitmapImage();using(var stream=new MemoryStream(bytes)){bitmap.BeginInit();bitmap.StreamSource=stream;if(width>=height)bitmap.DecodePixelWidth=Math.Min(edge,width);else bitmap.DecodePixelHeight=Math.Min(edge,height);bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.EndInit();}bitmap.Freeze();return bitmap;
 }
 internal static string Store(string root,string id,string source){
  if(new FileInfo(source).Length>4*1024*1024)throw new InvalidDataException("대표 이미지는 4MB 이하로 선택해 주세요.");var bytes=File.ReadAllBytes(source);var image=Decode(bytes);string folder=Folder(root);Directory.CreateDirectory(folder);string name=id+".png",target=Path.Combine(folder,name),part=target+"."+Guid.NewGuid().ToString("N");
  try{var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(part))encoder.Save(output);AtomicFile.Write(target,File.ReadAllBytes(part));}finally{if(File.Exists(part))File.Delete(part);}return name;
 }
 internal static string Local(ManagedMod item,ModManager manager){
  if(!string.IsNullOrEmpty(item.Cover)&&item.Cover==item.Id+".png"){string cover=Path.Combine(Folder(manager.Root),item.Cover);if(File.Exists(cover))return cover;}
  string folder=ModManager.Source(item,manager);
  if(!Directory.Exists(folder))return null;
  return ModManager.SafeFiles(folder).Take(20000).Where(x=>new[]{".png",".jpg",".jpeg"}.Contains(Path.GetExtension(x).ToLowerInvariant())&&new[]{"preview","cover","thumbnail","screenshot"}.Contains(Path.GetFileNameWithoutExtension(x).ToLowerInvariant())&&(File.GetAttributes(x)&FileAttributes.ReparsePoint)==0).OrderBy(x=>x.Length).FirstOrDefault();
 }
 internal static async void Present(Image view,ManagedMod item,ModManager manager,string preview=null,int decodeEdge=768){
  object requestId=new object();view.Tag=requestId;try{var bitmap=await Task.Run<BitmapSource>(async()=>{
   await downloads.WaitAsync();try{
    string path=preview==null?Local(item,manager):null;if(path!=null){if(new FileInfo(path).Length>4*1024*1024)return null;return Decode(File.ReadAllBytes(path),decodeEdge);}
    var spec=ModBuiltins.Catalog.FirstOrDefault(x=>x.Key==item.Builtin);string source=preview??(spec==null?null:spec.Preview);if(string.IsNullOrEmpty(source))return null;
    // The reviewed dark-loading DDS is a uniform black texture; no unrelated HUD screenshot.
    if(source.StartsWith("solid:",StringComparison.Ordinal)){var color=(Color)ColorConverter.ConvertFromString(source.Substring(6));var swatch=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{color.B,color.G,color.R,color.A},4);swatch.Freeze();return swatch;}
    var url=new Uri(source);if(url.Scheme!="https"||url.Host!="images.gamebanana.com"||!url.IsDefaultPort||url.UserInfo.Length>0)return null;
    string key;using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(url.AbsoluteUri))).Replace("-","");
    string cache=Path.Combine(manager.Root,"cache","mod-covers",key+".img");byte[] bytes;
    if(File.Exists(cache))bytes=File.ReadAllBytes(cache);else{var request=HttpTransport.Create(url.AbsoluteUri);request.AllowAutoRedirect=false;request.Timeout=12000;request.ReadWriteTimeout=12000;using(var response=request.GetResponse())using(var stream=response.GetResponseStream())using(var output=new MemoryStream()){var buffer=new byte[8192];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){if(output.Length+count>4*1024*1024)throw new InvalidDataException("Cover too large");output.Write(buffer,0,count);}bytes=output.ToArray();}}
    var decoded=Decode(bytes,decodeEdge);lock(cacheWrites){if(!File.Exists(cache)){Directory.CreateDirectory(Path.GetDirectoryName(cache));AtomicFile.Write(cache,bytes);}}return decoded;
   }finally{downloads.Release();}
  });if(bitmap!=null&&ReferenceEquals(view.Tag,requestId))view.Source=bitmap;
  }catch(Exception error){System.Diagnostics.Trace.TraceWarning("Mod cover: "+error.Message);}
 }
}
