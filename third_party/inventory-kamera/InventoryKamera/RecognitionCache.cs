using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using CatheryneScanning;
namespace InventoryKamera {
 // Exact cropped pixels only: changed stats, equipment, names, and rendering miss
 // the cache and are recognized normally. No screenshots or item identities stored.
 internal static class RecognitionCache {
  const int Limit=20000;
  static readonly object Gate=new object();
  static string FilePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"recognition-cache.json");
  static Dictionary<string,string> values;
  static int dirty;internal static int Hits,Misses;
  internal static string Key(Bitmap image,int mode,bool numbers){
   if(!ScanBridge.Active)return null;
   using(var normalized=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppArgb)){
    using(var g=Graphics.FromImage(normalized))g.DrawImageUnscaled(image,0,0);
    var data=normalized.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
    try{var pixels=new byte[data.Stride*data.Height];Marshal.Copy(data.Scan0,pixels,0,pixels.Length);using(var sha=SHA256.Create())return image.Width+"x"+image.Height+":"+mode+":"+numbers+":"+BitConverter.ToString(sha.ComputeHash(pixels));}
    finally{normalized.UnlockBits(data);}
   }
  }
  internal static bool TryGet(string key,out string value){value=null;if(key==null)return false;lock(Gate){Load();if(values.TryGetValue(key,out value)){Hits++;return true;}Misses++;return false;}}
  static void Load(){if(values!=null)return;values=new Dictionary<string,string>();try{if(File.Exists(FilePath)&&new FileInfo(FilePath).Length<16000000){var saved=JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(FilePath));if(saved!=null&&saved.Count<=Limit)values=saved;}}catch(IOException){}catch(JsonException){}}
  internal static void Add(string key,string text,float confidence){if(key==null||confidence<0.95f||string.IsNullOrWhiteSpace(text))return;lock(Gate){Load();if(values.Count>=Limit)values.Clear();values[key]=text;if(++dirty>=128)Flush();}}
  internal static void Test(){
   string folder=Path.Combine(Path.GetTempPath(),"catheryne-recognition-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
   string original=FilePath;ScanBridge.Folder=folder;FilePath=Path.Combine(folder,"cache.json");
   try{using(var image=new Bitmap(12,12)){
    string key=Key(image,1,false),text;if(TryGet(key,out text))throw new Exception("Empty cache hit");
    Add(key,"90",0.5f);if(TryGet(key,out text))throw new Exception("Low confidence result cached");
    Add(key,"90",1);Flush();values=null;if(!TryGet(key,out text)||text!="90")throw new Exception("Persistent reuse failed");
    image.SetPixel(0,0,Color.White);if(Key(image,1,false)==key)throw new Exception("Changed pixels reused");
    if(Key(image,1,false)==Key(image,2,false)||Key(image,1,false)==Key(image,1,true))throw new Exception("OCR mode not isolated");
   }}finally{ScanBridge.Folder=null;FilePath=original;values=null;dirty=0;Directory.Delete(folder,true);}
  }
  internal static void Flush(){lock(Gate){if(values==null||dirty==0)return;try{ScanBridge.Publish(FilePath,JsonConvert.SerializeObject(values));dirty=0;}catch(IOException){}}}
 }
}