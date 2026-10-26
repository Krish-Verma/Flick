# Inspect the shipped PE, then prove its IPC starts without a developer PATH.
# Ping/shutdown do not initialize OBS, select sources, or record audio/video.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$HostPath,
    [string]$EvidencePath = '',
    [switch]$ImportsOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-CaptureHostImports([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        function Assert-Range([long]$Offset, [long]$Count) {
            if ($Offset -lt 0 -or $Count -lt 0 -or $Offset -gt $stream.Length - $Count) {
                throw 'Malformed capture helper PE: a file range exceeds the binary.'
            }
        }
        function Read-U16([long]$Offset) {
            Assert-Range $Offset 2
            $stream.Position = $Offset
            return $reader.ReadUInt16()
        }
        function Read-U32([long]$Offset) {
            Assert-Range $Offset 4
            $stream.Position = $Offset
            return $reader.ReadUInt32()
        }
        if ((Read-U16 0) -ne 0x5A4D) { throw 'Capture helper is not a Windows PE.' }
        $pe = [long](Read-U32 0x3C)
        Assert-Range $pe 24
        if ($pe -lt 64 -or (Read-U32 $pe) -ne 0x00004550 -or
            (Read-U16 ($pe + 4)) -ne 0x8664) {
            throw 'Capture helper must be a valid Windows x64 PE.'
        }
        $sectionCount = [int](Read-U16 ($pe + 6))
        $optionalSize = [int](Read-U16 ($pe + 20))
        $optional = $pe + 24
        Assert-Range $optional $optionalSize
        if ($sectionCount -lt 1 -or $sectionCount -gt 96 -or $optionalSize -lt 128 -or
            (Read-U16 $optional) -ne 0x20B -or (Read-U32 ($optional + 108)) -lt 2) {
            throw 'Malformed capture helper PE64 headers.'
        }
        $headersSize = [long](Read-U32 ($optional + 60))
        $table = $optional + $optionalSize
        Assert-Range $table ($sectionCount * 40)
        if ($headersSize -lt $table + $sectionCount * 40 -or $headersSize -gt $stream.Length) {
            throw 'Malformed capture helper PE header size.'
        }
        $sections = @(
            for ($i = 0; $i -lt $sectionCount; $i++) {
                $s = $table + $i * 40
                $rawSize = [long](Read-U32 ($s + 16))
                $rawStart = [long](Read-U32 ($s + 20))
                Assert-Range $rawStart $rawSize
                [pscustomobject]@{
                    Rva = [long](Read-U32 ($s + 12))
                    Span = [Math]::Max([long](Read-U32 ($s + 8)), $rawSize)
                    RawStart = $rawStart
                    RawSize = $rawSize
                }
            }
        )
        function Resolve-Rva([long]$Rva, [long]$Count) {
            if ($Rva -lt 0 -or $Count -lt 1 -or $Rva + $Count -gt 0x100000000L) {
                throw 'Malformed capture helper PE RVA.'
            }
            if ($Rva -lt $headersSize -and $Count -le $headersSize - $Rva) {
                Assert-Range $Rva $Count
                return $Rva
            }
            $mappedSections = @($sections | Where-Object { $Rva -ge $_.Rva -and $Rva - $_.Rva -lt $_.Span })
            if ($mappedSections.Count -ne 1) { throw 'Unmapped or ambiguous capture helper PE RVA.' }
            $section = $mappedSections[0]
            $delta = $Rva - $section.Rva
            if ($delta -gt $section.RawSize - $Count) { throw 'Capture helper PE RVA has no file data.' }
            $offset = $section.RawStart + $delta
            Assert-Range $offset $Count
            return $offset
        }
        function Read-ImportName([long]$Rva) {
            $name = [Text.StringBuilder]::new()
            for ($j = 0; $j -lt 260; $j++) {
                $stream.Position = Resolve-Rva ($Rva + $j) 1
                $b = $reader.ReadByte()
                if ($b -eq 0) {
                    $result = $name.ToString()
                    if ($result -notmatch '^[A-Za-z0-9_+.-]+\.dll$') { throw 'Invalid PE import DLL name.' }
                    return $result
                }
                if ($b -gt 127) { throw 'Non-ASCII PE import DLL name.' }
                $null = $name.Append([char]$b)
            }
            throw 'Unterminated or excessive PE import DLL name.'
        }
        $importRva = [long](Read-U32 ($optional + 120))
        $importSize = [long](Read-U32 ($optional + 124))
        if ($importRva -eq 0 -or $importSize -lt 20 -or $importSize -gt 1048576) {
            throw 'Missing or invalid capture helper PE import table.'
        }
        $imports = [Collections.Generic.List[string]]::new()
        $terminated = $false
        for ($i = 0; $i -lt [Math]::Min(4096, [Math]::Floor($importSize / 20)); $i++) {
            $descriptor = Resolve-Rva ($importRva + $i * 20) 20
            $fields = @(for ($j = 0; $j -lt 5; $j++) { Read-U32 ($descriptor + $j * 4) })
            if (@($fields | Where-Object { $_ -ne 0 }).Count -eq 0) { $terminated = $true; break }
            if ($fields[3] -eq 0 -or $fields[4] -eq 0) { throw 'Malformed PE import descriptor.' }
            $imports.Add((Read-ImportName ([long]$fields[3])))
        }
        if (-not $terminated -or $imports.Count -eq 0) { throw 'Unterminated or empty PE import table.' }
        # MinGW emits ordinary imports. Reject a delay table rather than silently
        # omitting DLLs from this intentionally small capture-helper validator.
        if ((Read-U32 ($optional + 108)) -ge 14) {
            if ($optionalSize -lt 224) { throw 'Truncated PE data directory table.' }
            if ((Read-U32 ($optional + 216)) -ne 0 -or (Read-U32 ($optional + 220)) -ne 0) {
                throw 'Unexpected delayed DLL imports in capture helper; extend verification before shipping.'
            }
        }
        return @($imports.ToArray() | Sort-Object -Unique)
    }
    finally { $reader.Dispose() }
}

$process = $null
$stdoutTask = $null
$stderrTask = $null
$work = $null
$clock = [Diagnostics.Stopwatch]::StartNew()
$receipt = [ordered]@{
    Result = 'FAIL'; Phase = 'InspectingPE'; HostPath = $HostPath; HostSha256 = $null
    Imports = @(); ImportsOnly = [bool]$ImportsOnly; ChildPath = $null
    Responses = @(); ExitCode = $null; Stdout = ''; Stderr = ''; ElapsedMilliseconds = $null
    HardwareCaptureQualified = $false; FailureReason = $null
}
try {
    $absoluteHost = [IO.Path]::GetFullPath($HostPath)
    $receipt.HostPath = $absoluteHost
    if ($EvidencePath) {
        $EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
        if ($EvidencePath.Equals($absoluteHost, [StringComparison]::OrdinalIgnoreCase)) {
            $EvidencePath = ''
            throw 'Evidence path must not overwrite the capture helper.'
        }
    }
    if (-not (Test-Path -LiteralPath $absoluteHost -PathType Leaf)) { throw 'Capture helper binary is missing.' }
    $receipt.HostSha256 = (Get-FileHash -LiteralPath $absoluteHost -Algorithm SHA256).Hash
    $receipt.Imports = @(Get-CaptureHostImports $absoluteHost)
    $compilerDlls = @($receipt.Imports | Where-Object {
        $_ -match '^(libwinpthread|libstdc\+\+|libgcc|libssp|msys-|cygwin).*\.dll$'
    })
    if ($compilerDlls.Count) { throw "Capture helper imports compiler runtime DLLs that must be linked statically: $($compilerDlls -join ', ')" }
    if (-not $ImportsOnly) {
        $receipt.Phase = 'CleanEnvironmentIPC'
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
            throw 'Capture helper execution requires Windows; use -ImportsOnly for PE inspection elsewhere.'
        }
        $windows = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
        $system = [Environment]::SystemDirectory
        if (-not $windows -or -not $system) { throw 'Unable to establish Windows system directories.' }
        $receipt.ChildPath = "$system;$windows"
        $work = Join-Path ([IO.Path]::GetTempPath()) ('flick-host-runtime-' + [Guid]::NewGuid().ToString('N'))
        $null = [IO.Directory]::CreateDirectory($work)
        if (-not ('Flick.CaptureHostRuntimeErrorMode' -as [type])) {
            Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
namespace Flick {
    public static class CaptureHostRuntimeErrorMode {
        [DllImport("kernel32.dll")] public static extern uint GetErrorMode();
        [DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint mode);
    }
}
'@
        }
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = $absoluteHost
        $info.WorkingDirectory = $work
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardInput = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $info.Environment['PATH'] = $receipt.ChildPath
        $previousMode = [Flick.CaptureHostRuntimeErrorMode]::GetErrorMode()
        try {
            $null = [Flick.CaptureHostRuntimeErrorMode]::SetErrorMode($previousMode -bor 0x8003)
            $process = [Diagnostics.Process]::Start($info)
        }
        finally { $null = [Flick.CaptureHostRuntimeErrorMode]::SetErrorMode($previousMode) }
        if ($null -eq $process) { throw 'Capture helper did not start.' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.WriteLine('{"id":"runtime-ping","cmd":"ping"}')
        $process.StandardInput.WriteLine('{"id":"runtime-shutdown","cmd":"shutdown"}')
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(10000)) { throw 'Capture helper IPC timed out after ten seconds.' }
        $receipt.ExitCode = $process.ExitCode
        if (-not $stdoutTask.Wait(2000) -or -not $stderrTask.Wait(2000)) { throw 'Capture helper pipes did not close.' }
        $receipt.Stdout = $stdoutTask.Result
        $receipt.Stderr = $stderrTask.Result
        if ($process.ExitCode -ne 0) { throw "Capture helper failed with exit code $($process.ExitCode)." }
        if ($receipt.Stdout.Length -gt 65536 -or $receipt.Stderr.Length -gt 65536) { throw 'Excessive capture helper output.' }
        $responses = @()
        foreach ($line in @($receipt.Stdout -split '\r?\n' | Where-Object { $_.Trim() })) {
            $message = $line | ConvertFrom-Json -Depth 16
            if ($null -ne $message.PSObject.Properties['event']) {
                if ($message.event -cne 'state_changed' -or $message.data.state -cne 'Off') {
                    throw 'Unexpected capture activity during loader-only smoke.'
                }
                continue
            }
            if ($null -eq $message.PSObject.Properties['id'] -or
                $message.id -cnotin @('runtime-ping', 'runtime-shutdown') -or
                $message.ok -isnot [bool] -or -not $message.ok) { throw 'Invalid capture helper IPC response.' }
            $responses += $message
        }
        $receipt.Responses = $responses
        $ping = @($responses | Where-Object id -CEQ 'runtime-ping')
        $shutdown = @($responses | Where-Object id -CEQ 'runtime-shutdown')
        if ($ping.Count -ne 1 -or $shutdown.Count -ne 1 -or $responses.Count -ne 2 -or
            $ping[0].result -cne 'pong' -or $shutdown[0].result.status -cne 'shutdown_complete') {
            throw 'Capture helper did not complete the exact ping/shutdown exchange.'
        }
    }
    $receipt.Result = 'PASS'
    $receipt.Phase = 'Complete'
    Write-Host 'Capture helper PE and compiler-runtime independence PASS; physical capture qualification is separate.' -ForegroundColor Green
}
catch {
    $receipt.FailureReason = $_.Exception.Message
    throw
}
finally {
    $cleanupFailure = $null
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) { $process.Kill($true); $null = $process.WaitForExit(5000) }
            if (-not $process.HasExited) { throw 'Owned capture helper did not exit during cleanup.' }
            if ($process.HasExited) { $receipt.ExitCode = $process.ExitCode }
            if ($null -ne $stdoutTask -and $stdoutTask.IsCompletedSuccessfully) { $receipt.Stdout = $stdoutTask.Result }
            if ($null -ne $stderrTask -and $stderrTask.IsCompletedSuccessfully) { $receipt.Stderr = $stderrTask.Result }
        }
        catch { $cleanupFailure = $_.Exception.Message }
        finally { $process.Dispose() }
    }
    try { if ($work -and (Test-Path -LiteralPath $work)) { Remove-Item -LiteralPath $work -Force -Recurse } }
    catch { $cleanupFailure = "Unable to remove smoke working directory: $($_.Exception.Message)" }
    if ($cleanupFailure) {
        $receipt.Result = 'FAIL'
        if (-not $receipt.FailureReason) { $receipt.FailureReason = $cleanupFailure }
    }
    $clock.Stop()
    $receipt.ElapsedMilliseconds = $clock.ElapsedMilliseconds
    if ($EvidencePath) {
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($EvidencePath))
        $receipt | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $EvidencePath -Encoding utf8
    }
    if ($cleanupFailure) { throw $cleanupFailure }
}
