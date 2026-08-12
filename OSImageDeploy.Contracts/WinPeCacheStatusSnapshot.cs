namespace OSImageDeploy.Contracts
{
	public enum WinPeCacheState
	{
		Missing,
		Incomplete,
		Available
	}

	public sealed class WinPeCacheStatusSnapshot
	{
		public WinPeCacheState State { get; init; }

		public DateTimeOffset? CreatedUtc { get; init; }

		public Int64 ArchiveSizeBytes { get; init; }
	}
}
