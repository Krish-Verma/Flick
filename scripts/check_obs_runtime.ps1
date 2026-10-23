# Payload completeness for the pinned OBS 32.2.2 Windows x64 runtime.
# This does not load plugins or qualify a GPU/driver. Keep the full upstream
# runtime: these are minimum required capture/encoder dependencies, not a prune list.
param([Parameter(Mandatory=$true)][string]$RuntimeRoot)
$ErrorActionPreference = "Stop"
$runtime = [IO.Path]::GetFullPath($RuntimeRoot)

$required = @(
    "bin\64bit\obs.dll",
    "bin\64bit\libobs-d3d11.dll",
    "bin\64bit\libobs-winrt.dll",
    "bin\64bit\obs-nvenc-test.exe",
    "bin\64bit\obs-amf-test.exe",
    "bin\64bit\obs-qsv-test.exe",
    "bin\64bit\obs-ffmpeg-mux.exe",
    "bin\64bit\libx264-164.dll",
    "bin\64bit\avcodec-62.dll",
    "bin\64bit\avdevice-62.dll",
    "bin\64bit\avfilter-11.dll",
    "bin\64bit\avformat-62.dll",
    "bin\64bit\avutil-60.dll",
    "bin\64bit\swscale-9.dll",
    "bin\64bit\swresample-6.dll",
    "bin\64bit\w32-pthreads.dll",
    "bin\64bit\librist.dll",
    "bin\64bit\srt.dll",
    "bin\64bit\zlib.dll",
    "bin\64bit\libcurl.dll",
    "obs-plugins\64bit\obs-nvenc.dll",
    "obs-plugins\64bit\obs-qsv11.dll",
    "obs-plugins\64bit\obs-x264.dll",
    "obs-plugins\64bit\obs-ffmpeg.dll",
    "obs-plugins\64bit\obs-outputs.dll",
    "obs-plugins\64bit\win-capture.dll",
    "obs-plugins\64bit\win-wasapi.dll",
    "data\libobs\default.effect",
    "data\obs-plugins\obs-nvenc\locale\en-US.ini",
    "data\obs-plugins\obs-qsv11\locale\en-US.ini",
    "data\obs-plugins\obs-x264\locale\en-US.ini",
    "data\obs-plugins\obs-ffmpeg\locale\en-US.ini",
    "data\obs-plugins\obs-outputs\locale\en-US.ini",
    "data\obs-plugins\win-capture\locale\en-US.ini",
    "data\obs-plugins\win-wasapi\locale\en-US.ini",
    "data\obs-plugins\win-capture\graphics-hook32.dll",
    "data\obs-plugins\win-capture\graphics-hook64.dll",
    "data\obs-plugins\win-capture\get-graphics-offsets32.exe",
    "data\obs-plugins\win-capture\get-graphics-offsets64.exe",
    "data\obs-plugins\win-capture\inject-helper32.exe",
    "data\obs-plugins\win-capture\inject-helper64.exe"
)
foreach ($relative in $required) {
    $path = Join-Path $runtime $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Incomplete OBS 32.2.2 runtime: $relative is missing or empty. Restore the full pinned archive using scripts/setup_tools.ps1 before building."
    }
    if ([IO.Path]::GetExtension($path) -notin @('.exe', '.dll')) { continue }
    # Detect empty/substitute/wrong-architecture binaries without loading code.
    $stream = [IO.File]::OpenRead($path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) {
            throw "Invalid OBS Windows binary: $relative"
        }
        $stream.Position = 0x3C
        $header = $reader.ReadInt32()
        if ($header -lt 64 -or $header -gt $stream.Length - 6) {
            throw "Invalid OBS PE header: $relative"
        }
        $stream.Position = $header
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid OBS PE signature: $relative" }
        $expectedMachine = if ($relative -match '\\(?:graphics-hook|inject-helper|get-graphics-offsets)32\.(?:exe|dll)$') { 0x014C } else { 0x8664 }
        if ($reader.ReadUInt16() -ne $expectedMachine) { throw "Wrong OBS binary architecture: $relative" }
    }
    finally { $reader.Dispose() }
}
Write-Host "OBS 32.2.2 capture, NVENC/AMF/QSV/x264 payload completeness PASS; hardware qualification is separate." -ForegroundColor Green
