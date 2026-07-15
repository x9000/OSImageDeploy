#nullable disable

using System;

namespace Utilities
{
	public sealed class WinPeCacheManifest
	{
		public Int32 SchemaVersion { get; set; } = 1;

		public DateTime CreatedUtc { get; set; }

		public String ApplicationVersion { get; set; } = "";

		public String AdkVersion { get; set; } = "";

		public String Architecture { get; set; } = "amd64";

		public String WinPeClientHash { get; set; } = "";

		public String DriverPackagesHash { get; set; } = "";

		public String PackageConfigurationHash { get; set; } = "";

		public String ArchiveHash { get; set; } = "";
	}
}