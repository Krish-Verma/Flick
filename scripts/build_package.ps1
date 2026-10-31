# Build and Package Script for LocalClip (Windows x64)
# The CI-only -SkipNativeBuild switch requires a previously compiled owned host.
param([switch]$SkipNativeBuild)

$ErrorActionPreference = "Stop"

$RootDir = (Get-Item $PSScriptRoot).Parent.FullName
$DistDir = Join-Path $RootDir "dist"
$PackageName = "Flick-v1.0.13-win-x64"
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
if (-not $SkipNativeBuild -and -not (Get-Command windres -ErrorAction SilentlyContinue)) {
    throw "MinGW-w64 windres is required for the capture worker's product/version metadata"
}
if (-not (Test-Path (Join-Path $RootDir "tools\\obs\\runtime\\bin\\64bit\\obs.dll"))) { throw "Missing pinned OBS runtime. Run scripts/setup_tools.ps1." }
if (-not (Test-Path (Join-Path $RootDir "tools\\ffmpeg\\ffmpeg.exe"))) { throw "Missing FFmpeg runtime. Run scripts/setup_tools.ps1." }
& (Join-Path $PSScriptRoot "check_ffmpeg_runtime.ps1") -RuntimeRoot (Join-Path $RootDir "tools\ffmpeg")
& (Join-Path $PSScriptRoot "check_obs_runtime.ps1") -RuntimeRoot (Join-Path $RootDir "tools\obs\runtime")

Write-Host "=== Step 2: Compiling Native CaptureHost (C++ MinGW) ===" -ForegroundColor Cyan
$HostSrc = Join-Path $RootDir "src\LocalClip.CaptureHost\main.cpp"
$HostOut = Join-Path $RootDir "tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe"
$ObsInc = Join-Path $RootDir "tools\obs\include\libobs"
$HostInc = Join-Path $RootDir "src\LocalClip.CaptureHost"
$ObsBin = Join-Path $RootDir "tools\obs\runtime\bin\64bit"

if (-not $SkipNativeBuild) {
    $ResourceOut = Join-Path $DistDir "capture-version.o"
    windres -I "$HostInc" (Join-Path $HostInc "nativeversion.rc") -O coff -o "$ResourceOut"
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile native capture product/version resources" }
    g++ -O2 -std=c++17 -static-libgcc -static-libstdc++ `
        -I "$ObsInc" -I "$HostInc" "$HostSrc" "$ResourceOut" `
        -L "$ObsBin" "-Wl,-Bstatic,--whole-archive" -lwinpthread "-Wl,--no-whole-archive,-Bdynamic" -lobs -ldwmapi -lole32 `
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
Copy-Item (Join-Path $RootDir "tools\ffmpeg\UPSTREAM-LICENSE.txt") (Join-Path $ToolsDest "ffmpeg\") -Force
Copy-Item (Join-Path $RootDir "tools\ffmpeg\UPSTREAM-README.txt") (Join-Path $ToolsDest "ffmpeg\") -Force
& (Join-Path $PSScriptRoot "check_ffmpeg_runtime.ps1") -RuntimeRoot (Join-Path $ToolsDest "ffmpeg")

# Copy OBS runtime
Copy-Item (Join-Path $RootDir "tools\obs\runtime") (Join-Path $ToolsDest "obs\") -Recurse -Force
& (Join-Path $PSScriptRoot "check_obs_runtime.ps1") -RuntimeRoot (Join-Path $ToolsDest "obs\runtime")
& (Join-Path $PSScriptRoot "check_capture_host_runtime.ps1") `
    -HostPath (Join-Path $PackageDir "tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe") `
    -EvidencePath (Join-Path $DistDir "capture-runtime/portable.json")

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
$Readme = @'
# Flick 1.0.13 Preview 1 — UNSIGNED

This Windows x64 preview improves startup, tray replay, game detection, Settings Save, hover feedback and clip titles. Recording, editing, sharing and existing settings are retained. Exact-source package and installer checks are required before publication; physical gameplay acceptance remains separate.

## Start and save a replay

1. Keep every bundled OBS, FFmpeg and .NET file together. Run Flick.exe for portable use, or use the matching Flick-Setup.exe for a per-user installation.
2. Select an available window or display. Foreground launches wait for manual replay or an approved game. Tray/startup launches keep your saved replay source ready when enabled in General. Use Start Replay (default Ctrl+Shift+F9), wait for Replay Ready, then Save Clip (default Ctrl+Shift+F8).
3. Recording Health shows the actual encoder/profile, retained-buffer estimate, source and save errors. A save is successful only after its original MKV is validated and indexed.
4. The recording test uses the confirmed source and saves an actual test clip. Audio is off in its consent dialog unless explicitly included. Its result does not certify every game or device.

## Optional automatic game replay

Games settings are off by default. Windows startup registration is verified on every enabled Settings Save; Windows Startup apps can independently disable launching. Successful Save returns to your clips. Keep replay ready in the tray starts only the saved, currently available source and can be disabled in General. Manual Stop pauses automatic replay; Start Replay resumes it. To opt in, refresh running games, approve an exact executable and choose its replay profile. When enabled, this setting may start replay automatically for an eligible approved game, including after a later Flick launch. It does not select a display when the approved window is missing. Stop Replay pauses that running game session. Manual replay and its preferences remain separate.

## Choose actual audio sources

System Audio records the whole selected output device. Separate App Audio records explicitly selected Game and/or Voice process trees on supported Windows builds; app restarts require refreshing those selections. An unavailable process refuses or stops capture instead of silently substituting system audio. Attachment and observed sound are reported separately. Microphone capture remains an independent, explicit opt-in.

Dedicated tracks represent real configured sources. Game/Voice are user-assigned roles; they do not identify individual people or prove endpoint isolation. Physical app/Discord/endpoint acceptance remains pending.

## Edit without changing the original recording

Use favorites, tags, game grouping, markers, search and sorting to organize the local library. Trim and audio mute/gain settings affect derived copies. Reset Mix restores the known source defaults. Combined is an alternate complete mix and is not added to its own dedicated tracks a second time.

Original playback plays the recorded file. Preview Mix creates an on-demand playback copy using current trim/levels; compatibility previews and mix previews share an owned worker. Previews are limited to the first 3 minutes of the selected range and a 120 MB output budget. Optional timeline frames are limited to six small stills. These limits are implementation safeguards, not measured guarantees of FPS or memory overhead.

## Prepare a real file to share

Choose a size limit and Original, Portrait or Square framing, then export an H.264/AAC MP4. Copy File and the Share tile provide a Windows file attachment; they do not upload or paste a URL. The destination's size limits and file acceptance may differ. Actual Discord desktop drag/paste still requires acceptance testing. Optional webhook posting requires a configured destination and explicit confirmation.

## Settings, updates and removal

Settings, the library index and bounded local diagnostics live separately from installed binaries under LocalAppData/LocalClip. Original/imported videos and exports stay in their selected folders. Diagnostics are local, allowlisted events; export them only when requested. Local capture/edit/export requires no account, and there are no automatic uploads.

Installed copies can check Settings > General > Software Updates; preview updates require the preview channel and an actually published matching release. Portable copies require quitting Flick before replacing the extracted app folder. Updates are available only after a matching verified release is published. Older versions may need a one-time manual installer upgrade.

Use Uninstall Flick or Windows Installed Apps to remove the program. Silent uninstall preserves user data. The interactive uninstaller separately asks whether to remove app-owned preferences/index/cache; recordings and exports are retained. Do not use historical LocalClip uninstall scripts.

## Requirements and qualification limits

Targets Windows 11 x64 with working Direct3D11 graphics, current drivers and the Microsoft Visual C++ v14 x64 runtime. Selected-app audio uses the documented process-loopback API floor of Windows build 20348, and still requires successful attachment. Available NVIDIA/AMD/Intel H.264 encoders are attempted; optional x264 fallback is capped at 1280x720 and 30 fps and can increase CPU use or reduce gameplay performance.

GPU/hybrid setups, hotkeys during games, actual audio isolation, Discord transfer, default-device changes, high-DPI interaction and endurance/performance are not universally qualified. No leak-free, zero-impact or all-GPU claim is made.

This preview has no trusted publisher signature. Windows Smart App Control may block it. Checksums verify transfer consistency, not publisher trust; keep Windows security protections enabled. SignPath/trusted signing and Microsoft Store certification remain separate work.

See docs/USER-GUIDE.md, docs/UX-RELIABILITY-20261030.md and docs/product-phase/MASTER-PLAN.md. Original Flick source remains GPL-2.0-or-later; bundled components retain their separate notices and obligations.
'@
$Readme | Set-Content (Join-Path $PackageDir "README.md") -Encoding utf8

Write-Host "=== Step 5: Creating Portable ZIP Archive ===" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $PackageDir "*") -DestinationPath $ZipPath -CompressionLevel Optimal -Force
Write-Host "Portable ZIP created at: $ZipPath" -ForegroundColor Green

Write-Host "=== Step 6: Calculating SHA-256 Checksums ===" -ForegroundColor Cyan
$ZipHash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash
$ExeHash = (Get-FileHash -Path (Join-Path $PackageDir "Flick.exe") -Algorithm SHA256).Hash
$HostHash = (Get-FileHash -Path (Join-Path $PackageDir "tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe") -Algorithm SHA256).Hash

$ChecksumContent = @"
# Flick v1.0.13 Preview (unsigned Windows x64 replay-and-UX candidate) SHA-256 Checksums
$ZipHash  Flick-v1.0.13-win-x64.zip
$ExeHash  Flick.exe
$HostHash  LocalClip.CaptureHost.exe
"@
$ChecksumContent | Set-Content (Join-Path $DistDir "SHA256SUMS.txt") -Encoding utf8
Write-Host "Checksums written to: $(Join-Path $DistDir 'SHA256SUMS.txt')" -ForegroundColor Green
Write-Host "`nAll build and packaging steps completed successfully!" -ForegroundColor Green
