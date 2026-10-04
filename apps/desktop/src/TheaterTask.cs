using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Rebuildable projection of the canonical journal, never a second run store.
internal static class TheaterTask {
 internal static AiTaskRecord Project(Dictionary<string,object> journal,string root=null){
  var state=TheaterService.Project(journal);string phase=CodexChat.S(state,"phase");
  int total=state.ContainsKey("totalActs")?Convert.ToInt32(state["totalActs"]):0,done=state.ContainsKey("completedActs")?Convert.ToInt32(state["completedActs"]):0,act=state.ContainsKey("act")?Convert.ToInt32(state["act"]):0;
  var steps=Enumerable.Range(1,total).Select(n=>new Dictionary<string,object>{{"id","act:"+n},{"title",n+"막"}}).ToArray();
  var statuses=Enumerable.Range(1,total).ToDictionary(n=>"act:"+n,n=>(object)new{status=n<=done?"completed":"pending"});
  var roster=new Dictionary<string,object>();
  if(root!=null){string snapshot=CodexChat.S(CodexChat.Map(journal["accountReference"]),"snapshot");if(snapshot.Length>0&&System.IO.Path.GetFileName(snapshot)==snapshot){string path=System.IO.Path.Combine(root,"profiles","default","account","snapshots",snapshot);if(System.IO.File.Exists(path)){var account=StoryClient.Read(path);object raw;if(account.TryGetValue("characters",out raw))foreach(var row in CodexChat.Items(raw)){string id=AccountIdentity.Key(row);var members=CodexChat.Map(state["members"]);if(members.ContainsKey(id)&&CodexChat.S(CodexChat.Map(members[id]),"source")=="owned")roster[id]=row.Where(p=>new[]{"identity","key","gameId","names","level","constellation","element","rarity","icon"}.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);}}}}
  return new AiTaskRecord{Id="theater:"+CodexChat.S(journal,"id"),GroupId="theater:"+CodexChat.S(journal,"id"),Thread=CodexChat.S(journal,"thread"),Tool="catheryne_theater",Action="theater",Operation="theater",Title=CodexChat.S(journal,"goal"),Started=CodexChat.S(journal,"createdAt"),Ended=phase=="completed"||phase=="ended"?CodexChat.S(state,"observedAt"):null,State=phase=="completed"?"completed":phase=="ended"?"cancelled":"waiting",Result=new Dictionary<string,object>{{"theater",new{sessionId=journal["id"],goal=journal["goal"],state=state,roster=roster}},{"plan_steps",steps},{"steps",statuses},{"stage","act:"+act},{"stage_title",total>0?Locale.Format("{0} / {1}막 완료",done,total):Locale.T("막 수 확인 대기")},{"progress",new{measured=true,completed=done,total=total}}}};
 }
}

// Content extension of AiTaskCard; all surfaces consume the same task projection.
internal sealed class TheaterTaskView : StackPanel {
 string rendered;internal Action<string,string> Confirm;readonly List<ComboBox> confirmations=new List<ComboBox>();
 internal void Invalidate(){rendered=null;}
 internal void EnableConfirmation(bool value){foreach(var combo in confirmations)combo.IsEnabled=value;}
 static string S(Dictionary<string,object> row,string key){return CodexChat.S(row,key);}
 static Dictionary<string,object> M(object row){return CodexChat.Map(row);}
 static object V(Dictionary<string,object> row,string key){object value;return row.TryGetValue(key,out value)?value:null;}
 static Brush B(string color){return (Brush)new BrushConverter().ConvertFromString(color);}
 static TextBlock Text(string text,int size=13,bool muted=false){var view=PanelUi.Text(text,muted);view.FontSize=size;view.Margin=new Thickness(0);return view;}
 static string MemberName(Dictionary<string,object> member){string name=S(member,"name");if(name.Length>0)return name;string identity=S(member,"identity");name=GameCatalog.Name(identity);return name==identity&&GameCatalog.CharacterField(identity,"gameId").Length==0?Locale.T("미확인"):name;}
 static string Vigor(Dictionary<string,object> member){return !member.ContainsKey("vigor")||member["vigor"]==null?Locale.T("출전 횟수 미확인"):Locale.Format("출전 {0}회",member["vigor"]);}
 internal static string Availability(Dictionary<string,object> member){
  string status=!member.ContainsKey("available")?Locale.T("출전 여부 미확인"):Equals(member["available"],true)?"":Locale.T("현재 출전 불가");string uses=V(member,"vigor")!=null?Vigor(member):Equals(V(member,"available"),true)?Vigor(member):"";return string.Join("  ",new[]{status,uses}.Where(x=>x.Length>0));
 }
 static UIElement Portrait(Dictionary<string,object> member){
  var panel=new StackPanel{Width=116};var showcase=InventoryCard.Create("characters",member,MemberName(member),new Dictionary<string,object>(),null,true);showcase.Width=116;showcase.Height=166;panel.Children.Add(showcase);var uses=Text(Availability(member),11,true);uses.Margin=new Thickness(0,6,0,0);panel.Children.Add(uses);string source=S(member,"source");if(source=="trial"||source=="support"){var label=Text(Locale.T(source=="trial"?"체험 캐릭터":"지원 캐릭터"),11,true);label.Margin=new Thickness(0,3,0,0);panel.Children.Add(label);}return panel;
 }
 static UIElement EventCard(Dictionary<string,object> choice){
  var body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(92)});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  string kind=S(choice,"kind");var art=new Grid{Background=new LinearGradientBrush(Color.FromRgb(68,79,110),Color.FromRgb(37,44,62),90)};
  string path=kind=="enemy"?"M5,4 L20,19 M19,4 L4,19 M3,16 L8,21 M16,21 L21,16":kind=="character"?"M8,8 A4,4 0 1 0 16,8 A4,4 0 1 0 8,8 M4,22 V19 C4,13 20,13 20,19 V22":kind=="card"?"M5,3 H19 V21 H5 Z M12,6 L14,10 L18,12 L14,14 L12,18 L10,14 L6,12 L10,10 Z":"M12,3 L15,9 L21,12 L15,15 L12,21 L9,15 L3,12 L9,9 Z";
  var canvas=new Canvas{Width=24,Height=24};canvas.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(path),Stroke=B("#DDD4C5"),StrokeThickness=1.5,StrokeLineJoin=PenLineJoin.Round});var symbol=new Viewbox{Child=canvas,Width=44,Height=44};symbol.HorizontalAlignment=HorizontalAlignment.Center;symbol.VerticalAlignment=VerticalAlignment.Center;art.Children.Add(symbol);
  if(V(choice,"level")!=null){var level=Text("Lv. "+choice["level"],12);level.HorizontalAlignment=HorizontalAlignment.Right;level.VerticalAlignment=VerticalAlignment.Bottom;level.Margin=new Thickness(6);art.Children.Add(level);}body.Children.Add(art);
  var label=Text(S(choice,"text"),14);label.FontWeight=FontWeights.SemiBold;label.Foreground=B("#3D434E");label.TextAlignment=TextAlignment.Center;label.Margin=new Thickness(8);var title=new Border{Background=B("#DDD4C5"),Child=label,MinHeight=54};Grid.SetRow(title,1);body.Children.Add(title);
  string amount=V(choice,"cost")!=null?"−"+choice["cost"]:V(choice,"reward")!=null?"+"+choice["reward"]:"";var currency=Text(amount.Length>0?Locale.Format("환상꽃 {0}",amount):Locale.T(kind=="enemy"?"전투":kind=="character"?"동료":kind=="card"?"강화":"이벤트"),12,true);currency.TextAlignment=TextAlignment.Center;currency.Margin=new Thickness(0,8,0,4);Grid.SetRow(currency,2);body.Children.Add(currency);return body;
 }
 static string CheckLabel(string kind){return Locale.T(kind=="mechanics"?"적 기믹":kind=="survival"?"생존":kind=="damage"?"화력":kind=="execution"?"운용":"반응");}
 static string Readiness(string status){return Locale.T(status=="supported"?"근거 확보":status=="recruitment"?"영입 필요":status=="blocked"?"편성 불가":"검증 필요");}
 static string Phase(string phase,IEnumerable<Dictionary<string,object>> choices){switch(phase){case "setup":return "참가 명단 확인";case "party":return "출전 파티";case "combat":return "전투 진행 중";case "result":return "결과 확인";case "completed":return "도전 완료";case "ended":return "관제 종료";default:var kinds=choices.Select(x=>S(x,"kind")).Distinct().ToArray();return kinds.Length==1?(kinds[0]=="character"?"캐릭터 영입":kinds[0]=="card"?"카드 선택":kinds[0]=="enemy"?"적 선택":"이벤트 선택"):"다음 선택";}}
 internal void Update(AiTaskRecord task){
  if(task.Action!="theater"){Visibility=Visibility.Collapsed;return;}Visibility=Visibility.Visible;
  var data=task.ResultData;var theater=M(V(data,"theater"));var state=M(V(theater,"state"));string key=CatheryneTools.Json().Serialize(theater);if(key==rendered)return;rendered=key;bool expanded=Children.OfType<Expander>().Any(e=>e.IsExpanded);Children.Clear();confirmations.Clear();
  var members=M(V(state,"members"));var roster=M(V(theater,"roster"));Func<string,Dictionary<string,object>> display=id=>{var row=roster.ContainsKey(id)?new Dictionary<string,object>(M(roster[id])):new Dictionary<string,object>();foreach(var field in M(members[id]))row[field.Key]=field.Value;return row;};var choices=CodexChat.Items(V(state,"choices")).ToArray();var recommendation=M(V(state,"recommendation"));string phase=S(state,"phase");
  var heading=new DockPanel{Margin=new Thickness(0,12,0,12)};var summary=Text((state.ContainsKey("flowers")?Locale.Format("환상꽃 {0}",state["flowers"]):""),12,true);DockPanel.SetDock(summary,Dock.Right);heading.Children.Add(summary);string stage=S(state,"challenge");if(stage.Length==0&&state.ContainsKey("act")&&Convert.ToInt32(state["act"])>0&&phase!="completed"&&phase!="ended")stage=Locale.Format("{0}막",state["act"]);var title=Text((stage.Length>0?stage+"  ":"")+Locale.T(Phase(phase,choices)),18);title.FontWeight=FontWeights.SemiBold;heading.Children.Add(title);Children.Add(heading);
  if(S(state,"target").Length>0&&phase!="completed"&&phase!="ended"){var target=Text(S(state,"target"),13,true);target.Margin=new Thickness(0,0,0,12);Children.Add(target);}
  if(choices.Length>0){var row=new WrapPanel();foreach(var choice in choices){bool selected=S(choice,"id")==S(recommendation,"choice_id");string identity=S(choice,"identity");bool character=S(choice,"kind")=="character",known=character&&TheaterService.IdentifiedChoice(state,choice);
    var content=new StackPanel();var position=Text(S(choice,"position"),12,true);position.Margin=new Thickness(0,0,0,6);if(position.Text.Length>0)content.Children.Add(position);
    if(known){var member=display(identity);content.Children.Add(Portrait(member));}
    else if(!character)content.Children.Add(EventCard(choice));else {var label=Text(S(choice,"text"),15);label.FontWeight=FontWeights.SemiBold;content.Children.Add(label);}
    if(character&&!known){var check=Text(Locale.T("이름 확인 필요"),12);check.Foreground=B("#F0BE80");check.Margin=new Thickness(0,6,0,0);content.Children.Add(check);
     if(Confirm!=null&&Equals(V(state,"participantsConfirmed"),true)&&members.Count>0){
      var picker=new ComboBox{Margin=new Thickness(0,8,0,0),MinWidth=0,ToolTip=Locale.T("화면에 읽힌 이름을 실제 참가 명단에서 선택합니다.")};picker.SetResourceReference(FrameworkElement.StyleProperty,"OptionsCombo");picker.Items.Add(new ComboBoxItem{Content=Locale.T("이름 확인"),IsEnabled=false});
      foreach(string id in members.Keys.Where(k=>MemberName(M(members[k]))!=Locale.T("미확인")).OrderBy(k=>MemberName(M(members[k]))))picker.Items.Add(new ComboBoxItem{Content=MemberName(M(members[id])),Tag=id});picker.SelectedIndex=0;string choiceId=S(choice,"id");picker.SelectionChanged+=(sender,args)=>{var selectedName=picker.SelectedItem as ComboBoxItem;if(selectedName!=null&&selectedName.Tag!=null)Confirm(choiceId,Convert.ToString(selectedName.Tag));};confirmations.Add(picker);content.Children.Add(picker);
     }
    }
    if(character&&V(choice,"cost")!=null&&Convert.ToInt32(choice["cost"])>0){var cost=Text(Locale.Format("환상꽃 {0}",choice["cost"]),12,true);cost.Margin=new Thickness(0,6,0,0);content.Children.Add(cost);}
    if(selected){var badge=Text(Locale.T("추천"),12);badge.Foreground=B("#82C5A7");badge.Margin=new Thickness(0,8,0,0);content.Children.Add(badge);}
    var box=new Border{Width=character&&!known?220:character?144:168,Padding=new Thickness(known?12:character?12:8),Margin=new Thickness(0,0,8,8),CornerRadius=PanelUi.Corners,Background=B(selected?"#293E38":"#272E36"),BorderBrush=B(selected?"#82C5A7":"#434B58"),BorderThickness=new Thickness(selected?2:1),Child=content,ToolTip=S(choice,"description")};System.Windows.Automation.AutomationProperties.SetName(box,S(choice,"position")+" "+S(choice,"text")+(selected?" "+Locale.T("추천"):""));row.Children.Add(box);
   }Children.Add(row);}
  var team=V(recommendation,"team") as IEnumerable;if(team!=null){var party=new WrapPanel{Margin=new Thickness(0,0,0,12)};foreach(object identity in team){string id=Convert.ToString(identity);if(members.ContainsKey(id))party.Children.Add(new Border{Width=132,Padding=new Thickness(8),Margin=new Thickness(0,0,8,8),CornerRadius=PanelUi.Corners,Background=B("#272E36"),Child=Portrait(display(id))});}Children.Add(party);}
  var audit=M(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(V(state,"planningAudit"))));bool reviewing=Equals(V(audit,"needsReview"),true)&&phase!="completed"&&phase!="ended";
  if(reviewing){var check=Text(Locale.T("변경된 상태로 남은 계획 재검토 필요"),13);check.Foreground=B("#F0BE80");check.Margin=new Thickness(0,0,0,10);Children.Add(check);}
  var referencePlan=recommendation.Count>0?recommendation:M(V(state,"lastPlan"));
  var assessment=CodexChat.Items(V(referencePlan,"assessment")).ToArray();var battles=TheaterPlanning.Remaining(state,referencePlan);var uncertain=assessment.Where(x=>S(x,"status")!="passed").Select(x=>CheckLabel(S(x,"kind"))).ToArray();
  if(recommendation.Count>0&&uncertain.Length>0){var warning=Text(Locale.Format("검증 필요 · {0}",string.Join(" · ",uncertain)),13);warning.Foreground=B("#F0BE80");warning.Margin=new Thickness(0,0,0,10);warning.ToolTip=string.Join("\n",assessment.Where(x=>S(x,"status")!="passed").Select(x=>S(x,"evidence")));Children.Add(warning);}
  if(battles.Any(x=>S(x,"status")!="supported")){var warning=Text(Locale.Format("남은 전투 {0}개 확인 필요",battles.Count(x=>S(x,"status")!="supported")),12);warning.Foreground=B("#F0BE80");warning.Margin=new Thickness(0,0,0,10);Children.Add(warning);}
  if(S(recommendation,"changeReason").Length>0){var changed=Text(S(recommendation,"changeReason"),12,true);changed.Margin=new Thickness(0,0,0,10);Children.Add(changed);}
  if(S(recommendation,"reason").Length>0){var reason=Text(S(recommendation,"reason"));reason.Margin=new Thickness(0,4,0,12);Children.Add(reason);}
  var rotation=CodexChat.Items(V(recommendation,"rotation")).ToArray();if(rotation.Length>0){var label=Text(Locale.T("스킬 순서"),14);label.FontWeight=FontWeights.SemiBold;label.Margin=new Thickness(0,0,0,8);Children.Add(label);for(int i=0;i<rotation.Length;i++){var step=rotation[i];var row=new DockPanel{Margin=new Thickness(0,0,0,8)};var number=Text((i+1).ToString(),12,true);number.Width=24;row.Children.Add(number);string id=S(step,"identity");row.Children.Add(Text((members.ContainsKey(id)?MemberName(M(members[id])):GameCatalog.Name(id))+"  "+S(step,"action")));Children.Add(row);}}
  if(recommendation.Count==0&&phase!="completed"&&phase!="ended"){string text=phase=="combat"?"전투를 마친 뒤 판단해 주세요.":phase=="completed"?"최종 결과를 확인했습니다.":phase=="ended"?"이 도전의 관제를 종료했습니다.":"현재 화면 확인 대기";var pending=Text(Locale.T(text),13,true);pending.Margin=new Thickness(0,0,0,12);Children.Add(pending);}
  var context=new StackPanel();if((phase=="completed"||phase=="ended")&&S(state,"target").Length>0)context.Children.Add(Text(S(state,"target"),12,true));
  var cast=new WrapPanel();foreach(var member in members.Values.Select(M)){var label=Text(MemberName(member)+"  "+Availability(member),12,true);label.Margin=new Thickness(0,0,16,8);cast.Children.Add(label);}if(cast.Children.Count>0)context.Children.Add(cast);
  foreach(string field in new[]{"cards","reactions"}){var entries=M(V(state,field)).Values.Select(M).ToArray();if(entries.Length==0)continue;var label=Text(Locale.T(field=="cards"?"보유 카드":"반응 강화"),13);label.FontWeight=FontWeights.SemiBold;label.Margin=new Thickness(0,8,0,6);context.Children.Add(label);foreach(var entry in entries){var effect=Text(S(entry,"name")+(V(entry,"level")!=null?"  Lv. "+entry["level"]:""),12,true);effect.Margin=new Thickness(0,0,0,4);effect.ToolTip=S(entry,"description");context.Children.Add(effect);}}
  if(assessment.Length>0){var label=Text(Locale.T("판단 근거"),13);label.FontWeight=FontWeights.SemiBold;label.Margin=new Thickness(0,12,0,6);context.Children.Add(label);foreach(var check in assessment){var line=Text(CheckLabel(S(check,"kind"))+" · "+Locale.T(S(check,"status")=="passed"?"근거 확인":"검증 필요")+"\n"+S(check,"evidence"),12,true);line.Margin=new Thickness(0,0,0,8);context.Children.Add(line);}}
  if(battles.Length>0){var label=Text(Locale.T("남은 전투"),13);label.FontWeight=FontWeights.SemiBold;label.Margin=new Thickness(0,12,0,6);context.Children.Add(label);foreach(var battle in battles){var block=new StackPanel();var battleTitle=Text(S(battle,"title")+" · "+(reviewing?Locale.T("재검토 필요"):Readiness(S(battle,"status"))),13);battleTitle.FontWeight=FontWeights.SemiBold;battleTitle.Foreground=B(!reviewing&&S(battle,"status")=="supported"?"#82C5A7":"#F0BE80");block.Children.Add(battleTitle);var ids=V(battle,"team") as IEnumerable;if(ids!=null)block.Children.Add(Text(string.Join(" · ",ids.Cast<object>().Select(x=>members.ContainsKey(Convert.ToString(x))?MemberName(M(members[Convert.ToString(x)])):Locale.T("미확인"))),12,true));block.Children.Add(Text(S(battle,"requirements"),12,true));block.Children.Add(Text(S(battle,"evidence"),12,true));block.Children.Add(Text(Locale.T("대안")+" · "+S(battle,"fallback"),12,true));context.Children.Add(new Border{Child=block,Margin=new Thickness(0,0,0,10)});}}
  foreach(string field in new[]{"futurePlan","alternatives"})if(S(referencePlan,field).Length>0){var label=Text(Locale.T(field=="futurePlan"?"남은 도전 계획":"대안"),13);label.FontWeight=FontWeights.SemiBold;label.Margin=new Thickness(0,12,0,6);context.Children.Add(label);context.Children.Add(Text(S(referencePlan,field),12,true));}
  if(context.Children.Count>0){var details=PanelUi.InlineDetails(Locale.T(phase=="completed"||phase=="ended"?"캐릭터·카드":"캐릭터·카드·남은 계획"),context);details.IsExpanded=expanded;details.Margin=new Thickness(0,4,0,8);Children.Add(details);}
 }
}
