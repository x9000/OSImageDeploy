using OSImageDeploy.Contracts;
using OSImageDeploy.Transport.Grpc.V1;
using ContractCacheState = OSImageDeploy.Contracts.WinPeCacheState;
using GrpcCacheState = OSImageDeploy.Transport.Grpc.V1.WinPeCacheState;

namespace OSImageDeploy.Transport.Grpc
{
	public static class GrpcWinPeCacheMapper
	{
		public static WinPeCacheStatusResponse ToMessage(
			WinPeCacheStatusSnapshot snapshot)
		{
			ArgumentNullException.ThrowIfNull(snapshot);

			return new WinPeCacheStatusResponse
			{
				State = ToMessage(snapshot.State),
				CreatedUnixTimeMs = snapshot.CreatedUtc?.ToUnixTimeMilliseconds() ?? 0,
				HasCreatedTime = snapshot.CreatedUtc.HasValue,
				ArchiveSizeBytes = snapshot.ArchiveSizeBytes
			};
		}

		public static WinPeCacheStatusSnapshot ToSnapshot(
			WinPeCacheStatusResponse response)
		{
			ArgumentNullException.ThrowIfNull(response);

			return new WinPeCacheStatusSnapshot
			{
				State = ToContract(response.State),
				CreatedUtc = response.HasCreatedTime
					? DateTimeOffset.FromUnixTimeMilliseconds(
						response.CreatedUnixTimeMs)
					: null,
				ArchiveSizeBytes = response.ArchiveSizeBytes
			};
		}

		private static GrpcCacheState ToMessage(ContractCacheState state)
		{
			return state switch
			{
				ContractCacheState.Incomplete =>
					GrpcCacheState.Incomplete,
				ContractCacheState.Available =>
					GrpcCacheState.Available,
				_ => GrpcCacheState.Missing
			};
		}

		private static ContractCacheState ToContract(GrpcCacheState state)
		{
			return state switch
			{
				GrpcCacheState.Incomplete =>
					ContractCacheState.Incomplete,
				GrpcCacheState.Available =>
					ContractCacheState.Available,
				_ => ContractCacheState.Missing
			};
		}
	}
}
