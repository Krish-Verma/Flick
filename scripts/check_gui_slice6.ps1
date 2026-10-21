# Pure source/design QA. No window/media/GPU result is fabricated by this gate.
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
function ReadSource([string]$relative) {
    $path = Join-Path $root $relative
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required Slice 6 file: $relative" }
    return Get-Content -LiteralPath $path -Raw
}
function Require([string]$actual, [string]$pattern, [string]$reason) {
    if ($actual -notmatch $pattern) { throw "Slice 6 contract FAILED: $reason" }
}
$main = ReadSource "src/LocalClip.App/MainWindow.xaml"
$code = ReadSource "src/LocalClip.App/MainWindow.xaml.cs"
$policy = ReadSource "src/LocalClip.Core/Rules/WorkspaceLayoutRules.cs"
$accessibility = ReadSource "src/LocalClip.App/Services/FlickVisualPreferences.cs"
$toast = ReadSource "src/LocalClip.App/Services/FlickNotificationService.cs"
$settings = ReadSource "src/LocalClip.App/Views/SettingsWindow.xaml"
$folders = ReadSource "src/LocalClip.App/Views/ConnectedFoldersWindow.xaml"
$tests = ReadSource "tests/LocalClip.Core.Tests/WorkspaceLayoutRulesTests.cs"
$settingsModel = ReadSource "src/LocalClip.Core/Models/Settings.cs"
$settingsDialogCode = ReadSource "src/LocalClip.App/Views/SettingsWindow.xaml.cs"

# Every XAML involved must at minimum parse as well-formed XML.
foreach ($relative in @(
    "src/LocalClip.App/MainWindow.xaml",
    "src/LocalClip.App/Views/SettingsWindow.xaml",
    "src/LocalClip.App/Views/ConnectedFoldersWindow.xaml",
    "src/LocalClip.App/Views/Components/FlickClipToast.xaml",
    "src/LocalClip.App/Views/Components/TrimRangeControl.xaml",
    "src/LocalClip.App/Resources/Theme/Flick.Colors.xaml",
    "src/LocalClip.App/Resources/Theme/Flick.Controls.xaml"
)) {
    [xml]$parsed = ReadSource $relative
    if (-not $parsed.DocumentElement) { throw "XAML XML parsing FAILED: $relative" }
}

Require $main 'MinWidth="720" MinHeight="480"' "Compact window minimum not applied"
Require $main 'ClipLibraryColumn' "Adaptive library column missing"
Require $main 'ClipEditorColumn' "Adaptive editor column missing"
Require $main 'BtnBackToLibrary' "Compact mode needs a return-to-library control"
Require $main 'ClipLibraryPanel' "Cannot toggle clip library visibility safely"
Require $main 'ClipEditorPanel' "Cannot toggle clip editor visibility safely"
Require $main 'Flick\.Text\.Base' "Legacy implicit text styles must use shared design tokens"
Require $main 'Flick\.Input\.Text' "Input template must use shared design system"
Require $main 'Flick\.Button\.Secondary' "Legacy buttons must use shared controls"
Require $main 'Flick\.VideoAspectHeight' "Responsive video aspect ratio lost"
Require $main 'StatusBadge.*' "Capture status must stay visible"
if ($main -match 'Background="#(?:30283B|11121D|06060B)"' -or
    $code -match 'StatusBadge\.Background\s*=\s*new SolidColorBrush') {
    throw "Static opaque surfaces bypass the Windows high-contrast token override."
}
Require $main 'BtnSaveReplay' "Capture Save action hidden by layout"
Require $main 'DragHandle_PreviewMouseMove' "Validated native Discord drag removed"
Require $code 'WorkspaceLayoutRules\.Classify' "Width layout rule must be used"
Require $code 'ShowCompactLibrary' "Back to library navigation is not connected"
Require $code 'ShowCompactEditor' "Selecting a clip must reach compact editor"
Require $code 'if \(next == _layoutMode\) return' "Resize must not churn layout for every pixel"
Require $code 'UpdateStatusBadgeColor' "Capture must remain accessible in both modes"
Require $policy 'CompactBreakpointDips = 1120' "Tested breakpoint contract is not in place"
Require $tests 'WorkspaceModeFollowsLogicalSize' "Breakpoint regression tests are missing"
Require $accessibility 'SystemParameters\.HighContrast' "Windows high contrast is not honored"
Require $accessibility 'SystemParameters\.ClientAreaAnimation' "Reduced motion is not honored"
Require $accessibility 'EnableTransparency' "Windows transparency option is ignored"
Require $toast 'FlickVisualPreferences\.ShouldAnimate' "Overlay animation must respect Windows setting"
Require $settings 'PanelRecording' "Recording settings vanished"
Require $settings 'PanelAudio' "Audio settings vanished"
Require $settings 'PanelShortcuts' "Shortcut settings vanished"
Require $settings 'PanelStorage' "Storage settings vanished"
Require $settings 'PanelGeneral' "General settings vanished"
Require $settings 'PanelSharing' "Sharing settings vanished"
Require $folders 'Connected Clip Folders' "Linked-folder management missing"
Require $settingsModel 'RequiresExplicitSettingsSave' "Recovered settings must block incidental disk writes"
Require $settingsDialogCode 'MarkExplicitSettingsSaveCompleted' "Only explicit Settings Save may accept recovery"
$targetRules = ReadSource "src/LocalClip.Core/Rules/CaptureTargetSelectionRules.cs"
$mainVm = ReadSource "src/LocalClip.App/ViewModels/MainViewModel.cs"
Require $targetRules 'WindowExecutable' "Saved window identity must not degrade to desktop capture"
Require $mainVm 'MatchesSavedPreference' "Startup must restore source by safe identity, not first target"
Require $mainVm '!Targets.Contains\(SelectedTarget\)' "Starting capture with a stale source must be refused"

# Contrast ratios measured directly from final named color tokens.
[xml]$theme = ReadSource "src/LocalClip.App/Resources/Theme/Flick.Colors.xaml"
$xmlns = "http://schemas.microsoft.com/winfx/2006/xaml"
$colors = @{}
foreach ($node in $theme.DocumentElement.ChildNodes) {
    if ($node.LocalName -ne "Color") { continue }
    $key = $node.GetAttribute("Key", $xmlns)
    if ($key -and $node.InnerText -match '^#(?:FF)?[0-9A-Fa-f]{6}$') { $colors[$key] = $node.InnerText }
}
function ToLinear([double]$v) {
    if ($v -le 0.04045) { return $v / 12.92 }
    return [Math]::Pow(($v + 0.055) / 1.055, 2.4)
}
function Lum([string]$hex) {
    if ($hex.Length -eq 9) { $hex = "#" + $hex.Substring(3) }
    $r = ToLinear ([Convert]::ToInt32($hex.Substring(1, 2), 16) / 255.0)
    $g = ToLinear ([Convert]::ToInt32($hex.Substring(3, 2), 16) / 255.0)
    $b = ToLinear ([Convert]::ToInt32($hex.Substring(5, 2), 16) / 255.0)
    return 0.2126 * $r + 0.7152 * $g + 0.0722 * $b
}
function Contrast([string]$a, [string]$b) {
    $first = Lum $a
    $second = Lum $b
    if ($first -lt $second) { $swap = $first; $first = $second; $second = $swap }
    return ($first + 0.05) / ($second + 0.05)
}
$required = @(
    @("Flick.Color.TextPrimary", "Flick.Color.Canvas"),
    @("Flick.Color.TextPrimary", "Flick.Color.Surface"),
    @("Flick.Color.TextSecondary", "Flick.Color.Surface"),
    @("Flick.Color.TextMuted", "Flick.Color.Raised"),
    @("Flick.Color.OnAccent", "Flick.Color.Accent")
)
foreach ($pair in $required) {
    $nameA = $pair[0]; $nameB = $pair[1]
    if (!$colors.ContainsKey($nameA) -or !$colors.ContainsKey($nameB)) {
        throw "Missing a design token: $nameA / $nameB"
    }
    $ratio = Contrast $colors[$nameA] $colors[$nameB]
    if ($ratio -lt 4.5) {
        throw "Insufficient text contrast for $nameA against $nameB : $([Math]::Round($ratio,2)):1"
    }
}
Write-Host "Flick GUI Slice 6 source/layout/contrast gates PASS — 5 text/background pairs >= 4.5:1." -ForegroundColor Green
Write-Host "Windows screenshots, actual focus order, FPS, GPU and game overlay are NOT tested here."
