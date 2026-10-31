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

# 2. Keep the pinned archive outside the distributable runtime. Reuse requires
# comparing every upstream file against these verified bytes, not a marker JSON.
$obsPin = (Get-Content -LiteralPath (Join-Path $RootDir 'dependencies.lock.json') -Raw | ConvertFrom-Json).pinnedDependencies.libobs
if ($obsPin.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid pinned OBS version.' }
$obsArchive = Join-Path $RootDir ("dist/obs-upstream-{0}.zip" -f $obsPin.version)
if (-not (Test-Path -LiteralPath $obsArchive -PathType Leaf) -or
    (Get-FileHash -LiteralPath $obsArchive -Algorithm SHA256).Hash -ine $obsPin.sha256) {
    $obsDownload = Join-Path ([IO.Path]::GetTempPath()) ('flick-obs-' + [Guid]::NewGuid().ToString('N') + '.zip')
    try {
        Invoke-WebRequest -Uri $obsPin.source -OutFile $obsDownload -UseBasicParsing
        if ((Get-FileHash -LiteralPath $obsDownload -Algorithm SHA256).Hash -ine $obsPin.sha256) {
            throw 'OBS archive SHA-256 mismatch: refusing unverified runtime bytes.'
        }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($obsArchive)) | Out-Null
        Move-Item -LiteralPath $obsDownload -Destination $obsArchive -Force
    }
    finally { if (Test-Path -LiteralPath $obsDownload) { Remove-Item -LiteralPath $obsDownload -Force } }
}
$reuseObs = $false
try {
    & (Join-Path $PSScriptRoot 'check_obs_runtime.ps1') -RuntimeRoot (Join-Path $ObsDir 'runtime') -VerifiedArchivePath $obsArchive
    $reuseObs = $true
}
catch { Write-Host 'Restoring changed or incomplete OBS files from the verified archive.' -ForegroundColor Yellow }
if (-not $reuseObs) {
    . (Join-Path $PSScriptRoot 'FlickObsArchive.ps1')
    $null = Invoke-FlickObsArchive -ArchivePath $obsArchive -ExpectedSha256 $obsPin.sha256 -RuntimeRoot (Join-Path $ObsDir 'runtime') -Restore
}
& (Join-Path $PSScriptRoot 'check_obs_runtime.ps1') -RuntimeRoot (Join-Path $ObsDir 'runtime') -VerifiedArchivePath $obsArchive

Write-Host "`nAll tools are ready for building and running Flick." -ForegroundColor Green
