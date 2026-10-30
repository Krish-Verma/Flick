<#
.SYNOPSIS
Exercise the installed-update handoff on a disposable GitHub-hosted Windows runner.
.DESCRIPTION
Installs into RUNNER_TEMP, uses a surrogate parent holding Flick's real mutex,
checks the updater's wait/restart behavior, and verifies unknown user data survives.
Does not exercise GPU capture, UI appearance, Smart App Control, or publisher trust.
#>
param(
    [string]$SetupPath = "dist/Flick-Setup.exe",
    [string]$PackagePath = "dist/Flick-v1.0.12-win-x64",
    [string]$BaselineSetupPath = ""
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or
    $env:RUNNER_OS -ne 'Windows' -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
    [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    throw 'Installer smoke is restricted to disposable GitHub-hosted Windows CI runners.'
}

$repositoryRoot = (Get-Item -LiteralPath $PSScriptRoot).Parent.FullName
function Resolve-RepositoryPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

$setupFile = Resolve-RepositoryPath $SetupPath
$packageDirectory = Resolve-RepositoryPath $PackagePath
$packageExe = Join-Path $packageDirectory 'Flick.exe'
$baselineFile = if ($BaselineSetupPath) { Resolve-RepositoryPath $BaselineSetupPath } else { $null }
foreach ($file in @($setupFile, $packageExe)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required smoke input missing: $file" }
}
if ($baselineFile -and -not (Test-Path -LiteralPath $baselineFile -PathType Leaf)) {
    throw 'Previous-version Setup input is missing.'
}

$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{F3C41F96-EF62-4CB1-A292-91EF3F6C18E8}_is1'
function Get-FlickRegistration {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
        $registryRoot = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
        try {
            $key = $registryRoot.OpenSubKey($uninstallKey)
            if ($null -eq $key) { continue }
            try {
                [pscustomobject]@{
                    View = $view.ToString()
                    DisplayName = [string]$key.GetValue('DisplayName')
                    InstallLocation = [string]$key.GetValue('InstallLocation')
                    UninstallString = [string]$key.GetValue('UninstallString')
                }
            }
            finally { $key.Dispose() }
        }
        finally { $registryRoot.Dispose() }
    }
}

# Never overwrite an existing user's registration, even on a misconfigured runner.
if (@(Get-FlickRegistration).Count -ne 0 -or @(Get-Process -Name Flick -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'Refusing to smoke-test over an existing Flick installation or Flick process.'
}

$fixtureId = [Guid]::NewGuid().ToString('N')
$workRoot = Join-Path ([IO.Path]::GetFullPath($env:RUNNER_TEMP)) "flick-installer-smoke-$fixtureId"
$evidenceRoot = Join-Path $repositoryRoot "dist/installer-smoke/$fixtureId"
$installDirectory = Join-Path $workRoot 'Installed App'
$installedExe = Join-Path $installDirectory 'Flick.exe'
$normalLog = Join-Path $evidenceRoot 'normal-install.log'
$updateLog = Join-Path $evidenceRoot 'update-install.log'
$uninstallLog = Join-Path $evidenceRoot 'uninstall.log'
$profileDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'LocalClip'
$sentinelFile = Join-Path $profileDirectory "installer-smoke-$fixtureId.sentinel"
$sentinelValue = "Flick installer must preserve this unknown user data: $fixtureId"
[IO.Directory]::CreateDirectory($workRoot) | Out-Null
[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
[IO.Directory]::CreateDirectory($profileDirectory) | Out-Null
[IO.File]::WriteAllText($sentinelFile, $sentinelValue)
$packageHash = (Get-FileHash -LiteralPath $packageExe -Algorithm SHA256).Hash
$startedInstallers = [Collections.Generic.List[Diagnostics.Process]]::new()
$surrogate = $null
$testPassed = $false
$waitTimer = $null
$restartedFlick = @()
$failureReason = $null
$baselineInstalledHash = $null
$baselineVersion = $null

function Get-TestFlickProcess {
    foreach ($candidate in @(Get-Process -Name Flick -ErrorAction SilentlyContinue)) {
        try {
            $candidatePath = $candidate.Path
            if ([string]::IsNullOrWhiteSpace($candidatePath) -and -not $candidate.HasExited) {
                throw 'A live Flick process has an unknown path.'
            }
            if ($candidatePath -and [IO.Path]::GetFullPath($candidatePath).Equals($installedExe, [StringComparison]::OrdinalIgnoreCase)) {
                $candidate
            }
        }
        catch {
            if (-not $candidate.HasExited) { throw 'Unable to establish a Flick process path; refusing unsafe cleanup.' }
        }
    }
}

function Assert-DataPreserved {
    if (-not (Test-Path -LiteralPath $sentinelFile -PathType Leaf) -or
        [IO.File]::ReadAllText($sentinelFile) -cne $sentinelValue) {
        throw 'Installer changed or removed unknown user data.'
    }
}

function Assert-InstalledFiles {
    if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf) -or
        (Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash -cne $packageHash) {
        throw 'Installed Flick.exe does not match the reviewed portable package.'
    }
    $expectedUninstaller = Join-Path $installDirectory 'unins000.exe'
    if (-not (Test-Path -LiteralPath $expectedUninstaller -PathType Leaf)) { throw 'Installed uninstaller missing.' }
    $registrations = @(Get-FlickRegistration)
    if ($registrations.Count -eq 0) { throw 'The Flick AppId was not registered under the current user.' }
    foreach ($registration in $registrations) {
        if ([string]::IsNullOrWhiteSpace($registration.InstallLocation)) { throw 'Missing registered install location.' }
        $registeredDirectory = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($registration.InstallLocation))
        if ($registration.DisplayName -cne 'Flick' -or
            -not $registeredDirectory.Equals([IO.Path]::TrimEndingDirectorySeparator($installDirectory), [StringComparison]::OrdinalIgnoreCase) -or
            -not $registration.UninstallString.Trim('"').Equals($expectedUninstaller, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Flick AppId registration does not point to the unique test installation.'
        }
    }
    Assert-DataPreserved
}

function Start-TestSetup([string]$LogPath, [string[]]$ExtraArguments = @(), [string]$InstallerFile = $setupFile) {
    $installerArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
        "/DIR=`"$installDirectory`"", "/LOG=`"$LogPath`"") + $ExtraArguments
    $process = Start-Process -FilePath $InstallerFile -ArgumentList $installerArguments -PassThru
    $startedInstallers.Add($process)
    return $process
}

function Wait-ForSuccessfulSetup([Diagnostics.Process]$Process) {
    if (-not $Process.WaitForExit(180000)) { throw 'Setup did not complete within three minutes.' }
    if ($Process.ExitCode -ne 0) { throw "Setup failed with exit code $($Process.ExitCode). Logs: $evidenceRoot" }
}

function Read-SetupLog([string]$Path) {
    # Setup still owns a write handle while we wait for its script log entry.
    # Permit that existing writer while opening a read-only view of the log.
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
    try { return $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}

Write-Host "Installer lifecycle smoke evidence: $evidenceRoot"
try {
    $initialSetup = if ($baselineFile) { $baselineFile } else { $setupFile }
    $normalSetup = Start-TestSetup -LogPath $normalLog -InstallerFile $initialSetup
    Wait-ForSuccessfulSetup $normalSetup
    if ($baselineFile) {
        if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) { throw 'Previous-version installation is missing.' }
        $baselineInstalledHash = (Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash
        $baselineVersion = (Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion
        if ($baselineInstalledHash -ceq $packageHash -or $baselineVersion -notlike '1.0.4*') {
            throw 'Baseline must be the distinct public Flick 1.0.4 installation.'
        }
        if (@(Get-FlickRegistration).Count -eq 0) { throw 'Previous version did not register the shared Flick AppId.' }
        Assert-DataPreserved
    } else { Assert-InstalledFiles }
    Start-Sleep -Seconds 2
    if (@(Get-TestFlickProcess).Count -ne 0) { throw 'A normal silent installation unexpectedly launched Flick.' }
    $initialRegistration = @(Get-FlickRegistration | ConvertTo-Json -Compress)

    # The surrogate has the same session-local mutex as real Flick. It remains
    # alive until Setup logs that it reached the exact-parent wait, then exits
    # six seconds later. This avoids false passes caused by slow decompression.
    $surrogateScript = Join-Path $workRoot 'surrogate-parent.ps1'
    $readyFile = Join-Path $workRoot 'parent-ready.txt'
    $allowExitFile = Join-Path $workRoot 'parent-allow-exit.txt'
    $parentExitFile = Join-Path $workRoot 'parent-exited.txt'
    @'
param([string]$ReadyFile, [string]$AllowExitFile, [string]$ExitFile)
$ErrorActionPreference = 'Stop'
$createdNew = $false
$heldMutex = [Threading.Mutex]::new($true, 'LocalClip_SingleInstance_UserLock', [ref]$createdNew)
if (-not $createdNew) { $heldMutex.Dispose(); throw 'Flick mutex is already held.' }
try {
    [IO.File]::WriteAllText($ReadyFile, 'ready')
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while (-not (Test-Path -LiteralPath $AllowExitFile)) {
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Timed out waiting for Setup to enter its parent wait.' }
        Start-Sleep -Milliseconds 100
    }
    Start-Sleep -Seconds 6
    [IO.File]::WriteAllText($ExitFile, [DateTime]::UtcNow.ToString('O'))
}
finally { $heldMutex.ReleaseMutex(); $heldMutex.Dispose() }
'@ | Set-Content -LiteralPath $surrogateScript -Encoding utf8
    $surrogate = Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -PassThru -ArgumentList @(
        '-NoProfile', '-NonInteractive', '-File', "`"$surrogateScript`"", '-ReadyFile', "`"$readyFile`"",
        '-AllowExitFile', "`"$allowExitFile`"", '-ExitFile', "`"$parentExitFile`"")
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $readyFile)) {
        if ($surrogate.HasExited -or [DateTime]::UtcNow -ge $deadline) { throw 'The surrogate parent did not become ready.' }
        Start-Sleep -Milliseconds 100
    }

    $updateSetup = Start-TestSetup $updateLog @('/FLICKUPDATE=1', "/FLICKWAITPID=$($surrogate.Id)")
    $expectedWaitMessage = "Flick update: waiting for parent PID $($surrogate.Id)"
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (-not ((Test-Path -LiteralPath $updateLog) -and
        (Read-SetupLog $updateLog).Contains($expectedWaitMessage))) {
        if ($updateSetup.HasExited -or $surrogate.HasExited -or [DateTime]::UtcNow -ge $deadline) {
            throw 'Setup did not reach the logged wait for the exact surrogate parent PID.'
        }
        Start-Sleep -Milliseconds 100
    }
    $waitTimer = [Diagnostics.Stopwatch]::StartNew()
    [IO.File]::WriteAllText($allowExitFile, 'exit after six seconds')
    Start-Sleep -Milliseconds 1500
    if ($updateSetup.HasExited -or $surrogate.HasExited -or @(Get-TestFlickProcess).Count -ne 0) {
        throw 'Setup completed or restarted Flick before its surrogate parent exited.'
    }
    Wait-ForSuccessfulSetup $updateSetup
    $waitTimer.Stop()
    if ($waitTimer.Elapsed.TotalSeconds -lt 5.5 -or -not $surrogate.WaitForExit(5000) -or
        $surrogate.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $parentExitFile) -or
        -not (Read-SetupLog $updateLog).Contains('Flick update: parent exited.')) {
        throw 'The updater did not prove it waited for the live parent to exit.'
    }
    Assert-InstalledFiles
    if ((@(Get-FlickRegistration | ConvertTo-Json -Compress) -join '') -cne ($initialRegistration -join '')) {
        throw 'Update reinstallation changed the registered AppId or installation path.'
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $restartedFlick = @()
    while ($restartedFlick.Count -eq 0) {
        $restartedFlick = @(Get-TestFlickProcess)
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Update Setup did not restart the installed Flick executable.' }
        if ($restartedFlick.Count -eq 0) { Start-Sleep -Milliseconds 200 }
    }
    if ($restartedFlick.Count -ne 1) { throw 'Update started more than one Flick process.' }
    Start-Sleep -Seconds 2
    if ($restartedFlick[0].HasExited) { throw 'Restarted Flick exited immediately.' }
    $hostRelativePath = 'tools/obs/runtime/bin/64bit/LocalClip.CaptureHost.exe'
    $installedHost = Join-Path $installDirectory $hostRelativePath
    $packagedHost = Join-Path $packageDirectory $hostRelativePath
    if (-not (Test-Path -LiteralPath $installedHost -PathType Leaf) -or
        (Get-FileHash -LiteralPath $installedHost -Algorithm SHA256).Hash -cne
        (Get-FileHash -LiteralPath $packagedHost -Algorithm SHA256).Hash) {
        throw 'Installed capture helper does not match the reviewed portable package.'
    }
    & (Join-Path $PSScriptRoot 'check_capture_host_runtime.ps1') `
        -HostPath $installedHost -EvidencePath (Join-Path $evidenceRoot 'installed-capture-runtime.json')
    Assert-DataPreserved
    $testPassed = $true
    Write-Host 'PASS: per-user install, exact-parent update wait, same AppId, package hash, data retention and Flick restart.'
    Write-Host 'This is installer lifecycle evidence only; no GPU capture or physical Windows acceptance is asserted.'
}
catch { $failureReason = $_.Exception.Message; throw }
finally {
    $cleanupErrors = [Collections.Generic.List[string]]::new()
    # Stop only processes launched by this fixture. Never stop another Flick copy.
    foreach ($installerProcess in $startedInstallers) {
        try { if (-not $installerProcess.HasExited) { $installerProcess.Kill($true); $null = $installerProcess.WaitForExit(10000) } }
        catch { $cleanupErrors.Add("Could not stop test Setup: $($_.Exception.Message)") }
    }
    if ($null -ne $surrogate) {
        try { if (-not $surrogate.HasExited) { $surrogate.Kill(); $null = $surrogate.WaitForExit(10000) } }
        catch { $cleanupErrors.Add("Could not stop surrogate parent: $($_.Exception.Message)") }
    }
    try {
        foreach ($appProcess in @(Get-TestFlickProcess)) {
            $null = $appProcess.CloseMainWindow()
            # Flick's close button normally minimizes to tray. CI teardown may
            # terminate only this fixture's exact executable after a grace period.
            if (-not $appProcess.WaitForExit(3000)) { $appProcess.Kill($true); $null = $appProcess.WaitForExit(10000) }
        }
        if (@(Get-TestFlickProcess).Count -ne 0) {
            throw 'Test Flick is still active; refusing to uninstall its running files.'
        }
        $testUninstaller = Join-Path $installDirectory 'unins000.exe'
        if (Test-Path -LiteralPath $testUninstaller -PathType Leaf) {
            $uninstallProcess = Start-Process -FilePath $testUninstaller -PassThru -ArgumentList @(
                '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$uninstallLog`"")
            if (-not $uninstallProcess.WaitForExit(60000) -or $uninstallProcess.ExitCode -ne 0) {
                throw 'Fixture uninstallation did not complete successfully.'
            }
            Assert-DataPreserved
            $deadline = [DateTime]::UtcNow.AddSeconds(10)
            while (@(Get-FlickRegistration).Count -ne 0 -and [DateTime]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 200
            }
            if (@(Get-FlickRegistration).Count -ne 0) { throw 'Fixture uninstall left the test AppId registered.' }
            if (Test-Path -LiteralPath $installedExe) { throw 'Fixture uninstall left the installed Flick.exe behind.' }
        }
    }
    catch { $cleanupErrors.Add($_.Exception.Message) }
    # Never recursively delete profile data or reset settings. Remove this one
    # known fixture file; installer logs stay in dist/installer-smoke for upload.
    try { if (Test-Path -LiteralPath $sentinelFile) { Remove-Item -LiteralPath $sentinelFile -Force } }
    catch { $cleanupErrors.Add("Could not remove fixture sentinel: $($_.Exception.Message)") }
    foreach ($message in $cleanupErrors) { Write-Warning $message }
    $zipFile = $packageDirectory + '.zip'
    [pscustomobject]@{
        Result = if ($testPassed -and $cleanupErrors.Count -eq 0) { 'PASS' } else { 'FAIL' }
        FailureReason = $failureReason
        CleanupErrors = @($cleanupErrors.ToArray())
        Version = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'release/preview-version.txt') -Raw).Trim()
        SourceCommit = $env:GITHUB_SHA
        SetupSha256 = (Get-FileHash -LiteralPath $setupFile -Algorithm SHA256).Hash
        PortableZipSha256 = if (Test-Path -LiteralPath $zipFile) { (Get-FileHash -LiteralPath $zipFile -Algorithm SHA256).Hash } else { $null }
        InstalledExeSha256 = $packageHash
        BaselineSetupSha256 = if ($baselineFile) { (Get-FileHash -LiteralPath $baselineFile -Algorithm SHA256).Hash } else { $null }
        BaselineInstalledExeSha256 = $baselineInstalledHash
        BaselineVersion = $baselineVersion
        UpgradedFromPreviousVersion = [bool]($testPassed -and $baselineFile)
        ParentPid = if ($null -ne $surrogate) { $surrogate.Id } else { $null }
        VerifiedParentWaitSeconds = if ($null -ne $waitTimer) { [Math]::Round($waitTimer.Elapsed.TotalSeconds, 2) } else { $null }
        RestartedFlickPid = if ($restartedFlick.Count -gt 0) { $restartedFlick[0].Id } else { $null }
        UnknownUserDataPreserved = $testPassed
        HardwareCaptureQualified = $false
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'result.json') -Encoding utf8
    if ($testPassed -and $cleanupErrors.Count -ne 0) { throw 'Installer smoke passed, but fixture cleanup failed.' }
}
