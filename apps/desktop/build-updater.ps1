$ErrorActionPreference='Stop'
$versionSource=& (Join-Path $PSScriptRoot 'build-version.ps1')
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources=@((Join-Path $PSScriptRoot 'src/AppUpdateProtocol.cs'),(Join-Path $PSScriptRoot 'src/AppUpdaterEntry.cs'),(Join-Path $PSScriptRoot 'src/ProcessGuard.cs'),(Join-Path $PSScriptRoot 'src/NativePaths.cs'),(Join-Path $PSScriptRoot 'src/GameInputLease.cs'),(Join-Path $PSScriptRoot 'src/PrivateIpc.cs'),$versionSource)
& $compiler /nologo /target:winexe /platform:x64 /optimize+ ('/out:'+(Join-Path $PSScriptRoot 'AppUpdater.exe')) /r:System.Web.Extensions.dll @sources
if($LASTEXITCODE -ne 0){throw 'App updater build failed'}
