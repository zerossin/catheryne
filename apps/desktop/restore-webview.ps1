$ErrorActionPreference='Stop'
$version='1.0.4191.47'
$expected='F492BBF547D0DA329553B6727435B677579B1E9F91CC9E4A1AD029366D5F23D0'
$required=@('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','WebView2Loader.dll')
if(@($required | Where-Object {!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $_))}).Count -eq 0 -and (Test-Path (Join-Path $PSScriptRoot 'WebView2-LICENSE.txt')) -and (Test-Path (Join-Path $PSScriptRoot 'WebView2-NOTICE.txt'))){return}
$stage=Join-Path ([IO.Path]::GetTempPath()) ('catheryne-webview-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$zip=Join-Path $stage 'sdk.zip'
Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$version/microsoft.web.webview2.$version.nupkg" -OutFile $zip
if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected){throw 'WebView2 SDK checksum mismatch'}
Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $stage 'sdk')
foreach($name in $required[0..1]){Copy-Item -LiteralPath (Join-Path $stage ('sdk/lib/net462/'+$name)) -Destination $PSScriptRoot}
Copy-Item -LiteralPath (Join-Path $stage 'sdk/runtimes/win-x64/native/WebView2Loader.dll') -Destination $PSScriptRoot
# Preserve package notices with redistributed SDK files.
foreach($name in @('LICENSE.txt','NOTICE.txt')){Copy-Item -LiteralPath (Join-Path $stage ('sdk/'+$name)) -Destination (Join-Path $PSScriptRoot ('WebView2-'+$name)) -Force}
