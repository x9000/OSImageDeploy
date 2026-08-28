namespace Utilities
{
	public sealed class UsbBootPartitionRefreshTarget
	{
		public UInt32 DiskNumber { get; init; }

		public UInt32 PartitionNumber { get; init; }

		public UInt64 SizeBytes { get; init; }

		public String DriveLetter { get; init; } = String.Empty;

		public String DataDriveLetter { get; init; } = String.Empty;
	}
}
