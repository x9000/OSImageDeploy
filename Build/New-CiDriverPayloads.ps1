[CmdletBinding()]
param(
	[string] $DestinationDirectory = (Join-Path $PSScriptRoot '..\OSImageDeploy')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$payloadNames = @(
	'DellPEDrivers.zip',
	'HPPEDrivers.zip'
)

$resolvedDestination = [System.IO.Path]::GetFullPath($DestinationDirectory)
[System.IO.Directory]::CreateDirectory($resolvedDestination) | Out-Null

foreach ($payloadName in $payloadNames)
{
	$payloadPath = Join-Path $resolvedDestination $payloadName

	if (Test-Path -LiteralPath $payloadPath)
	{
		throw "Refusing to replace existing driver payload: $payloadPath"
	}

	$archive = [System.IO.Compression.ZipFile]::Open(
		$payloadPath,
		[System.IO.Compression.ZipArchiveMode]::Create)

	try
	{
		$entry = $archive.CreateEntry('CI-PLACEHOLDER.txt')
		$writer = [System.IO.StreamWriter]::new($entry.Open())

		try
		{
			$writer.WriteLine('CI-only placeholder. This archive contains no deployable drivers.')
			$writer.WriteLine('Never distribute an installer built with this payload.')
		}
		finally
		{
			$writer.Dispose()
		}
	}
	finally
	{
		$archive.Dispose()
	}

	Write-Host "Created CI-only placeholder: $payloadPath"
}
