using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

// Caption visuals fill the app header; Windows still owns window commands and Snap Layouts.
internal sealed class WindowCaptionControls {
 internal const double ButtonWidth=46;
 readonly Window window;readonly Button maximize;readonly Path maximizeIcon;bool pressed;
 internal readonly StackPanel View=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Height=LauncherWindowLayout.HeaderHeight};
 static readonly ControlTemplate Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' IsHitTestVisible='False'/></Border></ControlTemplate>");
 internal WindowCaptionControls(Window window){
  this.window=window;
  var minimize=Create("WindowMinimize",Locale.T("최소화"),"M1,6 H11",false);minimize.Click+=(s,e)=>SystemCommands.MinimizeWindow(window);
  maximize=Create("WindowMaximize",Locale.T("최대화"),"M2,2 H10 V10 H2 Z",false);maximizeIcon=(Path)maximize.Content;maximize.Click+=(s,e)=>ToggleMaximize();
  var close=Create("WindowClose",Locale.T("닫기"),"M2,2 L10,10 M10,2 L2,10",true);close.Click+=(s,e)=>SystemCommands.CloseWindow(window);
  var surface=(Grid)window.FindName("AppSurface");Grid.SetColumn(View,1);Panel.SetZIndex(View,10);surface.Children.Add(View);
  ((Grid)window.FindName("TitleBar")).Margin=new Thickness(0,0,3*ButtonWidth+8,0);
  window.StateChanged+=(s,e)=>UpdateMaximize();window.Deactivated+=(s,e)=>Reset();
  window.SourceInitialized+=(s,e)=>HwndSource.FromHwnd(new WindowInteropHelper(window).Handle).AddHook(Message);
  UpdateMaximize();
 }
 Button Create(string name,string label,string geometry,bool close){
  var icon=new Path{Data=Geometry.Parse(geometry),Width=12,Height=12,StrokeThickness=1,Stroke=Brushes.White,SnapsToDevicePixels=true};
  var button=new Button{Name=name,Content=icon,Width=ButtonWidth,Height=LauncherWindowLayout.HeaderHeight,MinWidth=0,Padding=new Thickness(0),Margin=new Thickness(0),Template=Template,Focusable=false,ToolTip=label};
  var hover=new SolidColorBrush(close?Color.FromRgb(196,43,28):Color.FromRgb(49,51,55));
  var style=new Style(typeof(Button));style.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));foreach(var property in new[]{UIElement.IsMouseOverProperty,FrameworkElement.TagProperty}){var trigger=new Trigger{Property=property,Value=property==FrameworkElement.TagProperty?(object)"hover":true};trigger.Setters.Add(new Setter(Control.BackgroundProperty,hover));style.Triggers.Add(trigger);}button.Style=style;
  AutomationProperties.SetName(button,label);WindowChrome.SetIsHitTestVisibleInChrome(button,true);window.RegisterName(name,button);View.Children.Add(button);return button;
 }
 void UpdateMaximize(){bool restored=window.WindowState==WindowState.Maximized;maximizeIcon.Data=Geometry.Parse(restored?"M2,4 H8 V10 H2 Z M4,4 V2 H10 V8 H8":"M2,2 H10 V10 H2 Z");string label=Locale.T(restored?"복원":"최대화");maximize.ToolTip=label;AutomationProperties.SetName(maximize,label);}
 void ToggleMaximize(){if(window.WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(window);else SystemCommands.MaximizeWindow(window);}
 static bool Contains(Button button,Point screen){if(!button.IsVisible)return false;var point=button.PointFromScreen(screen);return point.X>=0&&point.Y>=0&&point.X<button.ActualWidth&&point.Y<button.ActualHeight;}
 bool Contains(Point screen){return Contains(maximize,screen);}
 Point ClientScreenPoint(IntPtr packed){return window.PointToScreen(PresentationSource.FromVisual(window).CompositionTarget.TransformFromDevice.Transform(ScreenPoint(packed)));}
 static Point ScreenPoint(IntPtr packed){long n=packed.ToInt64();return new Point(unchecked((short)(n&65535)),unchecked((short)((n>>16)&65535)));}
 void Reset(){pressed=false;maximize.Tag=null;}
 [StructLayout(LayoutKind.Sequential)] struct MouseTracking {public int Size,Flags;public IntPtr Window;public int HoverTime;}
 [DllImport("user32.dll")] static extern bool TrackMouseEvent(ref MouseTracking tracking);
 [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr window);
 [DllImport("user32.dll")] static extern bool ReleaseCapture();
 IntPtr Message(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
  // HTMAXBUTTON is the Windows 11 contract for the native Snap Layout menu.
  if(message==0x84){var point=ScreenPoint(lParam);foreach(Button button in View.Children)if(Contains(button,point)){handled=true;return new IntPtr(button==maximize?9:1);}}
  if(message==0xA0){bool inside=Contains(ScreenPoint(lParam));maximize.Tag=inside?"hover":null;if(inside){var tracking=new MouseTracking{Size=Marshal.SizeOf(typeof(MouseTracking)),Flags=0x12,Window=hwnd};TrackMouseEvent(ref tracking);}}
  if(message==0x2A2&&!pressed)maximize.Tag=null;
  if(message==0xA1&&wParam.ToInt32()==9){pressed=true;SetCapture(hwnd);handled=true;}
  if(message==0x200&&pressed){var screen=ClientScreenPoint(lParam);maximize.Tag=Contains(screen)?"hover":null;}
  if((message==0x202||message==0xA2)&&pressed){var screen=message==0xA2?ScreenPoint(lParam):ClientScreenPoint(lParam);bool click=Contains(screen);Reset();ReleaseCapture();handled=true;if(click)ToggleMaximize();}
  if(message==0x215)Reset();
  if(message==0x1F&&pressed){Reset();ReleaseCapture();}
  return IntPtr.Zero;
 }
}
