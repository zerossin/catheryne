using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

internal sealed class BuildReferenceRow { public int Row {get;set;} public Dictionary<string,string> Values {get;set;} public Dictionary<string,string> Notes {get;set;} public Dictionary<string,string> Colors {get;set;} }
internal sealed class BuildReferenceBlock { public string Character {get;set;} public string Annotation {get;set;} public int First {get;set;} public int Last {get;set;} public List<BuildReferenceRow> Rows {get;set;} }
internal sealed class BuildReferenceSheet { public string Name {get;set;} public string Url {get;set;} public string Version {get;set;} public string Conditions {get;set;} public List<BuildReferenceBlock> Characters {get;set;} }
internal sealed class BuildReferenceData { public int Parser {get;set;} public string CheckedUtc {get;set;} public string FetchedUtc {get;set;} public string Hash {get;set;} public string Error {get;set;} public List<BuildReferenceSheet> Sheets {get;set;} }

// The remote workbook is authoritative. This disposable cache contains no hand-maintained builds.
internal static class BuildReferences {
 internal const string Workbook="https://docs.google.com/spreadsheets/d/1sjVkeR8s41wW0oTtBHC1at9riOxWqyXPcbJscYI8fdE";
 static readonly XNamespace Main="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
 static readonly XNamespace Rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
 static readonly Dictionary<string,string> Sources=new Dictionary<string,string>{{"이정도면잘키웠다","1394698652"},{"올인원준종결표","1110572553"}};
 static string Compact(string text){return Regex.Replace(text??"",@"\s+","");}
 static string Digest(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
 internal static BuildReferenceData Read(string root){try{string file=Path.Combine(root,"cache","build-references.json");var data=File.Exists(file)?CatheryneTools.Json().Deserialize<BuildReferenceData>(File.ReadAllText(file)):null;return data!=null&&data.Parser==2&&data.Sheets!=null?data:null;}catch(IOException){return null;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}}
 internal static BuildReferenceData Load(string root,bool force=false,Func<byte[]> download=null){
  string file=Path.Combine(root,"cache","build-references.json");Directory.CreateDirectory(Path.GetDirectoryName(file));var old=Read(root);DateTime checkedAt;
  if(!force&&old!=null&&DateTime.TryParse(old.CheckedUtc,out checkedAt)&&DateTime.UtcNow-checkedAt.ToUniversalTime()<TimeSpan.FromHours(string.IsNullOrEmpty(old.Error)?24:1))return old;
  using(var mutex=new Mutex(false,"Local\\Catheryne.BuildReferences."+Digest(Encoding.UTF8.GetBytes(Path.GetFullPath(root))).Substring(0,16))){
   bool held=false;
   try{
    try{held=mutex.WaitOne(old==null?45000:0);}catch(AbandonedMutexException){held=true;}
    if(!held)return old??new BuildReferenceData{Parser=2,Error="기준표를 갱신하고 있습니다.",Sheets=new List<BuildReferenceSheet>()};
    var latest=Read(root);if(!force&&latest!=null&&latest.CheckedUtc!=(old==null?null:old.CheckedUtc))return latest;
    try{
     byte[] bytes=(download??Download)();
     string hash=Digest(bytes);var data=old!=null&&old.Hash==hash?old:Parse(bytes);
     if(old!=null&&old.Sheets.Count>0&&data.Sheets.Sum(s=>s.Characters.Count)<old.Sheets.Sum(s=>s.Characters.Count)*0.8)throw new InvalidDataException("기준표 구성이 크게 바뀌어 갱신을 보류했습니다.");
     data.Parser=2;data.Hash=hash;data.FetchedUtc=data.CheckedUtc=DateTime.UtcNow.ToString("o");data.Error=null;AtomicFile.Write(file,CatheryneTools.Json().Serialize(data));return data;
    }catch(Exception error){
     var data=old??new BuildReferenceData{Parser=2,Sheets=new List<BuildReferenceSheet>()};data.CheckedUtc=DateTime.UtcNow.ToString("o");data.Error=error is InvalidDataException?error.Message:"기준표를 갱신하지 못했습니다.";AtomicFile.Write(file,CatheryneTools.Json().Serialize(data));return data;
    }
   }finally{if(held)mutex.ReleaseMutex();}
  }
 }
 static byte[] Download(){
  var request=HttpTransport.Create(Workbook+"/export?format=xlsx");request.Timeout=30000;request.ReadWriteTimeout=30000;
  using(var response=request.GetResponse())using(var input=response.GetResponseStream())using(var output=new MemoryStream()){
   byte[] buffer=new byte[65536];int count;while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>48*1024*1024)throw new InvalidDataException("기준표 파일 크기가 허용 범위를 넘었습니다.");output.Write(buffer,0,count);}return output.ToArray();
  }
 }
 static XDocument Xml(ZipArchive zip,string path){var entry=zip.GetEntry(path);if(entry==null||entry.Length>8*1024*1024)throw new InvalidDataException("기준표 문서 구조를 확인하지 못했습니다.");using(var stream=entry.Open())using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))return XDocument.Load(reader);}
 static string Part(string target){if(target.Contains("..")||target.Contains(":"))throw new InvalidDataException("잘못된 기준표 경로입니다.");return target.StartsWith("/")?target.TrimStart('/'):"xl/"+target;}
 static int Column(string cell){int value=0;foreach(char c in cell.TakeWhile(char.IsLetter))value=value*26+c-'A'+1;return value-1;}
 static int Row(string cell){return int.Parse(new string(cell.SkipWhile(char.IsLetter).ToArray()));}
 sealed class Area {internal int Left,Right,Top,Bottom;internal string Anchor;internal bool Contains(int col,int row){return col>=Left&&col<=Right&&row>=Top&&row<=Bottom;}}
 static Area Range(string value){string[] ends=value.Split(':');string end=ends.Length==1?ends[0]:ends[1];return new Area{Anchor=ends[0],Left=Column(ends[0]),Right=Column(end),Top=Row(ends[0]),Bottom=Row(end)};}
 static string Key(int col,int row){string key="";for(int c=col+1;c>0;c=(c-1)/26)key=(char)('A'+(c-1)%26)+key;return key+row;}
 static string Cell(Dictionary<string,string> values,List<Area> merged,int col,int row){string value;if(values.TryGetValue(Key(col,row),out value)&&value.Length>0)return value;var range=merged.FirstOrDefault(a=>a.Contains(col,row));return range!=null&&values.TryGetValue(range.Anchor,out value)?value:"";}
 internal static BuildReferenceData Parse(byte[] bytes){
  var result=new BuildReferenceData{Parser=2,Sheets=new List<BuildReferenceSheet>()};
  using(var input=new MemoryStream(bytes))using(var zip=new ZipArchive(input,ZipArchiveMode.Read)){
   var styleXml=Xml(zip,"xl/styles.xml");var fonts=styleXml.Root.Element(Main+"fonts").Elements().ToArray();var styles=styleXml.Root.Element(Main+"cellXfs").Elements().ToArray();
   var strings=Xml(zip,"xl/sharedStrings.xml").Root.Elements(Main+"si").Select(x=>string.Concat(x.Descendants(Main+"t").Select(t=>t.Value))).ToArray();
   var relations=Xml(zip,"xl/_rels/workbook.xml.rels").Root.Elements().ToDictionary(x=>(string)x.Attribute("Id"),x=>Part((string)x.Attribute("Target")));
   foreach(var sheet in Xml(zip,"xl/workbook.xml").Descendants(Main+"sheet")){
    string name=(string)sheet.Attribute("name"),gid;if(!Sources.TryGetValue(Compact(name),out gid))continue;
    string part=relations[(string)sheet.Attribute(Rel+"id")];var xml=Xml(zip,part);var values=new Dictionary<string,string>();var colors=new Dictionary<string,string>();
    foreach(var cell in xml.Descendants(Main+"c")){var value=cell.Element(Main+"v");string text=value==null?string.Concat(cell.Descendants(Main+"t").Select(x=>x.Value)):value.Value;if((string)cell.Attribute("t")=="s"){int index;if(!int.TryParse(text,out index)||index<0||index>=strings.Length)throw new InvalidDataException("기준표 텍스트 참조가 바뀌었습니다.");text=strings[index];}values[(string)cell.Attribute("r")]=text;int style; if(int.TryParse((string)cell.Attribute("s"),out style)&&style<styles.Length){int font=int.Parse((string)styles[style].Attribute("fontId")??"0");var color=fonts[font].Element(Main+"color");if(color!=null&&color.Attribute("rgb")!=null)colors[(string)cell.Attribute("r")]=(string)color.Attribute("rgb");}}
    var merged=xml.Descendants(Main+"mergeCell").Select(x=>Range((string)x.Attribute("ref"))).ToList();
    var header=values.FirstOrDefault(x=>Row(x.Key)<12&&(Compact(x.Value)=="캐릭터"||Compact(x.Value)=="캐릭명"));
    if(header.Key==null)throw new InvalidDataException("캐릭터 기준표의 열 구성이 바뀌었습니다.");
    int headerRow=Row(header.Key),firstColumn=Column(header.Key);var headerRange=merged.FirstOrDefault(a=>a.Contains(firstColumn,headerRow));int lastColumn=headerRange==null?firstColumn:headerRange.Right;
    int headerBottom=headerRange==null?headerRow:headerRange.Bottom;
    int width=values.Keys.Select(x=>Column(x)).Max()+1;if(width>64)throw new InvalidDataException("기준표의 열 범위를 확인하지 못했습니다.");
    var headers=new Dictionary<int,string>();for(int col=lastColumn+1;col<width;col++){var parts=Enumerable.Range(headerRow,headerBottom-headerRow+1).Select(row=>Cell(values,merged,col,row)).Where(v=>!string.IsNullOrWhiteSpace(v)&&v.Length<100).Distinct();headers[col]=string.Join(" / ",parts);}
    if(!headers.Values.Any(v=>v.Contains("무기"))||!headers.Values.Any(v=>v.Contains("성유물")))throw new InvalidDataException("기준표의 장비 조건 열이 없습니다.");
    var notes=new Dictionary<string,string>();string relpath=Path.GetDirectoryName(part).Replace('\\','/')+"/_rels/"+Path.GetFileName(part)+".rels";
    if(zip.GetEntry(relpath)!=null)foreach(var relation in Xml(zip,relpath).Root.Elements().Where(x=>((string)x.Attribute("Type")??"").EndsWith("/comments"))){string target=(string)relation.Attribute("Target");string path=target.StartsWith("/")?target.TrimStart('/'):"xl/"+target.Replace("../","");foreach(var note in Xml(zip,path).Descendants(Main+"comment"))notes[(string)note.Attribute("ref")]=string.Concat(note.Descendants(Main+"t").Select(x=>x.Value));}
    var output=new BuildReferenceSheet{Name=name,Url=Workbook+"/edit?gid="+gid,Version=string.Join(" | ",values.Where(x=>Row(x.Key)==1).Select(x=>x.Value).Where(v=>v.Length>0)),Characters=new List<BuildReferenceBlock>()};
    foreach(var cell in values.Where(x=>Column(x.Key)>=firstColumn&&Column(x.Key)<=lastColumn&&Row(x.Key)>headerBottom).OrderBy(x=>Row(x.Key))){
     string character=cell.Value.Split('\n')[0].Trim();if(character.Length==0||character.Length>40||!Regex.IsMatch(character,@"^[가-힣A-Za-z][가-힣A-Za-z ()·]+$"))continue;
     int row=Row(cell.Key),column=Column(cell.Key);var area=merged.FirstOrDefault(a=>a.Contains(column,row));int first=area==null?row:area.Top,last=area==null?row:area.Bottom;
     if(area==null){var picture=merged.FirstOrDefault(a=>a.Left==column&&a.Right==column&&a.Bottom==row-1&&string.IsNullOrWhiteSpace(Cell(values,merged,column,a.Top)));if(picture!=null)first=picture.Top;}
     var block=new BuildReferenceBlock{Character=character,Annotation=cell.Value,First=first,Last=last,Rows=new List<BuildReferenceRow>()};var seen=new HashSet<string>();
     for(int r=first;r<=last;r++){var fields=new Dictionary<string,string>();var comments=new Dictionary<string,string>();var fieldColors=new Dictionary<string,string>();foreach(var field in headers){if(field.Value.Length==0)continue;string value=Cell(values,merged,field.Key,r);if(value.Length>0){string label=field.Value;if(fields.ContainsKey(label)&&fields[label]!=value)fields[label]+="\n"+value;else fields[label]=value;}string note;var noteArea=merged.FirstOrDefault(a=>a.Contains(field.Key,r));if(notes.TryGetValue(noteArea==null?Key(field.Key,r):noteArea.Anchor,out note))comments[field.Value]=note;string color;if(colors.TryGetValue(noteArea==null?Key(field.Key,r):noteArea.Anchor,out color))fieldColors[field.Value]=color;}
      string identity=CatheryneTools.Json().Serialize(fields)+CatheryneTools.Json().Serialize(comments);if(fields.Count>0&&seen.Add(identity))block.Rows.Add(new BuildReferenceRow{Row=r,Values=fields,Notes=comments,Colors=fieldColors});
     }
     if(block.Rows.Count>0)output.Characters.Add(block);
    }
    if(output.Characters.Count<30)throw new InvalidDataException("기준표의 캐릭터 범위를 확인하지 못했습니다.");
    int firstData=output.Characters.Min(c=>c.First);output.Conditions=string.Join("\n",values.Where(x=>Row(x.Key)>headerBottom&&Row(x.Key)<firstData).Select(x=>x.Value).Where(v=>v.Length>10).Distinct());result.Sheets.Add(output);
   }
  }
  if(result.Sheets.Count!=Sources.Count)throw new InvalidDataException("원본 기준표의 탭 구성이 바뀌었습니다.");return result;
 }
}
