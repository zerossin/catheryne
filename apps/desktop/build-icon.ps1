$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$source=Join-Path $PSScriptRoot 'branding/launcher.png'
$image=New-Object Windows.Media.Imaging.BitmapImage
$image.BeginInit();$image.CacheOption=[Windows.Media.Imaging.BitmapCacheOption]::OnLoad;$image.UriSource=[Uri]$source;$image.EndInit();$image.Freeze()
$sizes=@(16,24,32,48,64,128,256)
$payloads=@()
foreach($size in $sizes){
 $visual=New-Object Windows.Media.DrawingVisual
 [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual,[Windows.Media.BitmapScalingMode]::HighQuality)
 $context=$visual.RenderOpen()
 $brush=New-Object Windows.Media.ImageBrush $image
 $brush.Stretch=[Windows.Media.Stretch]::UniformToFill
 $context.DrawRoundedRectangle($brush,$null,(New-Object Windows.Rect 0,0,$size,$size),($size*0.094),($size*0.094));$context.Close()
 $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap $size,$size,96,96,([Windows.Media.PixelFormats]::Pbgra32)
 $bitmap.Render($visual)
 $png=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $png.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $stream=New-Object IO.MemoryStream;$png.Save($stream);$payloads+=,$stream.ToArray();$stream.Dispose()
}
$output=New-Object IO.BinaryWriter ([IO.File]::Create((Join-Path $PSScriptRoot 'launcher.ico')))
try {
 $output.Write([uint16]0);$output.Write([uint16]1);$output.Write([uint16]$sizes.Count)
 $offset=6+16*$sizes.Count
 for($i=0;$i -lt $sizes.Count;$i++){$dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]};$output.Write([byte]$dimension);$output.Write([byte]$dimension);$output.Write([byte]0);$output.Write([byte]0);$output.Write([uint16]1);$output.Write([uint16]32);$output.Write([int]$payloads[$i].Length);$output.Write([int]$offset);$offset+=$payloads[$i].Length}
 foreach($payload in $payloads){$output.Write([byte[]]$payload)}
}finally{$output.Dispose()}
