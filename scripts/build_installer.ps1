<#
.SYNOPSIS
Build the standard Flick Setup EXE using Inno Setup 6 on Windows.
.DESCRIPTION
Does not sign or publish releases or alter your repository visibility.
#>
param(
    [switch]$SkipPortable,
    [switch]$SkipNativeBuild,
    [switch]$RequireTrustedSigning,
    [string]$IsccPath = ""
)
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
$portable = Join-Path $root "dist\Flick-v1.0.13-win-x64"
$icon = Join-Path $root "dist\Flick.ico"
$setup = Join-Path $root "installer\Flick.iss"

if (-not $SkipPortable) {
    & (Join-Path $PSScriptRoot "build_package.ps1") -SkipNativeBuild:$SkipNativeBuild
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "Portable build failed" }
}
foreach ($required in @(
    (Join-Path $portable "Flick.exe"),
    (Join-Path $portable "tools\ffmpeg\ffmpeg.exe"),
    (Join-Path $portable "tools\ffmpeg\ffprobe.exe"),
    (Join-Path $portable "tools\obs\runtime\bin\64bit\obs.dll"),
    (Join-Path $portable "LICENSE"),
    $icon, $setup
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing installer input: $required" }
}
if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $resolved = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($resolved) { $IsccPath = $resolved.Source }
    else {
        $folders = @([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86),
                     [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles))
        $IsccPath = $folders | ForEach-Object { Join-Path $_ "Inno Setup 6\ISCC.exe" } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw "ISCC.exe not found. Install official Inno Setup 6 or supply -IsccPath. Do not silently download executables."
}
if ($RequireTrustedSigning) {
    # This is intentionally unavailable without a real Public Trust signing
    # identity and a configured SignTool/Dlib client. No fake/self-signed fallback.
    foreach ($item in @('FLICK_SIGNTOOL_PATH','FLICK_ARTIFACT_SIGNING_DLIB','FLICK_ARTIFACT_SIGNING_METADATA')) {
        $file = [Environment]::GetEnvironmentVariable($item)
        if ([string]::IsNullOrWhiteSpace($file) -or -not (Test-Path -LiteralPath $file)) {
            throw "Official release signing required; missing $item. Refusing unsigned installer."
        }
    }
    $signScript = Join-Path $root 'scripts\sign_flick_file.ps1'
    $signCommand = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $q' +
        $signScript + '$q $f'
    & $IsccPath '/DFLICK_REQUIRE_TRUSTED_SIGNING=1' ("/Sflicksign=$signCommand") $setup
} else {
    & $IsccPath $setup
}
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }
if ($RequireTrustedSigning) {
    $flick = Join-Path $portable 'Flick.exe'
    & (Join-Path $root 'scripts\verify_flick_trust.ps1') -PackageRoot $portable -InstallerPath (Join-Path $root 'dist\Flick-Setup.exe')
    if ($LASTEXITCODE -ne 0) { throw "Trusted installer signature verification failed" }
}

$output = Join-Path $root "dist\Flick-Setup.exe"
if (-not (Test-Path -LiteralPath $output)) { throw "Installer output missing: $output" }
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
$checksums = Join-Path $root "dist\SHA256SUMS.txt"
if (Test-Path -LiteralPath $checksums) {
    Add-Content -LiteralPath $checksums -Encoding UTF8 -Value (
        "$hash  Flick-Setup.exe")
}
Write-Host "Flick Setup built: $output" -ForegroundColor Green
Write-Host "SHA256: $hash"
Write-Warning "Unsigned preview: release qualification requires actual install, upgrade, uninstall, data preservation and Windows UI evidence."
