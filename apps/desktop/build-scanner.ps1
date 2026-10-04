param([string]$Dotnet="dotnet")
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$output=Join-Path $PSScriptRoot 'scanner-build'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& $Dotnet publish (Join-Path $repo 'third_party/akasha-scanner/AkashaScanner/AkashaScanner.csproj') -c Release -p:Platform=x64 --self-contained false -o $output --verbosity quiet
if($LASTEXITCODE -ne 0){throw 'Scanner publish failed'}
Copy-Item -LiteralPath (Join-Path $repo 'third_party/akasha-scanner/LICENSE.txt') -Destination $output
$check=Start-Process (Join-Path $output 'AkashaScanner.exe') -ArgumentList '--checkpoint-self-test' -WindowStyle Hidden -PassThru -Wait
if($check.ExitCode -ne 0){throw 'Scanner checkpoint tests failed'}
Write-Output $output

Set-Content -LiteralPath (Join-Path $output "scan-protocol.json") -Value '{"version":1,"kind":"achievements"}' -Encoding utf8
