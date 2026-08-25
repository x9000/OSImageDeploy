namespace OSImageDeploy.Contracts
{
	public sealed class WinPeDriverPackageDescriptor
	{
		public required String PackageId { get; init; }

		public required String DisplayName { get; init; }

		public required String Manufacturer { get; init; }

		public String SourceVersion { get; init; } = "";

		public String SourcePageUrl { get; init; } = "";

		public String PreparationInstructions { get; init; } = "";

		public String PreparationFileExtension { get; init; } = "";

		public Boolean CanPrepareAutomatically { get; init; }

		public Boolean IsAvailable { get; init; }

		public Int32 DriverCount { get; init; }

		public Int64 ArchiveSizeBytes { get; init; }

		public String ArchiveSha256 { get; init; } = "";

		public String StatusMessage { get; init; } = "";
	}
}
