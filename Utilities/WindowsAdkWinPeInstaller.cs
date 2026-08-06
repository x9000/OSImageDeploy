#nullable disable
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class WindowsAdkWinPeInstaller
{
	#region Fields

	private readonly Uri _adkSetupUri;
	private readonly Uri _winPeSetupUri;
	private readonly string _workDirectory;
	private readonly Version _minimumAcceptableVersion;
	private readonly bool _forceUpgradeIfInstalledVersionIsOlder;

	#endregion

	#region Events and Properties

	public event EventHandler<InstallerProgressEventArgs> ProgressChanged;

	public InstalledPackage ADK { get; set; }
	public InstalledPackage WinPE { get; set; }

	#endregion

	#region Construction

	public WindowsAdkWinPeInstaller(Version minimumAcceptableVersion = null, bool forceUpgradeIfInstalledVersionIsOlder = false, string workDirectory = null, Uri adkSetupUri = null, Uri winPeSetupUri = null)
	{
		_minimumAcceptableVersion = minimumAcceptableVersion;
		_forceUpgradeIfInstalledVersionIsOlder = forceUpgradeIfInstalledVersionIsOlder;
		_workDirectory = workDirectory ?? Path.Combine(Path.GetTempPath(), "WindowsADK-WinPE");
		_adkSetupUri = adkSetupUri ?? new Uri("https://go.microsoft.com/fwlink/?linkid=2337875");
		_winPeSetupUri = winPeSetupUri ?? new Uri("https://go.microsoft.com/fwlink/?linkid=2337681");

		this.ADK = FindInstalledPackage("Windows Assessment and Deployment Kit");
		this.WinPE = FindInstalledPackage("Windows PE");
	}

	#endregion

	#region Public API

	public async Task InstallOrModifyAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			OnProgress("Starting", "Preparing Windows ADK / Windows PE installation.");
			Directory.CreateDirectory(_workDirectory);

			OnProgress("Detecting", "Checking installed ADK and Windows PE packages.");
			InstalledPackage adk = FindInstalledPackage("Windows Assessment and Deployment Kit");
			InstalledPackage winPe = FindInstalledPackage("Windows PE");

			if (ShouldUpgrade(adk, winPe))
			{
				OnProgress("Upgrade", "Installed version is older than the minimum acceptable version.");

				if (winPe != null)
				{
					await UninstallAsync(winPe, cancellationToken);
				}

				if (adk != null)
				{
					await UninstallAsync(adk, cancellationToken);
				}

				adk = null;
				winPe = null;
			}

			string adkSetup = adk != null ? adk.SetupExePath : Path.Combine(_workDirectory, "adksetup.exe");
			string winPeSetup = winPe != null ? winPe.SetupExePath : Path.Combine(_workDirectory, "adkwinpesetup.exe");

			if (adk == null)
			{
				await DownloadAndValidateAsync(_adkSetupUri, adkSetup, "ADK Download", cancellationToken);
			}
			else
			{
				OnProgress("ADK", "Using existing ADK setup executable.");
			}

			if (winPe == null)
			{
				await DownloadAndValidateAsync(_winPeSetupUri, winPeSetup, "WinPE Download", cancellationToken);
			}
			else
			{
				OnProgress("WinPE", "Using existing Windows PE setup executable.");
			}

			await RunInstallerAsync(adkSetup, cancellationToken, "/quiet", "/norestart", "/ceip", "off", "/features", "OptionId.DeploymentTools");
			await RunInstallerAsync(winPeSetup, cancellationToken, "/quiet", "/norestart", "/ceip", "off", "/features", "OptionId.WindowsPreinstallationEnvironment");

			ValidateInstallation();

			this.ADK = FindInstalledPackage("Windows Assessment and Deployment Kit");
			this.WinPE = FindInstalledPackage("Windows PE");

			OnProgress("Completed", "Windows ADK and Windows PE are installed.", 100);
		}
		catch (OperationCanceledException)
		{
			OnProgress("Cancelled", "Windows ADK / Windows PE installation was cancelled.");
			throw;
		}
		catch (Exception ex)
		{
			OnProgress("Failed", ex.Message);
			throw;
		}
	}

	public static bool IsAdkDeploymentToolsInstalled()
	{
		foreach (string root in CandidateAdkRoots())
		{
			string path = Path.Combine(root, "Deployment Tools");

			if (Directory.Exists(path))
			{
				return true;
			}
		}

		return false;
	}

	public static bool IsWinPeAddonInstalled()
	{
		foreach (string root in CandidateAdkRoots())
		{
			string path = Path.Combine(root, "Windows Preinstallation Environment");

			if (Directory.Exists(path))
			{
				return true;
			}
		}

		return false;
	}

	#endregion

	#region Install Workflow Helpers

	private bool ShouldUpgrade(InstalledPackage adk, InstalledPackage winPe)
	{
		if (!_forceUpgradeIfInstalledVersionIsOlder || _minimumAcceptableVersion == null)
		{
			return false;
		}

		List<InstalledPackage> packages = new List<InstalledPackage>();

		if (adk != null)
		{
			packages.Add(adk);
		}

		if (winPe != null)
		{
			packages.Add(winPe);
		}

		foreach (InstalledPackage package in packages)
		{
			if (package.Version != null && package.Version < _minimumAcceptableVersion)
			{
				return true;
			}
		}

		return false;
	}

	private async Task UninstallAsync(InstalledPackage package, CancellationToken cancellationToken)
	{
		OnProgress("Uninstall", "Uninstalling " + package.DisplayName + ".");
		await RunInstallerAsync(package.SetupExePath, cancellationToken, "/uninstall", "/quiet", "/norestart");
	}

	private void ValidateInstallation()
	{
		OnProgress("Validation", "Checking installed ADK Deployment Tools.");

		if (!IsAdkDeploymentToolsInstalled())
		{
			throw new InvalidOperationException("ADK Deployment Tools were not detected after setup.");
		}

		OnProgress("Validation", "Checking installed Windows PE add-on.");

		if (!IsWinPeAddonInstalled())
		{
			throw new InvalidOperationException("Windows PE add-on was not detected after setup.");
		}
	}

	#endregion

	#region Package Discovery

	private static InstalledPackage FindInstalledPackage(string displayNameContains)
	{
		List<RegistryKey> uninstallRegistryKeys = new List<RegistryKey>();
		uninstallRegistryKeys.Add(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"));
		uninstallRegistryKeys.Add(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"));

		foreach (RegistryKey key in uninstallRegistryKeys)
		{
			if (key == null)
			{
				continue;
			}

			using (RegistryKey uninstallKey = key)
			{
				foreach (string subKeyName in uninstallKey.GetSubKeyNames())
				{
					using (RegistryKey packageKey = uninstallKey.OpenSubKey(subKeyName))
					{
						if (packageKey == null)
						{
							continue;
						}

						string displayName = packageKey.GetValue("DisplayName", "").ToString();

						if (String.IsNullOrWhiteSpace(displayName))
						{
							continue;
						}

						if (!displayName.Contains(displayNameContains, StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}

						string displayVersion = packageKey.GetValue("DisplayVersion", "").ToString();
						Version version = TryCreateVersion(displayVersion);
						string setupExePath = FindCachedSetupExe(packageKey, displayName);

						if (String.IsNullOrWhiteSpace(setupExePath))
						{
							continue;
						}

						return new InstalledPackage(displayName, version, setupExePath);
					}
				}
			}
		}

		return null;
	}

	private static Version TryCreateVersion(string displayVersion)
	{
		if (String.IsNullOrWhiteSpace(displayVersion))
		{
			return null;
		}

		Version version = null;

		if (Version.TryParse(displayVersion, out version))
		{
			return version;
		}

		return null;
	}

	private static string FindCachedSetupExe(RegistryKey packageKey, string displayName)
	{
		string[] registryValues =
		{
			packageKey.GetValue("ModifyPath") as string,
			packageKey.GetValue("UninstallString") as string,
			packageKey.GetValue("QuietUninstallString") as string
		};

		foreach (string registryValue in registryValues)
		{
			if (String.IsNullOrWhiteSpace(registryValue))
			{
				continue;
			}

			string exePath = ExtractExePath(registryValue);

			if (File.Exists(exePath))
			{
				return exePath;
			}
		}

		string packageCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Package Cache");

		if (!Directory.Exists(packageCachePath))
		{
			return null;
		}

		string expectedExeName = displayName.Contains("Windows PE", StringComparison.OrdinalIgnoreCase) ? "adkwinpesetup.exe" : "adksetup.exe";

		EnumerationOptions enumerationOptions = new EnumerationOptions
		{
			RecurseSubdirectories = true,
			IgnoreInaccessible = true,
			ReturnSpecialDirectories = false
		};

		foreach (string filePath in Directory.EnumerateFiles(
			packageCachePath,
			expectedExeName,
			enumerationOptions))
		{
			return filePath;
		}

		return null;
	}

	private static string ExtractExePath(string command)
	{
		command = command.Trim();

		if (command.StartsWith("\"", StringComparison.Ordinal))
		{
			int endQuoteIndex = command.IndexOf("\"", 1, StringComparison.Ordinal);

			if (endQuoteIndex > 1)
			{
				return command.Substring(1, endQuoteIndex - 1);
			}
		}

		int exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);

		if (exeIndex >= 0)
		{
			return command.Substring(0, exeIndex + 4);
		}

		return command;
	}

	#endregion

	#region Download and Process Execution

	private async Task DownloadAndValidateAsync(Uri uri, string destination, string stage, CancellationToken cancellationToken)
	{
		OnProgress(stage, "Downloading " + Path.GetFileName(destination) + ".", 0);

		using (HttpClient client = new HttpClient())
		{
			using (HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
			{
				response.EnsureSuccessStatusCode();

				long? totalBytes = response.Content.Headers.ContentLength;

				using (Stream input = await response.Content.ReadAsStreamAsync())
				{
					using (FileStream output = File.Create(destination))
					{
						byte[] buffer = new byte[81920];
						long totalRead = 0;
						int read = 0;
						int lastPercent = -1;

						while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
						{
							await output.WriteAsync(buffer, 0, read, cancellationToken);
							totalRead += read;

							if (totalBytes.HasValue && totalBytes.Value > 0)
							{
								int percent = (int)((totalRead * 100L) / totalBytes.Value);

								if (percent != lastPercent)
								{
									lastPercent = percent;
									OnProgress(stage, "Downloaded " + totalRead + " of " + totalBytes.Value + " bytes.", percent);
								}
							}
							else
							{
								OnProgress(stage, "Downloaded " + totalRead + " bytes.");
							}
						}
					}
				}
			}
		}

		OnProgress(stage, "Downloaded " + Path.GetFileName(destination) + ".", 100);
	}

	private async Task RunInstallerAsync(string filePath, CancellationToken cancellationToken, params string[] arguments)
	{
		OnProgress("Installer", "Starting " + Path.GetFileName(filePath) + ".");

		ProcessStartInfo processStartInfo = new ProcessStartInfo();
		processStartInfo.FileName = filePath;
		processStartInfo.UseShellExecute = true;
		processStartInfo.Verb = "runas";
		processStartInfo.Arguments = String.Join(" ", arguments.Select(QuoteIfNeeded));

		using (Process process = Process.Start(processStartInfo))
		{
			if (process == null)
			{
				throw new InvalidOperationException("Could not start " + filePath + ".");
			}

			await process.WaitForExitAsync(cancellationToken);

			OnProgress("Installer", Path.GetFileName(filePath) + " exited with code " + process.ExitCode + ".");

			if (process.ExitCode == 0)
			{
				return;
			}

			if (process.ExitCode == 3010)
			{
				OnProgress("Installer", Path.GetFileName(filePath) + " completed and requires a restart.");
				return;
			}

			throw new InvalidOperationException(Path.GetFileName(filePath) + " failed with exit code " + process.ExitCode + ".");
		}
	}

	private static string QuoteIfNeeded(string value)
	{
		if (value.Contains(" "))
		{
			return "\"" + value + "\"";
		}

		return value;
	}

	#endregion

	#region Validation Paths

	private static string[] CandidateAdkRoots()
	{
		return new string[]
		{
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Windows Kits\10\Assessment and Deployment Kit"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Kits\10\Assessment and Deployment Kit")
		};
	}

	#endregion

	#region Progress

	private void OnProgress(string stage, string message, int? percent = null)
	{
		ProgressChanged?.Invoke(this, new InstallerProgressEventArgs(stage, message, percent));
	}

	#endregion

	#region Nested Types

	public sealed class InstallerProgressEventArgs : EventArgs
	{
		public InstallerProgressEventArgs(string stage, string message, int? percent)
		{
			Stage = stage;
			Message = message;
			Percent = percent;
		}

		public string Stage { get; private set; }

		public string Message { get; private set; }

		public int? Percent { get; private set; }
	}

	public sealed class InstalledPackage
	{
		public InstalledPackage(string displayName, Version version, string setupExePath)
		{
			DisplayName = displayName;
			Version = version;
			SetupExePath = setupExePath;
		}

		public string DisplayName { get; private set; }

		public Version Version { get; private set; }

		public string SetupExePath { get; private set; }
	}

	#endregion
}
