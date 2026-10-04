$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('catheryne-build-cleanup-'+[Guid]::NewGuid().ToString('N'))
$root=Join-Path $fixture 'dist'
try{
 New-Item -ItemType Directory -Path $root -Force | Out-Null
 Set-Content -LiteralPath (Join-Path $fixture 'VERSION') -Value 'test'
 Set-Content -LiteralPath (Join-Path $fixture 'package.ps1') -Value '# fixture'
 foreach($name in @('20260928-0150-runtime','20261001-100000','setup','notes')){
  $folder=Join-Path $root $name;New-Item -ItemType Directory -Path $folder | Out-Null
  Set-Content -LiteralPath (Join-Path $folder 'payload.txt') -Value 'fixture'
 }
 $script=Join-Path $PSScriptRoot 'clean-builds.ps1'
 $plan=@(& $script -BuildRoot $root)
 if($plan.Count -ne 1 -or $plan[0].Applied -or !(Test-Path -LiteralPath $plan[0].Path)){throw 'Dry run changed files'}
 & $script -BuildRoot $root -Apply | Out-Null
 foreach($name in @('20261001-100000','setup','notes')){if(!(Test-Path -LiteralPath (Join-Path $root $name))){throw 'Preserved output was deleted'}}
 if(Test-Path -LiteralPath (Join-Path $root '20260928-0150-runtime')){throw 'Old package was not deleted'}
 $private=Join-Path $root '20260927-100000';New-Item -ItemType Directory -Path $private | Out-Null
 Set-Content -LiteralPath (Join-Path $private 'settings.json') -Value '{}'
 $rejected=$false;try{& $script -BuildRoot $root -Apply | Out-Null}catch{$rejected=$true}
 if(!$rejected -or !(Test-Path -LiteralPath $private)){throw 'Private records were not protected'}
 Write-Output 'PASS: dry run, retention and private-record protection'
}finally{
 $resolved=[IO.Path]::GetFullPath($fixture)
 if($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -match '^catheryne-build-cleanup-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $resolved)){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
