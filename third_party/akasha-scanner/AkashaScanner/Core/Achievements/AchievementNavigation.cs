using AkashaScanner.Core.Screenshot;
using AkashaScanner.Core.Suspender;
using CatheryneScanning;
using Tesseract;
using WindowsInput;
using WindowsInput.Native;
using System.Runtime.InteropServices;
namespace AkashaScanner.Core.Achievements {
 // Navigate only to labels observed on this frame. Never guess menu grid positions.
 internal static class AchievementNavigation {
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
  internal static void Open(GameWindow win,IScreenshotProvider screenshots,ISuspender wait){
   using var engine=new TesseractEngine(Path.Combine(Utils.ExecutableDirectory,"Resources","tessdata"),"genshin_eng",EngineMode.LstmOnly);
   var input=new InputSimulator();
   for(int attempt=0;attempt<6;attempt++){
    ScanBridge.Check(true);
    using var bitmap=screenshots.Capture(new Rectangle(win.WindowX,win.WindowY,win.WindowWidth,win.WindowHeight));
    using var bytes=new MemoryStream();bitmap.Save(bytes,System.Drawing.Imaging.ImageFormat.Png);
    using var pix=Pix.LoadFromMemory(bytes.ToArray());using var page=engine.Process(pix,PageSegMode.SparseText);using var iter=page.GetIterator();
    Point? menu=null,category=null;iter.Begin();
    do {
     string text=(iter.GetText(PageIteratorLevel.Word)??"").Trim(' ','.',':','!').ToLowerInvariant();
     if(!iter.TryGetBoundingBox(PageIteratorLevel.Word,out var box))continue;
     int x=box.X1,y=box.Y1;double rx=x/win.ScaleMultiplier,ry=y/win.ScaleMultiplier;
     if(text=="search"&&rx<410&&ry>45&&ry<150)return;
     if(text=="achievements"&&rx<500&&ry>150)menu=new Point((box.X1+box.X2)/2+win.WindowX,(box.Y1+box.Y2)/2+win.WindowY);
     if(text=="wonders"&&ry>100)category=new Point((box.X1+box.X2)/2+win.WindowX,(box.Y1+box.Y2)/2+win.WindowY);
    }while(iter.Next(PageIteratorLevel.Word));
    ScanBridge.Check(true);var target=category??menu;
    if(target.HasValue){SetCursorPos(target.Value.X,target.Value.Y);wait.Sleep(80);ScanBridge.Check(true);input.Mouse.LeftButtonClick();}
    else input.Keyboard.KeyPress(VirtualKeyCode.ESCAPE);
    wait.Sleep(700);
   }
   throw new InvalidOperationException("업적 검색 화면을 확인하지 못했습니다. 업적 검색 화면을 연 뒤 이어서 최신화해 주세요.");
  }
 }
}