[CmdletBinding()]
param
(
	[Parameter(Mandatory)]
	[string] $CertificateThumbprint,

	[string] $InstallerPath,

	[string] $InstallDirectory =
		(Join-Path $env:ProgramFiles 'OS Image Deployment Suite'),

	[switch] $RunLiveServiceChecks,

	[switch] $LaunchUi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

function Get-MsiProperty
{
	param
	(
		[Parameter(Mandatory)]
		[object] $Database,

		[Parameter(Mandatory)]
		[string] $Name
	)

	$view = $Database.OpenView(
		"SELECT `Value` FROM `Property` WHERE `Property`='$Name'")

	try
	{
		[void] $view.Execute()
		$record = $view.Fetch()

		if ($null -eq $record)
		{
			throw "MSI property '$Name' was not found."
		}

		try
		{
			return ([string] $record.StringData(1)).Trim()
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

function Test-ProjectSignature
{
	param
	(
		[Parameter(Mandatory)]
		[string] $Path,

		[Parameter(Mandatory)]
		[string] $ExpectedThumbprint
	)

	Assert-Condition (Test-Path -LiteralPath $Path -PathType Leaf) `
		"Expected installed file was not found: $Path"

	$signature = Get-AuthenticodeSignature -LiteralPath $Path
	Assert-Condition `
		($signature.Status -eq [Management.Automation.SignatureStatus]::Valid) `
		"Authenticode signature is not valid for '$Path': $($signature.StatusMessage)"
	Assert-Condition `
		($signature.SignerCertificate.Thumbprint -eq $ExpectedThumbprint) `
		"Authenticode signer is unexpected for '$Path'."
	Assert-Condition `
		($null -ne $signature.TimeStamperCertificate) `
		"Authenticode signature is not timestamped for '$Path'."

	Write-Host "PASS: Valid timestamped installed signature: $Path"
}

$normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').Trim().ToUpperInvariant()
$resolvedInstallDirectory =
	$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
		$InstallDirectory)
$expectedProductCode = $null
$expectedProductVersion = $null

if (-not [string]::IsNullOrWhiteSpace($InstallerPath))
{
	$resolvedInstallerPath =
		$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
			$InstallerPath)
	Assert-Condition (Test-Path -LiteralPath $resolvedInstallerPath -PathType Leaf) `
		"Installer was not found: $resolvedInstallerPath"

	$installer = New-Object -ComObject WindowsInstaller.Installer

	try
	{
		$database = $installer.GetType().InvokeMember(
			'OpenDatabase',
			'InvokeMethod',
			$null,
			$installer,
			@($resolvedInstallerPath, 0))

		try
		{
			$expectedProductCode = Get-MsiProperty -Database $database -Name 'ProductCode'
			$expectedProductVersion = Get-MsiProperty -Database $database -Name 'ProductVersion'
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

	Test-ProjectSignature -Path $resolvedInstallerPath -ExpectedThumbprint $normalizedThumbprint
}

$uninstallEntries = @(
	Get-ItemProperty `
		'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', `
		'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' `
		-ErrorAction SilentlyContinue |
		Where-Object {
			$null -ne $_.PSObject.Properties['DisplayName'] -and
			$_.DisplayName -eq 'OS Image Deployment Suite'
		}
)

Assert-Condition ($uninstallEntries.Count -eq 1) `
	"Expected exactly one installed OS Image Deployment Suite registration; found $($uninstallEntries.Count)."

$installedProduct = $uninstallEntries[0]
Assert-Condition `
	($installedProduct.Publisher -eq 'x9000.com Consulting Services Limited') `
	'Installed product publisher is unexpected.'

if ($null -ne $expectedProductCode)
{
	Assert-Condition `
		($installedProduct.PSChildName -eq $expectedProductCode) `
		"Installed product code '$($installedProduct.PSChildName)' does not match MSI '$expectedProductCode'."
	Assert-Condition `
		($installedProduct.DisplayVersion -eq $expectedProductVersion) `
		"Installed version '$($installedProduct.DisplayVersion)' does not match MSI '$expectedProductVersion'."
}

Write-Host "PASS: Installed Apps registration version $($installedProduct.DisplayVersion)."

$service = Get-CimInstance Win32_Service -Filter "Name='OSImageDeploy.Service'"
Assert-Condition ($null -ne $service) 'OSImageDeploy.Service is not installed.'
Assert-Condition ($service.State -eq 'Running') 'OSImageDeploy.Service is not running.'
Assert-Condition ($service.StartMode -eq 'Auto') 'OSImageDeploy.Service is not configured for automatic startup.'
Assert-Condition ($service.StartName -eq 'LocalSystem') 'OSImageDeploy.Service does not run as LocalSystem.'

$expectedServicePath = Join-Path $resolvedInstallDirectory 'Service\OSImageDeploy.Service.exe'
$configuredServicePath = $service.PathName.Trim().Trim('"')
Assert-Condition `
	([IO.Path]::GetFullPath($configuredServicePath) -eq [IO.Path]::GetFullPath($expectedServicePath)) `
	"Service executable path is unexpected: $configuredServicePath"

Write-Host 'PASS: Service is Running, Automatic, LocalSystem, and uses the installed executable.'

$recoveryText = & sc.exe qfailure OSImageDeploy.Service 2>&1 | Out-String
Assert-Condition ($LASTEXITCODE -eq 0) 'Unable to query service recovery configuration.'
Assert-Condition `
	($recoveryText -match 'RESET_PERIOD[^\r\n]*86400') `
	'Service recovery reset period is not 86400 seconds.'
$restartActions = [regex]::Matches(
	$recoveryText,
	'RESTART\s+--\s+Delay\s*=\s*120000\s+milliseconds',
	[Text.RegularExpressions.RegexOptions]::IgnoreCase)
Assert-Condition `
	($restartActions.Count -eq 2) `
	"Expected two 120-second service restart actions; found $($restartActions.Count)."

Write-Host 'PASS: Service recovery policy has two 120-second restarts and a one-day reset.'

$installedFiles = @(
	(Join-Path $resolvedInstallDirectory 'OSImageDeploy.exe'),
	(Join-Path $resolvedInstallDirectory 'WinPEClient\OSImageDeployClient.exe'),
	(Join-Path $resolvedInstallDirectory 'Service\OSImageDeploy.Contracts.dll'),
	(Join-Path $resolvedInstallDirectory 'Service\OSImageDeploy.Engine.dll'),
	(Join-Path $resolvedInstallDirectory 'Service\OSImageDeploy.Platform.Windows.dll'),
	(Join-Path $resolvedInstallDirectory 'Service\OSImageDeploy.Service.dll'),
	(Join-Path $resolvedInstallDirectory 'Service\OSImageDeploy.Service.exe'),
	(Join-Path $resolvedInstallDirectory 'Service\OSImageDeploy.Transport.Grpc.dll'),
	(Join-Path $resolvedInstallDirectory 'Service\Utilities.dll'),
	(Join-Path $resolvedInstallDirectory 'Service\WinPEClient\OSImageDeployClient.exe')
)

foreach ($installedFile in $installedFiles)
{
	Test-ProjectSignature -Path $installedFile -ExpectedThumbprint $normalizedThumbprint
}

foreach ($payloadName in 'DellPEDrivers.zip', 'HPPEDrivers.zip')
{
	$payloadPath = Join-Path $resolvedInstallDirectory $payloadName
	Assert-Condition (-not (Test-Path -LiteralPath $payloadPath)) `
		"Installed product still contains a legacy OEM driver payload: $payloadPath"
	$servicePayloadPath = Join-Path $resolvedInstallDirectory "Service\$payloadName"
	Assert-Condition (-not (Test-Path -LiteralPath $servicePayloadPath)) `
		"Installed service still contains a legacy OEM driver payload: $servicePayloadPath"
}

Write-Host 'PASS: Installed product contains no embedded legacy OEM driver payloads.'

if ($RunLiveServiceChecks)
{
	$integrityOutput = whoami.exe /groups /fo csv /nh | Out-String
	Assert-Condition `
		($integrityOutput -match 'S-1-16-8192') `
		'Live service checks must run from a Medium-integrity user process.'

	& dotnet run `
		--project (Join-Path $PSScriptRoot '..\OSImageDeploy.Service.Tests\OSImageDeploy.Service.Tests.csproj') `
		--configuration Release `
		--no-build `
		-- --live

	Assert-Condition ($LASTEXITCODE -eq 0) 'Live service checks failed.'
	Write-Host 'PASS: Live named-pipe and destructive-operation guard checks passed from Medium integrity.'
}

if ($LaunchUi)
{
	$integrityOutput = whoami.exe /groups /fo csv /nh | Out-String
	Assert-Condition `
		($integrityOutput -match 'S-1-16-8192') `
		'Installed UI validation must run from a Medium-integrity user process.'

	$uiPath = Join-Path $resolvedInstallDirectory 'OSImageDeploy.exe'
	$startInfo = [Diagnostics.ProcessStartInfo]::new()
	$startInfo.FileName = $uiPath
	$startInfo.WorkingDirectory = $resolvedInstallDirectory
	$startInfo.UseShellExecute = $false
	$uiProcess = [Diagnostics.Process]::Start($startInfo)

	try
	{
		Assert-Condition ($null -ne $uiProcess) 'Installed UI did not start.'
		[void] $uiProcess.WaitForInputIdle(30000)
		$uiProcess.Refresh()
		Assert-Condition (-not $uiProcess.HasExited) 'Installed UI exited during startup.'
		Assert-Condition ($uiProcess.Responding) 'Installed UI is not responding.'
		Assert-Condition ($uiProcess.MainWindowHandle -ne [IntPtr]::Zero) `
			'Installed UI did not create a main window.'

		Write-Host 'PASS: Installed UI opened directly from Medium integrity and is responsive.'
	}
	finally
	{
		if ($null -ne $uiProcess -and -not $uiProcess.HasExited)
		{
			[void] $uiProcess.CloseMainWindow()

			if (-not $uiProcess.WaitForExit(10000))
			{
				$uiProcess.Kill($true)
				$uiProcess.WaitForExit()
			}
		}

		if ($null -ne $uiProcess)
		{
			$uiProcess.Dispose()
		}
	}
}

Write-Host 'Installed-product validation passed. No destructive USB operation was requested.'
