#nullable disable
using Imaging;
using Models;
using OSImageDeployClient.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using x9000.Utilities;
using static x9000.Utilities.FSManager;

namespace ViewModels
{
    class DeploymentWindowViewModel : BaseViewModel
    {
		private const long ONE_MEGABYTE = 1024 * 1024;
		private String _currentStage;
		private String _statusMessage;
		private String _progressText;
		private Int32 _overallProgress;
		private Int32 _stageProgress;
		private Boolean _isStageProgressIndeterminate;
		private String _driverPackStatus;
		private String _driverPackDetails;
		#region "Relay Commands"
		public RelayCommand StartRestoreCommand { get; }
		public RelayCommand RefreshDriverPacksCommand { get; }
		public RelayCommand CancelCommand { get; }
		public RelayCommand CommandLineCommand { get; }
		public RelayCommand WindowLoadedCommand { get; }

		private readonly WimImageService _wimImageService;
		public DeploymentWindowViewModel()
		{
			_wimImageService = new WimImageService();

			_wimImageService.ProgressChanged += OnWimProgressChanged;
			_wimImageService.LogMessage += OnWimLogMessage;
			_wimImageService.OperationCompleted += OnWimOperationCompleted;
			_wimImageService.OperationFailed += OnWimOperationFailed;
			_applyImageCancellationTokenSource = new CancellationTokenSource();

			StartRestoreCommand = new RelayCommand(execute: StartRestoreClickHandler);
			RefreshDriverPacksCommand = new RelayCommand(execute: RefreshDriverPacksClickHandler);
			CancelCommand = new RelayCommand(execute: CancelClickHandler);
			CommandLineCommand = new RelayCommand(execute: CommandLineClickHandler);
			WindowLoadedCommand = new RelayCommand(execute: WindowLoadedCommandHandler);

			LogItems = new ObservableCollection<LogItemModel>();

			RestoreEnabled = true;
			StatusMessage = "Ready to restore Windows image.";
			CurrentStage = "Waiting";
			DriverPackStatus = "Driver-pack preflight has not run.";
			DriverPackDetails = "Select Refresh driver packs to scan attached deployment media.";
		}



		private void PromptForWim()
		{
			String WimDirRoot = "";
			foreach (DriveInfo drive in DriveInfo.GetDrives())
			{
				AddLog("INFO", $"Drive {drive.Name} is {drive.DriveType.ToString()}", 0);
				if (!drive.IsReady)
				{
					continue;
				}

				if (Directory.Exists(System.IO.Path.Combine(drive.RootDirectory.FullName, "WindowsImages")))
				{
					WimDirRoot = System.IO.Path.Combine(drive.RootDirectory.FullName, "WindowsImages");
				}
			}
			AddLog("INFO", $"WIM Directory is {WimDirRoot}", 0);
			WimSelectionWindow wimSelectionWindow = new WimSelectionWindow
			{
				Owner = Application.Current.MainWindow
			};

			foreach(String wimFilePath in Directory.GetFiles(WimDirRoot, "*.wim"))
			{
				wimSelectionWindow.WimSelectionVM.AvailableWimFiles.Add(wimFilePath);
			}
			//if (Directory.GetFiles(WimDirRoot, "*.wim").Length == 1)
			//{

			//	wimSelectionWindow.WimSelectionVM.SelectedWimFilePath = Directory.GetFiles(WimDirRoot, "*.wim")[0];
			//}
			Boolean? result = wimSelectionWindow.ShowDialog();
			if (result == true && wimSelectionWindow.WimSelectionVM.SelectedImage != null)
			{
				AddLog("INFO", "Selected WIM image: " + wimSelectionWindow.WimSelectionVM.SelectedImage.Name + " Index: " + wimSelectionWindow.WimSelectionVM.SelectedImage.Index, 0);
				SelectedWimFilePath = wimSelectionWindow.WimSelectionVM.SelectedWimFilePath;
				SelectedWimIndex = wimSelectionWindow.WimSelectionVM.SelectedImage.Index;
				StartButtonText = "Start Restore";
			}
			else
			{
				StartButtonText = "Select Image";
			}
		}

		private String _SelectedWimFilePath;

		public String SelectedWimFilePath
		{
			get
			{
				return _SelectedWimFilePath;
			}
			set
			{
				_SelectedWimFilePath = value;
				NotifyPropertyChanged(nameof(SelectedWimFilePath));
			}
		}
		private String _StartButtonText = "Start Restore";
		public String StartButtonText
		{
			get
			{
				return _StartButtonText;
			}
			set
			{
				_StartButtonText = value;
				NotifyPropertyChanged(nameof(StartButtonText));
			}
		}

		private int _SelectedWimIndex;
		public int SelectedWimIndex
		{
			get
			{
				return _SelectedWimIndex;
			}
			set
			{
				_SelectedWimIndex = value;
				//RestoreEnabled = _SelectedWimIndex != 0;
				//StartButtonText = _SelectedWimIndex != 0 ? "Start Restore" : "Select Image";
				NotifyPropertyChanged(nameof(SelectedWimIndex));
			}
		}

		private async void StartRestoreClickHandler()
		{
			// Check SecureBoot status = ((Get-ItemProperty -Path Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecureBoot\State).UEFISecureBootEnabled -eq 1)
			// Apply image to Windows partition.
			// Prepare Recovery Environment on Recovery partition. 	
			//				New-Item -Path R:\Recovery\WindowsRE -ItemType Directory -ErrorAction SilentlyContinue | Out-Null
			//				Copy - Item - Path W:\Windows\System32\Recovery\Winre.wim - Destination R:\Recovery\WindowsRE\
			// Find Driver Packs on USB drive and apply to image.
			//
			if (SelectedWimIndex == 0)
			{
				PromptForWim();
			}
			else
			{
				DriverPackSelection driverPackSelection;

				try
				{
					driverPackSelection = await RefreshDriverPackSelectionAsync();
				}
				catch (Exception ex)
				{
					AddLog("ERROR", "Driver-pack preflight failed: " + ex.Message, 0);
					MessageBox.Show(
						Application.Current.MainWindow,
						"The driver-pack preflight could not be completed. No changes have been made to the target disk.\n\n" +
							ex.Message,
						"Driver-pack preflight failed",
						MessageBoxButton.OK,
						MessageBoxImage.Error);
					return;
				}

				if (!ConfirmDriverPackSelection(driverPackSelection))
				{
					StatusMessage = "Restore cancelled before target-disk preparation.";
					CurrentStage = "Waiting";
					ProgressText = "Add or replace driver packs, then refresh the preflight before trying again.";
					return;
				}

				RestoreEnabled = false;
				StatusMessage = "Restoring";
				CurrentStage = "Starting Restore...";
				await CreateDiskLayoutAsync();

				await ApplyImageAsync(SelectedWimFilePath, SelectedWimIndex);

				await _wimImageService.AddDriverPacksToAppliedWindowsAsync(
					driverPackSelection.DriverPackPaths,
					cancellationToken: _applyImageCancellationTokenSource.Token);

				Process process = new Process();
				process.StartInfo.FileName = @"W:\Windows\System32\bcdboot.exe";
				process.StartInfo.Arguments = @"W:\Windows /s S:";
				process.Start();
				process.WaitForExit();

				Directory.CreateDirectory(@"R:\Recovery\WindowsRE");
				File.Copy(@"W:\Windows\System32\Recovery\Winre.wim", @"R:\Recovery\Winre.wim");
				process = new Process();
				process.StartInfo.FileName = @"cmd.exe";
				process.StartInfo.Arguments = @"/C W:\Windows\System32\Reagentc.exe /setreimage /path R:\Recovery\Winre.wim /target W:\Windows";
				process.Start();
				process.WaitForExit();
				String windowsBCDGuid = Utilities.BCDHelper.GetWindowsGUID();
				process = new Process();
				process.StartInfo.FileName = @"cmd.exe";
				process.StartInfo.Arguments = @"/C W:\Windows\System32\Reagentc.exe /enable /osguid " + windowsBCDGuid;
				process.Start();
				process.WaitForExit();
				FSManager.RemoveDriveLetter('R');
				OverallProgress = 100;
				Environment.Exit(0);
			}
		}

		private async void RefreshDriverPacksClickHandler()
		{
			try
			{
				await RefreshDriverPackSelectionAsync();
			}
			catch (Exception ex)
			{
				AddLog("ERROR", "Driver-pack preflight failed: " + ex.Message, 0);
				DriverPackStatus = "Driver-pack scan failed.";
				DriverPackDetails = ex.Message;
			}
		}

		private async Task<DriverPackSelection> RefreshDriverPackSelectionAsync()
		{
			RestoreEnabled = false;
			DriverPackStatus = "Scanning attached media for matching driver packs...";
			DriverPackDetails = "Reading the target computer model and package support metadata.";
			CurrentStage = "Driver-pack preflight";
			IsStageProgressIndeterminate = true;

			List<String> discoveryLog = new List<String>();

			try
			{
				DriverPackSelection selection = await Task.Run(() =>
					DriverPackHelper.DiscoverDriverPacksOnMountedDrives(
						message => discoveryLog.Add(message)));

				foreach (String message in discoveryLog)
				{
					AddLog("INFO", message, 8);
				}

				UpdateDriverPackStatus(selection);

				return selection;
			}
			finally
			{
				IsStageProgressIndeterminate = false;
				StageProgress = 0;
				CurrentStage = "Waiting";
				RestoreEnabled = true;
			}
		}

		private void UpdateDriverPackStatus(DriverPackSelection selection)
		{
			String hardwareDescription =
				(String.IsNullOrWhiteSpace(selection.Manufacturer)
					? "Unknown manufacturer"
					: selection.Manufacturer) +
				" / " +
				(String.IsNullOrWhiteSpace(selection.Model)
					? "Unknown model"
					: selection.Model);

			if (!selection.HasDriverPacks)
			{
				DriverPackStatus = "No matching driver pack found.";
				DriverPackDetails = hardwareDescription +
					"\nNo driver pack will be installed unless matching media is added and the scan is refreshed.";
				AddLog("WARN", "No matching driver pack found for " + hardwareDescription + ".", 0);
				return;
			}

			DriverPackStatus = selection.DriverPackPaths.Count == 1
				? "1 matching driver pack will be installed."
				: selection.DriverPackPaths.Count + " matching driver packs will be installed.";
			DriverPackDetails = hardwareDescription + "\n" +
				String.Join(
					Environment.NewLine,
					selection.DriverPackPaths.Select(path => "• " + Path.GetFileName(path)));
		}

		private Boolean ConfirmDriverPackSelection(DriverPackSelection selection)
		{
			String hardwareDescription = selection.Manufacturer + " " + selection.Model;
			String message;
			MessageBoxImage image;

			if (selection.HasDriverPacks)
			{
				message =
					"Detected computer: " + hardwareDescription.Trim() + "\n\n" +
					"The following driver pack" +
					(selection.DriverPackPaths.Count == 1 ? "" : "s") +
					" will be installed:\n\n" +
					String.Join(
						Environment.NewLine,
						selection.DriverPackPaths.Select(path => "• " + Path.GetFileName(path))) +
					"\n\nContinue with the Windows restore?";
				image = MessageBoxImage.Information;
			}
			else
			{
				message =
					"No matching driver pack was found for " + hardwareDescription.Trim() + ".\n\n" +
					"You can cancel now, add the missing pack to the DriverPacks folder, and refresh the scan. " +
					"If you continue, Windows will be restored without an offline device-specific driver pack.\n\n" +
					"Continue without a driver pack?";
				image = MessageBoxImage.Warning;
			}

			MessageBoxResult result = MessageBox.Show(
				Application.Current.MainWindow,
				message,
				"Confirm driver-pack selection",
				MessageBoxButton.OKCancel,
				image,
				MessageBoxResult.Cancel);

			return result == MessageBoxResult.OK;
		}

		private CancellationTokenSource _applyImageCancellationTokenSource;

		public async Task ApplyImageAsync(String selectedWimFile,int selectedImageIndex)
		{
			if (!File.Exists(selectedWimFile))
			{
				AddLog("ERROR", "WIM file does not exist: " + selectedWimFile, 0);
				return;
			}

			FileInfo fileInfo = new FileInfo(selectedWimFile);

			if (fileInfo.Length == 0)
			{
				AddLog("ERROR", "WIM file is empty: " + selectedWimFile, 0);
				return;
			}

			AddLog("INFO", "WIM file: " + selectedWimFile, 0);
			AddLog("INFO", "WIM size: " + fileInfo.Length.ToString("N0") + " bytes", 0);
			AddLog("INFO", "Image index: " + selectedImageIndex, 0);
			AddLog("INFO", "Target path: W:\\", 0);

			//try
			//{
			//	XDocument info = await _wimImageService.GetWimInfoAsync(
			//		selectedWimFile,
			//		_applyImageCancellationTokenSource.Token);

			//	AddLog("INFO", info.ToString(), 0);
			//}
			//catch (Exception ex)
			//{
			//	AddLog("ERROR", "Unable to read WIM info: " + ex.ToString(), 0);
			//	return;
			//}
			Directory.CreateDirectory(@"W:\Windows\Temp");

			try
			{
				CurrentStage = "Applying Windows image";
				await _wimImageService.ApplyImageAsync(
					wimPath: selectedWimFile,
					imageIndex: selectedImageIndex,
					targetPath: @"W:\",
					cancellationToken: _applyImageCancellationTokenSource.Token);

				AddLog("SUCCESS", "Image applied successfully.");
			}
			catch (Exception ex)
			{
				AddLog("ERROR", ex.ToString(), 0);
			}
		}

		public void CancelApplyWindowsImage()
		{
			_applyImageCancellationTokenSource?.Cancel();
		}

		private String ConvertWimLogLevel(WimLogLevel level)
		{
			if (level == WimLogLevel.Error)
			{
				return "ERROR";
			}

			if (level == WimLogLevel.Warning)
			{
				return "WARNING";
			}

			return "INFO";
		}

		private CancellationTokenSource _layoutCancellationTokenSource;
		

		public async Task CreateDiskLayoutAsync()
		{
			FSManager fsManager = new FSManager();
			_layoutCancellationTokenSource = new CancellationTokenSource();

			Progress<DeploymentProgress> progress = new Progress<DeploymentProgress>(p =>
			{
				CurrentStage = p.Stage;
				OverallProgress = p.OverallProgress;
				StageProgress = p.StageProgress;
				ProgressText = p.Message;
				AddLog(p.LogLevel, p.Message, p.LogLevel == "ERROR" ? 0 : 8);
			});

			Boolean result = await fsManager.CreateSimpleGptLayoutForWindowsUefiAsync(
				diskNumber: 0,
				progress: progress,
				cancellationToken: _layoutCancellationTokenSource.Token);

			if (!result)
			{
				AddLog("ERROR", "Disk layout creation failed.", 0);
				return;
			}

			AddLog("SUCCESS", "Disk layout creation completed.", 0);
		}

		public void CancelDiskLayout()
		{
			_layoutCancellationTokenSource?.Cancel();
		}

		private void CommandLineClickHandler()
		{
			Process process = new Process();
			process.StartInfo.FileName = "cmd.exe";
			process.StartInfo.UseShellExecute = true;
			process.Start();
		}
		private async void WindowLoadedCommandHandler()
		{
			PromptForWim();

			try
			{
				await RefreshDriverPackSelectionAsync();
			}
			catch (Exception ex)
			{
				AddLog("ERROR", "Driver-pack preflight failed: " + ex.Message, 0);
				DriverPackStatus = "Driver-pack scan failed.";
				DriverPackDetails = ex.Message;
			}
		}

		private void CancelClickHandler()
		{
			Environment.Exit(0);
		}
		#endregion

		public ObservableCollection<LogItemModel> LogItems { get; }

		private void OnWimOperationFailed(object sender, WimOperationFailedEventArgs e)
		{
			Application.Current.Dispatcher.Invoke(() =>
			{
				CurrentStage = e.OperationName;
				StageProgress = 0;
				IsStageProgressIndeterminate = false;

				AddLog("ERROR",	$"{e.OperationName} - {e.Exception.ToString()}");
			});
		}

		private void OnWimOperationCompleted(object sender, WimOperationCompletedEventArgs e)
		{
			Application.Current.Dispatcher.Invoke(() =>
			{
				CurrentStage = e.OperationName;
				StageProgress = 100;
				IsStageProgressIndeterminate = false;

				AddLog("SUCCESS", $"{e.OperationName} 100%");
			});
		}

		private void OnWimLogMessage(Object sender, WimLogEventArgs e)
		{
			Application.Current.Dispatcher.Invoke(delegate
			{
				AddLog(ConvertWimLogLevel(e.Level), e.Message, e.Level == WimLogLevel.Error ? 0 : 8);
			});
		}

		private void OnWimProgressChanged(Object sender, WimOperationProgressEventArgs e)
		{
			Application.Current.Dispatcher.Invoke(delegate
			{
				Int32 percentage = Convert.ToInt32(e.Percentage);
				Boolean isDriverOperation =
					e.OperationName.Contains("driver", StringComparison.OrdinalIgnoreCase);

				if (isDriverOperation)
				{
					OverallProgress = 75 + Convert.ToInt32(percentage * 0.15);
				}
				else if (e.OperationName.Equals("Apply image", StringComparison.OrdinalIgnoreCase))
				{
					OverallProgress = 20 + Convert.ToInt32(percentage * 0.55);
				}

				CurrentStage = e.OperationName;
				StageProgress = percentage;
				IsStageProgressIndeterminate =
					e.OperationName.StartsWith("Installing ", StringComparison.OrdinalIgnoreCase);
				ProgressText = e.OperationName;

				if (!IsStageProgressIndeterminate)
				{
					ProgressText += " " + percentage + "%";
				}

				if (e.SecondsRemaining > 0)
				{
					ProgressText += " - about " + e.SecondsRemaining + " seconds remaining";
				}
			});
		}

		public String CurrentStage
		{
			get { return _currentStage; }
			set
			{
				if (_currentStage != value)
				{
					_currentStage = value;
					NotifyPropertyChanged(nameof(CurrentStage));
				}
			}
		}

		public String StatusMessage
		{
			get { return _statusMessage; }
			set
			{
				if (_statusMessage != value)
				{
					_statusMessage = value;
					NotifyPropertyChanged(nameof(StatusMessage));
				}
			}
		}

		public String ProgressText
		{
			get { return _progressText; }
			set
			{
				if (_progressText != value)
				{
					_progressText = value;
					NotifyPropertyChanged(nameof(ProgressText));
				}
			}
		}

		public Int32 OverallProgress
		{
			get { return _overallProgress; }
			set
			{
				if (_overallProgress != value)
				{
					_overallProgress = value;
					NotifyPropertyChanged(nameof(OverallProgress));
				}
			}
		}

		private bool _restoreEnabled;

		public bool RestoreEnabled
		{
			get
			{
				return _restoreEnabled;
			}
			set
			{
				_restoreEnabled = value;
				NotifyPropertyChanged(nameof(RestoreEnabled));
			}
		}

		public Int32 StageProgress
		{
			get { return _stageProgress; }
			set
			{
				if (_stageProgress != value)
				{
					_stageProgress = value;
					NotifyPropertyChanged(nameof(StageProgress));
				}
			}
		}

		public Boolean IsStageProgressIndeterminate
		{
			get { return _isStageProgressIndeterminate; }
			set
			{
				if (_isStageProgressIndeterminate != value)
				{
					_isStageProgressIndeterminate = value;
					NotifyPropertyChanged(nameof(IsStageProgressIndeterminate));
				}
			}
		}

		public String DriverPackStatus
		{
			get { return _driverPackStatus; }
			set
			{
				if (_driverPackStatus != value)
				{
					_driverPackStatus = value;
					NotifyPropertyChanged(nameof(DriverPackStatus));
				}
			}
		}

		public String DriverPackDetails
		{
			get { return _driverPackDetails; }
			set
			{
				if (_driverPackDetails != value)
				{
					_driverPackDetails = value;
					NotifyPropertyChanged(nameof(DriverPackDetails));
				}
			}
		}

		public void AddLog(String level, String message, int collapseAutomatically = 0)
		{
			Console.WriteLine($"{DateTime.Now}\t{level}\t{message}");

			LogItemModel logItem = new LogItemModel
			{
				Level = level,
				Message = message
			};

			LogItems.Insert(0, logItem);

			if (collapseAutomatically > 0)
			{
				CollapseLogItemAfterDelay(logItem, TimeSpan.FromSeconds(collapseAutomatically));
			}
		}

		private void CollapseLogItemAfterDelay(LogItemModel logItem, TimeSpan delay)
		{
			DispatcherTimer timer = new DispatcherTimer
			{
				Interval = delay
			};

			timer.Tick += delegate
			{
				timer.Stop();
				logItem.Visibility = Visibility.Collapsed;
			};

			timer.Start();
		}
	}
}
