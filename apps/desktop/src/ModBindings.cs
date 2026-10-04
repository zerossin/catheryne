using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Input;

internal sealed class ModKeyOverride {
 public string Path,Section,Option,Original,Value;
 public int Occurrence;
}
internal sealed class ModBinding {
 internal string ModId,Name,Path,Section,Option,Original,Value;
 internal int Occurrence,Line;
 internal bool Editable;
 internal bool Matches(ModKeyOverride value){return Path==value.Path&&Section==value.Section&&Option==value.Option&&Occurrence==value.Occurrence;}
}
// Read the engine's bindings, and edit only rebuildable copies of managed mods.
internal static class ModBindings {
 internal static IEnumerable<string> IniFiles(string folder){return Directory.Exists(folder)?ModManager.SafeFiles(folder).Where(file=>System.IO.Path.GetExtension(file).Equals(".ini",StringComparison.OrdinalIgnoreCase)&&!file.Substring(folder.TrimEnd(System.IO.Path.DirectorySeparatorChar).Length+1).Split(System.IO.Path.DirectorySeparatorChar).Any(part=>part.StartsWith("DISABLED",StringComparison.OrdinalIgnoreCase))):Enumerable.Empty<string>();}
 internal static ModBinding[] Read(ManagedMod item,ModManager manager){
  string folder=ModManager.Source(item,manager);return ReadFolder(folder,item.Id,ModBuiltins.NameOf(item),item.ExternalFolder==null,item.Keys).ToArray();
 }
 internal static IEnumerable<ModBinding> ReadFolder(string folder,string id,string name,bool editable,IEnumerable<ModKeyOverride> overrides=null){
  foreach(string file in IniFiles(folder).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)){
   if(new FileInfo(file).Length>4*1024*1024)throw new InvalidDataException("모드 설정 파일이 너무 큽니다.");
   string section="";int number=0;var counts=new Dictionary<string,int>();
   foreach(string line in File.ReadAllLines(file)){
    int index=number++;var header=Regex.Match(line,@"^\s*\[([^\]]+)\]");if(header.Success){section=header.Groups[1].Value;continue;}
    if(!section.StartsWith("Key",StringComparison.OrdinalIgnoreCase))continue;
    var entry=Regex.Match(line,@"^\s*(key|back|reload_config|reload_fixes|wipe_user_config|toggle_input)\s*=\s*([^;\r\n]+)",RegexOptions.IgnoreCase);if(!entry.Success)continue;
    string option=entry.Groups[1].Value.ToLowerInvariant(),counter=section+"/"+option;int occurrence;counts.TryGetValue(counter,out occurrence);counts[counter]=occurrence+1;
    var binding=new ModBinding{ModId=id,Name=name,Path=file.Substring(folder.TrimEnd(System.IO.Path.DirectorySeparatorChar).Length+1),Section=section,Option=option,Occurrence=occurrence,Line=index,Original=entry.Groups[2].Value.Trim(),Editable=editable};binding.Value=binding.Original;
    var setting=(overrides??Enumerable.Empty<ModKeyOverride>()).SingleOrDefault(binding.Matches);if(setting!=null){if(setting.Original!=binding.Original)throw new InvalidDataException(Locale.Format("단축키 원본이 변경되었습니다: {0}",name));binding.Value=setting.Value;}
    yield return binding;
   }
  }
 }
 internal static ModBinding[] Active(ModState state,ModManager manager){
  var keys=state.Items.Where(x=>x.Enabled).SelectMany(x=>Read(x,manager)).ToList();string runtime=ModIntegration.RuntimeRoot(manager.Root,false);
  if(runtime!=null){string importer=System.IO.Path.GetDirectoryName(ModIntegration.ModsFolder(runtime));string core=System.IO.Path.Combine(importer,"Core");keys.AddRange(ReadFolder(core,"runtime",Locale.T("실행 환경"),false));string main=System.IO.Path.Combine(importer,"d3dx.ini");if(File.Exists(main)){
    // d3dx.ini is the engine's main config. Its folder is not recursively scanned.
    keys.AddRange(ReadMain(main));
   }}return keys.ToArray();
 }
 static IEnumerable<ModBinding> ReadMain(string file){string section="";foreach(string line in File.ReadAllLines(file)){var header=Regex.Match(line,@"^\s*\[([^\]]+)\]");if(header.Success){section=header.Groups[1].Value;continue;}if(section!="Hunting"&&section!="Input")continue;var entry=Regex.Match(line,@"^\s*(reload_config|reload_fixes|wipe_user_config|toggle_input)\s*=\s*([^;\r\n]+)",RegexOptions.IgnoreCase);if(entry.Success)yield return new ModBinding{ModId="runtime",Name=Locale.T("실행 환경"),Section=section,Option=entry.Groups[1].Value,Value=entry.Groups[2].Value.Trim(),Original=entry.Groups[2].Value.Trim()};}}
 sealed class Chord {internal int Key,Required,Forbidden;}
 static Chord Parse(string value){
  var result=new Chord();foreach(string raw in value.ToUpperInvariant().Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries)){
   string token=raw;bool negative=token.StartsWith("NO_");if(negative)token=token.Substring(3);if(token=="MODIFIERS"){if(!negative)return null;result.Forbidden|=31;continue;}token=token.Replace("VK_","");int bit=token=="SHIFT"?1:token=="CTRL"||token=="CONTROL"?2:token=="ALT"||token=="MENU"?4:token=="LWIN"?8:token=="RWIN"?16:0;
   if(bit!=0){if(negative)result.Forbidden|=bit;else result.Required|=bit;continue;}if(negative||result.Key!=0)return null;
   if(token=="WIN")return null;if(token=="=")token="OEMPLUS";if(token=="+")token="ADD";if(token=="-")token="SUBTRACT";if(token=="RETURN")token="ENTER";if(token=="OEM_PLUS")token="OEMPLUS";if(token=="OEM_MINUS")token="OEMMINUS";if(token=="ESC")token="ESCAPE";
   System.Windows.Forms.Keys key;int number;if(token.StartsWith("0X")&&int.TryParse(token.Substring(2),System.Globalization.NumberStyles.HexNumber,null,out number))result.Key=number;else if(token.Length==1&&char.IsLetterOrDigit(token[0]))result.Key=(int)token[0];else if(Enum.TryParse<System.Windows.Forms.Keys>(token,true,out key))result.Key=(int)key;else return null;
  }return result.Key>0&&(result.Required&result.Forbidden)==0?result:null;
 }
 internal static bool Overlap(string left,string right){var a=Parse(left);var b=Parse(right);return a!=null&&b!=null&&a.Key==b.Key&&(a.Required&b.Forbidden)==0&&(b.Required&a.Forbidden)==0;}
 internal static string[] Conflicts(ModBinding key,IEnumerable<ModBinding> active){return active.Where(other=>!(other.ModId==key.ModId&&other.Path==key.Path&&other.Section==key.Section&&other.Option==key.Option&&other.Occurrence==key.Occurrence)&&!(other.ModId=="runtime"&&key.ModId=="runtime")&&Overlap(key.Value,other.Value)).Select(x=>x.Name).Distinct().ToArray();}
 internal static string Capture(Key key,ModifierKeys modifiers){if(key==Key.LeftCtrl||key==Key.RightCtrl||key==Key.LeftAlt||key==Key.RightAlt||key==Key.LeftShift||key==Key.RightShift||key==Key.LWin||key==Key.RWin||key==Key.None||(modifiers&ModifierKeys.Windows)!=0)return null;return string.Join(" ",new[]{(modifiers&ModifierKeys.Shift)!=0?"SHIFT":"NO_SHIFT",(modifiers&ModifierKeys.Control)!=0?"CTRL":"NO_CTRL",(modifiers&ModifierKeys.Alt)!=0?"ALT":"NO_ALT","NO_LWIN NO_RWIN","0x"+KeyInterop.VirtualKeyFromKey(key).ToString("X2")});}
 internal static string Display(string value){var chord=Parse(value);if(chord==null)return value;return ((chord.Required&2)!=0?"Ctrl+":"")+((chord.Required&4)!=0?"Alt+":"")+((chord.Required&1)!=0?"Shift+":"")+((chord.Required&24)!=0?"Win+":"")+(chord.Key>=0x30&&chord.Key<=0x39?((char)chord.Key).ToString():chord.Key==0xBB?"+":chord.Key==0xBD?"-":((System.Windows.Forms.Keys)chord.Key).ToString());}
 internal static bool CanEdit(ModBinding binding){return binding.Editable&&Parse(binding.Original)!=null;}
 internal static void Validate(string value){if(Parse(value)==null)throw new ArgumentException("단축키를 확인해 주세요.");}
 internal static void Project(ManagedMod item,ModManager manager,string destination){
  if(item.Keys==null||item.Keys.Count==0)return;var bindings=Read(item,manager);
  var changes=item.Keys.Select(setting=>{var key=bindings.SingleOrDefault(x=>x.Matches(setting));if(key==null)throw new InvalidDataException(Locale.Format("단축키 원본이 변경되었습니다: {0}",item.Name));Validate(setting.Value);return new{Key=key,Value=setting.Value};}).ToArray();
  foreach(var group in changes.GroupBy(x=>x.Key.Path,StringComparer.OrdinalIgnoreCase)){
   string file=Components.SafeArchivePath(destination,group.Key);var values=group.ToDictionary(x=>x.Key.Line,x=>x.Value);int line=-1;
   string text=Regex.Replace(File.ReadAllText(file),@"(?m)^[^\r\n]*",match=>{line++;string value;if(!values.TryGetValue(line,out value))return match.Value;return Regex.Replace(match.Value,@"^(\s*(?:key|back)\s*=\s*)[^;\r\n]*",x=>x.Groups[1].Value+value+" ",RegexOptions.IgnoreCase);});
   File.WriteAllText(file,text,new System.Text.UTF8Encoding(false));
  }
 }
}
