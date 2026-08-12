using OSImageDeploy.Contracts;
using OSImageDeploy.Transport.Grpc.V1;

namespace OSImageDeploy.Transport.Grpc
{
	public static class GrpcTargetMapper
	{
		public static UsbTarget ToMessage(UsbTargetDescriptor target)
		{
			ArgumentNullException.ThrowIfNull(target);

			return new UsbTarget
			{
				TargetId = target.TargetId,
				DiskNumber = target.DiskNumber,
				DisplayName = target.DisplayName,
				Model = target.Model,
				SerialNumber = target.SerialNumber,
				BusType = target.BusType,
				SizeBytes = target.SizeBytes,
				IsSystemDisk = target.IsSystemDisk,
				IsBootDisk = target.IsBootDisk,
				IsReadOnly = target.IsReadOnly,
				IsOffline = target.IsOffline,
				IsClustered = target.IsClustered,
				HealthStatus = target.HealthStatus
			};
		}

		public static UsbTargetDescriptor ToDescriptor(UsbTarget target)
		{
			ArgumentNullException.ThrowIfNull(target);

			return new UsbTargetDescriptor
			{
				TargetId = target.TargetId,
				DiskNumber = target.DiskNumber,
				DisplayName = target.DisplayName,
				Model = target.Model,
				SerialNumber = target.SerialNumber,
				BusType = target.BusType,
				SizeBytes = target.SizeBytes,
				IsSystemDisk = target.IsSystemDisk,
				IsBootDisk = target.IsBootDisk,
				IsReadOnly = target.IsReadOnly,
				IsOffline = target.IsOffline,
				IsClustered = target.IsClustered,
				HealthStatus = checked((UInt16)target.HealthStatus)
			};
		}
	}
}
