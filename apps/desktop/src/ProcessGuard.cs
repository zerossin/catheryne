using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

internal sealed class ProcessState {
 internal bool Game,Unlocker;
 internal bool Busy {get{return Game||Unlocker;}}
}
internal static class ProcessGuard {
 internal static bool Running(string name){
  var processes=Process.GetProcessesByName(name);
  try{return processes.Length>0;}finally{foreach(var process in processes)process.Dispose();}
 }
 internal static ProcessState FromNames(IEnumerable<string> names){var found=new HashSet<string>(names,StringComparer.OrdinalIgnoreCase);return new ProcessState{Game=found.Contains("GenshinImpact")||found.Contains("YuanShen"),Unlocker=found.Contains("unlockfps_nc")||found.Contains("unlockfps_nc_signed")};}
 // One fresh sample; action guards never depend on the periodic GUI sample.
 internal static ProcessState Inspect(){var names=new List<string>();var processes=Process.GetProcesses();try{foreach(var process in processes)try{names.Add(process.ProcessName);}catch(InvalidOperationException){}catch(Win32Exception){}}finally{foreach(var process in processes)process.Dispose();}return FromNames(names);}
 internal static bool Busy(){return Inspect().Busy;}
}
