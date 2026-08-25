using OSImageDeploy.Contracts;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OSImageDeploy.Platform.Windows
{
	public sealed class WindowsWinPeDriverPackageStore
	{
		private const String ArchiveFileName = "drivers.zip";
		private const String ManifestFileName = "package.json";
		private const Int32 HpSoftPaqMissingLaunchTargetExitCode = 1168;
		private const Int64 MaximumSourceFileSizeBytes =
			2L * 1024L * 1024L * 1024L;

		private static readonly Regex _packageIdPattern = new Regex(
			"^[a-z0-9][a-z0-9.-]{0,63}$",
			RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

		private static readonly JsonSerializerOptions _jsonOptions =
			new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};

		private static readonly IReadOnlyList<SourceDefinition>
			_builtinSources =
			[
				new SourceDefinition
				{
					PackageId = "dell-winpe",
					DisplayName = "Dell WinPE driver pack",
					Manufacturer = "Dell",
					SourcePageUrl =
						"https://www.dell.com/support/kbdoc/en-us/000107478/dell-command-deploy-winpe-driver-packs",
					PreparationInstructions =
						"Download the current Dell WinPE CAB file. OS Image Deploy can validate, " +
						"extract and prepare the CAB automatically.",
					PreparationFileExtension = ".cab"
				},
				new SourceDefinition
				{
					PackageId = "hp-winpe",
					DisplayName = "HP WinPE driver pack",
					Manufacturer = "HP",
					SourcePageUrl =
						"https://ftp.ext.hp.com/pub/caps-softpaq/cmit/HP_WinPE_DriverPack.html",
					PreparationInstructions =
						"Download the current HP Client WinPE SoftPaq executable. OS Image Deploy " +
						"verifies the HP signature before running its supported extractor and " +
						"preparing the package.",
					PreparationFileExtension = ".exe"
				}
			];

		private readonly Boolean _secureDefaultDirectory;
		private readonly SemaphoreSlim _preparationLock = new(1, 1);

		public WindowsWinPeDriverPackageStore(String? rootDirectory = null)
		{
			_secureDefaultDirectory = String.IsNullOrWhiteSpace(rootDirectory);
			RootDirectory = _secureDefaultDirectory
				? Path.Combine(
					Environment.GetFolderPath(
						Environment.SpecialFolder.CommonApplicationData),
					"OSImageDeploy",
					"DriverPackages")
				: Path.GetFullPath(rootDirectory!);

			if (_secureDefaultDirectory)
			{
				SecureRootDirectory();
			}
		}

		public String RootDirectory { get; }

		public IReadOnlyList<WinPeDriverPackageDescriptor> GetPackages()
		{
			Dictionary<String, ResolvedWinPeDriverPackage> installed =
				ReadInstalledPackages();
			List<WinPeDriverPackageDescriptor> packages = new();

			foreach (SourceDefinition source in _builtinSources)
			{
				if (installed.Remove(source.PackageId, out ResolvedWinPeDriverPackage? package))
				{
					packages.Add(
						WithSourceGuidance(package.Descriptor, source));
				}
				else
				{
					packages.Add(
						new WinPeDriverPackageDescriptor
						{
							PackageId = source.PackageId,
							DisplayName = source.DisplayName,
							Manufacturer = source.Manufacturer,
							SourcePageUrl = source.SourcePageUrl,
							PreparationInstructions =
								source.PreparationInstructions,
							PreparationFileExtension =
								source.PreparationFileExtension,
							CanPrepareAutomatically = true,
							IsAvailable = false,
							StatusMessage =
								"The package has not been prepared in the service package store."
						});
				}
			}

			packages.AddRange(
				installed.Values
					.Select(package => package.Descriptor)
					.OrderBy(package => package.Manufacturer)
					.ThenBy(package => package.DisplayName));

			return packages;
		}

		public async Task<WinPeDriverPackageDescriptor>
			PrepareBuiltInPackageAsync(
				String packageId,
				String sourceFilePath,
				String sourceVersion,
				Boolean replaceExistingConfirmed,
				CancellationToken cancellationToken = default)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
			ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
			String normalizedSourceVersion = sourceVersion ?? "";

			SourceDefinition source = _builtinSources.SingleOrDefault(
				candidate => candidate.PackageId.Equals(
					packageId,
					StringComparison.OrdinalIgnoreCase)) ??
				throw new ArgumentException(
					$"Package '{packageId}' cannot be prepared automatically.",
					nameof(packageId));

			if (!Path.IsPathFullyQualified(sourceFilePath))
			{
				throw new ArgumentException(
					"The manufacturer download path must be absolute.",
					nameof(sourceFilePath));
			}

			String resolvedSourcePath = Path.GetFullPath(sourceFilePath);

			if (new Uri(resolvedSourcePath).IsUnc)
			{
				throw new ArgumentException(
					"The manufacturer download must be on a local drive.",
					nameof(sourceFilePath));
			}

			if (normalizedSourceVersion.Length > 128)
			{
				throw new ArgumentException(
					"The package version cannot exceed 128 characters.",
					nameof(sourceVersion));
			}

			FileInfo sourceFile = new(resolvedSourcePath);

			if (!sourceFile.Exists)
			{
				throw new FileNotFoundException(
					"The selected manufacturer download does not exist.",
					resolvedSourcePath);
			}

			if (!sourceFile.Extension.Equals(
				source.PreparationFileExtension,
				StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException(
					$"{source.DisplayName} requires a " +
					$"{source.PreparationFileExtension.ToUpperInvariant()} file.");
			}

			if (source.PackageId == "dell-winpe" &&
				!sourceFile.Name.Contains(
					"WinPE",
					StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException(
					"The selected Dell CAB filename does not identify it as a WinPE package.");
			}

			if ((sourceFile.Attributes & FileAttributes.ReparsePoint) != 0)
			{
				throw new InvalidDataException(
					"The selected manufacturer download cannot be a reparse point.");
			}

			if (sourceFile.Length <= 0 ||
				sourceFile.Length > MaximumSourceFileSizeBytes)
			{
				throw new InvalidDataException(
					"The selected manufacturer download has an unsupported size.");
			}

			if (source.PreparationFileExtension == ".exe")
			{
				// Reject an unrelated or untrusted SoftPaq before copying a large file.
				// The staged copy is checked again immediately before it is executed.
				await VerifyHpSourceAsync(
					resolvedSourcePath,
					cancellationToken);
			}

			await _preparationLock.WaitAsync(cancellationToken);

			try
			{
				String packageDirectory = Path.Combine(
					RootDirectory,
					source.PackageId);

				if (Directory.Exists(packageDirectory) &&
					!replaceExistingConfirmed)
				{
					throw new InvalidOperationException(
						$"Package '{source.DisplayName}' is already prepared. " +
						"Explicit replacement confirmation is required.");
				}

				String stagingRoot = GetStagingRoot();
				String operationDirectory = Path.Combine(
					stagingRoot,
					$"{source.PackageId}-{Guid.NewGuid():N}");
				String sourceCopyPath = Path.Combine(
					operationDirectory,
					"source" + source.PreparationFileExtension);
				String extractionDirectory = Path.Combine(
					operationDirectory,
					"extracted");

				Directory.CreateDirectory(extractionDirectory);

				try
				{
					await CopySourceFileAsync(
						resolvedSourcePath,
						sourceCopyPath,
						cancellationToken);

					if (source.PreparationFileExtension == ".cab")
					{
						await ExtractDellCabAsync(
							sourceCopyPath,
							extractionDirectory,
							cancellationToken);
					}
					else
					{
						await ExtractHpSoftPaqAsync(
							sourceCopyPath,
							extractionDirectory,
							cancellationToken);
					}

					ValidateExtractedDirectory(extractionDirectory);
					String resolvedVersion = ResolveSourceVersion(
						normalizedSourceVersion,
						sourceFile,
						source.PreparationFileExtension);

					InstallPreparedDirectory(
						source,
						extractionDirectory,
						resolvedVersion,
						replaceExistingConfirmed);
				}
				finally
				{
					if (Directory.Exists(operationDirectory))
					{
						Directory.Delete(operationDirectory, recursive: true);
					}
				}

				return GetPackages().Single(package =>
					package.PackageId.Equals(
						source.PackageId,
						StringComparison.OrdinalIgnoreCase));
			}
			finally
			{
				_preparationLock.Release();
			}
		}

		public IReadOnlyList<ResolvedWinPeDriverPackage> ResolveSelection(
			IEnumerable<String> packageIds)
		{
			ArgumentNullException.ThrowIfNull(packageIds);

			List<String> selectedIds = packageIds
				.Select(packageId => packageId?.Trim() ?? "")
				.ToList();

			if (selectedIds.Any(String.IsNullOrWhiteSpace))
			{
				throw new ArgumentException(
					"A selected WinPE driver package ID is empty.",
					nameof(packageIds));
			}

			String? duplicate = selectedIds
				.GroupBy(packageId => packageId, StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault(group => group.Count() > 1)?
				.Key;

			if (duplicate != null)
			{
				throw new ArgumentException(
					$"WinPE driver package '{duplicate}' was selected more than once.",
					nameof(packageIds));
			}

			Dictionary<String, ResolvedWinPeDriverPackage> installed =
				ReadInstalledPackages();
			List<ResolvedWinPeDriverPackage> resolved = new();

			foreach (String packageId in selectedIds)
			{
				if (!installed.TryGetValue(packageId, out ResolvedWinPeDriverPackage? package) ||
					!package.Descriptor.IsAvailable)
				{
					throw new InvalidDataException(
						$"WinPE driver package '{packageId}' is not available and valid.");
				}

				resolved.Add(package);
			}

			return resolved;
		}

		private static async Task CopySourceFileAsync(
			String sourcePath,
			String destinationPath,
			CancellationToken cancellationToken)
		{
			await using FileStream source = new(
				sourcePath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read,
				bufferSize: 1024 * 1024,
				FileOptions.Asynchronous | FileOptions.SequentialScan);
			await using FileStream destination = new(
				destinationPath,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None,
				bufferSize: 1024 * 1024,
				FileOptions.Asynchronous | FileOptions.SequentialScan);

			await source.CopyToAsync(destination, cancellationToken);
		}

		private static async Task ExtractDellCabAsync(
			String sourcePath,
			String destinationDirectory,
			CancellationToken cancellationToken)
		{
			String expandPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.System),
				"expand.exe");
			ProcessResult result = await RunProcessAsync(
				expandPath,
				["-R", "-F:*", sourcePath, destinationDirectory],
				cancellationToken);

			if (result.ExitCode != 0)
			{
				throw new InvalidDataException(
					"The Dell CAB could not be extracted. " +
					GetProcessError(result));
			}
		}

		private static async Task ExtractHpSoftPaqAsync(
			String sourcePath,
			String destinationDirectory,
			CancellationToken cancellationToken)
		{
			await VerifyHpSourceAsync(
				sourcePath,
				cancellationToken);

			ProcessResult result = await RunProcessAsync(
				sourcePath,
				["/s", "/e", "/f", destinationDirectory],
				cancellationToken);

			if (!IsAcceptedHpSoftPaqExitCode(result.ExitCode))
			{
				throw new InvalidDataException(
					"The HP SoftPaq extractor did not complete successfully. " +
					GetProcessError(result));
			}
		}

		internal static Boolean IsAcceptedHpSoftPaqExitCode(Int32 exitCode)
		{
			// Current HP WinPE wrappers can extract the complete payload, then return
			// ERROR_NOT_FOUND because unpack-only mode has no post-extraction target.
			// The caller still validates the extracted tree before installing it.
			return exitCode == 0 ||
				exitCode == HpSoftPaqMissingLaunchTargetExitCode;
		}

		private static async Task VerifyHpSourceAsync(
			String sourcePath,
			CancellationToken cancellationToken)
		{
			await VerifyHpAuthenticodeSignatureAsync(
				sourcePath,
				cancellationToken);
			ValidateHpProductMetadata(sourcePath);
		}

		private static async Task VerifyHpAuthenticodeSignatureAsync(
			String sourcePath,
			CancellationToken cancellationToken)
		{
			const String signatureCommand =
				"$signature = Get-AuthenticodeSignature -LiteralPath " +
				"$env:OSIMAGEDEPLOY_HP_SOURCE; " +
				"if ($signature.Status -ne 'Valid' -or " +
				"$null -eq $signature.SignerCertificate) { exit 10 }; " +
				"[Console]::Out.Write($signature.SignerCertificate.Subject)";

			ProcessStartInfo startInfo = CreateProcessStartInfo(
				"powershell.exe",
				[
					"-NoLogo",
					"-NoProfile",
					"-NonInteractive",
					"-Command",
					signatureCommand
				]);
			startInfo.Environment["OSIMAGEDEPLOY_HP_SOURCE"] = sourcePath;
			ProcessResult result = await RunProcessAsync(
				startInfo,
				cancellationToken);
			String signerSubject = result.StandardOutput.Trim();

			if (result.ExitCode != 0 ||
				!IsHpSignerSubject(signerSubject))
			{
				throw new InvalidDataException(
					"The selected HP executable does not have a valid, trusted HP Authenticode signature.");
			}
		}

		private static Boolean IsHpSignerSubject(String subject)
		{
			return subject.Contains(
				"O=HP Inc.",
				StringComparison.OrdinalIgnoreCase) ||
				subject.Contains(
					"O=\"HP Inc.\"",
					StringComparison.OrdinalIgnoreCase) ||
				subject.Contains(
					"O=Hewlett-Packard",
					StringComparison.OrdinalIgnoreCase) ||
				subject.Contains(
					"O=\"Hewlett-Packard",
					StringComparison.OrdinalIgnoreCase) ||
				subject.Contains(
					"O=HP Development Company",
					StringComparison.OrdinalIgnoreCase) ||
				subject.Contains(
					"O=\"HP Development Company",
					StringComparison.OrdinalIgnoreCase);
		}

		private static void ValidateHpProductMetadata(String sourcePath)
		{
			FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(sourcePath);
			String productIdentity = String.Join(
				" ",
				versionInfo.ProductName,
				versionInfo.FileDescription,
				versionInfo.Comments);

			if (!productIdentity.Contains(
					"WinPE",
					StringComparison.OrdinalIgnoreCase) &&
				!productIdentity.Contains(
					"Windows PE",
					StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException(
					"The signed HP product metadata does not identify this executable as a WinPE driver pack.");
			}
		}

		private static async Task<ProcessResult> RunProcessAsync(
			String fileName,
			IEnumerable<String> arguments,
			CancellationToken cancellationToken)
		{
			return await RunProcessAsync(
				CreateProcessStartInfo(fileName, arguments),
				cancellationToken);
		}

		private static ProcessStartInfo CreateProcessStartInfo(
			String fileName,
			IEnumerable<String> arguments)
		{
			ProcessStartInfo startInfo = new(fileName)
			{
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};

			foreach (String argument in arguments)
			{
				startInfo.ArgumentList.Add(argument);
			}

			return startInfo;
		}

		private static async Task<ProcessResult> RunProcessAsync(
			ProcessStartInfo startInfo,
			CancellationToken cancellationToken)
		{
			using Process process = new()
			{
				StartInfo = startInfo
			};

			if (!process.Start())
			{
				throw new InvalidOperationException(
					$"Unable to start {Path.GetFileName(startInfo.FileName)}.");
			}

			Task<String> outputTask = process.StandardOutput.ReadToEndAsync();
			Task<String> errorTask = process.StandardError.ReadToEndAsync();

			try
			{
				await process.WaitForExitAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
				}

				throw;
			}

			return new ProcessResult
			{
				ExitCode = process.ExitCode,
				StandardOutput = await outputTask,
				StandardError = await errorTask
			};
		}

		private static String GetProcessError(ProcessResult result)
		{
			String error = String.IsNullOrWhiteSpace(result.StandardError)
				? result.StandardOutput
				: result.StandardError;

			return String.IsNullOrWhiteSpace(error)
				? $"Exit code: {result.ExitCode}."
				: error.Trim();
		}

		private static void ValidateExtractedDirectory(String directoryPath)
		{
			Int32 fileCount = 0;
			Int32 driverCount = 0;
			Int64 totalSize = 0;

			foreach (String entryPath in Directory.EnumerateFileSystemEntries(
				directoryPath,
				"*",
				SearchOption.AllDirectories))
			{
				FileAttributes attributes = File.GetAttributes(entryPath);

				if ((attributes & FileAttributes.ReparsePoint) != 0)
				{
					throw new InvalidDataException(
						"The extracted driver package contains a reparse point.");
				}

				if ((attributes & FileAttributes.Directory) != 0)
				{
					continue;
				}

				fileCount++;
				totalSize += new FileInfo(entryPath).Length;

				if (entryPath.EndsWith(
					".inf",
					StringComparison.OrdinalIgnoreCase))
				{
					driverCount++;
				}
			}

			if (fileCount == 0 || driverCount == 0)
			{
				throw new InvalidDataException(
					"The manufacturer package did not extract any WinPE INF drivers.");
			}

			if (fileCount > 100000 || totalSize > 8L * 1024L * 1024L * 1024L)
			{
				throw new InvalidDataException(
					"The extracted manufacturer package exceeds the supported limits.");
			}
		}

		private void InstallPreparedDirectory(
			SourceDefinition source,
			String extractedDirectory,
			String sourceVersion,
			Boolean replaceExistingConfirmed)
		{
			String stagingRoot = GetStagingRoot();
			String stagedPackageDirectory = Path.Combine(
				stagingRoot,
				$"package-{source.PackageId}-{Guid.NewGuid():N}");
			String packageDirectory = Path.Combine(
				RootDirectory,
				source.PackageId);
			String backupDirectory = Path.Combine(
				stagingRoot,
				$"backup-{source.PackageId}-{Guid.NewGuid():N}");

			Directory.CreateDirectory(stagedPackageDirectory);

			try
			{
				String archivePath = Path.Combine(
					stagedPackageDirectory,
					ArchiveFileName);
				ZipFile.CreateFromDirectory(
					extractedDirectory,
					archivePath,
					CompressionLevel.Optimal,
					includeBaseDirectory: false);
				ValidateArchive(archivePath);

				WinPeDriverPackageManifest manifest = new()
				{
					PackageId = source.PackageId,
					DisplayName = source.DisplayName,
					Manufacturer = source.Manufacturer,
					SourceVersion = sourceVersion,
					SourcePageUrl = source.SourcePageUrl,
					PreparedUtc = DateTimeOffset.UtcNow
				};
				File.WriteAllText(
					Path.Combine(
						stagedPackageDirectory,
						ManifestFileName),
					JsonSerializer.Serialize(
						manifest,
						new JsonSerializerOptions { WriteIndented = true }));

				if (Directory.Exists(packageDirectory))
				{
					if (!replaceExistingConfirmed)
					{
						throw new InvalidOperationException(
							"Explicit replacement confirmation is required.");
					}

					Directory.Move(packageDirectory, backupDirectory);
				}

				try
				{
					Directory.Move(
						stagedPackageDirectory,
						packageDirectory);

					TryDeleteDirectory(backupDirectory);
				}
				catch
				{
					if (!Directory.Exists(packageDirectory) &&
						Directory.Exists(backupDirectory))
					{
						Directory.Move(backupDirectory, packageDirectory);
					}

					throw;
				}
			}
			finally
			{
				if (Directory.Exists(stagedPackageDirectory))
				{
					Directory.Delete(stagedPackageDirectory, recursive: true);
				}
			}
		}

		private static void TryDeleteDirectory(String directoryPath)
		{
			try
			{
				if (Directory.Exists(directoryPath))
				{
					Directory.Delete(directoryPath, recursive: true);
				}
			}
			catch (IOException)
			{
				// A stale ignored staging backup is safer than reporting a
				// successfully installed package as failed.
			}
			catch (UnauthorizedAccessException)
			{
				// The ignored protected staging backup can be removed later.
			}
		}

		private String GetStagingRoot()
		{
			String stagingRoot = Path.Combine(RootDirectory, ".staging");
			Directory.CreateDirectory(stagingRoot);
			return stagingRoot;
		}

		private static String ResolveSourceVersion(
			String requestedVersion,
			FileInfo sourceFile,
			String sourceExtension)
		{
			if (!String.IsNullOrWhiteSpace(requestedVersion))
			{
				return requestedVersion.Trim();
			}

			if (sourceExtension == ".exe")
			{
				FileVersionInfo versionInfo =
					FileVersionInfo.GetVersionInfo(sourceFile.FullName);
				String? productVersion = versionInfo.ProductVersion ??
					versionInfo.FileVersion;

				if (!String.IsNullOrWhiteSpace(productVersion))
				{
					return productVersion.Trim();
				}
			}

			return Path.GetFileNameWithoutExtension(sourceFile.Name);
		}

		private Dictionary<String, ResolvedWinPeDriverPackage>
			ReadInstalledPackages()
		{
			Dictionary<String, ResolvedWinPeDriverPackage> packages =
				new(StringComparer.OrdinalIgnoreCase);

			if (!Directory.Exists(RootDirectory))
			{
				return packages;
			}

			foreach (String packageDirectory in Directory.EnumerateDirectories(
				RootDirectory,
				"*",
				SearchOption.TopDirectoryOnly))
			{
				String directoryName = Path.GetFileName(packageDirectory);

				if (directoryName.StartsWith('.'))
				{
					continue;
				}

				try
				{
					ResolvedWinPeDriverPackage package =
						ReadPackage(packageDirectory, directoryName);

					if (!packages.TryAdd(
						package.Descriptor.PackageId,
						package))
					{
						throw new InvalidDataException(
							$"Duplicate WinPE driver package ID '{package.Descriptor.PackageId}'.");
					}
				}
				catch (Exception exception) when (
					exception is InvalidDataException or
					JsonException or
					IOException or
					UnauthorizedAccessException)
				{
					packages[directoryName] =
						new ResolvedWinPeDriverPackage
						{
							ArchivePath = Path.Combine(
								packageDirectory,
								ArchiveFileName),
							Descriptor =
								new WinPeDriverPackageDescriptor
								{
									PackageId = directoryName,
									DisplayName = directoryName,
									Manufacturer = "Unknown",
									IsAvailable = false,
									StatusMessage = exception.Message
								}
						};
				}
			}

			return packages;
		}

		private static ResolvedWinPeDriverPackage ReadPackage(
			String packageDirectory,
			String directoryName)
		{
			if (!_packageIdPattern.IsMatch(directoryName))
			{
				throw new InvalidDataException(
					$"Package directory name '{directoryName}' is not a valid package ID.");
			}

			String manifestPath = Path.Combine(
				packageDirectory,
				ManifestFileName);
			String archivePath = Path.Combine(
				packageDirectory,
				ArchiveFileName);

			if (!File.Exists(manifestPath) || !File.Exists(archivePath))
			{
				throw new InvalidDataException(
					"The package must contain package.json and drivers.zip.");
			}

			WinPeDriverPackageManifest? manifest =
				JsonSerializer.Deserialize<WinPeDriverPackageManifest>(
					File.ReadAllText(manifestPath),
					_jsonOptions);

			if (manifest == null || manifest.SchemaVersion != 1)
			{
				throw new InvalidDataException(
					"The package manifest schema is missing or unsupported.");
			}

			ValidateManifest(manifest, directoryName);
			Int32 driverCount = ValidateArchive(archivePath);
			FileInfo archive = new FileInfo(archivePath);
			using FileStream archiveStream = File.OpenRead(archivePath);
			String archiveHash = Convert.ToHexString(
				SHA256.HashData(archiveStream));

			return new ResolvedWinPeDriverPackage
			{
				ArchivePath = archivePath,
				Descriptor =
					new WinPeDriverPackageDescriptor
					{
						PackageId = manifest.PackageId,
						DisplayName = manifest.DisplayName,
						Manufacturer = manifest.Manufacturer,
						SourceVersion = manifest.SourceVersion,
						SourcePageUrl = manifest.SourcePageUrl,
						IsAvailable = true,
						DriverCount = driverCount,
						ArchiveSizeBytes = archive.Length,
						ArchiveSha256 = archiveHash,
						StatusMessage =
							$"Available: {driverCount} INF files."
					}
			};
		}

		private static void ValidateManifest(
			WinPeDriverPackageManifest manifest,
			String directoryName)
		{
			if (!_packageIdPattern.IsMatch(manifest.PackageId) ||
				!String.Equals(
					manifest.PackageId,
					directoryName,
					StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException(
					"The package manifest ID is invalid or does not match its directory.");
			}

			if (String.IsNullOrWhiteSpace(manifest.DisplayName) ||
				String.IsNullOrWhiteSpace(manifest.Manufacturer))
			{
				throw new InvalidDataException(
					"The package manifest requires a display name and manufacturer.");
			}

			if (!String.IsNullOrWhiteSpace(manifest.SourcePageUrl) &&
				(!Uri.TryCreate(
					manifest.SourcePageUrl,
					UriKind.Absolute,
					out Uri? sourceUri) ||
				sourceUri.Scheme != Uri.UriSchemeHttps))
			{
				throw new InvalidDataException(
					"The package source page must be an absolute HTTPS URL.");
			}
		}

		private static Int32 ValidateArchive(String archivePath)
		{
			using ZipArchive archive = ZipFile.OpenRead(archivePath);
			Int32 driverCount = 0;

			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				String normalizedName = entry.FullName.Replace('\\', '/');
				String[] segments = normalizedName.Split(
					'/',
					StringSplitOptions.RemoveEmptyEntries);

				if (normalizedName.StartsWith('/') ||
					Path.IsPathRooted(entry.FullName) ||
					segments.Any(segment => segment == ".."))
				{
					throw new InvalidDataException(
						$"Driver archive contains an unsafe path: {entry.FullName}");
				}

				if (String.Equals(
					normalizedName,
					"CI-PLACEHOLDER.txt",
					StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException(
						"A CI placeholder is not a deployable driver package.");
				}

				if (normalizedName.EndsWith(
					".inf",
					StringComparison.OrdinalIgnoreCase))
				{
					driverCount++;
				}
			}

			if (driverCount == 0)
			{
				throw new InvalidDataException(
					"The driver archive does not contain any INF files.");
			}

			return driverCount;
		}

		private static WinPeDriverPackageDescriptor WithSourceGuidance(
			WinPeDriverPackageDescriptor package,
			SourceDefinition source)
		{
			return new WinPeDriverPackageDescriptor
			{
				PackageId = package.PackageId,
				DisplayName = package.DisplayName,
				Manufacturer = package.Manufacturer,
				SourceVersion = package.SourceVersion,
				SourcePageUrl = String.IsNullOrWhiteSpace(package.SourcePageUrl)
					? source.SourcePageUrl
					: package.SourcePageUrl,
				PreparationInstructions = source.PreparationInstructions,
				PreparationFileExtension = source.PreparationFileExtension,
				CanPrepareAutomatically = true,
				IsAvailable = package.IsAvailable,
				DriverCount = package.DriverCount,
				ArchiveSizeBytes = package.ArchiveSizeBytes,
				ArchiveSha256 = package.ArchiveSha256,
				StatusMessage = package.StatusMessage
			};
		}

		private void SecureRootDirectory()
		{
			DirectoryInfo directory = Directory.CreateDirectory(RootDirectory);
			SecurityIdentifier system = new SecurityIdentifier(
				WellKnownSidType.LocalSystemSid,
				domainSid: null);
			SecurityIdentifier administrators = new SecurityIdentifier(
				WellKnownSidType.BuiltinAdministratorsSid,
				domainSid: null);
			InheritanceFlags inheritance =
				InheritanceFlags.ContainerInherit |
				InheritanceFlags.ObjectInherit;
			DirectorySecurity security = new DirectorySecurity();

			security.SetAccessRuleProtection(
				isProtected: true,
				preserveInheritance: false);
			security.SetOwner(system);
			security.AddAccessRule(
				new FileSystemAccessRule(
					system,
					FileSystemRights.FullControl,
					inheritance,
					PropagationFlags.None,
					AccessControlType.Allow));
			security.AddAccessRule(
				new FileSystemAccessRule(
					administrators,
					FileSystemRights.FullControl,
					inheritance,
					PropagationFlags.None,
					AccessControlType.Allow));

			directory.SetAccessControl(security);
		}

		private sealed class SourceDefinition
		{
			public required String PackageId { get; init; }

			public required String DisplayName { get; init; }

			public required String Manufacturer { get; init; }

			public required String SourcePageUrl { get; init; }

			public required String PreparationInstructions { get; init; }

			public required String PreparationFileExtension { get; init; }
		}

		private sealed class ProcessResult
		{
			public Int32 ExitCode { get; init; }

			public required String StandardOutput { get; init; }

			public required String StandardError { get; init; }
		}
	}
}
