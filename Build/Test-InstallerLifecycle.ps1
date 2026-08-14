[CmdletBinding()]
param
(
	[Parameter(Mandatory)]
	[string] $CurrentInstallerPath,

	[Parameter(Mandatory)]
	[string] $OlderInstallerPath,

	[Parameter(Mandatory)]
	[string] $CertificateThumbprint,

	[switch] $ConfirmLifecycleTest,

	[string] $OutputDirectory =
		(Join-Path $env:TEMP (
			'OSImageDeploy.InstallerLifecycle\' +
			[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))),

	[string] $InstallDirectory =
		(Join-Path $env:ProgramFiles 'OS Image Deployment Suite')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$productName = 'OS Image Deployment Suite'
$serviceName = 'OSImageDeploy.Service'
$publisher = 'x9000.com Consulting Services Limited'
$startMenuShortcut = Join-Path `
	$env:ProgramData `
	'Microsoft\Windows\Start Menu\Programs\OS Image Deploy.lnk'

function Assert-Condition
{
	param
	(
		[bool] $Condition,
		[string] $Message
	)

	if (-not $Condition)
	{
		throw $Message
	}
}

function Get-MsiProperties
{
	param
	(
		[Parameter(Mandatory)]
		[string] $Path
	)

	$installer = New-Object -ComObject WindowsInstaller.Installer

	try
	{
		$database = $installer.GetType().InvokeMember(
			'OpenDatabase',
			'InvokeMethod',
			$null,
			$installer,
			@($Path, 0))

		try
		{
			$properties = [ordered] @{}

			foreach ($propertyName in 'ProductName', 'ProductVersion', 'ProductCode', 'UpgradeCode')
			{
				$view = $database.OpenView(
					"SELECT `Value` FROM `Property` WHERE `Property`='$propertyName'")

				try
				{
					[void] $view.Execute()
					$record = $view.Fetch()
					Assert-Condition ($null -ne $record) `
						"MSI property '$propertyName' was not found in '$Path'."

					try
					{
						$properties[$propertyName] =
							([string] $record.StringData(1)).Trim()
					}
					finally
					{
						[void] [Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
					}
				}
				finally
				{
					[void] $view.Close()
					[void] [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
				}
			}

			return [pscustomobject] $properties
		}
		finally
		{
			[void] [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
		}
	}
	finally
	{
		[void] [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
	}
}

function Get-InstalledProduct
{
	return @(
		Get-ItemProperty `
			'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', `
			'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' `
			-ErrorAction SilentlyContinue |
			Where-Object {
				$null -ne $_.PSObject.Properties['DisplayName'] -and
				$_.DisplayName -eq $productName
			}
	)
}

function Assert-InstalledState
{
	param
	(
		[Parameter(Mandatory)]
		[object] $ExpectedMsi
	)

	$registrations = @(Get-InstalledProduct)
	Assert-Condition ($registrations.Count -eq 1) `
		"Expected one installed product registration; found $($registrations.Count)."
	Assert-Condition ($registrations[0].PSChildName -eq $ExpectedMsi.ProductCode) `
		"Installed ProductCode '$($registrations[0].PSChildName)' does not match '$($ExpectedMsi.ProductCode)'."
	Assert-Condition ($registrations[0].DisplayVersion -eq $ExpectedMsi.ProductVersion) `
		"Installed version '$($registrations[0].DisplayVersion)' does not match '$($ExpectedMsi.ProductVersion)'."
	Assert-Condition ($registrations[0].Publisher -eq $publisher) `
		'Installed product publisher is unexpected.'

	$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
	Assert-Condition ($null -ne $service) "$serviceName is not installed."
	Assert-Condition ($service.State -eq 'Running') "$serviceName is not running."
	Assert-Condition ($service.StartMode -eq 'Auto') "$serviceName is not automatic."
	Assert-Condition ($service.StartName -eq 'LocalSystem') "$serviceName is not LocalSystem."
	Assert-Condition (Test-Path -LiteralPath $InstallDirectory -PathType Container) `
		"Install directory was not found: $InstallDirectory"
	Assert-Condition (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf) `
		"Start menu shortcut was not found: $startMenuShortcut"
}

function Assert-UninstalledState
{
	Assert-Condition (@(Get-InstalledProduct).Count -eq 0) `
		'Installed Apps registration remains after uninstall.'
	Assert-Condition ($null -eq (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) `
		'Service remains after uninstall.'
	Assert-Condition (-not (Test-Path -LiteralPath $InstallDirectory)) `
		"Install directory remains after uninstall: $InstallDirectory"
	Assert-Condition (-not (Test-Path -LiteralPath $startMenuShortcut)) `
		"Start menu shortcut remains after uninstall: $startMenuShortcut"
}

function Test-InstallerSignature
{
	param
	(
		[Parameter(Mandatory)]
		[string] $Path,

		[Parameter(Mandatory)]
		[string] $ExpectedThumbprint
	)

	$signature = Get-AuthenticodeSignature -LiteralPath $Path
	Assert-Condition `
		($signature.Status -eq [Management.Automation.SignatureStatus]::Valid) `
		"Installer signature is not valid for '$Path': $($signature.StatusMessage)"
	Assert-Condition `
		($signature.SignerCertificate.Thumbprint -eq $ExpectedThumbprint) `
		"Installer signer is unexpected for '$Path'."
	Assert-Condition `
		($null -ne $signature.TimeStamperCertificate) `
		"Installer signature is not timestamped for '$Path'."
}

function Invoke-MsiOperation
{
	param
	(
		[Parameter(Mandatory)]
		[string] $Operation,

		[Parameter(Mandatory)]
		[string] $Target,

		[Parameter(Mandatory)]
		[string] $LogPath
	)

	$arguments = @(
		$Operation,
		('"' + $Target + '"'),
		'/qn',
		'/norestart',
		'/l*v',
		('"' + $LogPath + '"')
	)

	$process = Start-Process `
		-FilePath 'msiexec.exe' `
		-ArgumentList $arguments `
		-Wait `
		-PassThru

	return $process.ExitCode
}

Assert-Condition $ConfirmLifecycleTest.IsPresent `
	'Lifecycle testing was not confirmed. Supply -ConfirmLifecycleTest.'

$principal = [Security.Principal.WindowsPrincipal]::new(
	[Security.Principal.WindowsIdentity]::GetCurrent())
Assert-Condition `
	($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) `
	'Installer lifecycle validation must run from an elevated administrator process.'

$resolvedCurrentInstaller = [IO.Path]::GetFullPath($CurrentInstallerPath)
$resolvedOlderInstaller = [IO.Path]::GetFullPath($OlderInstallerPath)
$resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').Trim().ToUpperInvariant()

foreach ($installerPath in $resolvedCurrentInstaller, $resolvedOlderInstaller)
{
	Assert-Condition (Test-Path -LiteralPath $installerPath -PathType Leaf) `
		"Installer was not found: $installerPath"
	Test-InstallerSignature `
		-Path $installerPath `
		-ExpectedThumbprint $normalizedThumbprint
}

$currentMsi = Get-MsiProperties -Path $resolvedCurrentInstaller
$olderMsi = Get-MsiProperties -Path $resolvedOlderInstaller
Assert-Condition ($currentMsi.ProductName -eq $productName) `
	'Current installer product name is unexpected.'
Assert-Condition ($olderMsi.ProductName -eq $productName) `
	'Older installer product name is unexpected.'
Assert-Condition ($currentMsi.UpgradeCode -eq $olderMsi.UpgradeCode) `
	'Current and older installers do not share an UpgradeCode.'
Assert-Condition `
	([Version] $currentMsi.ProductVersion -gt [Version] $olderMsi.ProductVersion) `
	"Current version '$($currentMsi.ProductVersion)' is not newer than '$($olderMsi.ProductVersion)'."

[IO.Directory]::CreateDirectory($resolvedOutputDirectory) | Out-Null

$evidence = [ordered] @{
	StartedUtc = [DateTime]::UtcNow.ToString('o')
	MachineName = [Environment]::MachineName
	CurrentVersion = $currentMsi.ProductVersion
	CurrentProductCode = $currentMsi.ProductCode
	OlderVersion = $olderMsi.ProductVersion
	OlderProductCode = $olderMsi.ProductCode
	UpgradeCode = $currentMsi.UpgradeCode
	CurrentInstallerSha256 = (Get-FileHash $resolvedCurrentInstaller -Algorithm SHA256).Hash
	OlderInstallerSha256 = (Get-FileHash $resolvedOlderInstaller -Algorithm SHA256).Hash
	Operations = [ordered] @{}
}

$initialInstallLog = Join-Path $resolvedOutputDirectory '01-initial-install.log'
$repairLog = Join-Path $resolvedOutputDirectory '02-repair.log'
$downgradeLog = Join-Path $resolvedOutputDirectory '03-downgrade-rejection.log'
$uninstallLog = Join-Path $resolvedOutputDirectory '04-uninstall.log'
$reinstallLog = Join-Path $resolvedOutputDirectory '05-reinstall.log'
$recoveryLog = Join-Path $resolvedOutputDirectory '99-recovery-install.log'

try
{
	$exitCode = Invoke-MsiOperation '/i' $resolvedCurrentInstaller $initialInstallLog
	$evidence.Operations.InitialInstall = $exitCode
	Assert-Condition ($exitCode -in 0, 3010) `
		"Initial current-version install failed with exit code $exitCode."
	Assert-InstalledState -ExpectedMsi $currentMsi
	Write-Host "PASS: Current version $($currentMsi.ProductVersion) is installed."

	$exitCode = Invoke-MsiOperation '/fa' $resolvedCurrentInstaller $repairLog
	$evidence.Operations.Repair = $exitCode
	Assert-Condition ($exitCode -in 0, 3010) `
		"Repair failed with exit code $exitCode."
	Assert-InstalledState -ExpectedMsi $currentMsi
	Write-Host 'PASS: Repair completed and retained a healthy installed product.'

	$exitCode = Invoke-MsiOperation '/i' $resolvedOlderInstaller $downgradeLog
	$evidence.Operations.DowngradeAttempt = $exitCode
	Assert-Condition ($exitCode -notin 0, 3010) `
		'Older installer unexpectedly succeeded over the current version.'
	$downgradeLogText = Get-Content -LiteralPath $downgradeLog -Raw
	Assert-Condition `
		($downgradeLogText -match 'NEWERPRODUCTFOUND|newer version') `
		'Downgrade failed without evidence of newer-product detection.'
	Assert-InstalledState -ExpectedMsi $currentMsi
	Write-Host "PASS: Downgrade to $($olderMsi.ProductVersion) was rejected; current version remains healthy."

	$exitCode = Invoke-MsiOperation '/x' $currentMsi.ProductCode $uninstallLog
	$evidence.Operations.Uninstall = $exitCode
	Assert-Condition ($exitCode -in 0, 3010) `
		"Uninstall failed with exit code $exitCode."
	Assert-UninstalledState
	Write-Host 'PASS: Uninstall removed registration, service, installed files, and shortcut.'

	$exitCode = Invoke-MsiOperation '/i' $resolvedCurrentInstaller $reinstallLog
	$evidence.Operations.Reinstall = $exitCode
	Assert-Condition ($exitCode -in 0, 3010) `
		"Reinstall failed with exit code $exitCode."
	Assert-InstalledState -ExpectedMsi $currentMsi
	Write-Host 'PASS: Clean reinstall restored a healthy installed product.'

	$evidence.CompletedUtc = [DateTime]::UtcNow.ToString('o')
	$evidence.Result = 'Passed'
}
finally
{
	$currentRegistration = @(
		Get-InstalledProduct |
		Where-Object {
			$_.PSChildName -eq $currentMsi.ProductCode -and
			$_.DisplayVersion -eq $currentMsi.ProductVersion
		}
	)

	if ($currentRegistration.Count -ne 1)
	{
		Write-Warning 'Current product is not healthy after lifecycle execution; attempting recovery install.'

		try
		{
			$recoveryExitCode = Invoke-MsiOperation '/i' $resolvedCurrentInstaller $recoveryLog
			$evidence.Operations.RecoveryInstall = $recoveryExitCode
			Write-Warning "Recovery install completed with exit code $recoveryExitCode."
		}
		catch
		{
			Write-Warning "Recovery install could not be completed: $($_.Exception.Message)"
		}
	}

	if (-not $evidence.Contains('CompletedUtc'))
	{
		$evidence.CompletedUtc = [DateTime]::UtcNow.ToString('o')
		$evidence.Result = 'Failed'
	}

	$evidencePath = Join-Path $resolvedOutputDirectory 'LifecycleEvidence.json'
	$evidence |
		ConvertTo-Json -Depth 5 |
		Set-Content -LiteralPath $evidencePath -Encoding utf8
	Write-Host "Lifecycle evidence: $evidencePath"
}

Write-Host 'Installer lifecycle validation passed. The current signed product remains installed.'
