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
	("Reassigned disk number is accepted with warning", ReassignedDiskNumberIsAccepted),
	("Existing OSImageDeploy layout is refreshable", ExistingLayoutIsRefreshable),
	("Refresh rejects a non-FAT32 boot partition", RefreshRejectsNonFatBootPartition),
	("Refresh rejects missing preserved-data folders", RefreshRejectsMissingDataFolders),
	("Refresh rejects unexpected extra partitions", RefreshRejectsExtraPartitions)
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
	("Media operation cancellation is recorded", OperationCancellationIsRecorded),
	("Completed operation status survives coordinator recreation", CompletedOperationStatusSurvivesRestart),
	("Interrupted operation is reconciled without resuming", InterruptedOperationIsReconciledWithoutResuming),
	("Status persistence failure does not stop the workflow", PersistenceFailureDoesNotStopWorkflow)
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

static void ExistingLayoutIsRefreshable()
{
	UsbMediaRefreshValidationResult result =
		UsbMediaRefreshSafetyPolicy.Validate(CreateRefreshLayout());

	Assert(result.IsEligible, result.Summary);
	Assert(
		result.BootPartition?.PartitionNumber == 1,
		"The boot partition was not identified.");
	Assert(
		result.DataPartition?.PartitionNumber == 2,
		"The preserved data partition was not identified.");
}

static void RefreshRejectsNonFatBootPartition()
{
	UsbMediaRefreshValidationResult result =
		UsbMediaRefreshSafetyPolicy.Validate(
			CreateRefreshLayout(bootFileSystem: "NTFS"));

	Assert(!result.IsEligible, "A non-FAT32 boot partition was accepted.");
	Assert(
		result.Summary.Contains("not FAT32", StringComparison.OrdinalIgnoreCase),
		"The boot filesystem rejection was not explained.");
}

static void RefreshRejectsMissingDataFolders()
{
	UsbMediaRefreshValidationResult result =
		UsbMediaRefreshSafetyPolicy.Validate(
			CreateRefreshLayout(hasDriverPacksFolder: false));

	Assert(!result.IsEligible, "A data partition without DriverPacks was accepted.");
	Assert(
		result.Summary.Contains("DriverPacks", StringComparison.OrdinalIgnoreCase),
		"The missing DriverPacks folder was not explained.");
}

static void RefreshRejectsExtraPartitions()
{
	UsbMediaLayoutDescriptor layout = CreateRefreshLayout();
	UsbMediaRefreshValidationResult result =
		UsbMediaRefreshSafetyPolicy.Validate(
			new UsbMediaLayoutDescriptor
			{
				PartitionStyle = layout.PartitionStyle,
				Partitions = layout.Partitions.Concat(
					new[]
					{
						new UsbMediaPartitionDescriptor
						{
							PartitionNumber = 3,
							SizeBytes = 1024 * 1024,
							FileSystem = "NTFS",
							Label = "Unexpected",
							DriveLetter = "Z"
						}
					}).ToList()
			});

	Assert(!result.IsEligible, "A layout with an unexpected partition was accepted.");
	Assert(
		result.Summary.Contains("exactly", StringComparison.OrdinalIgnoreCase),
		"The unexpected partition rejection was not explained.");
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

static async Task CompletedOperationStatusSurvivesRestart()
{
	FakeUsbMediaOperationStore store = new FakeUsbMediaOperationStore();
	String operationId;

	using (UsbMediaOperationCoordinator coordinator =
		new UsbMediaOperationCoordinator(new FakeUsbMediaWorkflow(), store))
	{
		UsbMediaOperationSnapshot started =
			coordinator.Start(CreateBuildRequest());
		operationId = started.OperationId;

		await ReadUntilTerminalAsync(coordinator, operationId);
	}

	using UsbMediaOperationCoordinator restoredCoordinator =
		new UsbMediaOperationCoordinator(new FakeUsbMediaWorkflow(), store);

	UsbMediaOperationSnapshot? restored =
		restoredCoordinator.GetLastOperation();

	Assert(restored != null, "The persisted operation was not restored.");
	Assert(
		restored!.OperationId == operationId,
		"The restored operation identity changed.");
	Assert(
		restored.State == UsbMediaOperationState.Succeeded,
		"The successful terminal state was not restored.");
	Assert(
		restoredCoordinator.GetStatus(operationId).State ==
			UsbMediaOperationState.Succeeded,
		"The restored operation could not be queried by identity.");
	Assert(
		restoredCoordinator.GetActiveOperation() == null,
		"A restored terminal operation was reported as active.");
}

static Task InterruptedOperationIsReconciledWithoutResuming()
{
	Int32 workflowInvocations = 0;
	FakeUsbMediaOperationStore store = new FakeUsbMediaOperationStore
	{
		Snapshot = new UsbMediaOperationSnapshot
		{
			OperationId = "interrupted-operation",
			State = UsbMediaOperationState.Running,
			Progress = new OperationProgress
			{
				Stage = "Copying",
				Message = "Copying files.",
				OverallPercent = 63
			},
			StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
		}
	};
	FakeUsbMediaWorkflow workflow = new FakeUsbMediaWorkflow
	{
		CreateHandler = (request, progress, cancellationToken) =>
		{
			workflowInvocations++;
			return Task.CompletedTask;
		}
	};

	using UsbMediaOperationCoordinator coordinator =
		new UsbMediaOperationCoordinator(workflow, store);

	UsbMediaOperationSnapshot? restored = coordinator.GetLastOperation();

	Assert(restored != null, "The interrupted operation was not restored.");
	Assert(
		restored!.State == UsbMediaOperationState.Failed,
		"The interrupted operation was not reconciled as failed.");
	Assert(
		restored.Progress?.Stage == "Interrupted",
		"The interruption was not clearly identified.");
	Assert(
		restored.Progress?.OverallPercent == 63,
		"The last recorded progress was not preserved.");
	Assert(
		restored.CompletedUtc.HasValue,
		"The reconciled operation was not made terminal.");
	Assert(
		workflowInvocations == 0,
		"Restoring status incorrectly resumed the destructive workflow.");
	Assert(
		coordinator.GetActiveOperation() == null,
		"The interrupted operation was incorrectly reported as active.");
	Assert(
		store.Snapshot?.State == UsbMediaOperationState.Failed,
		"The reconciled terminal state was not persisted.");

	return Task.CompletedTask;
}

static async Task PersistenceFailureDoesNotStopWorkflow()
{
	Int32 persistenceErrors = 0;
	FakeUsbMediaOperationStore store = new FakeUsbMediaOperationStore
	{
		SaveException = new IOException("Fake persistence failure.")
	};

	using UsbMediaOperationCoordinator coordinator =
		new UsbMediaOperationCoordinator(
			new FakeUsbMediaWorkflow(),
			store,
			exception => persistenceErrors++);

	UsbMediaOperationSnapshot started =
		coordinator.Start(CreateBuildRequest());
	List<UsbMediaOperationSnapshot> updates =
		await ReadUntilTerminalAsync(coordinator, started.OperationId);

	Assert(
		updates[^1].State == UsbMediaOperationState.Succeeded,
		"A status persistence failure stopped the workflow.");
	Assert(
		persistenceErrors > 0,
		"The status persistence failure was not reported.");
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

static UsbMediaLayoutDescriptor CreateRefreshLayout(
	String bootFileSystem = "FAT32",
	Boolean hasDriverPacksFolder = true,
	Boolean hasWindowsImagesFolder = true)
{
	return new UsbMediaLayoutDescriptor
	{
		PartitionStyle = "GPT",
		Partitions = new[]
		{
			new UsbMediaPartitionDescriptor
			{
				PartitionNumber = 1,
				SizeBytes = 4UL * 1024 * 1024 * 1024,
				FileSystem = bootFileSystem,
				Label = "WinPE",
				DriveLetter = "E"
			},
			new UsbMediaPartitionDescriptor
			{
				PartitionNumber = 2,
				SizeBytes = 60UL * 1024 * 1024 * 1024,
				FileSystem = "NTFS",
				Label = "BuildData",
				DriveLetter = "F",
				HasDriverPacksFolder = hasDriverPacksFolder,
				HasWindowsImagesFolder = hasWindowsImagesFolder
			}
		}
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

sealed class FakeUsbMediaOperationStore : IUsbMediaOperationStore
{
	public UsbMediaOperationSnapshot? Snapshot { get; set; }

	public Exception? LoadException { get; init; }

	public Exception? SaveException { get; init; }

	public UsbMediaOperationSnapshot? Load()
	{
		if (LoadException != null)
		{
			throw LoadException;
		}

		return Snapshot;
	}

	public void Save(UsbMediaOperationSnapshot snapshot)
	{
		if (SaveException != null)
		{
			throw SaveException;
		}

		Snapshot = snapshot;
	}
}
