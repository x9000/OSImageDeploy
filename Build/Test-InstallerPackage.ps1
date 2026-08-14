[CmdletBinding()]
param
(
	[string] $InstallerPath =
		(Join-Path $PSScriptRoot '..\OSImageDeploy.Installer\bin\x64\Release\OSImageDeploySuite.msi'),

	[ValidateSet('Any', 'NotSigned', 'Valid')]
	[string] $ExpectedSignatureStatus = 'Any'
)

$ErrorActionPreference = 'Stop'
$expectedUpgradeCode =
	'{64552430-8321-4966-B779-B464DF8D6E86}'
$resolvedInstallerPath =
	[System.IO.Path]::GetFullPath($InstallerPath)

function Assert-Equal
{
	param
	(
		[AllowNull()]
		[Object] $Actual,

		[AllowNull()]
		[Object] $Expected,

		[String] $Description
	)

	if ($Actual -ne $Expected)
	{
		throw "$Description Expected '$Expected', but received '$Actual'."
	}
}

function Get-MsiRows
{
	param
	(
		[Object] $Database,
		[String] $Query,
		[Int32] $FieldCount
	)

	$view = $Database.GetType().InvokeMember(
		'OpenView',
		'InvokeMethod',
		$null,
		$Database,
		@($Query))

	try
	{
		$view.GetType().InvokeMember(
			'Execute',
			'InvokeMethod',
			$null,
			$view,
			$null) | Out-Null

		$rows = @()
		$record = $view.GetType().InvokeMember(
			'Fetch',
			'InvokeMethod',
			$null,
			$view,
			$null)

		while ($null -ne $record)
		{
			$fields = @()

			for ($index = 1; $index -le $FieldCount; $index++)
			{
				$fields += $record.GetType().InvokeMember(
					'StringData',
					'GetProperty',
					$null,
					$record,
					$index)
			}

			$rows += ,$fields
			$record = $view.GetType().InvokeMember(
				'Fetch',
				'InvokeMethod',
				$null,
				$view,
				$null)
		}

		return $rows
	}
	finally
	{
		[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
	}
}

if (-not (Test-Path -LiteralPath $resolvedInstallerPath -PathType Leaf))
{
	throw "The installer '$resolvedInstallerPath' does not exist."
}

if ((Get-Item -LiteralPath $resolvedInstallerPath).Length -eq 0)
{
	throw "The installer '$resolvedInstallerPath' is empty."
}

$signature = Get-AuthenticodeSignature -LiteralPath $resolvedInstallerPath

if ($ExpectedSignatureStatus -ne 'Any')
{
	Assert-Equal `
		-Actual $signature.Status.ToString() `
		-Expected $ExpectedSignatureStatus `
		-Description 'Installer signature status is incorrect.'
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember(
	'OpenDatabase',
	'InvokeMethod',
	$null,
	$installer,
	@($resolvedInstallerPath, 0))

try
{
	$propertyRows = @(Get-MsiRows `
		-Database $database `
		-Query 'SELECT `Property`,`Value` FROM `Property`' `
		-FieldCount 2)
	$properties = @{}

	foreach ($row in $propertyRows)
	{
		$properties[$row[0]] = $row[1]
	}

	Assert-Equal $properties.ProductName `
		'OS Image Deployment Suite' `
		'Product name is incorrect.'
	Assert-Equal $properties.Manufacturer `
		'x9000.com Consulting Services Limited' `
		'Manufacturer is incorrect.'
	Assert-Equal $properties.UpgradeCode `
		$expectedUpgradeCode `
		'Upgrade code is incorrect.'
	Assert-Equal $properties.ALLUSERS '1' `
		'The installer is not authored per-machine.'

	if ($properties.ProductCode -notmatch
		'^\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}$')
	{
		throw "ProductCode '$($properties.ProductCode)' is not a valid uppercase GUID."
	}

	if ($properties.ProductVersion -notmatch
		'^(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.(?:0|[1-9][0-9]{0,4})$')
	{
		throw "ProductVersion '$($properties.ProductVersion)' is not a valid three-field MSI version."
	}

	$versionFields = $properties.ProductVersion.Split('.') |
		ForEach-Object { [UInt32]$_ }

	if ($versionFields[2] -gt 65535)
	{
		throw "ProductVersion '$($properties.ProductVersion)' exceeds the MSI build-field maximum."
	}

	$serviceRows = @(Get-MsiRows `
		-Database $database `
		-Query 'SELECT `Name`,`ServiceType`,`StartType`,`ErrorControl`,`StartName` FROM `ServiceInstall`' `
		-FieldCount 5)

	Assert-Equal $serviceRows.Count 1 `
		'The installer must contain exactly one service definition.'
	Assert-Equal $serviceRows[0][0] 'OSImageDeploy.Service' `
		'Service name is incorrect.'
	Assert-Equal $serviceRows[0][1] '16' `
		'The service must run in its own process.'
	Assert-Equal $serviceRows[0][2] '2' `
		'The service must start automatically.'
	Assert-Equal $serviceRows[0][3] '1' `
		'The service error-control policy is incorrect.'
	Assert-Equal $serviceRows[0][4] 'LocalSystem' `
		'The service identity is incorrect.'

	$recoveryRows = @(Get-MsiRows `
		-Database $database `
		-Query 'SELECT `ServiceName`,`NewService`,`FirstFailureActionType`,`SecondFailureActionType`,`ThirdFailureActionType`,`ResetPeriodInDays`,`RestartServiceDelayInSeconds` FROM `Wix4ServiceConfig`' `
		-FieldCount 7)

	Assert-Equal $recoveryRows.Count 1 `
		'The installer must contain exactly one service recovery policy.'
	Assert-Equal ($recoveryRows[0] -join '|') `
		'OSImageDeploy.Service|1|restart|restart|none|1|120' `
		'Service recovery policy is incorrect.'

	$upgradeRows = @(Get-MsiRows `
		-Database $database `
		-Query 'SELECT `UpgradeCode`,`ActionProperty` FROM `Upgrade`' `
		-FieldCount 2)

	Assert-Equal $upgradeRows.Count 2 `
		'The installer must contain upgrade and downgrade detection rows.'

	foreach ($row in $upgradeRows)
	{
		Assert-Equal $row[0] $expectedUpgradeCode `
			'An upgrade row uses an unexpected UpgradeCode.'
	}

	$upgradeActions = $upgradeRows | ForEach-Object { $_[1] }

	foreach ($requiredAction in
		@('WIX_UPGRADE_DETECTED', 'WIX_DOWNGRADE_DETECTED'))
	{
		if ($requiredAction -notin $upgradeActions)
		{
			throw "The Upgrade table is missing '$requiredAction'."
		}
	}

	$mediaRows = @(Get-MsiRows `
		-Database $database `
		-Query 'SELECT `Cabinet` FROM `Media`' `
		-FieldCount 1)

	if ($mediaRows.Count -eq 0 -or
		($mediaRows | Where-Object { $_[0] -notlike '#*' }).Count -gt 0)
	{
		throw 'All installer cabinets must be embedded in the MSI.'
	}

	$summary = $database.SummaryInformation(0)

	try
	{
		$template = $summary.Property(7)
		$minimumInstallerVersion = $summary.Property(14)

		if ($template -notlike 'x64;*')
		{
			throw "The MSI summary template '$template' is not x64."
		}

		Assert-Equal $minimumInstallerVersion 500 `
			'Minimum Windows Installer version is incorrect.'
	}
	finally
	{
		[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)
	}

	Write-Host "PASS: MSI structure validated: $resolvedInstallerPath"
	Write-Host "Product version: $($properties.ProductVersion)"
	Write-Host "Signature status: $($signature.Status)"
}
finally
{
	[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
	[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
