using Grpc.Core;
using OSImageDeploy.Contracts;
using OSImageDeploy.Engine;
using OSImageDeploy.Platform.Windows;
using OSImageDeploy.Transport.Grpc;
using OSImageDeploy.Transport.Grpc.V1;
using System.Reflection;
using ContractUsbMediaBuildMode = OSImageDeploy.Contracts.UsbMediaBuildMode;
using GrpcUsbMediaBuildMode = OSImageDeploy.Transport.Grpc.V1.UsbMediaBuildMode;

namespace OSImageDeploy.Service.Services
{
	public sealed class OsImageDeployControlService :
		OsImageDeployControl.OsImageDeployControlBase
	{
		private readonly IUsbTargetDiscovery _targetDiscovery;
		private readonly IUsbTargetValidator _targetValidator;
		private readonly IUsbMediaRefreshValidator _refreshValidator;
		private readonly IUsbMediaOperationCoordinator _operationCoordinator;
		private readonly IWinPeCacheService _winPeCacheService;
		private readonly WindowsWinPeDriverPackageStore _driverPackageStore;
		private readonly ILogger<OsImageDeployControlService> _logger;

		public OsImageDeployControlService(
			IUsbTargetDiscovery targetDiscovery,
			IUsbTargetValidator targetValidator,
			IUsbMediaRefreshValidator refreshValidator,
			IUsbMediaOperationCoordinator operationCoordinator,
			IWinPeCacheService winPeCacheService,
			WindowsWinPeDriverPackageStore driverPackageStore,
			ILogger<OsImageDeployControlService> logger)
		{
			_targetDiscovery = targetDiscovery;
			_targetValidator = targetValidator;
			_refreshValidator = refreshValidator;
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
			ContractUsbMediaBuildMode buildMode = request.BuildMode switch
			{
				GrpcUsbMediaBuildMode.FullRebuild =>
					ContractUsbMediaBuildMode.FullRebuild,
				GrpcUsbMediaBuildMode.RefreshBootPartition =>
					ContractUsbMediaBuildMode.RefreshBootPartition,
				_ => throw new RpcException(
					new Status(
						StatusCode.InvalidArgument,
						"The requested USB media build mode is not supported."))
			};

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

			if (buildMode == ContractUsbMediaBuildMode.RefreshBootPartition)
			{
				UsbMediaRefreshValidationResult refreshValidation =
					await _refreshValidator.ValidateRefreshAsync(
						selectedTarget,
						context.CancellationToken);

				if (!refreshValidation.IsEligible)
				{
					throw new RpcException(
						new Status(
							StatusCode.FailedPrecondition,
							refreshValidation.Summary));
				}
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
							BuildMode = buildMode,
							WinPeDriverPackageIds = driverPackages
								.Select(package => package.Descriptor.PackageId)
								.ToList(),
							DestructiveActionConfirmed = true
						});

				_logger.LogWarning(
					"USB media operation {OperationId} ({BuildMode}) was authorised for target {TargetId}, selected as disk {DiskNumber}.",
					operation.OperationId,
					buildMode,
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

		public override async Task<ValidateUsbMediaRefreshResponse>
			ValidateUsbMediaRefresh(
				ValidateUsbMediaRefreshRequest request,
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
				UsbMediaRefreshValidationResult validation =
					await _refreshValidator.ValidateRefreshAsync(
						GrpcTargetMapper.ToDescriptor(request.SelectedTarget),
						context.CancellationToken);
				ValidateUsbMediaRefreshResponse response =
					new ValidateUsbMediaRefreshResponse
					{
						IsEligible = validation.IsEligible,
						Summary = validation.Summary
					};

				response.Warnings.AddRange(validation.Warnings);

				if (validation.ResolvedTarget != null)
				{
					response.ResolvedTarget =
						GrpcTargetMapper.ToMessage(validation.ResolvedTarget);
				}

				if (validation.BootPartition != null)
				{
					response.BootPartition =
						ToMessage(validation.BootPartition);
				}

				if (validation.DataPartition != null)
				{
					response.DataPartition =
						ToMessage(validation.DataPartition);
				}

				return response;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception exception) when (exception is not RpcException)
			{
				_logger.LogError(
					exception,
					"USB media refresh validation failed.");

				throw new RpcException(
					new Status(
						StatusCode.Internal,
						"USB media refresh validation failed."));
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

		public override async Task<WinPeDriverPackage>
			PrepareWinPeDriverPackage(
				PrepareWinPeDriverPackageRequest request,
				ServerCallContext context)
		{
			if (String.IsNullOrWhiteSpace(request.PackageId) ||
				String.IsNullOrWhiteSpace(request.SourceFilePath))
			{
				throw new RpcException(
					new Status(
						StatusCode.InvalidArgument,
						"A package ID and manufacturer download path are required."));
			}

			if (_operationCoordinator.GetActiveOperation() != null)
			{
				throw new RpcException(
					new Status(
						StatusCode.ResourceExhausted,
						"Driver packages cannot be changed during an active USB operation."));
			}

			try
			{
				WinPeDriverPackageDescriptor package =
					await _driverPackageStore.PrepareBuiltInPackageAsync(
						request.PackageId,
						request.SourceFilePath,
						request.SourceVersion,
						request.ReplaceExistingConfirmed,
						context.CancellationToken);

				_logger.LogInformation(
					"WinPE driver package {PackageId} was prepared with {DriverCount} INF files and archive hash {ArchiveSha256}.",
					package.PackageId,
					package.DriverCount,
					package.ArchiveSha256);

				return GrpcWinPeDriverPackageMapper.ToMessage(package);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (ArgumentException exception)
			{
				throw new RpcException(
					new Status(
						StatusCode.InvalidArgument,
						exception.Message));
			}
			catch (FileNotFoundException exception)
			{
				throw new RpcException(
					new Status(
						StatusCode.NotFound,
						exception.Message));
			}
			catch (UnauthorizedAccessException)
			{
				throw new RpcException(
					new Status(
						StatusCode.FailedPrecondition,
						"The service cannot read the selected download. " +
						"Copy it to a local folder that SYSTEM can access and try again."));
			}
			catch (Exception exception) when (
				exception is InvalidDataException or
				InvalidOperationException)
			{
				_logger.LogWarning(
					exception,
					"WinPE driver package {PackageId} was rejected during preparation.",
					request.PackageId);

				throw new RpcException(
					new Status(
						StatusCode.FailedPrecondition,
						exception.Message));
			}
			catch (Exception exception)
			{
				_logger.LogError(
					exception,
					"WinPE driver package {PackageId} preparation failed.",
					request.PackageId);

				throw new RpcException(
					new Status(
						StatusCode.Internal,
						"The WinPE driver package could not be prepared."));
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

		private static UsbMediaPartition ToMessage(
			UsbMediaPartitionDescriptor partition)
		{
			return new UsbMediaPartition
			{
				PartitionNumber = partition.PartitionNumber,
				SizeBytes = partition.SizeBytes,
				FileSystem = partition.FileSystem,
				Label = partition.Label,
				DriveLetter = partition.DriveLetter,
				HasDriverPacksFolder = partition.HasDriverPacksFolder,
				HasWindowsImagesFolder = partition.HasWindowsImagesFolder
			};
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
