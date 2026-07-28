# 将 PNG 转换为多尺寸 ICO（16/32/48/256，全部使用 PNG 编码条目）
param(
    [string]$Source = "C:\Users\77935\.qoder\vibe_images\lingo-icon_1785196683.png",
    [string]$Target = "e:\Code\C#\Lingo\Lingo\Assets\Lingo.ico"
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 32, 48, 256)
$images = @()

$src = [System.Drawing.Image]::FromFile($Source)
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $images += , @($size, $ms.ToArray())
    $ms.Dispose()
}
$src.Dispose()

New-Item -ItemType Directory -Force -Path (Split-Path $Target) | Out-Null
$fs = [System.IO.File]::Create($Target)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$images.Count)

$offset = 6 + 16 * $images.Count
foreach ($entry in $images) {
    $size = $entry[0]
    $data = $entry[1]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([Byte]$dim)          # width
    $bw.Write([Byte]$dim)          # height
    $bw.Write([Byte]0)             # palette
    $bw.Write([Byte]0)             # reserved
    $bw.Write([UInt16]1)           # planes
    $bw.Write([UInt16]32)          # bit count
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($entry in $images) {
    $bw.Write($entry[1])
}
$bw.Close()
$fs.Close()
Write-Host "ICO written: $Target"
