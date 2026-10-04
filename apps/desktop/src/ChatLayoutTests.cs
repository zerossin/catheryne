using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class ChatLayoutTests {
 static void Wait(int milliseconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 static void Check(bool value,string message){if(!value)throw new Exception("Chat layout: "+message);}
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
 static IntPtr Packed(Point point){return new IntPtr(((long)((int)point.Y&65535)<<16)|(uint)((int)point.X&65535));}
 static void Caption(Window window){
  var chrome=System.Windows.Shell.WindowChrome.GetWindowChrome(window);Check(!chrome.UseAeroCaptionButtons&&chrome.GlassFrameThickness.Top==0&&chrome.CaptionHeight==LauncherWindowLayout.HeaderHeight,"custom caption visuals retain Windows chrome behavior");
  var hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;
  string[] names={"WindowMinimize","WindowMaximize","WindowClose"};
  foreach(string name in names){
   var button=(Button)window.FindName(name);Check(button.ActualHeight==LauncherWindowLayout.HeaderHeight&&button.ActualWidth==46,"caption controls fill the header at Windows button width");
   foreach(double y in new[]{2.0,20.0,38.0}){var point=button.PointToScreen(new Point(button.ActualWidth/2,y));Check(SendMessage(hwnd,0x84,IntPtr.Zero,Packed(point)).ToInt32()==(name=="WindowMaximize"?9:1),"caption hit area includes header top, center and bottom: "+name+" / "+y);}
  }
  var rail=(FrameworkElement)window.FindName("NavigationRail");Check(rail.TranslatePoint(new Point(),window).Y<2,"navigation rail joins the top window edge");
  var title=(Grid)window.FindName("TitleBar");var toggle=(FrameworkElement)window.FindName("ToggleHomeChat");var account=System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<Button>(title.Children),b=>b!=toggle);
  var controls=new FrameworkElement[]{account,toggle,(FrameworkElement)window.FindName("HeaderIndicators")};double center=toggle.PointToScreen(new Point(0,toggle.ActualHeight/2)).Y;var caption=(FrameworkElement)window.FindName("WindowMinimize");
  foreach(var control in controls){Check(control.PointToScreen(new Point(control.ActualWidth,0)).X<caption.PointToScreen(new Point()).X,"app controls sit left of caption buttons");Check(Math.Abs(control.PointToScreen(new Point(0,control.ActualHeight/2)).Y-center)<1,"app toolbar controls share one center line");}
  Check(account.PointToScreen(new Point(account.ActualWidth,0)).X<toggle.PointToScreen(new Point()).X,"account and hide controls use distinct columns");
  // Release at the enlarged lower edge must maximize and restore through the same system command.
  var maximize=(Button)window.FindName("WindowMaximize");var original=window.WindowState;
  for(int i=0;i<2;i++){var point=maximize.PointToScreen(new Point(maximize.ActualWidth/2,38));var origin=window.PointToScreen(new Point());SendMessage(hwnd,0xA1,new IntPtr(9),Packed(point));SendMessage(hwnd,0x202,IntPtr.Zero,Packed(new Point(point.X-origin.X,point.Y-origin.Y)));Wait(180);Check(window.WindowState==(i==0?WindowState.Maximized:original),"lower caption edge uses maximize/restore commands");}
  var between=window.PointToScreen(new Point(500,20));Check(SendMessage(hwnd,0x84,IntPtr.Zero,Packed(between)).ToInt32()==2,"unused header retains native dragging and double-click hit testing");
 }
 internal static void Run(Window window,WorkspaceHome workspace){
  var menu=(Border)window.FindName("WorkspaceMenu");var chat=(Border)window.FindName("WorkspaceHome");double width=window.Width;
  Action<double> margin=left=>{window.UpdateLayout();var expected=new Thickness(left,LauncherWindowLayout.ChatTop,18,84);Check((Thickness)chat.GetAnimationBaseValue(FrameworkElement.MarginProperty)==expected&&chat.Margin==expected,"transcript and composer reservation: expected "+expected+", base "+chat.GetAnimationBaseValue(FrameworkElement.MarginProperty)+", current "+chat.Margin);};
  try{
   window.Width=1240;workspace.Show("설정");Wait(300);margin(258);Caption(window);Wait(300);margin(258);
   Check(chat.TranslatePoint(new Point(),window).X>=menu.TranslatePoint(new Point(menu.ActualWidth,0),window).X,"wide chat must remain to the right of the menu: chat "+chat.TranslatePoint(new Point(),window).X+", menu right "+menu.TranslatePoint(new Point(menu.ActualWidth,0),window).X+", menu width "+menu.ActualWidth+", visibility "+chat.Visibility+", window "+window.ActualWidth);
   DrawerMotion.Hide(menu);Wait(20);workspace.EnsureMenu("설정");Wait(300);Check(DrawerMotion.IsOpen(menu),"reopening the same menu must cancel its pending close");margin(258);
   // Recomputed geometry must be recoverable even when the requested destination did not change.
   chat.BeginAnimation(FrameworkElement.MarginProperty,null);chat.Margin=new Thickness(18,LauncherWindowLayout.ChatTop,18,84);workspace.EnsureMenu("설정");Wait(300);margin(258);
   foreach(string route in new[]{"플레이","내 계정","캘린더","설정"}){workspace.HideMenu();workspace.Show(route);Wait(15);}Wait(300);margin(258);
   window.Width=980;Wait(300);margin(18);Caption(window);Check(DrawerMotion.IsOpen(menu),"narrow layout must retain the overlay menu");
   window.Width=1240;Wait(300);margin(258);
   var feature=(Border)window.FindName("CompanionPage");DrawerMotion.Show(feature);Wait(250);Check(chat.Visibility==Visibility.Collapsed,"a detail panel must keep covering chat");DrawerMotion.Hide(feature);Wait(250);Check(chat.Visibility==Visibility.Visible,"chat must return after the detail panel closes");margin(258);
   workspace.HideMenu();Wait(300);margin(18);
  }finally{workspace.HideMenu();window.Width=width;Wait(300);}
 }
}
