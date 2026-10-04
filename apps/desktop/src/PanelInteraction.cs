using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

internal static class PanelInteraction {
 internal static DependencyObject Parent(DependencyObject node){
  var item=node as ComboBoxItem;if(item!=null)return ItemsControl.ItemsControlFromItemContainer(item);
  var popup=node as Popup;if(popup!=null)return popup.PlacementTarget??popup.TemplatedParent??LogicalTreeHelper.GetParent(popup);
  var element=node as FrameworkElement;if(element!=null&&element.Parent!=null)return element.Parent;
  var parent=node is Visual||node is Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);
  return parent??(element==null?null:element.TemplatedParent);
 }
 internal static bool Within(DependencyObject child,DependencyObject parent){var seen=new HashSet<DependencyObject>();while(child!=null&&seen.Add(child)){if(child==parent)return true;child=Parent(child);}return false;}
 static ComboBox Combo(DependencyObject node){while(node!=null){var combo=node as ComboBox;if(combo!=null)return combo;node=Parent(node);}return null;}
 internal static bool Switch(Window window,ComboBox current,Point point){
  if(current==null||!current.IsDropDownOpen||!Within(current,window))return false;
  var popup=current.Template.FindName("PART_Popup",current) as Popup;if(popup!=null&&popup.Child!=null&&popup.Child.IsMouseOver)return false;
  var target=Combo(window.InputHitTest(point) as DependencyObject);if(target==null||target==current||!target.IsEnabled||!target.IsVisible)return false;
  current.IsDropDownOpen=false;
  window.Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>{if(window.IsVisible&&target.IsVisible&&target.IsEnabled){if(window.IsActive)target.Focus();target.IsDropDownOpen=true;}}));return true;
 }
 internal static bool SurfaceHit(DependencyObject source,DependencyObject surface){while(source!=null&&source!=surface){if(source is ButtonBase||source is TextBoxBase||source is ComboBox||source is ScrollBar||source is Thumb)return false;source=Parent(source);}return source==surface;}
 internal static void OpenSurface(Border surface,string name,Action open){
  surface.Focusable=true;surface.Cursor=Cursors.Hand;surface.BorderBrush=Brushes.Transparent;surface.BorderThickness=new Thickness(1);System.Windows.Automation.AutomationProperties.SetName(surface,name);Point? pressed=null;
  surface.PreviewMouseLeftButtonDown+=(s,e)=>{pressed=SurfaceHit(e.OriginalSource as DependencyObject,surface)?(Point?)e.GetPosition(surface):null;if(pressed.HasValue)e.Handled=true;};
  surface.MouseLeftButtonUp+=(s,e)=>{var down=pressed;pressed=null;if(!down.HasValue||!SurfaceHit(e.OriginalSource as DependencyObject,surface))return;var point=e.GetPosition(surface);if(Math.Abs(point.X-down.Value.X)>SystemParameters.MinimumHorizontalDragDistance||Math.Abs(point.Y-down.Value.Y)>SystemParameters.MinimumVerticalDragDistance)return;e.Handled=true;open();};
  surface.KeyDown+=(s,e)=>{if(!surface.IsKeyboardFocused||(e.Key!=Key.Enter&&e.Key!=Key.Space))return;e.Handled=true;open();};
  surface.GotKeyboardFocus+=(s,e)=>{if(surface.IsKeyboardFocused)surface.BorderBrush=new SolidColorBrush(Color.FromRgb(185,230,216));};surface.LostKeyboardFocus+=(s,e)=>surface.BorderBrush=Brushes.Transparent;
 }
 internal static void Attach(Window window){
  PreProcessInputEventHandler handler=(s,e)=>{var mouse=e.StagingItem.Input as MouseButtonEventArgs;if(mouse==null||mouse.RoutedEvent!=Mouse.PreviewMouseDownEvent||mouse.ChangedButton!=MouseButton.Left||mouse.ButtonState!=MouseButtonState.Pressed)return;var current=Mouse.Captured as ComboBox;if(Switch(window,current,mouse.GetPosition(window)))mouse.Handled=true;};
  InputManager.Current.PreProcessInput+=handler;window.Closed+=(s,e)=>InputManager.Current.PreProcessInput-=handler;
 }
}
