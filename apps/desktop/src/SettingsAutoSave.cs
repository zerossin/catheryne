using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

// One debounced save path for editable settings; incomplete input keeps the last valid value.
internal sealed class SettingsAutoSave : IDisposable {
 readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(450)};readonly Action save;bool pending;
 internal bool Pending {get{return pending;}}
 internal SettingsAutoSave(Action save){this.save=save;timer.Tick+=(s,e)=>Flush();}
 internal void Request(){pending=true;timer.Stop();timer.Start();}
 internal void Attach(UIElement scope,Func<bool> ready){RoutedEventHandler changed=(s,e)=>{if(ready())Request();};scope.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((s,e)=>{if(ready())Request();}));RoutedEventHandler optionChanged=(s,e)=>{if((e.OriginalSource is CheckBox||e.OriginalSource is RadioButton)&&ready())Request();};scope.AddHandler(ToggleButton.CheckedEvent,optionChanged);scope.AddHandler(ToggleButton.UncheckedEvent,optionChanged);scope.AddHandler(PasswordBox.PasswordChangedEvent,changed);scope.AddHandler(Selector.SelectionChangedEvent,new SelectionChangedEventHandler((s,e)=>{if(ready())Request();}));}
 internal void Flush(){timer.Stop();if(!pending)return;pending=false;save();}
 public void Dispose(){Flush();timer.Stop();}
}
