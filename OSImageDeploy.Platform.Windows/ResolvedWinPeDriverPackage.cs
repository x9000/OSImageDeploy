using OSImageDeploy.Contracts;

namespace OSImageDeploy.Platform.Windows
{
	public sealed class ResolvedWinPeDriverPackage
	{
		public required WinPeDriverPackageDescriptor Descriptor { get; init; }

		public required String ArchivePath { get; init; }
	}
}
