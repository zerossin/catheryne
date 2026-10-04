param()
$ErrorActionPreference='Stop'
$version='1.6.0'
$expected='48886f506b21f62e4661f0f4cbfca19800897c385128e8902542d29a950c93f1'
$cache=Join-Path $PSScriptRoot 'image-build'
$package=Join-Path $cache 'libwebp.zip'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
if(!(Test-Path -LiteralPath $package)){Invoke-WebRequest -UseBasicParsing -Uri "https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-$version-windows-x64.zip" -OutFile $package}
if((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expected){throw 'WebP package integrity mismatch'}
$dependency=Join-Path $cache "libwebp-$version-windows-x64"
if(!(Test-Path -LiteralPath (Join-Path $dependency 'lib/libwebp.lib'))){Expand-Archive -LiteralPath $package -DestinationPath $cache -Force}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'MSVC x64 build tools not found'}
$dev=Join-Path $vs 'Common7/Tools/VsDevCmd.bat'
$source=Join-Path $PSScriptRoot 'native/Images.cpp'
$dll=Join-Path $PSScriptRoot 'Catheryne.Images.dll'
$batch=Join-Path $cache 'build.cmd'
$commands=@('@echo off', "call `"$dev`" -arch=x64 -host_arch=x64 >nul", 'if errorlevel 1 exit /b 1', "cl /nologo /EHsc /O2 /MT /LD /I`"$dependency/include`" `"$source`" /Fo`"$cache/Images.obj`" /Fe`"$dll`" /link /IMPLIB:`"$cache/Images.lib`" `"$dependency/lib/libwebp.lib`"", 'exit /b %errorlevel%')
[IO.File]::WriteAllLines($batch,$commands,[Text.Encoding]::Default)
& $env:ComSpec /d /c $batch
if($LASTEXITCODE -ne 0){throw 'Image decoder build failed'}
