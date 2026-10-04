using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

// Frozen decoded images only; originals and integrity checks remain in their loaders.
internal sealed class BitmapCache {
 internal static readonly BitmapCache Shared=new BitmapCache(16*1024*1024,128);
 sealed class Entry {internal string Key;internal long Used,Bytes;internal Lazy<Task<BitmapSource>> Work;}
 readonly object sync=new object();
 readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
 readonly long capacity;readonly int limit;long clock,bytes;
 internal BitmapCache(long capacity,int limit){if(capacity<1||limit<1)throw new ArgumentOutOfRangeException();this.capacity=capacity;this.limit=limit;}
 internal Task<BitmapSource> Get(string key,Func<Task<BitmapSource>> load){
  Entry entry;lock(sync){if(!entries.TryGetValue(key,out entry)){entry=new Entry{Key=key};var pending=entry;entry.Work=new Lazy<Task<BitmapSource>>(()=>Load(pending,load),LazyThreadSafetyMode.ExecutionAndPublication);entries.Add(key,entry);}entry.Used=++clock;}
  return entry.Work.Value;
 }
 async Task<BitmapSource> Load(Entry entry,Func<Task<BitmapSource>> load){
  try{
   var bitmap=await load().ConfigureAwait(false);if(bitmap!=null&&!bitmap.IsFrozen)throw new InvalidOperationException("Cached images must be frozen.");
   lock(sync){
    if(bitmap==null){Remove(entry);return null;}
    long size=Cost(bitmap);
    if(size>capacity){Remove(entry);return bitmap;}
    entry.Bytes=size;bytes+=size;
    while(bytes>capacity||entries.Count>limit){var oldest=entries.Values.Where(e=>e.Bytes>0).OrderBy(e=>e.Used).FirstOrDefault();if(oldest==null)break;Remove(oldest);}
   }
   return bitmap;
  }catch{lock(sync)Remove(entry);throw;}
 }
 static long Cost(BitmapSource bitmap){
  long size=(long)bitmap.PixelWidth*bitmap.PixelHeight*Math.Max(4,(bitmap.Format.BitsPerPixel+7)/8);
  var transformed=bitmap as TransformedBitmap;if(transformed!=null)size+=Cost(transformed.Source);
  // OnLoad releases the decoder, but BitmapImage still holds its source stream.
  var decoded=bitmap as BitmapImage;var stream=decoded==null?null:decoded.StreamSource as MemoryStream;ArraySegment<byte> encoded;
  if(stream!=null&&stream.TryGetBuffer(out encoded))size+=encoded.Count;
  return size;
 }
 void Remove(Entry entry){if(entries.Remove(entry.Key))bytes-=entry.Bytes;}
 internal long Bytes {get{lock(sync)return bytes;}}
 internal int Count {get{lock(sync)return entries.Count;}}
}
