using OSImageDeploy.Contracts;
using System.Runtime.CompilerServices;

namespace OSImageDeploy.Engine
{
	public sealed class UsbMediaOperationCoordinator :
		IUsbMediaOperationCoordinator,
		IDisposable
	{
		private readonly Object _sync = new Object();
		private readonly IUsbMediaWorkflow _workflow;
		private readonly IUsbMediaOperationStore? _store;
		private readonly Action<Exception>? _persistenceErrorHandler;
		private readonly Dictionary<String, OperationRecord> _operations =
			new Dictionary<String, OperationRecord>(StringComparer.Ordinal);

		private OperationRecord? _activeOperation;
		private UsbMediaOperationSnapshot? _lastOperation;
		private Boolean _disposed;

		public UsbMediaOperationCoordinator(
			IUsbMediaWorkflow workflow,
			IUsbMediaOperationStore? store = null,
			Action<Exception>? persistenceErrorHandler = null)
		{
			_workflow = workflow ??
				throw new ArgumentNullException(nameof(workflow));
			_store = store;
			_persistenceErrorHandler = persistenceErrorHandler;

			RestoreLastOperation();
		}

		public UsbMediaOperationSnapshot Start(UsbMediaBuildRequest request)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			ArgumentNullException.ThrowIfNull(request);

			if (!request.DestructiveActionConfirmed)
			{
				throw new ArgumentException(
					"Explicit confirmation is required for USB media creation.",
					nameof(request));
			}

			if (String.IsNullOrWhiteSpace(request.Target.TargetId))
			{
				throw new ArgumentException(
					"A stable USB target identity is required.",
					nameof(request));
			}

			OperationRecord operation;

			lock (_sync)
			{
				if (_activeOperation != null &&
					!_activeOperation.Current.IsTerminal)
				{
					throw new InvalidOperationException(
						"Another USB media operation is already running.");
				}

				String operationId = Guid.NewGuid().ToString("N");
				DateTimeOffset startedUtc = DateTimeOffset.UtcNow;

				operation = new OperationRecord(
					request,
					new UsbMediaOperationSnapshot
					{
						OperationId = operationId,
						State = UsbMediaOperationState.Pending,
						StartedUtc = startedUtc
					});

				_operations.Add(operationId, operation);
				_activeOperation = operation;
				_lastOperation = operation.Current;
				Persist(operation.Current);
			}

			operation.ExecutionTask = Task.Run(
				() => ExecuteAsync(operation));

			return operation.Current;
		}

		public UsbMediaOperationSnapshot GetStatus(String operationId)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			lock (_sync)
			{
				return GetOperation(operationId).Current;
			}
		}

		public UsbMediaOperationSnapshot? GetActiveOperation()
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			lock (_sync)
			{
				if (_activeOperation == null ||
					_activeOperation.Current.IsTerminal)
				{
					return null;
				}

				return _activeOperation.Current;
			}
		}

		public UsbMediaOperationSnapshot? GetLastOperation()
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			lock (_sync)
			{
				return _lastOperation;
			}
		}

		public async IAsyncEnumerable<UsbMediaOperationSnapshot> WatchAsync(
			String operationId,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			Int32 nextIndex = 0;

			while (true)
			{
				List<UsbMediaOperationSnapshot> updates;
				Task changedTask;

				lock (_sync)
				{
					OperationRecord operation = GetOperation(operationId);
					updates = operation.History.Skip(nextIndex).ToList();
					nextIndex = operation.History.Count;
					changedTask = operation.Changed.Task;
				}

				foreach (UsbMediaOperationSnapshot update in updates)
				{
					yield return update;

					if (update.IsTerminal)
					{
						yield break;
					}
				}

				await changedTask.WaitAsync(cancellationToken)
					.ConfigureAwait(false);
			}
		}

		public UsbMediaOperationSnapshot RequestCancellation(String operationId)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			lock (_sync)
			{
				OperationRecord operation = GetOperation(operationId);

				if (operation.Current.IsTerminal)
				{
					return operation.Current;
				}

				if (operation.Current.State !=
					UsbMediaOperationState.CancellationRequested)
				{
					AppendSnapshot(
						operation,
						UsbMediaOperationState.CancellationRequested,
						operation.Current.Progress);
				}

				operation.Cancellation.Cancel();

				return operation.Current;
			}
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;

			lock (_sync)
			{
				foreach (OperationRecord operation in _operations.Values)
				{
					operation.Cancellation.Cancel();
				}
			}
		}

		private async Task ExecuteAsync(OperationRecord operation)
		{
			try
			{
				UsbMediaBuildRequest request = operation.Request ??
					throw new InvalidOperationException(
						"The USB media operation request is unavailable.");
				Boolean isRefresh = request.BuildMode ==
					UsbMediaBuildMode.RefreshBootPartition;
				AppendSnapshot(
					operation,
					UsbMediaOperationState.Running,
					new OperationProgress
					{
						Stage = "Starting",
						Message = isRefresh
							? "Starting USB boot partition refresh."
							: "Starting USB media creation.",
						OverallPercent = 0
					});

				IProgress<OperationProgress> progress =
					new InlineProgress<OperationProgress>(
						update => AppendSnapshot(
							operation,
							operation.Current.State ==
								UsbMediaOperationState.CancellationRequested
								? UsbMediaOperationState.CancellationRequested
								: UsbMediaOperationState.Running,
							update));

				await _workflow.CreateUsbMediaAsync(
					request,
					progress,
					operation.Cancellation.Token);

				AppendSnapshot(
					operation,
					UsbMediaOperationState.Succeeded,
					new OperationProgress
					{
						Stage = "Complete",
						Message = isRefresh
							? "USB boot partition refresh completed. BuildData was preserved."
							: "USB media creation completed.",
						OverallPercent = 100
					});
			}
			catch (OperationCanceledException)
			{
				AppendSnapshot(
					operation,
					UsbMediaOperationState.Cancelled,
					operation.Current.Progress);
			}
			catch (Exception exception)
			{
				AppendSnapshot(
					operation,
					UsbMediaOperationState.Failed,
					operation.Current.Progress,
					exception.Message);
			}
		}

		private void AppendSnapshot(
			OperationRecord operation,
			UsbMediaOperationState state,
			OperationProgress? progress,
			String errorMessage = "")
		{
			lock (_sync)
			{
				UsbMediaOperationSnapshot snapshot =
					new UsbMediaOperationSnapshot
					{
						OperationId = operation.Current.OperationId,
						State = state,
						Progress = progress,
						ErrorMessage = errorMessage,
						StartedUtc = operation.Current.StartedUtc,
						CompletedUtc = IsTerminal(state)
							? DateTimeOffset.UtcNow
							: null
					};

				operation.Current = snapshot;
				operation.History.Add(snapshot);
				_lastOperation = snapshot;
				Persist(snapshot);

				TaskCompletionSource changed = operation.Changed;
				operation.Changed = CreateChangedSignal();
				changed.TrySetResult();
			}
		}

		private void RestoreLastOperation()
		{
			if (_store == null)
			{
				return;
			}

			try
			{
				UsbMediaOperationSnapshot? snapshot = _store.Load();

				if (snapshot == null)
				{
					return;
				}

				if (!snapshot.IsTerminal)
				{
					snapshot = new UsbMediaOperationSnapshot
					{
						OperationId = snapshot.OperationId,
						State = UsbMediaOperationState.Failed,
						Progress = new OperationProgress
						{
							Stage = "Interrupted",
							Message =
								"The service stopped before USB media creation completed.",
							OverallPercent = snapshot.Progress?.OverallPercent,
							StagePercent = snapshot.Progress?.StagePercent,
							LogLevel = OperationLogLevel.Error
						},
						ErrorMessage =
							"The service restarted before the operation completed. " +
							"Inspect the USB target before starting a new operation.",
						StartedUtc = snapshot.StartedUtc,
						CompletedUtc = DateTimeOffset.UtcNow
					};

					Persist(snapshot);
				}

				OperationRecord restored = new OperationRecord(snapshot);
				_operations[snapshot.OperationId] = restored;
				_lastOperation = snapshot;
			}
			catch (Exception exception)
			{
				ReportPersistenceError(exception);
			}
		}

		private void Persist(UsbMediaOperationSnapshot snapshot)
		{
			if (_store == null)
			{
				return;
			}

			try
			{
				_store.Save(snapshot);
			}
			catch (Exception exception)
			{
				ReportPersistenceError(exception);
			}
		}

		private void ReportPersistenceError(Exception exception)
		{
			try
			{
				_persistenceErrorHandler?.Invoke(exception);
			}
			catch
			{
				// Status persistence must never interrupt a destructive workflow.
			}
		}

		private OperationRecord GetOperation(String operationId)
		{
			if (String.IsNullOrWhiteSpace(operationId) ||
				!_operations.TryGetValue(operationId, out OperationRecord? operation))
			{
				throw new KeyNotFoundException(
					"The USB media operation could not be found.");
			}

			return operation;
		}

		private static Boolean IsTerminal(UsbMediaOperationState state)
		{
			return state == UsbMediaOperationState.Succeeded ||
				state == UsbMediaOperationState.Failed ||
				state == UsbMediaOperationState.Cancelled;
		}

		private static TaskCompletionSource CreateChangedSignal()
		{
			return new TaskCompletionSource(
				TaskCreationOptions.RunContinuationsAsynchronously);
		}

		private sealed class OperationRecord
		{
			public OperationRecord(
				UsbMediaBuildRequest request,
				UsbMediaOperationSnapshot initialSnapshot)
			{
				Request = request;
				Current = initialSnapshot;
				History.Add(initialSnapshot);
			}

			public OperationRecord(UsbMediaOperationSnapshot initialSnapshot)
			{
				Current = initialSnapshot;
				History.Add(initialSnapshot);
			}

			public UsbMediaBuildRequest? Request { get; }

			public UsbMediaOperationSnapshot Current { get; set; }

			public List<UsbMediaOperationSnapshot> History { get; } =
				new List<UsbMediaOperationSnapshot>();

			public CancellationTokenSource Cancellation { get; } =
				new CancellationTokenSource();

			public TaskCompletionSource Changed { get; set; } =
				CreateChangedSignal();

			public Task? ExecutionTask { get; set; }
		}

		private sealed class InlineProgress<T> : IProgress<T>
		{
			private readonly Action<T> _handler;

			public InlineProgress(Action<T> handler)
			{
				_handler = handler;
			}

			public void Report(T value)
			{
				_handler(value);
			}
		}
	}
}
