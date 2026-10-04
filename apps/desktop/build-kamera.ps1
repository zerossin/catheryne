param([string]$MSBuild="msbuild")
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& $MSBuild (Join-Path $repo 'third_party/inventory-kamera/InventoryKamera/InventoryKamera.csproj') /restore /p:Configuration=Release /p:Platform=x64 /verbosity:quiet /nologo
if($LASTEXITCODE -ne 0){throw 'Kamera build failed'}

# Ship upstream recognition tables so headless first use does not download the full game database.
$archive=Join-Path $env:TEMP 'catheryne-kamera-v1.4.5.zip'
$expected='624ab74978850bb9bc6d85600efe069675c0dd191e64fa318e97865f37e9f5a3'
if(!(Test-Path -LiteralPath $archive)){Invoke-WebRequest -Uri 'https://github.com/taiwenlee/Inventory_Kamera/releases/download/v1.4.5/Inventory.Kamera.v1.4.5.zip' -OutFile $archive -TimeoutSec 90}
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected){throw 'Kamera catalogue package hash mismatch'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$target=Join-Path $repo 'third_party/inventory-kamera/InventoryKamera/bin/x64/Release/inventorylists'
New-Item -ItemType Directory -Path $target -Force | Out-Null
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try {foreach($name in @('characters.json','weapons.json','artifacts.json','materials.json','version.txt')){
 $entry=$zip.GetEntry('inventorylists/'+$name)
 if(!$entry){throw "Missing upstream catalogue: $name"}
 [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $target $name),$true)
}}finally{$zip.Dispose()}
