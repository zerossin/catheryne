param([Parameter(Mandatory=$true)][string]$Compiler)
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath $Compiler)){throw 'Pass the path to a verified Inno Setup compiler'}
$taskRoot=Join-Path $env:TEMP ('catheryne-app-update-fixture-'+[Guid]::NewGuid().ToString('N'))
$oldApp=Join-Path $taskRoot 'installed'
$payload=Join-Path $taskRoot 'payload'
$data=Join-Path $taskRoot 'data'
$output=Join-Path $taskRoot 'setup'
$product='CatheryneUpdateFixture'+[Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $oldApp,$payload,$data -Force|Out-Null
$dummy=@'
using System;
using System.IO;
using System.Reflection;
using System.Threading;
[assembly: AssemblyVersion("VERSION")]
[assembly: AssemblyFileVersion("VERSION.0")]
[assembly: AssemblyInformationalVersion("VERSION")]
internal static class Entry {
 static int Main(string[] args) {
  if(args.Length>0&&args[0]=="--wait")Thread.Sleep(2500);
  if(args.Length>0&&args[0]=="--preview")File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reopened.txt"),"--preview");
  return 0;
 }
}
'@
$csc='C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
foreach($pair in @(@($oldApp,'1.0.0'),@($payload,'1.1.0'))){
 $source=Join-Path $taskRoot ('v'+$pair[1]+'.cs')
 [IO.File]::WriteAllText($source,$dummy.Replace('VERSION',$pair[1]),[Text.UTF8Encoding]::new($false))
 & $csc /nologo /target:winexe /platform:x64 ('/out:'+(Join-Path $pair[0] 'GenshinLauncher.exe')) $source
 if($LASTEXITCODE -ne 0){throw 'Fixture build failed'}
 [IO.File]::WriteAllText((Join-Path $pair[0] 'resources.json'),'{"projectUrl":"https://github.com/example/catheryne"}',[Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AppUpdater.exe') -Destination $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'launcher.ico') -Destination $payload
$settings=Join-Path $data 'settings.json'
[IO.File]::WriteAllText($settings,'{"fixturePreference":42,"appAutoUpdate":true}',[Text.UTF8Encoding]::new($false))
$before=(Get-FileHash -LiteralPath $settings).Hash
try {
 $installer=& (Join-Path $PSScriptRoot 'build-installer.ps1') -Payload $payload -Compiler $Compiler -Version '1.1.0' -Output $output -ProductId $product
 $installerHash=(Get-FileHash -LiteralPath $installer).Hash.ToLowerInvariant()
 $cache=Join-Path $data 'cache/app-updates'
 $package=Join-Path $cache ('1.1.0-'+$installerHash+'/Catheryne-Setup-1.1.0.exe')
 New-Item -ItemType Directory -Path (Split-Path -Parent $package) -Force|Out-Null
 Copy-Item -LiteralPath $installer -Destination $package
 $plan=[ordered]@{Schema=1;AppDirectory=$oldApp;DataDirectory=$data;Repository='example/catheryne';Installer=$package;PriorHash=(Get-FileHash -LiteralPath (Join-Path $oldApp 'GenshinLauncher.exe')).Hash.ToLowerInvariant();Release=@{Tag='v1.1.0';Version='1.1.0';Url='https://github.com/example/catheryne/releases/download/v1.1.0/Catheryne-Setup-1.1.0.exe';Sha256=$installerHash;Size=(Get-Item -LiteralPath $package).Length}}
 [IO.File]::WriteAllText((Join-Path $cache 'pending.json'),($plan|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
 $helper=Join-Path $cache 'AppUpdater.exe'
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AppUpdater.exe') -Destination $helper
 $parent=Start-Process -FilePath (Join-Path $oldApp 'GenshinLauncher.exe') -ArgumentList '--wait' -WindowStyle Hidden -PassThru
 $started=$parent.StartTime.ToUniversalTime().Ticks
 $apply=Start-Process -FilePath $helper -ArgumentList @('--apply',('"'+$data+'"'),$parent.Id,$started) -WorkingDirectory $oldApp -WindowStyle Hidden -Wait -PassThru
 $result=Get-Content -LiteralPath (Join-Path $cache 'result.json') -Raw|ConvertFrom-Json
 if($apply.ExitCode -ne 0 -or $result.state -ne 'installed'){throw ('Real updater fixture failed: '+$apply.ExitCode+'/'+$result.state)}
 if(Test-Path -LiteralPath (Join-Path $cache 'pending.json')){throw 'Successful plan was not cleared'}
 if([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $oldApp 'GenshinLauncher.exe')).FileVersion -ne '1.1.0.0'){throw 'Wrong installed version'}
 if((Get-FileHash -LiteralPath $settings).Hash -ne $before){throw 'Fixture settings changed'}
 $reopened=Join-Path $oldApp 'reopened.txt'
 for($i=0;$i -lt 30 -and !(Test-Path -LiteralPath $reopened);$i++){Start-Sleep -Milliseconds 100}
 if(!(Test-Path -LiteralPath $reopened) -or (Get-Content -LiteralPath $reopened -Raw) -ne '--preview'){throw 'Home restart missing'}
 [pscustomobject]@{result='PASS';old_parent_exited=$parent.HasExited;new_version='1.1.0';settings_preserved=$true;pending_cleared=$true;home_restart=$true;fixture=$taskRoot}|ConvertTo-Json -Compress
} finally {
 $uninstaller=Join-Path $oldApp 'unins000.exe'
 if(Test-Path -LiteralPath $uninstaller){
  $uninstall=Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
  if($uninstall.ExitCode -ne 0){throw 'Fixture uninstall failed'}
 }
 if(Test-Path -LiteralPath ('HKCU:\Software\'+$product)){throw 'Fixture registry key was not removed'}
}