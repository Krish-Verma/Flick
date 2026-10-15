# Script to run all automated tests with proper environment configuration
$ErrorActionPreference = "Stop"

$RootDir = (Get-Item $PSScriptRoot).Parent.FullName
$env:PATH = "C:\Users\krish\AppData\Local\Microsoft\dotnet;C:\Users\krish\AppData\Local\Microsoft\WinGet\Packages\BrechtSanders.WinLibs.POSIX.UCRT_Microsoft.Winget.Source_8wekyb3d8bbwe\mingw64\bin;$env:PATH"

Write-Host "Running LocalClip Automated Test Suite across solution..." -ForegroundColor Cyan
dotnet test (Join-Path $RootDir "LocalClip.slnx") --logger "console;verbosity=normal"
