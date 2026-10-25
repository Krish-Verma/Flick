<# Build a separate qualification artifact; never install or launch capture. #>
param([string]$PackageDirectory = 'dist/Flick-v1.0.8-win-x64')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Get-Item -LiteralPath $PSScriptRoot).Parent.FullName
function Assert-SourceClean {
    git -C $root diff --quiet HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Qualification provenance refuses modified tracked source.' }
    $untracked = @(git -C $root ls-files --others --exclude-standard -- src tools/Flick.RecordingHarness scripts .github)
    if ($LASTEXITCODE -ne 0 -or $untracked.Count -ne 0) { throw 'Qualification provenance refuses untracked executable inputs.' }
}
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'Qualification payload assembly is restricted to Windows CI; use its hash-verified artifact on the test PC.'
}
Assert-SourceClean
$package = [IO.Path]::GetFullPath((Join-Path $root $PackageDirectory))
& (Join-Path $PSScriptRoot 'check_obs_runtime.ps1') -RuntimeRoot (Join-Path $package 'tools/obs/runtime')
& (Join-Path $PSScriptRoot 'check_ffmpeg_runtime.ps1') -RuntimeRoot (Join-Path $package 'tools/ffmpeg')
if (-not (Test-Path -LiteralPath (Join-Path $package 'tools/obs/runtime/bin/64bit/LocalClip.CaptureHost.exe') -PathType Leaf)) {
    throw 'The matching CI-compiled capture helper is missing.'
}
$commit = (git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot determine source commit.' }
$tree = (git -C $root rev-parse 'HEAD^{tree}').Trim()
if ($LASTEXITCODE -ne 0 -or $tree -notmatch '^[0-9a-f]{40}$') { throw 'Cannot determine source tree.' }
$stage = Join-Path $root ('dist/recording-qualification-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
dotnet publish (Join-Path $root 'tools/Flick.RecordingHarness/Flick.RecordingHarness.csproj') -c Release `
    -r win-x64 --self-contained true --warnaserror -o (Join-Path $stage 'harness')
if ($LASTEXITCODE -ne 0) { throw 'Qualification harness publish failed.' }
Assert-SourceClean
Copy-Item -LiteralPath (Join-Path $package 'tools') -Destination (Join-Path $stage 'tools') -Recurse
$scripts = Join-Path $stage 'scripts'
[IO.Directory]::CreateDirectory($scripts) | Out-Null
foreach ($name in @('run_recording_qualification.ps1','RecordingHarnessSession.cs')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $scripts $name)
}
foreach ($name in @('LICENSE','LICENSE-NOTICE.md','THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $stage $name)
}
Copy-Item -LiteralPath (Join-Path $root 'docs/product-phase/A4-RECORDING-QUALIFICATION.md') -Destination (Join-Path $stage 'QUALIFICATION.md')
$files = @(Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path=$_.FullName.Substring($stage.Length+1).Replace('\','/'); Bytes=$_.Length;
        Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
[ordered]@{ SchemaVersion=1; SourceCommit=$commit; SourceTree=$tree;
    Purpose='SyntheticWindowNoAudio'; WindowsCaptureExecuted=$false; Files=$files } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'qualification-manifest.json') -Encoding UTF8
$zip = $stage + '.zip'
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $stage -Force).FullName -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
Write-Host "Separate qualification payload: $zip"
Write-Host "SHA256: $hash"
Write-Host 'No capture, installation, profile access or physical hardware acceptance occurred during assembly.'
