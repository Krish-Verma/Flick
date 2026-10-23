# Explicit owner-desktop test: captures the selected display, no microphone.
param(
    [Parameter(Mandatory=$true)][string]$ExpectedEncoder,
    [Parameter(Mandatory=$true)][string]$PackageDirectory,
    [ValidateRange(0,32)][int]$MonitorIndex = 0,
    [string]$EvidenceDirectory = ""
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and [Environment]::OSVersion.Platform -ne 'Win32NT') {
    throw 'GPU compatibility recording tests require Windows.'
}
$root = (Get-Item $PSScriptRoot).Parent.FullName
$package = [IO.Path]::GetFullPath($PackageDirectory)
$hostPath = Join-Path $package 'tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe'
$ffmpeg = Join-Path $package 'tools\ffmpeg\ffmpeg.exe'
$ffprobe = Join-Path $package 'tools\ffmpeg\ffprobe.exe'
foreach ($path in @($hostPath, $ffmpeg, $ffprobe)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required test binary missing: $path" }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the pinned .NET SDK to run repository hardware tests.' }
if (!$EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $env:TEMP ('Flick-GpuCompatibility-' + [Guid]::NewGuid().ToString('N'))
}
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Choose a new evidence directory; prior evidence is preserved.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$environmentNames = @('FLICK_HARDWARE_TESTS','FLICK_CAPTURE_HOST_PATH','FLICK_TEST_OUTPUT_DIR',
    'FLICK_EXPECT_ENCODER','FLICK_TEST_MONITOR','FLICK_TEST_FFMPEG','FLICK_TEST_FFPROBE')
$previous = @{}
foreach ($name in $environmentNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:FLICK_HARDWARE_TESTS = '1'
    $env:FLICK_CAPTURE_HOST_PATH = $hostPath
    $env:FLICK_TEST_OUTPUT_DIR = $evidence
    $env:FLICK_EXPECT_ENCODER = $ExpectedEncoder
    $env:FLICK_TEST_MONITOR = "$MonitorIndex"
    $env:FLICK_TEST_FFMPEG = $ffmpeg
    $env:FLICK_TEST_FFPROBE = $ffprobe
    Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion, VideoModeDescription |
        ConvertTo-Json | Set-Content (Join-Path $evidence 'video-controllers.json') -Encoding utf8
    [pscustomobject]@{
        TimestampUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Os = [Environment]::OSVersion.VersionString
        ExpectedEncoder = $ExpectedEncoder; MonitorIndex = $MonitorIndex
        HostSha256 = (Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).Hash
        SourceCommit = (git -C $root rev-parse HEAD)
        CaptureScope = 'Selected display; microphone disabled. Saved recordings require visual/audio review.'
    } | ConvertTo-Json | Set-Content (Join-Path $evidence 'session.json') -Encoding utf8
    Write-Host "Recording the selected display twice (automatic encoder, then CPU compatibility). Evidence: $evidence"
    dotnet test (Join-Path $root 'tests/LocalClip.Core.Tests/LocalClip.Core.Tests.csproj') -c Release `
        --filter 'FullyQualifiedName~GpuCompatibilityHardwareTests' --logger 'trx;LogFileName=gpu-compatibility.trx' --results-directory $evidence
    if ($LASTEXITCODE -ne 0) { throw "GPU compatibility tests failed; preserve evidence at $evidence" }
    $report = Join-Path $evidence 'gpu-compatibility.trx'
    if (-not (Test-Path -LiteralPath $report -PathType Leaf)) { throw 'Missing GPU test execution report.' }
    [xml]$results = Get-Content -LiteralPath $report -Raw
    $ns = [Xml.XmlNamespaceManager]::new($results.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $counters = $results.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters', $ns)
    $cases = @($results.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
    if ($null -eq $counters -or [int]$counters.total -ne 2 -or [int]$counters.executed -ne 2 -or
        [int]$counters.passed -ne 2 -or [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0 -or
        $cases.Count -ne 2 -or @($cases | Where-Object outcome -ne 'Passed').Count -ne 0) {
        throw 'GPU recording qualification requires both expected tests to execute and pass; skips or missing tests are not acceptance.'
    }
    Write-Host 'Recording, MP4 export and decode checks passed for this exact machine and host binary.'
    Write-Host 'Inspect the MKVs for full-display framing, motion, audio sync, HDR appearance and game performance before qualifying the setup.'
}
finally {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
