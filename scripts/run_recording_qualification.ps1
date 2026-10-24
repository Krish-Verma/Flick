<#
.SYNOPSIS
Run only Flick's owned, silent synthetic-window qualification harness.
.DESCRIPTION
Requires a locally reviewed SHA256 manifest and explicit capture opt-in. Uses the
same owner's existing Explorer token when SSH runs in Session 0. No credentials,
scheduled tasks, services, privilege/ACL changes, desktop or microphone capture.
Missing interactive access is a refused physical gate, never a reason to weaken
Windows security. This does not install, update, or operate the real Flick app.
#>
param(
    [Parameter(Mandatory=$true)][string]$ManifestPath,
    [Parameter(Mandatory=$true)][string]$HarnessPath,
    [Parameter(Mandatory=$true)][string]$HostPath,
    [Parameter(Mandatory=$true)][string]$FfmpegPath,
    [Parameter(Mandatory=$true)][string]$FfprobePath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][ValidateSet('obs_nvenc_h264_tex','obs_nvenc_h264_soft','h264_texture_amf','obs_qsv11_v2','obs_x264')][string]$Encoder,
    [int]$ExplorerProcessId = 0,
    [ValidateRange(60,1200)][int]$TimeoutSeconds = 900,
    [switch]$RunCapture
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Windows is required.' }
if (-not $RunCapture) { throw 'Refused: explicitly pass -RunCapture for the owned synthetic window only.' }

function Assert-RegularPath([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path) -or $Path.StartsWith('\\')) { throw 'Qualification paths must be absolute local paths.' }
    $item = Get-Item -LiteralPath ([IO.Path]::GetFullPath($Path)) -Force
    while ($null -ne $item) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse paths are refused.' }
        $item = if ($item -is [IO.FileInfo]) { $item.Directory } else { $item.Parent }
    }
}
foreach ($file in @($ManifestPath,$HarnessPath,$HostPath,$FfmpegPath,$FfprobePath)) {
    Assert-RegularPath $file
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Required qualification file is missing.' }
}
if ([IO.Path]::GetFileName($HarnessPath) -cne 'Flick.RecordingHarness.exe' -or
    [IO.Path]::GetFileName($HostPath) -cne 'LocalClip.CaptureHost.exe' -or
    [IO.Path]::GetFileName($FfmpegPath) -cne 'ffmpeg.exe' -or
    [IO.Path]::GetFileName($FfprobePath) -cne 'ffprobe.exe') { throw 'Unexpected qualification executable.' }
$stageRoot = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ManifestPath))
$stagePrefix = $stageRoot.TrimEnd('\') + '\'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.SchemaVersion -ne 1 -or $manifest.Purpose -cne 'SyntheticWindowNoAudio' -or $manifest.SourceCommit -notmatch '^[0-9a-f]{40}$' -or
    @($manifest.Files).Count -lt 4) { throw 'Invalid qualification source/file manifest.' }
$verified = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.Files) {
    $relative = [string]$entry.Path
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)' -or
        [string]$entry.Sha256 -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Invalid manifest file entry.' }
    $path = [IO.Path]::GetFullPath((Join-Path $stageRoot $relative))
    if (-not $path.StartsWith($stagePrefix,[StringComparison]::OrdinalIgnoreCase) -or
        -not $verified.Add($path)) { throw 'Manifest path escapes stage or is duplicated.' }
    Assert-RegularPath $path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $entry.Sha256) { throw "Qualification hash mismatch: $relative" }
}
foreach ($file in @($HarnessPath,$HostPath,$FfmpegPath,$FfprobePath,
    (Join-Path $PSScriptRoot 'RecordingHarnessSession.cs'),$PSCommandPath)) {
    if (-not $verified.Contains([IO.Path]::GetFullPath($file))) { throw 'An executed input is absent from the manifest.' }
}
$directories = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
$directories.Push([IO.DirectoryInfo]::new($stageRoot))
while ($directories.Count -gt 0) {
    foreach ($entry in $directories.Pop().GetFileSystemInfos()) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Stage contains a reparse entry.' }
        if ($entry -is [IO.DirectoryInfo]) { $directories.Push($entry); continue }
        if ($entry.FullName -ine [IO.Path]::GetFullPath($ManifestPath) -and -not $verified.Contains($entry.FullName)) {
            throw 'Stage contains a file outside the verified manifest. Use a fresh owned stage.'
        }
    }
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
$parent = [IO.Path]::GetDirectoryName($output)
Assert-RegularPath $parent
if (-not $output.StartsWith($stagePrefix,[StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($output) -notmatch '^Flick-RecordingQualification-[0-9a-f]{32}$' -or
    (Test-Path -LiteralPath $output)) { throw 'Output must be a new uniquely named directory inside the owned stage.' }
$receiptFile = $output + '.launcher.json'
$reservation = [IO.File]::Open($receiptFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
$reservation.Dispose()
$receipt = [ordered]@{ SchemaVersion=1; SourceCommit=$manifest.SourceCommit; StartedUtc=[DateTime]::UtcNow.ToString('o');
    Outcome='Refused'; Admission=$null; HarnessPid=$null; SessionId=$null; ExitCode=$null; ForcedTermination=$false; Error=$null; CleanupError=$null; After=$null; AfterObservationError=$null }
function Save-Receipt { $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptFile -Encoding UTF8 }
function Get-Resources {
    $os = Get-CimInstance Win32_OperatingSystem
    $cpu = @(Get-CimInstance Win32_Processor | ForEach-Object { [double]$_.LoadPercentage } | Measure-Object -Average).Average
    [ordered]@{ Utc=[DateTime]::UtcNow.ToString('o'); AvailableMiB=[math]::Floor([double]$os.FreePhysicalMemory/1024); CpuPercent=$cpu }
}
$owned = $null
try {
    $receipt.Admission = Get-Resources
    Save-Receipt
    if ($receipt.Admission.AvailableMiB -lt 3072 -or $null -eq $receipt.Admission.CpuPercent -or
        $receipt.Admission.CpuPercent -ge 60) { throw 'Resource admission refused: need >=3072MiB available RAM and CPU below60%.' }
    $explorers = @(Get-Process -Name explorer -ErrorAction SilentlyContinue | Where-Object SessionId -gt 0)
    if ($ExplorerProcessId -ne 0) { $explorers = @($explorers | Where-Object Id -eq $ExplorerProcessId) }
    if ($explorers.Count -ne 1) { throw 'One explicit existing interactive Explorer process is required.' }
    # Bind the observed process before opening its existing token. The bridge
    # independently verifies SID and session using the retained process handle.
    $desktop = $explorers[0]
    if ([IO.Path]::GetFullPath($desktop.Path) -ine (Join-Path $env:WINDIR 'explorer.exe')) { throw 'Unexpected desktop process path.' }
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    Add-Type -Path (Join-Path $PSScriptRoot 'RecordingHarnessSession.cs')
    function Quote-Argument([string]$Value) {
        if ($Value -match '["\r\n\x00]' -or $Value.EndsWith('\')) { throw 'Unsafe command argument.' }
        '"' + $Value + '"'
    }
    $arguments = @($HarnessPath,'--host',$HostPath,'--ffmpeg',$FfmpegPath,'--ffprobe',$FfprobePath,
        '--output',$output,'--encoder',$Encoder,'--run-capture') | ForEach-Object { Quote-Argument $_ }
    $owned = [RecordingHarnessSession]::Start([uint32]$desktop.Id,[uint32]$desktop.SessionId,$sid,$HarnessPath,
        ($arguments -join ' '),[IO.Path]::GetDirectoryName($HarnessPath))
    $receipt.HarnessPid = $owned.ProcessId
    $receipt.SessionId = $owned.SessionId
    $receipt.Outcome = 'Running'
    Save-Receipt
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not $owned.HasExited -and $timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) { Start-Sleep -Milliseconds 500 }
    if (-not $owned.HasExited) {
        if (Test-Path -LiteralPath (Join-Path $output '.flick-recording-qualification.json') -PathType Leaf) {
            [IO.File]::WriteAllText((Join-Path $output 'CANCEL'),'Owned qualification timeout: cancel and release the helper.')
        }
        $grace = [Diagnostics.Stopwatch]::StartNew()
        while (-not $owned.HasExited -and $grace.Elapsed.TotalSeconds -lt 45) { Start-Sleep -Milliseconds 500 }
        if (-not $owned.HasExited) { $receipt.ForcedTermination = $true; $owned.StopOwned() }
        throw 'Owned qualification exceeded its deadline. See preserved case receipts.'
    }
    $receipt.ExitCode = $owned.ExitCode
    if ($receipt.ExitCode -ne 0) { throw "Harness failed or refused with exit code $($receipt.ExitCode)." }
    $receipt.Outcome = 'ExitedSuccessfully'
}
catch {
    $receipt.Error = $_.Exception.Message
    if ($receipt.Outcome -eq 'Running') { $receipt.Outcome = 'Failed' }
    throw
}
finally {
    if ($null -ne $owned) {
        try {
            if (-not $owned.HasExited) { $receipt.ForcedTermination = $true; $owned.StopOwned() }
        }
        catch { $receipt.CleanupError = $_.Exception.Message; $receipt.Outcome = 'CleanupUnconfirmed' }
        finally { $owned.Dispose() }
    }
    try { $receipt.After = Get-Resources }
    catch { $receipt.AfterObservationError = $_.Exception.Message }
    Save-Receipt
}
Write-Host "Owned harness exited successfully. Inspect every case and media receipt in $output before claiming capture qualification."
