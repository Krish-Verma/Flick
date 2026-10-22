<#
.SYNOPSIS
Sign one Windows PE with Microsoft Artifact Signing (public-trust certificate).
.DESCRIPTION
Run only after provisioning a validated Public Trust Artifact Signing account
and authenticating on the signing runner (Azure CLI / OIDC). Never use a
self-signed test certificate or disable Windows Smart App Control.
#>
param([Parameter(Mandatory=$true)][string]$FilePath)
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($FilePath)
if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Missing code file: $target" }
$tool = [Environment]::GetEnvironmentVariable('FLICK_SIGNTOOL_PATH')
$dlib = [Environment]::GetEnvironmentVariable('FLICK_ARTIFACT_SIGNING_DLIB')
$metadata = [Environment]::GetEnvironmentVariable('FLICK_ARTIFACT_SIGNING_METADATA')
foreach ($path in @($tool,$dlib,$metadata)) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Trusted signing not configured. An Artifact Signing Public Trust account, Windows SDK SignTool, client Dlib and metadata file are required. No unsigned fallback is allowed."
    }
}
$config = Get-Content -LiteralPath $metadata -Raw | ConvertFrom-Json
if (-not $config.Endpoint.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase) -or
    [string]::IsNullOrWhiteSpace($config.CodeSigningAccountName) -or
    [string]::IsNullOrWhiteSpace($config.CertificateProfileName)) {
    throw "Artifact Signing Public Trust profile details are missing or invalid."
}
& $tool sign /v /fd SHA256 /tr 'http://timestamp.acs.microsoft.com' /td SHA256 /dlib $dlib /dmdf $metadata $target
if ($LASTEXITCODE -ne 0) { throw "Trusted SignTool signing failed ($LASTEXITCODE): $target" }
$signature = Get-AuthenticodeSignature -LiteralPath $target
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate -eq $null) {
    throw "Signed file did not verify as Windows-trusted: $target ($($signature.Status))"
}
if ($signature.TimeStamperCertificate -eq $null) {
    throw "No verified timestamp for $target; a short-lived Artifact Signing certificate requires timestamping."
}
Write-Host "Trusted Authenticode signature: $target" -ForegroundColor Green
