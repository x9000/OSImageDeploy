using Models;
using OSImageDeploy.Client;
using OSImageDeploy.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Utilities;
using x9000.Utilities;

namespace ViewModels
{
#nullable disable

	internal class MainUSBCreatorWindowViewModel : BaseViewModel
	{
		#region Construction

		public MainUSBCreatorWindowViewModel()
		{
			_installer = new WindowsAdkWinPeInstaller();
			_installer.ProgressChanged += Installer_ProgressChanged;
			_serviceClient = new OsImageDeployServiceClient();

			RefreshUSBButtonCommand = new RelayCommand(execute: RefreshUSBButtonClickHandler, canExecute: RefreshUSBButtonCanExecuteHandler);
			CreateUSBCommand =
				new RelayCommand<UsbTargetDescriptor>(
					execute: CreateUSBClickHandler);
			CancelUSBCommand =
				new RelayCommand(execute: CancelUSBClickHandler);
			ExitCommand = new RelayCommand(execute: ExitCommandHandler, canExecute: ExitCanExecuteHandler);
			StartPrereqInstalls = new RelayCommand(execute: StartPrereqInstallsHandler);
			RebuildWinPeCacheCommand =
				new RelayCommand(execute: RebuildWinPeCacheCommandHandler);

			_ = InitializeAsync();
		}

		#endregion

		private async Task InitializeAsync()
		{
			await PerformPrereqTestingAsync();
			await PopulateUSBDriveListAsync();
			await RefreshWinPeCacheStatusAsync();
			await RefreshWinPeDriverPackagesAsync();
			await ReconnectToActiveUsbMediaBuildAsync();
		}

		#region Fields

		private readonly WindowsAdkWinPeInstaller _installer;
		private readonly OsImageDeployServiceClient _serviceClient;

		private String _infoTextBlockText = "";
		private String _subInfoTextBlockText = "";
		private String _winPeCacheStatusText = "WinPE cache: Checking...";
		private String _winPeCacheDetailsText = "";
		private String _winPeDriverPackagesStatusText =
			"Optional WinPE drivers: Checking...";
		private String _activeOperationId = "";
		private String _titleTextBlockText = GetApplicationTitle();
		private Int32 _operationProgressValue;
		private Visibility _operationProgressVisibility = Visibility.Collapsed;
		private int _usbComboxSelectedItemIndex;
		private bool _createUSBButtonEnabled = false;
		private bool _cancelUSBButtonEnabled = false;
		private bool _rebuildWinPeCacheButtonEnabled;
		private bool _preReqInstallButtonEnabled = false;
		private bool _exitButtonEnabled = true;
		private bool _refreshUSBButtonEnabled = true;
		private Visibility _prereqStackPanelVisibility = Visibility.Collapsed;
		private Visibility _mainStackPanelVisibility = Visibility.Visible;

		#endregion

		#region Commands

		public RelayCommand<UsbTargetDescriptor> CreateUSBCommand { get; }
		public RelayCommand CancelUSBCommand { get; }
		public RelayCommand ExitCommand { get; }
		public RelayCommand RefreshUSBButtonCommand { get; }
		public RelayCommand StartPrereqInstalls { get; }
		public RelayCommand RebuildWinPeCacheCommand { get; }

		#endregion

		#region Collections

		public ObservableCollection<PreCheckModel> PreReqChecks { get; set; } = new ObservableCollection<PreCheckModel>();
		public ObservableCollection<UsbTargetDescriptor> ListOfUSBDrives { get; set; } =
			new ObservableCollection<UsbTargetDescriptor>();
		public ObservableCollection<WinPeDriverPackageSelectionModel>
			WinPeDriverPackages { get; } = new();

		#endregion

		#region Bindable Properties

		public String InfoTextBlockText
		{
			get
			{
				return _infoTextBlockText;
			}
			set
			{
				_infoTextBlockText = value;
				NotifyPropertyChanged(nameof(InfoTextBlockText));
			}
		}

		public String TitleTextBlockText
		{
			get
			{
				return _titleTextBlockText;
			}
			set
			{
				_titleTextBlockText = value;
				NotifyPropertyChanged(nameof(TitleTextBlockText));
			}
		}

		public String SubInfoTextBlockText
		{
			get
			{
				return _subInfoTextBlockText;
			}
			set
			{
				_subInfoTextBlockText = value;
				NotifyPropertyChanged(nameof(SubInfoTextBlockText));
			}
		}

		public Int32 OperationProgressValue
		{
			get => _operationProgressValue;
			set
			{
				_operationProgressValue = Math.Clamp(value, 0, 100);
				NotifyPropertyChanged(nameof(OperationProgressValue));
			}
		}

		public Visibility OperationProgressVisibility
		{
			get => _operationProgressVisibility;
			set
			{
				_operationProgressVisibility = value;
				NotifyPropertyChanged(nameof(OperationProgressVisibility));
			}
		}

		public String WinPeCacheStatusText
		{
			get
			{
				return _winPeCacheStatusText;
			}
			set
			{
				_winPeCacheStatusText = value;
				NotifyPropertyChanged(nameof(WinPeCacheStatusText));
			}
		}

		public String WinPeCacheDetailsText
		{
			get
			{
				return _winPeCacheDetailsText;
			}
			set
			{
				_winPeCacheDetailsText = value;
				NotifyPropertyChanged(nameof(WinPeCacheDetailsText));
			}
		}

		public String WinPeDriverPackagesStatusText
		{
			get => _winPeDriverPackagesStatusText;
			set
			{
				_winPeDriverPackagesStatusText = value;
				NotifyPropertyChanged(nameof(WinPeDriverPackagesStatusText));
			}
		}

		public int USBComboxSelectedItemIndex
		{
			get
			{
				return _usbComboxSelectedItemIndex;
			}
			set
			{
				_usbComboxSelectedItemIndex = value;
				NotifyPropertyChanged(nameof(USBComboxSelectedItemIndex));
			}
		}

		public bool CreateUSBButtonEnabled
		{
			get
			{
				return _createUSBButtonEnabled;
			}
			set
			{
				_createUSBButtonEnabled = value;
				NotifyPropertyChanged(nameof(CreateUSBButtonEnabled));
			}
		}

		public Visibility PrereqStackPanelVisibility
		{
			get
			{
				return _prereqStackPanelVisibility;
			}
			set
			{
				_prereqStackPanelVisibility = value;
				NotifyPropertyChanged(nameof(PrereqStackPanelVisibility));
			}
		}

		public Visibility MainStackPanelVisibility
		{
			get
			{
				return _mainStackPanelVisibility;
			}
			set
			{
				_mainStackPanelVisibility = value;
				NotifyPropertyChanged(nameof(MainStackPanelVisibility));
			}
		}

		public bool PreReqInstallButtonEnabled
		{
			get
			{
				return _preReqInstallButtonEnabled;
			}
			set
			{
				_preReqInstallButtonEnabled = value;
				NotifyPropertyChanged(nameof(PreReqInstallButtonEnabled));
			}
		}

		public bool ExitButtonEnabled
		{
			get
			{
				return _exitButtonEnabled;
			}
			set
			{
				_exitButtonEnabled = value;
				NotifyPropertyChanged(nameof(ExitButtonEnabled));
			}
		}

		public bool RefreshUSBButtonEnabled
		{
			get
			{
				return _refreshUSBButtonEnabled;
			}
			set
			{
				_refreshUSBButtonEnabled = value;
				NotifyPropertyChanged(nameof(RefreshUSBButtonEnabled));
			}
		}

		public bool RebuildWinPeCacheButtonEnabled
		{
			get
			{
				return _rebuildWinPeCacheButtonEnabled;
			}
			set
			{
				_rebuildWinPeCacheButtonEnabled = value;
				NotifyPropertyChanged(
					nameof(RebuildWinPeCacheButtonEnabled));
			}
		}

		#endregion

		#region WinPE Cache

		private async Task RefreshWinPeCacheStatusAsync()
		{
			try
			{
				WinPeCacheStatusSnapshot status =
					await _serviceClient.GetWinPeCacheStatusAsync();

				if (status.State == WinPeCacheState.Missing)
				{
					WinPeCacheStatusText =
						"WinPE cache: Not available";

					WinPeCacheDetailsText =
						"A new cache will be created during the next USB build.";

					RebuildWinPeCacheButtonEnabled = false;

					return;
				}

				if (status.State == WinPeCacheState.Incomplete)
				{
					WinPeCacheStatusText =
						"WinPE cache: Incomplete";

					WinPeCacheDetailsText =
						"The cache will be rebuilt during the next USB build.";

					RebuildWinPeCacheButtonEnabled = true;

					return;
				}

				String createdText =
					!status.CreatedUtc.HasValue
						? "Unknown"
						: status.CreatedUtc.Value
							.ToLocalTime()
							.ToString("g");

				WinPeCacheStatusText =
					"WinPE cache: Available";

				WinPeCacheDetailsText =
					$"Created: {createdText}    " +
					$"Size: {status.ArchiveSizeBytes / 1024D / 1024D:F1} MB" +
					Environment.NewLine +
					"Validated when USB creation starts.";

				RebuildWinPeCacheButtonEnabled = true;
			}
			catch (Exception exception)
			{
				WinPeCacheStatusText =
					"WinPE cache: Status unavailable";

				WinPeCacheDetailsText =
					"The cache will be checked when USB creation starts.";

				RebuildWinPeCacheButtonEnabled = false;

				AppLog.Error(
					"Failed to read WinPE media cache status.",
					exception);
			}
		}

		public async Task RefreshWinPeDriverPackagesAsync()
		{
			try
			{
				IReadOnlyList<WinPeDriverPackageDescriptor> packages =
					await _serviceClient.GetWinPeDriverPackagesAsync();

				HashSet<String> selectedIds = WinPeDriverPackages
					.Where(package => package.IsSelected)
					.Select(package => package.Package.PackageId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);

				WinPeDriverPackages.Clear();

				foreach (WinPeDriverPackageDescriptor package in packages)
				{
					WinPeDriverPackageSelectionModel selection =
						new WinPeDriverPackageSelectionModel
						{
							Package = package
						};
					selection.IsSelected =
						package.IsAvailable &&
						selectedIds.Contains(package.PackageId);
					WinPeDriverPackages.Add(selection);
				}

				Int32 availableCount = packages.Count(package => package.IsAvailable);
				WinPeDriverPackagesStatusText = availableCount == 0
					? "Optional WinPE drivers: None prepared. Download an OEM package, then use Prepare package."
					: $"Optional WinPE drivers: {availableCount} package(s) available. Select only those required for this USB.";
			}
			catch (Exception exception)
			{
				WinPeDriverPackages.Clear();
				WinPeDriverPackagesStatusText =
					"Optional WinPE driver packages could not be read from the service.";
				AppLog.Error(
					"Failed to retrieve WinPE driver packages from the service.",
					exception);
			}
		}

		private async void RebuildWinPeCacheCommandHandler()
		{
			MessageBoxResult result = MessageBox.Show(
				"The existing WinPE cache will be deleted. " +
				"The next USB build will recreate it using the current " +
				"WinPE client, drivers and packages.",
				"Rebuild WinPE Cache",
				MessageBoxButton.YesNo,
				MessageBoxImage.Question);

			if (result != MessageBoxResult.Yes)
			{
				return;
			}

			RebuildWinPeCacheButtonEnabled = false;

			try
			{
				await _serviceClient.ClearWinPeCacheAsync(
					cacheClearConfirmed: true);

				await RefreshWinPeCacheStatusAsync();

				InfoTextBlockText =
					"WinPE cache deleted. It will be rebuilt during the next USB build.";
			}
			catch (Exception exception)
			{
				AppLog.Error(
					"Failed to delete the WinPE media cache.",
					exception);

				MessageBox.Show(
					"The WinPE cache could not be deleted. " +
					"See the application log for details.",
					"WinPE Cache",
					MessageBoxButton.OK,
					MessageBoxImage.Error);

				await RefreshWinPeCacheStatusAsync();
			}
		}

		#endregion

		#region Prerequisites

		private async Task PerformPrereqTestingAsync()
		{
			PreReqChecks.Add(new PreCheckModel { IsChecked = WindowsAdkWinPeInstaller.IsAdkDeploymentToolsInstalled(), Text = "Windows Assessment and Deployment Kit (ADK)" });
			PreReqChecks.Add(new PreCheckModel { IsChecked = WindowsAdkWinPeInstaller.IsWinPeAddonInstalled(), Text = "Windows Preinstallation Environment (WinPE) Add-On" });

			await Task.CompletedTask;
		}

		private async void StartPrereqInstallsHandler()
		{
			PreReqInstallButtonEnabled = false;
			ExitButtonEnabled = false;

			try
			{
				await _installer.InstallOrModifyAsync();
			}
			catch (Exception exception)
			{
				AppLog.Error(
					"Windows ADK / WinPE prerequisite setup failed or was cancelled.",
					exception);

				MessageBox.Show(
					"Windows ADK / WinPE setup did not complete." +
					Environment.NewLine +
					Environment.NewLine +
					exception.Message,
					"Prerequisite Setup",
					MessageBoxButton.OK,
					MessageBoxImage.Warning);
			}
			finally
			{
				ExitButtonEnabled = true;
				PreReqChecks.Clear();

				await PerformPrereqTestingAsync();
				await PopulateUSBDriveListAsync();
			}
		}

		private bool ArePrerequisitesInstalled()
		{
			bool checkResult = true;

			foreach (PreCheckModel preCheckModel in PreReqChecks)
			{
				checkResult = checkResult && preCheckModel.IsChecked;
			}

			return checkResult;
		}

		private void UpdatePrerequisitePanelVisibility(bool prerequisitesInstalled)
		{
			if (prerequisitesInstalled)
			{
				MainStackPanelVisibility = Visibility.Visible;
				PrereqStackPanelVisibility = Visibility.Collapsed;
			}
			else
			{
				MainStackPanelVisibility = Visibility.Collapsed;
				PrereqStackPanelVisibility = Visibility.Visible;
			}
		}

		#endregion

		#region USB Drive Discovery

		private async Task PopulateUSBDriveListAsync()
		{
			bool prerequisitesInstalled = ArePrerequisitesInstalled();

			PreReqInstallButtonEnabled = !prerequisitesInstalled;
			UpdatePrerequisitePanelVisibility(prerequisitesInstalled);
			CreateUSBButtonEnabled = false;
			RefreshUSBButtonEnabled = false;

			try
			{
				IReadOnlyList<UsbTargetDescriptor> targets =
					await _serviceClient.GetEligibleTargetsAsync();

				ListOfUSBDrives.Clear();

				foreach (UsbTargetDescriptor target in targets)
				{
					ListOfUSBDrives.Add(target);
				}

				USBComboxSelectedItemIndex = targets.Count > 0 ? 0 : -1;
				CreateUSBButtonEnabled =
					prerequisitesInstalled && targets.Count > 0;

				if (targets.Count == 0)
				{
					InfoTextBlockText =
						"No suitable USB storage devices were reported by the service.";
				}
			}
			catch (OsImageDeployServiceException exception)
			{
				ListOfUSBDrives.Clear();
				USBComboxSelectedItemIndex = -1;
				InfoTextBlockText =
					"The OS Image Deploy service is unavailable. " +
					"USB creation is disabled.";

				AppLog.Error(
					"Failed to retrieve USB targets from the service.",
					exception);
			}
			finally
			{
				RefreshUSBButtonEnabled = true;
			}
		}

		public bool CancelUSBButtonEnabled
		{
			get
			{
				return _cancelUSBButtonEnabled;
			}
			set
			{
				_cancelUSBButtonEnabled = value;
				NotifyPropertyChanged(nameof(CancelUSBButtonEnabled));
			}
		}

		#endregion

		#region Command Handlers

		private async Task ReconnectToActiveUsbMediaBuildAsync()
		{
			try
			{
				UsbMediaOperationSnapshot operation =
					await _serviceClient.GetActiveUsbMediaBuildAsync();

				if (operation == null)
				{
					await DisplayInterruptedUsbMediaBuildAsync();
					return;
				}

				SetUsbOperationControls(operation);
				InfoTextBlockText =
					"Reconnected to the USB media operation already running in the service.";

				await WatchUsbMediaOperationAsync(operation);
			}
			catch (UsbMediaOperationFailedException exception)
			{
				HandleUsbOperationTerminalFailure(
					"The reconnected USB media operation failed.",
					exception);
			}
			catch (Exception exception) when (
				String.IsNullOrWhiteSpace(_activeOperationId))
			{
				AppLog.Error(
					"Failed to query the service for an active USB media operation.",
					exception);
			}
			catch (Exception exception)
			{
				HandleUsbOperationFailure(
					"Failed while reconnecting to the active USB media operation.",
					exception,
					statusMayBeLost: true);
			}
			finally
			{
				if (!String.IsNullOrWhiteSpace(_activeOperationId))
				{
					await RestoreControlsAfterUsbOperationAsync();
				}
			}
		}

		private async Task DisplayInterruptedUsbMediaBuildAsync()
		{
			UsbMediaOperationSnapshot operation =
				await _serviceClient.GetLastUsbMediaBuildAsync();

			if (operation?.State != UsbMediaOperationState.Failed ||
				!String.Equals(
					operation.Progress?.Stage,
					"Interrupted",
					StringComparison.Ordinal))
			{
				return;
			}

			InfoTextBlockText =
				"The previous USB media operation was interrupted by a service restart.";
			SubInfoTextBlockText = operation.ErrorMessage;
		}

		private bool ExitCanExecuteHandler()
		{
			return true;
		}

		private void ExitCommandHandler()
		{
			Environment.Exit(0);
		}

		private async void CreateUSBClickHandler(
			UsbTargetDescriptor selectedTarget)
		{
			if (selectedTarget == null ||
				String.IsNullOrWhiteSpace(selectedTarget.TargetId))
			{
				return;
			}

			List<WinPeDriverPackageSelectionModel> selectedDriverPackages =
				WinPeDriverPackages
					.Where(package => package.IsSelected)
					.ToList();
			String driverPackageSummary = selectedDriverPackages.Count == 0
				? "Optional WinPE drivers: None"
				: "Optional WinPE drivers: " + String.Join(
					", ",
					selectedDriverPackages.Select(
						package => package.Package.DisplayName));

			MessageBoxResult confirmation = MessageBox.Show(
				$"All existing data on the following device will be permanently erased:" +
				Environment.NewLine +
				Environment.NewLine +
				selectedTarget.DisplayName +
				Environment.NewLine +
				$"Size: {selectedTarget.SizeBytes / 1024D / 1024D / 1024D:F1} GB" +
				Environment.NewLine +
				driverPackageSummary +
				Environment.NewLine +
				Environment.NewLine +
				"Continue with USB creation?",
				"Confirm Destructive USB Operation",
				MessageBoxButton.YesNo,
				MessageBoxImage.Warning,
				MessageBoxResult.No);

			if (confirmation != MessageBoxResult.Yes)
			{
				return;
			}

			ExitButtonEnabled = false;
			CreateUSBButtonEnabled = false;
			RefreshUSBButtonEnabled = false;
			RebuildWinPeCacheButtonEnabled = false;

			try
			{
				UsbMediaOperationSnapshot operation =
					await _serviceClient.StartUsbMediaBuildAsync(
						new UsbMediaBuildRequest
						{
							Target = selectedTarget,
							WinPeDriverPackageIds = selectedDriverPackages
								.Select(package => package.Package.PackageId)
								.ToList(),
							DestructiveActionConfirmed = true
						});

				SetUsbOperationControls(operation);
				await WatchUsbMediaOperationAsync(operation);
			}
			catch (UsbMediaOperationFailedException exception)
			{
				HandleUsbOperationTerminalFailure(
					$"USB creation failed for target {selectedTarget.TargetId}.",
					exception);
			}
			catch (Exception exception)
			{
				HandleUsbOperationFailure(
					$"USB creation failed for target {selectedTarget.TargetId}.",
					exception,
					statusMayBeLost:
						!String.IsNullOrWhiteSpace(_activeOperationId));
			}
			finally
			{
				await RestoreControlsAfterUsbOperationAsync();
			}
		}

		private void SetUsbOperationControls(
			UsbMediaOperationSnapshot operation)
		{
			_activeOperationId = operation.OperationId;
			OperationProgressVisibility = Visibility.Visible;
			ExitButtonEnabled = false;
			CreateUSBButtonEnabled = false;
			RefreshUSBButtonEnabled = false;
			RebuildWinPeCacheButtonEnabled = false;
			CancelUSBButtonEnabled =
				operation.State != UsbMediaOperationState.CancellationRequested;
		}

		private async Task WatchUsbMediaOperationAsync(
			UsbMediaOperationSnapshot operation)
		{
			UpdateOperationProgress(operation.Progress);

			await foreach (UsbMediaOperationSnapshot update in
				_serviceClient.WatchUsbMediaBuildAsync(operation.OperationId))
			{
				UpdateOperationProgress(update.Progress);

				if (update.State == UsbMediaOperationState.CancellationRequested)
				{
					CancelUSBButtonEnabled = false;
				}

				if (update.State == UsbMediaOperationState.Failed)
				{
					throw new UsbMediaOperationFailedException(
						String.IsNullOrWhiteSpace(update.ErrorMessage)
							? "The service could not create the USB media."
							: update.ErrorMessage);
				}

				if (update.State == UsbMediaOperationState.Cancelled)
				{
					InfoTextBlockText = "USB creation was cancelled.";
					return;
				}
			}
		}

		private void HandleUsbOperationTerminalFailure(
			String logMessage,
			Exception exception)
		{
			InfoTextBlockText = "USB creation failed.";
			SubInfoTextBlockText = "";

			AppLog.Error(logMessage, exception);

			MessageBox.Show(
				"The service reported that USB media creation failed." +
				Environment.NewLine +
				Environment.NewLine +
				exception.Message +
				Environment.NewLine +
				Environment.NewLine +
				"The operation has stopped. Inspect the target before retrying.",
				"USB Creation Failed",
				MessageBoxButton.OK,
				MessageBoxImage.Error);
		}

		private void HandleUsbOperationFailure(
			String logMessage,
			Exception exception,
			Boolean statusMayBeLost)
		{
			InfoTextBlockText = statusMayBeLost
				? "USB operation status was lost."
				: "USB creation could not be started.";
			SubInfoTextBlockText = "";

			AppLog.Error(logMessage, exception);

			String guidance = statusMayBeLost
				? "If the service restarted, its in-memory operation state was lost. " +
					"Inspect the target before starting another build."
				: "No USB media operation was started.";

			MessageBox.Show(
				(statusMayBeLost
					? "The USB media operation could not be monitored to completion."
					: "The bootable USB operation could not be started.") +
				Environment.NewLine +
				Environment.NewLine +
				exception.Message +
				Environment.NewLine +
				Environment.NewLine +
				guidance,
				statusMayBeLost
					? "USB Operation Status Lost"
					: "USB Creation Not Started",
				MessageBoxButton.OK,
				MessageBoxImage.Error);
		}

		private async Task RestoreControlsAfterUsbOperationAsync()
		{
			_activeOperationId = "";
			OperationProgressVisibility = Visibility.Collapsed;
			CancelUSBButtonEnabled = false;
			ExitButtonEnabled = true;
			RefreshUSBButtonEnabled = true;

			await RefreshWinPeCacheStatusAsync();
			await RefreshWinPeDriverPackagesAsync();
			await PopulateUSBDriveListAsync();
		}

		private async void CancelUSBClickHandler()
		{
			if (String.IsNullOrWhiteSpace(_activeOperationId))
			{
				return;
			}

			CancelUSBButtonEnabled = false;

			try
			{
				await _serviceClient.CancelUsbMediaBuildAsync(
					_activeOperationId);

				InfoTextBlockText =
					"Cancellation requested. The current Windows operation " +
					"will finish before the service stops at a safe checkpoint.";
			}
			catch (OsImageDeployServiceException exception)
			{
				AppLog.Error(
					"Failed to request USB operation cancellation.",
					exception);

				InfoTextBlockText =
					"The cancellation request could not be delivered to the service.";
				CancelUSBButtonEnabled = true;
			}
		}

		private void UpdateOperationProgress(OperationProgress progress)
		{
			if (progress == null)
			{
				return;
			}

			if (progress.Stage.StartsWith("DISM"))
			{
				SubInfoTextBlockText = progress.OverallPercent.HasValue
					? $"{progress.Stage} - {progress.Message} " +
						$"{progress.OverallPercent}%"
					: progress.Message;
			}
			else
			{
				SubInfoTextBlockText = "";
				InfoTextBlockText = progress.OverallPercent.HasValue
					? $"{progress.Stage} - {progress.Message} " +
						$"{progress.OverallPercent}%"
					: progress.Message;
			}

			if (progress.OverallPercent.HasValue)
			{
				OperationProgressValue = progress.OverallPercent.Value;
			}
		}

		private static String GetApplicationTitle()
		{
			Assembly entryAssembly = Assembly.GetEntryAssembly();
			String fileVersion = entryAssembly?
				.GetCustomAttribute<AssemblyFileVersionAttribute>()?
				.Version;

			if (Version.TryParse(fileVersion, out Version version))
			{
				return $"OS Image Deployment Tool v" +
					$"{version.Major}.{version.Minor}.{version.Build}";
			}

			FileVersionInfo versionInfo = entryAssembly == null
				? null
				: FileVersionInfo.GetVersionInfo(entryAssembly.Location);

			return String.IsNullOrWhiteSpace(versionInfo?.FileVersion)
				? "OS Image Deployment Tool"
				: $"OS Image Deployment Tool v{versionInfo.FileVersion}";
		}

		private bool RefreshUSBButtonCanExecuteHandler()
		{
			return true;
		}

		private async void RefreshUSBButtonClickHandler()
		{
			await PopulateUSBDriveListAsync();
		}

		#endregion

		#region Installer Progress

		private void Installer_ProgressChanged(object sender, WindowsAdkWinPeInstaller.InstallerProgressEventArgs e)
		{
			String updateText = "";

			if (e.Percent.HasValue)
			{
				updateText = "Progress: " + e.Percent.Value + "% - ";
			}

			updateText += e.Stage + "\n" + e.Message;

			if (e.Stage == "Installer")
			{
				updateText += "\nWARNING! Install in progress. Please wait. This will take a minute or two.";
			}

			InfoTextBlockText = updateText;
		}

		#endregion

		private sealed class UsbMediaOperationFailedException : Exception
		{
			public UsbMediaOperationFailedException(String message)
				: base(message)
			{
			}
		}
	}
}
