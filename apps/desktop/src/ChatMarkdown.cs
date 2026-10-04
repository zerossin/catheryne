using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;

// One native document per message keeps Markdown selectable across paragraphs, lists and tables.
internal sealed class ChatText : RichTextBox {
 static readonly MarkdownPipeline Pipeline=new MarkdownPipelineBuilder().DisableHtml().UsePipeTables().UseEmphasisExtras().UseCjkFriendlyEmphasis().Build();
 readonly bool plain;string value="";
 readonly List<XElement> rendered=new List<XElement>();
 readonly List<double> bottomMargins=new List<double>();
 internal ChatText(bool plain=false){
  this.plain=plain;IsReadOnly=true;IsReadOnlyCaretVisible=false;IsUndoEnabled=false;IsDocumentEnabled=true;BorderThickness=new Thickness(0);Padding=new Thickness(0);Background=Brushes.Transparent;Foreground=Brushes.White;FontSize=14;FocusVisualStyle=null;
  VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
  Document=new FlowDocument{PagePadding=new Thickness(0),ColumnWidth=double.PositiveInfinity,FontSize=14,Foreground=Brushes.White};
  PreviewMouseWheel+=(sender,args)=>{for(DependencyObject parent=PanelInteraction.Parent(this);parent!=null;parent=PanelInteraction.Parent(parent)){var scroll=parent as ScrollViewer;if(scroll==null)continue;scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(args.MouseDevice,args.Timestamp,args.Delta){RoutedEvent=System.Windows.Input.Mouse.MouseWheelEvent});args.Handled=true;break;}};
 }
 internal string Text {get{return value;}set{
  string next=value??"";if(next==this.value&&Document.Blocks.Count>0)return;this.value=next;
  int start=Document.ContentStart.GetOffsetToPosition(Selection.Start),end=Document.ContentStart.GetOffsetToPosition(Selection.End);bool selected=!Selection.IsEmpty;
  try{
   if(plain){if(Document.Blocks.Count==0)Document.Blocks.Add(new Paragraph{Margin=new Thickness(0)});SetPlain((Paragraph)Document.Blocks.FirstBlock,this.value);return;}
   var blocks=Parse(this.value).Elements().ToList();
   var priorBlocks=Document.Blocks.ToList();
   for(int i=0;i<blocks.Count;i++){
    if(i<rendered.Count&&XNode.DeepEquals(rendered[i],blocks[i]))continue;
    var prior=i<priorBlocks.Count?priorBlocks[i]:null;var existing=prior as Paragraph;
    if(existing!=null&&i<rendered.Count&&rendered[i].Name=="p"&&blocks[i].Name=="p"&&!rendered[i].HasElements&&!blocks[i].HasElements)SetPlain(existing,blocks[i].Value);
    else{var block=Block(blocks[i]);if(prior==null)Document.Blocks.Add(block);else{Document.Blocks.InsertBefore(prior,block);Document.Blocks.Remove(prior);}if(i<bottomMargins.Count)bottomMargins[i]=block.Margin.Bottom;else bottomMargins.Add(block.Margin.Bottom);}
   }
   while(Document.Blocks.Count>blocks.Count)Document.Blocks.Remove(Document.Blocks.LastBlock);
   while(bottomMargins.Count>blocks.Count)bottomMargins.RemoveAt(bottomMargins.Count-1);
   rendered.Clear();rendered.AddRange(blocks);
   int index=0;foreach(var block in Document.Blocks){var margin=block.Margin;block.Margin=new Thickness(margin.Left,margin.Top,margin.Right,index==blocks.Count-1?0:bottomMargins[index]);index++;}
  }catch(System.Xml.XmlException){rendered.Clear();bottomMargins.Clear();Document.Blocks.Clear();var fallback=Paragraph();fallback.Margin=new Thickness(0);SetPlain(fallback,this.value);Document.Blocks.Add(fallback);}
  finally{if(selected){int limit=Document.ContentStart.GetOffsetToPosition(Document.ContentEnd);Selection.Select(Document.ContentStart.GetPositionAtOffset(Math.Min(start,limit)),Document.ContentStart.GetPositionAtOffset(Math.Min(end,limit)));}}
 }}
 static void SetPlain(Paragraph paragraph,string text){var run=paragraph.Inlines.FirstInline as Run;if(run!=null&&paragraph.Inlines.Count==1)run.Text=text;else{paragraph.Inlines.Clear();paragraph.Inlines.Add(new Run(text));}}
 internal static XElement Parse(string text){return XElement.Parse("<root>"+Markdown.ToHtml(text,Pipeline)+"</root>");}
 static Paragraph Paragraph(){return new Paragraph{FontSize=14,LineHeight=23,Margin=new Thickness(0,0,0,10)};}
 static void InlineText(InlineCollection target,XContainer node){foreach(var child in node.Nodes()){
  var raw=child as XText;if(raw!=null){target.Add(new Run(raw.Value));continue;}var el=child as XElement;if(el==null)continue;string tag=el.Name.LocalName;
  if(tag=="br"){target.Add(new LineBreak());continue;}if(tag=="img"){target.Add(new Run((string)el.Attribute("alt")??""));continue;}
  Span span=new Span();if(tag=="strong")span.FontWeight=FontWeights.SemiBold;else if(tag=="em")span.FontStyle=FontStyles.Italic;else if(tag=="del")span.TextDecorations=TextDecorations.Strikethrough;else if(tag=="code"){span.FontFamily=new FontFamily("Consolas");span.Background=new SolidColorBrush(Color.FromRgb(48,53,60));}
  else if(tag=="a"){Uri uri;if(Uri.TryCreate((string)el.Attribute("href"),UriKind.Absolute,out uri)&&(uri.Scheme=="https"||uri.Scheme=="http")){var link=new Hyperlink{Foreground=new SolidColorBrush(Color.FromRgb(142,190,230)),NavigateUri=uri};link.RequestNavigate+=(s,e)=>{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;};span=link;}}
  InlineText(span.Inlines,el);target.Add(span);
 }}
 static Block Block(XElement el){string tag=el.Name.LocalName;
  if(tag=="pre"){var code=Paragraph();code.FontFamily=new FontFamily("Consolas");code.FontSize=13;code.Padding=new Thickness(12);code.Background=new SolidColorBrush(Color.FromRgb(27,30,35));code.Margin=new Thickness(0,4,0,14);SetPlain(code,el.Value.TrimEnd('\n'));return code;}
  if(tag=="ul"||tag=="ol"){int index;int.TryParse((string)el.Attribute("start")??"1",out index);var list=new System.Windows.Documents.List{MarkerStyle=tag=="ol"?TextMarkerStyle.Decimal:TextMarkerStyle.Disc,StartIndex=Math.Max(1,index),Padding=new Thickness(28,0,0,0),Margin=new Thickness(4,0,0,8)};
   foreach(var item in el.Elements("li")){var row=new ListItem();if(item.Elements().Any(x=>x.Name.LocalName=="p")){foreach(var child in item.Elements())row.Blocks.Add(Block(child));}else{var paragraph=Paragraph();foreach(var node in item.Nodes().Where(x=>!(x is XElement)||!new[]{"ul","ol"}.Contains(((XElement)x).Name.LocalName))){var holder=new XElement("p",node is XElement?new XElement((XElement)node):(object)new XText(((XText)node).Value));InlineText(paragraph.Inlines,holder);}row.Blocks.Add(paragraph);foreach(var nested in item.Elements().Where(x=>x.Name.LocalName=="ul"||x.Name.LocalName=="ol"))row.Blocks.Add(Block(nested));}list.ListItems.Add(row);}return list;}
  if(tag=="blockquote"){var quote=new Section{BorderBrush=new SolidColorBrush(Color.FromRgb(119,146,149)),BorderThickness=new Thickness(3,0,0,0),Padding=new Thickness(12,0,0,0),Margin=new Thickness(0,4,0,12)};foreach(var child in el.Elements())quote.Blocks.Add(Block(child));return quote;}
  if(tag=="hr")return new Paragraph{BorderThickness=new Thickness(0,1,0,0),BorderBrush=new SolidColorBrush(Color.FromRgb(80,87,96)),FontSize=1,LineHeight=1,Margin=new Thickness(0,12,0,20)};
  if(tag=="table"){var table=new Table{CellSpacing=0,Margin=new Thickness(0,4,0,14)};var rows=el.Descendants("tr").ToList();int columns=rows.Count==0?0:rows.Max(r=>r.Elements().Count());for(int i=0;i<columns;i++)table.Columns.Add(new TableColumn());var group=new TableRowGroup();table.RowGroups.Add(group);for(int i=0;i<rows.Count;i++){var row=new TableRow();group.Rows.Add(row);foreach(var source in rows[i].Elements()){var text=Paragraph();text.Margin=new Thickness(0);InlineText(text.Inlines,source);if(source.Name.LocalName=="th")text.FontWeight=FontWeights.SemiBold;row.Cells.Add(new TableCell(text){Padding=new Thickness(10,7,10,7),BorderThickness=new Thickness(0,0,0,1),BorderBrush=new SolidColorBrush(Color.FromRgb(72,79,88)),Background=i==0?new SolidColorBrush(Color.FromArgb(100,48,54,62)):Brushes.Transparent});}}return table;}
  var p=Paragraph();InlineText(p.Inlines,el);if(tag.Length==2&&tag[0]=='h'){int level;if(int.TryParse(tag.Substring(1),out level)){p.FontSize=level==1?22:level==2?19:16;p.FontWeight=FontWeights.SemiBold;p.Margin=new Thickness(0,14,0,10);}}return p;
 }
 internal static void Test(){
  var root=Parse("## Heading\n\n**bold** and `code`\n\n- One\n- Two\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n<script>bad</script>");if(root.Element("h2")==null||root.Descendants("strong").Count()!=1||root.Element("table")==null||root.Descendants("script").Any())throw new Exception("Markdown contract failed");
  var view=new ChatText{Text="**bold**\n\n```\ncode\n```"};if(view.Document.Blocks.Count!=2||view.Document.Blocks.LastBlock.Margin.Bottom!=0)throw new Exception("Markdown rendering and trailing spacing");
  view.Selection.Select(view.Document.ContentStart,view.Document.ContentEnd);if(!view.Selection.Text.Contains("bold")||!view.Selection.Text.Contains("code"))throw new Exception("Message selection must cross paragraph and code boundaries");
  var user=new ChatText(true){Text="hello"};var first=user.Document.Blocks.FirstBlock;user.Text="second line";if(!ReferenceEquals(first,user.Document.Blocks.FirstBlock)||first.Margin.Bottom!=0)throw new Exception("Plain message must retain its paragraph and spacing");
  view.Text="**first**\n\nsecond";var completed=view.Document.Blocks.FirstBlock;var streaming=view.Document.Blocks.LastBlock;view.Selection.Select(completed.ContentStart,completed.ContentEnd);string selection=view.Selection.Text;view.Text="**first**\n\nsecond grows";if(!ReferenceEquals(completed,view.Document.Blocks.FirstBlock)||!ReferenceEquals(streaming,view.Document.Blocks.LastBlock)||view.Selection.Text!=selection)throw new Exception("Streaming must preserve completed blocks and selection");
  view.Text="**first**\n\nsecond grows\n\nthird";if(view.Document.Blocks.ElementAt(1).Margin.Bottom!=10||view.Document.Blocks.LastBlock.Margin.Bottom!=0)throw new Exception("Streaming paragraph spacing");view.Text="short";if(view.Document.Blocks.Count!=1)throw new Exception("Shorter message must remove old blocks");
  view.Text="- One\n- Two\n\n| A | B |\n|---|---|\n| 1 | 2 |";view.Selection.Select(view.Document.ContentStart,view.Document.ContentEnd);if(!view.Selection.Text.Contains("One")||!view.Selection.Text.Contains("2"))throw new Exception("Lists and tables must be selectable");
 }
}
