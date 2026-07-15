#nullable disable

using System;

namespace Utilities
{
	public sealed class WinPeBuildResult
	{
		public String WorkingFolder { get; init; } = "";

		public String MediaFolder { get; init; } = "";

		public String DriverFolder { get; init; } = "";

		public String MountFolder { get; init; } = "";

		public String BootWimPath { get; init; } = "";

		public Boolean WasLoadedFromCache { get; init; }
	}
}