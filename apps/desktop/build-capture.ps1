param()
$ErrorActionPreference='Stop'
# Build with the locally installed Windows SDK and MSVC; users receive the DLL.
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if(!(Test-Path -LiteralPath $vswhere)){throw 'MSVC and the Windows 10/11 SDK are required to build the capture helper'}
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'MSVC x64 build tools not found'}
$dev=Join-Path $vs 'Common7/Tools/VsDevCmd.bat'
$output=Join-Path $PSScriptRoot 'native-build'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$source=Join-Path $PSScriptRoot 'native/Capture.cpp'
$dll=Join-Path $PSScriptRoot 'Catheryne.Capture.dll'
$batch=Join-Path $output 'build.cmd'
$commands=@("@echo off", "call `"$dev`" -arch=x64 -host_arch=x64 >nul", "if errorlevel 1 exit /b 1", "cl /nologo /std:c++17 /EHsc /O2 /MT /LD /DWIN32_LEAN_AND_MEAN `"$source`" /Fo`"$output\Capture.obj`" /Fe`"$dll`" /link /IMPLIB:`"$output\Capture.lib`" windowsapp.lib d3d11.lib dxgi.lib dwmapi.lib user32.lib ole32.lib", "exit /b %errorlevel%")
[IO.File]::WriteAllLines($batch,$commands,[Text.Encoding]::Default)
& $env:ComSpec /d /c $batch
if($LASTEXITCODE -ne 0){throw 'Capture helper build failed'}
