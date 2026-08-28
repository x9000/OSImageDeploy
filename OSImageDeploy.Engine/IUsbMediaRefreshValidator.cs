using OSImageDeploy.Contracts;

namespace OSImageDeploy.Engine
{
	public interface IUsbMediaRefreshValidator
	{
		Task<UsbMediaRefreshValidationResult> ValidateRefreshAsync(
			UsbTargetDescriptor target,
			CancellationToken cancellationToken = default);
	}
}
