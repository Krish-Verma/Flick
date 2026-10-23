# Slice 2 contract audit. This is NOT a substitute for WPF window or GPU testing.
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
function ReadRepo([string]$path) {
    $file = Join-Path $root $path
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing $path" }
    return Get-Content -LiteralPath $file -Raw
}
function Require([string]$text, [string]$pattern, [string]$reason) {
    if ($text -notmatch $pattern) { throw "Slice 2 contract failed: $reason" }
}

$settings = ReadRepo "src/LocalClip.Core/Models/Settings.cs"
Require $settings 'ShowClipSavedOverlay\s*\{\s*get;\s*set;\s*\}\s*=\s*true;' "Overlay must be enabled by default"
Require $settings 'PlayClipSavedSound\s*\{\s*get;\s*set;\s*\}\s*=\s*true;' "Sound must be enabled by default"

$vm = ReadRepo "src/LocalClip.App/ViewModels/MainViewModel.cs"
Require $vm 'ClipSavedConfirmed\?\.Invoke\(clip\)' "Confirmed clip event missing"
Require $vm 'ReplaySaveFailed\?\.Invoke' "Save failure event missing"
$registeredAt = $vm.IndexOf("await _storage.RegisterNewOriginalAsync")
$confirmedAt = $vm.IndexOf("ClipSavedConfirmed?.Invoke")
if ($registeredAt -lt 0 -or $confirmedAt -lt $registeredAt) {
    throw "Success overlay is not linked to validated/registered media"
}

$main = ReadRepo "src/LocalClip.App/MainWindow.xaml"
foreach ($control in @("BtnNavClips", "BtnNavSettings", "CaptureStateDot", "StatusBadge",
                       "CmbTargets", "BtnToggleCapture", "BtnSaveReplay", "TxtClipSearch")) {
    Require $main ('x:Name="' + $control + '"') "Shell control missing: $control"
}
Require $main 'Flick\.Image\.BrandMark' "Navigation must use embedded full-squircle logo"
Require $main 'Save Last 30 Seconds' "Primary capture action must use Title Case"
Require $main 'Flick\.Brush\.Frosted' "Frosted capture bar must use a shared token"

$code = ReadRepo "src/LocalClip.App/MainWindow.xaml.cs"
Require $code 'ClipSavedConfirmed\s*\+=' "Shell does not listen for confirmed successful saves"
Require $code 'ReplaySaveFailed\s*\+=' "Shell does not surface save failures"
Require $code 'ConfirmTargetReset\s*=' "Source change lacks buffer-reset confirmation"
Require $code 'PreviewNotificationRequested\s*\+=' "Settings preview not connected"
if ($code -match 'CaptureHost\.ReplaySaved\s*\+=') {
    throw "Showing success directly from native callback would bypass index verification"
}

$toast = ReadRepo "src/LocalClip.App/Views/Components/FlickClipToast.xaml"
Require $toast 'Flick.Image.BrandMark' "Toast must display the actual full-squircle logo"
if ($toast -match 'LinearGradientBrush|<Ellipse') {
    throw "Toast must not add a circular badge or unnecessary gradient divider"
}
$overlay = ReadRepo "src/LocalClip.App/Views/ToastOverlayWindow.xaml"
foreach ($rule in @('ShowActivated="False"', 'ShowInTaskbar="False"', 'IsHitTestVisible="False"')) {
    if (!$overlay.Contains($rule)) { throw "Focus-safe overlay rule missing: $rule" }
}
$service = ReadRepo "src/LocalClip.App/Services/FlickNotificationService.cs"
Require $service 'TimeSpan\.FromSeconds\(2\)' "Overlay must disappear quickly"
Require $service '_window\.Close\(' "Overlay must be closed, not rendered indefinitely"
Require $service 'if \(playSound\) PlayChime\(' "Sound preference is not independent"
$settingsView = ReadRepo "src/LocalClip.App/Views/SettingsWindow.xaml"
foreach ($name in @("ChkClipSavedOverlay", "ChkClipSavedSound")) {
    Require $settingsView ('x:Name="' + $name + '"') "Missing Settings toggle: $name"
}
Write-Host "Flick GUI Slice 2 structural invariants PASS (hardware/runtime acceptance still pending)." -ForegroundColor Green

Require $service 'NotificationWaveRules.DurationText\(durationSeconds\)' "Save detail must be actual duration only"
if ($service -match 'clipTitle|Last Round Clutch') { throw "Save notification must not accept clip titles" }
