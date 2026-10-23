# R2 source contracts; only actual Windows QA can validate visuals and SAC.
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
function Read([string]$path) { Get-Content -LiteralPath (Join-Path $root $path) -Raw }
function Require([string]$source,[string]$pattern,[string]$why) {
    if ($source -notmatch $pattern) { throw "Flick R2 source gate FAILED: $why" }
}
$service = Read "src/LocalClip.App/Services/FlickBackdropService.cs"
$main = Read "src/LocalClip.App/MainWindow.xaml"
$mainCode = Read "src/LocalClip.App/MainWindow.xaml.cs"
$typ = Read "src/LocalClip.App/Resources/Theme/Flick.Typography.xaml"
$settings = Read "src/LocalClip.App/Views/SettingsWindow.xaml"
$toast = Read "src/LocalClip.App/Views/Components/FlickClipToast.xaml"
$app = Read "src/LocalClip.App/App.xaml"
$chrome = Read "src/LocalClip.App/Resources/Theme/Flick.Chrome.xaml"
$controls = Read "src/LocalClip.App/Resources/Theme/Flick.Controls.xaml"
$master = Read "docs/GUI-REFRESH-2026-MASTER-PLAN.md"

foreach ($path in @(
    "src/LocalClip.App/App.xaml",
    "src/LocalClip.App/Resources/Theme/Flick.Chrome.xaml",
    "src/LocalClip.App/Resources/Theme/Flick.Controls.xaml",
    "src/LocalClip.App/Resources/Theme/Flick.Typography.xaml",
    "src/LocalClip.App/Views/SettingsWindow.xaml",
    "src/LocalClip.App/MainWindow.xaml"
)) {
    [xml]$doc = Read $path
    if (-not $doc.DocumentElement) { throw "Invalid WPF XML $path" }
}
Require $service 'TryApplyDarkTitleBar' "Native caption theme must exist"
Require $service 'DwmwaUseImmersiveDarkMode\s*=\s*20' "Dark caption attribute missing"
Require $service 'DwmwaCaptionColor\s*=\s*35' "Caption background attribute missing"
Require $service 'DwmwaTextColor\s*=\s*36' "Caption foreground attribute missing"
Require $service 'SystemParameters.HighContrast' "Windows high contrast must remain OS-owned"
Require $mainCode 'FlickBackdropService.TryApplyDarkTitleBar\(this\)' "Application must call caption style on SourceInitialized"
if ($main -match 'WindowStyle="None"' -or $main -match 'AllowsTransparency="True"' -or
    $mainCode -match 'DragMove\(\)' -or $mainCode -match 'SetWindowLong\(') {
    throw "R2 must preserve real Windows minimize/restore/resize/Snap"
}
Require $typ 'Flick.Image.BrandMark' "Original logo token must persist"
Require $typ '<EllipseGeometry\s+Center="19,19"\s+RadiusX="19"\s+RadiusY="19"' "Circular logo clip missing"
Require $typ '<Setter Property="Stretch" Value="Uniform"' "Logo image must not distort"
Require $settings 'Image Width="38" Height="38"' "Logo widths must align across app and settings"
Require $toast 'Flick.Image.BrandMark' "Toast original brand must remain"
Require $app 'Flick.Chrome.xaml' "Global chrome resource dictionary missing"
Require $chrome 'Flick.Scrollbar.Thumb.Vertical' "Vertical scroll thumb missing"
Require $chrome 'Flick.Scrollbar.Thumb.Horizontal' "Horizontal scroll thumb missing"
Require $chrome 'PART_Track' "WPF scrolling native Track missing"
Require $chrome 'PageUpCommand' "PageUp behavior missing"
Require $chrome 'PageDownCommand' "PageDown behavior missing"
Require $chrome 'PageLeftCommand' "PageLeft behavior missing"
Require $chrome 'PageRightCommand' "PageRight behavior missing"
Require $chrome 'TargetType="\{x:Type ContextMenu\}"' "Themed popup menu template missing"
Require $chrome 'TargetType="\{x:Type MenuItem\}"' "Themed menu item missing"
Require $chrome 'IsKeyboardFocusWithin' "Keyboard focus indicator missing"
Require $controls 'VirtualizingStackPanel.IsVirtualizing' "Clip virtualization lost"
Require $master 'R8.*Clip Saved' "Separate R8 notification slice missing"
Require $master '30 sec' "Clip Saved actual-duration subtitle acceptance missing"
Write-Host "Flick GUI Refresh R2 source contracts PASS. Real Windows title/menu/DPI verification remains required." -ForegroundColor Green
