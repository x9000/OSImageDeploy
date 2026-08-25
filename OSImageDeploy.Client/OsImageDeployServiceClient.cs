using Grpc.Core;
using Grpc.Net.Client;
using OSImageDeploy.Contracts;
using OSImageDeploy.Engine;
using OSImageDeploy.Transport.Grpc;
using OSImageDeploy.Transport.Grpc.V1;
using System.Runtime.CompilerServices;

namespace OSImageDeploy.Client
{
	public sealed class OsImageDeployServiceClient :
		IUsbTargetDiscovery,
		IUsbTargetValidator,
		IDisposable
	{
		private static readonly TimeSpan RequestTimeout =
			TimeSpan.FromSeconds(15);
		private static readonly TimeSpan PackagePreparationTimeout =
			TimeSpan.FromMinutes(20);

		private readonly GrpcChannel _channel;
		private readonly OsImageDeployControl.OsImageDeployControlClient _client;

		public OsImageDeployServiceClient()
			: this(NamedPipeGrpcChannelFactory.Create())
		{
		}

		internal OsImageDeployServiceClient(GrpcChannel channel)
		{
			_channel = channel ?? throw new ArgumentNullException(nameof(channel));
			_client = new OsImageDeployControl.OsImageDeployControlClient(channel);
		}

		public async Task<IReadOnlyList<UsbTargetDescriptor>>
			GetEligibleTargetsAsync(
				CancellationToken cancellationToken = default)
		{
			try
			{
				ListEligibleUsbTargetsResponse response =
					await _client.ListEligibleUsbTargetsAsync(
						new ListEligibleUsbTargetsRequest(),
						deadline: DateTime.UtcNow.Add(RequestTimeout),
						cancellationToken: cancellationToken);

				return response.Targets
					.Select(GrpcTargetMapper.ToDescriptor)
					.ToList();
			}
			catch (RpcException exception)
			{
				throw TranslateException(exception, cancellationToken);
			}
		}

		public async Task<UsbTargetValidationResult> ValidateTargetAsync(
			UsbTargetDescriptor target,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(target);

			try
			{
				ValidateUsbTargetResponse response =
					await _client.ValidateUsbTargetAsync(
						new ValidateUsbTargetRequest
						{
							SelectedTarget = GrpcTargetMapper.ToMessage(target)
						},
						deadline: DateTime.UtcNow.Add(RequestTimeout),
						cancellationToken: cancellationToken);

				return new UsbTargetValidationResult
				{
					IsValid = response.IsValid,
					Summary = response.Summary,
					Warnings = response.Warnings.ToList(),
					ResolvedTarget = response.ResolvedTarget == null
						? null
						: GrpcTargetMapper.ToDescriptor(response.ResolvedTarget)
				};
			}
			catch (RpcException exception)
			{
				throw TranslateException(exception, cancellationToken);
			}
		}

		public async Task<UsbMediaOperationSnapshot> StartUsbMediaBuildAsync(
			UsbMediaBuildRequest request,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);

			try
			{
				StartUsbMediaBuildRequest message =
					new StartUsbMediaBuildRequest
					{
						SelectedTarget =
							GrpcTargetMapper.ToMessage(request.Target),
						DestructiveActionConfirmed =
							request.DestructiveActionConfirmed,
						RebuildWinPeCache = request.RebuildWinPeCache
					};

				message.WinPeDriverPackageIds.AddRange(
					request.WinPeDriverPackageIds);

				UsbMediaOperationUpdate response =
					await _client.StartUsbMediaBuildAsync(
						message,
						deadline: DateTime.UtcNow.Add(RequestTimeout),
						cancellationToken: cancellationToken);

				return GrpcOperationMapper.ToSnapshot(response);
			}
			catch (RpcException exception)
			{
				throw TranslateException(exception, cancellationToken);
			}
		}

		public async Task<IReadOnlyList<WinPeDriverPackageDescriptor>>
			GetWinPeDriverPackagesAsync(
				CancellationToken cancellationToken = default)
		{
			try
			{
				ListWinPeDriverPackagesResponse response =
					await _client.ListWinPeDriverPackagesAsync(
						new ListWinPeDriverPackagesRequest(),
						deadline: DateTime.UtcNow.Add(RequestTimeout),
						cancellationToken: cancellationToken);

				return response.Packages
					.Select(GrpcWinPeDriverPackageMapper.ToDescriptor)
					.ToList();
			}
			catch (RpcException exception)
			{
				throw TranslateException(exception, cancellationToken);
			}
		}

		public async Task<WinPeDriverPackageDescriptor>
			PrepareWinPeDriverPackageAsync(
				String packageId,
				String sourceFilePath,
				String sourceVersion,
				Boolean replaceExistingConfirmed,
				CancellationToken cancellationToken = default)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
			ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

			try
			{
				WinPeDriverPackage response =
					await _client.PrepareWinPeDriverPackageAsync(
						new PrepareWinPeDriverPackageRequest
						{
							PackageId = packageId,
							SourceFilePath = sourceFilePath,
							SourceVersion = sourceVersion ?? "",
							ReplaceExistingConfirmed =
								replaceExistingConfirmed
						},
						deadline: DateTime.UtcNow.Add(
							PackagePreparationTimeout),
						cancellationToken: cancellationToken);

				return GrpcWinPeDriverPackageMapper.ToDescriptor(response);
			}
			catch (RpcException exception)
			{
				throw TranslateException(exception, cancellationToken);
			}
		}

		public async IAsyncEnumerable<UsbMediaOperationSnapshot>
			WatchUsbMediaBuildAsync(
				String operationId,
				[EnumeratorCancellation]
				CancellationToken cancellationToken = default)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

			AsyncServerStreamingCall<UsbMediaOperationUpdate>? call = null;

			try
			{
				call = _client.WatchUsbMediaBuild(
					new UsbMediaOperationRequest
					{
						OperationId = operationId
					},
					cancellationToken: cancellationToken);

				while (true)
				{
					Boolean hasNext;
					UsbMediaOperationUpdate? update = null;

					try
					{
						hasNext = await call.ResponseStream.MoveNext(
							cancellationToken);

						if (hasNext)
						{
							update = call.ResponseStream.Current;
						}
					}
					catch (RpcException exception)
					{
						throw TranslateException(
							exception,
							cancellationToken);
					}

					if (!hasNext)
					{
						yield break;
					}

					yield return GrpcOperationMapper.ToSnapshot(update!);
				}
			}
			finally
			{
				call?.Dispose();
			}
		}

		public async Task<UsbMediaOperationSnapshot> GetUsbMediaBuildStatusAsync(
			String operationId,
			CancellationToken cancellationToken = default)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

			try
			{
				UsbMediaOperationUpdate response =
					await _client.GetUsbMediaBuildStatusAsync(
						new UsbMediaOperationRequest
						{
							OperationId = operationId
						},
						deadline: DateTime.UtcNow.Add(RequestTimeout),
						cancellationToken: cancellationToken);

				return GrpcOperationMapper.ToSnapshot(response);
			}
			catch (RpcException exception)
			{
				throw TranslateException(exception, cancellationToken);
			}
		}

		public async Task<UsbMediaOperationSnapshot?>
			GetActiveUsbMediaBuildAsync(
				CancellationToken cancellationToken = default)
		{
			try
			{
				GetActiveUsbMediaBuildResponse response =
					await _client.GetActiveUsbMediaBuildAsync(
						new GetActiveUsbMediaBuildRequest(),
						deadline: DateTime.UtcNow.Add(RequestTimeout),
						cancellationToken: cancellationToken);

				if (!response.HasActiveOperation ||
					response.Operation == null)
				{
					return null;
				}

				return GrpcOperationMapper.ToSnapshot(response.Operation);
			}
			catch (RpcException exception)
			{
				throw TranslateException(exception, cancellationToken);
			}
		}

		public async Task<UsbMediaOperationSnapshot?>
			GetLastUsbMediaBuildAsync(
				CancellationToken cancellationToken = default)
		{
			try
			{
				GetLastUsbMediaBuildResponse response =
					await _client.GetLastUsbMediaBuildAsync(
						new GetLastUsbMediaBuildRequest(),
						deadline: DateTime.UtcNow.Add(RequestTimeout),
						cancellationToken: cancellationToken);

				if (!response.HasOperation || response.Operation == null)
				{
					return null;
				}

				return GrpcOperationMapper.ToSnapshot(response.Operation);
			}
			catch (RpcException exception)
			{
				throw TranslateException(exception, cancellationToken);
			}
		}

		public async Task<UsbMediaOperationSnapshot> CancelUsbMediaBuildAsync(
			String operationId,
			CancellationToken cancellationToken = default)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

			try
			{
				UsbMediaOperationUpdate response =
					await _client.CancelUsbMediaBuildAsync(
						new UsbMediaOperationRequest
						{
							OperationId = operationId
						},
						deadline: DateTime.UtcNow.Add(RequestTimeout),
						cancellationToken: cancellationToken);

				return GrpcOperationMapper.ToSnapshot(response);
			}
			catch (RpcException exception)
			{
				throw TranslateException(exception, cancellationToken);
			}
		}

		public async Task<WinPeCacheStatusSnapshot> GetWinPeCacheStatusAsync(
			CancellationToken cancellationToken = default)
		{
			try
			{
				WinPeCacheStatusResponse response =
					await _client.GetWinPeCacheStatusAsync(
						new GetWinPeCacheStatusRequest(),
						deadline: DateTime.UtcNow.Add(RequestTimeout),
						cancellationToken: cancellationToken);

				return GrpcWinPeCacheMapper.ToSnapshot(response);
			}
			catch (RpcException exception)
			{
				throw TranslateException(exception, cancellationToken);
			}
		}

		public async Task<WinPeCacheStatusSnapshot> ClearWinPeCacheAsync(
			Boolean cacheClearConfirmed,
			CancellationToken cancellationToken = default)
		{
			try
			{
				WinPeCacheStatusResponse response =
					await _client.ClearWinPeCacheAsync(
						new ClearWinPeCacheRequest
						{
							CacheClearConfirmed = cacheClearConfirmed
						},
						deadline: DateTime.UtcNow.Add(RequestTimeout),
						cancellationToken: cancellationToken);

				return GrpcWinPeCacheMapper.ToSnapshot(response);
			}
			catch (RpcException exception)
			{
				throw TranslateException(exception, cancellationToken);
			}
		}

		public void Dispose()
		{
			_channel.Dispose();
		}

		private static Exception TranslateException(
			RpcException exception,
			CancellationToken cancellationToken)
		{
			if (exception.StatusCode == StatusCode.Cancelled &&
				cancellationToken.IsCancellationRequested)
			{
				return new OperationCanceledException(cancellationToken);
			}

			String detail = String.IsNullOrWhiteSpace(exception.Status.Detail)
				? "The OS Image Deploy service could not complete the request."
				: exception.Status.Detail;

			return new OsImageDeployServiceException(detail, exception);
		}
	}
}
