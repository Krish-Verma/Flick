<#
.SYNOPSIS
Produce an AUTHENTICODE-SIGNED PREVIEW for Smart App Control acceptance QA.
.DESCRIPTION
This never publishes/releases files automatically. A validated Public Trust
signing identity and Artifact Signing tools must already be configured.
#>
param([switch]$SkipNativeBuild)
$ErrorActionPreference = 'Stop'
$root = (Get-Item $PSScriptRoot).Parent.FullName
$packageDir = Join-Path $root 'dist\Flick-v1.0.10-win-x64'
$zip = Join-Path $root 'dist\Flick-v1.0.10-win-x64.zip'
$setup = Join-Path $root 'dist\Flick-Setup.exe'
$manifest = Join-Path $root 'dist\SHA256SUMS.txt'
$signScript = Join-Path $PSScriptRoot 'sign_flick_file.ps1'
$verify = Join-Path $PSScriptRoot 'verify_flick_trust.ps1'
# Fail before compilation if no valid authenticated signer has been prepared.
foreach ($envName in @('FLICK_SIGNTOOL_PATH','FLICK_ARTIFACT_SIGNING_DLIB','FLICK_ARTIFACT_SIGNING_METADATA')) {
    $value = [Environment]::GetEnvironmentVariable($envName)
    if (-not $value -or -not (Test-Path -LiteralPath $value -PathType Leaf)) {
        throw "Cannot build Smart App Control-ready release: signing prerequisite $envName missing. Do not distribute unsigned fallback."
    }
}
& (Join-Path $PSScriptRoot 'build_package.ps1') -SkipNativeBuild:$SkipNativeBuild
if ($LASTEXITCODE -ne 0) { throw "Portable compile/package failed" }

$ownedRelativePaths = @(
    'Flick.exe',
    'Flick.dll',
    'LocalClip.Core.dll',
    'tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe'
)
foreach ($relative in $ownedRelativePaths) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageDir $relative) -PathType Leaf)) {
        throw "Expected Flick-owned signing input absent: $relative"
    }
}

# Signing changes PE bytes. Upstream OBS, FFmpeg and .NET files must remain
# exactly as redistributed; a project certificate is not an upstream identity.
# This boundary also supports Foundation's own-binaries-only signing policy.
$upstreamBefore = @(Get-ChildItem -LiteralPath $packageDir -Recurse -File |
    Where-Object { $_.Extension -in @('.exe','.dll') } |
    ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($packageDir, $_.FullName)
        if ($relative -notin $ownedRelativePaths) {
            [pscustomobject]@{
                RelativePath = $relative
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        }
    })

foreach ($relative in $ownedRelativePaths) {
    $filePath = Join-Path $packageDir $relative
    $status = (Get-AuthenticodeSignature -LiteralPath $filePath).Status
    if ($status -eq 'NotSigned') {
        & $signScript -FilePath $filePath
        if ($LASTEXITCODE -ne 0) { throw "Signing stopped at $filePath" }
    }
    elseif ($status -ne 'Valid') {
        throw "Refusing invalid Flick-owned signing input: $filePath, signature status $status"
    }
}

foreach ($upstream in $upstreamBefore) {
    $path = Join-Path $packageDir $upstream.RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $upstream.Sha256) {
        throw "Signing modified an upstream executable; refusing package: $($upstream.RelativePath)"
    }
}
& (Join-Path $PSScriptRoot 'check_ffmpeg_runtime.ps1') -RuntimeRoot (Join-Path $packageDir 'tools\ffmpeg')
Write-Host "Preserved $($upstreamBefore.Count) upstream EXE/DLL files byte-for-byte; signed only the four explicit Flick-owned paths."

# Keep the stricter all-code Windows trust gate. Unsigned upstream files can
# still block SAC readiness even though Foundation permits packaging them.
& $verify -PackageRoot $packageDir
if ($LASTEXITCODE -ne 0) { throw "Portable signature audit failed" }

# The original portable ZIP was made before code signing; remake it from the
# signed app files. Hash it only AFTER signing, never reuse old checksum data.
Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zip -CompressionLevel Optimal -Force
$exeHash = (Get-FileHash -LiteralPath (Join-Path $packageDir 'Flick.exe') -Algorithm SHA256).Hash
$hostHash = (Get-FileHash -LiteralPath (Join-Path $packageDir 'tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe') -Algorithm SHA256).Hash
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
@(
    "# Flick signed preview SHA-256 checksums, not yet Smart App Control certified",
    "$zipHash  Flick-v1.0.10-win-x64.zip",
    "$exeHash  Flick.exe",
    "$hostHash  LocalClip.CaptureHost.exe"
) | Set-Content -LiteralPath $manifest -Encoding utf8

# Inno SignedUninstaller signs the uninstaller as well as the outer Setup.
& (Join-Path $PSScriptRoot 'build_installer.ps1') -SkipPortable -RequireTrustedSigning
if ($LASTEXITCODE -ne 0) { throw "Signed Setup/Uninstaller compile failed" }
& $verify -PackageRoot $packageDir -InstallerPath $setup
if ($LASTEXITCODE -ne 0) { throw "Trusted Setup verification failed" }

& (Join-Path $PSScriptRoot 'verify_flick_release.ps1')
if ($LASTEXITCODE -ne 0) { throw "Signed build checksum verification failed" }
Write-Host "SIGNED PREVIEW BUILD SUCCESS. Not yet Smart App Control-qualified until on-device enforcement tests include installer, app, native capture and uninstaller." -ForegroundColor Green
