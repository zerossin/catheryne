param([Parameter(Mandatory=$true)][string]$Payload,[Parameter(Mandatory=$true)][string]$Compiler)
$ErrorActionPreference='Stop'
$fixture=Join-Path $env:TEMP ('catheryne-full-install-'+[Guid]::NewGuid().ToString('N'))
$product='CatheryneFullFixture'+[Guid]::NewGuid().ToString('N')
$installed=Join-Path $fixture 'installed'
$data=Join-Path $fixture 'data'
New-Item -ItemType Directory -Path $data -Force | Out-Null
$settings=Join-Path $data 'settings.json'
[IO.File]::WriteAllText($settings,'{"fixturePreference":42}',[Text.UTF8Encoding]::new($false))
$before=(Get-FileHash -LiteralPath $settings).Hash
try{
 $installer=& (Join-Path $PSScriptRoot 'build-installer.ps1') -Payload $Payload -Compiler $Compiler -Output (Join-Path $fixture 'setup') -ProductId $product
 foreach($pass in @('fresh','reinstall')){
  $install=Start-Process $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$installed+'"')) -WindowStyle Hidden -Wait -PassThru
  if($install.ExitCode -ne 0){throw "$pass installation failed: $($install.ExitCode)"}
  foreach($entry in Get-Content -LiteralPath (Join-Path $installed 'install-manifest.txt')){
   $parts=$entry.Split('|');$path=Join-Path $installed $parts[0]
   if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $parts[1]){throw "Installed manifest mismatch: $($parts[0])"}
  }
  if((Get-FileHash -LiteralPath $settings).Hash -ne $before){throw 'Settings were changed'}
 }
 $python=Join-Path $installed 'integrations/runtime/python.exe'
 & $python -I -c 'import mcp,ctypes,sqlite3,PIL,numpy; from mcp.server.fastmcp import FastMCP; print("Installed runtime ready")'
 if($LASTEXITCODE -ne 0){throw 'Installed runtime failed'}
 $scanner=Start-Process (Join-Path $installed 'integrations/scanner/AkashaScanner.exe') -ArgumentList '--checkpoint-self-test' -WindowStyle Hidden -Wait -PassThru
 if($scanner.ExitCode -ne 0){throw 'Installed scanner checkpoint test failed'}
 $app=Start-Process (Join-Path $installed 'GenshinLauncher.exe') -ArgumentList '--self-test' -WorkingDirectory $installed -WindowStyle Hidden -Wait -PassThru
 if($app.ExitCode -ne 0 -or (Get-Content -LiteralPath (Join-Path $installed 'test-result.txt') -Raw) -notmatch '^PASS:'){throw 'Installed app self-test failed'}
 Remove-Item -LiteralPath (Join-Path $installed 'test-result.txt') -Force
 Write-Output 'PASS: full payload install, every manifest hash, reinstall/settings preservation, bundled Python, scanner checkpoint, installed app/first-run shell'
}finally{
 $uninstaller=Join-Path $installed 'unins000.exe'
 if(Test-Path -LiteralPath $uninstaller){
  $remove=Start-Process $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
  if($remove.ExitCode -ne 0){throw 'Full installation fixture uninstall failed'}
 }
 if(Test-Path -LiteralPath ('HKCU:\Software\'+$product)){throw 'Fixture registry remains'}
 if(Test-Path -LiteralPath (Join-Path $installed 'GenshinLauncher.exe')){throw 'Fixture app remains after uninstall'}
 if((Get-FileHash -LiteralPath $settings).Hash -ne $before){throw 'Uninstall changed user settings'}
 $resolved=[IO.Path]::GetFullPath($fixture)
 if(!$resolved.StartsWith([IO.Path]::GetFullPath($env:TEMP)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notmatch '^catheryne-full-install-[a-f0-9]{32}$'){throw 'Unsafe fixture cleanup path'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
 Write-Output 'PASS: uninstall preserves user settings and removes fixture registration'
}
