param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$targetRoot=(Resolve-Path -LiteralPath $Destination).Path
$documents=@(
 @{Source='USER-GUIDE.md';Target='README.md'},
 @{Source='USER-GUIDE.ko.md';Target='README.ko.md'},
 @{Source='../NOTICE.md';Target='NOTICE.md'}
)
foreach($document in $documents){
 $source=Join-Path $repoRoot ('docs/'+$document.Source)
 $content=[IO.File]::ReadAllText($source).Replace('](USER-GUIDE.md)','](README.md)').Replace('](USER-GUIDE.ko.md)','](README.ko.md)').Replace('](apps/desktop/THIRD-PARTY.md)','](THIRD-PARTY.md)').Replace('](apps/desktop/branding/README.md)','](branding/README.md)')
 [IO.File]::WriteAllText((Join-Path $targetRoot $document.Target),$content,[Text.UTF8Encoding]::new($false))
}
