<#
.SYNOPSIS
Rasterize Flick's canonical vector SVG to a transparent, circular WPF PNG and
a matching multi-size Windows ICO. No network/native SVG parser is involved.
.DESCRIPTION
Reads only the project's deliberately limited, version-controlled SVG schema:
an outer circle, inner gradient circle, evenly spaced horizontal rectangles
and one filled angular F polygon. No arbitrary SVG script/foreign objects.
#>
param(
    [Parameter(Mandatory=$true)][string]$InputSvg,
    [Parameter(Mandatory=$true)][string]$OutputPng,
    [Parameter(Mandatory=$true)][string]$OutputIco
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$InputSvg = [IO.Path]::GetFullPath($InputSvg)
$OutputPng = [IO.Path]::GetFullPath($OutputPng)
$OutputIco = [IO.Path]::GetFullPath($OutputIco)
if (-not (Test-Path -LiteralPath $InputSvg -PathType Leaf)) { throw "Canonical Flick vector logo missing: $InputSvg" }

[xml]$svg = Get-Content -LiteralPath $InputSvg -Raw
$ns = [Xml.XmlNamespaceManager]::new($svg.NameTable)
$ns.AddNamespace("s", "http://www.w3.org/2000/svg")
$root = $svg.SelectSingleNode("/s:svg", $ns)
if ($null -eq $root -or $root.GetAttribute("viewBox") -cne "0 0 128 128") {
    throw "Flick brand source must be 128x128 canonical SVG"
}
$rim = $svg.SelectSingleNode("//s:circle[@id='rim']", $ns)
$face = $svg.SelectSingleNode("//s:circle[@id='face']", $ns)
$clip = $svg.SelectSingleNode("//s:clipPath[@id='inner-disk']/s:circle", $ns)
$gradient = $svg.SelectSingleNode("//s:linearGradient[@id='sunset']", $ns)
$stripes = @($svg.SelectNodes("//s:g[@id='stripes']/s:rect", $ns))
$letter = $svg.SelectSingleNode("//s:polygon[@id='letter']", $ns)
if ($null -eq $rim -or $null -eq $face -or $null -eq $clip -or
    $null -eq $gradient -or $null -eq $letter -or $stripes.Count -ne 7) {
    throw "Flick canonical logo geometry is incomplete"
}
function Number([string]$value) {
    return [single]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
}
function Color([string]$value) {
    if ($value -cnotmatch '^#[A-Fa-f0-9]{6}$') { throw "Non-solid SVG color in Flick brand" }
    return [Drawing.ColorTranslator]::FromHtml($value)
}
$centerX = Number $rim.GetAttribute("cx")
$centerY = Number $rim.GetAttribute("cy")
$radius = Number $rim.GetAttribute("r")
$diskRadius = Number $face.GetAttribute("r")
if ($centerX -ne 64 -or $centerY -ne 64 -or $radius -ne 62 -or
    $diskRadius -ne 58.5 -or (Number $clip.GetAttribute("r")) -ne $diskRadius -or
    $face.GetAttribute("fill") -cne "url(#sunset)") {
    throw "Outer rim, circular gradient disk or clip are inconsistent"
}
$stops = @($gradient.SelectNodes("s:stop", $ns))
if ($stops.Count -lt 4 -or $stops.Count -gt 12) { throw "Expected a bounded Flick sunset gradient" }
$colors = [Drawing.Color[]]@($stops | ForEach-Object { Color $_.GetAttribute("stop-color") })
$positions = [single[]]@($stops | ForEach-Object { Number $_.GetAttribute("offset") })
if ($positions[0] -ne 0 -or $positions[-1] -ne 1) { throw "Gradient must cover the complete disk" }
for ($i=1; $i -lt $positions.Length; $i++) {
    if ($positions[$i] -le $positions[$i-1] -or $positions[$i] -gt 1) {
        throw "Gradient stops must be strictly increasing"
    }
}
$lastY = -1
foreach ($stripe in $stripes) {
    $y = Number $stripe.GetAttribute("y")
    if ((Number $stripe.GetAttribute("x")) -ne 4 -or
        (Number $stripe.GetAttribute("width")) -ne 120 -or
        (Number $stripe.GetAttribute("height")) -ne 3 -or
        ($lastY -ge 0 -and ($y - $lastY) -ne 13)) {
        throw "Gradient stripes must be full-width, straight and evenly spaced"
    }
    $lastY = $y
}
$vertices = @($letter.GetAttribute("points") -split '\s+' | Where-Object { $_.Length -gt 0 })
if ($vertices.Count -lt 9 -or $vertices.Count -gt 20) { throw "Unexpected Flick F polygon" }
$points = [Drawing.PointF[]]@($vertices | ForEach-Object {
    $xy = $_.Split(',')
    if ($xy.Count -ne 2) { throw "Invalid polygon point" }
    [Drawing.PointF]::new((Number $xy[0]), (Number $xy[1]))
})
$rimColor = Color $rim.GetAttribute("fill")
$stripeColor = Color $svg.SelectSingleNode("//s:g[@id='stripes']", $ns).GetAttribute("fill")
$letterColor = Color $letter.GetAttribute("fill")

function Draw-Logo([int]$size) {
    $bmp = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $rimBrush = [Drawing.SolidBrush]::new($rimColor)
    $stripesBrush = [Drawing.SolidBrush]::new($stripeColor)
    $letterBrush = [Drawing.SolidBrush]::new($letterColor)
    $sunset = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.PointF]::new(64, 5.5), [Drawing.PointF]::new(64, 122.5), $colors[0], $colors[-1])
    $diskClip = [Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $g.Clear([Drawing.Color]::Transparent)
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.ScaleTransform([single]$size / 128.0, [single]$size / 128.0)
        $blend = [Drawing.Drawing2D.ColorBlend]::new($colors.Length)
        $blend.Colors = $colors
        $blend.Positions = $positions
        $sunset.InterpolationColors = $blend
        $g.FillEllipse($rimBrush, 64-$radius, 64-$radius, $radius*2, $radius*2)
        $g.FillEllipse($sunset, 64-$diskRadius, 64-$diskRadius, $diskRadius*2, $diskRadius*2)
        $diskClip.AddEllipse([Drawing.RectangleF]::new(
            64-$diskRadius, 64-$diskRadius, $diskRadius*2, $diskRadius*2))
        $g.SetClip($diskClip)
        foreach ($stripe in $stripes) {
            $g.FillRectangle($stripesBrush, (Number $stripe.GetAttribute("x")),
                (Number $stripe.GetAttribute("y")), (Number $stripe.GetAttribute("width")),
                (Number $stripe.GetAttribute("height")))
        }
        $g.FillPolygon($letterBrush, $points)
        $g.ResetClip()
    }
    finally {
        $diskClip.Dispose()
        $sunset.Dispose()
        $letterBrush.Dispose()
        $stripesBrush.Dispose()
        $rimBrush.Dispose()
        $g.Dispose()
    }
    return $bmp
}

function Make-LogoPng([int]$size) {
    $high = Draw-Logo ($size * 4)
    $small = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($small)
    try {
        $g.Clear([Drawing.Color]::Transparent)
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($high, [Drawing.Rectangle]::new(0,0,$size,$size),
            [Drawing.Rectangle]::new(0,0,$high.Width,$high.Height), [Drawing.GraphicsUnit]::Pixel)
        return $small
    } finally {
        $g.Dispose()
        $high.Dispose()
    }
}

[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPng)) | Out-Null
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputIco)) | Out-Null
$source = Make-LogoPng 512
try { $source.Save($OutputPng, [Drawing.Imaging.ImageFormat]::Png) }
finally { $source.Dispose() }
$images = [System.Collections.Generic.List[object]]::new()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = Make-LogoPng $size
    try {
        $stream = [IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $images.Add([pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() })
        } finally { $stream.Dispose() }
    } finally { $bitmap.Dispose() }
}
$stream = [IO.File]::Open($OutputIco, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $dim = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$dim); $writer.Write([byte]$dim)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Bytes.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Bytes) }
} finally { $writer.Dispose() }
if ((Get-Item -LiteralPath $OutputPng).Length -le 5000 -or
    (Get-Item -LiteralPath $OutputIco).Length -le 5000) {
    throw "Generated Flick brand assets are suspiciously small"
}
Write-Host "Flick circular vector brand generated: PNG=$OutputPng ICO=$OutputIco" -ForegroundColor Green
