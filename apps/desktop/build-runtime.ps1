param([Parameter(Mandatory=$true)][string]$BuildPython)
$ErrorActionPreference='Stop'
$destination=Join-Path $PSScriptRoot 'runtime-build'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$archive=Join-Path $env:TEMP 'catheryne-python-3.13.15.zip'
if(!(Test-Path -LiteralPath $archive)){Invoke-WebRequest 'https://www.python.org/ftp/python/3.13.15/python-3.13.15-embed-amd64.zip' -OutFile $archive -UseBasicParsing}
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'd1f04d990aee1253d8569e8e5104e30fa9f5fa830899f14843448872d936a2cf'){throw 'Python archive hash mismatch'}
Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
$requirements=Join-Path $PSScriptRoot '../../integrations/ai/requirements.txt'
& $BuildPython -m pip install --disable-pip-version-check --only-binary=:all: --platform win_amd64 --python-version 313 --implementation cp --abi cp313 --target (Join-Path $destination 'Lib/site-packages') -r $requirements --upgrade
if($LASTEXITCODE -ne 0){throw 'Runtime dependencies failed'}
@('python313.zip','.','Lib/site-packages','import site') | Set-Content (Join-Path $destination 'python313._pth') -Encoding ascii
& (Join-Path $destination 'python.exe') -I -c 'import mcp,ctypes,sqlite3,PIL,numpy; from mcp.server.fastmcp import FastMCP; print("Bundled runtime ready")'
if($LASTEXITCODE -ne 0){throw 'Bundled runtime verification failed'}
$hash=(Get-FileHash -LiteralPath $requirements -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content (Join-Path $destination 'requirements.sha256') $hash -Encoding ascii
Write-Output $destination
