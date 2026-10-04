using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

// One script-aware family for all ordinary app text. Icon and code faces remain explicit.
internal static class Typography {
 internal static FontFamily Create(string gameExecutable,string fontDirectory) {
  string gameFonts=String.IsNullOrEmpty(gameExecutable)?null:Path.Combine(Path.GetDirectoryName(gameExecutable),"GenshinImpact_Data","StreamingAssets","MiHoYoSDKRes","HttpServerResources","font");
  string sc=GameFace(gameFonts,"zh-cn.ttf");
  string jp=GameFace(gameFonts,"ja-jp.ttf");
  string ko=File.Exists(Path.Combine(fontDirectory,"NanumGothic-Regular.ttf"))?new Uri(Path.GetFullPath(fontDirectory)+Path.DirectorySeparatorChar).AbsoluteUri+"#NanumGothic":"Malgun Gothic";
  var family=new FontFamily();
  Add(family,"0000-024F,2000-206F,20A0-20CF,2100-214F,FF00-FFEF",ko+", Malgun Gothic","ko");
  Add(family,"1100-11FF,3130-318F,A960-A97F,AC00-D7FF",ko+", Malgun Gothic",null);
  Add(family,"3000-30FF,31F0-31FF,3400-4DBF,4E00-9FFF,F900-FAFF,FF00-FFEF,20000-2FA1F",WithFallback(jp,"Yu Gothic UI"),"ja");
  Add(family,"3000-30FF,31F0-31FF,3400-4DBF,4E00-9FFF,F900-FAFF,FF00-FFEF,20000-2FA1F",WithFallback(sc,"Microsoft JhengHei UI"),"zh-Hant");
  Add(family,"3040-30FF,31F0-31FF",WithFallback(jp,"Yu Gothic UI"),null);
  Add(family,"3000-303F,3400-4DBF,4E00-9FFF,F900-FAFF,FF00-FFEF,20000-2FA1F",WithFallback(sc,"Microsoft YaHei UI"),null);
  Add(family,"0000-10FFFF",WithFallback(sc,"Segoe UI"),null);
  return family;
 }
 static string GameFace(string directory,string file) {
  if(directory==null||!File.Exists(Path.Combine(directory,file)))return null;
  try { foreach(var family in Fonts.GetFontFamilies(new Uri(Path.GetFullPath(directory)+Path.DirectorySeparatorChar),file))return new Uri(family.BaseUri,file).AbsoluteUri+family.Source.Substring(family.Source.IndexOf("#",StringComparison.Ordinal)); } catch(IOException){} catch(ArgumentException){} catch(NotSupportedException){}
  return null;
 }
 static string WithFallback(string face,string fallback){return face==null?fallback:face+", "+fallback;}
 static void Add(FontFamily family,string range,string target,string language){family.FamilyMaps.Add(new FontFamilyMap{Unicode=range,Target=target,Language=language==null?null:XmlLanguage.GetLanguage(language)});}
 internal static void Apply(Window window,string language,string gameExecutable){
  window.Language=XmlLanguage.GetLanguage(language);
  window.FontSize=14;
  window.FontWeight=language.StartsWith("ko",StringComparison.OrdinalIgnoreCase)?FontWeights.Bold:FontWeights.Normal;
  TextOptions.SetTextFormattingMode(window,TextFormattingMode.Ideal);
  TextOptions.SetTextRenderingMode(window,TextRenderingMode.Grayscale);
  window.FontFamily=Create(gameExecutable,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fonts"));
 }
}

