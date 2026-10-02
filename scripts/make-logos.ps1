<#
.SYNOPSIS
  Draws the Flow Mate app icon (rounded square, indigo to purple gradient, white lightning bolt) and writes
  - the app icon: src\Flowmate.App\Assets\flowmate.ico / flowmate.png
  - every tile and Store logo the MSIX needs (all scales): packaging\Assets\
  - Partner Center listing icons: docs\store\assets\
  Re-run after changing the design below. Needs only Windows (System.Drawing).
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'packaging\Assets'
$appAssets = Join-Path $root 'src\Flowmate.App\Assets'
$listing = Join-Path $root 'docs\store\assets'
foreach ($d in $assets, $appAssets, $listing) { New-Item -ItemType Directory -Force $d | Out-Null }

# ---- the design -------------------------------------------------------------------------------
$top    = [System.Drawing.ColorTranslator]::FromHtml('#5B50F0')   # indigo
$bottom = [System.Drawing.ColorTranslator]::FromHtml('#A23FE6')   # purple
# Lightning bolt in a unit square (0..1), pointing down-left.
$bolt = @(@(0.60, 0.08), @(0.27, 0.56), @(0.47, 0.56), @(0.39, 0.92), @(0.73, 0.43), @(0.53, 0.43))

function New-Logo([int]$w, [int]$h, [double]$radiusFraction = 0.22) {
    $ss = 4                                   # draw 4x bigger, then shrink: smooth edges at every size
    $bw = $w * $ss; $bh = $h * $ss
    $big = New-Object System.Drawing.Bitmap $bw, $bh, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)

    $short = [Math]::Min($bw, $bh)
    $r = [int]($short * $radiusFraction); $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($bw - $d, 0, $d, $d, 270, 90)
    $path.AddArc($bw - $d, $bh - $d, $d, $d, 0, 90); $path.AddArc(0, $bh - $d, $d, $d, 90, 90); $path.CloseFigure()
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bw, $bh
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $top, $bottom, 55
    $g.FillPath($brush, $path)

    # soft highlight on the upper half
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(46, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90
    $g.FillPath($hl, $path)

    # bolt, centred, 66% of the short side
    $size = $short * 0.66
    $ox = ($bw - $size) / 2; $oy = ($bh - $size) / 2
    $pts = $bolt | ForEach-Object { New-Object System.Drawing.PointF ([single]($ox + $_[0] * $size)), ([single]($oy + $_[1] * $size)) }
    $shadow = $bolt | ForEach-Object { New-Object System.Drawing.PointF ([single]($ox + $_[0] * $size + $short * 0.012)), ([single]($oy + $_[1] * $size + $short * 0.018)) }
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 30, 10, 90))), [System.Drawing.PointF[]]$shadow)
    $g.FillPolygon([System.Drawing.Brushes]::White, [System.Drawing.PointF[]]$pts)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]($short * 0.035))
    $pen.LineJoin = 'Round'
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]$pts)   # rounds the bolt's corners
    $g.Dispose()

    $out = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g2 = [System.Drawing.Graphics]::FromImage($out)
    $g2.InterpolationMode = 'HighQualityBicubic'; $g2.PixelOffsetMode = 'HighQuality'; $g2.CompositingQuality = 'HighQuality'
    $g2.DrawImage($big, 0, 0, $w, $h)
    $g2.Dispose(); $big.Dispose()
    return $out
}

function Save-Png($bmp, [string]$file) { $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose() }

function Get-PngBytes([int]$size) {
    $bmp = New-Logo $size $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return , $ms.ToArray()
}

function Save-Ico([string]$file, [int[]]$sizes) {
    $pngs = $sizes | ForEach-Object { , (Get-PngBytes $_) }
    $fs = [System.IO.File]::Create($file); $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $b = [byte[]]$pngs[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$b.Length); $bw.Write([uint32]$offset)
        $offset += $b.Length
    }
    foreach ($b in $pngs) { $bw.Write([byte[]]$b) }
    $bw.Close(); $fs.Close()
}

# ---- app icon ---------------------------------------------------------------------------------
Save-Ico (Join-Path $appAssets 'flowmate.ico') @(16, 24, 32, 48, 64, 128, 256)
Save-Png (New-Logo 256 256) (Join-Path $appAssets 'flowmate.png')

# ---- MSIX tiles and logos (scale-100 size x scale; scales 100 125 150 200 400) -------------------
$scales = 100, 125, 150, 200, 400
function Save-Scaled([string]$name, [int]$w, [int]$h, [double]$radius = 0.22) {
    foreach ($s in $scales) {
        Save-Png (New-Logo ([int][Math]::Round($w * $s / 100)) ([int][Math]::Round($h * $s / 100)) $radius) (Join-Path $assets "$name.scale-$s.png")
    }
}
Save-Scaled 'Square44x44Logo' 44 44
Save-Scaled 'Square71x71Logo' 71 71
Save-Scaled 'Square150x150Logo' 150 150
Save-Scaled 'Square310x310Logo' 310 310
Save-Scaled 'Wide310x150Logo' 310 150 0.16
Save-Scaled 'StoreLogo' 50 50

# Taskbar, Start list and title-bar sizes (plain, and without the tile plate).
foreach ($t in 16, 24, 32, 48, 256) {
    foreach ($suffix in '', '_altform-unplated', '_altform-lightunplated') {
        Save-Png (New-Logo $t $t) (Join-Path $assets "Square44x44Logo.targetsize-$t$suffix.png")
    }
}

# ---- Partner Center listing icons -------------------------------------------------------------
Save-Png (New-Logo 300 300) (Join-Path $listing 'store-icon-300.png')
Save-Png (New-Logo 1080 1080) (Join-Path $listing 'store-icon-1080.png')
Save-Png (New-Logo 512 512) (Join-Path $listing 'store-icon-512.png')

Write-Host "App icon:  $appAssets\flowmate.ico, flowmate.png"
Write-Host ("MSIX logos: {0} files in {1}" -f (Get-ChildItem $assets -File).Count, $assets)
Write-Host "Listing:   $listing"
