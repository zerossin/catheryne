param([string]$Compiler,[Parameter(Mandatory=$true)][string]$VerifiedBinaryHash,[string]$Repository)
$ErrorActionPreference='Stop'
$resource=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'resources.json') -Raw | ConvertFrom-Json
if(!$Repository -and $resource.projectUrl){$Repository=([Uri]$resource.projectUrl).AbsolutePath.Trim('/')}
if($Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$'){throw 'Pass -Repository OWNER/REPO for the public GitHub release feed'}
if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'GenshinLauncher.exe') -Algorithm SHA256).Hash -ne $VerifiedBinaryHash){throw 'Build hash differs from the verified binary'}
if(!(Test-Path (Join-Path $PSScriptRoot 'runtime-build/python.exe'))){throw 'Run build-runtime.ps1 before packaging'}
if(!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'scanner-build/AkashaScanner.exe'))){throw 'Run build-scanner.ps1 before packaging; the maintained scanner is required.'}
$packageRoot=Join-Path $PSScriptRoot ('dist\'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$appRoot=Join-Path $packageRoot 'app'
New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
# Explicit installation payload; personal settings and runtime journals stay outside.
foreach($name in @('Markdig.dll','System.Memory.dll','System.Buffers.dll','System.Runtime.CompilerServices.Unsafe.dll','System.Numerics.Vectors.dll','WebView2-LICENSE.txt','WebP-LICENSE.txt','WebView2-NOTICE.txt','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','WebView2Loader.dll','GenshinLauncher.exe','AppUpdater.exe','Catheryne.Capture.dll','Catheryne.Images.dll','Catheryne.Notifications.dll','Main.xaml','launcher.ico','resources.json','components.json','LICENSE','THIRD-PARTY.md','Markdown-NOTICES.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $appRoot }
& (Join-Path $PSScriptRoot 'copy-user-docs.ps1') -Destination $appRoot
$resource.projectUrl='https://github.com/'+$Repository
$resourceJson=$resource | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText((Join-Path $appRoot 'resources.json'),$resourceJson,[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'catalog') -Destination $appRoot -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fonts') -Destination $appRoot -Recurse
New-Item -ItemType Directory -Path (Join-Path $appRoot 'branding') -Force | Out-Null
foreach($art in @('background.png','welcome.png','launcher.png','resin.png','README.md')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('branding/'+$art)) -Destination (Join-Path $appRoot ('branding/'+$art))}
# The maintained scanner template is copied to local user data on first use.
# Bundle the same canonical service and MCP adapter; no journals or user settings.
$repoRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$coreRoot=Join-Path $repoRoot 'core/story-control'
$bundledCore=Join-Path $appRoot 'core/story-control'
New-Item -ItemType Directory -Path $bundledCore -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $coreRoot 'src') -Recurse -File | Where-Object {$_.Extension -in '.py','.html','.js'} | ForEach-Object {
 $relative=$_.FullName.Substring($coreRoot.Length).TrimStart('\','/')
 $target=Join-Path $bundledCore $relative
 New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
 Copy-Item -LiteralPath $_.FullName -Destination $target
}
Copy-Item -LiteralPath (Join-Path $coreRoot 'LICENSE') -Destination $bundledCore
Copy-Item -LiteralPath (Join-Path $coreRoot 'scripts') -Destination $bundledCore -Recurse
New-Item -ItemType Directory -Path (Join-Path $appRoot 'integrations') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'runtime-build') -Destination (Join-Path $appRoot 'integrations/runtime') -Recurse
$adapterRoot=Join-Path $appRoot 'integrations/ai'
New-Item -ItemType Directory -Path $adapterRoot -Force | Out-Null
foreach($name in @('server.py','GUIDE.md','requirements.txt','README.md','game_catalog.py')) {Copy-Item -LiteralPath (Join-Path $repoRoot ('integrations/ai/'+$name)) -Destination $adapterRoot}
# The launcher and both scanners are x64; omit unused x86 native runtimes.
# The maintained scanner is built from the canonical MIT source, with no user data.
$scannerBuild=Join-Path $PSScriptRoot 'scanner-build'
if(Test-Path -LiteralPath (Join-Path $scannerBuild 'AkashaScanner.exe')) {
 $scannerTarget=Join-Path $appRoot 'integrations/scanner'
 New-Item -ItemType Directory -Path $scannerTarget -Force | Out-Null
 Get-ChildItem -LiteralPath $scannerBuild -Recurse -File | Where-Object {$_.FullName -notmatch '[\\/](ScannedData|GenshinDatabase|logs|webview|win-x86)[\\/]' -and $_.Name -notin @('config.json','scan-diagnostic.txt') -and $_.Extension -notin @('.log','.tmp')} | ForEach-Object {
  $relative=$_.FullName.Substring($scannerBuild.Length).TrimStart('\','/')
  $destination=Join-Path $scannerTarget $relative
  New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
  Copy-Item -LiteralPath $_.FullName -Destination $destination
 }
}
$kameraBuild=Join-Path $repoRoot 'third_party/inventory-kamera/InventoryKamera/bin/x64/Release'
if(!(Test-Path (Join-Path $kameraBuild 'InventoryKamera.exe'))){throw 'Build Kamera before packaging'}
Set-Content -LiteralPath (Join-Path $kameraBuild 'scan-protocol.json') -Value '{"version":1,"kind":"account"}' -Encoding utf8
$kameraTarget=Join-Path $appRoot 'integrations/kamera'
New-Item -ItemType Directory -Path $kameraTarget -Force | Out-Null
Get-ChildItem $kameraBuild -File | Where-Object {$_.Extension -in '.exe','.dll','.config' -or $_.Name -eq 'scan-protocol.json'} | Copy-Item -Destination $kameraTarget
# Recognition lists are a first-use cache generated by DatabaseManager, not build inputs.
foreach($folder in @('tessdata','x64')){Copy-Item -LiteralPath (Join-Path $kameraBuild $folder) -Destination $kameraTarget -Recurse}
Copy-Item -LiteralPath (Join-Path $repoRoot 'third_party/inventory-kamera/LICENSE') -Destination (Join-Path $kameraTarget 'LICENSE.txt')

# Locale catalogs are the same set discovered by the language picker.
$languageFiles=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.json' -File | Where-Object {$_.BaseName -match '^[a-z]{2,3}-[A-Za-z]{2,8}$'})
foreach($languageFile in $languageFiles){Copy-Item -LiteralPath $languageFile.FullName -Destination $appRoot}
# Full source is distributed from the reviewed repository snapshot, never a desktop subset.
$installer=& (Join-Path $PSScriptRoot 'build-installer.ps1') -Payload $appRoot -Compiler $Compiler -Output $packageRoot
$checksum=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($installer)
[IO.File]::WriteAllText((Join-Path $packageRoot 'SHA256SUMS.txt'),$checksum+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
Write-Output $packageRoot
