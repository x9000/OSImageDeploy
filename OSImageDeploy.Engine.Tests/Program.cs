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

List<(String Name, Func<Task> Test)> asyncTests = new()
{
	("Unconfirmed media operation is rejected", UnconfirmedOperationIsRejected),
	("Media operation completes with progress", OperationCompletesWithProgress),
	("Active media operation is reported until completion", ActiveOperationIsReportedUntilCompletion),
	("Concurrent media operation is rejected", ConcurrentOperationIsRejected),
	("Media operation cancellation is recorded", OperationCancellationIsRecorded)
};

foreach ((String name, Func<Task> test) in asyncTests)
{
	try
	{
		await test();
		Console.WriteLine($"PASS: {name}");
	}
	catch (Exception exception)
	{
		failures.Add($"{name}: {exception.Message}");
		Console.WriteLine($"FAIL: {name} - {exception.Message}");
	}
}

Console.WriteLine();
Int32 totalChecks = tests.Count + asyncTests.Count;
Console.WriteLine($"{totalChecks - failures.Count}/{totalChecks} checks passed.");

if (failures.Count > 0)
{
	Environment.ExitCode = 1;
}

static void EligibleUsbTargetIsAccepted()
{
	UsbTargetDescriptor target = CreateTarget();
	UsbTargetValidationResult result = UsbTargetSafetyPolicy.Validate(target, target);

	Assert(result.IsValid, result.Summary);
	Assert(
		result.ResolvedTarget?.DiskNumber == target.DiskNumber,
		"The validated target was not returned to the caller.");
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
	Assert(
		result.ResolvedTarget?.DiskNumber == 7,
		"The service-resolved disk number was not returned.");
}

static Task UnconfirmedOperationIsRejected()
{
	using UsbMediaOperationCoordinator coordinator =
		new UsbMediaOperationCoordinator(new FakeUsbMediaWorkflow());

	Boolean rejected = false;

	try
	{
		coordinator.Start(CreateBuildRequest(confirmed: false));
	}
	catch (ArgumentException)
	{
		rejected = true;
	}

	Assert(rejected, "An unconfirmed destructive operation was accepted.");

	return Task.CompletedTask;
}

static async Task OperationCompletesWithProgress()
{
	FakeUsbMediaWorkflow workflow = new FakeUsbMediaWorkflow
	{
		CreateHandler = (request, progress, cancellationToken) =>
		{
			progress.Report(
				new OperationProgress
				{
					Stage = "Test",
					Message = "Fake workflow progress.",
					OverallPercent = 50
				});

			return Task.CompletedTask;
		}
	};

	using UsbMediaOperationCoordinator coordinator =
		new UsbMediaOperationCoordinator(workflow);

	UsbMediaOperationSnapshot started =
		coordinator.Start(CreateBuildRequest());

	List<UsbMediaOperationSnapshot> updates =
		await ReadUntilTerminalAsync(coordinator, started.OperationId);

	Assert(
		updates.Any(update => update.Progress?.OverallPercent == 50),
		"The workflow progress update was not recorded.");
	Assert(
		updates[^1].State == UsbMediaOperationState.Succeeded,
		"The completed workflow was not marked as successful.");
}

static async Task ConcurrentOperationIsRejected()
{
	TaskCompletionSource enteredWorkflow =
		new TaskCompletionSource(
			TaskCreationOptions.RunContinuationsAsynchronously);

	FakeUsbMediaWorkflow workflow = new FakeUsbMediaWorkflow
	{
		CreateHandler = async (request, progress, cancellationToken) =>
		{
			enteredWorkflow.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		}
	};

	using UsbMediaOperationCoordinator coordinator =
		new UsbMediaOperationCoordinator(workflow);

	UsbMediaOperationSnapshot first =
		coordinator.Start(CreateBuildRequest());

	await enteredWorkflow.Task.WaitAsync(TimeSpan.FromSeconds(5));

	Boolean rejected = false;

	try
	{
		coordinator.Start(CreateBuildRequest());
	}
	catch (InvalidOperationException)
	{
		rejected = true;
	}

	Assert(rejected, "A concurrent destructive operation was accepted.");

	coordinator.RequestCancellation(first.OperationId);
	await ReadUntilTerminalAsync(coordinator, first.OperationId);
}

static async Task ActiveOperationIsReportedUntilCompletion()
{
	TaskCompletionSource enteredWorkflow =
		new TaskCompletionSource(
			TaskCreationOptions.RunContinuationsAsynchronously);
	TaskCompletionSource releaseWorkflow =
		new TaskCompletionSource(
			TaskCreationOptions.RunContinuationsAsynchronously);

	FakeUsbMediaWorkflow workflow = new FakeUsbMediaWorkflow
	{
		CreateHandler = async (request, progress, cancellationToken) =>
		{
			enteredWorkflow.TrySetResult();
			await releaseWorkflow.Task.WaitAsync(cancellationToken);
		}
	};

	using UsbMediaOperationCoordinator coordinator =
		new UsbMediaOperationCoordinator(workflow);

	Assert(
		coordinator.GetActiveOperation() == null,
		"A new coordinator reported an active operation.");

	UsbMediaOperationSnapshot started =
		coordinator.Start(CreateBuildRequest());

	await enteredWorkflow.Task.WaitAsync(TimeSpan.FromSeconds(5));

	UsbMediaOperationSnapshot? active =
		coordinator.GetActiveOperation();

	Assert(active != null, "The running operation was not reported as active.");
	Assert(
		active!.OperationId == started.OperationId,
		"The active operation identity changed.");

	releaseWorkflow.TrySetResult();
	await ReadUntilTerminalAsync(coordinator, started.OperationId);

	Assert(
		coordinator.GetActiveOperation() == null,
		"A completed operation was still reported as active.");
}

static async Task OperationCancellationIsRecorded()
{
	TaskCompletionSource enteredWorkflow =
		new TaskCompletionSource(
			TaskCreationOptions.RunContinuationsAsynchronously);

	FakeUsbMediaWorkflow workflow = new FakeUsbMediaWorkflow
	{
		CreateHandler = async (request, progress, cancellationToken) =>
		{
			enteredWorkflow.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		}
	};

	using UsbMediaOperationCoordinator coordinator =
		new UsbMediaOperationCoordinator(workflow);

	UsbMediaOperationSnapshot started =
		coordinator.Start(CreateBuildRequest());

	await enteredWorkflow.Task.WaitAsync(TimeSpan.FromSeconds(5));
	coordinator.RequestCancellation(started.OperationId);

	List<UsbMediaOperationSnapshot> updates =
		await ReadUntilTerminalAsync(coordinator, started.OperationId);

	Assert(
		updates.Any(update =>
			update.State == UsbMediaOperationState.CancellationRequested),
		"The cancellation request was not recorded.");
	Assert(
		updates[^1].State == UsbMediaOperationState.Cancelled,
		"The cancelled workflow was not marked as cancelled.");
}

static UsbMediaBuildRequest CreateBuildRequest(Boolean confirmed = true)
{
	return new UsbMediaBuildRequest
	{
		Target = CreateTarget(),
		DestructiveActionConfirmed = confirmed
	};
}

static async Task<List<UsbMediaOperationSnapshot>> ReadUntilTerminalAsync(
	IUsbMediaOperationCoordinator coordinator,
	String operationId)
{
	using CancellationTokenSource timeout =
		new CancellationTokenSource(TimeSpan.FromSeconds(5));

	List<UsbMediaOperationSnapshot> updates =
		new List<UsbMediaOperationSnapshot>();

	await foreach (UsbMediaOperationSnapshot update in
		coordinator.WatchAsync(operationId, timeout.Token))
	{
		updates.Add(update);

		if (update.IsTerminal)
		{
			break;
		}
	}

	return updates;
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

sealed class FakeUsbMediaWorkflow : IUsbMediaWorkflow
{
	public Func<
		UsbMediaBuildRequest,
		IProgress<OperationProgress>,
		CancellationToken,
		Task> CreateHandler { get; init; } =
			(request, progress, cancellationToken) => Task.CompletedTask;

	public Task CreateUsbMediaAsync(
		UsbMediaBuildRequest request,
		IProgress<OperationProgress> progress,
		CancellationToken cancellationToken = default)
	{
		return CreateHandler(request, progress, cancellationToken);
	}

	public Task<IReadOnlyList<UsbTargetDescriptor>> GetEligibleTargetsAsync(
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IReadOnlyList<UsbTargetDescriptor>>(
			Array.Empty<UsbTargetDescriptor>());
	}

	public Task<UsbTargetValidationResult> ValidateTargetAsync(
		UsbTargetDescriptor target,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(
			new UsbTargetValidationResult
			{
				IsValid = true,
				Summary = "Fake target is valid.",
				ResolvedTarget = target
			});
	}
}
