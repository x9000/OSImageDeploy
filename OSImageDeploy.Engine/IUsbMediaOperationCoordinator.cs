using OSImageDeploy.Contracts;

namespace OSImageDeploy.Engine
{
	public interface IUsbMediaOperationCoordinator
	{
		UsbMediaOperationSnapshot Start(UsbMediaBuildRequest request);

		UsbMediaOperationSnapshot GetStatus(String operationId);

		UsbMediaOperationSnapshot? GetActiveOperation();

		UsbMediaOperationSnapshot? GetLastOperation();

		IAsyncEnumerable<UsbMediaOperationSnapshot> WatchAsync(
			String operationId,
			CancellationToken cancellationToken = default);

		UsbMediaOperationSnapshot RequestCancellation(String operationId);
	}
}
