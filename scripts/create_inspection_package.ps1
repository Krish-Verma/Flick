# Script to assemble the comprehensive full inspection package for another agent
$ErrorActionPreference = "Stop"

$RootDir = (Get-Item $PSScriptRoot).Parent.FullName
$DistDir = Join-Path $RootDir "dist"
$PackageName = "LocalClip-Full-Inspection-Package-2026-10-14"
$StagingDir = Join-Path $DistDir $PackageName
$ZipPath = Join-Path $DistDir "$PackageName.zip"

Write-Host "=== Creating Comprehensive Inspection Package ===" -ForegroundColor Cyan

# 1. Clean previous staging
if (Test-Path $StagingDir) { Remove-Item -Recurse -Force $StagingDir }
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

# 2. Copy Root Manifest Files
$RootFiles = @("README.md", "LocalClip.slnx", "dependencies.lock.json", "LICENSE", "THIRD-PARTY-NOTICES.md", ".gitignore")
foreach ($f in $RootFiles) {
    $src = Join-Path $RootDir $f
    if (Test-Path $src) {
        Copy-Item $src $StagingDir -Force
    }
}

# 3. Copy Key Directories
$DirsToCopy = @("ClipperBlueprint", "docs", "scripts", "src", "tests", "tools", "TestOutput")
foreach ($d in $DirsToCopy) {
    $src = Join-Path $RootDir $d
    $dest = Join-Path $StagingDir $d
    if (Test-Path $src) {
        Write-Host "Copying $d..." -ForegroundColor Gray
        Copy-Item -Path $src -Destination $StagingDir -Recurse -Force
    }
}

# 4. Clean bin/obj from staged src and tests to keep inspection package clean
Get-ChildItem -Path (Join-Path $StagingDir "src"), (Join-Path $StagingDir "tests") -Recurse -Directory -Filter "bin" | Remove-Item -Recurse -Force
Get-ChildItem -Path (Join-Path $StagingDir "src"), (Join-Path $StagingDir "tests") -Recurse -Directory -Filter "obj" | Remove-Item -Recurse -Force

# 5. Copy pre-built release artifacts from dist into staged release folder
$StagedDist = Join-Path $StagingDir "dist"
New-Item -ItemType Directory -Path $StagedDist -Force | Out-Null
if (Test-Path (Join-Path $DistDir "SHA256SUMS.txt")) {
    Copy-Item (Join-Path $DistDir "SHA256SUMS.txt") $StagedDist -Force
}
if (Test-Path (Join-Path $DistDir "app")) {
    Copy-Item -Path (Join-Path $DistDir "app") -Destination $StagedDist -Recurse -Force
}
if (Test-Path (Join-Path $DistDir "LocalClip-v1.0.0-win-x64.zip")) {
    Copy-Item (Join-Path $DistDir "LocalClip-v1.0.0-win-x64.zip") $StagedDist -Force
}

# 6. Generate Package Manifest & Verification Inventory
$InventoryFile = Join-Path $StagingDir "PACKAGE-INVENTORY.md"
$FileList = Get-ChildItem -Path $StagingDir -Recurse -File | Select-Object -Property FullName, Length

$Manifest = @"
# LocalClip Comprehensive Inspection Package Manifest
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Package: $PackageName.zip

## Summary of Packaged Assets
- **Blueprint & Handoff Specs:** ClipperBlueprint/ (authoritative specifications)
- **Source Code:** src/LocalClip.App/, src/LocalClip.Core/, src/LocalClip.CaptureHost/
- **Test Suite:** tests/LocalClip.Core.Tests/ (41 automated tests)
- **Toolchains & Runtimes:** tools/ffmpeg/ (v9.0.2), tools/obs/ (v32.2.2 runtime & headers)
- **Build & Test Automation:** scripts/build_package.ps1, scripts/run_tests.ps1
- **Real Verification Evidence:** TestOutput/ (captured MKVs, 20MB Discord export MP4, thumbnails)
- **Documentation:** docs/ (User Guide, Architecture, Implementation State, Test Results, Agent Handoff Guide)
- **Pre-Built Distribution:** dist/LocalClip-v1.0.0-win-x64.zip, dist/app/ (Release win-x64), dist/SHA256SUMS.txt

Total Files: $($FileList.Count)
Total Size (Uncompressed): $([math]::Round(($FileList | Measure-Object -Property Length -Sum).Sum / 1MB, 2)) MB
"@
$Manifest | Set-Content $InventoryFile -Encoding utf8

# 7. Compress Archive
Write-Host "Compressing full inspection package to: $ZipPath..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $StagingDir "*") -DestinationPath $ZipPath -CompressionLevel Optimal -Force

# 8. Checksum
$ZipHash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash
Write-Host "Inspection Package created successfully!" -ForegroundColor Green
Write-Host "Path: $ZipPath" -ForegroundColor Green
Write-Host "SHA-256: $ZipHash" -ForegroundColor Green
Write-Host "Size: $([math]::Round((Get-Item $ZipPath).Length / 1MB, 2)) MB" -ForegroundColor Green
