#nullable disable
using Imaging;
using Microsoft.Management.Infrastructure;
using Microsoft.Win32;
using System.IO.Compression;
using x9000.Utilities;

namespace Utilities
{
	public sealed class DiskBuilder
	{
		private const string Ns = @"root\Microsoft\Windows\Storage";

		private void OnProgress(string stage, string message, int? percent = null)
		{
			ProgressChanged?.Invoke(this, new DiskBuilderProgressEventArgs(stage, message, percent));
		}

		public event EventHandler<DiskBuilderProgressEventArgs> ProgressChanged;
		public sealed class DiskBuilderProgressEventArgs : EventArgs
		{
			public DiskBuilderProgressEventArgs(string stage, string message, int? percent)
			{
				Stage = stage;
				Message = message;
				Percent = percent;
			}

			public string Stage { get; private set; }

			public string Message { get; private set; }

			public int? Percent { get; private set; }
		}

		public Task PrepareDiskAsync(uint diskNumber, CancellationToken cancellationToken = default)
		{
			OnProgress("Preparing Disk", $"Preparing disk number {diskNumber}...");
			Task returnValue = Task.Run(async () =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				await PrepareDisk(diskNumber);
			}, cancellationToken);
			OnProgress("Preparing Disk", $"Disk number {diskNumber} preparation complete.", percent: 100);
			return returnValue;
		}



		public async Task PrepareDisk(uint diskNumber)
		{
			using CimSession session = CimSession.Create(null);
			//Disable user autoplay selection to prevent explorer flashing.
			
			//RegistryKey autoplayKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\EventHandlersDefaultSelection\StorageOnArrival", true);
			//String currentAutoPlayOnStorageSetting = autoplayKey.GetValue("", "").ToString();
			//autoplayKey.SetValue("", "MSTakeNoAction", RegistryValueKind.String);

			CimInstance disk = GetDisk(session, diskNumber);
			OnProgress("Preparing Disk", $"Setting disk to be read/write.", percent: 10);
			// Equivalent to: Set-Disk -IsReadOnly $false
			Invoke(session, disk, "SetAttributes", new()
			{
				["IsReadOnly"] = false
			}, ignoreErrors: false);

			// Equivalent to: Initialize-Disk -ErrorAction SilentlyContinue
			OnProgress("Preparing Disk", $"Initialising disk.", percent: 12);
			Invoke(session, disk, "Initialize", new()
			{
				["PartitionStyle"] = (ushort)2 // GPT
			}, ignoreErrors: true);

			// Equivalent to: Clear-Disk -RemoveData -Confirm:$false
			OnProgress("Preparing Disk", $"Clearing disk.", percent: 15);
			Invoke(session, disk, "Clear", new()
			{
				["RemoveData"] = true,
				["RemoveOEM"] = false,
				["ZeroOutEntireDisk"] = false
			}, ignoreErrors: true);

			disk = GetDisk(session, diskNumber);

			Invoke(session, disk, "Initialize", new()
			{
				["PartitionStyle"] = (ushort)2 // GPT
			}, ignoreErrors: true);

			disk = GetDisk(session, diskNumber);

			// New-Partition -Size 4GB -AssignDriveLetter
			OnProgress("Preparing Disk", $"Creating bootable FAT32 partition.", percent: 17);
			CimInstance winPePartition = CreatePartition(session, disk,	sizeBytes: 4UL * 1024 * 1024 * 1024, useMaximumSize: false);
			String winPEPartitionDriveLetter = Convert.ToString(winPePartition.CimInstanceProperties["DriveLetter"].Value) ?? "";

			FormatPartition(session, winPePartition, "FAT32", "WinPE");

			disk = GetDisk(session, diskNumber);

			// New-Partition -UseMaximumSize -AssignDriveLetter
			OnProgress("Preparing Disk", $"Creating NTFS data partition.", percent: 20);
			CimInstance dataPartition = CreatePartition(session, disk, sizeBytes: null,	useMaximumSize: true);
			FormatPartition(session, dataPartition, "NTFS", "BuildData");


			////Restore Autoplay settings
			//autoplayKey.SetValue("", currentAutoPlayOnStorageSetting, RegistryValueKind.String);


			String dataPartitionDriveLetter = Convert.ToString(dataPartition.CimInstanceProperties["DriveLetter"].Value) ?? "";
			Directory.CreateDirectory($"{dataPartitionDriveLetter}:\\DriverPacks");
			Directory.CreateDirectory($"{dataPartitionDriveLetter}:\\WindowsImages");

			WinPeBuildResult winPeBuildResult =	await BuildWinPeMediaAsync();

			OnProgress(
				"Preparing Disk",
				"Copying WinPE environment to USB drive.",
				percent: 85);

			FSManager.CopyDirectory(
				winPeBuildResult.MediaFolder,
				$"{winPEPartitionDriveLetter}:\\");

			OnProgress(
				"Preparing Disk",
				"USB Build Complete.",
				percent: 100);

		}

		private async Task<WinPeBuildResult> BuildWinPeMediaAsync()
		{
			OnProgress(
				"Preparing Disk",
				"Preparing WinPE environment from ADK.",
				percent: 22);

			String winPeInstallFolder = "";

			String[] possibleAdkLocations = new String[]
			{
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			@"Windows Kits\10\Assessment and Deployment Kit"),

		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			@"Windows Kits\10\Assessment and Deployment Kit")
			};

			foreach (String root in possibleAdkLocations)
			{
				String path = Path.Combine(
					root,
					"Windows Preinstallation Environment");

				if (Directory.Exists(path))
				{
					winPeInstallFolder = path;
					break;
				}
			}

			if (String.IsNullOrWhiteSpace(winPeInstallFolder))
			{
				throw new DirectoryNotFoundException(
					"The Windows ADK WinPE installation folder could not be found.");
			}

			String workingFolder = Path.Combine(
				Path.GetTempPath(),
				$"WinPEBuild_{Guid.NewGuid()}");

			String mediaFolder = Path.Combine(workingFolder, "media");
			String sourcesFolder = Path.Combine(mediaFolder, "Sources");
			String driverFolder = Path.Combine(workingFolder, "pedrivers");
			String mountFolder = Path.Combine(workingFolder, "mount");

			Directory.CreateDirectory(workingFolder);
			Directory.CreateDirectory(mediaFolder);
			Directory.CreateDirectory(sourcesFolder);
			Directory.CreateDirectory(driverFolder);
			Directory.CreateDirectory(mountFolder);

			String adkMediaFolder = Path.Combine(
				winPeInstallFolder,
				@"amd64\Media");

			String sourceWimPath = Path.Combine(
				winPeInstallFolder,
				@"amd64\en-us\winpe.wim");

			String bootWimPath = Path.Combine(
				sourcesFolder,
				"Boot.wim");

			FSManager.CopyDirectory(
				adkMediaFolder,
				mediaFolder);

			File.Copy(
				sourceWimPath,
				bootWimPath);

			OnProgress(
				"Preparing Disk",
				"Mounting Boot.wim file.",
				percent: 25);

			using WimImageService service = new WimImageService();

			service.ProgressChanged += Service_ProgressChanged;

			await using WimServicingSession wimSession =
				await service.MountForServicingAsync(
					bootWimPath,
					1,
					mountFolder);

			ZipFile.ExtractToDirectory(
				Path.Combine(
					AppContext.BaseDirectory,
					"DellPEDrivers.zip"),
				driverFolder);

			ZipFile.ExtractToDirectory(
				Path.Combine(
					AppContext.BaseDirectory,
					"HPPEDrivers.zip"),
				driverFolder);

			String[] packages = new String[]
			{
		"WinPE-NetFX.cab",
		"WinPE-PowerShell.cab",
		"WinPE-WMI.cab",
		"WinPE-Scripting.cab",
		"WinPE-DismCmdlets.cab",
		"WinPE-StorageWMI.cab",
		"WinPE-HSP-Driver.cab",
		"WinPE-SecureStartup.cab",
		"WinPE-EnhancedStorage.cab",
		"WinPE-FMAPI.cab",
		"WinPE-PlatformId.cab"
			};

			for (Int32 packageIndex = 0;
				packageIndex < packages.Length;
				packageIndex++)
			{
				String package = packages[packageIndex];

				OnProgress(
					"Preparing Disk",
					$"Adding packages to WinPE ({package})",
					percent: 26 + packageIndex);

				String packagePath = Path.Combine(
					winPeInstallFolder,
					@"amd64\WinPE_OCs",
					package);

				wimSession.AddPackage(packagePath);
			}

			String packagedWinPeClientFolder = Path.Combine(
				AppContext.BaseDirectory,
				"WinPEClient");

			String destinationWinPeClientFolder = Path.Combine(
				mountFolder,
				"WinPEClient");

			if (Directory.Exists(packagedWinPeClientFolder))
			{
				FSManager.CopyDirectory(
					packagedWinPeClientFolder,
					destinationWinPeClientFolder);
			}
			else
			{
				String developmentWinPeClientFolder =
					@"C:\Users\PaulPrior\OneDrive - x9000.com\_Repository\VSProjects\2026\OSImageDeploy\OSImageDeployClient\bin\Release\net10.0-windows\publish\win-x64";

				FSManager.CopyDirectory(
					developmentWinPeClientFolder,
					destinationWinPeClientFolder);
			}

			String startNetPath = Path.Combine(
				mountFolder,
				@"Windows\System32\startnet.cmd");

			File.WriteAllLines(
				startNetPath,
				new String[]
				{
			"@echo off",
			"echo Initialising environment.",
			"wpeinit",
			"echo Starting imaging tool.",
			@"\WinPEClient\OSImageDeployClient.exe",
			"Exit"
				});

			OnProgress(
				"Preparing Disk",
				"Adding drivers",
				percent: 50);

			wimSession.AddDriver(
				driverFolder,
				true,
				false);

			OnProgress(
				"Preparing Disk",
				"Dismounting WIM image",
				percent: 60);

			await wimSession.UnmountAsync(commit: true);

			OnProgress(
				"Preparing Disk",
				"Dismounted WIM image",
				percent: 85);

			return new WinPeBuildResult
			{
				WorkingFolder = workingFolder,
				MediaFolder = mediaFolder,
				DriverFolder = driverFolder,
				MountFolder = mountFolder,
				BootWimPath = bootWimPath,
				WasLoadedFromCache = false
			};
		}

		private void Service_ProgressChanged(object sender, WimOperationProgressEventArgs e)
		{
			OnProgress(e.OperationName, $"{e.Current} / {e.Total}", Convert.ToInt32(e.Percentage));
		}

		private static CimInstance GetDisk(CimSession session, uint diskNumber)
		{
			string query = $"SELECT * FROM MSFT_Disk WHERE Number = {diskNumber}";
			return session.QueryInstances(Ns, "WQL", query).Single();
		}

		private static CimInstance CreatePartition(CimSession session, CimInstance disk, ulong? sizeBytes, bool useMaximumSize)
		{
			CimMethodParametersCollection args = new CimMethodParametersCollection{
			CimMethodParameter.Create("AssignDriveLetter", true, CimFlags.In),
			CimMethodParameter.Create("UseMaximumSize", useMaximumSize, CimFlags.In)
			};

			if (sizeBytes.HasValue)
				args.Add(CimMethodParameter.Create("Size", sizeBytes.Value, CimFlags.In));

			CimMethodResult result = session.InvokeMethod(Ns, disk, "CreatePartition", args);

			CheckReturnCode(result);

			// CreatedPartition is an embedded MSFT_Partition object path/string.
			// Easiest practical approach: re-query latest partition on disk.
			uint diskNumber = (uint)disk.CimInstanceProperties["Number"].Value;

			return session.QueryInstances(Ns, "WQL", $"SELECT * FROM MSFT_Partition WHERE DiskNumber = {diskNumber}").OrderByDescending(p => (ulong)p.CimInstanceProperties["Size"].Value).First();
		}

		private static void FormatPartition(CimSession session, CimInstance partition, string fileSystem, string label)
		{
			char driveLetter = (char)partition.CimInstanceProperties["DriveLetter"].Value;

			CimInstance volume = session.QueryInstances(Ns, "WQL", $"SELECT * FROM MSFT_Volume WHERE DriveLetter = '{driveLetter}'").Single();

			Invoke(session, volume, "Format", new()
			{
				["FileSystem"] = fileSystem,
				["FileSystemLabel"] = label,
				["Full"] = false,
				["Force"] = true
			}, ignoreErrors: false);
		}

		private static void Invoke(CimSession session, CimInstance instance, string method, Dictionary<string, object> values, bool ignoreErrors)
		{
			CimMethodParametersCollection parameters = new CimMethodParametersCollection();
			foreach (KeyValuePair<string, object> kvp in values)
			{
				parameters.Add(CimMethodParameter.Create(kvp.Key, kvp.Value, CimFlags.In));
			}

			CimMethodResult result = session.InvokeMethod(Ns, instance, method, parameters);

			if (!ignoreErrors)
			{
				CheckReturnCode(result);
			}
		}

		private static void CheckReturnCode(CimMethodResult result)
		{
			uint code = Convert.ToUInt32(result.ReturnValue.Value);
			if (code != 0)
			{
				throw new InvalidOperationException($"Storage WMI call failed. ReturnCode={code}");
			}
		}
	}
}