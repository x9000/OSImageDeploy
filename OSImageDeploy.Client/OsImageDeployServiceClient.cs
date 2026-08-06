using Grpc.Core;
using Grpc.Net.Client;
using OSImageDeploy.Contracts;
using OSImageDeploy.Engine;
using OSImageDeploy.Transport.Grpc;
using OSImageDeploy.Transport.Grpc.V1;

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
