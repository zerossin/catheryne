using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

internal sealed class AiQuestionCard : Border {
 internal readonly AiUserInput Request;
 readonly Dictionary<string,Func<string>> read=new Dictionary<string,Func<string>>();
 readonly StackPanel content=new StackPanel();readonly TextBlock state;
 readonly Button submit;internal bool Resolved;
 internal AiQuestionCard(AiUserInput request,Action<Dictionary<string,string>,bool> answer,Action stop){
  Request=request;Padding=new Thickness(18);CornerRadius=PanelUi.Corners;Margin=ChatLayout.EntryMargin(false);MaxWidth=ChatLayout.MaxWidth;HorizontalAlignment=HorizontalAlignment.Stretch;SetResourceReference(BackgroundProperty,"ControlSurface");Child=content;
  state=PanelUi.Text(Locale.T(request.Blocking?"응답 필요":"선택 질문"));state.FontWeight=FontWeights.SemiBold;content.Children.Add(state);
  foreach(var question in request.Questions){
   var section=new StackPanel{Margin=new Thickness(0,0,0,12)};var label=PanelUi.Text(CodexChat.S(question,"question"));section.Children.Add(label);string id=CodexChat.S(question,"id");
   object raw;var options=question.TryGetValue("options",out raw)?CodexChat.Items(raw).ToArray():new Dictionary<string,object>[0];
   ComboBox choice=null;TextBox free=null;PasswordBox secret=null;
   if(options.Length>0){choice=new ComboBox{MinHeight=40,HorizontalAlignment=HorizontalAlignment.Stretch};choice.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");choice.Items.Add(Locale.T("선택하세요"));foreach(var option in options){var item=new ComboBoxItem{Content=CodexChat.S(option,"label"),Tag=CodexChat.S(option,"label"),ToolTip=CodexChat.S(option,"description")};choice.Items.Add(item);}choice.SelectedIndex=0;choice.Margin=new Thickness(0,0,0,8);section.Children.Add(choice);choice.SelectionChanged+=(s,e)=>RefreshSubmit();}
   if(options.Length==0||Equals(question.ContainsKey("isOther")?question["isOther"]:null,true)){
    if(Equals(question.ContainsKey("isSecret")?question["isSecret"]:null,true)){secret=new PasswordBox{MinHeight=40,Padding=new Thickness(10,6,10,6)};secret.PasswordChanged+=(s,e)=>RefreshSubmit();section.Children.Add(secret);}
    else{free=new TextBox{MinHeight=40,MaxHeight=120,Padding=new Thickness(10,6,10,6),TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};free.TextChanged+=(s,e)=>RefreshSubmit();section.Children.Add(PanelUi.TextInput(free,Locale.T("직접 입력")));}
   }
   var selected=choice;var typed=free;var password=secret;
   read[id]=()=>password!=null&&!string.IsNullOrWhiteSpace(password.Password)?password.Password:typed!=null&&!string.IsNullOrWhiteSpace(typed.Text)?typed.Text: selected!=null&&selected.SelectedIndex>0?Convert.ToString(((ComboBoxItem)selected.SelectedItem).Tag):"";
   content.Children.Add(section);
  }
  submit=PanelUi.Button(Locale.T("답변 보내기"));PanelUi.PrimaryAction(submit,true);submit.Margin=new Thickness(0);submit.IsEnabled=false;
  submit.Click+=(s,e)=>{if(Resolved)return;try{answer(read.ToDictionary(p=>p.Key,p=>p.Value()),false);foreach(var box in Descendants<PasswordBox>(content))box.Clear();}catch(Exception error){state.Text=error.Message;}};
  var secondary=PanelUi.Button(Locale.T(request.Blocking?"중단":"건너뛰기"));secondary.Margin=new Thickness(8,0,0,0);secondary.Click+=(s,e)=>{if(Resolved)return;try{if(request.Blocking)stop();else answer(null,true);}catch(Exception error){state.Text=error.Message;}};
  var actions=new WrapPanel();actions.Children.Add(submit);actions.Children.Add(secondary);content.Children.Add(actions);RefreshSubmit();
 }
 static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject{for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);var match=child as T;if(match!=null)yield return match;foreach(var nested in Descendants<T>(child))yield return nested;}}
 void RefreshSubmit(){if(submit!=null)submit.IsEnabled=!Resolved&&read.Values.All(get=>!string.IsNullOrWhiteSpace(get()));}
 internal void Resolve(string label){if(Resolved)return;Resolved=true;state.Text=label;foreach(var box in Descendants<PasswordBox>(content))box.Clear();content.IsEnabled=false;}
}
