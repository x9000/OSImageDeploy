[CmdletBinding()]
param
(
	[Parameter(Mandatory)]
	[string] $CertificateThumbprint,

	[string] $InstallerPath =
		(Join-Path $PSScriptRoot '..\OSImageDeploy.Installer\bin\Release\OSImageDeploySuite.msi'),

	[string] $PublishRoot =
		(Join-Path $env:TEMP 'OSImageDeploy.Installer\Release\publish'),

	[string] $MsiValPath =
		'C:\Program Files (x86)\MsiVal2\MsiVal2.exe',

	[string] $EvaluationPath =
		'C:\Program Files (x86)\MsiVal2\darice.cub'
)

$ErrorActionPreference = 'Stop'
$normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').Trim()
$resolvedInstallerPath =
	[System.IO.Path]::GetFullPath($InstallerPath)

$filesToVerify = @(
	$resolvedInstallerPath
	(Join-Path $PublishRoot 'Main\OSImageDeploy.exe')
	(Join-Path $PublishRoot 'WinPeClient\OSImageDeployClient.exe')
	(Join-Path $PublishRoot 'Service\OSImageDeploy.Contracts.dll')
	(Join-Path $PublishRoot 'Service\OSImageDeploy.Engine.dll')
	(Join-Path $PublishRoot 'Service\OSImageDeploy.Platform.Windows.dll')
	(Join-Path $PublishRoot 'Service\OSImageDeploy.Service.dll')
	(Join-Path $PublishRoot 'Service\OSImageDeploy.Service.exe')
	(Join-Path $PublishRoot 'Service\OSImageDeploy.Transport.Grpc.dll')
	(Join-Path $PublishRoot 'Service\Utilities.dll')
	(Join-Path $PublishRoot 'Service\WinPEClient\OSImageDeployClient.exe')
)

foreach ($filePath in $filesToVerify)
{
	if (-not (Test-Path -LiteralPath $filePath -PathType Leaf))
	{
		throw "The expected signed file '$filePath' does not exist."
	}

	$signature = Get-AuthenticodeSignature -LiteralPath $filePath

	if ($signature.Status -ne
		[System.Management.Automation.SignatureStatus]::Valid)
	{
		throw "The Authenticode signature on '$filePath' is not valid: $($signature.StatusMessage)"
	}

	if ($signature.SignerCertificate.Thumbprint -ne $normalizedThumbprint)
	{
		throw "The Authenticode signature on '$filePath' was created by an unexpected certificate."
	}

	if ($null -eq $signature.TimeStamperCertificate)
	{
		throw "The Authenticode signature on '$filePath' is not timestamped."
	}

	Write-Host "PASS: Valid timestamped signature: $filePath"
}

if (-not (Test-Path -LiteralPath $MsiValPath -PathType Leaf))
{
	throw "Microsoft MsiVal2 was not found at '$MsiValPath'. Install the Windows SDK MsiVal2 package or supply -MsiValPath."
}

if (-not (Test-Path -LiteralPath $EvaluationPath -PathType Leaf))
{
	throw "The MSI validation rules were not found at '$EvaluationPath'."
}

$validationDirectory = Join-Path `
	([System.IO.Path]::GetTempPath()) `
	("OSImageDeploy.MsiValidation\" + [Guid]::NewGuid().ToString('N'))
$validationInstaller = Join-Path `
	$validationDirectory `
	'OSImageDeploySuite.msi'
$validationLog = Join-Path `
	$validationDirectory `
	'MsiVal2.log'

try
{
	New-Item `
		-ItemType Directory `
		-Path $validationDirectory `
		-Force | Out-Null
	Copy-Item `
		-LiteralPath $resolvedInstallerPath `
		-Destination $validationInstaller

	& $MsiValPath `
		$validationInstaller `
		$EvaluationPath `
		-l $validationLog `
		-f

	if ($LASTEXITCODE -ne 0)
	{
		if (Test-Path -LiteralPath $validationLog)
		{
			Get-Content -LiteralPath $validationLog |
				Write-Error
		}

		throw "MsiVal2 reported validation failures with exit code $LASTEXITCODE."
	}

	if ((Test-Path -LiteralPath $validationLog) -and
		(Get-Item -LiteralPath $validationLog).Length -gt 0)
	{
		$validationFindings =
			Get-Content -LiteralPath $validationLog |
			Out-String

		throw "MsiVal2 reported validation findings:`n$validationFindings"
	}

	Write-Host 'PASS: Microsoft MsiVal2 ICE validation completed without findings.'
}
finally
{
	if (Test-Path -LiteralPath $validationDirectory)
	{
		Remove-Item `
			-LiteralPath $validationDirectory `
			-Recurse `
			-Force
	}
}
