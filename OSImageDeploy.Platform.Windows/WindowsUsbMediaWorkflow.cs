using OSImageDeploy.Contracts;
using OSImageDeploy.Engine;
using Utilities;

namespace OSImageDeploy.Platform.Windows
{
	public sealed class WindowsUsbMediaWorkflow : IUsbMediaWorkflow
	{
		private readonly WindowsUsbTargetProvider _targetProvider;

		public WindowsUsbMediaWorkflow(
			WindowsUsbTargetProvider targetProvider)
		{
			_targetProvider = targetProvider ??
				throw new ArgumentNullException(nameof(targetProvider));
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

			if (request.RebuildWinPeCache)
			{
				cancellationToken.ThrowIfCancellationRequested();
				new WinPeMediaCacheManager().Delete();
			}

			DiskBuilder diskBuilder = new DiskBuilder();

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
			finally
			{
				diskBuilder.ProgressChanged -= handler;
			}
		}
	}
}
