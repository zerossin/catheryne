using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

internal static class AiPersistenceTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception("AI persistence: "+message);}
 static Dictionary<string,object> Data(bool blocking){return CodexChat.Map(CatheryneTools.Json().DeserializeObject("{\"threadId\":\"synthetic\",\"turnId\":\"turn\",\"isBlocking\":"+(blocking?"true":"false")+",\"questions\":[{\"id\":\"choice\",\"header\":\"Choice\",\"question\":\"Which available path?\",\"isOther\":true,\"options\":[{\"label\":\"Path A\",\"description\":\"First path\"},{\"label\":\"Path B\",\"description\":\"Second path\"}]}]}"));}
 internal static void Run(){
  string root=Path.Combine(Path.GetTempPath(),"catheryne-input-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var wire=new List<Dictionary<string,object>>();var events=new List<string>();
   using(var chat=new CodexChat(root,null,message=>wire.Add(CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(message)))))){
    chat.ThreadId="synthetic";chat.TurnId="turn";chat.Notification+=(name,data)=>events.Add(name);
    chat.HandleInput("string-id",Data(false)).GetAwaiter().GetResult();var first=chat.Questions.Pending().Single();
    Check(wire.Count==0&&events.SequenceEqual(new[]{"catheryne/question"}),"question stays open without blocking transport or selecting a default");
    chat.Answer(first,new Dictionary<string,string>{{"choice","Path B"}});Check(CodexChat.S(wire.Single(),"id")=="string-id"&&((System.Collections.IEnumerable)CodexChat.Map(CodexChat.Map(CodexChat.Map(wire[0]["result"])["answers"])["choice"])["answers"]).Cast<object>().Single().ToString()=="Path B","response preserves the original string RPC ID and answer map");
    Check(chat.Questions.Pending().Length==0,"answered request removed exactly once");bool late=false;try{chat.Answer(first,new Dictionary<string,string>{{"choice","Path A"}});}catch(InvalidOperationException){late=true;}Check(late&&wire.Count==1,"late answer cannot replay a resolved request");
    chat.HandleInput(42,Data(true)).GetAwaiter().GetResult();var blocking=chat.Questions.Pending().Single();bool skip=false;try{chat.Answer(blocking,null,true);}catch(InvalidOperationException){skip=true;}Check(skip&&chat.Questions.Pending().Length==1,"blocking input never accepts skip as consent");
    chat.ResolveInput("42");Check(chat.Questions.Pending().Length==0&&wire.Count==1,"server cancellation sends no answer");
    chat.HandleInput(43,Data(false)).GetAwaiter().GetResult();chat.Answer(chat.Questions.Pending().Single(),null,true);var skipped=CodexChat.Map(CodexChat.Map(CodexChat.Map(wire.Last()["result"])["answers"])["choice"]);Check(((System.Collections.IEnumerable)skipped["answers"]).Cast<object>().Count()==0,"optional skip is empty, not the recommended option");
    chat.HandleInput(44,Data(false)).GetAwaiter().GetResult();chat.New();Check(chat.Questions.Pending().Length==0,"navigation clears pending requests without auto-answer");
   }
   var stale=CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(CatheryneTools.Failure(new GameTaskMismatch("synthetic stale handle","old",new AiTaskRecord{State="cancelled"},new[]{new AiTaskRecord{Id="new",Action="game_control",State="running",Title="Synthetic task",Operation="story"}})))));
   Check(CodexChat.S(stale,"code")=="STALE_GAME_TASK"&&CodexChat.S(CodexChat.Items(CodexChat.Map(stale["recovery"])["current_tasks"]).Single(),"task_id")=="new","stale task reports the current identity without reviving terminal records");
   var request=new AiUserInput(99,Data(true));var card=new AiQuestionCard(request,(answers,skip)=>{},()=>{});
   foreach(double width in new[]{320.0,780.0}){card.Measure(new Size(width,double.PositiveInfinity));card.Arrange(new Rect(0,0,width,card.DesiredSize.Height));card.UpdateLayout();Check(card.DesiredSize.Width<=width+.1&&card.DesiredSize.Height>100,"question layout fits narrow/wide widths");}
   card.Resolve(Locale.T("질문 종료됨"));Check(card.Resolved&&!((StackPanel)card.Child).IsEnabled,"resolved cards disable stale controls");
   Check(Equals(AiExecutionPolicy.RuntimeConfig()["features.goals"],true)&&Equals(AiExecutionPolicy.RuntimeConfig()["features.default_mode_request_user_input"],true),"same per-thread policy exposes native goals and default-mode questions");
  }finally{Directory.Delete(root,true);}
 }
}
