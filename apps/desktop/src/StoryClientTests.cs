using System;
using System.Collections.Generic;
using System.IO;
internal static class StoryClientTests {
 internal static int Run(string directory){
  try {
   var client=new StoryClient(directory);
   var status=client.Call("status",new{after_sequence=0});
   if((bool)status["input_enabled"])throw new Exception("Tests require a no-input host");
   var user=client.Call("set_control",new{target="user",reason="integration_test"});
   if(Convert.ToString(user["control"])!="user")throw new Exception("Handoff failed");
   var agent=client.Call("set_control",new{target="agent",reason="integration_test"});
   if(!(bool)agent["stopped"]||agent["observation_wall"]!=null)throw new Exception("Resume must require observation");
   var policy=client.Call("configure",new{combat="on_failure",failure_limit=3,notifications=false});
   if(Convert.ToString(((Dictionary<string,object>)policy["settings"])["combat"])!="on_failure")throw new Exception("Policy failed");
   bool blocked=false;try{StoryClient.Endpoint("http://example.com/rpc");}catch(InvalidDataException){blocked=true;}if(!blocked)throw new Exception("Non-loopback endpoint accepted");
   client.Call("stop",new{});
   File.WriteAllText(Path.Combine(directory,"result.txt"),"PASS: real StoryService status, takeover, safe resume, combat policy, stop, loopback validation");
   return 0;
  }catch(Exception e){File.WriteAllText(Path.Combine(directory,"result.txt"),"FAIL: "+e);return 1;}
 }
}
