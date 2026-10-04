using System;
using System.Windows;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class BuiltinPackage {
 public string Folder,File,Hash,Source,Credit,Extension=".zip",SelectPath,DisabledFolder;
}
internal sealed class BuiltinMod {
 public string Id,Key,Name,Help,Source,Preview,Settings,Author,Version,Description,Controls,Group;
 public Rect PreviewRegion=new Rect(0,0,1,1);
 public Point PreviewFocus=new Point(.5,.5);
 public string[] Photos=new string[0];
 public bool Available=true,RequiresUi;
 public int Order;
 public BuiltinPackage[] Packages=new BuiltinPackage[0];
}
internal sealed class BuiltinModGroup {
 public string Key,Name,Description,Help;
}
internal static class ModBuiltins {
 internal static readonly BuiltinModGroup[] Groups={new BuiltinModGroup{Key="water",Name="수중·우주 화면 정리",Description="수중과 우주 이동의 필터, 기포, 손발 잔상을 각각 정리합니다.",Help="필터·기포·잔상을 개별로 선택합니다. 같은 제작자의 원본 패키지를 사용하며 다음 게임 실행부터 적용됩니다."}};
 internal static string NameOf(ManagedMod item){return item.Builtin==null?item.Name:Locale.T(Catalog.Single(x=>x.Key==item.Builtin).Name);}
 internal static BuiltinModGroup GroupOf(ManagedMod item){var spec=Catalog.FirstOrDefault(x=>x.Key==item.Builtin);return spec==null||spec.Group==null?null:Groups.Single(x=>x.Key==spec.Group);}
 // UI entries are a projection of existing option identities and their desired state.
 internal static System.Collections.Generic.IEnumerable<ManagedMod[]> ListEntries(System.Collections.Generic.IEnumerable<ManagedMod> items){
  var ordered=Ordered(items).ToArray();var seen=new System.Collections.Generic.HashSet<string>();
  foreach(var item in ordered){var group=GroupOf(item);if(group==null)yield return new[]{item};else if(seen.Add(group.Key))yield return ordered.Where(x=>GroupOf(x)==group).ToArray();}
 }

 static string TransparencySettings {get{return "; TexFx user setting: https://github.com/SinsOfSeven/TexFx/blob/main/Config.ini\nnamespace = Catheryne.Transparency\n[Constants]\npost $\\TexFx\\uncensor = 0\n";}}
 static readonly BuiltinPackage UiLibrary=new BuiltinPackage{Folder="UILib",File="1640178",Hash="2b05294db44dddf1055e23fade707047bdfc0a568d39f8505aa78783cab4a9eb",Source="https://gamebanana.com/mods/616408",Credit="Unicornshell, GPLv3"};
 static BuiltinPackage Water(string file=null){return new BuiltinPackage{Folder="Water",File="1781618",Hash="fe71a087bbc98757cd17f5531da3aa94c5bf34ac13174867c1ddc129f9d47fd8",Source="https://gamebanana.com/mods/699780",Credit="Radorei and a4happy20, CC BY-NC-SA 4.0; original author download",SelectPath=file};}
 internal static readonly BuiltinMod[] Catalog={
  new BuiltinMod{Id="ca000000000000000000000000000001",Key="hide-uid",Author="Unicornshell",Version="UIDeleter 1.2.5 / UILib 2.0",Description="화면과 메뉴에 표시되는 UID를 숨깁니다. 스크린샷이나 방송 화면을 정리할 때 사용할 수 있습니다.",Controls="F11: UID 표시 전환",Photos=new[]{"https://images.gamebanana.com/img/ss/mods/68c8d41088fe3.jpg","https://images.gamebanana.com/img/ss/mods/68c8d425c9896.jpg","https://images.gamebanana.com/img/ss/mods/68c8d41899fe4.jpg","https://images.gamebanana.com/img/ss/mods/68c8d4afdf62a.jpg"},Order=4,RequiresUi=true,Preview="https://images.gamebanana.com/img/ss/mods/68c8d425c9896.jpg",PreviewRegion=new Rect(0.04,0.01,0.37,0.28),PreviewFocus=new Point(0.52,0.58),Name="UID 숨기기",Source="https://gamebanana.com/mods/620520",Help="UIDeleter와 UI Scale & Padding Library를 제작자 배포처에서 준비합니다. 화면·메뉴의 UID를 숨기며 게임 안에서는 F11로 전환할 수 있습니다. 처음 메뉴를 열 때 보정 중에는 일부 글자가 잠시 숨겨질 수 있습니다. 전체 화면 녹화의 개인정보 보호를 보장하는 기능은 아닙니다. UIDeleter 1.2.5와 UILib 2.0을 검증된 버전으로 사용합니다. 새 배포 파일을 임의로 자동 교체하지 않습니다.",Packages=new[]{new BuiltinPackage{Folder="UIDeleter",File="1746597",Hash="0d9a61c72372a7babae273075b13e613bdfb8372d5be8ab7d25abfbab032e2ee",Source="https://gamebanana.com/mods/620520",Credit="Unicornshell, GPLv3"},UiLibrary}},
  new BuiltinMod{Id="ca000000000000000000000000000002",Key="transparency",Author="SinsOfSeven",Version="GIMI에 포함된 TexFx",Description="카메라가 캐릭터에 가까워질 때 생기는 투명도 필터를 제거합니다.",Controls="",Order=3,Preview="https://images.gamebanana.com/img/ss/mods/6a0f819448a42.jpg",PreviewRegion=new Rect(0.285,0.145,0.405,0.445),PreviewFocus=new Point(0.5,0.3),Name="투명도 필터 제거",Source="https://github.com/SinsOfSeven/TexFx/blob/main/Config.ini",Settings=TransparencySettings,Help="GIMI에 포함된 TexFx의 공식 설정을 사용합니다. 카메라가 캐릭터에 가까워질 때 적용되는 투명도 필터를 제거합니다. 별도의 투명도 제거 모드와 함께 켜지 마세요. TexFx는 GIMI의 서명된 패키지 업데이트에 포함됩니다."},
  new BuiltinMod{Id="ca000000000000000000000000000003",Key="dark-loading",Author="Ciprianno",Version="독립 어두운 배경 모듈",Description="밝은 로딩 배경을 어둡게 바꿉니다. CipStyle HUD에서 검증한 배경 모듈만 사용합니다.",Controls="",Order=1,Preview="solid:#000000",Name="어두운 로딩 화면",Source="https://gamebanana.com/mods/426212",Help="CipStyle의 독립 어두운 배경 모듈을 사용합니다. 7.1용 로딩 패키지의 배경 식별자와 일치하는 것을 확인했습니다. 실제 게임 적용은 아직 확인하지 않았으며 일부 화면은 밝게 남을 수 있습니다. 다른 로딩 화면 모드와 함께 켜지 마세요. 배포 파일을 검증된 버전으로 고정하며 임의의 최신 파일로 자동 교체하지 않습니다.",Packages=new[]{new BuiltinPackage{Folder="DarkLoading",File="976152",Hash="4b01f9a7010e32cf53a097adce68491b1f7af38f60b8e9eeaff99b40557927a5",Source="https://gamebanana.com/mods/426212",Extension=".7z",Credit="Ciprianno, credit required; noncommercial use"}}},
  new BuiltinMod{Id="ca000000000000000000000000000004",Key="ui-clutter",Author="Unicornshell",Version="2.3.0",Description="평소에는 상단 아이콘을 줄이고, 커서를 표시하면 다시 보여줍니다. 화면을 넓게 보고 싶은 사용자에게 맞습니다.",Controls="= 키: HUD 표시 전환",Photos=new[]{"https://images.gamebanana.com/img/ss/mods/6870050c751c6.jpg","https://images.gamebanana.com/img/ss/mods/68b9e2d50ff17.jpg","https://images.gamebanana.com/img/ss/mods/68b9e2892d255.jpg","https://images.gamebanana.com/img/ss/mods/687005e1c3085.jpg","https://images.gamebanana.com/img/ss/mods/687005c91aa1a.jpg","https://images.gamebanana.com/img/ss/mods/6870061a22da1.jpg","https://images.gamebanana.com/img/ss/mods/68700649b69b8.jpg","https://images.gamebanana.com/img/ss/mods/68b9e2315e544.jpg"},Order=2,RequiresUi=true,Preview="https://images.gamebanana.com/img/ss/mods/6870050c751c6.jpg",PreviewRegion=new Rect(0.01,0.0,0.98,0.22),PreviewFocus=new Point(0.68,0.5),Name="HUD 간소화",Source="https://gamebanana.com/mods/604692",Help="UI Clutter Reducer 2.3.0의 7.1 대응 배포를 사용합니다. 상단 아이콘을 줄이고 커서를 표시하면 다시 나타납니다. 게임 안에서는 = 키로 전환합니다. 애니메이션 속도는 런처의 프레임 설정에 맞춥니다. 부가 UID·촬영 로고 모듈은 제외해 별도 UID 설정을 유지합니다. UILib는 다른 기본 모드와 한 번만 적용합니다. 다운로드는 검증한 버전으로 고정하며 실제 게임 화면은 미확인입니다.",Packages=new[]{new BuiltinPackage{Folder="ClutterReducer",File="1827653",Hash="f433d2b992573b45bb1e0fc1d2d83af571a83a9b316a668e7a74623f96c52ca4",Source="https://gamebanana.com/mods/604692",Credit="Unicornshell, GPLv3",SelectPath="ClutterReducer_v2.3.0",DisabledFolder="Extras"},UiLibrary}},
  new BuiltinMod{Id="ca000000000000000000000000000005",Key="outline-resizer",Author="caioo_0",Version="1.0.1",Description="캐릭터 윤곽선의 두께를 줄이거나 조절합니다. 깜빡임을 피하도록 처음에는 원래 두께의 0.02배로 적용합니다.",Controls="Alt+0: 전환\nAlt++ / Alt+-: 두께 조절",Photos=new[]{"https://images.gamebanana.com/img/ss/mods/6abac6ef99e27.jpg","https://images.gamebanana.com/img/ss/mods/6abac6d04192c.jpg","https://images.gamebanana.com/img/ss/mods/6abac6d0c0859.jpg","https://images.gamebanana.com/img/ss/mods/6abac6d048ff7.jpg","https://images.gamebanana.com/img/ss/mods/6abac6d0726e0.jpg","https://images.gamebanana.com/img/ss/mods/6abac6d0b4a22.jpg","https://images.gamebanana.com/img/ss/mods/6abac6d0a9a22.jpg","https://images.gamebanana.com/img/ss/mods/6abac6ce614de.jpg","https://images.gamebanana.com/img/ss/mods/6abac6d14e977.jpg","https://images.gamebanana.com/img/ss/mods/6abac6cfe9427.jpg","https://images.gamebanana.com/img/ss/mods/6abac6d032d34.jpg","https://images.gamebanana.com/img/ss/mods/6abac6cfd16e6.jpg"},Order=8,Preview="https://images.gamebanana.com/img/ss/mods/6abac6ef99e27.jpg",PreviewRegion=new Rect(0.015,0.205,0.97,0.34),PreviewFocus=new Point(0.5,0.48),Name="윤곽선 조절",Source="https://gamebanana.com/mods/721992",Help="GIMI Outline Resizer 1.0.1입니다. 7.1 셰이더와 일반 ORFix·OffsetFix의 공존을 제작자가 확인한 배포를 사용합니다. 처음에는 윤곽선을 0.02배로 줄이고 Alt+0으로 전환, Alt++와 Alt+-로 두께를 조절합니다. 일부 NPC·특수 물체는 지원하지 않을 수 있습니다. 검증한 버전으로 고정하며 실제 게임 화면은 미확인입니다.",Packages=new[]{new BuiltinPackage{Folder="OutlineResizer",File="1830990",Hash="f0c2928bada43312815a4111cc2a6ac09e6c94dc78afd974fe0e9137285e0e19",Source="https://gamebanana.com/mods/721992",Credit="caioo_0; concept by mob159, CC BY-NC-ND 4.0; original author download",SelectPath="GIMI_Outline_Resizer_v1.0.1"}}},
  new BuiltinMod{Id="ca000000000000000000000000000006",Key="water-censor",Group="water",Author="Radorei",Version="7.0.1",Description="수중과 우주 이동에서 적용되는 파란 필터를 제거합니다. 기포와 잔상 제거는 별도 항목입니다.",Controls="",Photos=new[]{"https://images.gamebanana.com/img/ss/mods/6a704b2bc4757.jpg","https://images.gamebanana.com/img/ss/mods/6a6f282f92740.jpg","https://images.gamebanana.com/img/ss/mods/6a6f28866f94c.jpg","https://images.gamebanana.com/img/ss/mods/6a701d54187a5.jpg"},Order=5,Preview="https://images.gamebanana.com/img/ss/mods/6a704b2bc4757.jpg",PreviewRegion=new Rect(0.0,0.08,1,0.76),PreviewFocus=new Point(0.5,0.42),Name="수중·우주 필터 제거",Source="https://gamebanana.com/mods/699780",Help="Remove Underwater/Space Censorship 7.0.1입니다. 제작자가 7.1 동작을 확인한 수중·우주 이동의 파란 필터 제거 모듈입니다. 기포와 잔상은 별도 스위치로 선택합니다. 구형 Remove Underwater Censorship과 함께 사용하지 마세요. 검증한 원본 배포를 사용하며 실제 게임 화면은 미확인입니다.",Packages=new[]{Water("RemoveUnderwaterSpaceCensorship/WaterCensor.ini")}},
  new BuiltinMod{Id="ca000000000000000000000000000007",Key="water-bubbles",Group="water",Author="Radorei",Version="7.0.1 선택 모듈",Description="수중의 기포와 기포 궤적을 제거합니다. 필터나 손발의 잔상은 바꾸지 않습니다.",Controls="",Photos=new[]{"https://images.gamebanana.com/img/ss/mods/6a704b2bc4757.jpg","https://images.gamebanana.com/img/ss/mods/6a6f282f92740.jpg","https://images.gamebanana.com/img/ss/mods/6a6f28866f94c.jpg","https://images.gamebanana.com/img/ss/mods/6a701d54187a5.jpg"},Order=6,Preview="https://images.gamebanana.com/img/ss/mods/6a6f28866f94c.jpg",PreviewRegion=new Rect(0.2,0.27,0.72,0.48),PreviewFocus=new Point(0.53,0.4),Name="수중 기포 제거",Source="https://gamebanana.com/mods/699780",Help="수중 시야를 가리는 기포와 기포 궤적을 제거하는 제작자 선택 모듈입니다. 필터·손발 잔상과 독립적으로 켤 수 있습니다. 원본 안내대로 해당 INI만 활성화하며 다른 모듈의 상태는 바꾸지 않습니다. 검증한 원본 배포를 사용하며 실제 게임 화면은 미확인입니다.",Packages=new[]{Water("RemoveUnderwaterSpaceCensorship/DISABLED-WaterBubbles.ini")}},
  new BuiltinMod{Id="ca000000000000000000000000000008",Key="water-contrails",Group="water",Author="Radorei",Version="7.0.1 선택 모듈",Description="수중에서 손발에 생기는 궤적과 흐림 효과를 제거합니다. 필터나 기포는 바꾸지 않습니다.",Controls="",Photos=new[]{"https://images.gamebanana.com/img/ss/mods/6a704b2bc4757.jpg","https://images.gamebanana.com/img/ss/mods/6a6f282f92740.jpg","https://images.gamebanana.com/img/ss/mods/6a6f28866f94c.jpg","https://images.gamebanana.com/img/ss/mods/6a701d54187a5.jpg"},Order=7,Preview="https://images.gamebanana.com/img/ss/mods/6a6f28866f94c.jpg",PreviewRegion=new Rect(0.25,0.49,0.37,0.38),PreviewFocus=new Point(0.5,0.58),Name="수중 잔상 제거",Source="https://gamebanana.com/mods/699780",Help="수중에서 손발에 생기는 궤적과 흐림 효과를 제거하는 제작자 선택 모듈입니다. 필터·기포와 독립적으로 켤 수 있습니다. 원본 안내대로 해당 INI만 활성화하며 다른 모듈의 상태는 바꾸지 않습니다. 검증한 원본 배포를 사용하며 실제 게임 화면은 미확인입니다.",Packages=new[]{Water("RemoveUnderwaterSpaceCensorship/DISABLED-WaterContrails.ini")}},
  new BuiltinMod{Id="ca000000000000000000000000000009",Key="switch-prompts",Author="Ynovan",Version="6.6+",Description="XInput 패드의 버튼 그림을 스위치 패드 모양으로 바꿉니다. 실제 입력이나 버튼 배치는 바꾸지 않습니다.",Controls="",Photos=new[]{"https://images.gamebanana.com/img/ss/mods/687ac93d419ac.jpg","https://images.gamebanana.com/img/ss/mods/687acba0e3de3.jpg","https://images.gamebanana.com/img/ss/mods/687acba980bac.jpg"},Order=9,Preview="https://images.gamebanana.com/img/ss/mods/687acba0e3de3.jpg",PreviewRegion=new Rect(0.47,0.405,0.21,0.21),PreviewFocus=new Point(0.68,0.43),Name="스위치 패드 버튼 표시",Source="https://gamebanana.com/mods/607808",Help="Nintendo Switch Pro Controller Button UI의 6.6 이후 대응 배포입니다. XInput 패드의 표시를 스위치 버튼 모양으로 바꿉니다. 입력 방식이나 키 배치를 바꾸는 기능은 아닙니다. 현재 7.1용 UI 모드와 공통 버튼·축 식별자가 일치하지만 실제 게임 화면은 미확인입니다. 다른 패드 아이콘 교체 모드와 함께 사용하지 마세요. 검증한 원본 배포를 사용합니다.",Packages=new[]{new BuiltinPackage{Folder="SwitchPrompts",File="1479810",Hash="aa96bfd7e5b635de820a88116e05b8a343707ff864de7346a973888bb4d8f163",Source="https://gamebanana.com/mods/607808",Credit="Ynovan, original mod by RaitoMX; CC BY-NC-ND 4.0; original author download",SelectPath="NSButtonUI"}}}
 };
 internal static System.Linq.IOrderedEnumerable<ManagedMod> Ordered(System.Collections.Generic.IEnumerable<ManagedMod> items){return items.OrderBy(x=>x.Builtin==null?int.MaxValue:Catalog.Single(b=>b.Key==x.Builtin).Order).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Id,StringComparer.Ordinal);}
 static void WriteSettings(string target,BuiltinMod spec){if(spec.Settings==null)return;string file=Path.Combine(target,"TexFxSettings.ini");if(!File.Exists(file)||File.ReadAllText(file)!=spec.Settings)AtomicFile.Write(file,spec.Settings);}
 internal static void Prepare(string root,ManagedMod item,Action<ModProgress> progress=null){
  var spec=Catalog.Single(x=>x.Key==item.Builtin);if(!spec.Available)throw new InvalidOperationException("현재 버전의 호환성을 확인하지 못한 모드입니다.");string target=new ModManager(root).Payload(item);
  if(Directory.Exists(target)){WriteSettings(target,spec);return;}
  string stage=target+"-stage-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(stage);
  try{
   foreach(var package in spec.Packages)Unpack(root,stage,package,progress);
   WriteSettings(stage,spec);File.WriteAllText(Path.Combine(stage,"package.txt"),spec.Source+"\n",new UTF8Encoding(false));
   if(spec.Settings!=null)File.WriteAllText(Path.Combine(stage,"SOURCE.txt"),"TexFx by SinsOfSeven, CC BY-SA 4.0\n"+spec.Source+"\n",new UTF8Encoding(false));
   Directory.CreateDirectory(Path.GetDirectoryName(target));Directory.Move(stage,target);
  }finally{ModManager.DeleteInside(stage,Path.GetDirectoryName(target));}
 }
 internal static void Unpack(string root,string stage,BuiltinPackage package,Action<ModProgress> progress){
  string cache=Path.Combine(root,"mods","packages");Directory.CreateDirectory(cache);string archive=Path.Combine(cache,package.File+package.Extension);
  if(!File.Exists(archive)||UpdateService.Hash(archive)!=package.Hash){string part=archive+".part";try{
   ModProgress.Download("https://gamebanana.com/dl/"+package.File,part,4*1024*1024,progress,"모드 다운로드 중…");ModProgress.Report(progress,"배포 파일 검증 중…");
   if(UpdateService.Hash(part)!=package.Hash)throw new InvalidDataException("기본 모드 배포 파일 검증에 실패했습니다.");if(File.Exists(archive))File.Delete(archive);File.Move(part,archive);
  }finally{if(File.Exists(part))File.Delete(part);}}
  ModProgress.Report(progress,"모드 설치 중…");string folder=Path.Combine(stage,package.Folder);ModManager.Extract(archive,folder,true,root);
  if(package.SelectPath!=null){
   string selected=Components.SafeArchivePath(folder,package.SelectPath);
   if(Directory.Exists(selected)){string part=folder+"-selected";if(Directory.Exists(part))throw new IOException("모드 설치 폴더가 중복되어 있습니다.");try{Directory.Move(selected,part);ModManager.DeleteInside(folder,stage);Directory.Move(part,folder);}finally{ModManager.DeleteInside(part,stage);}}
   else{string name=Regex.Replace(Path.GetFileName(selected),"^DISABLED[-_]", "",RegexOptions.IgnoreCase);byte[] bytes=File.ReadAllBytes(selected);string readme=Path.Combine(folder,"README.txt");byte[] instructions=File.Exists(readme)?File.ReadAllBytes(readme):null;ModManager.DeleteInside(folder,stage);Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,name),bytes);if(instructions!=null)File.WriteAllBytes(readme,instructions);}
  }
  if(package.DisabledFolder!=null){string from=Components.SafeArchivePath(folder,package.DisabledFolder),to=Path.Combine(Path.GetDirectoryName(from),"DISABLED_"+Path.GetFileName(from));Directory.Move(from,to);}
  File.WriteAllText(Path.Combine(folder,"SOURCE.txt"),package.Folder+" by "+package.Credit+"\n"+package.Source+"\nOriginal archive: https://gamebanana.com/dl/"+package.File+"\nSHA256: "+package.Hash+"\nSelected module: "+(package.SelectPath??"full package")+"\nLicense: see author page\n",new UTF8Encoding(false));
 }
 internal static void ConfigureProjection(ModState state,ModManager manager,string runtime,string stage){
  if(state.Items.Any(x=>x.Enabled&&x.Builtin=="transparency")){
   string importer=Path.GetDirectoryName(ModIntegration.ModsFolder(runtime));
   string core=Path.Combine(importer,"Core");
   bool texfx=Directory.Exists(core)&&Directory.GetFiles(core,"Config.ini",SearchOption.AllDirectories).Any(x=>Path.GetFileName(Path.GetDirectoryName(x)).Equals("TexFx",StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(File.ReadAllText(x),@"(?im)^\s*(?:global\s+(?:persist\s+)?)?\$uncensor\s*="));
   if(!texfx)throw new InvalidOperationException("TexFx가 포함된 최신 GIMI를 설치한 뒤 다시 시작해 주세요.");
  }
  var ui=state.Items.Where(x=>x.Enabled&&x.Builtin!=null&&Catalog.Single(b=>b.Key==x.Builtin).RequiresUi).OrderBy(x=>x.Id).ToArray();
  bool existing=HasUiLibrary(Path.Combine(Path.GetDirectoryName(ModIntegration.ModsFolder(runtime)),"Core","GIMI","Libraries"))||state.Items.Where(x=>x.Enabled&&x.Builtin==null).Any(x=>HasUiLibrary(ModManager.Source(x,manager)));
  foreach(var item in ui)if(existing||item.Id!=ui[0].Id)ModManager.DeleteInside(Path.Combine(stage,item.Id,"UILib"),stage);
  var clutter=state.Items.SingleOrDefault(x=>x.Enabled&&x.Builtin=="ui-clutter");if(clutter!=null){
   int fps=60;string config=Path.Combine(runtime,"XXMI Launcher Config.json");if(File.Exists(config)){var importer=StoryClient.Read(config);foreach(string key in new[]{"Importers","GIMI","Importer"}){object field;importer=importer.TryGetValue(key,out field)?CodexChat.Map(field):new System.Collections.Generic.Dictionary<string,object>();}object unlocked;int selected;if(importer.TryGetValue("unlock_fps",out unlocked)&&Equals(unlocked,true)&&int.TryParse(CodexChat.S(importer,"unlock_fps_value"),out selected)&&selected>=1&&selected<=1000)fps=selected;}
   string settings="; UI Clutter Reducer documented frame setting\nnamespace = Catheryne.ClutterRuntime\n[Constants]\npost $\\ClutterReducer\\FRAMERATE = "+fps+"\n";AtomicFile.Write(Path.Combine(stage,clutter.Id,"ClutterRuntime.ini"),settings);
  }
 }
 internal static void ValidateLibraries(ModState state,string runtime,string stage){
  string libraries=Path.Combine(Path.GetDirectoryName(ModIntegration.ModsFolder(runtime)),"Core","GIMI","Libraries");var packaged=new System.Collections.Generic.HashSet<string>(Namespaces(libraries),StringComparer.OrdinalIgnoreCase);
  foreach(var item in state.Items.Where(x=>x.Enabled)){
   string source=item.ExternalFolder==null?Path.Combine(stage,item.Id):ModManager.Source(item,null);
   if(Namespaces(source).Any(packaged.Contains))throw new InvalidOperationException(Locale.Format("GIMI 기본 라이브러리와 중복된 모드가 있습니다: {0}",item.Name));
  }
 }
 static System.Collections.Generic.IEnumerable<string> Namespaces(string folder){
  if(!Directory.Exists(folder))yield break;
  foreach(string file in Directory.GetFiles(folder,"*.ini",SearchOption.AllDirectories).Where(x=>!x.Substring(folder.TrimEnd(Path.DirectorySeparatorChar).Length+1).Split(Path.DirectorySeparatorChar).Any(part=>part.StartsWith("DISABLED",StringComparison.OrdinalIgnoreCase)))){
   var match=Regex.Match(File.ReadAllText(file),@"(?im)^\s*namespace\s*=\s*([^;\r\n]+)");if(match.Success)yield return match.Groups[1].Value.Trim();
  }
 }
 static bool HasUiLibrary(string folder){return Directory.Exists(folder)&&Directory.GetFiles(folder,"*.ini",SearchOption.AllDirectories).Where(x=>!x.Substring(folder.TrimEnd(Path.DirectorySeparatorChar).Length+1).Split(Path.DirectorySeparatorChar).Any(segment=>segment.StartsWith("DISABLED",StringComparison.OrdinalIgnoreCase))).Any(x=>Regex.IsMatch(File.ReadAllText(x),@"(?im)^\s*namespace\s*=\s*UI\s*(?:;.*)?$"));}
}
