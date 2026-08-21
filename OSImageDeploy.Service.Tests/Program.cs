using Grpc.Core;
using Grpc.Net.Client;
using Google.Protobuf;
using OSImageDeploy.Client;
using OSImageDeploy.Contracts;
using OSImageDeploy.Platform.Windows;
using OSImageDeploy.Transport.Grpc;
using OSImageDeploy.Transport.Grpc.V1;
using Utilities;
using System.IO.Compression;
using System.Text.Json;
using ContractOperationProgress = OSImageDeploy.Contracts.OperationProgress;
using ContractOperationState = OSImageDeploy.Contracts.UsbMediaOperationState;

UsbTargetDescriptor original = new UsbTargetDescriptor
{
	TargetId = "disk-contract-round-trip",
	DiskNumber = 12,
	DisplayName = "Contract test USB target",
	Model = "Contract model",
	SerialNumber = "CONTRACT-SERIAL",
	BusType = "USB",
	SizeBytes = 128UL * 1024 * 1024 * 1024,
	IsSystemDisk = false,
	IsBootDisk = false,
	IsReadOnly = false,
	IsOffline = false,
	IsClustered = false,
	HealthStatus = 0
};

UsbTarget message = GrpcTargetMapper.ToMessage(original);
UsbTargetDescriptor roundTrip = GrpcTargetMapper.ToDescriptor(message);

Assert(roundTrip.TargetId == original.TargetId, "TargetId changed.");
Assert(roundTrip.DiskNumber == original.DiskNumber, "DiskNumber changed.");
Assert(roundTrip.DisplayName == original.DisplayName, "DisplayName changed.");
Assert(roundTrip.Model == original.Model, "Model changed.");
Assert(roundTrip.SerialNumber == original.SerialNumber, "SerialNumber changed.");
Assert(roundTrip.BusType == original.BusType, "BusType changed.");
Assert(roundTrip.SizeBytes == original.SizeBytes, "SizeBytes changed.");
Assert(roundTrip.IsSystemDisk == original.IsSystemDisk, "IsSystemDisk changed.");
Assert(roundTrip.IsBootDisk == original.IsBootDisk, "IsBootDisk changed.");
Assert(roundTrip.IsReadOnly == original.IsReadOnly, "IsReadOnly changed.");
Assert(roundTrip.IsOffline == original.IsOffline, "IsOffline changed.");
Assert(roundTrip.IsClustered == original.IsClustered, "IsClustered changed.");
Assert(roundTrip.HealthStatus == original.HealthStatus, "HealthStatus changed.");

Console.WriteLine("PASS: USB target gRPC contract round trip.");

UsbMediaOperationSnapshot operationSnapshot =
	new UsbMediaOperationSnapshot
	{
		OperationId = "operation-contract-round-trip",
		State = ContractOperationState.Running,
		Progress = new ContractOperationProgress
		{
			Stage = "Testing",
			Message = "Operation contract progress.",
			OverallPercent = 42
		},
		StartedUtc = DateTimeOffset.UtcNow
	};

UsbMediaOperationSnapshot operationRoundTrip =
	GrpcOperationMapper.ToSnapshot(
		GrpcOperationMapper.ToMessage(operationSnapshot));

Assert(
	operationRoundTrip.OperationId == operationSnapshot.OperationId,
	"OperationId changed.");
Assert(
	operationRoundTrip.State == operationSnapshot.State,
	"Operation state changed.");
Assert(
	operationRoundTrip.Progress?.OverallPercent == 42,
	"Operation progress changed.");

Console.WriteLine("PASS: USB media operation gRPC contract round trip.");

String operationStoreDirectory = Path.Combine(
	Path.GetTempPath(),
	"OSImageDeploy.Service.Tests",
	Guid.NewGuid().ToString("N"));

try
{
	JsonUsbMediaOperationStore operationStore =
		new JsonUsbMediaOperationStore(operationStoreDirectory);
	operationStore.Save(operationSnapshot);

	UsbMediaOperationSnapshot? persistedOperation = operationStore.Load();

	Assert(persistedOperation != null, "Persisted operation was not loaded.");
	Assert(
		persistedOperation!.OperationId == operationSnapshot.OperationId,
		"Persisted operation identity changed.");
	Assert(
		persistedOperation.Progress?.OverallPercent == 42,
		"Persisted operation progress changed.");

	Console.WriteLine("PASS: USB media operation JSON persistence round trip.");
}
finally
{
	if (Directory.Exists(operationStoreDirectory))
	{
		Directory.Delete(operationStoreDirectory, recursive: true);
	}
}

WinPeCacheStatusSnapshot cacheSnapshot =
	new WinPeCacheStatusSnapshot
	{
		State = OSImageDeploy.Contracts.WinPeCacheState.Available,
		CreatedUtc = DateTimeOffset.UtcNow,
		ArchiveSizeBytes = 123456789
	};

WinPeCacheStatusSnapshot cacheRoundTrip =
	GrpcWinPeCacheMapper.ToSnapshot(
		GrpcWinPeCacheMapper.ToMessage(cacheSnapshot));

Assert(cacheRoundTrip.State == cacheSnapshot.State, "Cache state changed.");
Assert(
	cacheRoundTrip.CreatedUtc?.ToUnixTimeMilliseconds() ==
		cacheSnapshot.CreatedUtc?.ToUnixTimeMilliseconds(),
	"Cache creation time changed.");
Assert(
	cacheRoundTrip.ArchiveSizeBytes == cacheSnapshot.ArchiveSizeBytes,
	"Cache archive size changed.");

Console.WriteLine("PASS: WinPE cache gRPC contract round trip.");

WinPeDriverPackageDescriptor driverPackageSnapshot =
	new WinPeDriverPackageDescriptor
	{
		PackageId = "example-winpe",
		DisplayName = "Example WinPE drivers",
		Manufacturer = "Example",
		SourceVersion = "2026.08",
		SourcePageUrl = "https://example.com/winpe",
		PreparationInstructions = "Extract and prepare the package.",
		IsAvailable = true,
		DriverCount = 12,
		ArchiveSizeBytes = 3456789,
		ArchiveSha256 = "ABCDEF",
		StatusMessage = "Available."
	};
WinPeDriverPackageDescriptor driverPackageRoundTrip =
	GrpcWinPeDriverPackageMapper.ToDescriptor(
		GrpcWinPeDriverPackageMapper.ToMessage(driverPackageSnapshot));

Assert(
	driverPackageRoundTrip.PackageId == driverPackageSnapshot.PackageId,
	"Driver package ID changed.");
Assert(
	driverPackageRoundTrip.SourceVersion == driverPackageSnapshot.SourceVersion,
	"Driver package version changed.");
Assert(
	driverPackageRoundTrip.SourcePageUrl == driverPackageSnapshot.SourcePageUrl,
	"Driver package source URL changed.");
Assert(
	driverPackageRoundTrip.IsAvailable &&
	driverPackageRoundTrip.DriverCount == 12 &&
	driverPackageRoundTrip.ArchiveSizeBytes == 3456789,
	"Driver package availability evidence changed.");

StartUsbMediaBuildRequest driverSelectionRequest = new();
driverSelectionRequest.WinPeDriverPackageIds.Add("dell-winpe");
driverSelectionRequest.WinPeDriverPackageIds.Add("example-winpe");
StartUsbMediaBuildRequest parsedDriverSelectionRequest =
	StartUsbMediaBuildRequest.Parser.ParseFrom(
		driverSelectionRequest.ToByteArray());
Assert(
	parsedDriverSelectionRequest.WinPeDriverPackageIds.SequenceEqual(
		new[] { "dell-winpe", "example-winpe" }),
	"Selected driver package IDs changed across the gRPC contract.");

Console.WriteLine("PASS: WinPE driver package gRPC contract round trip.");

Boolean targetResolverCalled = false;
Boolean preflightFailureObserved = false;
DiskBuilder preflightDiskBuilder =
	new DiskBuilder(
		_ => Task.FromException<WinPeBuildResult>(
			new InvalidOperationException("Expected preflight failure.")));

try
{
	await preflightDiskBuilder.PrepareDiskAsync(
		_ =>
		{
			targetResolverCalled = true;
			return Task.FromResult<UInt32>(1);
		});
}
catch (InvalidOperationException exception) when (
	exception.Message == "Expected preflight failure.")
{
	preflightFailureObserved = true;
}

Assert(preflightFailureObserved, "The injected preflight failure was not observed.");
Assert(
	!targetResolverCalled,
	"The destructive target resolver ran before WinPE preflight completed.");

Console.WriteLine("PASS: WinPE preflight precedes destructive target resolution.");

String cacheTestDirectory = Path.Combine(
	Path.GetTempPath(),
	$"OSImageDeploy-cache-test-{Guid.NewGuid():N}");

try
{
	WinPeMediaCacheManager cacheManager =
		new WinPeMediaCacheManager(cacheTestDirectory);
	WindowsUsbMediaWorkflow workflow =
		new WindowsUsbMediaWorkflow(
			new WindowsUsbTargetProvider(),
			cacheManager,
			new WindowsWinPeDriverPackageStore(
				Path.Combine(cacheTestDirectory, "driver-packages")));

	WinPeCacheStatusSnapshot missingStatus =
		await workflow.GetStatusAsync();
	Assert(
		missingStatus.State == OSImageDeploy.Contracts.WinPeCacheState.Missing,
		"An empty cache directory was not reported as missing.");

	Directory.CreateDirectory(cacheTestDirectory);
	await File.WriteAllBytesAsync(cacheManager.ArchivePath, new Byte[] { 1 });

	WinPeCacheStatusSnapshot incompleteStatus =
		await workflow.GetStatusAsync();
	Assert(
		incompleteStatus.State ==
			OSImageDeploy.Contracts.WinPeCacheState.Incomplete,
		"A partial cache was not reported as incomplete.");

	Boolean invalidPackageRejectedBeforeCacheClear = false;

	try
	{
		await workflow.CreateUsbMediaAsync(
			new UsbMediaBuildRequest
			{
				Target = original,
				RebuildWinPeCache = true,
				WinPeDriverPackageIds = new[] { "missing-winpe" },
				DestructiveActionConfirmed = true
			},
			new Progress<OSImageDeploy.Contracts.OperationProgress>());
	}
	catch (InvalidDataException)
	{
		invalidPackageRejectedBeforeCacheClear = true;
	}

	Assert(
		invalidPackageRejectedBeforeCacheClear,
		"An unavailable driver package selection was accepted by the workflow.");
	Assert(
		File.Exists(cacheManager.ArchivePath),
		"The WinPE cache was cleared before driver-package selection validation.");

	WinPeCacheStatusSnapshot clearedStatus =
		await workflow.ClearAsync();
	Assert(
		clearedStatus.State == OSImageDeploy.Contracts.WinPeCacheState.Missing,
		"The cache was not missing after it was cleared.");
}
finally
{
	if (Directory.Exists(cacheTestDirectory))
	{
		Directory.Delete(cacheTestDirectory, recursive: true);
	}
}

Console.WriteLine("PASS: Windows WinPE cache status and clear boundary.");

String driverPackageStoreDirectory = Path.Combine(
	Path.GetTempPath(),
	$"OSImageDeploy-driver-package-test-{Guid.NewGuid():N}");
String driverExtractionDirectory = Path.Combine(
	Path.GetTempPath(),
	$"OSImageDeploy-driver-extraction-test-{Guid.NewGuid():N}");

try
{
	CreateDriverPackage(
		driverPackageStoreDirectory,
		"dell-winpe",
		"Dell test WinPE drivers",
		"Dell",
		"A01");
	CreateDriverPackage(
		driverPackageStoreDirectory,
		"example-winpe",
		"Example manufacturer WinPE drivers",
		"Example Manufacturer",
		"2026.08");
	CreateDriverPackage(
		driverPackageStoreDirectory,
		"unsafe-winpe",
		"Unsafe test package",
		"Test",
		"1",
		unsafeArchivePath: true);

	WindowsWinPeDriverPackageStore packageStore =
		new WindowsWinPeDriverPackageStore(driverPackageStoreDirectory);
	IReadOnlyList<WinPeDriverPackageDescriptor> packages =
		packageStore.GetPackages();

	WinPeDriverPackageDescriptor dellPackage = packages.Single(
		package => package.PackageId == "dell-winpe");
	Assert(dellPackage.IsAvailable, "The prepared Dell package was unavailable.");
	Assert(dellPackage.DriverCount == 1, "The Dell INF count was incorrect.");
	Assert(
		dellPackage.SourcePageUrl.StartsWith("https://www.dell.com/"),
		"The Dell source guidance was not applied.");
	Assert(
		!String.IsNullOrWhiteSpace(dellPackage.ArchiveSha256),
		"The Dell archive hash was not reported.");

	WinPeDriverPackageDescriptor hpPackage = packages.Single(
		package => package.PackageId == "hp-winpe");
	Assert(!hpPackage.IsAvailable, "An unprepared HP package was available.");
	Assert(
		hpPackage.SourcePageUrl.StartsWith("https://ftp.ext.hp.com/"),
		"The HP source guidance was not reported.");

	WinPeDriverPackageDescriptor customPackage = packages.Single(
		package => package.PackageId == "example-winpe");
	Assert(customPackage.IsAvailable, "The custom package was unavailable.");
	Assert(
		customPackage.Manufacturer == "Example Manufacturer",
		"The custom manufacturer changed.");

	WinPeDriverPackageDescriptor unsafePackage = packages.Single(
		package => package.PackageId == "unsafe-winpe");
	Assert(unsafePackage.IsAvailable == false, "An unsafe archive was available.");
	Assert(
		unsafePackage.StatusMessage.Contains("unsafe path"),
		"The unsafe archive reason was not reported.");

	IReadOnlyList<ResolvedWinPeDriverPackage> selectedPackages =
		packageStore.ResolveSelection(
			new[] { "example-winpe", "dell-winpe" });
	Assert(selectedPackages.Count == 2, "The selected packages were not resolved.");
	Assert(
		selectedPackages.All(package => File.Exists(package.ArchivePath)),
		"A selected archive path does not exist.");

	List<(Int32 Completed, Int32 Total, String ArchiveName)>
		extractionProgress = new();
	DiskBuilder.ExtractDriverArchives(
		selectedPackages.Select(package => package.ArchivePath).ToList(),
		driverExtractionDirectory,
		(completed, total, archiveName) =>
			extractionProgress.Add((completed, total, archiveName)));
	Assert(
		Directory.GetFiles(
			driverExtractionDirectory,
			"*.inf",
			SearchOption.AllDirectories).Length == 2,
		"The selected driver archives were not extracted independently.");
	Assert(
		Directory.GetDirectories(
			driverExtractionDirectory,
			"package-*",
			SearchOption.TopDirectoryOnly).Length == 2,
		"Selected driver packages did not receive isolated extraction directories.");
	Assert(
		extractionProgress.Count == 2,
		"Driver extraction did not report progress for every file.");
	Assert(
		extractionProgress[^1].Completed == extractionProgress[^1].Total,
		"Driver extraction progress did not reach the total file count.");
	Assert(
		extractionProgress.All(update =>
			!String.IsNullOrWhiteSpace(update.ArchiveName)),
		"Driver extraction progress omitted the archive name.");

	Boolean duplicateRejected = false;

	try
	{
		packageStore.ResolveSelection(new[] { "dell-winpe", "DELL-WINPE" });
	}
	catch (ArgumentException)
	{
		duplicateRejected = true;
	}

	Assert(duplicateRejected, "A duplicate package selection was accepted.");

	Boolean unavailableRejected = false;

	try
	{
		packageStore.ResolveSelection(new[] { "hp-winpe" });
	}
	catch (InvalidDataException)
	{
		unavailableRejected = true;
	}

	Assert(unavailableRejected, "An unavailable package selection was accepted.");
}
finally
{
	if (Directory.Exists(driverPackageStoreDirectory))
	{
		Directory.Delete(driverPackageStoreDirectory, recursive: true);
	}

	if (Directory.Exists(driverExtractionDirectory))
	{
		Directory.Delete(driverExtractionDirectory, recursive: true);
	}
}

Console.WriteLine("PASS: external WinPE driver package store and selection.");

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
	using GrpcChannel channel = NamedPipeGrpcChannelFactory.Create();

	OsImageDeployControl.OsImageDeployControlClient client =
		new OsImageDeployControl.OsImageDeployControlClient(channel);

	GetServiceStatusResponse status = await client.GetServiceStatusAsync(
		new GetServiceStatusRequest(),
		deadline: DateTime.UtcNow.AddSeconds(10));

	Assert(
		status.ServiceName == GrpcTransportDefaults.ServiceName,
		"The service returned an unexpected name.");
	Assert(
		status.ApiVersion == GrpcTransportDefaults.ApiVersion,
		"The service returned an unexpected API version.");
	Assert(!status.ReadOnly, "The service did not report media-build support.");

	Console.WriteLine("PASS: Live named-pipe service status call.");

	ListWinPeDriverPackagesResponse driverPackages =
		await client.ListWinPeDriverPackagesAsync(
			new ListWinPeDriverPackagesRequest(),
			deadline: DateTime.UtcNow.AddSeconds(10));

	Assert(
		driverPackages.Packages.Any(package => package.PackageId == "dell-winpe") &&
		driverPackages.Packages.Any(package => package.PackageId == "hp-winpe"),
		"The service driver-package catalog omitted built-in OEM guidance.");

	Console.WriteLine(
		$"PASS: Live WinPE driver package catalog ({driverPackages.Packages.Count} package(s)).");

	ListEligibleUsbTargetsResponse eligibleTargets =
		await client.ListEligibleUsbTargetsAsync(
			new ListEligibleUsbTargetsRequest(),
			deadline: DateTime.UtcNow.AddSeconds(10));

	Assert(
		eligibleTargets.Targets.All(target =>
			target.BusType.Equals("USB", StringComparison.OrdinalIgnoreCase) &&
			!target.IsSystemDisk &&
			!target.IsBootDisk &&
			!target.IsReadOnly &&
			!target.IsOffline &&
			!target.IsClustered &&
			target.HealthStatus == 0),
		"The service returned a USB target outside the safety envelope.");

	Console.WriteLine(
		$"PASS: Live USB target enumeration ({eligibleTargets.Targets.Count} eligible target(s)).");

	Boolean unconfirmedRequestRejected = false;

	try
	{
		await client.StartUsbMediaBuildAsync(
			new StartUsbMediaBuildRequest
			{
				SelectedTarget = GrpcTargetMapper.ToMessage(original),
				DestructiveActionConfirmed = false
			},
			deadline: DateTime.UtcNow.AddSeconds(10));
	}
	catch (RpcException exception) when (
		exception.StatusCode == StatusCode.InvalidArgument)
	{
		unconfirmedRequestRejected = true;
	}

	Assert(
		unconfirmedRequestRejected,
		"The service accepted an unconfirmed destructive request.");

	Console.WriteLine("PASS: Live destructive-operation confirmation guard.");

	GetActiveUsbMediaBuildResponse activeOperation =
		await client.GetActiveUsbMediaBuildAsync(
			new GetActiveUsbMediaBuildRequest(),
			deadline: DateTime.UtcNow.AddSeconds(10));

	Assert(
		!activeOperation.HasActiveOperation,
		"The service reported an active USB operation when none was started.");

	Console.WriteLine("PASS: Live no-active-operation query.");

	GetLastUsbMediaBuildResponse lastOperation =
		await client.GetLastUsbMediaBuildAsync(
			new GetLastUsbMediaBuildRequest(),
			deadline: DateTime.UtcNow.AddSeconds(10));

	if (lastOperation.HasOperation)
	{
		Assert(
			!String.IsNullOrWhiteSpace(lastOperation.Operation?.OperationId),
			"The service returned a last operation without an identity.");
	}

	Console.WriteLine("PASS: Live last-operation status query.");

	await client.GetWinPeCacheStatusAsync(
		new GetWinPeCacheStatusRequest(),
		deadline: DateTime.UtcNow.AddSeconds(10));

	Console.WriteLine("PASS: Live WinPE cache status call.");

	Boolean unconfirmedCacheClearRejected = false;

	try
	{
		await client.ClearWinPeCacheAsync(
			new ClearWinPeCacheRequest
			{
				CacheClearConfirmed = false
			},
			deadline: DateTime.UtcNow.AddSeconds(10));
	}
	catch (RpcException exception) when (
		exception.StatusCode == StatusCode.InvalidArgument)
	{
		unconfirmedCacheClearRejected = true;
	}

	Assert(
		unconfirmedCacheClearRejected,
		"The service accepted an unconfirmed WinPE cache-clear request.");

	Console.WriteLine("PASS: Live WinPE cache-clear confirmation guard.");
}

static void Assert(Boolean condition, String message)
{
	if (!condition)
	{
		throw new InvalidOperationException(message);
	}
}

static void CreateDriverPackage(
	String storeDirectory,
	String packageId,
	String displayName,
	String manufacturer,
	String sourceVersion,
	Boolean unsafeArchivePath = false)
{
	String packageDirectory = Path.Combine(storeDirectory, packageId);
	Directory.CreateDirectory(packageDirectory);

	WinPeDriverPackageManifest manifest =
		new WinPeDriverPackageManifest
		{
			PackageId = packageId,
			DisplayName = displayName,
			Manufacturer = manufacturer,
			SourceVersion = sourceVersion,
			PreparedUtc = DateTimeOffset.UtcNow
		};

	File.WriteAllText(
		Path.Combine(packageDirectory, "package.json"),
		JsonSerializer.Serialize(manifest));

	using ZipArchive archive = ZipFile.Open(
		Path.Combine(packageDirectory, "drivers.zip"),
		ZipArchiveMode.Create);
	ZipArchiveEntry entry = archive.CreateEntry(
		unsafeArchivePath
			? "../unsafe.inf"
			: "drivers/test-driver.inf");

	using StreamWriter writer = new StreamWriter(entry.Open());
	writer.WriteLine("[Version]");
}
