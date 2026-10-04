using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Rebuildable file projections. Callers keep ownership of mutable copies.
internal sealed class FileReadCache<T> where T:class {
 sealed class Entry {internal long Length,Write,Created;internal T Value;internal long Access;}
 readonly object gate=new object();readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);readonly int capacity;readonly long maxBytes;long clock,bytes;
 internal FileReadCache(int capacity=16,long maxBytes=8*1024*1024){if(capacity<1||maxBytes<1)throw new ArgumentOutOfRangeException();this.capacity=capacity;this.maxBytes=maxBytes;}
 internal T Read(string path,Func<string,T> load){
  path=Path.GetFullPath(path);lock(gate){var info=new FileInfo(path);Entry prior;
   if(!info.Exists){Forget(path);return null;}
   if(entries.TryGetValue(path,out prior)&&prior.Length==info.Length&&prior.Write==info.LastWriteTimeUtc.Ticks&&prior.Created==info.CreationTimeUtc.Ticks){prior.Access=++clock;return prior.Value;}
   Forget(path);long length=info.Length,write=info.LastWriteTimeUtc.Ticks,created=info.CreationTimeUtc.Ticks;var value=load(path);var after=new FileInfo(path);
   if(value!=null&&length<=maxBytes&&after.Exists&&after.Length==length&&after.LastWriteTimeUtc.Ticks==write&&after.CreationTimeUtc.Ticks==created){
    while(entries.Count>=capacity||bytes+length>maxBytes)Forget(entries.OrderBy(x=>x.Value.Access).First().Key);
    entries[path]=new Entry{Length=length,Write=write,Created=created,Value=value,Access=++clock};bytes+=length;
   }return value;
  }
 }
 void Forget(string path){Entry entry;if(entries.TryGetValue(path,out entry)){bytes-=entry.Length;entries.Remove(path);}}
 internal void Invalidate(string path){lock(gate)Forget(Path.GetFullPath(path));}
 internal int Count {get{lock(gate)return entries.Count;}}
 internal long Bytes {get{lock(gate)return bytes;}}
}
internal static class JsonCopy {
 internal static object Value(object value){var map=value as Dictionary<string,object>;if(map!=null)return map.ToDictionary(x=>x.Key,x=>Value(x.Value));var array=value as object[];if(array!=null)return array.Select(Value).ToArray();var list=value as ArrayList;if(list!=null)return new ArrayList(list.Cast<object>().Select(Value).ToArray());return value;}
 internal static Dictionary<string,object> Map(Dictionary<string,object> value){return (Dictionary<string,object>)Value(value);}
}
