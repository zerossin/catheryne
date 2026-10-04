using System;
using System.Collections.Generic;
using System.Linq;

// Ephemeral server requests. The runtime owns history and resolves their lifetime.
internal sealed class AiUserInput {
 internal readonly object Id;
 internal readonly string Key,Thread,Turn;
 internal readonly bool Blocking;
 internal readonly Dictionary<string,object>[] Questions;
 internal AiUserInput(object id,Dictionary<string,object> data){
  Id=id;Key=Convert.ToString(id,System.Globalization.CultureInfo.InvariantCulture);Thread=CodexChat.S(data,"threadId");Turn=CodexChat.S(data,"turnId");
  Blocking=!data.ContainsKey("isBlocking")||!Equals(data["isBlocking"],false);
  object raw;Questions=data.TryGetValue("questions",out raw)?CodexChat.Items(raw).ToArray():new Dictionary<string,object>[0];
  if(string.IsNullOrEmpty(Thread)||string.IsNullOrEmpty(Turn)||Questions.Length==0||Questions.Any(q=>string.IsNullOrEmpty(CodexChat.S(q,"id")))||Questions.Select(q=>CodexChat.S(q,"id")).Distinct().Count()!=Questions.Length)throw new ArgumentException("Invalid user input request");
 }
 internal object Response(Dictionary<string,string> answers,bool skip=false){
  if(skip&&Blocking)throw new InvalidOperationException("Blocking requests must be answered or interrupted");
  if(!skip&&(answers==null||Questions.Any(q=>!answers.ContainsKey(CodexChat.S(q,"id"))||string.IsNullOrWhiteSpace(answers[CodexChat.S(q,"id")]))||answers.Count!=Questions.Length))throw new ArgumentException("Answer every question");
  var result=new Dictionary<string,object>();
  foreach(var q in Questions){string id=CodexChat.S(q,"id");result[id]=new{answers=skip?new string[0]:new[]{answers[id]}};}
  return new{answers=result};
 }
}
internal sealed class AiInputInbox {
 readonly object gate=new object();readonly Dictionary<string,AiUserInput> items=new Dictionary<string,AiUserInput>();
 internal AiUserInput[] Pending(){lock(gate)return items.Values.ToArray();}
 internal bool Add(AiUserInput request){lock(gate){if(items.ContainsKey(request.Key))return false;items.Add(request.Key,request);return true;}}
 internal bool Resolve(string key,out AiUserInput request){lock(gate){if(!items.TryGetValue(key,out request))return false;items.Remove(key);return true;}}
 internal AiUserInput[] Clear(string thread=null,string turn=null){lock(gate){var removed=items.Values.Where(q=>(thread==null||q.Thread==thread)&&(turn==null||q.Turn==turn)).ToArray();foreach(var q in removed)items.Remove(q.Key);return removed;}}
}
