namespace OSImageDeploy.Contracts
{
	public sealed class UsbTargetDescriptor
	{
		public required String TargetId { get; init; }

		public required UInt32 DiskNumber { get; init; }

		public required String DisplayName { get; init; }

		public String Model { get; init; } = String.Empty;

		public String SerialNumber { get; init; } = String.Empty;

		public String BusType { get; init; } = String.Empty;

		public UInt64 SizeBytes { get; init; }

		public Boolean IsSystemDisk { get; init; }

		public Boolean IsBootDisk { get; init; }

		public Boolean IsReadOnly { get; init; }

		public Boolean IsOffline { get; init; }

		public Boolean IsClustered { get; init; }

		public UInt16 HealthStatus { get; init; }
	}
}
