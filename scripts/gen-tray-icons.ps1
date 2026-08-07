#requires -Version 5.1
<#
.SYNOPSIS
  Generates the tray status icons (grey/amber/green/red circles) into Resources\.
  Run once when the icons need regenerating; the .ico files are committed.
#>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path (Split-Path -Parent $PSScriptRoot) "src\SimpleRay.App\Resources"

# state -> fill color (with a slightly darker ring for contrast on any taskbar)
$states = @{
    "tray-off"        = [System.Drawing.Color]::FromArgb(158,158,158)  # grey  = disconnected
    "tray-connecting" = [System.Drawing.Color]::FromArgb(245,166,35)   # amber = connecting/reconnecting
    "tray-on"         = [System.Drawing.Color]::FromArgb(63,185,80)     # green = connected
    "tray-fault"      = [System.Drawing.Color]::FromArgb(229,83,75)     # red   = fault
}
$sizes = 16, 24, 32, 48

function New-CirclePng([int]$size, [System.Drawing.Color]$fill) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $margin = [Math]::Max(1, [int]($size * 0.12))
    $d = $size - 2 * $margin
    $ring = [System.Drawing.Color]::FromArgb(200, [int]($fill.R*0.55), [int]($fill.G*0.55), [int]($fill.B*0.55))
    $brush = New-Object System.Drawing.SolidBrush($fill)
    $pen = New-Object System.Drawing.Pen($ring, [Math]::Max(1, $size/16))
    $g.FillEllipse($brush, $margin, $margin, $d, $d)
    $g.DrawEllipse($pen, $margin, $margin, $d, $d)
    $g.Dispose(); $brush.Dispose(); $pen.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $bytes = $ms.ToArray()
    return ,$bytes  # leading comma: return the byte[] as one object, not enumerated
}

function Write-Ico([string]$path, [int[]]$sizes, [System.Drawing.Color]$fill) {
    $pngs = New-Object 'System.Collections.Generic.List[byte[]]'
    foreach ($s in $sizes) { $pngs.Add((New-CirclePng $s $fill)) }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count) # ICONDIR
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $len = $pngs[$i].Length
        $bw.Write([byte]($(if ($s -ge 256) {0} else {$s})))  # width
        $bw.Write([byte]($(if ($s -ge 256) {0} else {$s})))  # height
        $bw.Write([byte]0); $bw.Write([byte]0)               # colors, reserved
        $bw.Write([uint16]1); $bw.Write([uint16]32)          # planes, bpp
        $bw.Write([uint32]$len); $bw.Write([uint32]$offset)  # size, offset
        $offset += $len
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Flush(); $fs.Close()
}

foreach ($name in $states.Keys) {
    $path = Join-Path $outDir "$name.ico"
    Write-Ico $path $sizes $states[$name]
    Write-Host "wrote $path"
}
