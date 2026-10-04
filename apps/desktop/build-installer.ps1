param(
 [Parameter(Mandatory=$true)][string]$Payload,
 [string]$Compiler,
 [string]$Version=(Get-Content (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim(),
 [string]$Output,
 [string]$ProductId='Catheryne'
)
$ErrorActionPreference='Stop'
$payloadRoot=(Resolve-Path -LiteralPath $Payload).Path
if(!(Test-Path -LiteralPath (Join-Path $payloadRoot 'GenshinLauncher.exe'))){throw 'App payload is missing'}
if($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$'){throw 'Invalid version'}
$parsed=[Version]$Version
$expected='{0}.{1}.{2}.{3}' -f $parsed.Major,$parsed.Minor,$parsed.Build,[Math]::Max(0,$parsed.Revision)
$binaryVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payloadRoot 'GenshinLauncher.exe')).FileVersion
if($binaryVersion -ne $expected){throw 'Installer and verified app binary versions disagree'}
if(!$Compiler){$Compiler=Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'}
if(!(Test-Path -LiteralPath $Compiler)){throw 'Inno Setup compiler is required. Pass -Compiler with its installed path.'}
if(!$Output){$Output=Join-Path $PSScriptRoot 'dist/setup'}
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$outputRoot=(Resolve-Path -LiteralPath $Output).Path
$manifest=Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | Where-Object {$_.Name -ne 'install-manifest.txt'} | ForEach-Object {
 $relative=$_.FullName.Substring($payloadRoot.Length+1)
 if($relative -match '(^|[\\/])(profiles|secrets|captures|logs|\.git)([\\/]|$)' -or ($relative -match '(^|[\\/])data([\\/]|$)' -and $relative -notmatch '^integrations[\\/]runtime[\\/]Lib[\\/]site-packages[\\/]')){throw "Private runtime directory in payload: $relative"}
 $relative+'|'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest | Set-Content -LiteralPath (Join-Path $payloadRoot 'install-manifest.txt') -Encoding UTF8
& $Compiler /Q ('/DPayload='+$payloadRoot) ('/DVersion='+$Version) ('/DOutputPath='+$outputRoot) ('/DProductId='+$ProductId) (Join-Path $PSScriptRoot 'installer/Catheryne.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
Join-Path $outputRoot ('Catheryne-Setup-'+$Version+'.exe')
