[CmdletBinding(DefaultParameterSetName = 'Directory')]
param(
	[Parameter(Mandatory)]
	[ValidatePattern('^[a-z0-9][a-z0-9.-]{0,63}$')]
	[string] $PackageId,

	[Parameter(Mandatory)]
	[ValidateNotNullOrEmpty()]
	[string] $DisplayName,

	[Parameter(Mandatory)]
	[ValidateNotNullOrEmpty()]
	[string] $Manufacturer,

	[string] $SourceVersion = '',

	[ValidatePattern('^$|^https://')]
	[string] $SourcePageUrl = '',

	[Parameter(Mandatory, ParameterSetName = 'Directory')]
	[string] $SourceDirectory,

	[Parameter(Mandatory, ParameterSetName = 'Archive')]
	[string] $ArchivePath,

	[string] $DestinationDirectory =
		(Join-Path $env:ProgramData 'OSImageDeploy\DriverPackages'),

	[switch] $Replace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Test-DriverArchive
{
	param([Parameter(Mandatory)][string] $Path)

	$archive = [System.IO.Compression.ZipFile]::OpenRead($Path)

	try
	{
		$driverCount = 0

		foreach ($entry in $archive.Entries)
		{
			$normalizedName = $entry.FullName.Replace('\', '/')
			$segments = @($normalizedName.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries))

			if ($normalizedName.StartsWith('/') -or
				[System.IO.Path]::IsPathRooted($entry.FullName) -or
				$segments -contains '..')
			{
				throw "Driver archive contains an unsafe path: $($entry.FullName)"
			}

			if ($normalizedName.Equals(
				'CI-PLACEHOLDER.txt',
				[System.StringComparison]::OrdinalIgnoreCase))
			{
				throw 'A CI placeholder is not a deployable driver package.'
			}

			if ($normalizedName.EndsWith(
				'.inf',
				[System.StringComparison]::OrdinalIgnoreCase))
			{
				$driverCount++
			}
		}

		if ($driverCount -eq 0)
		{
			throw 'The driver archive does not contain any INF files.'
		}

		return $driverCount
	}
	finally
	{
		$archive.Dispose()
	}
}

$resolvedDestination = [System.IO.Path]::GetFullPath($DestinationDirectory)
$defaultDestination = [System.IO.Path]::GetFullPath(
	(Join-Path $env:ProgramData 'OSImageDeploy\DriverPackages'))
$packageDirectory = Join-Path $resolvedDestination $PackageId.ToLowerInvariant()
$stagingDirectory = Join-Path $resolvedDestination ('.' + $PackageId + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
$stagedArchive = Join-Path $stagingDirectory 'drivers.zip'
$stagedManifest = Join-Path $stagingDirectory 'package.json'

if (Test-Path -LiteralPath $packageDirectory)
{
	if (-not $Replace)
	{
		throw "Package '$PackageId' already exists. Supply -Replace to replace it."
	}
}

[System.IO.Directory]::CreateDirectory($resolvedDestination) | Out-Null

if ($resolvedDestination.Equals(
	$defaultDestination,
	[System.StringComparison]::OrdinalIgnoreCase))
{
	$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
	$principal = [System.Security.Principal.WindowsPrincipal]::new($identity)

	if (-not $principal.IsInRole(
		[System.Security.Principal.WindowsBuiltInRole]::Administrator))
	{
		throw 'The service driver-package store must be updated from an elevated administrator session.'
	}

	$inheritance =
		[System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
		[System.Security.AccessControl.InheritanceFlags]::ObjectInherit
	$security = [System.Security.AccessControl.DirectorySecurity]::new()
	$system = [System.Security.Principal.SecurityIdentifier]::new(
		[System.Security.Principal.WellKnownSidType]::LocalSystemSid,
		$null)
	$administrators = [System.Security.Principal.SecurityIdentifier]::new(
		[System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid,
		$null)

	$security.SetAccessRuleProtection($true, $false)
	# An elevated administrator cannot assign SYSTEM as owner without a token
	# privilege that is not normally enabled. Administrators own the store while
	# both Administrators and SYSTEM retain full control; the LocalSystem service
	# reasserts its service-side ACL when it opens the default store.
	$security.SetOwner($administrators)
	$security.AddAccessRule(
		[System.Security.AccessControl.FileSystemAccessRule]::new(
			$system,
			[System.Security.AccessControl.FileSystemRights]::FullControl,
			$inheritance,
			[System.Security.AccessControl.PropagationFlags]::None,
			[System.Security.AccessControl.AccessControlType]::Allow))
	$security.AddAccessRule(
		[System.Security.AccessControl.FileSystemAccessRule]::new(
			$administrators,
			[System.Security.AccessControl.FileSystemRights]::FullControl,
			$inheritance,
			[System.Security.AccessControl.PropagationFlags]::None,
			[System.Security.AccessControl.AccessControlType]::Allow))

	[System.IO.DirectoryInfo]::new($resolvedDestination).SetAccessControl($security)
}

[System.IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null

try
{
	if ($PSCmdlet.ParameterSetName -eq 'Directory')
	{
		if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container))
		{
			throw "Source directory does not exist: $SourceDirectory"
		}

		$resolvedSource = (Resolve-Path -LiteralPath $SourceDirectory).Path
		$sourcePrefix = $resolvedSource.TrimEnd('\') + '\'
		$destinationPrefix = $resolvedDestination.TrimEnd('\') + '\'

		if ($resolvedDestination.Equals(
			$resolvedSource,
			[System.StringComparison]::OrdinalIgnoreCase) -or
			$resolvedDestination.StartsWith(
			$sourcePrefix,
			[System.StringComparison]::OrdinalIgnoreCase) -or
			$resolvedSource.StartsWith(
				$destinationPrefix,
				[System.StringComparison]::OrdinalIgnoreCase))
		{
			throw 'The source directory and package-store directory must not contain one another.'
		}

		$reparsePoint = Get-ChildItem -LiteralPath $resolvedSource -Recurse -Force |
			Where-Object { $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint } |
			Select-Object -First 1

		if ($null -ne $reparsePoint)
		{
			throw "Source directory contains a reparse point: $($reparsePoint.FullName)"
		}

		[System.IO.Compression.ZipFile]::CreateFromDirectory(
			$resolvedSource,
			$stagedArchive,
			[System.IO.Compression.CompressionLevel]::Optimal,
			$false)
	}
	else
	{
		if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf))
		{
			throw "Driver archive does not exist: $ArchivePath"
		}

		$resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
		Copy-Item -LiteralPath $resolvedArchive -Destination $stagedArchive
	}

	$driverCount = Test-DriverArchive -Path $stagedArchive
	$archiveHash = (Get-FileHash -LiteralPath $stagedArchive -Algorithm SHA256).Hash

	$manifest = [ordered]@{
		SchemaVersion = 1
		PackageId = $PackageId.ToLowerInvariant()
		DisplayName = $DisplayName
		Manufacturer = $Manufacturer
		SourceVersion = $SourceVersion
		SourcePageUrl = $SourcePageUrl
		PreparedUtc = [DateTimeOffset]::UtcNow.ToString('o')
	}

	$manifestJson = $manifest | ConvertTo-Json
	[System.IO.File]::WriteAllText(
		$stagedManifest,
		$manifestJson,
		[System.Text.UTF8Encoding]::new($false))

	if (Test-Path -LiteralPath $packageDirectory)
	{
		$backupDirectory = $packageDirectory + '.backup.' + [guid]::NewGuid().ToString('N')
		Move-Item -LiteralPath $packageDirectory -Destination $backupDirectory

		try
		{
			Move-Item -LiteralPath $stagingDirectory -Destination $packageDirectory
			Remove-Item -LiteralPath $backupDirectory -Recurse -Force
		}
		catch
		{
			if (-not (Test-Path -LiteralPath $packageDirectory) -and
				(Test-Path -LiteralPath $backupDirectory))
			{
				Move-Item -LiteralPath $backupDirectory -Destination $packageDirectory
			}

			throw
		}
	}
	else
	{
		Move-Item -LiteralPath $stagingDirectory -Destination $packageDirectory
	}

	Write-Host "Prepared WinPE driver package '$PackageId'."
	Write-Host "Location: $packageDirectory"
	Write-Host "Drivers: $driverCount INF files"
	Write-Host "SHA-256: $archiveHash"
}
finally
{
	if (Test-Path -LiteralPath $stagingDirectory)
	{
		Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
	}
}
