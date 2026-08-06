using OSImageDeploy.Contracts;
using OSImageDeploy.Engine;

List<(String Name, Action Test)> tests = new()
{
	("Eligible USB target is accepted", EligibleUsbTargetIsAccepted),
	("System disk is rejected", SystemDiskIsRejected),
	("Boot disk is rejected", BootDiskIsRejected),
	("Non-USB disk is rejected", NonUsbDiskIsRejected),
	("Read-only disk is rejected", ReadOnlyDiskIsRejected),
	("Offline disk is rejected", OfflineDiskIsRejected),
	("Clustered disk is rejected", ClusteredDiskIsRejected),
	("Unhealthy disk is rejected", UnhealthyDiskIsRejected),
	("Changed size is rejected", ChangedSizeIsRejected),
	("Missing target is rejected", MissingTargetIsRejected),
	("Reassigned disk number is accepted with warning", ReassignedDiskNumberIsAccepted)
};

List<String> failures = new List<String>();

foreach ((String name, Action test) in tests)
{
	try
	{
		test();
		Console.WriteLine($"PASS: {name}");
	}
	catch (Exception exception)
	{
		failures.Add($"{name}: {exception.Message}");
		Console.WriteLine($"FAIL: {name} - {exception.Message}");
	}
}

Console.WriteLine();
Console.WriteLine($"{tests.Count - failures.Count}/{tests.Count} checks passed.");

if (failures.Count > 0)
{
	Environment.ExitCode = 1;
}

static void EligibleUsbTargetIsAccepted()
{
	UsbTargetDescriptor target = CreateTarget();
	UsbTargetValidationResult result = UsbTargetSafetyPolicy.Validate(target, target);

	Assert(result.IsValid, result.Summary);
}

static void SystemDiskIsRejected()
{
	AssertRejected(CreateTarget(), CreateTarget(isSystemDisk: true), "system");
}

static void BootDiskIsRejected()
{
	AssertRejected(CreateTarget(), CreateTarget(isBootDisk: true), "boot");
}

static void NonUsbDiskIsRejected()
{
	AssertRejected(CreateTarget(), CreateTarget(busType: "NVMe"), "not connected by USB");
}

static void ReadOnlyDiskIsRejected()
{
	AssertRejected(CreateTarget(), CreateTarget(isReadOnly: true), "read-only");
}

static void OfflineDiskIsRejected()
{
	AssertRejected(CreateTarget(), CreateTarget(isOffline: true), "offline");
}

static void ClusteredDiskIsRejected()
{
	AssertRejected(CreateTarget(), CreateTarget(isClustered: true), "cluster");
}

static void UnhealthyDiskIsRejected()
{
	AssertRejected(CreateTarget(), CreateTarget(healthStatus: 2), "unhealthy");
}

static void ChangedSizeIsRejected()
{
	AssertRejected(
		CreateTarget(),
		CreateTarget(sizeBytes: 128UL * 1024 * 1024 * 1024),
		"size has changed");
}

static void MissingTargetIsRejected()
{
	UsbTargetValidationResult result = UsbTargetSafetyPolicy.Validate(
		CreateTarget(),
		null);

	Assert(!result.IsValid, "A missing target was accepted.");
}

static void ReassignedDiskNumberIsAccepted()
{
	UsbTargetValidationResult result = UsbTargetSafetyPolicy.Validate(
		CreateTarget(diskNumber: 3),
		CreateTarget(diskNumber: 7));

	Assert(result.IsValid, result.Summary);
	Assert(
		result.Warnings.Any(warning => warning.Contains("reassigned")),
		"The disk-number reassignment was not reported.");
}

static void AssertRejected(
	UsbTargetDescriptor expected,
	UsbTargetDescriptor current,
	String expectedMessage)
{
	UsbTargetValidationResult result = UsbTargetSafetyPolicy.Validate(
		expected,
		current);

	Assert(!result.IsValid, "An unsafe target was accepted.");
	Assert(
		result.Summary.Contains(
			expectedMessage,
			StringComparison.OrdinalIgnoreCase),
		$"Expected validation message containing '{expectedMessage}', " +
		$"but received '{result.Summary}'.");
}

static UsbTargetDescriptor CreateTarget(
	UInt32 diskNumber = 3,
	String busType = "USB",
	UInt64 sizeBytes = 64UL * 1024 * 1024 * 1024,
	Boolean isSystemDisk = false,
	Boolean isBootDisk = false,
	Boolean isReadOnly = false,
	Boolean isOffline = false,
	Boolean isClustered = false,
	UInt16 healthStatus = 0)
{
	return new UsbTargetDescriptor
	{
		TargetId = "disk-test-identity",
		DiskNumber = diskNumber,
		DisplayName = "Test USB target",
		Model = "Test model",
		SerialNumber = "TEST-SERIAL",
		BusType = busType,
		SizeBytes = sizeBytes,
		IsSystemDisk = isSystemDisk,
		IsBootDisk = isBootDisk,
		IsReadOnly = isReadOnly,
		IsOffline = isOffline,
		IsClustered = isClustered,
		HealthStatus = healthStatus
	};
}

static void Assert(Boolean condition, String message)
{
	if (!condition)
	{
		throw new InvalidOperationException(message);
	}
}
