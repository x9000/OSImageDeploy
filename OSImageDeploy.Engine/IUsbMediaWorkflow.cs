using OSImageDeploy.Contracts;

namespace OSImageDeploy.Engine
{
	public interface IUsbMediaWorkflow :
		IUsbTargetDiscovery,
		IUsbTargetValidator
	{
		Task CreateUsbMediaAsync(
			UsbMediaBuildRequest request,
			IProgress<OperationProgress> progress,
			CancellationToken cancellationToken = default);
	}
}
