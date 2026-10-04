$ErrorActionPreference='Stop'
$version=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(\.(0|[1-9]\d*))?$'){throw 'Invalid app version'}
$parsed=[Version]$version
$assemblyVersion='{0}.{1}.{2}.{3}' -f $parsed.Major,$parsed.Minor,$parsed.Build,[Math]::Max(0,$parsed.Revision)
$folder=Join-Path $PSScriptRoot '.build'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$target=Join-Path $folder 'VersionInfo.cs'
$content='using System.Reflection;'+[Environment]::NewLine+
 '[assembly: AssemblyVersion("'+$assemblyVersion+'")]'+[Environment]::NewLine+
 '[assembly: AssemblyFileVersion("'+$assemblyVersion+'")]'+[Environment]::NewLine+
 '[assembly: AssemblyInformationalVersion("'+$version+'")]'
Set-Content -LiteralPath $target -Value $content -Encoding UTF8
$target
