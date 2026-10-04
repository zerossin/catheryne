using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
namespace CatheryneScanning {
 // One request contract for launcher UI, AI, and the scanner process.
 internal sealed class AccountScanOptions {
  internal static readonly string[] Areas={"characters","weapons","artifacts","materials"};
  internal string[] Sections=Areas.ToArray();
  internal int WeaponRarity=1,WeaponLevel=1,ArtifactRarity=1,ArtifactLevel=0;
  internal static AccountScanOptions Read(IDictionary<string,object> values){
   var options=new AccountScanOptions();object raw;
   if(values.TryGetValue("sections",out raw)){
    var list=raw as IEnumerable;if(list==null||raw is string)throw new ArgumentException("수집 범위를 목록으로 지정해 주세요.");
    var selected=new List<string>();foreach(var item in list){string section=Convert.ToString(item);if(!Areas.Contains(section))throw new ArgumentException("수집 범위를 확인해 주세요.");if(!selected.Contains(section))selected.Add(section);}
    if(selected.Count==0)throw new ArgumentException("수집할 항목을 선택해 주세요.");options.Sections=Areas.Where(selected.Contains).ToArray();
   }
   options.WeaponRarity=Number(values,"minimum_weapon_rarity",1,1,5);
   options.WeaponLevel=Number(values,"minimum_weapon_level",1,1,90);
   options.ArtifactRarity=Number(values,"minimum_artifact_rarity",1,1,5);
   options.ArtifactLevel=Number(values,"minimum_artifact_level",0,0,20);
   return options;
  }
  static int Number(IDictionary<string,object> values,string key,int fallback,int min,int max){
   object value;if(!values.TryGetValue(key,out value))return fallback;
   if(!(value is int)&&!(value is long)&&!(value is decimal)&&!(value is double))throw new ArgumentException("수집 필터는 정수여야 합니다.");
   double number=Convert.ToDouble(value);if(double.IsNaN(number)||number<min||number>max||number!=Math.Truncate(number))throw new ArgumentException("수집 필터 범위를 확인해 주세요.");return (int)number;
  }
  internal bool Includes(string section){return Sections.Contains(section);}
  internal Dictionary<string,object> Parameters(){return new Dictionary<string,object>{{"sections",Sections},{"minimum_weapon_rarity",WeaponRarity},{"minimum_weapon_level",WeaponLevel},{"minimum_artifact_rarity",ArtifactRarity},{"minimum_artifact_level",ArtifactLevel}};}
  internal Dictionary<string,object> Coverage(){var result=new Dictionary<string,object>();foreach(string section in Sections)result[section]=section=="weapons"&&WeaponRarity==1&&WeaponLevel==1||section=="artifacts"&&ArtifactRarity==1&&ArtifactLevel==0?"full":"partial";return result;}
 }
}
