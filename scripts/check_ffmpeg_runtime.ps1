# Exact payload hashes taken from the SHA-256-verified upstream archive.
param([Parameter(Mandatory=$true)][string]$RuntimeRoot)
$ErrorActionPreference = 'Stop'
$root = (Get-Item $PSScriptRoot).Parent.FullName
$pin = (Get-Content -LiteralPath (Join-Path $root 'dependencies.lock.json') -Raw | ConvertFrom-Json).pinnedDependencies.ffmpeg
if ($null -eq $pin.files -or @($pin.files.PSObject.Properties).Count -ne 4) {
    throw 'Missing exact FFmpeg binary/notice hashes in dependency lock.'
}
foreach ($file in $pin.files.PSObject.Properties) {
    if ($file.Name -notin @('ffmpeg.exe','ffprobe.exe','UPSTREAM-LICENSE.txt','UPSTREAM-README.txt')) {
        throw 'Unexpected FFmpeg payload path in dependency lock.'
    }
    $path = Join-Path $RuntimeRoot $file.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing FFmpeg payload/notice: $($file.Name)" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Value) {
        throw "FFmpeg payload hash does not match the pinned upstream archive: $($file.Name)"
    }
}
Write-Host "Verified exact FFmpeg $($pin.version) binaries and upstream license/readme." -ForegroundColor Green
