<# Builds only a separate, generated-tone qualification payload. Never launches audio/capture. #>
param([string]$PackageDirectory = 'dist/Flick-v1.0.12-win-x64')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') { throw 'Build the qualification artifact in disposable Windows CI only.' }
$root = (Get-Item -LiteralPath $PSScriptRoot).Parent.FullName
function Assert-CleanSource {
    git -C $root diff --quiet HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Qualification provenance refuses modified source.' }
    $untracked = @(git -C $root ls-files --others --exclude-standard -- src tools/Flick.AudioQualification scripts .github)
    if ($LASTEXITCODE -ne 0 -or $untracked.Count -ne 0) { throw 'Qualification provenance refuses untracked executable inputs.' }
}
Assert-CleanSource
$package = [IO.Path]::GetFullPath((Join-Path $root $PackageDirectory))
& (Join-Path $PSScriptRoot 'check_obs_runtime.ps1') -RuntimeRoot (Join-Path $package 'tools/obs/runtime')
& (Join-Path $PSScriptRoot 'check_ffmpeg_runtime.ps1') -RuntimeRoot (Join-Path $package 'tools/ffmpeg')
$helper = Join-Path $package 'tools/obs/runtime/bin/64bit/LocalClip.CaptureHost.exe'
if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) { throw 'Matching native capture helper is missing.' }
$source = (git -C $root rev-parse HEAD).Trim()
$tree = (git -C $root rev-parse 'HEAD^{tree}').Trim()
if ($source -notmatch '^[0-9a-f]{40}$' -or $tree -notmatch '^[0-9a-f]{40}$') { throw 'Invalid source provenance.' }
$stage = Join-Path $root ('dist/audio-qualification-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
dotnet publish (Join-Path $root 'tools/Flick.AudioQualification/Flick.AudioQualification.csproj') -c Release `
    -r win-x64 --self-contained true --warnaserror -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Audio qualification build failed.' }
Assert-CleanSource
Copy-Item -LiteralPath (Join-Path $package 'tools') -Destination (Join-Path $stage 'tools') -Recurse
foreach ($name in @('LICENSE','LICENSE-NOTICE.md','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $stage $name) }
Copy-Item -LiteralPath (Join-Path $root 'docs/product-phase/C-AUDIO-SEPARATION.md') -Destination (Join-Path $stage 'QUALIFICATION.md')
$files = @(Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path=$_.FullName.Substring($stage.Length+1).Replace('\','/'); Bytes=$_.Length; Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
[ordered]@{ SchemaVersion=1; SourceCommit=$source; SourceTree=$tree; Purpose='ThreeIndependentGeneratedProcessTones';
    AudioCaptureExecuted=$false; Microphone=$false; DeviceLoopback=$false; Files=$files } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'qualification-manifest.json') -Encoding UTF8
$zip = $stage + '.zip'
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $stage -Force).FullName -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
Write-Host "Separate audio qualification payload: $zip"
Write-Host "SHA256: $hash"
Write-Host 'Assembly only: no recording, playback, endpoint access, installed Flick or user settings were touched.'
