# Flick design-foundation static checks. This does NOT claim visual/runtime QA.
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
$themeRoot = Join-Path $root "src/LocalClip.App/Resources/Theme"
$themeNames = @(
    "Flick.Colors.xaml",
    "Flick.Metrics.xaml",
    "Flick.Typography.xaml",
    "Flick.Controls.xaml"
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
    "Flick.Input.Slider", "Flick.Surface.Frosted", "Flick.Surface.Toast"
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

$logo = Join-Path $root "src/LocalClip.App/Assets/Brand/FlickLogo.png"
if (-not (Test-Path -LiteralPath $logo -PathType Leaf)) { throw "Flick full-logo image missing" }
$bytes = [System.IO.File]::ReadAllBytes($logo)
if ($bytes.Length -lt 1000 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80 -or
    $bytes[2] -ne 78 -or $bytes[3] -ne 71) {
    throw "Flick branding image is not a valid PNG asset"
}

$toast = Get-Content -LiteralPath (Join-Path $root "src/LocalClip.App/Views/Components/FlickClipToast.xaml") -Raw
if ($toast -match '<Ellipse|<Ellipse\s' -or
    $toast -notmatch 'Flick.Image.BrandMark') {
    throw "Toast must use the whole original squircle logo; no nested circular badge."
}
if ($toast -match 'GradientStop|LinearGradientBrush') {
    throw "Toast chrome must not add a full-width decorative gradient separator."
}
$preview = Get-Content -LiteralPath (Join-Path $root "src/LocalClip.App/Views/DesignSystemPreviewWindow.xaml") -Raw
if ($preview -notmatch '<components:FlickClipToast' -or
    $preview -notmatch 'Flick.Input.Combo') {
    throw "Design preview must exercise branded toast and reusable controls."
}
Write-Host "Flick GUI resources validated: $($keys.Count) unique tokens, full-logo asset, reusable toast and preview." -ForegroundColor Green
