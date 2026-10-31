# Offline owned-archive regression for the real OBS byte-verification helper.
# No upstream download, executable launch, device, user profile or existing runtime.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'FlickObsArchive.ps1')
Add-Type -AssemblyName System.IO.Compression
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('Flick-Obs-Integrity-' + [Guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $fixture) { throw 'Runtime regression needs a fresh owned fixture.' }
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$marker = Join-Path $fixture '.flick-owned-runtime-fixture'
[IO.File]::WriteAllText($marker, 'Owned archive metadata and bytes only')
try {
    $archivePath = Join-Path $fixture 'upstream.zip'
    $runtime = Join-Path $fixture 'runtime'
    $dll = Join-Path $runtime 'bin/64bit/obs.dll'
    $native = Join-Path $runtime 'bin/64bit/LocalClip.CaptureHost.exe'
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($native)) | Out-Null
    [IO.File]::WriteAllText($native, 'Owned compiled-helper sentinel, not an executable')
    $unrelated = Join-Path $runtime 'unrelated-owner-notes.keep'
    [IO.File]::WriteAllText($unrelated, 'Preserve unrelated contents')
    # Minimal PE-shaped fixture, not usable code. Byte128 is outside its header.
    $payload = [byte[]]::new(256)
    $payload[0] = 0x4d; $payload[1] = 0x5a; $payload[0x3c] = 64
    $payload[64] = 0x50; $payload[65] = 0x45; $payload[68] = 0x64; $payload[69] = 0x86; $payload[128] = 7
    $stream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $entry = $archive.CreateEntry('bin/64bit/obs.dll').Open()
        try { $entry.Write($payload, 0, $payload.Length) } finally { $entry.Dispose() }
    }
    finally { $archive.Dispose() }
    $expected = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    $null = Invoke-FlickObsArchive $archivePath $expected $runtime -Restore
    $count = Invoke-FlickObsArchive $archivePath $expected $runtime
    if ($count -ne 1) { throw 'Fixture inventory was not verified.' }
    $drift = [IO.File]::ReadAllBytes($dll); $drift[128] = $drift[128] -bxor 1
    [IO.File]::WriteAllBytes($dll, $drift)
    $rejected = $false
    try { $null = Invoke-FlickObsArchive $archivePath $expected $runtime }
    catch { if ($_.Exception.Message -notmatch 'payload hash mismatch') { throw }; $rejected = $true }
    if (-not $rejected) { throw 'A changed nonheader byte was accepted as a pinned OBS file.' }
    $null = Invoke-FlickObsArchive $archivePath $expected $runtime -Restore
    $null = Invoke-FlickObsArchive $archivePath $expected $runtime
    if ([IO.File]::ReadAllBytes($dll)[128] -ne 7 -or
        [IO.File]::ReadAllText($native) -cne 'Owned compiled-helper sentinel, not an executable' -or
        [IO.File]::ReadAllText($unrelated) -cne 'Preserve unrelated contents') {
        throw 'Verified restore did not repair drift or changed an unrelated local file.'
    }
    [IO.File]::Delete($dll)
    $rejected = $false
    try { $null = Invoke-FlickObsArchive $archivePath $expected $runtime }
    catch { if ($_.Exception.Message -notmatch 'Missing pinned OBS payload') { throw }; $rejected = $true }
    if (-not $rejected) { throw 'An incomplete runtime was accepted.' }
    $null = Invoke-FlickObsArchive $archivePath $expected $runtime -Restore
    $null = Invoke-FlickObsArchive $archivePath $expected $runtime
    $extra = Join-Path $runtime 'stale-plugin.dll'
    [IO.File]::WriteAllText($extra, 'Unknown cached code must be refused, never silently deleted')
    $rejected = $false
    try { $null = Invoke-FlickObsArchive $archivePath $expected $runtime }
    catch { if ($_.Exception.Message -notmatch 'Unverified extra OBS executable') { throw }; $rejected = $true }
    if (-not $rejected -or -not [IO.File]::Exists($extra)) { throw 'Unverified extra code was accepted or purged.' }
    [IO.File]::Delete($extra) # This fixture owns precisely this test file.
    $badArchive = Join-Path $fixture 'changed.zip'
    $changed = [IO.File]::ReadAllBytes($archivePath); $changed[20] = $changed[20] -bxor 1
    [IO.File]::WriteAllBytes($badArchive, $changed)
    $before = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    $rejected = $false
    try { $null = Invoke-FlickObsArchive $badArchive $expected $runtime -Restore }
    catch { if ($_.Exception.Message -notmatch 'archive SHA-256 mismatch') { throw }; $rejected = $true }
    if (-not $rejected -or (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -cne $before) {
        throw 'An unverified source archive reached runtime restoration.'
    }
    [ordered]@{ Result = 'PASS'; NonheaderDriftRejected = $true; PinnedBytesRestored = $true;
        MissingPayloadRejectedAndRestored = $true;
        LocalNativeHostAndUnknownDataPreserved = $true; ExtraCodeRejectedWithoutDeletion = $true;
        ChangedArchiveRejectedBeforeWrite = $true; ActualObsRuntimeExecuted = $false } | ConvertTo-Json
}
finally { if ([IO.File]::Exists($marker)) { Remove-Item -LiteralPath $fixture -Recurse -Force } }
