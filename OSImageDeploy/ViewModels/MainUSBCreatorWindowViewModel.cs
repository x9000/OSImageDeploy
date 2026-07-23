using Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
			_diskBuilder = new DiskBuilder();
			_diskBuilder.ProgressChanged += _diskBuilder_ProgressChanged;
			_winPeMediaCacheManager =
				new WinPeMediaCacheManager();

			RefreshUSBButtonCommand = new RelayCommand(execute: RefreshUSBButtonClickHandler, canExecute: RefreshUSBButtonCanExecuteHandler);
			CreateUSBCommand = new RelayCommand<uint>(execute: CreateUSBClickHandler);
			ExitCommand = new RelayCommand(execute: ExitCommandHandler, canExecute: ExitCanExecuteHandler);
			StartPrereqInstalls = new RelayCommand(execute: StartPrereqInstallsHandler);
			RebuildWinPeCacheCommand =
				new RelayCommand(execute: RebuildWinPeCacheCommandHandler);

			_ = PerformPrereqTestingAsync();
			_ = PopulateUSBDriveListAsync();
			_ = RefreshWinPeCacheStatusAsync();
		}

		private void _diskBuilder_ProgressChanged(object sender, DiskBuilder.DiskBuilderProgressEventArgs e)
		{
			
			InfoTextBlockText = e.Stage + " - " + e.Message;
		}

		#endregion

		#region Fields

		private readonly WindowsAdkWinPeInstaller _installer;
		private readonly DiskBuilder _diskBuilder;
		private readonly WinPeMediaCacheManager _winPeMediaCacheManager;

		private String _infoTextBlockText = "";
		private String _subInfoTextBlockText = "";
		private String _winPeCacheStatusText = "WinPE cache: Checking...";
		private String _winPeCacheDetailsText = "";
		private String _titleTextBlockText = $"OS Image Deployment Tool v{Assembly.GetEntryAssembly().GetName().Version.Major}.{Assembly.GetEntryAssembly().GetName().Version.Minor}.{Assembly.GetEntryAssembly().GetName().Version.Build}";
		private int _usbComboxSelectedItemIndex;
		private bool _createUSBButtonEnabled = false;
		private bool _rebuildWinPeCacheButtonEnabled;
		private bool _preReqInstallButtonEnabled = false;
		private bool _exitButtonEnabled = true;
		private bool _refreshUSBButtonEnabled = true;
		private Visibility _prereqStackPanelVisibility = Visibility.Collapsed;
		private Visibility _mainStackPanelVisibility = Visibility.Visible;

		#endregion

		#region Commands

		public RelayCommand<uint> CreateUSBCommand { get; }
		public RelayCommand ExitCommand { get; }
		public RelayCommand RefreshUSBButtonCommand { get; }
		public RelayCommand StartPrereqInstalls { get; }
		public RelayCommand RebuildWinPeCacheCommand { get; }

		#endregion

		#region Collections

		public ObservableCollection<PreCheckModel> PreReqChecks { get; set; } = new ObservableCollection<PreCheckModel>();
		public ObservableCollection<FSManager.DiskInfo> ListOfUSBDrives { get; set; } = new ObservableCollection<FSManager.DiskInfo>();

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
				if (!_winPeMediaCacheManager.CacheExists)
				{
					WinPeCacheStatusText =
						"WinPE cache: Not available";

					WinPeCacheDetailsText =
						"A new cache will be created during the next USB build.";

					RebuildWinPeCacheButtonEnabled = false;

					return;
				}

				WinPeCacheManifest manifest =
					await _winPeMediaCacheManager.LoadManifestAsync();

				if (manifest == null)
				{
					WinPeCacheStatusText =
						"WinPE cache: Incomplete";

					WinPeCacheDetailsText =
						"The cache will be rebuilt during the next USB build.";

					RebuildWinPeCacheButtonEnabled = true;

					return;
				}

				FileInfo archiveInfo =
					new FileInfo(
						_winPeMediaCacheManager.ArchivePath);

				String createdText =
					manifest.CreatedUtc == default
						? "Unknown"
						: manifest.CreatedUtc
							.ToLocalTime()
							.ToString("g");

				WinPeCacheStatusText =
					"WinPE cache: Available";

				WinPeCacheDetailsText =
					$"Created: {createdText}    " +
					$"Size: {archiveInfo.Length / 1024D / 1024D:F1} MB" +
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
				_winPeMediaCacheManager.Delete();

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

			await _installer.InstallOrModifyAsync();

			ExitButtonEnabled = true;
			PreReqChecks.Clear();

			_ = PerformPrereqTestingAsync();
			_ = PopulateUSBDriveListAsync();
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
			List<FSManager.DiskInfo> disks = await Task.Run(() =>
				FSManager.EnumerateDisks()
					.Where(disk => disk.InterfaceType.Contains("USB"))
					.ToList());

			ListOfUSBDrives.Clear();

			if (disks.Count > 0)
			{
				foreach (FSManager.DiskInfo disk in disks)
				{
					ListOfUSBDrives.Add(disk);
				}
			}
			else
			{
				ListOfUSBDrives.Add(new FSManager.DiskInfo { Name = "No USB suitable devices found." });
			}

			USBComboxSelectedItemIndex = 0;

			bool prerequisitesInstalled = ArePrerequisitesInstalled();

			PreReqInstallButtonEnabled = !prerequisitesInstalled;
			UpdatePrerequisitePanelVisibility(prerequisitesInstalled);

			CreateUSBButtonEnabled = prerequisitesInstalled && disks.Count > 0;
		}

		#endregion

		#region Command Handlers

		private bool ExitCanExecuteHandler()
		{
			return true;
		}

		private void ExitCommandHandler()
		{
			Environment.Exit(0);
		}

		private async void CreateUSBClickHandler(uint diskNumber)
		{
			ExitButtonEnabled = false;
			CreateUSBButtonEnabled = false;
			RefreshUSBButtonEnabled = false;
			RebuildWinPeCacheButtonEnabled = false;
			DiskBuilder diskBuilder = new DiskBuilder();
			diskBuilder.ProgressChanged += DiskBuilder_ProgressChanged;

			try
			{
				await diskBuilder.PrepareDiskAsync(diskNumber);
			}
			catch (Exception exception)
			{
				InfoTextBlockText =
					"USB creation failed.";

				SubInfoTextBlockText = "";

				AppLog.Error(
					$"USB creation failed for disk number {diskNumber}.",
					exception);

				MessageBox.Show(
					"The bootable USB could not be created." +
					Environment.NewLine +
					Environment.NewLine +
					exception.Message +
					Environment.NewLine +
					Environment.NewLine +
					"See the application log for full details.",
					"USB Creation Failed",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
			finally
			{
				diskBuilder.ProgressChanged -=
					DiskBuilder_ProgressChanged;

				ExitButtonEnabled = true;
				CreateUSBButtonEnabled = true;
				RefreshUSBButtonEnabled = true;

				await RefreshWinPeCacheStatusAsync();
			}
		}

		private void DiskBuilder_ProgressChanged(object sender, DiskBuilder.DiskBuilderProgressEventArgs e)
		{
			if (e.Stage.StartsWith("DISM"))
			{
				if (e.Percent != null)
				{
					SubInfoTextBlockText = $"{e.Stage} - {e.Message} {e.Percent}%";
				}
				else
				{
					SubInfoTextBlockText = e.Message;
				}
			}
			else
			{
				SubInfoTextBlockText = "";
				if (e.Percent != null)
				{
					InfoTextBlockText = $"{e.Stage} - {e.Message} {e.Percent}%";
				}
				else
				{
					InfoTextBlockText = e.Message;
				}
			}

			
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
	}
}
