using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class ArtifactReviewTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception("Artifact review: "+message);}
 static Dictionary<string,object> Item(string id,int level=20,double multiplier=1){return new Dictionary<string,object>{{"id",id},{"setKey","EmblemOfSeveredFate"},{"slotKey","flower"},{"mainStatKey","hp"},{"rarity",5},{"level",level},{"location",""},{"lock",false},{"substats",new[]{new Dictionary<string,object>{{"key","critRate_"},{"value",multiplier==1?5.4:multiplier==2?6.2:7.0}},new Dictionary<string,object>{{"key","critDMG_"},{"value",multiplier==1?10.9:multiplier==2?12.4:14.0}},new Dictionary<string,object>{{"key","atk_"},{"value",multiplier==1?8.2:multiplier==2?9.3:10.5}},new Dictionary<string,object>{{"key","enerRech_"},{"value",multiplier==1?9.1:multiplier==2?10.4:11.7}}}}};}
 static KeyValuePair<string,BuildCriterion>[] Criteria(){return new[]{new KeyValuePair<string,BuildCriterion>("시험 캐릭터",new BuildCriterion{Useful=new[]{"critRate_","critDMG_","atk_","enerRech_"},Sands=new[]{"enerRech_"},Goblet=new[]{"hydro_dmg_"},Circlet=new[]{"critRate_"}})};}
 static ArtifactReviewReport Review(Dictionary<string,object>[] items,Dictionary<string,object>[] saved=null){return ArtifactReview.Assess(items,Criteria(),saved??new Dictionary<string,object>[0]);}
 internal static void Run(){
  var weak=Item("weak");var strong=Item("strong",20,2);var best=Item("best",20,3);var items=new[]{weak,strong,best};string before=CatheryneTools.Json().Serialize(items);
  var report=Review(items);Check(report.Items[0].Status=="candidate"&&report.Items[0].Alternatives.Length==2,"two stronger same-type pieces form a conservative candidate");
  Check(report.Items[1].Status!="candidate"&&report.Items[2].Status!="candidate","at least two stronger alternatives are kept, even in a dominance chain");
  Check(before==CatheryneTools.Json().Serialize(items),"classification never mutates inventory or lock state");
  Check(Review(new[]{weak,strong}).Items[0].Status!="candidate","one replacement is insufficient");
  var locked=Item("locked");locked["lock"]=true;var equipped=Item("equipped");equipped["location"]="Xingqiu";
  Check(Review(new[]{locked,equipped,strong,best}).Items.Take(2).All(x=>x.Status=="protected"),"game lock and equipped items are protected");
  Check(Review(items,new[]{weak}).Items[0].Status=="protected","saved build equipment is protected by instance ID");
  var saved=Item("old-id");saved.Remove("id");Check(Review(items,new[]{saved}).Items[0].Status=="protected","matching saved stats protect ambiguous instances too");
  var missing=Item("missing");missing.Remove("lock");Check(Review(new[]{missing,strong,best}).Items[0].Status=="review","missing lock cannot become unlocked");
  missing=Item("missing-location");missing["location"]=null;Check(Review(new[]{missing,strong,best}).Items[0].Status=="review","unknown ownership cannot become unequipped");
  var unresolved=Item("pending");unresolved["inventoryMatch"]="unresolved";Check(Review(new[]{unresolved,strong,best}).Items[0].Status=="review","pending HoYoLAB observations cannot be disposal candidates");
  var immature=Item("immature",0);immature["substats"]=new[]{new Dictionary<string,object>{{"key","critRate_"},{"value",2.7}},new Dictionary<string,object>{{"key","atk_"},{"value",4.1}},new Dictionary<string,object>{{"key","def"},{"value",16}}};
  var potential=Review(new[]{immature,strong,best}).Items[0];Check(potential.Status=="level"&&potential.PotentialRolls>potential.UsefulRolls,"a hidden fourth stat and future rolls protect an unfinished item");
  var fourStar=Item("instructor");fourStar["rarity"]=4;fourStar["level"]=16;fourStar["setKey"]="Instructor";Check(Review(new[]{fourStar}).Items[0].Status=="review","special four-star sets are not blanket fodder");
  var wrongMain=Item("bad-main");wrongMain["mainStatKey"]="critRate_";Check(Review(new[]{wrongMain,strong,best}).Items[0].Status=="review","impossible slot/main and repeated main/substat stay unverified");
  var different=Item("different",20,3);different["setKey"]="NoblesseOblige";Check(Review(new[]{weak,strong,different}).Items[0].Status!="candidate","another set cannot replace same-set demand");
  var duplicate=Item("strong",20,2);Check(Review(new[]{weak,strong,duplicate}).Items[0].Status!="candidate","the same instance cannot count as two replacements");
  Check(ArtifactReview.Assess(items,Criteria(),new Dictionary<string,object>[0],true).Items.All(x=>x.Status=="review"),"unreadable saved build data suppresses all candidates");
  Check(ArtifactReview.Assess(items,new KeyValuePair<string,BuildCriterion>[0],new Dictionary<string,object>[0]).Items[0].Status=="candidate","componentwise comparison does not need guessed character criteria");
  var watch=Stopwatch.StartNew();var many=Enumerable.Range(0,2000).Select(i=>{var a=Item("mass-"+i);return a;}).ToArray();Review(many);watch.Stop();Check(watch.ElapsedMilliseconds<8000,"2000-item review is bounded");File.WriteAllText(Path.Combine(Path.GetTempPath(),"catheryne-artifact-review-timing.txt"),"2000 items: "+watch.ElapsedMilliseconds+" ms");
  Persistence(items);Ui(report);
 }

 static void Persistence(Dictionary<string,object>[] items){
  string root=Path.Combine(Path.GetTempPath(),"catheryne-artifact-query-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var account=new Dictionary<string,object>{{"format","GOOD"},{"version",1},{"characters",new object[0]},{"artifacts",items}};
   string file=Path.Combine(root,"fixture.json");File.WriteAllText(file,CatheryneTools.Json().Serialize(account));new ProfileStore(root).Import("account",file);
   var tools=new CatheryneTools(root);var query=CodexChat.Map(CatheryneTools.Json().DeserializeObject(CatheryneTools.Json().Serialize(tools.Query("artifact_review",0,"candidate"))));
   Check(Convert.ToInt32(query["total"])==1&&Equals(query["automatic_disposal"],false),"AI and GUI share bounded read-only candidate decisions");
   Dictionary<string,object> pointer;var snapshot=tools.AccountSnapshot(out pointer);var savedFolder=Path.Combine(root,"endgame","accounts",EndgameKnowledge.Hash("|"));Directory.CreateDirectory(savedFolder);string saved=Path.Combine(savedFolder,"abyss.json");
   File.WriteAllText(saved,CatheryneTools.Json().Serialize(new{builds=new[]{new{slots=new[]{new{members=new[]{new{artifacts=new[]{items[0]}}}}}}}}));
   Check(ArtifactReview.Load(root,snapshot).Items[0].Status=="protected","selected-account frozen build protection uses the actual canonical store");
   File.WriteAllText(saved,"{\"builds\":null}");
   var failed=ArtifactReview.Load(root,snapshot);Check(failed.Error!=null&&failed.Items.All(x=>x.Status=="review"),"malformed saved builds fail closed through the public projection");
  }finally{Check(Path.GetDirectoryName(Path.GetFullPath(root)).Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("catheryne-artifact-query-",StringComparison.Ordinal),"temporary test cleanup stays in its named directory");Directory.Delete(root,true);}
 }
 static System.Collections.Generic.IEnumerable<T> Children<T>(DependencyObject node) where T:DependencyObject {foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()){if(child is T)yield return (T)child;foreach(var nested in Children<T>(child))yield return nested;}}
 static object Get(object owner,string name){return owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);}
 static void Set(object owner,string name,object value){owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(owner,value);}
 static void Invoke(object owner,string name,params object[] args){owner.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,args);}
 static void Ui(ArtifactReviewReport report){
  using(var shell=new Launcher(null,false,true,false,false,connectAi:false)){
  var panel=new InventoryPanel();string draft=null;panel.Ask=question=>draft=question;var host=new Grid{Background=new SolidColorBrush(Color.FromRgb(30,34,40))};shell.Window.Content=host;host.Children.Add(panel.View);Set(panel,"section","artifacts");Set(panel,"snapshot",new Dictionary<string,object>{{"artifacts",report.Items.Select(x=>x.Artifact).ToArray()}});Set(panel,"records",report.Items.Select(x=>x.Artifact).ToList());
  Set(panel,"browse",Activator.CreateInstance(typeof(InventoryPanel).GetNestedType("BrowseState",BindingFlags.NonPublic),true));
  Set(panel,"artifactReviews",report.Items.ToDictionary(x=>CatheryneTools.Json().Serialize(x.Artifact),x=>x));
  Invoke(panel,"BindFilters");Invoke(panel,"Filter");var selectors=(Dictionary<string,ComboBox>)Get(panel,"selectors");selectors["review"].SelectedItem=ArtifactReview.Label("candidate");
  Check(((System.Collections.IList)Get(panel,"filtered")).Count==1,"review filter applies to the existing inventory projection");
  foreach(double width in new[]{1100.0,520.0}){
   panel.View.Measure(new Size(width,620));panel.View.Arrange(new Rect(0,0,width,620));panel.View.UpdateLayout();
   var buttons=((System.Windows.Controls.WrapPanel)Get(panel,"cards")).Children.OfType<Button>().ToArray();Check(buttons.Length==1,"candidate page has one accessible selection");
   buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));panel.View.UpdateLayout();Check(((StackPanel)Get(panel,"details")).Children.Count==2,"selection presents judgment and existing equipment details");
   var image=new RenderTargetBitmap((int)width,620,96,96,PixelFormats.Pbgra32);image.Render(panel.View);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(Path.GetTempPath(),"catheryne-artifact-review-"+(int)width+".png")))png.Save(file);
  }
  var consult=Children<Button>((StackPanel)Get(panel,"details")).Single(x=>Equals(x.Content,Locale.T("AI 상담")));consult.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(draft!=null&&draft.Contains("artifact_review"),"AI consultation prepares a draft from the same review without sending it");
  var search=(TextBox)Get(panel,"search");search.Text="no-such-artifact";Check(((System.Collections.IList)Get(panel,"filtered")).Count==0,"empty search clears selection without changing inventory");
  }
 }
}
