[CmdletBinding()]
param(
	[string] $PayloadDirectory = (Join-Path $PSScriptRoot '..\OSImageDeploy')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$payloadNames = @(
	'DellPEDrivers.zip',
	'HPPEDrivers.zip'
)

$resolvedPayloadDirectory = [System.IO.Path]::GetFullPath($PayloadDirectory)

foreach ($payloadName in $payloadNames)
{
	$payloadPath = Join-Path $resolvedPayloadDirectory $payloadName

	if (-not (Test-Path -LiteralPath $payloadPath -PathType Leaf))
	{
		throw "Required OEM driver payload was not found: $payloadPath"
	}

	$archive = $null

	try
	{
		$archive = [System.IO.Compression.ZipFile]::OpenRead($payloadPath)
		$entryNames = @($archive.Entries | ForEach-Object FullName)

		if ($entryNames -contains 'CI-PLACEHOLDER.txt')
		{
			throw "CI placeholder cannot be used in a release build: $payloadPath"
		}

		$driverCount = @($entryNames | Where-Object { $_.EndsWith('.inf', [System.StringComparison]::OrdinalIgnoreCase) }).Count

		if ($driverCount -eq 0)
		{
			throw "OEM driver payload contains no INF files: $payloadPath"
		}
	}
	catch [System.IO.InvalidDataException]
	{
		throw "OEM driver payload is not a readable ZIP archive: $payloadPath"
	}
	finally
	{
		if ($null -ne $archive)
		{
			$archive.Dispose()
		}
	}

	$hash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash
	$length = (Get-Item -LiteralPath $payloadPath).Length
	Write-Host "${payloadName}: $driverCount INF files, $length bytes, SHA-256 $hash"
}

Write-Host 'OEM driver payload validation passed.'
