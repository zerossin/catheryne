param(
 [string]$SourceRoot=(Split-Path $PSScriptRoot -Parent),
 [string]$DependencyRoot,
 [string]$AssetsRoot,
 [string]$OutputRoot=(Join-Path ([IO.Path]::GetTempPath()) ('catheryne-performance-'+[Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference='Stop'
$base=Join-Path $SourceRoot 'apps/desktop'
if(!$DependencyRoot){$DependencyRoot=$base}
if(!$AssetsRoot){$AssetsRoot=$DependencyRoot}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
foreach($file in @('Main.xaml','en-US.json','resources.json','components.json','VERSION','launcher.ico')){Copy-Item -LiteralPath (Join-Path $base $file) -Destination $OutputRoot -Force}
Get-ChildItem -LiteralPath $DependencyRoot -File -Filter '*.dll' | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $OutputRoot -Force}
foreach($folder in @('catalog','branding','fonts')){if(Test-Path -LiteralPath (Join-Path $AssetsRoot $folder)){Copy-Item -LiteralPath (Join-Path $AssetsRoot $folder) -Destination $OutputRoot -Recurse -Force}}
$version=& (Join-Path $base 'build-version.ps1');$fx='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs=@("$fx\WPF\PresentationFramework.dll","$fx\WPF\PresentationCore.dll","$fx\WPF\WindowsBase.dll","$OutputRoot\Microsoft.Web.WebView2.Core.dll","$OutputRoot\Microsoft.Web.WebView2.Wpf.dll","$OutputRoot\Markdig.dll",'System.Xml.Linq.dll','System.Security.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') | ForEach-Object {'/r:'+$_}
$files=@(Get-Content (Join-Path $base 'build-sources.txt') | Where-Object {$_ -and !$_.StartsWith('#')} | ForEach-Object {Join-Path $base ('src/'+$_)})
& "$fx\csc.exe" /nologo /target:exe /platform:x64 /optimize+ /main:DesktopPerformanceProbe "/out:$OutputRoot\probe.exe" @refs @files $version (Join-Path $PSScriptRoot 'desktop-performance-probe.cs')
if($LASTEXITCODE -ne 0){throw 'Performance probe build failed'}
& (Join-Path $OutputRoot 'probe.exe') (Join-Path $OutputRoot 'metrics.json')
if($LASTEXITCODE -ne 0){throw 'Performance probe failed'}
Get-Content (Join-Path $OutputRoot 'metrics.json')
Write-Output (Join-Path $OutputRoot 'metrics.json')