using OSImageDeploy.Contracts;
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
						"Download the current Dell Command | Deploy WinPE driver pack from Dell. " +
						"If it is supplied as a Dell Update Package executable, extract it with " +
						"the documented /s /e=<folder> options, then prepare the extracted folder."
				},
				new SourceDefinition
				{
					PackageId = "hp-winpe",
					DisplayName = "HP WinPE driver pack",
					Manufacturer = "HP",
					SourcePageUrl =
						"https://ftp.ext.hp.com/pub/caps-softpaq/cmit/HP_WinPE_DriverPack.html",
					PreparationInstructions =
						"Download the current HP Client WinPE SoftPaq from HP and extract its contents. " +
						"The SoftPaq UI can extract it, or supported packages can use " +
						"spxxxxx.exe /s /e /f <folder>. Prepare the extracted folder, not the executable."
				}
			];

		private readonly Boolean _secureDefaultDirectory;

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
		}
	}
}
