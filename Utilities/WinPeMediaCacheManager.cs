#nullable disable

using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Utilities
{
	public sealed class WinPeMediaCacheManager
	{
		private const Int32 CurrentSchemaVersion = 1;

		private static readonly JsonSerializerOptions _jsonOptions =
			new JsonSerializerOptions
			{
				WriteIndented = true
			};

		public WinPeMediaCacheManager()
			: this(null)
		{
		}

		public WinPeMediaCacheManager(String cacheDirectory)
		{
			if (String.IsNullOrWhiteSpace(cacheDirectory))
			{
				String programDataFolder =
					Environment.GetFolderPath(
						Environment.SpecialFolder.CommonApplicationData);

				cacheDirectory = Path.Combine(
					programDataFolder,
					"OSImageDeploy",
					"Cache",
					"WinPE");
			}

			CacheDirectory = cacheDirectory;
			ArchivePath = Path.Combine(CacheDirectory, "WinPEMedia.zip");
			ManifestPath = Path.Combine(CacheDirectory, "WinPEMedia.json");
		}

		public String CacheDirectory { get; }

		public String ArchivePath { get; }

		public String ManifestPath { get; }

		public Boolean CacheExists
		{
			get
			{
				return File.Exists(ArchivePath) &&
					File.Exists(ManifestPath);
			}
		}

		public async Task<Boolean> IsValidAsync(
			WinPeCacheManifest expectedManifest,
			CancellationToken cancellationToken = default)
		{
			if (expectedManifest == null)
			{
				throw new ArgumentNullException(nameof(expectedManifest));
			}

			if (!CacheExists)
			{
				return false;
			}

			try
			{
				WinPeCacheManifest cachedManifest =
					await LoadManifestAsync(cancellationToken);

				if (cachedManifest == null)
				{
					return false;
				}

				if (!ManifestMatches(
					cachedManifest,
					expectedManifest))
				{
					return false;
				}

				String archiveHash = await Task.Run(
					() => CalculateFileHash(ArchivePath),
					cancellationToken);

				if (!String.Equals(
					archiveHash,
					cachedManifest.ArchiveHash,
					StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}

				return await Task.Run(
					() => ValidateArchiveContents(),
					cancellationToken);
			}
			catch
			{
				return false;
			}
		}

		public async Task CreateAsync(
			String mediaDirectory,
			WinPeCacheManifest manifest,
			CancellationToken cancellationToken = default)
		{
			if (String.IsNullOrWhiteSpace(mediaDirectory))
			{
				throw new ArgumentException(
					"A WinPE media directory must be supplied.",
					nameof(mediaDirectory));
			}

			if (!Directory.Exists(mediaDirectory))
			{
				throw new DirectoryNotFoundException(
					$"The WinPE media directory does not exist: {mediaDirectory}");
			}

			if (manifest == null)
			{
				throw new ArgumentNullException(nameof(manifest));
			}

			Directory.CreateDirectory(CacheDirectory);

			String temporaryArchivePath =
				ArchivePath + ".tmp";

			String temporaryManifestPath =
				ManifestPath + ".tmp";

			DeleteFileIfPresent(temporaryArchivePath);
			DeleteFileIfPresent(temporaryManifestPath);

			try
			{
				await Task.Run(
					() =>
					{
						cancellationToken.ThrowIfCancellationRequested();

						ZipFile.CreateFromDirectory(
							mediaDirectory,
							temporaryArchivePath,
							CompressionLevel.Fastest,
							includeBaseDirectory: false);

						cancellationToken.ThrowIfCancellationRequested();

						ValidateArchiveContents(temporaryArchivePath);
					},
					cancellationToken);

				manifest.SchemaVersion = CurrentSchemaVersion;
				manifest.CreatedUtc = DateTime.UtcNow;

				manifest.ArchiveHash = await Task.Run(
					() => CalculateFileHash(temporaryArchivePath),
					cancellationToken);

				String manifestJson =
					JsonSerializer.Serialize(
						manifest,
						_jsonOptions);

				await File.WriteAllTextAsync(
					temporaryManifestPath,
					manifestJson,
					cancellationToken);

				File.Move(
					temporaryArchivePath,
					ArchivePath,
					overwrite: true);

				File.Move(
					temporaryManifestPath,
					ManifestPath,
					overwrite: true);
			}
			catch
			{
				DeleteFileIfPresent(temporaryArchivePath);
				DeleteFileIfPresent(temporaryManifestPath);

				throw;
			}
		}

		public async Task ExtractAsync(
			String destinationDirectory,
			CancellationToken cancellationToken = default)
		{
			if (String.IsNullOrWhiteSpace(destinationDirectory))
			{
				throw new ArgumentException(
					"A destination directory must be supplied.",
					nameof(destinationDirectory));
			}

			if (!File.Exists(ArchivePath))
			{
				throw new FileNotFoundException(
					"The cached WinPE media archive does not exist.",
					ArchivePath);
			}

			await Task.Run(
				() =>
				{
					cancellationToken.ThrowIfCancellationRequested();

					ValidateArchiveContents();

					Directory.CreateDirectory(destinationDirectory);

					ZipFile.ExtractToDirectory(
						ArchivePath,
						destinationDirectory,
						overwriteFiles: true);
				},
				cancellationToken);
		}

		public async Task<WinPeCacheManifest> LoadManifestAsync(
			CancellationToken cancellationToken = default)
		{
			if (!File.Exists(ManifestPath))
			{
				return null;
			}

			String manifestJson =
				await File.ReadAllTextAsync(
					ManifestPath,
					cancellationToken);

			if (String.IsNullOrWhiteSpace(manifestJson))
			{
				return null;
			}

			return JsonSerializer.Deserialize<WinPeCacheManifest>(
				manifestJson,
				_jsonOptions);
		}

		public void Delete()
		{
			DeleteFileIfPresent(ArchivePath);
			DeleteFileIfPresent(ManifestPath);
			DeleteFileIfPresent(ArchivePath + ".tmp");
			DeleteFileIfPresent(ManifestPath + ".tmp");
		}

		private static Boolean ManifestMatches(
			WinPeCacheManifest cachedManifest,
			WinPeCacheManifest expectedManifest)
		{
			return
				cachedManifest.SchemaVersion == CurrentSchemaVersion &&

				String.Equals(
					cachedManifest.ApplicationVersion,
					expectedManifest.ApplicationVersion,
					StringComparison.OrdinalIgnoreCase) &&

				String.Equals(
					cachedManifest.AdkVersion,
					expectedManifest.AdkVersion,
					StringComparison.OrdinalIgnoreCase) &&

				String.Equals(
					cachedManifest.Architecture,
					expectedManifest.Architecture,
					StringComparison.OrdinalIgnoreCase) &&

				String.Equals(
					cachedManifest.WinPeClientHash,
					expectedManifest.WinPeClientHash,
					StringComparison.OrdinalIgnoreCase) &&

				String.Equals(
					cachedManifest.DriverPackagesHash,
					expectedManifest.DriverPackagesHash,
					StringComparison.OrdinalIgnoreCase) &&

				String.Equals(
					cachedManifest.PackageConfigurationHash,
					expectedManifest.PackageConfigurationHash,
					StringComparison.OrdinalIgnoreCase);
		}

		private Boolean ValidateArchiveContents()
		{
			return ValidateArchiveContents(ArchivePath);
		}

		private static Boolean ValidateArchiveContents(
			String archivePath)
		{
			using ZipArchive archive =
				ZipFile.OpenRead(archivePath);

			Boolean hasBootManager =
				ArchiveContainsEntry(
					archive,
					"bootmgr");

			Boolean hasBootWim =
				ArchiveContainsEntry(
					archive,
					"sources/boot.wim");

			Boolean hasUefiBootFile =
				ArchiveContainsEntry(
					archive,
					"efi/boot/bootx64.efi");

			if (!hasBootManager ||
				!hasBootWim ||
				!hasUefiBootFile)
			{
				throw new InvalidDataException(
					"The WinPE cache archive does not contain all required boot files.");
			}

			return true;
		}

		private static Boolean ArchiveContainsEntry(
			ZipArchive archive,
			String expectedPath)
		{
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				String normalizedEntryPath =
					entry.FullName.Replace('\\', '/');

				if (String.Equals(
					normalizedEntryPath,
					expectedPath,
					StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		private static String CalculateFileHash(
			String filePath)
		{
			using SHA256 sha256 = SHA256.Create();

			using FileStream fileStream =
				new FileStream(
					filePath,
					FileMode.Open,
					FileAccess.Read,
					FileShare.Read,
					bufferSize: 1024 * 1024,
					useAsync: false);

			Byte[] hashBytes =
				sha256.ComputeHash(fileStream);

			return Convert.ToHexString(hashBytes);
		}

		private static void DeleteFileIfPresent(
			String filePath)
		{
			if (File.Exists(filePath))
			{
				File.Delete(filePath);
			}
		}
	}
}