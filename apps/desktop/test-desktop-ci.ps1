param([string]$AppDirectory=(Join-Path $PSScriptRoot '.'))
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){
 throw 'This prerequisite setup is only for disposable GitHub-hosted Windows runners.'
}
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CatheryneCiChildSessions {
 [DllImport("wtsapi32.dll",SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)]
 public static extern bool WTSIsChildSessionsEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
 [DllImport("wtsapi32.dll",SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)]
 public static extern bool WTSEnableChildSessions([MarshalAs(UnmanagedType.Bool)] bool enabled);
}
"@
function Get-ChildSessionEnabled {
 $enabled=$false
 if(![CatheryneCiChildSessions]::WTSIsChildSessionsEnabled([ref]$enabled)){
  throw [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error())
 }
 return $enabled
}
function Set-ChildSessionEnabled([bool]$Enabled) {
 if(![CatheryneCiChildSessions]::WTSEnableChildSessions($Enabled)){
  throw [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error())
 }
 if((Get-ChildSessionEnabled) -ne $Enabled){throw 'Windows did not apply the child-session prerequisite.'}
}
Write-Output ('RDP client version: '+[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path ([Environment]::SystemDirectory) 'mstscax.dll')).FileVersion)
$prior=Get-ChildSessionEnabled
Write-Output ('Child-session prerequisite before tests: '+$prior)
try {
 if(!$prior){Set-ChildSessionEnabled $true}
 foreach($language in @('ko-KR','en-US')){
  $arguments=if($language -eq 'en-US'){@('--self-test','--english')}else{@('--self-test')}
  $result=Join-Path $AppDirectory 'test-result.txt'
  if(Test-Path -LiteralPath $result){Remove-Item -LiteralPath $result}
  $test=Start-Process (Join-Path $AppDirectory 'GenshinLauncher.exe') -ArgumentList $arguments -WorkingDirectory $AppDirectory -WindowStyle Hidden -Wait -PassThru
  if(Test-Path -LiteralPath $result){Get-Content -LiteralPath $result}
  if($test.ExitCode){throw "$language self-test failed (exit $($test.ExitCode))"}
  if(!(Test-Path -LiteralPath $result)){throw "$language self-test did not write a result"}
 }
} finally {
 if(!$prior){Set-ChildSessionEnabled $false}
}
