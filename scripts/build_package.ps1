# Build and Package Script for LocalClip (Windows x64)
# The CI-only -SkipNativeBuild switch requires a previously compiled owned host.
param([switch]$SkipNativeBuild)

$ErrorActionPreference = "Stop"

$RootDir = (Get-Item $PSScriptRoot).Parent.FullName
$DistDir = Join-Path $RootDir "dist"
$PackageName = "Flick-v1.0.0-win-x64"
$PackageDir = Join-Path $DistDir $PackageName
$ZipPath = Join-Path $DistDir "$PackageName.zip"

Write-Host "=== Step 1: Cleaning previous build and dist artifacts ===" -ForegroundColor Cyan
if (Test-Path $PackageDir) { Remove-Item -Recurse -Force $PackageDir }
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet SDK not found on PATH" }
if (-not $SkipNativeBuild -and -not (Get-Command g++ -ErrorAction SilentlyContinue)) {
    throw "MinGW-w64 g++ not found on PATH"
}
if (-not (Test-Path (Join-Path $RootDir "tools\\obs\\runtime\\bin\\64bit\\obs.dll"))) { throw "Missing pinned OBS runtime. Run scripts/setup_tools.ps1." }
if (-not (Test-Path (Join-Path $RootDir "tools\\ffmpeg\\ffmpeg.exe"))) { throw "Missing FFmpeg runtime. Run scripts/setup_tools.ps1." }

Write-Host "=== Step 2: Compiling Native CaptureHost (C++ MinGW) ===" -ForegroundColor Cyan
$HostSrc = Join-Path $RootDir "src\LocalClip.CaptureHost\main.cpp"
$HostOut = Join-Path $RootDir "tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe"
$ObsInc = Join-Path $RootDir "tools\obs\include\libobs"
$HostInc = Join-Path $RootDir "src\LocalClip.CaptureHost"
$ObsBin = Join-Path $RootDir "tools\obs\runtime\bin\64bit"

if (-not $SkipNativeBuild) {
    g++ -O2 -std=c++17 -static-libgcc -static-libstdc++ `
        -I "$ObsInc" -I "$HostInc" "$HostSrc" `
        -L "$ObsBin" "-Wl,-Bstatic" -lwinpthread "-Wl,-Bdynamic" -lobs -ldwmapi `
        -o "$HostOut"
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile LocalClip.CaptureHost.exe" }
    Write-Host "CaptureHost compiled successfully to: $HostOut" -ForegroundColor Green
}
else {
    if (-not (Test-Path -LiteralPath $HostOut -PathType Leaf)) {
        throw "CI requested -SkipNativeBuild but verified host binary is missing: $HostOut"
    }
    Write-Host "Using capture host prebuilt and verified by dedicated MSYS2 compilation step." -ForegroundColor Yellow
}

Write-Host "=== Step 3: Publishing WPF Application (Release win-x64) ===" -ForegroundColor Cyan
$AppCsproj = Join-Path $RootDir "src\LocalClip.App\LocalClip.App.csproj"
dotnet publish "$AppCsproj" -c Release -r win-x64 --self-contained true -o (Join-Path $DistDir "app") --nologo

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
Copy-Item (Join-Path $RootDir "LICENSE-NOTICE.md") $PackageDir -Force
Copy-Item (Join-Path $RootDir "THIRD-PARTY-NOTICES.md") $PackageDir -Force
Copy-Item (Join-Path $RootDir "docs") (Join-Path $PackageDir "docs") -Recurse -Force

# The former installer copied binaries into %LOCALAPPDATA%\LocalClip and
# its uninstaller deleted that whole directory (including user metadata).
# Do not embed those unsafe scripts. Use the conventional Inno Setup wizard
# via scripts/build_installer.ps1; this portable ZIP also runs without setup.
$BrandIco = Join-Path $RootDir "src\LocalClip.App\Assets\Brand\Flick.ico"
if (-not (Test-Path -LiteralPath $BrandIco -PathType Leaf)) {
    throw "Flick branded icon missing after WPF publish"
}
Copy-Item -LiteralPath $BrandIco -Destination (Join-Path $DistDir "Flick.ico") -Force
if (-not (Test-Path -LiteralPath (Join-Path $PackageDir "Flick.exe"))) {
    throw "Expected Flick.exe not found in published portable package"
}

# Generate User Readme
$Readme = @"
# Flick v1.0.0 Preview (Windows x64)

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
1. Run ``Flick.exe`` directly (portable mode).
2. To install with Start Menu, Installed Apps and optional desktop shortcut, use the Flick Setup EXE built by scripts/build_installer.ps1.
3. Your settings and library index remain in LocalAppData/LocalClip for backwards compatibility; do not run an older LocalClip uninstall.ps1.

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
$ExeHash = (Get-FileHash -Path (Join-Path $PackageDir "Flick.exe") -Algorithm SHA256).Hash
$HostHash = (Get-FileHash -Path (Join-Path $PackageDir "tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe") -Algorithm SHA256).Hash

$ChecksumContent = @"
# Flick v1.0.0 Preview SHA-256 Checksums
$ZipHash  Flick-v1.0.0-win-x64.zip
$ExeHash  Flick.exe
$HostHash  LocalClip.CaptureHost.exe
"@
$ChecksumContent | Set-Content (Join-Path $DistDir "SHA256SUMS.txt") -Encoding utf8
Write-Host "Checksums written to: $(Join-Path $DistDir 'SHA256SUMS.txt')" -ForegroundColor Green
Write-Host "`nAll build and packaging steps completed successfully!" -ForegroundColor Green
