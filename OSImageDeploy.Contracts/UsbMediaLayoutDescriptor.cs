namespace OSImageDeploy.Contracts
{
	public sealed class UsbMediaLayoutDescriptor
	{
		public String PartitionStyle { get; init; } = String.Empty;

		public IReadOnlyList<UsbMediaPartitionDescriptor> Partitions { get; init; } =
			Array.Empty<UsbMediaPartitionDescriptor>();
	}

	public sealed class UsbMediaPartitionDescriptor
	{
		public UInt32 PartitionNumber { get; init; }

		public UInt64 SizeBytes { get; init; }

		public String FileSystem { get; init; } = String.Empty;

		public String Label { get; init; } = String.Empty;

		public String DriveLetter { get; init; } = String.Empty;

		public String GptType { get; init; } = String.Empty;

		public Boolean IsHidden { get; init; }

		public Boolean HasDriverPacksFolder { get; init; }

		public Boolean HasWindowsImagesFolder { get; init; }
	}
}
