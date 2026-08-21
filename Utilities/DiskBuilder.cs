#nullable disable
using Imaging;
using Microsoft.Management.Infrastructure;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using x9000.Utilities;

namespace Utilities
{
	public sealed class DiskBuilder
	{
		private readonly Object _progressLock = new Object();
		private readonly Func<CancellationToken, Task<WinPeBuildResult>>
			_buildWinPeMediaAsync;
		private readonly IReadOnlyList<String> _driverArchivePaths;

		private Int32 _lastOverallProgress;

		public DiskBuilder()
			: this(Array.Empty<String>())
		{
		}

		public DiskBuilder(IEnumerable<String> driverArchivePaths)
		{
			ArgumentNullException.ThrowIfNull(driverArchivePaths);
			_driverArchivePaths = driverArchivePaths
				.Select(Path.GetFullPath)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			_buildWinPeMediaAsync = BuildWinPeMediaAsync;
		}

		internal DiskBuilder(
			Func<CancellationToken, Task<WinPeBuildResult>>
				buildWinPeMediaAsync)
		{
			_driverArchivePaths = Array.Empty<String>();
			_buildWinPeMediaAsync = buildWinPeMediaAsync ??
				throw new ArgumentNullException(nameof(buildWinPeMediaAsync));
		}
		private enum DiskBuildPhase
		{
			Cleanup,
			DiskPreparation,
			FreshWinPeBuild,
			CachedWinPeRestore,
			CopyingBootMedia,
			Complete
		}
		private void OnPhaseProgress(DiskBuildPhase phase, String message, Double phasePercent = 0)
		{
			phasePercent = Math.Clamp(
				phasePercent,
				0,
				100);

			Int32 rangeStart;
			Int32 rangeEnd;
			String stage;

			switch (phase)
			{
				case DiskBuildPhase.Cleanup:
					rangeStart = 0;
					rangeEnd = 2;
					stage = "Cleanup";
					break;

				case DiskBuildPhase.DiskPreparation:
					rangeStart = 70;
					rangeEnd = 85;
					stage = "Preparing Disk";
					break;

				case DiskBuildPhase.FreshWinPeBuild:
					rangeStart = 2;
					rangeEnd = 70;
					stage = "Building WinPE";
					break;

				case DiskBuildPhase.CachedWinPeRestore:
					rangeStart = 2;
					rangeEnd = 70;
					stage = "Restoring WinPE Cache";
					break;

				case DiskBuildPhase.CopyingBootMedia:
					rangeStart = 85;
					rangeEnd = 99;
					stage = "Copying Boot Media";
					break;

				case DiskBuildPhase.Complete:
					rangeStart = 100;
					rangeEnd = 100;
					stage = "Complete";
					break;

				default:
					throw new ArgumentOutOfRangeException(
						nameof(phase),
						phase,
						"Unknown disk build phase.");
			}

			Double phaseRange =
				rangeEnd - rangeStart;

			Int32 overallPercent =
				rangeStart +
				Convert.ToInt32(
					Math.Round(
						phaseRange * phasePercent / 100));

			lock (_progressLock)
			{
				if (overallPercent < _lastOverallProgress)
				{
					overallPercent = _lastOverallProgress;
				}
				else
				{
					_lastOverallProgress = overallPercent;
				}
			}

			OnProgress(
				stage,
				message,
				overallPercent);
		}
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

		private void ResetOverallProgress()
		{
			lock (_progressLock)
			{
				_lastOverallProgress = 0;
			}
		}

		public Task PrepareDiskAsync(uint diskNumber, CancellationToken cancellationToken = default)
		{
			return PrepareDiskAsync(
				_ => Task.FromResult(diskNumber),
				cancellationToken);
		}

		public Task PrepareDiskAsync(
			Func<CancellationToken, Task<UInt32>> resolveDiskNumberAsync,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(resolveDiskNumberAsync);

			ResetOverallProgress();
			OnPhaseProgress(
				DiskBuildPhase.Cleanup,
				"Preparing the selected USB target.",
				0);

			return Task.Run(async () =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				await PrepareDisk(
					resolveDiskNumberAsync,
					cancellationToken);
			}, cancellationToken);
		}



		public Task PrepareDisk(uint diskNumber)
		{
			return PrepareDisk(
				_ => Task.FromResult(diskNumber),
				CancellationToken.None);
		}

		private async Task PrepareDisk(
			Func<CancellationToken, Task<UInt32>> resolveDiskNumberAsync,
			CancellationToken cancellationToken)
		{
			OnPhaseProgress(
				DiskBuildPhase.Cleanup,
				"Removing temporary WinPE working folders.",
				0);

			DeleteAbandonedTemporaryFolders();
			cancellationToken.ThrowIfCancellationRequested();

			OnPhaseProgress(
				DiskBuildPhase.Cleanup,
				"Temporary folder cleanup complete.",
				100);

			WinPeBuildResult buildResult =
				await _buildWinPeMediaAsync(cancellationToken);

			try
			{
				UInt32 diskNumber =
					await resolveDiskNumberAsync(cancellationToken);

				cancellationToken.ThrowIfCancellationRequested();

				using CimSession session = CimSession.Create(null);

				CimInstance disk = GetDisk(session, diskNumber);
				OnPhaseProgress(
					DiskBuildPhase.DiskPreparation,
					"Setting disk to be read/write.",
					10);

				Invoke(session, disk, "SetAttributes", new()
				{
					["IsReadOnly"] = false
				}, ignoreErrors: false);

				cancellationToken.ThrowIfCancellationRequested();

				OnPhaseProgress(
					DiskBuildPhase.DiskPreparation,
					"Initialising disk.",
					20);

				Invoke(session, disk, "Initialize", new()
				{
					["PartitionStyle"] = (ushort)2 // GPT
				}, ignoreErrors: true);

				OnPhaseProgress(
					DiskBuildPhase.DiskPreparation,
					"Clearing disk.",
					35);

				Invoke(session, disk, "Clear", new()
				{
					["RemoveData"] = true,
					["RemoveOEM"] = false,
					["ZeroOutEntireDisk"] = false
				}, ignoreErrors: true);

				cancellationToken.ThrowIfCancellationRequested();

				disk = GetDisk(session, diskNumber);

				Invoke(session, disk, "Initialize", new()
				{
					["PartitionStyle"] = (ushort)2 // GPT
				}, ignoreErrors: true);

				disk = GetDisk(session, diskNumber);

				OnPhaseProgress(
					DiskBuildPhase.DiskPreparation,
					"Creating bootable FAT32 partition.",
					60);

				CimInstance winPePartition = CreatePartition(
					session,
					disk,
					sizeBytes: 4UL * 1024 * 1024 * 1024,
					useMaximumSize: false);

				String winPEPartitionDriveLetter =
					Convert.ToString(
						winPePartition.CimInstanceProperties["DriveLetter"].Value) ?? "";

				FormatPartition(session, winPePartition, "FAT32", "WinPE");

				disk = GetDisk(session, diskNumber);

				OnPhaseProgress(
					DiskBuildPhase.DiskPreparation,
					"Creating NTFS data partition.",
					80);

				CimInstance dataPartition = CreatePartition(
					session,
					disk,
					sizeBytes: null,
					useMaximumSize: true);

				FormatPartition(session, dataPartition, "NTFS", "BuildData");
				OnPhaseProgress(
					DiskBuildPhase.DiskPreparation,
					"Disk preparation complete.",
					100);

				cancellationToken.ThrowIfCancellationRequested();

				String dataPartitionDriveLetter = Convert.ToString(dataPartition.CimInstanceProperties["DriveLetter"].Value) ?? "";
				Directory.CreateDirectory($"{dataPartitionDriveLetter}:\\DriverPacks");
				Directory.CreateDirectory($"{dataPartitionDriveLetter}:\\WindowsImages");

				OnPhaseProgress(DiskBuildPhase.CopyingBootMedia, "Copying WinPE environment to the USB drive.",	0);
				cancellationToken.ThrowIfCancellationRequested();

				FSManager.CopyDirectory(
					buildResult.MediaFolder,
					$"{winPEPartitionDriveLetter}:\\");

				OnPhaseProgress(DiskBuildPhase.CopyingBootMedia, "WinPE environment copied to the USB drive.", 100);

				OnPhaseProgress(DiskBuildPhase.Complete, "USB build complete.",	100);
			}
			finally
			{
				TryDeleteDirectory(
					buildResult.WorkingFolder);
			}
		}

		private static void DeleteAbandonedTemporaryFolders()
		{
			String tempFolder = Path.GetTempPath();

			DeleteMatchingTemporaryFolders(
				tempFolder,
				"WinPEBuild_*");

			DeleteMatchingTemporaryFolders(
				tempFolder,
				"WinPECache_*");
		}

		private static void DeleteMatchingTemporaryFolders(
			String parentFolder,
			String searchPattern)
		{
			if (!Directory.Exists(parentFolder))
			{
				return;
			}

			String[] folders;

			try
			{
				folders = Directory.GetDirectories(
					parentFolder,
					searchPattern,
					SearchOption.TopDirectoryOnly);
			}
			catch
			{
				return;
			}

			foreach (String folder in folders)
			{
				TryDeleteDirectory(folder);
			}
		}

		private static void TryDeleteDirectory(
			String folderPath)
		{
			if (String.IsNullOrWhiteSpace(folderPath))
			{
				return;
			}

			try
			{
				if (Directory.Exists(folderPath))
				{
					Directory.Delete(
						folderPath,
						recursive: true);
				}
			}
			catch
			{
				// Cleanup is best effort only.
			}
		}

		private async Task<WinPeBuildResult> BuildWinPeMediaAsync(
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			Stopwatch operationStopwatch =
				Stopwatch.StartNew();

			WinPeEnvironment environment =
				WinPeEnvironment.Discover();

			AppLog.Information(
				$"Starting WinPE media operation for " +
				$"{environment.Architecture} using ADK identity " +
				$"'{environment.Version}'.");

			WinPeMediaCacheManager cacheManager = new WinPeMediaCacheManager();

			WinPeCacheManifest expectedManifest =
				await BuildWinPeCacheManifestAsync(
					environment);

			if (await cacheManager.IsValidAsync(
				expectedManifest,
				cancellationToken))
			{
				OnPhaseProgress(DiskBuildPhase.CachedWinPeRestore, "Valid WinPE cache found.", 5);

				String cachedWorkingFolder =
					Path.Combine(
						Path.GetTempPath(),
						$"WinPECache_{Guid.NewGuid()}");

				String cachedMediaFolder =
					Path.Combine(
						cachedWorkingFolder,
						"media");

				Directory.CreateDirectory(cachedMediaFolder);

				OnPhaseProgress(DiskBuildPhase.CachedWinPeRestore, "Extracting cached WinPE environment.", 20);
				await cacheManager.ExtractAsync(
					cachedMediaFolder,
					cancellationToken);
				OnPhaseProgress(DiskBuildPhase.CachedWinPeRestore, "Cached WinPE environment restored.", 100);

				operationStopwatch.Stop();

				AppLog.Information(
					$"WinPE media operation completed from cache in " +
					$"{operationStopwatch.Elapsed.TotalSeconds:F1} seconds.");

				return new WinPeBuildResult
				{
					WorkingFolder = cachedWorkingFolder,
					MediaFolder = cachedMediaFolder,
					DriverFolder = "",
					MountFolder = "",
					BootWimPath = Path.Combine(
						cachedMediaFolder,
						@"Sources\Boot.wim"),
					WasLoadedFromCache = true
				};
			}

			OnPhaseProgress(DiskBuildPhase.FreshWinPeBuild, "Preparing WinPE environment from ADK.", 0);

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
			cancellationToken.ThrowIfCancellationRequested();

			String adkMediaFolder =	environment.MediaFolder;

			String sourceWimPath =	environment.SourceBootWim;

			String bootWimPath = Path.Combine(
				sourcesFolder,
				"Boot.wim");

			OnPhaseProgress(DiskBuildPhase.FreshWinPeBuild,	"Copying standard WinPE media.", 5);

			FSManager.CopyDirectory(
				adkMediaFolder,
				mediaFolder);

			File.Copy(
				sourceWimPath,
				bootWimPath);

			OnPhaseProgress(DiskBuildPhase.FreshWinPeBuild,	"Mounting Boot.wim.", 10);

			using WimImageService service = new WimImageService();

			service.ProgressChanged += Service_ProgressChanged;

			await using WimServicingSession wimSession =
				await service.MountForServicingAsync(
					bootWimPath,
					1,
					mountFolder,
					cancellationToken: cancellationToken);

			cancellationToken.ThrowIfCancellationRequested();

			ExtractDriverArchives(_driverArchivePaths, driverFolder);

			String[] packages = GetWinPePackages();

			for (Int32 packageIndex = 0;
				packageIndex < packages.Length;
				packageIndex++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				String package = packages[packageIndex];

				Double packageProgress = 15 + ((packageIndex + 1D) / packages.Length * 35D);

				OnPhaseProgress(DiskBuildPhase.FreshWinPeBuild,	$"Adding package {packageIndex + 1} of {packages.Length}: {package}", packageProgress);

				String packagePath = Path.Combine(environment.OptionalComponentsFolder,	package);

				wimSession.AddPackage(packagePath);
			}

			String packagedWinPeClientFolder = Path.Combine(
				AppContext.BaseDirectory,
				"WinPEClient");

			String destinationWinPeClientFolder = Path.Combine(
				mountFolder,
				"WinPEClient");
			OnPhaseProgress(DiskBuildPhase.FreshWinPeBuild,	"Copying WinPE client application.", 55);

			String winPeClientSourceFolder =
				GetWinPeClientSourceFolder();

			FSManager.CopyDirectory(
				winPeClientSourceFolder,
				destinationWinPeClientFolder);

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

			if (_driverArchivePaths.Count > 0)
			{
				OnPhaseProgress(DiskBuildPhase.FreshWinPeBuild, "Adding selected WinPE drivers.", 65);
				wimSession.AddDriver(
					driverFolder,
					true,
					false);
			}
			else
			{
				OnPhaseProgress(DiskBuildPhase.FreshWinPeBuild, "No optional WinPE drivers selected.", 65);
			}

			cancellationToken.ThrowIfCancellationRequested();

			OnPhaseProgress(DiskBuildPhase.FreshWinPeBuild,	"Committing and dismounting Boot.wim.",	75);

			await wimSession.UnmountAsync(
				commit: true,
				cancellationToken);

			OnPhaseProgress(DiskBuildPhase.FreshWinPeBuild,	"Creating WinPE media cache.", 90);

			await cacheManager.CreateAsync(
				mediaFolder,
				expectedManifest,
				cancellationToken);

			OnPhaseProgress(DiskBuildPhase.FreshWinPeBuild,	"WinPE environment is ready.", 100);

			operationStopwatch.Stop();

			AppLog.Information(
				$"Fresh WinPE media operation completed in " +
				$"{operationStopwatch.Elapsed.TotalSeconds:F1} seconds.");

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
		private async Task<String> CalculateDriverPackageHashAsync()
		{
			return await CalculateFilesHashAsync(_driverArchivePaths);
		}

		internal static void ExtractDriverArchives(
			IReadOnlyList<String> driverArchivePaths,
			String destinationDirectory)
		{
			ArgumentNullException.ThrowIfNull(driverArchivePaths);
			ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

			for (Int32 archiveIndex = 0;
				archiveIndex < driverArchivePaths.Count;
				archiveIndex++)
			{
				String archiveFolder = Path.Combine(
					destinationDirectory,
					$"package-{archiveIndex + 1}");
				Directory.CreateDirectory(archiveFolder);
				ZipFile.ExtractToDirectory(
					driverArchivePaths[archiveIndex],
					archiveFolder);
			}
		}

		private static async Task<String> CalculatePackageConfigurationHashAsync()
		{
			String packageList =
				String.Join(
					Environment.NewLine,
					GetWinPePackages());

			return await CalculateStringHashAsync(packageList);
		}

		private static async Task<string> CalculateStringHashAsync(string packageList)
		{
			return packageList is null ? "" : await Task.Run(() =>
			{
				Byte[] bytes = Encoding.UTF8.GetBytes(packageList);
				Byte[] hash = SHA256.HashData(bytes);
				return Convert.ToHexString(hash);
			});
		}

		private async Task<WinPeCacheManifest>BuildWinPeCacheManifestAsync(WinPeEnvironment environment)
		{
			String applicationVersion =
				Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "";

			String winPeClientHash = await CalculateDirectoryHashAsync(GetWinPeClientSourceFolder());
			String driverPackagesHash = await CalculateDriverPackageHashAsync();
			String packageConfigurationHash = await CalculatePackageConfigurationHashAsync();
			return new WinPeCacheManifest
			{
				ApplicationVersion = applicationVersion,
				AdkVersion = environment.Version,
				Architecture = environment.Architecture,
				WinPeClientHash = winPeClientHash,
				DriverPackagesHash = driverPackagesHash,
				PackageConfigurationHash = packageConfigurationHash
			};
		}

		private static String[] GetWinPePackages()
		{
			return new String[]
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
		}

		private static String GetWinPeClientSourceFolder()
		{
			String packagedFolder =
				Path.Combine(
					AppContext.BaseDirectory,
					"WinPEClient");

			if (Directory.Exists(packagedFolder))
			{
				return packagedFolder;
			}

			String developmentFolder =
				Environment.GetEnvironmentVariable(
					"OSIMAGEDEPLOY_WINPECLIENT_PATH") ?? "";

			if (!String.IsNullOrWhiteSpace(developmentFolder) &&
				Directory.Exists(developmentFolder))
			{
				return developmentFolder;
			}

			throw new DirectoryNotFoundException(
				"The WinPEClient source folder could not be found. " +
				"Set OSIMAGEDEPLOY_WINPECLIENT_PATH for development builds.");
		}

		private static async Task<String> CalculateDirectoryHashAsync(
			String directoryPath)
		{
			if (!Directory.Exists(directoryPath))
			{
				throw new DirectoryNotFoundException(
					$"Directory not found: {directoryPath}");
			}

			String[] files =
				Directory.GetFiles(
					directoryPath,
					"*",
					SearchOption.AllDirectories);

			Array.Sort(
				files,
				StringComparer.OrdinalIgnoreCase);

			using SHA256 sha256 = SHA256.Create();

			foreach (String filePath in files)
			{
				String relativePath =
					Path.GetRelativePath(
						directoryPath,
						filePath);

				Byte[] relativePathBytes =
					Encoding.UTF8.GetBytes(
						relativePath.ToUpperInvariant());

				sha256.TransformBlock(
					relativePathBytes,
					0,
					relativePathBytes.Length,
					null,
					0);

				await using FileStream fileStream =
					new FileStream(
						filePath,
						FileMode.Open,
						FileAccess.Read,
						FileShare.Read,
						bufferSize: 1024 * 1024,
						useAsync: true);

				Byte[] buffer = new Byte[1024 * 1024];
				Int32 bytesRead;

				while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
				{
					sha256.TransformBlock(
						buffer,
						0,
						bytesRead,
						null,
						0);
				}
			}

			sha256.TransformFinalBlock(
				Array.Empty<Byte>(),
				0,
				0);

			return Convert.ToHexString(
				sha256.Hash);
		}

		private static async Task<String> CalculateFilesHashAsync(
			IEnumerable<String> filePaths)
		{
			String[] files =
				filePaths
					.OrderBy(
						path => path,
						StringComparer.OrdinalIgnoreCase)
					.ToArray();

			using SHA256 sha256 = SHA256.Create();

			foreach (String filePath in files)
			{
				if (!File.Exists(filePath))
				{
					throw new FileNotFoundException(
						"Required cache input file was not found.",
						filePath);
				}

				Byte[] fileNameBytes =
					Encoding.UTF8.GetBytes(
						Path.GetFileName(filePath).ToUpperInvariant());

				sha256.TransformBlock(
					fileNameBytes,
					0,
					fileNameBytes.Length,
					null,
					0);

				await using FileStream fileStream =
					new FileStream(
						filePath,
						FileMode.Open,
						FileAccess.Read,
						FileShare.Read,
						bufferSize: 1024 * 1024,
						useAsync: true);

				Byte[] buffer = new Byte[1024 * 1024];
				Int32 bytesRead;

				while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
				{
					sha256.TransformBlock(
						buffer,
						0,
						bytesRead,
						null,
						0);
				}
			}

			sha256.TransformFinalBlock(
				Array.Empty<Byte>(),
				0,
				0);

			return Convert.ToHexString(
				sha256.Hash);
		}

		private static String CalculateStringCollectionHash(
			IEnumerable<String> values)
		{
			String combinedValue =
				String.Join(
					"\n",
					values.OrderBy(
						value => value,
						StringComparer.OrdinalIgnoreCase));

			Byte[] bytes =
				Encoding.UTF8.GetBytes(
					combinedValue);

			Byte[] hash =
				SHA256.HashData(bytes);

			return Convert.ToHexString(hash);
		}
		private void Service_ProgressChanged(
			object sender,
			WimOperationProgressEventArgs e)
		{
			Double dismProgress =
				Math.Clamp(
					e.Percentage,
					0,
					100);

			OnPhaseProgress(
				DiskBuildPhase.FreshWinPeBuild,
				$"{e.OperationName}: {e.Current} / {e.Total}",
				10 + dismProgress * 0.65);
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
