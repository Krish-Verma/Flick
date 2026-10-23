<#
.SYNOPSIS
Build ICO from the original Flick squircle PNG. Never swap the brand artwork.
#>
param(
    [Parameter(Mandatory=$true)][string]$InputPng,
    [Parameter(Mandatory=$true)][string]$OutputIco
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$InputPng = [IO.Path]::GetFullPath($InputPng)
$OutputIco = [IO.Path]::GetFullPath($OutputIco)
if (-not (Test-Path -LiteralPath $InputPng -PathType Leaf)) { throw "Flick logo missing: $InputPng" }
$source = [Drawing.Bitmap]::new($InputPng)
$images = [System.Collections.Generic.List[object]]::new()
try {
    foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
        $bmp = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bmp)
        $clip = [Drawing.Drawing2D.GraphicsPath]::new()
        $canvas = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 6, 3, 13))
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            # Match Flick.Image.BrandMark: a fixed dark circular viewport with
            # the original bitmap shifted by 1 DIP in its 38-DIP design space.
            # Never move the circle or alter the original asset's pixels.
            $viewport = [Drawing.RectangleF]::new(0, 0, $size, $size)
            $graphics.FillEllipse($canvas, $viewport)
            $clip.AddEllipse($viewport)
            $graphics.SetClip($clip)
            $offset = [single]($size / 38.0)
            $graphics.DrawImage($source, [Drawing.RectangleF]::new($offset, $offset, $size, $size))
            $buffer = [IO.MemoryStream]::new()
            try {
                $bmp.Save($buffer, [Drawing.Imaging.ImageFormat]::Png)
                $images.Add([pscustomobject]@{ Size = $size; Bytes = $buffer.ToArray() })
            } finally { $buffer.Dispose() }
        } finally {
            $canvas.Dispose()
            $clip.Dispose()
            $graphics.Dispose()
            $bmp.Dispose()
        }
    }
} finally { $source.Dispose() }

[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputIco)) | Out-Null
$stream = [IO.File]::Open($OutputIco, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $dim = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$dim)
        $writer.Write([byte]$dim)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $entry.Bytes.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Bytes) }
} finally { $writer.Dispose() }
if ((Get-Item -LiteralPath $OutputIco).Length -le 0) { throw "Generated empty icon" }
Write-Host "Flick multi-size icon: $OutputIco" -ForegroundColor Green
