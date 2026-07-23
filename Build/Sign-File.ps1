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

$windowsKitsBin = Join-Path `
    ${env:ProgramFiles(x86)} `
    'Windows Kits\10\bin'

$signTool = Get-ChildItem `
    -LiteralPath $windowsKitsBin `
    -Recurse `
    -Filter 'signtool.exe' `
    -File `
    -ErrorAction SilentlyContinue |
    Where-Object DirectoryName -Like '*\x64' |
    Sort-Object `
        { [Version]$_.Directory.Parent.Name } `
        -Descending |
    Select-Object -First 1

if ($null -eq $signTool)
{
    throw "The Windows SDK x64 signing tool could not be found beneath '$windowsKitsBin'."
}

& $signTool.FullName `
    sign `
    /sha1 $normalizedThumbprint `
    /s My `
    /fd SHA256 `
    /tr 'http://timestamp.sectigo.com' `
    /td SHA256 `
    /v `
    $FilePath

if ($LASTEXITCODE -ne 0)
{
    throw "SignTool failed to sign '$FilePath' with exit code $LASTEXITCODE."
}

& $signTool.FullName verify /pa /v $FilePath

if ($LASTEXITCODE -ne 0)
{
    throw "SignTool could not verify the signature on '$FilePath'."
}

Write-Host "Successfully signed: $FilePath"
