namespace OSImageDeploy.Contracts
{
	public sealed class UsbMediaRefreshValidationResult
	{
		public Boolean IsEligible { get; init; }

		public String Summary { get; init; } = String.Empty;

		public IReadOnlyList<String> Warnings { get; init; } =
			Array.Empty<String>();

		public UsbTargetDescriptor? ResolvedTarget { get; init; }

		public UsbMediaPartitionDescriptor? BootPartition { get; init; }

		public UsbMediaPartitionDescriptor? DataPartition { get; init; }
	}
}
