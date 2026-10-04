using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

// Only Catheryne's private IPC objects accept its ordinary desktop client.
// User ACLs and caller process/session verification remain separate requirements.
internal static class PrivateIpc {
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text,uint revision,out IntPtr descriptor,out uint size);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetSecurityDescriptorSacl(IntPtr descriptor,out bool present,out IntPtr acl,out bool defaulted);
 [DllImport("advapi32.dll")] static extern uint SetSecurityInfo(IntPtr handle,int type,uint info,IntPtr owner,IntPtr group,IntPtr dacl,IntPtr sacl);
 [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr value);
 static void Medium(IntPtr handle){
  IntPtr descriptor;uint size;if(!ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;;NW;;;ME)",1,out descriptor,out size))throw new Win32Exception(Marshal.GetLastWin32Error());
  try{bool present,defaulted;IntPtr acl;if(!GetSecurityDescriptorSacl(descriptor,out present,out acl,out defaulted)||!present)throw new Win32Exception(Marshal.GetLastWin32Error());uint error=SetSecurityInfo(handle,6,0x10,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,acl);if(error!=0)throw new Win32Exception((int)error);}finally{LocalFree(descriptor);}
 }
 [StructLayout(LayoutKind.Sequential)] struct SecurityAttributes {
  internal int Length;internal IntPtr Descriptor;[MarshalAs(UnmanagedType.Bool)] internal bool Inherit;
 }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafePipeHandle CreateNamedPipe(string name,uint access,uint mode,uint instances,uint output,uint input,uint timeout,ref SecurityAttributes attributes);
 internal static NamedPipeServerStream Server(string name,int instances,int buffer){
  if(string.IsNullOrEmpty(name)||name.IndexOf('\\')>=0||instances<1||instances>254||buffer<1)throw new ArgumentException("Invalid private pipe configuration");
  var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);using(var identity=WindowsIdentity.GetCurrent())security.AddAccessRule(new PipeAccessRule(identity.User,PipeAccessRights.FullControl,AccessControlType.Allow));
  // Create the exact user ACL and Medium label together. WRITE_OWNER shares its
  // Win32 bit with FIRST_PIPE_INSTANCE and must not be requested on every instance.
  IntPtr descriptor;uint size;if(!ConvertStringSecurityDescriptorToSecurityDescriptor(security.GetSecurityDescriptorSddlForm(AccessControlSections.Access)+"S:(ML;;NW;;;ME)",1,out descriptor,out size))throw new Win32Exception(Marshal.GetLastWin32Error());
  try{
   var attributes=new SecurityAttributes{Length=Marshal.SizeOf(typeof(SecurityAttributes)),Descriptor=descriptor};
   var handle=CreateNamedPipe(@"\\.\pipe\"+name,3|0x40000000,0,(uint)instances,(uint)buffer,(uint)buffer,0,ref attributes);
   if(handle.IsInvalid){int error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error);}
   try{return new NamedPipeServerStream(PipeDirection.InOut,true,false,handle);}catch{handle.Dispose();throw;}
  }finally{LocalFree(descriptor);}
 }
 internal static Mutex Mutex(string name){
  var security=new MutexSecurity();security.SetAccessRuleProtection(true,false);using(var identity=WindowsIdentity.GetCurrent())security.AddAccessRule(new MutexAccessRule(identity.User,MutexRights.FullControl,AccessControlType.Allow));
  bool created;var mutex=new Mutex(false,name,out created,security);try{if(created)Medium(mutex.SafeWaitHandle.DangerousGetHandle());return mutex;}catch{mutex.Dispose();throw;}
 }
 internal static EventWaitHandle Event(string name,EventResetMode mode){
  var security=new EventWaitHandleSecurity();security.SetAccessRuleProtection(true,false);using(var identity=WindowsIdentity.GetCurrent())security.AddAccessRule(new EventWaitHandleAccessRule(identity.User,EventWaitHandleRights.FullControl,AccessControlType.Allow));
  bool created;var value=new EventWaitHandle(false,mode,name,out created,security);try{if(created)Medium(value.SafeWaitHandle.DangerousGetHandle());return value;}catch{value.Dispose();throw;}
 }
 internal static bool Accept(NamedPipeServerStream server,CancellationToken cancel){
  using(cancel.Register(()=>server.Dispose())){
   // The completion callback owns its async result. Never close its wait handle while cancellation is completing.
   try{Task.Factory.FromAsync(server.BeginWaitForConnection,server.EndWaitForConnection,null).GetAwaiter().GetResult();return !cancel.IsCancellationRequested;}
   catch(IOException){if(!cancel.IsCancellationRequested)throw;return false;}
   catch(ObjectDisposedException){if(!cancel.IsCancellationRequested)throw;return false;}
  }
 }
}
