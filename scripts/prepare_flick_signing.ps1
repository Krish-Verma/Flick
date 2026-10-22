<#
.SYNOPSIS
Configure a verified Windows SDK SignTool and Microsoft Artifact Signing dlib.
.DESCRIPTION
Prerequisites: Public Trust profile; Azure OIDC/CLI authorization; the official
Microsoft Artifact Signing Client Tools; Windows SDK SignTool >= 10.0.22621.
No private key, PFX, Azure secret, or authentication token is written here.
#>
param([string]$MetadataDirectory = "")
$ErrorActionPreference = 'Stop'
$root = (Get-Item $PSScriptRoot).Parent.FullName
if (-not $MetadataDirectory) { $MetadataDirectory = Join-Path $root 'dist\signing' }
foreach ($key in @('FLICK_SIGNING_ENDPOINT','FLICK_SIGNING_ACCOUNT','FLICK_SIGNING_PROFILE')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($key))) {
        throw "Public Trust Artifact Signing is not configured: missing $key. No signing credentials/certificate were provided."
    }
}
if ($env:FLICK_SIGNING_TRUST_MODEL -ne 'PublicTrust') {
    throw "Public releases require an Artifact Signing Public Trust certificate profile. A test/Private Trust profile is insufficient."
}
$endpoint = [Environment]::GetEnvironmentVariable('FLICK_SIGNING_ENDPOINT')
if ($endpoint -notmatch '^https://[a-z0-9-]+\.codesigning\.azure\.net/?$') {
    throw "Artifact Signing endpoint must match the validated Azure account region."
}
$tool = [Environment]::GetEnvironmentVariable('FLICK_SIGNTOOL_PATH')
if (-not $tool) {
    $kits = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)) 'Windows Kits\10\bin'
    $tool = Get-ChildItem -LiteralPath $kits -Filter 'signtool.exe' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '\\x64$' } |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
$dlib = [Environment]::GetEnvironmentVariable('FLICK_ARTIFACT_SIGNING_DLIB')
if (-not $dlib) {
    $search = @([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles),
                [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86))
    $dlib = foreach ($directory in $search) {
        Get-ChildItem -LiteralPath $directory -Recurse -Filter 'Azure.CodeSigning.Dlib.dll' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -match '\\x64$' } |
            Select-Object -First 1 -ExpandProperty FullName
    }
    $dlib = @($dlib) | Select-Object -First 1
}
foreach ($path in @($tool,$dlib)) {
    if (-not $path -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "No trusted SignTool or Azure Artifact Signing Client Tools dlib. Install the official Windows SDK and Microsoft client tools; do not download unknown binaries."
    }
}
$az = Get-Command 'az' -ErrorAction SilentlyContinue
if (-not $az) { throw "Azure CLI/OIDC login is not available to the signing runner." }
& $az.Source account show --output none
if ($LASTEXITCODE -ne 0) { throw "Azure identity not authenticated; no signing can proceed" }
[IO.Directory]::CreateDirectory($MetadataDirectory) | Out-Null
$metadata = Join-Path $MetadataDirectory 'metadata.json'
$config = [ordered]@{
    Endpoint = $endpoint.TrimEnd('/')
    CodeSigningAccountName = [Environment]::GetEnvironmentVariable('FLICK_SIGNING_ACCOUNT')
    CertificateProfileName = [Environment]::GetEnvironmentVariable('FLICK_SIGNING_PROFILE')
    CorrelationId = [Guid]::NewGuid().ToString('D')
}
($config | ConvertTo-Json -Depth 3) | Set-Content -LiteralPath $metadata -Encoding utf8
[Environment]::SetEnvironmentVariable('FLICK_SIGNTOOL_PATH',$tool,'Process')
[Environment]::SetEnvironmentVariable('FLICK_ARTIFACT_SIGNING_DLIB',$dlib,'Process')
[Environment]::SetEnvironmentVariable('FLICK_ARTIFACT_SIGNING_METADATA',$metadata,'Process')
if ($env:GITHUB_ENV) {
    Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8 -Value "FLICK_SIGNTOOL_PATH=$tool"
    Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8 -Value "FLICK_ARTIFACT_SIGNING_DLIB=$dlib"
    Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8 -Value "FLICK_ARTIFACT_SIGNING_METADATA=$metadata"
}
Write-Host "Artifact Signing Public Trust client configured (identity redacted). No source secrets or cert files were stored." -ForegroundColor Green
