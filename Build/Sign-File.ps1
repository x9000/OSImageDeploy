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

$certificateStore = [System.Security.Cryptography.X509Certificates.X509Store]::new(
    [System.Security.Cryptography.X509Certificates.StoreName]::My,
    [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)

try
{
    $certificateStore.Open(
        [System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)

    $certificate = $certificateStore.Certificates |
        Where-Object Thumbprint -EQ $normalizedThumbprint |
        Select-Object -First 1
}
finally
{
    $certificateStore.Close()
}

if ($null -eq $certificate)
{
    throw "Code-signing certificate '$normalizedThumbprint' was not found in the current user's Personal certificate store."
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
