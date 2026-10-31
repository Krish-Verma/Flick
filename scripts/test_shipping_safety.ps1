# Source policy plus owned-filesystem regression. No installer, build, registry,
# recording, user profile, security setting, network or existing dist is touched.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = (Get-Item -LiteralPath $PSScriptRoot).Parent.FullName
. (Join-Path $PSScriptRoot 'FlickPackageStaging.ps1')
$installer = Get-Content -LiteralPath (Join-Path $repository 'installer/Flick.iss') -Raw
$packaging = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build_package.ps1') -Raw
if ($installer -match '\bDelTree\s*\(') { throw 'Uninstall must not recursively purge potentially user-owned cache contents.' }
if ($installer -notmatch 'if not UninstallSilent then' -or $installer -notmatch 'if not RemoveFlickProfile then\s+Exit;') {
    throw 'Metadata cleanup must remain interactive and separately opted in.'
}
$metadata = @('settings.json','settings.json.tmp','clips_index.json','clips_index.json.tmp',
    'game-replay-state.json','connected_sources.json','connected_sources.json.tmp')
$cacheNames = @('Thumbnails','Temp','PreviewCache')
$deleted = @([regex]::Matches($installer, "DeleteFile\(ProfileDir \+ '\\(?<name>[^']+)'\);") |
    ForEach-Object { $_.Groups['name'].Value })
$emptyOnly = @([regex]::Matches($installer, "RemoveDir\(ProfileDir \+ '\\(?<name>[^']+)'\);") |
    ForEach-Object { $_.Groups['name'].Value })
if ($deleted.Count -ne $metadata.Count -or @(Compare-Object $metadata $deleted).Count -ne 0 -or
    ([regex]::Matches($installer, '\bDeleteFile\s*\(')).Count -ne $metadata.Count -or
    $emptyOnly.Count -ne $cacheNames.Count -or @(Compare-Object $cacheNames $emptyOnly).Count -ne 0 -or
    ([regex]::Matches($installer, '\bRemoveDir\s*\(')).Count -ne $cacheNames.Count + 1) {
    throw 'Uninstall cleanup deviated from the explicit metadata and empty-directory-only policy.'
}
if ($packaging -notmatch 'dotnet publish[^\r\n]+-o \$publishStage\.Path' -or
    $packaging -notmatch 'Copy-FlickPublishedApplication \$publishStage \$PackageDir' -or
    $packaging -notmatch 'finally\s*\{\s*Remove-FlickPublishStage \$publishStage\s*\}') {
    throw 'Packaging must use and clean its fresh owned publish stage.'
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('Flick-Shipping-Safety-' + [Guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $fixtureRoot) { throw 'Safety regression requires a new owned fixture.' }
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$marker = Join-Path $fixtureRoot '.flick-owned-shipping-fixture'
[IO.File]::WriteAllText($marker, 'Owned source/filesystem regression only')
try {
    $fixtureProfile = Join-Path $fixtureRoot 'LocalClip'
    [IO.Directory]::CreateDirectory($fixtureProfile) | Out-Null
    $sentinels = [Collections.Generic.List[string]]::new()
    foreach ($cache in $cacheNames) {
        foreach ($role in @('Originals','Exports','Imported')) {
            $folder = Join-Path (Join-Path $fixtureProfile $cache) $role
            [IO.Directory]::CreateDirectory($folder) | Out-Null
            $file = Join-Path $folder 'owner-video.mkv'
            [IO.File]::WriteAllText($file, 'Owned preservation sentinel, not a real video')
            $sentinels.Add($file)
        }
    }
    foreach ($name in $metadata) { [IO.File]::WriteAllText((Join-Path $fixtureProfile $name), '{}') }
    # These are legal configured originals/exports and imported roots beneath
    # the cache names; no JSON parser or guessed media ownership is needed.
    [ordered]@{ OriginalsFolder = (Join-Path $fixtureProfile 'Temp/Originals');
        ExportsFolder = (Join-Path $fixtureProfile 'PreviewCache/Exports') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixtureProfile 'settings.json') -Encoding UTF8
    $unknown = Join-Path $fixtureProfile 'owner-unknown.keep'
    [IO.File]::WriteAllText($unknown, 'Preserve unknown profile contents')

    # Apply the production installer's literal allowlisted filesystem actions
    # to the owned profile. This does not claim interactive Inno execution.
    foreach ($name in $deleted) { [IO.File]::Delete((Join-Path $fixtureProfile $name)) }
    foreach ($name in $emptyOnly) {
        try { [IO.Directory]::Delete((Join-Path $fixtureProfile $name), $false) }
        catch [IO.IOException] { } # Inno RemoveDir also leaves a nonempty directory.
    }
    foreach ($file in $sentinels) {
        if (-not [IO.File]::Exists($file) -or [IO.File]::ReadAllText($file) -cne 'Owned preservation sentinel, not a real video') {
            throw 'Metadata/cache cleanup changed configured or imported media.'
        }
    }
    if (-not [IO.File]::Exists($unknown)) { throw 'Unknown profile contents were removed.' }

    $dist = Join-Path $fixtureRoot 'dist'
    $legacy = Join-Path $dist 'app'
    $package = Join-Path $dist 'package'
    [IO.Directory]::CreateDirectory($legacy) | Out-Null
    [IO.Directory]::CreateDirectory($package) | Out-Null
    $obsolete = Join-Path $legacy 'obsolete-fixture.dll'
    [IO.File]::WriteAllText($obsolete, 'Old publish output must remain untouched and must not ship')
    $stage = New-FlickPublishStage $dist
    try {
        [IO.File]::WriteAllText((Join-Path $stage.Path 'Flick.exe'), 'Current publish sentinel, not an executable')
        Copy-FlickPublishedApplication $stage $package
        if ([IO.File]::Exists((Join-Path $package 'obsolete-fixture.dll')) -or
            [IO.File]::Exists((Join-Path $package '.flick-owned-publish-stage')) -or
            [IO.File]::ReadAllText((Join-Path $package 'Flick.exe')) -cne 'Current publish sentinel, not an executable') {
            throw 'Fresh package contains stale staging contents or omitted current output.'
        }
        $rejected = $false
        try { Remove-FlickPublishStage ([pscustomobject]@{ Path = $legacy; DistRoot = $dist; Token = $stage.Token }) }
        catch { $rejected = $true }
        if (-not $rejected -or -not [IO.File]::Exists($obsolete)) { throw 'Cleanup accepted an unrelated legacy directory.' }
    }
    finally { Remove-FlickPublishStage $stage }
    if ((Test-Path -LiteralPath $stage.Path) -or -not [IO.File]::Exists($obsolete)) {
        throw 'Cleanup failed to remove its owned stage or changed unrelated old output.'
    }
    [ordered]@{ Result = 'PASS'; PreservedMediaSentinels = $sentinels.Count;
        StalePublishExcluded = $true; ExistingPublishUntouched = $true;
        ActualInteractiveUninstallExecuted = $false; ActualBuildExecuted = $false } | ConvertTo-Json
}
finally {
    if ([IO.File]::Exists($marker)) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
