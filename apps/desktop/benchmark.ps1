param([string]$OutputPath)
$ErrorActionPreference='Stop'
$source=$PSScriptRoot
$output=Join-Path $env:TEMP ('catheryne-benchmark-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach($file in @('launcher.ico','en-US.json','resources.json','components.json','VERSION')){Copy-Item -LiteralPath (Join-Path $source $file) -Destination $output -Force}
Copy-Item -LiteralPath (Join-Path $source 'src/Main.xaml') -Destination $output -Force
Get-ChildItem -LiteralPath $source -Filter '*.dll' | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $output -Force}
foreach($folder in @('catalog','branding','fonts')){Copy-Item -LiteralPath (Join-Path $source $folder) -Destination $output -Recurse -Force}
$files=@(Get-Content -LiteralPath (Join-Path $source 'build-sources.txt') | Where-Object {$_ -and !$_.StartsWith('#')} | ForEach-Object {Join-Path $source ('src/'+$_)})
$fx='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs=@("$fx\WPF\PresentationFramework.dll","$fx\WPF\PresentationCore.dll","$fx\WPF\WindowsBase.dll","$source\Microsoft.Web.WebView2.Core.dll","$source\Microsoft.Web.WebView2.Wpf.dll","$source\Markdig.dll",'System.Xml.Linq.dll','System.Security.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') | ForEach-Object {'/r:'+$_}
& "$fx\csc.exe" /nologo /target:exe /platform:x64 /optimize+ /main:PerformanceProbe "/out:$output\probe.exe" @refs @files (Join-Path $source 'benchmark/PerformanceProbe.cs')
if($LASTEXITCODE -ne 0){throw 'Probe build failed'}
$result=if($OutputPath){[IO.Path]::GetFullPath($OutputPath)}else{Join-Path $output 'metrics.json'}
& "$output\probe.exe" $result
if($LASTEXITCODE -ne 0){throw 'Probe failed'}

Write-Output ('Benchmark result: '+$result)
