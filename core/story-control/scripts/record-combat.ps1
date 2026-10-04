$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    # Elevate this interactive recorder, not Codex or the game.
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    $child = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    exit $child.ExitCode
}
$workspace = Join-Path $env:LOCALAPPDATA 'Catheryne'
$bindingPath = Join-Path $workspace 'ai-connection.json'
$installationPath = Join-Path $workspace 'installation.json'
if (-not (Test-Path -LiteralPath $bindingPath) -or -not (Test-Path -LiteralPath $installationPath)) {
    throw 'Configure Catheryne before recording.'
}
$binding = Get-Content -LiteralPath $bindingPath -Raw | ConvertFrom-Json
$installation = Get-Content -LiteralPath $installationPath -Raw | ConvertFrom-Json
$gamePython = [string]$binding.story_python
if (-not (Test-Path -LiteralPath $gamePython -PathType Leaf)) {
    throw 'Prepare the Catheryne runtime before recording.'
}
$configPath = Join-Path (Split-Path -Parent $installation.Engine) 'fps_config.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if (-not (Test-Path -LiteralPath $config.GamePath -PathType Leaf)) {
    throw 'Check the configured game location in Catheryne.'
}
$env:CATHERYNE_GAME_EXE = [string]$config.GamePath
$recordingName = 'combat-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.json'
$recordingPath = Join-Path $workspace ('combat\recordings\' + $recordingName)
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src'))
& $gamePython -B -c "import sys,runpy;sys.path.insert(0,sys.argv.pop(1));runpy.run_module('story_control.combat_recording',run_name='__main__')" $source record $recordingPath --seconds 120 --continuous
exit $LASTEXITCODE
