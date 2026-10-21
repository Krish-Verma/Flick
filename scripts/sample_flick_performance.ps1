<#
.SYNOPSIS
Samples app-owned Windows process resource usage for a real Flick QA session.
.DESCRIPTION
No app launch, game injection, recording, network access, admin privileges,
process termination, screenshots, or fabricated FPS values. Run from the
owner's Windows PC; combine with an independent frame-time sampler.
#>
param(
    [Parameter(Mandatory=$true)][ValidateRange(1,2147483647)][int]$AppPid,
    [ValidateRange(0,2147483647)][int]$HostPid = 0,
    [ValidateRange(1,480)][int]$DurationMinutes = 5,
    [ValidateRange(5,60)][int]$SampleSeconds = 5,
    [ValidateSet("Idle", "Buffering", "NotificationOn", "NotificationOff",
                 "Library", "Editing", "Export", "Soak")][string]$Phase = "Idle",
    [string]$OutputCsv = ""
)
$ErrorActionPreference = "Stop"
$app = Get-Process -Id $AppPid -ErrorAction Stop
if ($app.ProcessName -notin @("Flick", "LocalClip.App")) {
    throw "The specified AppPid is not Flick.exe or legacy LocalClip.App.exe. Refusing to sample another program."
}
if ($HostPid -gt 0) {
    $hostProcess = Get-Process -Id $HostPid -ErrorAction Stop
    if ($hostProcess.ProcessName -ne "LocalClip.CaptureHost") {
        throw "The supplied HostPid is not LocalClip.CaptureHost."
    }
}
if (!$OutputCsv) {
    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
    $OutputCsv = Join-Path $env:TEMP "Flick-Performance-$Phase-$stamp.csv"
}
$OutputCsv = [IO.Path]::GetFullPath($OutputCsv)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($OutputCsv)) -Force | Out-Null
if (Test-Path -LiteralPath $OutputCsv) {
    throw "Output already exists. Choose a new CSV; previous evidence is never overwritten."
}

$targets = @(@{ Name = "App"; Id = $AppPid })
if ($HostPid -gt 0) { $targets += @{ Name = "CaptureHost"; Id = $HostPid } }
$cpuBefore = @{}
$timeBefore = @{}
$clock = [Diagnostics.Stopwatch]::StartNew()
$endTime = [TimeSpan]::FromMinutes($DurationMinutes)
$sampleIndex = 0
Write-Host "Flick QA: $Phase | PID $AppPid | Host PID $HostPid | interval $SampleSeconds seconds."
Write-Host "Sampling to: $OutputCsv"
Write-Host "CPU is percent of one logical CPU; FPS and GPU usage are not measured by this script."

while ($clock.Elapsed -lt $endTime) {
    $sampleIndex++
    $now = [DateTimeOffset]::Now
    foreach ($target in $targets) {
        $id = [int]$target.Id
        $name = [string]$target.Name
        $current = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($null -eq $current) {
            $row = [pscustomobject]@{
                TimeLocal = $now.ToString("o"); Phase=$Phase; Role=$name; Pid=$id
                Sample=$sampleIndex; State="Exited"; WorkingSetMiB=$null
                PrivateBytesMiB=$null; CpuOneCorePct=$null; Handles=$null; Threads=$null
            }
        }
        else {
            $cpu = [double]$current.TotalProcessorTime.TotalSeconds
            $currentTime = $clock.Elapsed.TotalSeconds
            $cpuPercent = $null
            if ($cpuBefore.ContainsKey($id) -and $timeBefore.ContainsKey($id)) {
                $deltaTime = $currentTime - [double]$timeBefore[$id]
                if ($deltaTime -gt 0) {
                    $cpuPercent = [Math]::Round(
                        100.0 * [Math]::Max(0, $cpu - [double]$cpuBefore[$id]) / $deltaTime, 2)
                }
            }
            $cpuBefore[$id] = $cpu
            $timeBefore[$id] = $currentTime
            $row = [pscustomobject]@{
                TimeLocal = $now.ToString("o"); Phase=$Phase; Role=$name; Pid=$id
                Sample=$sampleIndex; State="Running"
                WorkingSetMiB=[Math]::Round($current.WorkingSet64 / 1MB, 2)
                PrivateBytesMiB=[Math]::Round($current.PrivateMemorySize64 / 1MB, 2)
                CpuOneCorePct=$cpuPercent; Handles=$current.HandleCount
                Threads=$current.Threads.Count
            }
        }
        $row | Export-Csv -Path $OutputCsv -NoTypeInformation -Append -Encoding UTF8
    }
    if ($clock.Elapsed -ge $endTime) { break }
    $remaining = [Math]::Min($SampleSeconds, ($endTime - $clock.Elapsed).TotalSeconds)
    if ($remaining -gt 0) { Start-Sleep -Milliseconds ([int]($remaining * 1000)) }
}
Write-Host "Completed $sampleIndex samples. Preserve the CSV and correlate timestamps with manual FPS/1% lows."
