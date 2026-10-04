$ErrorActionPreference='Stop'
$packages=@(
 @('markdig','1.4.0','BDD84353A3F499F989F3111B33ACEEBD9434F8A069CA8B3DA2A065F2230E7BAF','net462','Markdig'),
 @('system.memory','4.6.3','26078AEB758C9AE985E8BF851F973026061DA6A5EB4837204D0C2D2204C72955','net462','System.Memory'),
 @('system.buffers','4.6.1','B00451E91D016FBEC091AD1E361F3A7015E1D91D4047F7E48A74455B2A673D79','net462','System.Buffers'),
 @('system.runtime.compilerservices.unsafe','6.1.2','5F6A7F53AF3465F92BEB6DA873EBE0E496206C313313B98BADEE4355A6B25937','net462','System.Runtime.CompilerServices.Unsafe'),
 @('system.numerics.vectors','4.6.1','2BC500A86DCB02F2032D6D877F9E2D6E9E4A79080E57239B4198679D4031F2C7','net462','System.Numerics.Vectors')
)
foreach($p in $packages){
 $cache=Join-Path ([IO.Path]::GetTempPath()) ('catheryne-package-'+$p[0]+'-'+$p[1])
 $zip=$cache+'.zip'
 if(!(Test-Path $zip)){Invoke-WebRequest ('https://api.nuget.org/v3-flatcontainer/'+$p[0]+'/'+$p[1]+'/'+$p[0]+'.'+$p[1]+'.nupkg') -OutFile $zip}
 if((Get-FileHash $zip -Algorithm SHA256).Hash -ne $p[2]){throw ('Package hash mismatch: '+$p[0])}
 $source=Join-Path $cache ('lib/'+$p[3]+'/'+$p[4]+'.dll')
 if(!(Test-Path -LiteralPath $source)){Expand-Archive -LiteralPath $zip -DestinationPath $cache -Force}
 $target=Join-Path $PSScriptRoot ($p[4]+'.dll')
 if(!(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash){Copy-Item -LiteralPath $source -Destination $target -Force}
}
