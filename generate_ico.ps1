Add-Type -AssemblyName System.Drawing

$srcPath = "C:\Users\Admin\Desktop\Jamb-Mock\src\CbtExam.Desktop\Resources\prep4jamb.png"
$outPath = "C:\Users\Admin\Desktop\Jamb-Mock\src\CbtExam.Desktop\Resources\appicon.ico"

$sizes = @(16, 32, 48, 64, 128, 256)

$src = New-Object System.Drawing.Bitmap($srcPath)

Write-Host "Source: $($src.Width) x $($src.Height)"

# ── Step 1: Find tight content bounding box (ignore near-transparent pixels) ─
$minX = $src.Width;  $maxX = 0
$minY = $src.Height; $maxY = 0

for ($y = 0; $y -lt $src.Height; $y++) {
    for ($x = 0; $x -lt $src.Width; $x++) {
        $px = $src.GetPixel($x, $y)
        if ($px.A -gt 10) {
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}

$contentW = $maxX - $minX + 1
$contentH = $maxY - $minY + 1
Write-Host "Content box: ${contentW} x ${contentH}  (X:$minX-$maxX  Y:$minY-$maxY)"

# ── Step 2: Crop to content bounding box ─────────────────────────────────────
$cropRect = New-Object System.Drawing.Rectangle($minX, $minY, $contentW, $contentH)
$cropped  = $src.Clone($cropRect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

# ── Step 3: Place cropped content centred on a square canvas (5% padding) ────
$squareSide = [Math]::Max($contentW, $contentH)
$padding    = [int]($squareSide * 0.05)   # 5% breathing room on each side
$canvasSize = $squareSide + $padding * 2

$square = New-Object System.Drawing.Bitmap($canvasSize, $canvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($square)
$g.Clear([System.Drawing.Color]::Transparent)
$g.CompositingMode    = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

$destX = $padding + [int](($squareSide - $contentW) / 2)
$destY = $padding + [int](($squareSide - $contentH) / 2)
$g.DrawImage($cropped, $destX, $destY, $contentW, $contentH)
$g.Dispose()
$cropped.Dispose()

Write-Host "Square canvas: ${canvasSize} x ${canvasSize}  (padding: ${padding}px each side)"

# ── Step 4: Resize to each standard size ─────────────────────────────────────
function Resize-ToSquare($image, $size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CompositingMode    = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($image, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

$frames = @()
foreach ($sz in $sizes) {
    $bmp   = Resize-ToSquare $square $sz
    $bytes = Get-PngBytes $bmp
    $bmp.Dispose()
    $frames += ,@{ Size = $sz; Bytes = $bytes }
    Write-Host "  Generated ${sz}x${sz}  ($($bytes.Length) bytes)"
}
$square.Dispose()
$src.Dispose()

# ── Step 5: Write ICO binary ──────────────────────────────────────────────────
$count      = $frames.Count
$dataOffset = 6 + 16 * $count

$ms = New-Object System.IO.MemoryStream
$ms.Write([BitConverter]::GetBytes([uint16]0),      0, 2)   # reserved
$ms.Write([BitConverter]::GetBytes([uint16]1),      0, 2)   # type = icon
$ms.Write([BitConverter]::GetBytes([uint16]$count), 0, 2)   # count

$currentOffset = $dataOffset
$offsets = @()
foreach ($f in $frames) { $offsets += $currentOffset; $currentOffset += $f.Bytes.Length }

for ($i = 0; $i -lt $count; $i++) {
    $f  = $frames[$i]
    $sz = $f.Size
    $ms.WriteByte($(if ($sz -eq 256) { 0 } else { [byte]$sz }))   # width
    $ms.WriteByte($(if ($sz -eq 256) { 0 } else { [byte]$sz }))   # height
    $ms.WriteByte(0)                                                # color count
    $ms.WriteByte(0)                                                # reserved
    $ms.Write([BitConverter]::GetBytes([uint16]1),               0, 2)
    $ms.Write([BitConverter]::GetBytes([uint16]32),              0, 2)
    $ms.Write([BitConverter]::GetBytes([uint32]$f.Bytes.Length), 0, 4)
    $ms.Write([BitConverter]::GetBytes([uint32]$offsets[$i]),    0, 4)
}

foreach ($f in $frames) { $ms.Write($f.Bytes, 0, $f.Bytes.Length) }

[System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
$ms.Dispose()

# Copy to root too
[System.IO.File]::Copy($outPath, "C:\Users\Admin\Desktop\Jamb-Mock\appicon.ico", $true)

Write-Host ""
Write-Host "ICO written: $outPath"
Write-Host "File size:   $((Get-Item $outPath).Length) bytes"
Write-Host "All done."
