using OSImageDeploy.Contracts;

namespace OSImageDeploy.Engine
{
	public interface IUsbTargetValidator
	{
		Task<UsbTargetValidationResult> ValidateTargetAsync(
			UsbTargetDescriptor target,
			CancellationToken cancellationToken = default);
	}
}
