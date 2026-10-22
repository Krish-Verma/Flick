# Flick test runner. Hardware tests are SKIPPED unless explicitly enabled.
param([switch]$Hardware)
$ErrorActionPreference = "Stop"
$RootDir = (Get-Item $PSScriptRoot).Parent.FullName

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not on PATH. Install the pinned .NET 10 SDK."
}

if ($Hardware) {
    $env:FLICK_HARDWARE_TESTS = "1"
    Write-Host "Hardware suite enabled: requires the test owner's Windows GPU and OBS runtime." -ForegroundColor Yellow
} else {
    $env:FLICK_HARDWARE_TESTS = "0"
    Write-Host "Running hardware-independent tests; NVENC/end-to-end tests will be reported SKIPPED." -ForegroundColor Cyan
}

dotnet test (Join-Path $RootDir "LocalClip.slnx") --configuration Release --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { throw "Flick test suite failed with exit code $LASTEXITCODE" }
