namespace OSImageDeploy.Contracts
{
	public sealed class UsbTargetValidationResult
	{
		public Boolean IsValid { get; init; }

		public String Summary { get; init; } = String.Empty;

		public IReadOnlyList<String> Warnings { get; init; } =
			Array.Empty<String>();

		public UsbTargetDescriptor? ResolvedTarget { get; init; }
	}
}
