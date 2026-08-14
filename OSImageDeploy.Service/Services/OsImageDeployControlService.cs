using Grpc.Core;
using OSImageDeploy.Contracts;
using OSImageDeploy.Engine;
using OSImageDeploy.Platform.Windows;
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
		private readonly IUsbMediaOperationCoordinator _operationCoordinator;
		private readonly IWinPeCacheService _winPeCacheService;
		private readonly WindowsWinPeDriverPackageStore _driverPackageStore;
		private readonly ILogger<OsImageDeployControlService> _logger;

		public OsImageDeployControlService(
			IUsbTargetDiscovery targetDiscovery,
			IUsbTargetValidator targetValidator,
			IUsbMediaOperationCoordinator operationCoordinator,
			IWinPeCacheService winPeCacheService,
			WindowsWinPeDriverPackageStore driverPackageStore,
			ILogger<OsImageDeployControlService> logger)
		{
			_targetDiscovery = targetDiscovery;
			_targetValidator = targetValidator;
			_operationCoordinator = operationCoordinator;
			_winPeCacheService = winPeCacheService;
			_driverPackageStore = driverPackageStore;
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
					ReadOnly = false
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
						Summary = result.Summary,
						ResolvedTarget = result.ResolvedTarget == null
							? null
							: GrpcTargetMapper.ToMessage(result.ResolvedTarget)
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

		public override async Task<UsbMediaOperationUpdate> StartUsbMediaBuild(
			StartUsbMediaBuildRequest request,
			ServerCallContext context)
		{
			if (!request.DestructiveActionConfirmed)
			{
				throw new RpcException(
					new Status(
						StatusCode.InvalidArgument,
						"Explicit destructive-action confirmation is required."));
			}

			if (request.SelectedTarget == null ||
				String.IsNullOrWhiteSpace(request.SelectedTarget.TargetId))
			{
				throw new RpcException(
					new Status(
						StatusCode.InvalidArgument,
						"A selected USB target identity is required."));
			}

			UsbTargetDescriptor selectedTarget =
				GrpcTargetMapper.ToDescriptor(request.SelectedTarget);

			UsbTargetValidationResult validation =
				await _targetValidator.ValidateTargetAsync(
					selectedTarget,
					context.CancellationToken);

			if (!validation.IsValid)
			{
				throw new RpcException(
					new Status(
						StatusCode.FailedPrecondition,
						validation.Summary));
			}

			context.CancellationToken.ThrowIfCancellationRequested();

			try
			{
				IReadOnlyList<ResolvedWinPeDriverPackage> driverPackages =
					_driverPackageStore.ResolveSelection(
						request.WinPeDriverPackageIds);

				UsbMediaOperationSnapshot operation =
					_operationCoordinator.Start(
						new UsbMediaBuildRequest
						{
							Target = selectedTarget,
							RebuildWinPeCache = request.RebuildWinPeCache,
							WinPeDriverPackageIds = driverPackages
								.Select(package => package.Descriptor.PackageId)
								.ToList(),
							DestructiveActionConfirmed = true
						});

				_logger.LogWarning(
					"USB media operation {OperationId} was authorised for target {TargetId}, selected as disk {DiskNumber}.",
					operation.OperationId,
					selectedTarget.TargetId,
					selectedTarget.DiskNumber);

				_ = AuditOperationCompletionAsync(operation.OperationId);

				return GrpcOperationMapper.ToMessage(operation);
			}
			catch (InvalidOperationException exception)
			{
				throw new RpcException(
					new Status(
						StatusCode.ResourceExhausted,
						exception.Message));
			}
			catch (ArgumentException exception)
			{
				throw new RpcException(
					new Status(
						StatusCode.InvalidArgument,
						exception.Message));
			}
			catch (InvalidDataException exception)
			{
				throw new RpcException(
					new Status(
						StatusCode.FailedPrecondition,
						exception.Message));
			}
		}

		public override Task<ListWinPeDriverPackagesResponse>
			ListWinPeDriverPackages(
				ListWinPeDriverPackagesRequest request,
				ServerCallContext context)
		{
			try
			{
				ListWinPeDriverPackagesResponse response = new();
				response.Packages.AddRange(
					_driverPackageStore.GetPackages()
						.Select(GrpcWinPeDriverPackageMapper.ToMessage));
				return Task.FromResult(response);
			}
			catch (Exception exception)
			{
				_logger.LogError(
					exception,
					"Failed to read the WinPE driver package catalog.");
				throw new RpcException(
					new Status(
						StatusCode.Internal,
						"The WinPE driver package catalog could not be read."));
			}
		}

		public override async Task WatchUsbMediaBuild(
			UsbMediaOperationRequest request,
			IServerStreamWriter<UsbMediaOperationUpdate> responseStream,
			ServerCallContext context)
		{
			try
			{
				await foreach (UsbMediaOperationSnapshot update in
					_operationCoordinator.WatchAsync(
						request.OperationId,
						context.CancellationToken))
				{
					await responseStream.WriteAsync(
						GrpcOperationMapper.ToMessage(update));
				}
			}
			catch (KeyNotFoundException exception)
			{
				throw new RpcException(
					new Status(StatusCode.NotFound, exception.Message));
			}
		}

		public override Task<UsbMediaOperationUpdate> GetUsbMediaBuildStatus(
			UsbMediaOperationRequest request,
			ServerCallContext context)
		{
			return Task.FromResult(
				GetOperationUpdate(request.OperationId));
		}

		public override Task<GetActiveUsbMediaBuildResponse>
			GetActiveUsbMediaBuild(
				GetActiveUsbMediaBuildRequest request,
				ServerCallContext context)
		{
			UsbMediaOperationSnapshot? operation =
				_operationCoordinator.GetActiveOperation();

			GetActiveUsbMediaBuildResponse response =
				new GetActiveUsbMediaBuildResponse
				{
					HasActiveOperation = operation != null
				};

			if (operation != null)
			{
				response.Operation =
					GrpcOperationMapper.ToMessage(operation);
			}

			return Task.FromResult(response);
		}

		public override Task<GetLastUsbMediaBuildResponse>
			GetLastUsbMediaBuild(
				GetLastUsbMediaBuildRequest request,
				ServerCallContext context)
		{
			UsbMediaOperationSnapshot? operation =
				_operationCoordinator.GetLastOperation();

			GetLastUsbMediaBuildResponse response =
				new GetLastUsbMediaBuildResponse
				{
					HasOperation = operation != null
				};

			if (operation != null)
			{
				response.Operation =
					GrpcOperationMapper.ToMessage(operation);
			}

			return Task.FromResult(response);
		}

		public override Task<UsbMediaOperationUpdate> CancelUsbMediaBuild(
			UsbMediaOperationRequest request,
			ServerCallContext context)
		{
			try
			{
				UsbMediaOperationSnapshot update =
					_operationCoordinator.RequestCancellation(
						request.OperationId);

				_logger.LogWarning(
					"Cancellation was requested for USB media operation {OperationId}.",
					request.OperationId);

				return Task.FromResult(
					GrpcOperationMapper.ToMessage(update));
			}
			catch (KeyNotFoundException exception)
			{
				throw new RpcException(
					new Status(StatusCode.NotFound, exception.Message));
			}
		}

		public override async Task<WinPeCacheStatusResponse>
			GetWinPeCacheStatus(
				GetWinPeCacheStatusRequest request,
				ServerCallContext context)
		{
			try
			{
				WinPeCacheStatusSnapshot status =
					await _winPeCacheService.GetStatusAsync(
						context.CancellationToken);

				return GrpcWinPeCacheMapper.ToMessage(status);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception exception)
			{
				_logger.LogError(
					exception,
					"Failed to read the WinPE cache status.");

				throw new RpcException(
					new Status(
						StatusCode.Internal,
						"The WinPE cache status could not be read."));
			}
		}

		public override async Task<WinPeCacheStatusResponse> ClearWinPeCache(
			ClearWinPeCacheRequest request,
			ServerCallContext context)
		{
			if (!request.CacheClearConfirmed)
			{
				throw new RpcException(
					new Status(
						StatusCode.InvalidArgument,
						"Explicit WinPE cache-clear confirmation is required."));
			}

			try
			{
				WinPeCacheStatusSnapshot status =
					await _winPeCacheService.ClearAsync(
						context.CancellationToken);

				_logger.LogWarning(
					"The service-owned WinPE media cache was cleared by a local client.");

				return GrpcWinPeCacheMapper.ToMessage(status);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (InvalidOperationException exception)
			{
				throw new RpcException(
					new Status(
						StatusCode.ResourceExhausted,
						exception.Message));
			}
			catch (Exception exception)
			{
				_logger.LogError(
					exception,
					"Failed to clear the WinPE media cache.");

				throw new RpcException(
					new Status(
						StatusCode.Internal,
						"The WinPE media cache could not be cleared."));
			}
		}

		private UsbMediaOperationUpdate GetOperationUpdate(String operationId)
		{
			try
			{
				return GrpcOperationMapper.ToMessage(
					_operationCoordinator.GetStatus(operationId));
			}
			catch (KeyNotFoundException exception)
			{
				throw new RpcException(
					new Status(StatusCode.NotFound, exception.Message));
			}
		}

		private async Task AuditOperationCompletionAsync(String operationId)
		{
			try
			{
				await foreach (UsbMediaOperationSnapshot update in
					_operationCoordinator.WatchAsync(operationId))
				{
					if (!update.IsTerminal)
					{
						continue;
					}

					if (update.State ==
						OSImageDeploy.Contracts.UsbMediaOperationState.Succeeded)
					{
						_logger.LogInformation(
							"USB media operation {OperationId} completed successfully.",
							operationId);
					}
					else
					{
						_logger.LogError(
							"USB media operation {OperationId} ended in state {State}: {ErrorMessage}",
							operationId,
							update.State,
							update.ErrorMessage);
					}

					break;
				}
			}
			catch (Exception exception)
			{
				_logger.LogError(
					exception,
					"Failed to audit USB media operation {OperationId}.",
					operationId);
			}
		}
	}
}
