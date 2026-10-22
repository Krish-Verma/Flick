<#
.SYNOPSIS
Refuse official/public distribution when any required Windows code is unsigned.
.DESCRIPTION
This is a security gate, not an AV bypass or a simulated SAC pass. For
Smart App Control, also test all code paths on a separate Windows 11 device
with SAC enforcement enabled and inspect CodeIntegrity/Operational events.
#>
param(
    [Parameter(Mandatory=$true)][string]$PackageRoot,
    [string]$InstallerPath = "",
    [string]$InstalledUninstallerPath = ""
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PackageRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "No app package: $root" }
$firstParty = @((Join-Path $root 'Flick.exe'),
    (Join-Path $root 'tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe'))
$files = @(Get-ChildItem -LiteralPath $root -File -Recurse |
    Where-Object { $_.Extension -in @('.exe','.dll') })
if ($files.Count -eq 0) { throw "No Windows executables to inspect" }
$issues = [System.Collections.Generic.List[string]]::new()
$publisher = ''
foreach ($file in $files) {
    $sig = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate -eq $null) {
        $issues.Add("$($file.FullName) : $($sig.Status)")
    }
}
foreach ($path in $firstParty) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $issues.Add("Missing first-party binary $path")
        continue
    }
    $sig = Get-AuthenticodeSignature -LiteralPath $path
    if ($sig.Status -ne 'Valid' -or $sig.TimeStamperCertificate -eq $null) {
        $issues.Add("Flick-owned executable unsigned, invalid or not timestamped: $path")
        continue
    }
    if (-not $publisher) { $publisher = $sig.SignerCertificate.Thumbprint }
    elseif ($publisher -ne $sig.SignerCertificate.Thumbprint) {
        $issues.Add("Flick executable publisher thumbprint mismatch: $path")
    }
}
foreach ($path in @($InstallerPath,$InstalledUninstallerPath)) {
    if ([string]::IsNullOrWhiteSpace($path)) { continue }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $issues.Add("Missing Setup/Uninstall binary: $path")
        continue
    }
    $sig = Get-AuthenticodeSignature -LiteralPath $path
    if ($sig.Status -ne 'Valid' -or $sig.TimeStamperCertificate -eq $null) {
        $issues.Add("Setup/Uninstall not Windows-trusted/timestamped: $path")
    }
    elseif ($publisher -and $sig.SignerCertificate.Thumbprint -ne $publisher) {
        $issues.Add("Setup/Uninstall publisher does not match Flick app: $path")
    }
}
if ($issues.Count) {
    foreach ($item in $issues) { Write-Error $item -ErrorAction Continue }
    throw "BLOCK PUBLIC RELEASE: $($issues.Count) executable signatures failed. Do not bypass Smart App Control or mark the release signed."
}
Write-Host "Authenticode signatures verified for $($files.Count) app EXE/DLL files and specified Setup/Uninstall targets." -ForegroundColor Green
Write-Host "Smart App Control on-device enforcement QA is still required; signature verification is not a guarantee of allowlisting." -ForegroundColor Yellow
