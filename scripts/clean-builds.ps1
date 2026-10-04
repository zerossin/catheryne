param(
 [string]$BuildRoot=(Join-Path (Split-Path $PSScriptRoot -Parent) 'apps/desktop/dist'),
 [ValidateRange(1,100)][int]$Keep=1,
 [switch]$Apply
)
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath $BuildRoot)){return}
$root=(Get-Item -LiteralPath $BuildRoot).FullName.TrimEnd('\','/')
$desktop=Split-Path $root -Parent
if((Split-Path $root -Leaf) -ne 'dist' -or !(Test-Path -LiteralPath (Join-Path $desktop 'VERSION')) -or !(Test-Path -LiteralPath (Join-Path $desktop 'package.ps1'))){throw 'Expected the desktop build output directory'}
if((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Build root must not be a link'}
# Timestamped packages are reproducible. Keep the latest packages, setup installer,
# and any unrecognized folders. Never traverse links or remove private records.
$packages=@(Get-ChildItem -LiteralPath $root -Directory | Where-Object {$_.Name -match '^\d{8}-\d{4,6}(?:-[A-Za-z0-9-]+)?$'} | Sort-Object Name -Descending)
$plan=@()
foreach($package in ($packages | Select-Object -Skip $Keep)){
 $path=$package.FullName
 if(!$path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Package escaped the build directory'}
 $entries=@($package)+@(Get-ChildItem -LiteralPath $path -Recurse -Force)
 if($entries | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw 'Package contains a link'}
 if($entries | Where-Object {$_.Name -in @('userdata','profiles','screenshots','ScannedData','GenshinDatabase','settings.json','ai-connection.json','mcp-client-config.json')}){throw 'Package contains private runtime data'}
 $bytes=($entries | Where-Object {!$_.PSIsContainer} | Measure-Object Length -Sum).Sum
 $plan+= [pscustomobject]@{Path=$path;Bytes=[long]$bytes;Applied=[bool]$Apply}
}
# Validate every candidate before the first deletion, and recheck each path.
foreach($item in $plan){
 if($Apply){
  $resolved=(Get-Item -LiteralPath $item.Path).FullName
  if(!$resolved.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid deletion target'}
  if(@(Get-ChildItem -LiteralPath $resolved -Recurse -Force)+@(Get-Item -LiteralPath $resolved) | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw 'Package changed to a link'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
 }
 $item
}
