using OSImageDeploy.Contracts;
using OSImageDeploy.Engine;
using Utilities;

namespace OSImageDeploy.Platform.Windows
{
	public sealed class WindowsUsbMediaWorkflow :
		IUsbMediaWorkflow,
		IUsbMediaRefreshValidator,
		IWinPeCacheService
	{
		private readonly WindowsUsbTargetProvider _targetProvider;
		private readonly WinPeMediaCacheManager _cacheManager;
		private readonly WindowsWinPeDriverPackageStore _driverPackageStore;
		private readonly WindowsUsbMediaLayoutInspector _layoutInspector;
		private readonly SemaphoreSlim _operationGate =
			new SemaphoreSlim(1, 1);

		public WindowsUsbMediaWorkflow(
			WindowsUsbTargetProvider targetProvider,
			WinPeMediaCacheManager cacheManager,
			WindowsWinPeDriverPackageStore driverPackageStore,
			WindowsUsbMediaLayoutInspector layoutInspector)
		{
			_targetProvider = targetProvider ??
				throw new ArgumentNullException(nameof(targetProvider));
			_cacheManager = cacheManager ??
				throw new ArgumentNullException(nameof(cacheManager));
			_driverPackageStore = driverPackageStore ??
				throw new ArgumentNullException(nameof(driverPackageStore));
			_layoutInspector = layoutInspector ??
				throw new ArgumentNullException(nameof(layoutInspector));
		}

		public Task<IReadOnlyList<UsbTargetDescriptor>> GetEligibleTargetsAsync(
			CancellationToken cancellationToken = default)
		{
			return _targetProvider.GetEligibleTargetsAsync(cancellationToken);
		}

		public Task<UsbTargetValidationResult> ValidateTargetAsync(
			UsbTargetDescriptor target,
			CancellationToken cancellationToken = default)
		{
			return _targetProvider.ValidateTargetAsync(
				target,
				cancellationToken);
		}

		public async Task<UsbMediaRefreshValidationResult> ValidateRefreshAsync(
			UsbTargetDescriptor target,
			CancellationToken cancellationToken = default)
		{
			UsbTargetValidationResult targetValidation =
				await _targetProvider.ValidateTargetAsync(
					target,
					cancellationToken);

			if (!targetValidation.IsValid ||
				targetValidation.ResolvedTarget == null)
			{
				return new UsbMediaRefreshValidationResult
				{
					IsEligible = false,
					Summary = targetValidation.Summary,
					Warnings = targetValidation.Warnings
				};
			}

			UsbMediaLayoutDescriptor layout = _layoutInspector.Inspect(
				targetValidation.ResolvedTarget.DiskNumber);
			UsbMediaRefreshValidationResult layoutValidation =
				UsbMediaRefreshSafetyPolicy.Validate(layout);

			return new UsbMediaRefreshValidationResult
			{
				IsEligible = layoutValidation.IsEligible,
				Summary = layoutValidation.Summary,
				Warnings = targetValidation.Warnings
					.Concat(layoutValidation.Warnings)
					.ToList(),
				ResolvedTarget = targetValidation.ResolvedTarget,
				BootPartition = layoutValidation.BootPartition,
				DataPartition = layoutValidation.DataPartition
			};
		}

		public async Task CreateUsbMediaAsync(
			UsbMediaBuildRequest request,
			IProgress<OperationProgress> progress,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);
			ArgumentNullException.ThrowIfNull(progress);

			if (!request.DestructiveActionConfirmed)
			{
				throw new InvalidOperationException(
					"USB media creation was not explicitly confirmed.");
			}

			await _operationGate.WaitAsync(cancellationToken);

			try
			{
				IReadOnlyList<ResolvedWinPeDriverPackage> driverPackages =
					_driverPackageStore.ResolveSelection(
						request.WinPeDriverPackageIds);

				if (request.RebuildWinPeCache)
				{
					cancellationToken.ThrowIfCancellationRequested();
					_cacheManager.Delete();
				}

				DiskBuilder diskBuilder = new DiskBuilder(
					driverPackages.Select(package => package.ArchivePath));

				EventHandler<DiskBuilder.DiskBuilderProgressEventArgs> handler =
					(_, update) => progress.Report(
						new OperationProgress
						{
							Stage = update.Stage,
							Message = update.Message,
							OverallPercent = update.Percent
						});

				diskBuilder.ProgressChanged += handler;

				try
				{
					if (request.BuildMode == UsbMediaBuildMode.RefreshBootPartition)
					{
						await diskBuilder.RefreshBootPartitionAsync(
							async token =>
							{
								progress.Report(
									new OperationProgress
									{
										Stage = "Validating Refresh Layout",
										Message =
											"Rediscovering the selected USB disk and revalidating both partitions immediately before formatting the WinPE partition.",
										OverallPercent = 1
									});

								UsbMediaRefreshValidationResult validation =
									await ValidateRefreshAsync(
										request.Target,
										token);

								if (!validation.IsEligible ||
									validation.ResolvedTarget == null ||
									validation.BootPartition == null)
								{
									throw new InvalidOperationException(
										validation.Summary);
								}

								return new UsbBootPartitionRefreshTarget
								{
									DiskNumber =
										validation.ResolvedTarget.DiskNumber,
									PartitionNumber =
										validation.BootPartition.PartitionNumber,
									SizeBytes = validation.BootPartition.SizeBytes,
									DriveLetter = validation.BootPartition.DriveLetter,
									DataDriveLetter =
										validation.DataPartition?.DriveLetter ??
										String.Empty
								};
							},
							cancellationToken);
					}
					else
					{
						await diskBuilder.PrepareDiskAsync(
						async token =>
						{
							progress.Report(
								new OperationProgress
								{
									Stage = "Validating Target",
									Message =
										"Revalidating the selected USB target immediately before disk preparation.",
									OverallPercent = 1
								});

							UsbTargetValidationResult validation =
								await _targetProvider.ValidateTargetAsync(
									request.Target,
									token);

							if (!validation.IsValid ||
								validation.ResolvedTarget == null)
							{
								throw new InvalidOperationException(
									validation.Summary);
							}

							return validation.ResolvedTarget.DiskNumber;
						},
							cancellationToken);
					}
				}
				finally
				{
					diskBuilder.ProgressChanged -= handler;
				}
			}
			finally
			{
				_operationGate.Release();
			}
		}

		public async Task<WinPeCacheStatusSnapshot> GetStatusAsync(
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			Boolean archiveExists = File.Exists(_cacheManager.ArchivePath);
			Boolean manifestExists = File.Exists(_cacheManager.ManifestPath);

			if (!archiveExists && !manifestExists)
			{
				return new WinPeCacheStatusSnapshot
				{
					State = WinPeCacheState.Missing
				};
			}

			if (!archiveExists || !manifestExists)
			{
				return new WinPeCacheStatusSnapshot
				{
					State = WinPeCacheState.Incomplete
				};
			}

			WinPeCacheManifest? manifest;

			try
			{
				manifest = await _cacheManager.LoadManifestAsync(
					cancellationToken);
			}
			catch (System.Text.Json.JsonException)
			{
				return new WinPeCacheStatusSnapshot
				{
					State = WinPeCacheState.Incomplete
				};
			}

			if (manifest == null)
			{
				return new WinPeCacheStatusSnapshot
				{
					State = WinPeCacheState.Incomplete
				};
			}

			return new WinPeCacheStatusSnapshot
			{
				State = WinPeCacheState.Available,
				CreatedUtc = manifest.CreatedUtc,
				ArchiveSizeBytes =
					new FileInfo(_cacheManager.ArchivePath).Length
			};
		}

		public async Task<WinPeCacheStatusSnapshot> ClearAsync(
			CancellationToken cancellationToken = default)
		{
			if (!await _operationGate.WaitAsync(0, cancellationToken))
			{
				throw new InvalidOperationException(
					"The WinPE cache cannot be cleared while USB media creation is running.");
			}

			try
			{
				_cacheManager.Delete();
				return await GetStatusAsync(cancellationToken);
			}
			finally
			{
				_operationGate.Release();
			}
		}
	}
}
