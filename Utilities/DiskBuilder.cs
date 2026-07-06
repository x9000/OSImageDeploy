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

			OnProgress("Preparing Disk", $"Preparing WINPE environment from ADK.", percent: 22);
			String winPEInstallFolder = "";

			String[] possibleADKLocations = new string[] {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Windows Kits\10\Assessment and Deployment Kit"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Kits\10\Assessment and Deployment Kit")};
			foreach (string root in possibleADKLocations)
			{
				string path = Path.Combine(root, "Windows Preinstallation Environment");
				if (Directory.Exists(path))
				{
					winPEInstallFolder = path;
				}
			}
			String workingFolder = Path.Combine(Path.GetTempPath(), $"WinPEBuild_{Guid.NewGuid()}");
			Directory.CreateDirectory(workingFolder);
			Directory.CreateDirectory(Path.Combine(workingFolder, "media"));
			Directory.CreateDirectory(Path.Combine(workingFolder, @"media\Sources"));
			Directory.CreateDirectory(Path.Combine(workingFolder, "pedrivers"));
			Directory.CreateDirectory(Path.Combine(workingFolder, "mount"));
			FSManager.CopyDirectory(Path.Combine(winPEInstallFolder, @"amd64\Media"), Path.Combine($"{workingFolder}\\media"));
			File.Copy(Path.Combine(winPEInstallFolder, @"amd64\en-us\winpe.wim"), Path.Combine(workingFolder, @"media\Sources\Boot.wim"));
			OnProgress("Preparing Disk", $"Mounting Boot.wim file.", percent: 25);

			using WimImageService service = new WimImageService();
			{
				service.ProgressChanged += Service_ProgressChanged;

				await using WimServicingSession wimSession = await service.MountForServicingAsync(Path.Combine(workingFolder, @"media\Sources\Boot.wim"), 1, Path.Combine(workingFolder, @"mount"));
				{

					ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory, "DellPEDrivers.zip"), Path.Join(workingFolder, "pedrivers"));
					ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory, "HPPEDrivers.zip"), Path.Join(workingFolder, "pedrivers"));

					String[] packages = new String[] { "WinPE-NetFX.cab", "WinPE-PowerShell.cab", "WinPE-WMI.cab", "WinPE-Scripting.cab", "WinPE-DismCmdlets.cab", "WinPE-StorageWMI.cab", "WinPE-HSP-Driver.cab", "WinPE-SecureStartup.cab", "WinPE-EnhancedStorage.cab", "WinPE-FMAPI.cab", "WinPE-PlatformId.cab" };
					foreach (String package in packages)
					{
						OnProgress("Preparing Disk", $"Adding packages to WinPE ({package})", percent: (26 + packages.IndexOf(package)));
						wimSession.AddPackage(Path.Join(Path.Join(winPEInstallFolder, @"amd64\WinPE_OCs"), package));
					}

					//Copy client app to WINPE
					if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "WinPEClient")))
					{
						FSManager.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "WinPEClient"), Path.Combine(workingFolder, @"mount\WinPEClient"));
					}
					else
					{
						FSManager.CopyDirectory(@"C:\Users\PaulPrior\OneDrive - x9000.com\_Repository\VSProjects\2026\OSImageDeploy\OSImageDeployClient\bin\Release\net10.0-windows\publish\win-x64", Path.Combine(workingFolder, @"mount\WinPEClient"));
					}

					//File.WriteAllLines(Path.Combine(workingFolder, @"mount\Windows\System32\WinPEShl.ini"), new String[] { "[LaunchApp]", @"AppPath = \WinPEClient\OSImageDeployClient.exe" });
					File.WriteAllLines(Path.Combine(workingFolder, @"mount\Windows\System32\startnet.cmd"), new String[] { "@echo off", "echo Initialising environment.", "wpeinit", "echo Starting imaging tool.", @"\WinPEClient\OSImageDeployClient.exe", "Exit" });

					OnProgress("Preparing Disk", $"Adding drivers", percent: 50);
					wimSession.AddDriver(Path.Combine(workingFolder, "pedrivers"), true, false);


					OnProgress("Preparing Disk", $"Dismounting WIM image", percent: 60);
					await wimSession.UnmountAsync(commit: true);
				}
			}
			OnProgress("Preparing Disk", $"Dismounted WIM image", percent: 85);
			FSManager.CopyDirectory(Path.Join(workingFolder,"media"), $"{winPEPartitionDriveLetter}:\\");
			OnProgress("Preparing Disk", $"USB Build Complete.", percent: 100);
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