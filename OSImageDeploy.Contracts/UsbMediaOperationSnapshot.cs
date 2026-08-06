namespace OSImageDeploy.Contracts
{
	public sealed class UsbMediaOperationSnapshot
	{
		public required String OperationId { get; init; }

		public UsbMediaOperationState State { get; init; }

		public OperationProgress? Progress { get; init; }

		public String ErrorMessage { get; init; } = String.Empty;

		public DateTimeOffset StartedUtc { get; init; }

		public DateTimeOffset? CompletedUtc { get; init; }

		public Boolean IsTerminal =>
			State == UsbMediaOperationState.Succeeded ||
			State == UsbMediaOperationState.Failed ||
			State == UsbMediaOperationState.Cancelled;
	}

	public enum UsbMediaOperationState
	{
		Pending,
		Running,
		CancellationRequested,
		Succeeded,
		Failed,
		Cancelled
	}
}
