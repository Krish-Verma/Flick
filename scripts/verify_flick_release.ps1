# Verify actual SHA-256 values and bundled runtimes of the current Flick preview.
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
$dist = Join-Path $root "dist"
$manifest = Join-Path $dist "SHA256SUMS.txt"
$version = (Get-Content -LiteralPath (Join-Path $root "release\preview-version.txt") -Raw).Trim()
if ($version -ne "1.0.1-preview.1") { throw "Unexpected Flick release version: $version" }
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { throw "Missing SHA256SUMS.txt" }
$checksums = Get-Content -LiteralPath $manifest -Raw
$files = @(
  @{ Name = "Flick-Setup.exe"; Path = (Join-Path $dist "Flick-Setup.exe") },
  @{ Name = "Flick-v1.0.1-win-x64.zip"; Path = (Join-Path $dist "Flick-v1.0.1-win-x64.zip") },
  @{ Name = "Flick.exe"; Path = (Join-Path $dist "Flick-v1.0.1-win-x64\Flick.exe") },
  @{ Name = "LocalClip.CaptureHost.exe"; Path = (Join-Path $dist "Flick-v1.0.1-win-x64\tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe") }
)
foreach ($file in $files) {
  if (-not (Test-Path -LiteralPath $file.Path -PathType Leaf)) { throw "Required binary missing: $($file.Path)" }
  $actual = (Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash
  if (-not $checksums.Contains("$actual  $($file.Name)")) { throw "SHA-256 mismatch for $($file.Name)" }
  Write-Host "Verified $($file.Name): $actual" -ForegroundColor Green
}
foreach ($path in @(
  "Flick-v1.0.1-win-x64\LICENSE",
  "Flick-v1.0.1-win-x64\LICENSE-NOTICE.md",
  "Flick-v1.0.1-win-x64\THIRD-PARTY-NOTICES.md",
  "Flick-v1.0.1-win-x64\tools\ffmpeg\ffmpeg.exe",
  "Flick-v1.0.1-win-x64\tools\ffmpeg\ffprobe.exe",
  "Flick-v1.0.1-win-x64\tools\obs\runtime\bin\64bit\obs.dll"
)) {
  if (-not (Test-Path -LiteralPath (Join-Path $dist $path) -PathType Leaf)) {
    throw "Required runtime or notice missing: $path"
  }
}
Write-Host "Flick preview distribution integrity PASS; Windows install/real-GPU testing is separate." -ForegroundColor Green
