# Source-only packaging/performance gate; real Windows RAM and installer QA are separate.
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
function Read([string]$p) { Get-Content -LiteralPath (Join-Path $root $p) -Raw }
function Require([string]$body, [string]$pattern, [string]$why) {
    if ($body -notmatch $pattern) { throw "Flick release source contract FAILED: $why" }
}
$media = Read "src/LocalClip.Core/Media/MediaProber.cs"
$xaml = Read "src/LocalClip.App/MainWindow.xaml"
$view = Read "src/LocalClip.App/MainWindow.xaml.cs"
$decode = Read "src/LocalClip.App/Services/ThumbnailImageConverter.cs"
$monitor = Read "src/LocalClip.Core/Diagnostics/ResourceMonitor.cs"
$package = Read "scripts/build_package.ps1"
$csproj = Read "src/LocalClip.App/LocalClip.App.csproj"
$install = Read "installer/Flick.iss"
$start = Read "src/LocalClip.App/Services/WindowsStartupRegistration.cs"
Require $media 'force_original_aspect_ratio=decrease' "FFmpeg thumbnails must be bounded"
Require $decode 'DecodePixelWidth\s*=\s*256' "Legacy thumbnails must decode to display size"
Require $xaml 'Flick.ThumbnailDecoder' "Row and poster need bounded decoding"
Require $xaml 'Delay=180' "Search should debounce"
Require $view 'MediaElement.Source opens a decoder' "Editor must defer media until playback"
Require $view 'PlayerPoster.Visibility\s*=\s*Visibility.Visible' "Editor needs its poster"
Require $view 'ReleasePosterWhileHidden' "Tray/minimize should release decoded poster resources"
Require $monitor 'using var current' "Transient process handles must be disposed"
Require $package 'Flick.ico' "Portable packaging must include brand icon"
Require $package 'LICENSE-NOTICE.md' "Redistributable needs its original-source licensing notice"
Require $csproj 'AssemblyName>Flick<' "Branded apphost must be Flick.exe"
Require $install 'PrivilegesRequired=lowest' "Installer must be per user"
Require $install 'DefaultDirName=\{localappdata\}\\Programs\\Flick' "Program and user metadata must be separate"
Require $start 'Environment.ProcessPath' "Startup must use the running executable path"
$visual = Read "scripts/capture_flick_visual_evidence.ps1"
$performance = Read "scripts/sample_flick_performance.ps1"
Require $visual 'Flick", "LocalClip.App' "Screenshot QA must accept branded Flick.exe"
Require $performance 'Flick", "LocalClip.App' "Performance QA must accept branded Flick.exe"
if ($install -match '(?im)^\s*UninstallDelete|deleteall') {
    throw "Do not broadly delete user files on uninstall"
}
Write-Host "Flick packaging and memory source contracts PASS (not a hardware performance test)." -ForegroundColor Green
