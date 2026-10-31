# Read or restore only entries from the exact hash-verified upstream archive.
# Extra local files, including the separately compiled capture host, are preserved.
function Assert-FlickObsRegularPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'OBS runtime paths must not traverse reparse entries.'
            }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Invoke-FlickObsArchive([string]$ArchivePath, [string]$ExpectedSha256,
    [string]$RuntimeRoot, [switch]$Restore) {
    if ($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Missing reviewed OBS archive SHA-256.' }
    if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
        throw 'Verified OBS upstream archive is missing. Run scripts/setup_tools.ps1.'
    }
    Add-Type -AssemblyName System.IO.Compression
    $runtime = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    Assert-FlickObsRegularPath $runtime
    # Hold one read-only handle across hashing and ZIP reads: verification must
    # not reopen a possibly replaced archive after establishing its digest.
    $stream = [IO.File]::Open($ArchivePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $digest = [Security.Cryptography.SHA256]::Create()
    $archive = $null
    try {
        $actual = [BitConverter]::ToString($digest.ComputeHash($stream)).Replace('-', '')
        if ($actual -ine $ExpectedSha256) { throw 'OBS archive SHA-256 mismatch; refuse unverified runtime bytes.' }
        $stream.Position = 0
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $files = [Collections.Generic.List[object]]::new()
        foreach ($entry in $archive.Entries) {
            if (-not $entry.Name) { continue }
            $relative = $entry.FullName.Replace('\', '/')
            if ($relative -match '(^/|:|(^|/)\.\.(/|$))' -or [IO.Path]::IsPathRooted($relative)) {
                throw 'Upstream OBS archive contains an invalid relative file path.'
            }
            $path = [IO.Path]::GetFullPath((Join-Path $runtime $relative))
            if (-not $path.StartsWith($runtime + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
                -not $seen.Add($path) -or $relative -ieq 'bin/64bit/LocalClip.CaptureHost.exe') {
                throw 'Upstream OBS archive has an ambiguous path or conflicts with the Flick-owned capture helper.'
            }
            Assert-FlickObsRegularPath $path
            $files.Add([pscustomobject]@{ Entry = $entry; Path = $path; Relative = $relative })
        }
        if ($files.Count -eq 0) { throw 'The verified OBS archive contains no files.' }
        foreach ($file in $files) {
            $source = $file.Entry.Open()
            try {
                if ($Restore) {
                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file.Path)) | Out-Null
                    $destination = [IO.File]::Open($file.Path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
                    try { $source.CopyTo($destination) } finally { $destination.Dispose() }
                }
                else {
                    if (-not [IO.File]::Exists($file.Path)) { throw "Missing pinned OBS payload: $($file.Relative)" }
                    $expected = [BitConverter]::ToString($digest.ComputeHash($source))
                    $disk = [IO.File]::Open($file.Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
                    try { $actual = [BitConverter]::ToString($digest.ComputeHash($disk)) } finally { $disk.Dispose() }
                    if ($actual -cne $expected) { throw "Pinned OBS payload hash mismatch: $($file.Relative)" }
                }
            }
            finally { $source.Dispose() }
        }
        if (-not $Restore) {
            $nativeHost = [IO.Path]::GetFullPath((Join-Path $runtime 'bin/64bit/LocalClip.CaptureHost.exe'))
            $pending = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
            $pending.Push([IO.DirectoryInfo]::new($runtime))
            while ($pending.Count -gt 0) {
                foreach ($entry in $pending.Pop().GetFileSystemInfos()) {
                    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                        throw 'OBS runtime contains a reparse entry; retain it for inspection.'
                    }
                    if ($entry -is [IO.DirectoryInfo]) { $pending.Push($entry); continue }
                    if ($entry.Extension -in @('.exe', '.dll') -and -not $seen.Contains($entry.FullName) -and
                        -not $entry.FullName.Equals($nativeHost, [StringComparison]::OrdinalIgnoreCase)) {
                        throw "Unverified extra OBS executable: $($entry.FullName). It was preserved; move it aside before packaging."
                    }
                }
            }
        }
        return $files.Count
    }
    finally {
        if ($null -ne $archive) { $archive.Dispose() }
        $digest.Dispose()
        $stream.Dispose()
    }
}
