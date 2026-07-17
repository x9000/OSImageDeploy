#nullable disable

namespace Utilities
{
	public sealed class WinPeEnvironment
	{
		private const String DefaultArchitecture = "amd64";

		private WinPeEnvironment(
			String installFolder,
			String architecture)
		{
			InstallFolder = installFolder;
			Architecture = architecture;

			MediaFolder = Path.Combine(
				InstallFolder,
				Architecture,
				"Media");

			SourceBootWim = Path.Combine(
				InstallFolder,
				Architecture,
				"en-us",
				"winpe.wim");

			OptionalComponentsFolder = Path.Combine(
				InstallFolder,
				Architecture,
				"WinPE_OCs");

			Version = GetAdkVersion(
				InstallFolder);
		}

		public String InstallFolder { get; }

		public String Architecture { get; }

		public String MediaFolder { get; }

		public String SourceBootWim { get; }

		public String OptionalComponentsFolder { get; }

		public String Version { get; }

		public static WinPeEnvironment Discover()
		{
			String[] possibleAdkLocations =
			{
				Path.Combine(
					Environment.GetFolderPath(
						Environment.SpecialFolder.ProgramFilesX86),
					@"Windows Kits\10\Assessment and Deployment Kit"),

				Path.Combine(
					Environment.GetFolderPath(
						Environment.SpecialFolder.ProgramFiles),
					@"Windows Kits\10\Assessment and Deployment Kit")
			};

			foreach (String adkRootFolder in possibleAdkLocations)
			{
				String installFolder = Path.Combine(
					adkRootFolder,
					"Windows Preinstallation Environment");

				if (!Directory.Exists(installFolder))
				{
					continue;
				}

				WinPeEnvironment environment =
					new WinPeEnvironment(
						installFolder,
						DefaultArchitecture);

				environment.Validate();

				return environment;
			}

			throw new DirectoryNotFoundException(
				"The Windows ADK WinPE installation folder could not be found. " +
				"Ensure that both the Windows ADK and WinPE add-on are installed.");
		}

		private void Validate()
		{
			if (!Directory.Exists(MediaFolder))
			{
				throw new DirectoryNotFoundException(
					$"The WinPE media folder could not be found: {MediaFolder}");
			}

			if (!File.Exists(SourceBootWim))
			{
				throw new FileNotFoundException(
					"The source WinPE WIM could not be found.",
					SourceBootWim);
			}

			if (!Directory.Exists(OptionalComponentsFolder))
			{
				throw new DirectoryNotFoundException(
					"The WinPE optional-components folder could not be found: " +
					OptionalComponentsFolder);
			}
		}

		private static String GetAdkVersion(
			String installFolder)
		{
			DirectoryInfo installDirectory =
				new DirectoryInfo(
					installFolder);

			DirectoryInfo adkDirectory =
				installDirectory.Parent;

			return adkDirectory?.LastWriteTimeUtc.ToString("O") ?? "";
		}
	}
}