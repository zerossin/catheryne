using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

// All saved screenshots and AI observations use this color-aware capture primitive.
internal sealed class GameCaptureFrame : IDisposable {
 internal Bitmap Bitmap; internal Rectangle Bounds; internal bool Hdr; internal float WhiteNits;
 public void Dispose(){if(Bitmap!=null){Bitmap.Dispose();Bitmap=null;}}
}
internal static class GameWindow {
 [StructLayout(LayoutKind.Sequential)]struct RECT{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct POINT{public int X,Y;}
 [DllImport("user32.dll")]internal static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]internal static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out RECT rect);
 [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref POINT point);
 [DllImport("kernel32.dll")]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,System.Text.StringBuilder path,ref int size);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr process);
 internal static Rectangle Bounds(IntPtr window){RECT rect;POINT point=new POINT();if(!GetClientRect(window,out rect)||!ClientToScreen(window,ref point))throw new InvalidOperationException(Locale.T("게임 창 영역을 확인하지 못했습니다."));return new Rectangle(point.X,point.Y,rect.Right,rect.Bottom);}
 internal static IntPtr Find(string root){
  var install=new LauncherOperations(root).Installation();if(install==null)throw new InvalidOperationException(Locale.T("게임 설치 경로를 확인해 주세요."));
  string game=CodexChat.S(new ConfigStore(install.Config).Read(),"GamePath");
  foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(game)))using(process){
   if(process.SessionId!=WindowsChildSession.Current)continue;
   IntPtr handle=OpenProcess(0x1000,false,process.Id);if(handle==IntPtr.Zero)continue;
   try{var path=new System.Text.StringBuilder(32768);int size=path.Capacity;if(QueryFullProcessImageName(handle,0,path,ref size)&&string.Equals(path.ToString(),game,StringComparison.OrdinalIgnoreCase)&&process.MainWindowHandle!=IntPtr.Zero)return process.MainWindowHandle;}finally{CloseHandle(handle);}
  }
  throw new InvalidOperationException(Locale.T("원신 창을 찾을 수 없습니다. 실행 상태와 권한을 확인해 주세요."));
 }
}
internal static class GameCapture {
 [StructLayout(LayoutKind.Sequential)]struct NativeFrame {public IntPtr Pixels;public int Width,Height,Hdr;public float WhiteNits;public int Left,Top;}
 [DllImport("Catheryne.Capture.dll",CallingConvention=CallingConvention.Cdecl)]static extern int CatheryneCapture(IntPtr window,out NativeFrame result);
 [DllImport("Catheryne.Capture.dll",CallingConvention=CallingConvention.Cdecl)]internal static extern void CatheryneMapPixel(float r,float g,float b,float white,int hdr,[Out]byte[] pixel);
 internal static GameCaptureFrame Read(string root){
  if(!GameEnvironment.Remote(root))return Read(GameWindow.Find(root));
  var value=CodexChat.Map(GameEnvironment.Invoke(root,"game.capture",new Dictionary<string,object>(),false));
  using(var stream=new MemoryStream(Convert.FromBase64String(CodexChat.S(value,"png"))))using(var decoded=new Bitmap(stream))
   return new GameCaptureFrame{Bitmap=new Bitmap(decoded),Bounds=new Rectangle(Convert.ToInt32(value["left"]),Convert.ToInt32(value["top"]),decoded.Width,decoded.Height),Hdr=Convert.ToBoolean(value["hdr"]),WhiteNits=Convert.ToSingle(value["white_nits"])};
 }
 internal static object Export(GameCaptureFrame frame){using(frame)using(var stream=new MemoryStream()){frame.Bitmap.Save(stream,ImageFormat.Png);return new{png=Convert.ToBase64String(stream.ToArray()),left=frame.Bounds.Left,top=frame.Bounds.Top,hdr=frame.Hdr,white_nits=frame.WhiteNits};}}
 internal static GameCaptureFrame Read(IntPtr window){
  NativeFrame raw;int error=CatheryneCapture(window,out raw);
  if(error<0)throw new InvalidOperationException(Locale.T(error==unchecked((int)0x800705B4)?"화면 캡처 시간이 초과되었습니다. 게임 창을 표시한 뒤 다시 시도해 주세요.":"화면 캡처에 실패했습니다. 게임 창과 디스플레이 상태를 확인해 주세요.")+" (0x"+error.ToString("X8")+")");
  Bitmap bitmap=null;
  try{
   if(raw.Pixels==IntPtr.Zero||raw.Width<1||raw.Height<1||raw.Width>16384||raw.Height>16384)throw new InvalidOperationException("Invalid capture size");
   bitmap=new Bitmap(raw.Width,raw.Height,PixelFormat.Format32bppArgb);var pixels=bitmap.LockBits(new Rectangle(0,0,raw.Width,raw.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
   try{int stride=checked(raw.Width*4);byte[] row=new byte[stride];for(int y=0;y<raw.Height;y++){Marshal.Copy(IntPtr.Add(raw.Pixels,y*stride),row,0,stride);Marshal.Copy(row,0,IntPtr.Add(pixels.Scan0,y*pixels.Stride),stride);}}finally{bitmap.UnlockBits(pixels);}
   var frame=new GameCaptureFrame{Bitmap=bitmap,Bounds=new Rectangle(raw.Left,raw.Top,raw.Width,raw.Height),Hdr=raw.Hdr!=0,WhiteNits=raw.WhiteNits};bitmap=null;return frame;
  }finally{if(bitmap!=null)bitmap.Dispose();if(raw.Pixels!=IntPtr.Zero)Marshal.FreeCoTaskMem(raw.Pixels);}
 }
}
internal sealed class SavedCapture {
 public string Path {get;set;} public string Created {get;set;} public bool Hdr {get;set;} public int Width {get;set;} public int Height {get;set;}
}
internal sealed class CaptureStore {
 readonly string root;static readonly object IndexLock=new object();
 internal CaptureStore(string root){this.root=root;}
 internal string Folder {get{object value;var prefs=AppPreferences.Read(root);return prefs.TryGetValue("captureFolder",out value)&&!string.IsNullOrWhiteSpace(Convert.ToString(value))?Convert.ToString(value):System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"Catheryne");}}
 internal SavedCapture[] Recent(){lock(IndexLock){string index=System.IO.Path.Combine(root,"capture-index.json");if(!File.Exists(index))return new SavedCapture[0];var list=CatheryneTools.Json().Deserialize<SavedCapture[]>(File.ReadAllText(index));return (list??new SavedCapture[0]).Where(x=>x!=null&&File.Exists(x.Path)).OrderByDescending(x=>x.Created).ToArray();}}
 internal SavedCapture Save(GameCaptureFrame frame){
  string folder=Folder;Directory.CreateDirectory(folder);string path=System.IO.Path.Combine(folder,"Catheryne-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".png");
  try{using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write))frame.Bitmap.Save(stream,ImageFormat.Png);
   var shot=new SavedCapture{Path=path,Created=DateTime.UtcNow.ToString("o"),Hdr=frame.Hdr,Width=frame.Bitmap.Width,Height=frame.Bitmap.Height};
   lock(IndexLock){var rows=Recent().ToList();rows.Insert(0,shot);Directory.CreateDirectory(root);AtomicFile.Write(System.IO.Path.Combine(root,"capture-index.json"),CatheryneTools.Json().Serialize(rows));}return shot;
  }catch{if(File.Exists(path))File.Delete(path);throw;}
 }
}
internal static class CaptureShortcut {
 internal const string Default="Ctrl+Shift+F12";
 internal static bool Parse(string text,out uint modifiers,out uint key){
  modifiers=0;key=0;string[] parts=(text??"").Split('+');if(parts.Length<2)return false;
  for(int i=0;i<parts.Length-1;i++){uint flag=parts[i]=="Ctrl"?2u:parts[i]=="Alt"?1u:parts[i]=="Shift"?4u:parts[i]=="Win"?8u:0u;if(flag==0||(modifiers&flag)!=0)return false;modifiers|=flag;}
  Key parsed;if((modifiers&3)==0||!Enum.TryParse(parts[parts.Length-1],out parsed))return false;
  key=(uint)KeyInterop.VirtualKeyFromKey(parsed);return key>0&&key!=0x10&&key!=0x11&&key!=0x12&&key!=0x5B&&key!=0x5C&&key!=0x1B&&(key<0xA0||key>0xA5);
 }
 internal static string FromKey(Key key,ModifierKeys modifiers){if(key==Key.System)key=Key.None;var parts=new List<string>();if((modifiers&ModifierKeys.Control)!=0)parts.Add("Ctrl");if((modifiers&ModifierKeys.Alt)!=0)parts.Add("Alt");if((modifiers&ModifierKeys.Shift)!=0)parts.Add("Shift");if((modifiers&ModifierKeys.Windows)!=0)parts.Add("Win");parts.Add(key.ToString());return string.Join("+",parts);}
}
internal sealed class CaptureController : IDisposable {
 [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr window,int id);
 readonly Window window;readonly CaptureStore store;readonly Action<string> notify;HwndSource source;int hotkeyId;string registeredShortcut;bool busy;bool disposed;
 internal event Action<SavedCapture> Captured;internal string Error {get;private set;}
 System.Collections.Generic.Dictionary<string,object> HotkeySettings {get{object value;return AppPreferences.Read().TryGetValue("captureHotkey",out value)?CodexChat.Map(value):new System.Collections.Generic.Dictionary<string,object>();}}
 internal string Shortcut {get{string value=CodexChat.S(HotkeySettings,"shortcut");return string.IsNullOrEmpty(value)?CaptureShortcut.Default:value;}}
 internal bool Enabled {get{object value;return HotkeySettings.TryGetValue("enabled",out value)?Equals(value,true):true;}}
 internal CaptureController(Window window,Action<string> notify){this.window=window;this.notify=notify;store=new CaptureStore(Setup.DataFolder);window.SourceInitialized+=(s,e)=>Ready();window.Closed+=(s,e)=>Dispose();if(new WindowInteropHelper(window).Handle!=IntPtr.Zero)Ready();}
 void Ready(){if(disposed||source!=null)return;source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source.AddHook(Message);if(Enabled)try{Configure(true,Shortcut,false);}catch(Exception error){Error=error.Message;}}
 internal void Configure(bool enabled,string shortcut,bool persist=true){
  uint modifiers,key;if(!CaptureShortcut.Parse(shortcut,out modifiers,out key))throw new ArgumentException(Locale.T("Ctrl 또는 Alt를 포함한 단축키를 선택해 주세요."));
  if(source==null)throw new InvalidOperationException(Locale.T("창이 준비된 뒤 다시 시도해 주세요."));
  if(enabled&&hotkeyId!=0&&registeredShortcut==shortcut){Error=null;return;}
  int next=hotkeyId==0x4CA1?0x4CA2:0x4CA1;
  if(enabled&&!RegisterHotKey(source.Handle,next,modifiers|0x4000,key))throw new InvalidOperationException(Locale.T("다른 프로그램이 사용 중인 단축키입니다."));
  try{if(persist){AppPreferences.Set("captureHotkey",new{shortcut=shortcut,enabled=enabled});}}catch{if(enabled)UnregisterHotKey(source.Handle,next);throw;}
  if(hotkeyId!=0)UnregisterHotKey(source.Handle,hotkeyId);hotkeyId=enabled?next:0;registeredShortcut=enabled?shortcut:null;Error=null;
 }
 IntPtr Message(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){if(message==0x0312&&wParam.ToInt32()==hotkeyId&&hotkeyId!=0){handled=true;Trigger();}return IntPtr.Zero;}
 async void Trigger(){await Capture(true);}
 internal async Task<SavedCapture> Capture(bool hotkey=false){
  if(busy)return null;busy=true;
  try{
   // Hotkeys capture only the configured game in the foreground; never other apps.
   if(hotkey&&!GameEnvironment.Remote(Setup.DataFolder)&&GameWindow.Find(Setup.DataFolder)!=GameWindow.GetForegroundWindow())return null;
   var shot=await Task.Run(()=>{using(var frame=GameCapture.Read(Setup.DataFolder))return store.Save(frame);});Error=null;if(Captured!=null)Captured(shot);if(hotkey)notify(Locale.T("캡처를 저장했습니다."));return shot;
  }catch(Exception error){Error=error.Message;if(hotkey){notify(Error);return null;}throw;}finally{busy=false;}
 }
 public void Dispose(){if(disposed)return;disposed=true;if(source!=null){if(hotkeyId!=0)UnregisterHotKey(source.Handle,hotkeyId);source.RemoveHook(Message);}hotkeyId=0;}
}
