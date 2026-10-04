using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// One bounded decoder for cached public game images, including WebP enemies.
internal static class GameImage {
 [DllImport("Catheryne.Images.dll",CallingConvention=CallingConvention.Cdecl)]static extern int ImageInfo(byte[] data,UIntPtr size,out int width,out int height);
 [DllImport("Catheryne.Images.dll",CallingConvention=CallingConvention.Cdecl)]static extern int ImageDecode(byte[] data,UIntPtr size,byte[] output,UIntPtr capacity,int stride);
 internal static BitmapSource Decode(byte[] bytes,int width){
  if(bytes.Length>2*1024*1024)throw new InvalidDataException("Game image too large");
  if(bytes.Length>=12&&bytes[0]==82&&bytes[1]==73&&bytes[2]==70&&bytes[3]==70&&bytes[8]==87&&bytes[9]==69&&bytes[10]==66&&bytes[11]==80){
   int w,h;if(ImageInfo(bytes,(UIntPtr)bytes.Length,out w,out h)==0)throw new InvalidDataException("Invalid WebP image");int stride=checked(w*4);var pixels=new byte[checked(stride*h)];if(ImageDecode(bytes,(UIntPtr)bytes.Length,pixels,(UIntPtr)pixels.Length,stride)==0)throw new InvalidDataException("WebP decode failed");
   BitmapSource bitmap=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,stride);bitmap.Freeze();if(w>width){bitmap=new TransformedBitmap(bitmap,new ScaleTransform((double)width/w,(double)width/w));bitmap.Freeze();}return bitmap;
  }
  var image=new BitmapImage();using(var stream=new MemoryStream(bytes,0,bytes.Length,false,true)){image.BeginInit();image.StreamSource=stream;image.DecodePixelWidth=width;image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();}return image;
 }
}
