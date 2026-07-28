# 从源 PNG 生成 Lingo.ico：
# 1) 定位蓝色圆角方块的包围盒并裁剪（去掉白底、主体占满画布）
# 2) 从四角洪泛填充，把圆角外连通的浅色像素置为透明
# 3) 输出 16/32/48/256 多尺寸 PNG 条目的 ICO
param(
    [string]$Source = "C:\Users\77935\.qoder\vibe_images\lingo-icon_1785196683.png",
    [string]$Target = "e:\Code\C#\Lingo\Assets\Lingo.ico"
)

Add-Type -AssemblyName System.Drawing

$src = New-Object System.Drawing.Bitmap($Source)

# --- 1. 蓝色主体包围盒（限制在上部区域，避开底部无关像素） ---
$minX = $src.Width; $minY = $src.Height; $maxX = 0; $maxY = 0
$scanBottom = [Math]::Min($src.Height - 1, [int]($src.Height * 0.92))
for ($y = 0; $y -le $scanBottom; $y += 2) {
    for ($x = 0; $x -lt $src.Width; $x += 2) {
        $c = $src.GetPixel($x, $y)
        if ($c.B -gt 90 -and $c.B -gt ($c.R + 30) -and $c.G -lt 160) {
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
Write-Host "blue bbox: ($minX,$minY)-($maxX,$maxY)"

$w = $maxX - $minX + 1
$h = $maxY - $minY + 1
$side = [Math]::Max($w, $h)
$crop = New-Object System.Drawing.Bitmap($side, $side)
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, [System.Drawing.Rectangle]::new(([int](($side - $w) / 2)), ([int](($side - $h) / 2)), $w, $h),
    [System.Drawing.Rectangle]::new($minX, $minY, $w, $h), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$src.Dispose()

# --- 2. 四角洪泛：浅色（亮度>190）且与角连通的像素透明化 ---
$visited = New-Object 'bool[,]' $side, $side
$queue = New-Object System.Collections.Generic.Queue[int[]]
foreach ($corner in @(@(0, 0), @(($side - 1), 0), @(0, ($side - 1)), @(($side - 1), ($side - 1)))) {
    $queue.Enqueue($corner)
}
$transparent = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
while ($queue.Count -gt 0) {
    $p = $queue.Dequeue()
    $x = $p[0]; $y = $p[1]
    if ($x -lt 0 -or $y -lt 0 -or $x -ge $side -or $y -ge $side) { continue }
    if ($visited[$x, $y]) { continue }
    $visited[$x, $y] = $true
    $c = $crop.GetPixel($x, $y)
    $bright = ($c.R * 0.3 + $c.G * 0.59 + $c.B * 0.11)
    if ($c.A -ne 0 -and $bright -lt 190) { continue }
    $crop.SetPixel($x, $y, $transparent)
    $queue.Enqueue(@(($x + 1), $y)); $queue.Enqueue(@(($x - 1), $y))
    $queue.Enqueue(@($x, ($y + 1))); $queue.Enqueue(@($x, ($y - 1)))
}

# --- 3. 写多尺寸 ICO ---
$sizes = @(16, 32, 48, 256)
$images = @()
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($crop, 0, 0, $size, $size)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $images += , @($size, $ms.ToArray())
    $ms.Dispose()
}
$crop.Dispose()

$fs = [System.IO.File]::Create($Target)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($entry in $images) {
    $size = $entry[0]
    $data = $entry[1]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
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
