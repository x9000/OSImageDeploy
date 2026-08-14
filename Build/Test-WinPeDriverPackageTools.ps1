[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$testRoot = Join-Path $env:TEMP (
	'OSImageDeploy-driver-package-tools-' + [guid]::NewGuid().ToString('N'))
$sourceDirectory = Join-Path $testRoot 'source'
$storeDirectory = Join-Path $testRoot 'store'

[System.IO.Directory]::CreateDirectory($sourceDirectory) | Out-Null
[System.IO.File]::WriteAllText(
	(Join-Path $sourceDirectory 'test.inf'),
	'[Version]')

try
{
	& (Join-Path $PSScriptRoot 'Import-WinPeDriverPackage.ps1') `
		-PackageId example-winpe `
		-DisplayName 'Example WinPE drivers' `
		-Manufacturer Example `
		-SourceVersion '1.0' `
		-SourcePageUrl 'https://example.com/drivers' `
		-SourceDirectory $sourceDirectory `
		-DestinationDirectory $storeDirectory

	$packageDirectory = Join-Path $storeDirectory 'example-winpe'
	$manifestPath = Join-Path $packageDirectory 'package.json'
	$archivePath = Join-Path $packageDirectory 'drivers.zip'

	if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
		-not (Test-Path -LiteralPath $archivePath -PathType Leaf))
	{
		throw 'The preparation script did not create the package manifest and archive.'
	}

	$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

	if ($manifest.SchemaVersion -ne 1 -or
		$manifest.PackageId -ne 'example-winpe' -or
		$manifest.SourceVersion -ne '1.0')
	{
		throw 'The prepared package manifest is incorrect.'
	}

	$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)

	try
	{
		$driverCount = @($archive.Entries | Where-Object {
			$_.FullName.EndsWith(
				'.inf',
				[System.StringComparison]::OrdinalIgnoreCase)
		}).Count

		if ($driverCount -ne 1)
		{
			throw "The prepared package contained $driverCount INF files instead of one."
		}
	}
	finally
	{
		$archive.Dispose()
	}

	$replacementGuardPassed = $false

	try
	{
		& (Join-Path $PSScriptRoot 'Import-WinPeDriverPackage.ps1') `
			-PackageId example-winpe `
			-DisplayName 'Example WinPE drivers' `
			-Manufacturer Example `
			-SourceDirectory $sourceDirectory `
			-DestinationDirectory $storeDirectory
	}
	catch
	{
		if ($_.Exception.Message -like "Package 'example-winpe' already exists.*")
		{
			$replacementGuardPassed = $true
		}
		else
		{
			throw
		}
	}

	if (-not $replacementGuardPassed)
	{
		throw 'The package replacement confirmation guard did not reject the second import.'
	}

	& (Join-Path $PSScriptRoot 'Import-WinPeDriverPackage.ps1') `
		-PackageId example-winpe `
		-DisplayName 'Example WinPE drivers' `
		-Manufacturer Example `
		-SourceVersion '1.1' `
		-SourceDirectory $sourceDirectory `
		-DestinationDirectory $storeDirectory `
		-Replace

	$updatedManifest = Get-Content -Raw -LiteralPath $manifestPath |
		ConvertFrom-Json

	if ($updatedManifest.SourceVersion -ne '1.1')
	{
		throw 'The explicitly replaced package did not contain the updated manifest.'
	}

	Write-Host 'WinPE driver package preparation and replacement guards passed.'
}
finally
{
	$resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
	$resolvedTempRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'

	if ($resolvedTestRoot.StartsWith(
		$resolvedTempRoot,
		[System.StringComparison]::OrdinalIgnoreCase) -and
		(Split-Path -Leaf $resolvedTestRoot) -like
			'OSImageDeploy-driver-package-tools-*')
	{
		Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
	}
}
