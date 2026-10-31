# Shared by packaging and its filesystem regression. No build runs on import.
function Assert-FlickPublishStage($Stage) {
    $path = [IO.Path]::GetFullPath($Stage.Path)
    $parent = [IO.Path]::GetDirectoryName($path)
    if (-not $parent.Equals([IO.Path]::GetFullPath($Stage.DistRoot), [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($path) -notmatch '^\.flick-publish-[0-9a-f]{32}$' -or
        $Stage.Token -notmatch '^[0-9a-f]{32}$' -or
        -not [IO.File]::Exists((Join-Path $path '.flick-owned-publish-stage')) -or
        [IO.File]::ReadAllText((Join-Path $path '.flick-owned-publish-stage')) -cne $Stage.Token) {
        throw 'Publish staging ownership is unconfirmed; refusing to copy or remove it.'
    }
    if (([IO.File]::GetAttributes($path) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Publish staging must not be a reparse directory.'
    }
}

function New-FlickPublishStage([string]$DistRoot) {
    $dist = [IO.Path]::GetFullPath($DistRoot)
    [IO.Directory]::CreateDirectory($dist) | Out-Null
    $path = Join-Path $dist ('.flick-publish-' + [Guid]::NewGuid().ToString('N'))
    if (Test-Path -LiteralPath $path) { throw 'Publish staging must be a new directory.' }
    [IO.Directory]::CreateDirectory($path) | Out-Null
    $token = [Guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllText((Join-Path $path '.flick-owned-publish-stage'), $token)
    return [pscustomobject]@{ Path = $path; DistRoot = $dist; Token = $token }
}

function Copy-FlickPublishedApplication($Stage, [string]$Destination) {
    Assert-FlickPublishStage $Stage
    if (-not (Test-Path -LiteralPath (Join-Path $Stage.Path 'Flick.exe') -PathType Leaf)) {
        throw 'The fresh publish stage has no Flick.exe.'
    }
    $entries = @(Get-ChildItem -LiteralPath $Stage.Path -Force | Where-Object Name -ne '.flick-owned-publish-stage')
    Copy-Item -LiteralPath $entries.FullName -Destination $Destination -Recurse -Force
}

function Remove-FlickPublishStage($Stage) {
    Assert-FlickPublishStage $Stage
    # Inspect without following directory links before any recursive removal.
    $pending = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $pending.Push([IO.DirectoryInfo]::new($Stage.Path))
    while ($pending.Count -gt 0) {
        foreach ($entry in $pending.Pop().GetFileSystemInfos()) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Publish staging contains a reparse entry; retain it for inspection.'
            }
            if ($entry -is [IO.DirectoryInfo]) { $pending.Push($entry) }
        }
    }
    Remove-Item -LiteralPath $Stage.Path -Recurse -Force
}
