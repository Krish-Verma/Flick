# Build and Package Script for LocalClip (Windows x64)
# Enforces clean reproducible build and packaging for Milestone M4

$ErrorActionPreference = "Stop"

$RootDir = (Get-Item $PSScriptRoot).Parent.FullName
$DistDir = Join-Path $RootDir "dist"
$PackageName = "LocalClip-v1.0.0-win-x64"
$PackageDir = Join-Path $DistDir $PackageName
$ZipPath = Join-Path $DistDir "$PackageName.zip"

Write-Host "=== Step 1: Cleaning previous build and dist artifacts ===" -ForegroundColor Cyan
if (Test-Path $PackageDir) { Remove-Item -Recurse -Force $PackageDir }
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null

$env:PATH = "C:\Users\krish\AppData\Local\Microsoft\dotnet;C:\Users\krish\AppData\Local\Microsoft\WinGet\Packages\BrechtSanders.WinLibs.POSIX.UCRT_Microsoft.Winget.Source_8wekyb3d8bbwe\mingw64\bin;$env:PATH"

Write-Host "=== Step 2: Compiling Native CaptureHost (C++ MinGW) ===" -ForegroundColor Cyan
$HostSrc = Join-Path $RootDir "src\LocalClip.CaptureHost\main.cpp"
$HostOut = Join-Path $RootDir "tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe"
$ObsInc = Join-Path $RootDir "tools\obs\include\libobs"
$HostInc = Join-Path $RootDir "src\LocalClip.CaptureHost"
$ObsBin = Join-Path $RootDir "tools\obs\runtime\bin\64bit"

g++ -O2 -std=c++17 -static-libgcc -static-libstdc++ `
    -I "$ObsInc" -I "$HostInc" "$HostSrc" `
    -L "$ObsBin" "-Wl,-Bstatic" -lwinpthread "-Wl,-Bdynamic" -lobs -ldwmapi `
    -o "$HostOut"

if ($LASTEXITCODE -ne 0) {
    throw "Failed to compile LocalClip.CaptureHost.exe"
}
Write-Host "CaptureHost compiled successfully to: $HostOut" -ForegroundColor Green

Write-Host "=== Step 3: Publishing WPF Application (Release win-x64) ===" -ForegroundColor Cyan
$AppCsproj = Join-Path $RootDir "src\LocalClip.App\LocalClip.App.csproj"
dotnet publish "$AppCsproj" -c Release -r win-x64 -o (Join-Path $DistDir "app") --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Dotnet publish failed"
}

Write-Host "=== Step 4: Assembling Portable Package ===" -ForegroundColor Cyan
# Copy Application Binaries
Copy-Item -Path (Join-Path $DistDir "app\*") -Destination $PackageDir -Recurse -Force

# Create Tools directory structure
$ToolsDest = Join-Path $PackageDir "tools"
New-Item -ItemType Directory -Path (Join-Path $ToolsDest "ffmpeg") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ToolsDest "obs") -Force | Out-Null

# Copy FFmpeg tools
Copy-Item (Join-Path $RootDir "tools\ffmpeg\ffmpeg.exe") (Join-Path $ToolsDest "ffmpeg\") -Force
Copy-Item (Join-Path $RootDir "tools\ffmpeg\ffprobe.exe") (Join-Path $ToolsDest "ffmpeg\") -Force

# Copy OBS runtime
Copy-Item (Join-Path $RootDir "tools\obs\runtime") (Join-Path $ToolsDest "obs\") -Recurse -Force

# Copy Licenses & Legal Notices
Copy-Item (Join-Path $RootDir "LICENSE") $PackageDir -Force
Copy-Item (Join-Path $RootDir "THIRD-PARTY-NOTICES.md") $PackageDir -Force

# Generate Installer / Uninstaller PowerShell Scripts
$InstallScript = @"
# LocalClip Per-User Installer Script
`$ErrorActionPreference = "Stop"
`$TargetDir = Join-Path `$env:LOCALAPPDATA "LocalClip"
Write-Host "Installing LocalClip to: `$TargetDir" -ForegroundColor Cyan

if (!(Test-Path `$TargetDir)) {
    New-Item -ItemType Directory -Path `$TargetDir -Force | Out-Null
}

`$SrcDir = `$PSScriptRoot
Copy-Item -Path (Join-Path `$SrcDir "*") -Destination `$TargetDir -Recurse -Force

# Create Desktop Shortcut
`$WshShell = New-Object -ComObject WScript.Shell
`$Desktop = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
`$Shortcut = `$WshShell.CreateShortcut((Join-Path `$Desktop "LocalClip.lnk"))
`$Shortcut.TargetPath = Join-Path `$TargetDir "LocalClip.App.exe"
`$Shortcut.WorkingDirectory = `$TargetDir
`$Shortcut.Description = "LocalClip - Fast, Private Gameplay Clipping"
`$Shortcut.Save()

# Create Start Menu Shortcut
`$StartMenu = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::StartMenu)
`$Programs = Join-Path `$StartMenu "Programs"
`$AppShortcut = `$WshShell.CreateShortcut((Join-Path `$Programs "LocalClip.lnk"))
`$AppShortcut.TargetPath = Join-Path `$TargetDir "LocalClip.App.exe"
`$AppShortcut.WorkingDirectory = `$TargetDir
`$AppShortcut.Description = "LocalClip - Fast, Private Gameplay Clipping"
`$AppShortcut.Save()

Write-Host "LocalClip installed successfully!" -ForegroundColor Green
Write-Host "Shortcuts created on Desktop and Start Menu." -ForegroundColor Green
"@
$InstallScript | Set-Content (Join-Path $PackageDir "install.ps1") -Encoding utf8

$UninstallScript = @"
# LocalClip Uninstaller Script
`$ErrorActionPreference = "SilentlyContinue"
Write-Host "Uninstalling LocalClip..." -ForegroundColor Cyan

# Stop running instances
Get-Process -Name "LocalClip.App", "LocalClip.CaptureHost" -ErrorAction SilentlyContinue | Stop-Process -Force

# Remove Desktop Shortcut
`$Desktop = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
Remove-Item (Join-Path `$Desktop "LocalClip.lnk") -Force

# Remove Start Menu Shortcut
`$StartMenu = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::StartMenu)
`$Programs = Join-Path `$StartMenu "Programs"
Remove-Item (Join-Path `$Programs "LocalClip.lnk") -Force

# Remove Installation Directory
`$TargetDir = Join-Path `$env:LOCALAPPDATA "LocalClip"
if (Test-Path `$TargetDir) {
    Remove-Item -Recurse -Force `$TargetDir
}

Write-Host "LocalClip uninstalled successfully." -ForegroundColor Green
"@
$UninstallScript | Set-Content (Join-Path $PackageDir "uninstall.ps1") -Encoding utf8

# Generate User Readme
$Readme = @"
# LocalClip v1.0.0 (Windows x64)

Fast, private, offline-first gameplay clipper replicating the SteelSeries Moments workflow.

## Key Features
- **100% Free & Offline Core:** Zero accounts, zero ads, zero telemetry, zero watermarks.
- **Hardware-Accelerated:** NVIDIA NVENC CBR 15 Mbps @ 1080p30 (RTX 50-series / 40-series / 30-series / 20-series / 16-series).
- **SteelSeries Moments Workflow:**
  - Starts **DISARMED** by default.
  - Press **Ctrl+Shift+F9** to Arm/Disarm capture.
  - Press **Ctrl+Shift+F8** to Save preceding 30s replay to immutable MKV.
  - Review in built-in gallery, non-destructive trim, audio track mute/gain adjustment.
  - **Export for Discord (20 MB / 50 MB / 100 MB):** Watermark-free H.264/AAC MP4 strictly within budget.
  - **Direct Drag & Paste:** Native drag handle or "Copy File" button directly drops into real Discord desktop client.
  - **Single Instance & Leak-Free:** Subprocesses bounded by Windows Job Object (`KILL_ON_JOB_CLOSE`).

## Quick Start
1. Run ``LocalClip.App.exe`` directly (portable mode).
2. Or run ``powershell -ExecutionPolicy Bypass -File .\install.ps1`` for per-user installation with Desktop/Start shortcuts.

## Documentation
- See ``docs/USER-GUIDE.md`` for complete usage and shortcut customization.
- See ``docs/ARCHITECTURE.md`` for technical specifications and IPC protocol details.
"@
$Readme | Set-Content (Join-Path $PackageDir "README.md") -Encoding utf8

Write-Host "=== Step 5: Creating Portable ZIP Archive ===" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $PackageDir "*") -DestinationPath $ZipPath -CompressionLevel Optimal -Force
Write-Host "Portable ZIP created at: $ZipPath" -ForegroundColor Green

Write-Host "=== Step 6: Calculating SHA-256 Checksums ===" -ForegroundColor Cyan
$ZipHash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash
$ExeHash = (Get-FileHash -Path (Join-Path $PackageDir "LocalClip.App.exe") -Algorithm SHA256).Hash
$HostHash = (Get-FileHash -Path (Join-Path $PackageDir "tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe") -Algorithm SHA256).Hash

$ChecksumContent = @"
# LocalClip v1.0.0 SHA-256 Checksums
$ZipHash  LocalClip-v1.0.0-win-x64.zip
$ExeHash  LocalClip.App.exe
$HostHash  LocalClip.CaptureHost.exe
"@
$ChecksumContent | Set-Content (Join-Path $DistDir "SHA256SUMS.txt") -Encoding utf8
Write-Host "Checksums written to: $(Join-Path $DistDir 'SHA256SUMS.txt')" -ForegroundColor Green
Write-Host "`nAll build and packaging steps completed successfully!" -ForegroundColor Green
