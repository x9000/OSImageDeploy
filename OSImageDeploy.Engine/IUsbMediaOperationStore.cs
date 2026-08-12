using OSImageDeploy.Contracts;

namespace OSImageDeploy.Engine
{
	public interface IUsbMediaOperationStore
	{
		UsbMediaOperationSnapshot? Load();

		void Save(UsbMediaOperationSnapshot snapshot);
	}
}
