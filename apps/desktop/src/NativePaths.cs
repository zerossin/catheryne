using System;
using System.Diagnostics;
using System.IO;

// Resolve packaged-app filesystem redirection before starting an elevated child.
internal static class NativePaths {
 [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)] static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle,System.Text.StringBuilder path,uint length,uint flags);
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct FileInfo {
  internal uint Attributes,CreatedLow,CreatedHigh,AccessedLow,AccessedHigh,WrittenLow,WrittenHigh,Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
 }
 [System.Runtime.InteropServices.DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle,out FileInfo info);
 internal static string FileIdentity(string path){
  using(var handle=CreateFile(Path.GetFullPath(path),0,7,IntPtr.Zero,3,0,IntPtr.Zero)){
   FileInfo info;if(handle.IsInvalid||!GetFileInformationByHandle(handle,out info))throw new IOException("실제 실행 경로를 확인하지 못했습니다.");
   return info.Volume.ToString("X8")+info.IndexHigh.ToString("X8")+info.IndexLow.ToString("X8");
  }
 }
 internal static string Resolve(string path){
  path=Path.GetFullPath(path);
  if(!File.Exists(path)&&!Directory.Exists(path)){string parent=Path.GetDirectoryName(path);return string.IsNullOrEmpty(parent)?path:Path.Combine(Resolve(parent),Path.GetFileName(path));}
  using(var handle=CreateFile(path,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)){
   if(handle.IsInvalid)throw new IOException("실제 실행 경로를 확인하지 못했습니다.");
   var buffer=new System.Text.StringBuilder(32768);uint size=GetFinalPathNameByHandle(handle,buffer,(uint)buffer.Capacity,0);
   if(size==0||size>=buffer.Capacity)throw new IOException("실제 실행 경로를 확인하지 못했습니다.");
   string result=buffer.ToString();if(result.StartsWith(@"\\?\UNC\"))return @"\\"+result.Substring(8);return result.StartsWith(@"\\?\")?result.Substring(4):result;
  }
 }
}
