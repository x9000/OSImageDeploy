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
using System.Xml.Linq;
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
		private Boolean _isAutomaticDeployment;
		private Boolean _restoreInProgress;
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



		private Boolean PromptForWim()
		{
			String wimDirectoryRoot = FindWindowsImagesDirectory();

			if (String.IsNullOrWhiteSpace(wimDirectoryRoot))
			{
				AddLog("ERROR", "No WindowsImages folder was found on attached deployment media.", 0);
				StatusMessage = "No Windows image folder was found.";
				StartButtonText = "Select Image";
				return false;
			}

			AddLog("INFO", $"WIM Directory is {wimDirectoryRoot}", 0);
			WimSelectionWindow wimSelectionWindow = new WimSelectionWindow
			{
				Owner = Application.Current.MainWindow
			};

			foreach(String wimFilePath in Directory.GetFiles(wimDirectoryRoot, "*.wim"))
			{
				wimSelectionWindow.WimSelectionVM.AvailableWimFiles.Add(wimFilePath);
			}

			Boolean? result = wimSelectionWindow.ShowDialog();
			if (result == true && wimSelectionWindow.WimSelectionVM.SelectedImage != null)
			{
				AddLog("INFO", "Selected WIM image: " + wimSelectionWindow.WimSelectionVM.SelectedImage.Name + " Index: " + wimSelectionWindow.WimSelectionVM.SelectedImage.Index, 0);
				SelectedWimFilePath = wimSelectionWindow.WimSelectionVM.SelectedWimFilePath;
				SelectedWimIndex = wimSelectionWindow.WimSelectionVM.SelectedImage.Index;
				StartButtonText = "Start Restore";
				return true;
			}

			StartButtonText = "Select Image";
			return false;
		}

		private String FindWindowsImagesDirectory()
		{
			String wimDirectoryRoot = String.Empty;

			foreach (DriveInfo drive in DriveInfo.GetDrives())
			{
				AddLog("INFO", $"Drive {drive.Name} is {drive.DriveType.ToString()}", 0);
				if (!drive.IsReady ||
					drive.DriveType != DriveType.Fixed &&
						drive.DriveType != DriveType.Removable)
				{
					continue;
				}

				String candidateDirectory = Path.Combine(
					drive.RootDirectory.FullName,
					"WindowsImages");

				if (Directory.Exists(candidateDirectory))
				{
					wimDirectoryRoot = candidateDirectory;
				}
			}

			return wimDirectoryRoot;
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
			await StartRestoreAsync();
		}

		private async Task StartRestoreAsync()
		{
			if (_restoreInProgress)
			{
				return;
			}

			if (SelectedWimIndex == 0)
			{
				PromptForWim();
				return;
			}

			DriverPackSelection driverPackSelection;

			try
			{
				driverPackSelection = await RefreshDriverPackSelectionAsync();
			}
			catch (Exception ex)
			{
				AddLog("ERROR", "Driver-pack preflight failed: " + ex.Message, 0);

				if (!_isAutomaticDeployment)
				{
					MessageBox.Show(
						Application.Current.MainWindow,
						"The driver-pack preflight could not be completed. No changes have been made to the target disk.\n\n" +
							ex.Message,
						"Driver-pack preflight failed",
						MessageBoxButton.OK,
						MessageBoxImage.Error);
				}

				return;
			}

			_restoreInProgress = true;
			RestoreEnabled = false;
			StatusMessage = _isAutomaticDeployment
				? "Automatic Windows deployment is running."
				: "Restoring";
			CurrentStage = "Starting Restore...";
			_applyImageCancellationTokenSource.Dispose();
			_applyImageCancellationTokenSource = new CancellationTokenSource();

			try
			{
				await CreateDiskLayoutAsync();
				await ApplyImageAsync(SelectedWimFilePath, SelectedWimIndex);
				await _wimImageService.AddDriverPacksToAppliedWindowsAsync(
					driverPackSelection.DriverPackPaths,
					cancellationToken: _applyImageCancellationTokenSource.Token);

				RunProcessAndRequireSuccess(
					@"W:\Windows\System32\bcdboot.exe",
					@"W:\Windows /s S:",
					"Configuring Windows boot files");

				Directory.CreateDirectory(@"R:\Recovery\WindowsRE");
				File.Copy(
					@"W:\Windows\System32\Recovery\Winre.wim",
					@"R:\Recovery\Winre.wim",
					overwrite: true);
				RunProcessAndRequireSuccess(
					@"W:\Windows\System32\Reagentc.exe",
					@"/setreimage /path R:\Recovery\Winre.wim /target W:\Windows",
					"Configuring the Windows recovery image");
				String windowsBCDGuid = Utilities.BCDHelper.GetWindowsGUID();
				RunProcessAndRequireSuccess(
					@"W:\Windows\System32\Reagentc.exe",
					@"/enable /osguid " + windowsBCDGuid,
					"Enabling the Windows recovery environment");
				FSManager.RemoveDriveLetter('R');
				OverallProgress = 100;
				StatusMessage = "Windows deployment completed successfully.";
				CurrentStage = _isAutomaticDeployment ? "Rebooting" : "Complete";
				AddLog("SUCCESS", "Windows deployment completed successfully.", 0);

				if (_isAutomaticDeployment)
				{
					ProgressText = "Automatic deployment completed. Rebooting now.";
					RunProcessAndRequireSuccess(
						"wpeutil.exe",
						"Reboot",
						"Rebooting after automatic deployment",
						waitForExit: false);
					return;
				}

				Environment.Exit(0);
			}
			catch (OperationCanceledException)
			{
				StatusMessage = "Windows deployment was cancelled.";
				CurrentStage = "Cancelled";
				ProgressText = "Deployment did not complete.";
				AddLog("WARN", "Windows deployment was cancelled.", 0);
			}
			catch (Exception ex)
			{
				StatusMessage = "Windows deployment failed.";
				CurrentStage = "Failed";
				ProgressText = ex.Message;
				AddLog("ERROR", ex.ToString(), 0);
			}
			finally
			{
				_restoreInProgress = false;

				if (OverallProgress < 100)
				{
					RestoreEnabled = true;
				}
			}
		}

		private static void RunProcessAndRequireSuccess(
			String fileName,
			String arguments,
			String operationName,
			Boolean waitForExit = true)
		{
			using Process process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = fileName,
					Arguments = arguments,
					UseShellExecute = false,
					CreateNoWindow = true
				}
			};

			if (!process.Start())
			{
				throw new InvalidOperationException(
					operationName + " could not be started.");
			}

			if (!waitForExit)
			{
				return;
			}

			process.WaitForExit();

			if (process.ExitCode != 0)
			{
				throw new InvalidOperationException(
					operationName + " failed with exit code " + process.ExitCode + ".");
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

		private CancellationTokenSource _applyImageCancellationTokenSource;

		public async Task ApplyImageAsync(String selectedWimFile,int selectedImageIndex)
		{
			if (!File.Exists(selectedWimFile))
			{
				throw new FileNotFoundException(
					"The selected WIM file does not exist.",
					selectedWimFile);
			}

			FileInfo fileInfo = new FileInfo(selectedWimFile);

			if (fileInfo.Length == 0)
			{
				throw new InvalidDataException(
					"The selected WIM file is empty: " + selectedWimFile);
			}

			if (selectedImageIndex <= 0)
			{
				throw new InvalidDataException(
					"The selected WIM image index must be positive.");
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

			CurrentStage = "Applying Windows image";
			await _wimImageService.ApplyImageAsync(
				wimPath: selectedWimFile,
				imageIndex: selectedImageIndex,
				targetPath: @"W:\",
				cancellationToken: _applyImageCancellationTokenSource.Token);

			AddLog("SUCCESS", "Image applied successfully.");
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
				throw new InvalidOperationException(
					"Disk layout creation failed.");
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
			AutomaticDeploymentPlan automaticPlan = null;

			try
			{
				automaticPlan = await Task.Run(() =>
					AutomaticDeploymentConfigurationFile.DiscoverOnMountedDrives(
						message => Application.Current.Dispatcher.Invoke(() =>
							AddLog("INFO", message, 0))));
			}
			catch (Exception ex)
			{
				AddLog("ERROR", "Automatic deployment preflight failed: " + ex.Message, 0);
				StatusMessage = "Automatic deployment was not started.";
				ProgressText = ex.Message;
			}

			if (automaticPlan != null)
			{
				try
				{
					await ValidateAutomaticWimSelectionAsync(automaticPlan);
					_isAutomaticDeployment = true;
					SelectedWimFilePath = automaticPlan.WimFilePath;
					SelectedWimIndex = automaticPlan.WimIndex;
					StartButtonText = "Automatic Restore";
					StatusMessage = "Automatic deployment preflight passed.";
					AddLog(
						"INFO",
						"Automatic deployment selected " +
							Path.GetFileName(automaticPlan.WimFilePath) +
							" index " + automaticPlan.WimIndex + ".",
						0);
					await StartRestoreAsync();
					return;
				}
				catch (Exception ex)
				{
					_isAutomaticDeployment = false;
					SelectedWimFilePath = String.Empty;
					SelectedWimIndex = 0;
					AddLog("ERROR", "Automatic deployment preflight failed: " + ex.Message, 0);
					StatusMessage = "Automatic deployment was not started.";
					ProgressText = ex.Message;
				}
			}

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

		private async Task ValidateAutomaticWimSelectionAsync(
			AutomaticDeploymentPlan plan)
		{
			ArgumentNullException.ThrowIfNull(plan);

			CurrentStage = "Validating automatic image";
			IsStageProgressIndeterminate = true;
			ProgressText = "Reading configured WIM metadata before Disk 0 is changed.";

			try
			{
				XDocument document = await _wimImageService.GetWimInfoAsync(
					plan.WimFilePath,
					_applyImageCancellationTokenSource.Token);
				Boolean indexExists = document
					.Descendants("IMAGE")
					.Select(element => element.Attribute("INDEX")?.Value)
					.Any(value =>
						Int32.TryParse(value, out Int32 index) &&
						index == plan.WimIndex);

				if (!indexExists)
				{
					throw new InvalidDataException(
						"The configured WimIndex does not exist in " +
							Path.GetFileName(plan.WimFilePath) + ".");
				}
			}
			finally
			{
				IsStageProgressIndeterminate = false;
				StageProgress = 0;
				CurrentStage = "Waiting";
			}
		}

		private void CancelClickHandler()
		{
			if (_restoreInProgress)
			{
				StatusMessage = "Cancellation requested.";
				ProgressText = "Waiting for the current deployment step to stop safely.";
				CancelDiskLayout();
				CancelApplyWindowsImage();
				return;
			}

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
