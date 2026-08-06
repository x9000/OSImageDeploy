using OSImageDeploy.Contracts;

namespace OSImageDeploy.Engine
{
	public interface IUsbTargetDiscovery
	{
		Task<IReadOnlyList<UsbTargetDescriptor>> GetEligibleTargetsAsync(
			CancellationToken cancellationToken = default);
	}
}
