# CinematicCam simgesini (assets\CinematicCam.ico) üretir: koyu zemin üzerinde amber kamera.
# Windows PowerShell 5.1 ile çalışır:  powershell -ExecutionPolicy Bypass -File installer\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets\CinematicCam.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    $bg = New-RoundedRect (8 * $s) (8 * $s) (240 * $s) (240 * $s) (52 * $s)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x16, 0x19, 0x20))), $bg)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x2A, 0x30, 0x3B)), ([Math]::Max(1, 6 * $s))), $bg)

    $amber = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xFF, 0xB5, 0x47))
    $body = New-RoundedRect (40 * $s) (78 * $s) (122 * $s) (100 * $s) (18 * $s)
    $g.FillPath($amber, $body)
    $lens = @(
        (New-Object System.Drawing.PointF (166 * $s), (112 * $s)),
        (New-Object System.Drawing.PointF (218 * $s), (84 * $s)),
        (New-Object System.Drawing.PointF (218 * $s), (172 * $s)),
        (New-Object System.Drawing.PointF (166 * $s), (144 * $s))
    )
    $g.FillPolygon($amber, $lens)
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x1A, 0x14, 0x06))
    $g.FillEllipse($dark, (78 * $s), (105 * $s), (46 * $s), (46 * $s))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 128) {
        # Büyük boyutlar PNG olarak saklanır.
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        # Küçük boyutlar klasik DIB: BITMAPINFOHEADER + 32 bit BGRA (alttan üste) + AND maskesi.
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([UInt32]40); $bw.Write([Int32]$size); $bw.Write([Int32]($size * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]($size * $size * 4)); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
        $bw.Write((New-Object byte[] ($maskRow * $size)))
        $bw.Flush()
    }
    $bmp.Dispose()
    return , $ms.ToArray()
}

# ICO kabı: başlık + dizin + PNG görüntüler (Vista ve sonrası PNG girişleri destekler).
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($sz in $sizes) { $images.Add([byte[]](New-IconPng $sz)) }
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
# README ve GitHub için 256 px PNG.
[System.IO.File]::WriteAllBytes((Join-Path $root 'assets\CinematicCam.png'), $images[$images.Count - 1])
Write-Host "Simge yazıldı: $out"
