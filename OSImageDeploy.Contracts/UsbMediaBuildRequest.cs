namespace OSImageDeploy.Contracts
{
	public sealed class UsbMediaBuildRequest
	{
		public required UsbTargetDescriptor Target { get; init; }

		public Boolean RebuildWinPeCache { get; init; }

		public IReadOnlyList<String> WinPeDriverPackageIds { get; init; } =
			Array.Empty<String>();

		public Boolean DestructiveActionConfirmed { get; init; }
	}
}
