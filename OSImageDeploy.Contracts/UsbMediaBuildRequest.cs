namespace OSImageDeploy.Contracts
{
	public sealed class UsbMediaBuildRequest
	{
		public required UsbTargetDescriptor Target { get; init; }

		public Boolean RebuildWinPeCache { get; init; }
	}
}
