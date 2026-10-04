using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

// Windows ships SQLite. Keep query/index state here; imported originals remain immutable files.
internal sealed class LocalDataService : IDisposable {
 IntPtr db;
 readonly string root;
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] static extern int sqlite3_open16(string path,out IntPtr handle);
 [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr handle);
 [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr handle,int ms);
 [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_exec(IntPtr handle,byte[] sql,Callback callback,IntPtr context,out IntPtr error);
 [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] static extern void sqlite3_free(IntPtr value);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Callback(IntPtr context,int count,IntPtr values,IntPtr names);
 internal LocalDataService(string folder){root=folder;}
 bool disposed;
 void Open(){
  if(disposed)throw new ObjectDisposedException("LocalDataService");if(db!=IntPtr.Zero)return;Directory.CreateDirectory(root);
  if(sqlite3_open16(Path.Combine(root,"catheryne.db"),out db)!=0){if(db!=IntPtr.Zero)sqlite3_close(db);db=IntPtr.Zero;throw new IOException("Cannot open local database");}
  sqlite3_busy_timeout(db,5000);
  try {
   var versions=Query("PRAGMA user_version");int version=int.Parse(versions[0]["user_version"]);
   if(version>2)throw new InvalidDataException("This data requires a newer Catheryne version.");
   Execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
   if(version==0)Execute("BEGIN IMMEDIATE; CREATE TABLE IF NOT EXISTS observations(id INTEGER PRIMARY KEY,profile TEXT NOT NULL,kind TEXT NOT NULL,observed_at TEXT NOT NULL,payload TEXT NOT NULL); CREATE INDEX IF NOT EXISTS observations_latest ON observations(profile,kind,id DESC); CREATE TABLE IF NOT EXISTS notification_receipts(event_key TEXT PRIMARY KEY,created_at TEXT NOT NULL); PRAGMA user_version=1; COMMIT;");
   if(version<2)Execute("BEGIN IMMEDIATE; CREATE TABLE IF NOT EXISTS achievements(profile TEXT NOT NULL,id INTEGER NOT NULL,category INTEGER NOT NULL,verified INTEGER NOT NULL CHECK(verified IN (0,1)),snapshot TEXT NOT NULL,updated_at TEXT NOT NULL,PRIMARY KEY(profile,id)); PRAGMA user_version=2; COMMIT;");
   // Rebuildable task lookup index: keep the existing data version and originals.
   Execute("CREATE INDEX IF NOT EXISTS observations_task_id ON observations(json_extract(payload,'$.Id'),id DESC) WHERE profile='default' AND kind='ai-task'");
  } catch {Dispose();throw;}
 }
 static byte[] Utf8(string value){return Encoding.UTF8.GetBytes(value+"\0");}
 static string Text(IntPtr value){if(value==IntPtr.Zero)return null;int size=0;while(Marshal.ReadByte(value,size)!=0)size++;var bytes=new byte[size];Marshal.Copy(value,bytes,0,size);return Encoding.UTF8.GetString(bytes);}
 internal static string Sql(string value){if(value==null)return "NULL";if(value.IndexOf('\0')>=0)throw new ArgumentException("Invalid text");return "'"+value.Replace("'","''")+"'";}
 internal List<Dictionary<string,string>> Query(string sql){
  Open();
  var rows=new List<Dictionary<string,string>>();Exception failure=null;
  Callback callback=(context,count,values,names)=>{try{var row=new Dictionary<string,string>();for(int i=0;i<count;i++)row[Text(Marshal.ReadIntPtr(names,i*IntPtr.Size))]=Text(Marshal.ReadIntPtr(values,i*IntPtr.Size));rows.Add(row);return 0;}catch(Exception e){failure=e;return 1;}};
  IntPtr error;int code=sqlite3_exec(db,Utf8(sql),callback,IntPtr.Zero,out error);if(error!=IntPtr.Zero)sqlite3_free(error);GC.KeepAlive(callback);
  if(failure!=null)throw new IOException("Local database read failed",failure);if(code!=0)throw new IOException("Local database operation failed ("+code+")");return rows;
 }
 internal void Execute(string sql){Query(sql);}
 internal void Observe(string profile,string kind,object payload){Execute("INSERT INTO observations(profile,kind,observed_at,payload) VALUES("+Sql(profile)+","+Sql(kind)+","+Sql(DateTime.UtcNow.ToString("o"))+","+Sql(json.Serialize(payload))+")");}
 internal List<Dictionary<string,string>> Recent(string profile,string kind,int limit=30){if(limit<1||limit>1000)throw new ArgumentOutOfRangeException("limit");return Query("SELECT observed_at,payload FROM observations WHERE profile="+Sql(profile)+" AND kind="+Sql(kind)+" ORDER BY id DESC LIMIT "+limit);}
 internal void Backup(string path){if(File.Exists(path))throw new IOException("Backup already exists");Execute("VACUUM INTO "+Sql(Path.GetFullPath(path)));}
 internal void SetSecret(string name,string value){
  if(name!="hoyolab"&&name!="discord"&&name!=RedemptionService.Secret)throw new ArgumentException("Unknown credential");
  string folder=Path.Combine(root,"secrets");Directory.CreateDirectory(folder);
  var bytes=ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser);
  AtomicFile.Write(Path.Combine(folder,name+".dpapi"),Convert.ToBase64String(bytes));
 }
 internal string GetSecret(string name){if(name!="hoyolab"&&name!="discord"&&name!=RedemptionService.Secret)throw new ArgumentException("Unknown credential");string path=Path.Combine(root,"secrets",name+".dpapi");if(!File.Exists(path))return null;return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(path)),null,DataProtectionScope.CurrentUser));}
 internal void DeleteSecret(string name){if(name!="hoyolab"&&name!="discord"&&name!=RedemptionService.Secret)throw new ArgumentException("Unknown credential");File.Delete(Path.Combine(root,"secrets",name+".dpapi"));}
 public void Dispose(){disposed=true;if(db!=IntPtr.Zero){sqlite3_close(db);db=IntPtr.Zero;}}
}

// Shared read refresh policy. Existing observations stay readable on failure.
internal static class ObservationRefresh {
 sealed class Gate {internal DateTime Retry;internal Exception Error;}
 static readonly System.Collections.Concurrent.ConcurrentDictionary<string,Gate> Gates=new System.Collections.Concurrent.ConcurrentDictionary<string,Gate>();
 internal static bool Fresh(string root,string profile,string kind,TimeSpan age){using(var db=new LocalDataService(root)){var rows=db.Query("SELECT observed_at FROM observations WHERE profile="+LocalDataService.Sql(profile)+" AND kind="+LocalDataService.Sql(kind)+" ORDER BY id DESC LIMIT 1");DateTime at;return rows.Count>0&&DateTime.TryParse(rows[0]["observed_at"],out at)&&DateTime.UtcNow-at.ToUniversalTime()<age;}}
 internal static void Run(string root,string profile,string kind,TimeSpan age,bool force,Action fetch){
  var gate=Gates.GetOrAdd(Path.GetFullPath(root)+"|"+profile+"|"+kind,k=>new Gate());
  lock(gate){if(!force&&Fresh(root,profile,kind,age))return;if(!force&&gate.Error!=null&&DateTime.UtcNow<gate.Retry)throw new InvalidOperationException(gate.Error.Message,gate.Error);
   try{fetch();gate.Error=null;}catch(Exception error){AppDiagnostics.Record(DiagnosticEvent.DataRefreshFailure,error,root);gate.Error=error;gate.Retry=DateTime.UtcNow.AddMinutes(2);throw;}
  }
 }
}
