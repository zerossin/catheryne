using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Xml.Linq;
internal static class Locale {
 static Locale(){}
 internal static readonly string LanguageCode=ReadLanguage();
 internal static readonly bool IsEnglish=LanguageCode.StartsWith("en",StringComparison.OrdinalIgnoreCase);
 static readonly Dictionary<string,Dictionary<string,string>> Catalogs=new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);
 static string ReadLanguage(){object language;string code=AppPreferences.Read().TryGetValue("language",out language)?Convert.ToString(language):"ko-KR";return System.Text.RegularExpressions.Regex.IsMatch(code??"","^[a-z]{2,3}-[A-Za-z]{2,8}$")?code:"ko-KR";}
 static string Lookup(string value,string code){if(value==null||code=="ko-KR")return value;lock(Catalogs){Dictionary<string,string> catalog;if(!Catalogs.TryGetValue(code,out catalog)){string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,code+".json");catalog=File.Exists(file)?new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(file)):new Dictionary<string,string>();Catalogs[code]=catalog;}string translated;return catalog.TryGetValue(value,out translated)?translated:value;}}
 internal static string T(string value){return Lookup(value,LanguageCode);}
 internal static string T(string value,bool english){return Lookup(value,english?"en-US":"ko-KR");}
 internal static string Format(string value,params object[] args){return string.Format(System.Globalization.CultureInfo.GetCultureInfo(LanguageCode),T(value),args);}
 internal static System.Globalization.CultureInfo Culture {get{return System.Globalization.CultureInfo.GetCultureInfo(LanguageCode);}}
 internal static string[] AvailableLanguages(){return new[]{"ko-KR"}.Concat(Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory,"*.json").Select(Path.GetFileNameWithoutExtension).Where(code=>System.Text.RegularExpressions.Regex.IsMatch(code,"^[a-z]{2,3}-[A-Za-z]{2,8}$"))).Distinct().OrderBy(code=>code=="ko-KR"?"":code).ToArray();}
 internal static string[] Options(params string[] values){return values.Select(T).ToArray();}
 internal static string Xaml(string value){return TranslateXaml(value,LanguageCode);}
 internal static string Xaml(string value,bool english){return TranslateXaml(value,english?"en-US":"ko-KR");}
 static string TranslateXaml(string value,string code){if(code=="ko-KR")return value;var document=XDocument.Parse(value);foreach(var node in document.Descendants())foreach(var attribute in node.Attributes().Where(a=>new[]{"Text","Content","Header","ToolTip","AutomationProperties.Name"}.Contains(a.Name.LocalName)&&a.Name.NamespaceName!="http://schemas.microsoft.com/winfx/2006/xaml"))attribute.Value=Lookup(attribute.Value,code);foreach(var setter in document.Descendants().Where(n=>n.Name.LocalName=="Setter"&&new[]{"Text","Content","Header","ToolTip"}.Contains((string)n.Attribute("Property")))){var v=setter.Attribute("Value");if(v!=null)v.Value=Lookup(v.Value,code);}return document.ToString();}
}
