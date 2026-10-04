using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

// All manual game-dependent starts use the launcher's existing launch path.
internal static class GameRequirement {
 static Window owner;
 static Func<Task<bool>> launch;
 static bool preparing;
 internal static void Configure(Window window,Func<Task<bool>> start){owner=window;launch=start;}
 internal static bool Running(){foreach(string name in new[]{"GenshinImpact","YuanShen"}){var processes=Process.GetProcessesByName(name);bool found=System.Linq.Enumerable.Any(processes,p=>p.SessionId==WindowsChildSession.Current);foreach(var process in processes)process.Dispose();if(found)return true;}return false;}
 static bool RunningSelected(){return GameEnvironment.Remote(Setup.DataFolder)?new GameTools(Setup.DataFolder).Available():Running();}
 internal static async Task<bool> Ensure(Window window){
  if(preparing)return false;
  if(await Task.Run(()=>RunningSelected()))return true;
  window=window??owner;
  if(window==null||launch==null)return false;
  preparing=true;
  try{
   if(MessageBox.Show(window,Locale.T("원신이 꺼져 있습니다. 원신을 실행시키겠습니까?"),Locale.T("게임 시작"),MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return false;
   if(!await launch())return false;
   var limit=DateTime.UtcNow.AddSeconds(60);
   while(!await Task.Run(()=>RunningSelected())&&DateTime.UtcNow<limit&&window.IsLoaded)await Task.Delay(500);
   if(!window.IsLoaded)return false;
   if(!await Task.Run(()=>RunningSelected())){MessageBox.Show(window,Locale.T("게임 실행을 확인하지 못했습니다."),Locale.T("게임 시작"));return false;}
   if(GameEnvironment.Remote(Setup.DataFolder))await Task.Run(()=>GameEnvironment.ShowView(Setup.DataFolder,true));
   // A process can exist on the login screen. Never send scan or story input
   // there simply because the executable has started.
   bool confirmed=MessageBox.Show(window,Locale.T("게임 접속을 마친 뒤 확인을 누르면 작업을 이어갑니다."),Locale.T("게임 시작"),MessageBoxButton.OKCancel,MessageBoxImage.Information,MessageBoxResult.Cancel)==MessageBoxResult.OK;
   if(GameEnvironment.Remote(Setup.DataFolder))await Task.Run(()=>GameEnvironment.HideView(Setup.DataFolder));
   return confirmed&&await Task.Run(()=>RunningSelected());
  }finally{preparing=false;}
 }
}
