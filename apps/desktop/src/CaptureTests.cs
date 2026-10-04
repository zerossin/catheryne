using System;
using System.Drawing;
using System.IO;
using System.Linq;
internal static class CaptureTests {
 static void Assert(bool value,string reason){if(!value)throw new Exception("Capture: "+reason);}
 static byte[] Map(float value,float white,bool hdr){var pixel=new byte[4];GameCapture.CatheryneMapPixel(value,value,value,white,hdr?1:0,pixel);return pixel;}
 internal static void Run(){
  Assert(Map(0,80,false)[0]==0&&Map(1,80,false)[0]==255,"SDR endpoints");
  Assert(Math.Abs(Map(0.5f,80,false)[0]-188)<=1,"linear to sRGB transfer, not double gamma");
  Assert(Math.Abs(Map(1.5f,240,true)[0]-Map(0.5f,80,false)[0])<=1,"actual display SDR white normalization");
  Assert(Map(3,240,true)[0]<Map(6,240,true)[0]&&Map(6,240,true)[0]<255,"HDR highlights retain differences");
  Assert(Map(float.NaN,80,true)[0]==0&&Map(-1,80,true)[0]==0,"invalid and negative channels");
  Assert(PanelNavigation.Group("Capture")=="화면·성능","capture remains under display menu");
  uint modifiers,key;Assert(CaptureShortcut.Parse("Ctrl+Shift+F12",out modifiers,out key)&&modifiers==6&&key==123,"shortcut modifiers and virtual key");
  foreach(string invalid in new[]{"F12","Ctrl+Ctrl+F12","Ctrl+LeftCtrl","Ctrl+None","Ctrl+Escape","Bogus+F12"})Assert(!CaptureShortcut.Parse(invalid,out modifiers,out key),"reject shortcut "+invalid);
  string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"catheryne-capture-test-"+Guid.NewGuid().ToString("N"));
  try{
   AppPreferences.Set("captureFolder",System.IO.Path.Combine(root,"shots"),root);var store=new CaptureStore(root);
   using(var frame=new GameCaptureFrame{Bitmap=new Bitmap(6,4),Hdr=true,WhiteNits=240}){
    using(var g=Graphics.FromImage(frame.Bitmap))g.Clear(Color.FromArgb(188,120,30));
    var first=store.Save(frame);var second=store.Save(frame);Assert(first.Path!=second.Path,"captures never overwrite");Assert(store.Recent().Length==2&&store.Recent()[0].Path==second.Path,"saved capture index");
    byte[] png=File.ReadAllBytes(first.Path);Assert(png[0]==137&&png[1]==80&&png[2]==78&&png[3]==71,"PNG output");
    using(var loaded=new Bitmap(first.Path))Assert(loaded.Width==6&&loaded.Height==4&&loaded.GetPixel(2,2).R==188,"dimensions and colors retained");
    var input=CodexChat.PrepareInput("look at this",new[]{first.Path});var attached=(System.Collections.Generic.Dictionary<string,object>)input[1];Assert(input.Length==2&&Equals(attached["type"],"localImage")&&File.ReadAllBytes(Convert.ToString(attached["path"])).SequenceEqual(File.ReadAllBytes(first.Path)),"saved PNG uses the existing image attachment path");
    Assert(store.Recent()[0].Hdr,"HDR processing provenance");File.Delete(first.Path);Assert(store.Recent().Length==1,"missing files excluded");
   }
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
