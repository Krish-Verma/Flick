# Flick design-foundation static checks. This does NOT claim visual/runtime QA.
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
$themeRoot = Join-Path $root "src/LocalClip.App/Resources/Theme"
$themeNames = @(
    "Flick.Colors.xaml",
    "Flick.Metrics.xaml",
    "Flick.Typography.xaml",
    "Flick.Icons.xaml",
    "Flick.Controls.xaml",
    "Flick.Chrome.xaml"
)
$keys = @{}
$duplicateKeys = @()
foreach ($name in $themeNames) {
    $path = Join-Path $themeRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing WPF theme: $path" }
    [xml]$xml = Get-Content -LiteralPath $path -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace("x", "http://schemas.microsoft.com/winfx/2006/xaml")
    $items = $xml.SelectNodes('//*[@x:Key]', $ns)
    foreach ($item in $items) {
        $key = $item.GetAttribute("Key", "http://schemas.microsoft.com/winfx/2006/xaml")
        if ($keys.ContainsKey($key)) { $duplicateKeys += $key }
        else { $keys[$key] = $name }
    }
}
if ($duplicateKeys.Count) {
    throw "Duplicate shared WPF resource keys: $($duplicateKeys -join ', ')"
}

$required = @(
    "Flick.Brush.Canvas", "Flick.Brush.Toast", "Flick.Brush.Accent",
    "Flick.Brush.BrandGradient", "Flick.Brush.TextPrimary",
    "Flick.Corner.Control", "Flick.Corner.Panel", "Flick.Font.UI",
    "Flick.Space.1", "Flick.Space.2", "Flick.Space.3",
    "Flick.Space.4", "Flick.Space.6", "Flick.Space.8",
    "Flick.Text.PageTitle", "Flick.Text.Section", "Flick.Text.Body",
    "Flick.Text.Caption", "Flick.Image.BrandMark", "Flick.Icon.Line",
    "Flick.Button.Primary", "Flick.Button.Secondary",
    "Flick.Button.Ghost", "Flick.Button.Icon", "Flick.Button.Busy",
    "Flick.Input.Text", "Flick.Input.Combo", "Flick.Toggle",
    "Flick.Input.Slider", "Flick.Surface.Frosted", "Flick.Surface.Toast",
    "Flick.Scrollbar.Thumb", "Flick.Scrollbar.Thumb.Vertical",
    "Flick.Scrollbar.Thumb.Horizontal", "Flick.Scrollbar.PageButton"
)
foreach ($key in $required) {
    if (-not $keys.ContainsKey($key)) { throw "Missing mandatory WPF token: $key" }
}
$files = @($themeNames | ForEach-Object { Join-Path $themeRoot $_ })
$files += Join-Path $root "src/LocalClip.App/Views/DesignSystemPreviewWindow.xaml"
$files += Join-Path $root "src/LocalClip.App/Views/Components/FlickClipToast.xaml"
$files += Join-Path $root "src/LocalClip.App/App.xaml"
$allXaml = ($files | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
$refs = [regex]::Matches($allXaml, '(?:StaticResource|DynamicResource)\s+(Flick\.[A-Za-z0-9.]+)')
foreach ($match in $refs) {
    $key = $match.Groups[1].Value
    if (-not $keys.ContainsKey($key)) { throw "Reference to unknown Flick token: $key" }
}

$project = Get-Content -LiteralPath (Join-Path $root "src/LocalClip.App/LocalClip.App.csproj") -Raw
if ($project -notmatch 'Resource Include="\$\(GeneratedFlickLogo\)"' -or
    $project -notmatch '<LogicalName>Assets/Brand/FlickLogo.png</LogicalName>' -or
    $project -notmatch 'GenerateFlickIcon') {
    throw "The SVG-derived circular PNG must be generated into obj/ and embedded as a WPF Resource."
}
$svgPath = Join-Path $root "src/LocalClip.App/Assets/Brand/FlickMark.svg"
if (-not (Test-Path -LiteralPath $svgPath -PathType Leaf)) { throw "Canonical Flick circular SVG missing" }
[xml]$svg = Get-Content -LiteralPath $svgPath -Raw
$namespace = [Xml.XmlNamespaceManager]::new($svg.NameTable)
$namespace.AddNamespace("s", "http://www.w3.org/2000/svg")
$circle = $svg.SelectSingleNode("//s:circle[@id='rim']", $namespace)
$face = $svg.SelectSingleNode("//s:circle[@id='face']", $namespace)
$stripes = @($svg.SelectNodes("//s:g[@id='stripes']/s:rect", $namespace))
$stops = @($svg.SelectNodes("//s:linearGradient[@id='sunset']/s:stop", $namespace))
$letter = $svg.SelectSingleNode("//s:polygon[@id='letter']", $namespace)
if ($svg.DocumentElement.GetAttribute('viewBox') -cne '0 0 128 128' -or
    $null -eq $circle -or $null -eq $face -or
    $circle.GetAttribute('r') -cne '62' -or $face.GetAttribute('r') -cne '58.5' -or
    $stripes.Count -ne 7 -or $stops.Count -lt 5 -or $null -eq $letter) {
    throw "Flick mark must be a full circular gradient with even horizontal stripes and an angular F."
}
$assetBuilder = Get-Content -LiteralPath (Join-Path $root 'scripts/build_flick_icon.ps1') -Raw
if ($assetBuilder -notmatch 'InputSvg' -or $assetBuilder -notmatch 'OutputPng' -or
    $assetBuilder -notmatch 'OutputIco' -or $assetBuilder -notmatch 'Format32bppArgb') {
    throw "App and installer must be generated from the same SVG, with transparent corners."
}
$typography = Get-Content -LiteralPath (Join-Path $themeRoot 'Flick.Typography.xaml') -Raw
if ($typography -notmatch 'Rect="0,0,38,38"' -or $typography -match 'Rect="1,1,38,38"') {
    throw "All app/notification logo images must be centered, without the old offset."
}

$toast = Get-Content -LiteralPath (Join-Path $root "src/LocalClip.App/Views/Components/FlickClipToast.xaml") -Raw
if ($toast -match '<Ellipse|<Ellipse\s' -or
    $toast -notmatch 'Flick.Image.BrandMark') {
    throw "Toast must reuse the new circular logo without adding a second round badge."
}
if ($toast -match 'GradientStop|LinearGradientBrush') {
    throw "Toast chrome must not add a full-width decorative gradient separator."
}
$preview = Get-Content -LiteralPath (Join-Path $root "src/LocalClip.App/Views/DesignSystemPreviewWindow.xaml") -Raw
if ($preview -notmatch '<components:FlickClipToast' -or
    $preview -notmatch 'Flick.Input.Combo') {
    throw "Design preview must exercise branded toast and reusable controls."
}
Write-Host "Flick GUI resources validated: $($keys.Count) tokens, canonical circular SVG, shared generated logo, toast and preview." -ForegroundColor Green
