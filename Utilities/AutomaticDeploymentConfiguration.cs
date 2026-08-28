namespace Imaging
{
	using System.Text.Json;

	public static class AutomaticDeploymentConfigurationFile
	{
		public const String FileName = "OSImageDeploy.json";

		private static readonly JsonSerializerOptions _jsonOptions =
			new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				ReadCommentHandling = JsonCommentHandling.Skip,
				AllowTrailingCommas = true,
				WriteIndented = true
			};

		public static String EnsureDefaultFile(String windowsImagesDirectory)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(windowsImagesDirectory);

			Directory.CreateDirectory(windowsImagesDirectory);
			String configurationPath = Path.Combine(
				windowsImagesDirectory,
				FileName);

			if (!File.Exists(configurationPath))
			{
				AutomaticDeploymentConfigurationDocument document =
					new AutomaticDeploymentConfigurationDocument
					{
						Instructions = new[]
						{
							"MANUAL MODE IS THE SAFE DEFAULT. Leave AutomaticDeployment set to false to choose the Windows image interactively.",
							"To enable unattended deployment, set AutomaticDeployment to true.",
							"Replace REPLACE-WITH-YOUR-WIM-FILE.wim with the exact WIM file name stored in this WindowsImages folder.",
							"Replace the 0 beside WimIndex with the positive image index to apply. Confirm the index with DISM /Get-WimInfo before enabling automation.",
							"Automatic deployment erases internal Disk 0, applies the matching driver pack when found, and reboots after successful completion."
						},
						AutomaticDeployment = false,
						WimFileName = "REPLACE-WITH-YOUR-WIM-FILE.wim",
						WimIndex = 0
					};

				File.WriteAllText(
					configurationPath,
					JsonSerializer.Serialize(document, _jsonOptions));
			}

			return configurationPath;
		}

		public static AutomaticDeploymentPlan? DiscoverOnMountedDrives(
			Action<String>? log = null)
		{
			Action<String> logger = log ?? delegate { };
			List<AutomaticDeploymentPlan> automaticPlans = new();

			foreach (DriveInfo drive in DriveInfo.GetDrives())
			{
				if (!drive.IsReady ||
					drive.DriveType != DriveType.Fixed &&
						drive.DriveType != DriveType.Removable)
				{
					continue;
				}

				String windowsImagesDirectory = Path.Combine(
					drive.RootDirectory.FullName,
					"WindowsImages");
				String driverPacksDirectory = Path.Combine(
					drive.RootDirectory.FullName,
					"DriverPacks");

				if (!Directory.Exists(windowsImagesDirectory) ||
					!Directory.Exists(driverPacksDirectory))
				{
					continue;
				}

				String configurationPath = Path.Combine(
					windowsImagesDirectory,
					FileName);

				if (!File.Exists(configurationPath))
				{
					continue;
				}

				AutomaticDeploymentPlan? plan = Load(
					configurationPath);

				if (plan == null)
				{
					logger("Manual deployment configuration found: " + configurationPath);
					continue;
				}

				logger("Automatic deployment configuration found: " + configurationPath);
				automaticPlans.Add(plan);
			}

			if (automaticPlans.Count > 1)
			{
				throw new InvalidDataException(
					"More than one attached deployment volume enables automatic deployment. Disconnect the additional media before continuing.");
			}

			return automaticPlans.SingleOrDefault();
		}

		public static AutomaticDeploymentPlan? Load(String configurationPath)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);

			String resolvedConfigurationPath = Path.GetFullPath(configurationPath);
			String? windowsImagesDirectory = Path.GetDirectoryName(
				resolvedConfigurationPath);

			if (String.IsNullOrWhiteSpace(windowsImagesDirectory))
			{
				throw new InvalidDataException(
					"The automatic deployment configuration path is invalid.");
			}

			if ((File.GetAttributes(resolvedConfigurationPath) &
				FileAttributes.ReparsePoint) != 0 ||
				(File.GetAttributes(windowsImagesDirectory) &
					FileAttributes.ReparsePoint) != 0)
			{
				throw new InvalidDataException(
					"Automatic deployment configuration cannot use reparse points.");
			}

			AutomaticDeploymentConfigurationDocument? document;

			try
			{
				document = JsonSerializer.Deserialize<
					AutomaticDeploymentConfigurationDocument>(
					File.ReadAllText(resolvedConfigurationPath),
					_jsonOptions);
			}
			catch (JsonException exception)
			{
				throw new InvalidDataException(
					"The automatic deployment JSON is invalid: " +
						exception.Message,
					exception);
			}

			if (document == null)
			{
				throw new InvalidDataException(
					"The automatic deployment JSON is empty.");
			}

			if (!document.AutomaticDeployment)
			{
				return null;
			}

			String wimFileName = document.WimFileName?.Trim() ?? String.Empty;

			if (String.IsNullOrWhiteSpace(wimFileName) ||
				Path.IsPathRooted(wimFileName) ||
				!String.Equals(
					Path.GetFileName(wimFileName),
					wimFileName,
					StringComparison.Ordinal) ||
				!String.Equals(
					Path.GetExtension(wimFileName),
					".wim",
					StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException(
					"WimFileName must be the name of one .wim file in the WindowsImages folder, without a path.");
			}

			if (document.WimIndex <= 0)
			{
				throw new InvalidDataException(
					"WimIndex must be a positive image index.");
			}

			String wimFilePath = Path.GetFullPath(
				Path.Combine(windowsImagesDirectory, wimFileName));
			String expectedDirectoryPrefix =
				Path.TrimEndingDirectorySeparator(
					Path.GetFullPath(windowsImagesDirectory)) +
				Path.DirectorySeparatorChar;

			if (!wimFilePath.StartsWith(
				expectedDirectoryPrefix,
				StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException(
					"The configured WIM file resolves outside the WindowsImages folder.");
			}

			if (!File.Exists(wimFilePath))
			{
				throw new FileNotFoundException(
					"The configured WIM file was not found in the WindowsImages folder.",
					wimFilePath);
			}

			if ((File.GetAttributes(wimFilePath) &
				FileAttributes.ReparsePoint) != 0)
			{
				throw new InvalidDataException(
					"The configured WIM file cannot be a reparse point.");
			}

			if (new FileInfo(wimFilePath).Length == 0)
			{
				throw new InvalidDataException(
					"The configured WIM file is empty.");
			}

			return new AutomaticDeploymentPlan
			{
				ConfigurationPath = resolvedConfigurationPath,
				WimFilePath = wimFilePath,
				WimIndex = document.WimIndex
			};
		}

		private sealed class AutomaticDeploymentConfigurationDocument
		{
			public IReadOnlyList<String> Instructions { get; init; } =
				Array.Empty<String>();

			public Boolean AutomaticDeployment { get; init; }

			public String WimFileName { get; init; } = String.Empty;

			public Int32 WimIndex { get; init; }
		}
	}

	public sealed class AutomaticDeploymentPlan
	{
		public required String ConfigurationPath { get; init; }

		public required String WimFilePath { get; init; }

		public Int32 WimIndex { get; init; }
	}
}
