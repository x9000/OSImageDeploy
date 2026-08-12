using OSImageDeploy.Contracts;

namespace OSImageDeploy.Engine
{
	public interface IWinPeCacheService
	{
		Task<WinPeCacheStatusSnapshot> GetStatusAsync(
			CancellationToken cancellationToken = default);

		Task<WinPeCacheStatusSnapshot> ClearAsync(
			CancellationToken cancellationToken = default);
	}
}
