# Automated Toolchain & Runtime Setup Script for Flick / LocalClip
# Downloads and extracts pinned FFmpeg 9.0.1 and OBS Studio 32.2.2 runtimes if not present

$ErrorActionPreference = "Stop"
$RootDir = (Get-Item $PSScriptRoot).Parent.FullName
$ToolsDir = Join-Path $RootDir "tools"
$FfmpegDir = Join-Path $ToolsDir "ffmpeg"
$ObsDir = Join-Path $ToolsDir "obs"

Write-Host "=== Setting up Flick development & runtime tools ===" -ForegroundColor Cyan

# 1. Setup FFmpeg
if (!(Test-Path (Join-Path $FfmpegDir "ffmpeg.exe"))) {
    Write-Host "FFmpeg binaries not found. Downloading Gyan.dev FFmpeg 9.0.1 Essentials..." -ForegroundColor Yellow
    $ffmpegZip = Join-Path $env:TEMP "ffmpeg-9.0.1.zip"
    $ffmpegUrl = "https://github.com/GyanD/codexffmpeg/releases/download/9.0.1/ffmpeg-9.0.1-essentials_build.zip"
    
    Invoke-WebRequest -Uri $ffmpegUrl -OutFile $ffmpegZip -UseBasicParsing
    $tempExtract = Join-Path $env:TEMP "ffmpeg_extract"
    if (Test-Path $tempExtract) { Remove-Item -Recurse -Force $tempExtract }
    Expand-Archive -Path $ffmpegZip -DestinationPath $tempExtract
    
    New-Item -ItemType Directory -Path $FfmpegDir -Force | Out-Null
    $binDir = Get-ChildItem -Path $tempExtract -Recurse -Directory -Filter "bin" | Select-Object -First 1
    Copy-Item (Join-Path $binDir.FullName "ffmpeg.exe") $FfmpegDir -Force
    Copy-Item (Join-Path $binDir.FullName "ffprobe.exe") $FfmpegDir -Force
    
    Remove-Item -Force $ffmpegZip
    Remove-Item -Recurse -Force $tempExtract
    Write-Host "FFmpeg 9.0.1 installed to: $FfmpegDir" -ForegroundColor Green
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
    $targetRuntime = Join-Path $ObsDir "runtime"
    New-Item -ItemType Directory -Path $targetRuntime -Force | Out-Null
    Expand-Archive -Path $obsZip -DestinationPath $targetRuntime -Force
    
    Remove-Item -Force $obsZip
    Write-Host "OBS Studio 32.2.2 runtime installed to: $targetRuntime" -ForegroundColor Green
} else {
    Write-Host "OBS Studio runtime already present in: $(Join-Path $ObsDir 'runtime')" -ForegroundColor Green
}

Write-Host "`nAll tools are ready for building and running Flick." -ForegroundColor Green
