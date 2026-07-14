[CmdletBinding()]
param
(
    [Parameter(Mandatory)]
    [string] $FilePath,

    [Parameter(Mandatory)]
    [string] $CertificateThumbprint
)

$ErrorActionPreference = 'Stop'

$normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').Trim()

if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf))
{
    throw "The file '$FilePath' does not exist."
}

$certificate = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object Thumbprint -EQ $normalizedThumbprint |
    Select-Object -First 1

if ($null -eq $certificate)
{
    throw "Code-signing certificate '$normalizedThumbprint' was not found in Cert:\CurrentUser\My."
}

if (-not $certificate.HasPrivateKey)
{
    throw "The certificate '$normalizedThumbprint' does not have an accessible private key."
}

Write-Host "Signing: $FilePath"
Write-Host "Certificate: $($certificate.Subject)"
Write-Host "Thumbprint: $($certificate.Thumbprint)"

$signature = Set-AuthenticodeSignature `
    -LiteralPath $FilePath `
    -Certificate $certificate `
    -HashAlgorithm SHA256 `
    -TimestampServer 'http://timestamp.sectigo.com'

if ($null -eq $signature)
{
    throw "Set-AuthenticodeSignature did not return a signature result."
}

if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid)
{
    throw "Signing failed for '$FilePath': $($signature.Status) - $($signature.StatusMessage)"
}

Write-Host "Successfully signed: $FilePath"