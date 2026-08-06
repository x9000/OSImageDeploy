using Grpc.Core;
using OSImageDeploy.Contracts;
using OSImageDeploy.Engine;
using OSImageDeploy.Transport.Grpc;
using OSImageDeploy.Transport.Grpc.V1;
using System.Reflection;

namespace OSImageDeploy.Service.Services
{
	public sealed class OsImageDeployControlService :
		OsImageDeployControl.OsImageDeployControlBase
	{
		private readonly IUsbTargetDiscovery _targetDiscovery;
		private readonly IUsbTargetValidator _targetValidator;
		private readonly ILogger<OsImageDeployControlService> _logger;

		public OsImageDeployControlService(
			IUsbTargetDiscovery targetDiscovery,
			IUsbTargetValidator targetValidator,
			ILogger<OsImageDeployControlService> logger)
		{
			_targetDiscovery = targetDiscovery;
			_targetValidator = targetValidator;
			_logger = logger;
		}

		public override Task<GetServiceStatusResponse> GetServiceStatus(
			GetServiceStatusRequest request,
			ServerCallContext context)
		{
			Version? version =
				Assembly.GetExecutingAssembly().GetName().Version;

			return Task.FromResult(
				new GetServiceStatusResponse
				{
					ServiceName = GrpcTransportDefaults.ServiceName,
					ServiceVersion = version?.ToString() ?? "Unknown",
					ApiVersion = GrpcTransportDefaults.ApiVersion,
					ReadOnly = true
				});
		}

		public override async Task<ListEligibleUsbTargetsResponse>
			ListEligibleUsbTargets(
				ListEligibleUsbTargetsRequest request,
				ServerCallContext context)
		{
			try
			{
				IReadOnlyList<UsbTargetDescriptor> targets =
					await _targetDiscovery.GetEligibleTargetsAsync(
						context.CancellationToken);

				ListEligibleUsbTargetsResponse response =
					new ListEligibleUsbTargetsResponse();

				response.Targets.AddRange(
					targets.Select(GrpcTargetMapper.ToMessage));

				return response;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (UnauthorizedAccessException exception)
			{
				_logger.LogError(
					exception,
					"The service could not enumerate Windows storage targets.");

				throw new RpcException(
					new Status(
						StatusCode.FailedPrecondition,
						"The service does not have permission to enumerate storage targets."));
			}
			catch (Exception exception)
			{
				_logger.LogError(
					exception,
					"USB target enumeration failed.");

				throw new RpcException(
					new Status(
						StatusCode.Internal,
						"USB target enumeration failed."));
			}
		}

		public override async Task<ValidateUsbTargetResponse> ValidateUsbTarget(
			ValidateUsbTargetRequest request,
			ServerCallContext context)
		{
			if (request.SelectedTarget == null ||
				String.IsNullOrWhiteSpace(request.SelectedTarget.TargetId))
			{
				throw new RpcException(
					new Status(
						StatusCode.InvalidArgument,
						"A selected USB target identity is required."));
			}

			try
			{
				UsbTargetDescriptor selectedTarget =
					GrpcTargetMapper.ToDescriptor(request.SelectedTarget);

				UsbTargetValidationResult result =
					await _targetValidator.ValidateTargetAsync(
						selectedTarget,
						context.CancellationToken);

				ValidateUsbTargetResponse response =
					new ValidateUsbTargetResponse
					{
						IsValid = result.IsValid,
						Summary = result.Summary
					};

				response.Warnings.AddRange(result.Warnings);

				return response;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (OverflowException)
			{
				throw new RpcException(
					new Status(
						StatusCode.InvalidArgument,
						"The selected target contains an invalid health status."));
			}
			catch (Exception exception) when (exception is not RpcException)
			{
				_logger.LogError(
					exception,
					"USB target validation failed.");

				throw new RpcException(
					new Status(
						StatusCode.Internal,
						"USB target validation failed."));
			}
		}
	}
}
