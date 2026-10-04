param([switch]$Public,[string]$IconPath,[string]$OutputPath,[switch]$ManagedOnly)
$ErrorActionPreference='Stop'
$base=$PSScriptRoot
if(!$OutputPath){$OutputPath=Join-Path $base "GenshinLauncher.exe"}
& (Join-Path $base "build-updater.ps1")
$versionSource=& (Join-Path $base "build-version.ps1")
if(!$ManagedOnly){
 & (Join-Path $base "build-capture.ps1")
 & (Join-Path $base "build-images.ps1")
 & (Join-Path $base "build-notifications.ps1")
}elseif(!(Test-Path -LiteralPath (Join-Path $base "Catheryne.Capture.dll")) -or !(Test-Path -LiteralPath (Join-Path $base "Catheryne.Images.dll")) -or !(Test-Path -LiteralPath (Join-Path $base "Catheryne.Notifications.dll"))){throw "Build the native helpers before a managed-only build"}
& (Join-Path $base "restore-webview.ps1")
& (Join-Path $base "restore-markdown.ps1")
$sourceFiles=@(Get-Content -LiteralPath (Join-Path $base 'build-sources.txt') | Where-Object {$_ -and !$_.StartsWith('#')} | ForEach-Object {
 if($_ -notmatch '^[A-Za-z0-9]+\.cs$'){throw "Invalid source entry: $_"}
 $path=Join-Path $base ('src/'+$_);if(!(Test-Path -LiteralPath $path)){throw "Missing source: $_"};$path
})
$sourceFiles+=@($versionSource)
$fx='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$iconPath=if($IconPath){if(!(Test-Path -LiteralPath $IconPath)){throw "Icon not found: $IconPath"};(Resolve-Path -LiteralPath $IconPath).Path}else{"$base\launcher.ico"}
& "$fx\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /out:"$OutputPath" /win32icon:"$iconPath" /r:"$fx\WPF\PresentationFramework.dll" /r:"$fx\WPF\PresentationCore.dll" /r:"$fx\WPF\WindowsBase.dll" /r:"$base\Microsoft.Web.WebView2.Core.dll" /r:"$base\Microsoft.Web.WebView2.Wpf.dll" /r:"$base\Markdig.dll" /r:System.Xml.Linq.dll /r:System.Security.dll /r:System.Xaml.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll @sourceFiles
if($LASTEXITCODE -ne 0){throw 'Build failed'}
Copy-Item -LiteralPath "$base\src\Main.xaml" -Destination "$base\Main.xaml" -Force
