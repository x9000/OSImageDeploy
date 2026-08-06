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
				UsbMediaOperationUpdate response =
					await _client.StartUsbMediaBuildAsync(
						new StartUsbMediaBuildRequest
						{
							SelectedTarget =
								GrpcTargetMapper.ToMessage(request.Target),
							DestructiveActionConfirmed =
								request.DestructiveActionConfirmed,
							RebuildWinPeCache = request.RebuildWinPeCache
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
