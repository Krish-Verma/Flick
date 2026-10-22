# Evaluate whether a recorded video actually contains changing frames.
# This is a diagnostic, not a substitute for visually reviewing moving gameplay.
param(
    [Parameter(Mandatory=$true)][string]$Original,
    [Parameter(Mandatory=$true)][string]$Export,
    [string]$Ffmpeg = "",
    [int]$MinimumDistinctFrames = 8
)
$ErrorActionPreference = "Stop"
$Root = (Get-Item $PSScriptRoot).Parent.FullName
if ([string]::IsNullOrWhiteSpace($Ffmpeg)) {
    $Ffmpeg = Join-Path $Root "tools/ffmpeg/ffmpeg.exe"
    if (-not (Test-Path $Ffmpeg)) { $Ffmpeg = "ffmpeg" }
}
foreach ($path in @($Original, $Export)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing video: $path" }
}
foreach ($path in @($Original, $Export)) {
    Write-Host "Inspecting video: $path"
    $frames = @(& $Ffmpeg -hide_banner -v error -i $path -map 0:v:0 -vf "fps=2,scale=160:-2" -f framemd5 - 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg failed to decode sampled frames for $path" }
    $hashes = @($frames | Where-Object { $_ -match '^\s*\d+\s*,' } | ForEach-Object {
        ($_ -split ',')[-1].Trim().ToLowerInvariant()
    } | Where-Object { $_ -match '^[0-9a-f]{32}$' })
    $unique = @($hashes | Sort-Object -Unique).Count
    Write-Host "  Sampled frames: $($hashes.Count); distinct frame hashes: $unique"
    if ($hashes.Count -lt 8 -or $unique -lt $MinimumDistinctFrames) {
        throw "Insufficient motion evidence in $path. Capture a real animated game and inspect the output visually."
    }
}
Write-Host "Motion diagnostic passed. Listen to audio, compare quality, and inspect the entire clips manually."
