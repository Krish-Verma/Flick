# Automated Toolchain & Runtime Setup Script for Flick / LocalClip
# Downloads and extracts pinned FFmpeg 9.0.2 and OBS Studio 32.2.2 runtimes if not present

$ErrorActionPreference = "Stop"
$RootDir = (Get-Item $PSScriptRoot).Parent.FullName
$ToolsDir = Join-Path $RootDir "tools"
$FfmpegDir = Join-Path $ToolsDir "ffmpeg"
$ObsDir = Join-Path $ToolsDir "obs"

Write-Host "=== Setting up Flick development & runtime tools ===" -ForegroundColor Cyan

# 1. Restore the exact pinned FFmpeg payload, including upstream notices.
$ffmpegPin = (Get-Content -LiteralPath (Join-Path $RootDir "dependencies.lock.json") -Raw | ConvertFrom-Json).pinnedDependencies.ffmpeg
$reuseFfmpeg = $false
if (Test-Path (Join-Path $FfmpegDir "ffmpeg.exe")) {
    try {
        & (Join-Path $PSScriptRoot "check_ffmpeg_runtime.ps1") -RuntimeRoot $FfmpegDir
        $reuseFfmpeg = $true
    } catch { Write-Host "Restoring the verified FFmpeg payload and notices." -ForegroundColor Yellow }
}
if (-not $reuseFfmpeg) {
    Write-Host "FFmpeg binaries not found. Downloading Gyan.dev FFmpeg 9.0.2 Essentials..." -ForegroundColor Yellow
    $ffmpegZip = Join-Path $env:TEMP ("flick-ffmpeg-" + [Guid]::NewGuid().ToString("N") + ".zip")
    $ffmpegUrl = $ffmpegPin.source
    
    Invoke-WebRequest -Uri $ffmpegUrl -OutFile $ffmpegZip -UseBasicParsing
    $expected = $ffmpegPin.sha256
    if ((Get-FileHash -Path $ffmpegZip -Algorithm SHA256).Hash -ne $expected) {
        Remove-Item -Force $ffmpegZip
        throw "FFmpeg archive SHA-256 mismatch: refusing to install unverified binaries"
    }
    $tempExtract = Join-Path $env:TEMP ("flick-ffmpeg-extract-" + [Guid]::NewGuid().ToString("N"))
    if (Test-Path $tempExtract) { Remove-Item -Recurse -Force $tempExtract }
    Expand-Archive -Path $ffmpegZip -DestinationPath $tempExtract
    
    New-Item -ItemType Directory -Path $FfmpegDir -Force | Out-Null
    $binDir = Get-ChildItem -Path $tempExtract -Recurse -Directory -Filter "bin" | Select-Object -First 1
    Copy-Item (Join-Path $binDir.FullName "ffmpeg.exe") $FfmpegDir -Force
    Copy-Item (Join-Path $binDir.FullName "ffprobe.exe") $FfmpegDir -Force
    Copy-Item -LiteralPath (Join-Path $binDir.Parent.FullName "LICENSE") -Destination (Join-Path $FfmpegDir "UPSTREAM-LICENSE.txt") -Force
    Copy-Item -LiteralPath (Join-Path $binDir.Parent.FullName "README.txt") -Destination (Join-Path $FfmpegDir "UPSTREAM-README.txt") -Force
    & (Join-Path $PSScriptRoot "check_ffmpeg_runtime.ps1") -RuntimeRoot $FfmpegDir
    
    Remove-Item -Force $ffmpegZip
    Remove-Item -Recurse -Force $tempExtract
    Write-Host "FFmpeg 9.0.2 installed to: $FfmpegDir" -ForegroundColor Green
} else {
    Write-Host "FFmpeg binaries already present in: $FfmpegDir" -ForegroundColor Green
}

# 2. Setup OBS Runtime
$ObsRuntimeBin = Join-Path $ObsDir "runtime\bin\64bit\obs.dll"
if (!(Test-Path $ObsRuntimeBin)) {
    Write-Host "OBS Studio runtime not found. Downloading OBS Studio 32.2.2 Windows x64..." -ForegroundColor Yellow
    $obsZip = Join-Path $env:TEMP "obs-32.2.2.zip"
    $obsUrl = "https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Windows-x64.zip"
    
    Invoke-WebRequest -Uri $obsUrl -OutFile $obsZip -UseBasicParsing
    $expected = "4D6E40E3AB155F56B30DE517380566A206D74B63CDF5AD49AA596924768F97E1"
    if ((Get-FileHash -Path $obsZip -Algorithm SHA256).Hash -ne $expected) {
        Remove-Item -Force $obsZip
        throw "OBS runtime archive SHA-256 mismatch: refusing to install unverified binaries"
    }
    $targetRuntime = Join-Path $ObsDir "runtime"
    New-Item -ItemType Directory -Path $targetRuntime -Force | Out-Null
    Expand-Archive -Path $obsZip -DestinationPath $targetRuntime -Force
    if (!(Test-Path (Join-Path $targetRuntime "bin\\64bit\\obs.dll"))) {
        throw "OBS runtime extraction is missing bin/64bit/obs.dll; inspect ZIP layout before building"
    }
    
    Remove-Item -Force $obsZip
    Write-Host "OBS Studio 32.2.2 runtime installed to: $targetRuntime" -ForegroundColor Green
} else {
    Write-Host "OBS Studio runtime already present in: $(Join-Path $ObsDir 'runtime')" -ForegroundColor Green
}

& (Join-Path $PSScriptRoot "check_obs_runtime.ps1") -RuntimeRoot (Join-Path $ObsDir "runtime")

Write-Host "`nAll tools are ready for building and running Flick." -ForegroundColor Green
