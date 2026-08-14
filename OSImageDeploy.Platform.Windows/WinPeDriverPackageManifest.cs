namespace OSImageDeploy.Platform.Windows
{
	public sealed class WinPeDriverPackageManifest
	{
		public Int32 SchemaVersion { get; init; } = 1;

		public required String PackageId { get; init; }

		public required String DisplayName { get; init; }

		public required String Manufacturer { get; init; }

		public String SourceVersion { get; init; } = "";

		public String SourcePageUrl { get; init; } = "";

		public DateTimeOffset PreparedUtc { get; init; }
	}
}
