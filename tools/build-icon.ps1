# ASCII-only. Build multi-size app.ico (PNG-compressed frames, Vista+) from the master 1024px PNG.
# Usage: powershell -File tools/build-icon.ps1   (paths relative to repo root)
param(
    [string]$Master = "docs\brand\fancytext-icon-1024.png",
    [string]$Out    = "src\FancyText.Desktop\Assets\app.ico"
)
Add-Type -AssemblyName System.Drawing

$root = (Get-Item $PSScriptRoot).Parent.FullName
$masterPath = Join-Path $root $Master
$outPath = Join-Path $root $Out
New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null

$src = [System.Drawing.Bitmap]::FromFile($masterPath)
if ($src.Width -ne $src.Height) { throw "master must be square" }

$sizes = 256, 128, 64, 48, 32, 24, 16
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($src, 0, 0, $s, $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $frames += ,@($s, $ms.ToArray())
    $ms.Dispose()
}
$src.Dispose()

# ICO container: ICONDIR + ICONDIRENTRY*n + PNG blobs
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0)              # reserved
$bw.Write([uint16]1)              # type = icon
$bw.Write([uint16]$frames.Count)  # count
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $data = $f[1]
    $bw.Write([byte]($s -band 0xFF)) # width (256 -> 0)
    $bw.Write([byte]($s -band 0xFF)) # height
    $bw.Write([byte]0)               # palette
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # planes
    $bw.Write([uint16]32)            # bpp
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $bw.Write($f[1]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

"ico written: $outPath ($([IO.File]::ReadAllBytes($outPath).Length) bytes, $($frames.Count) frames)"
