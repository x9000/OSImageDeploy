using OSImageDeploy.Contracts;
using OSImageDeploy.Transport.Grpc.V1;

namespace OSImageDeploy.Transport.Grpc
{
	public static class GrpcWinPeDriverPackageMapper
	{
		public static WinPeDriverPackage ToMessage(
			WinPeDriverPackageDescriptor descriptor)
		{
			ArgumentNullException.ThrowIfNull(descriptor);

			return new WinPeDriverPackage
			{
				PackageId = descriptor.PackageId,
				DisplayName = descriptor.DisplayName,
				Manufacturer = descriptor.Manufacturer,
				SourceVersion = descriptor.SourceVersion,
				SourcePageUrl = descriptor.SourcePageUrl,
				PreparationInstructions = descriptor.PreparationInstructions,
				PreparationFileExtension =
					descriptor.PreparationFileExtension,
				CanPrepareAutomatically =
					descriptor.CanPrepareAutomatically,
				IsAvailable = descriptor.IsAvailable,
				DriverCount = descriptor.DriverCount,
				ArchiveSizeBytes = descriptor.ArchiveSizeBytes,
				ArchiveSha256 = descriptor.ArchiveSha256,
				StatusMessage = descriptor.StatusMessage
			};
		}

		public static WinPeDriverPackageDescriptor ToDescriptor(
			WinPeDriverPackage message)
		{
			ArgumentNullException.ThrowIfNull(message);

			return new WinPeDriverPackageDescriptor
			{
				PackageId = message.PackageId,
				DisplayName = message.DisplayName,
				Manufacturer = message.Manufacturer,
				SourceVersion = message.SourceVersion,
				SourcePageUrl = message.SourcePageUrl,
				PreparationInstructions = message.PreparationInstructions,
				PreparationFileExtension =
					message.PreparationFileExtension,
				CanPrepareAutomatically =
					message.CanPrepareAutomatically,
				IsAvailable = message.IsAvailable,
				DriverCount = message.DriverCount,
				ArchiveSizeBytes = message.ArchiveSizeBytes,
				ArchiveSha256 = message.ArchiveSha256,
				StatusMessage = message.StatusMessage
			};
		}
	}
}
