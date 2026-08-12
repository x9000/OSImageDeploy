using OSImageDeploy.Contracts;
using OSImageDeploy.Engine;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace OSImageDeploy.Platform.Windows
{
	public sealed class JsonUsbMediaOperationStore : IUsbMediaOperationStore
	{
		private static readonly JsonSerializerOptions _jsonOptions =
			new JsonSerializerOptions
			{
				WriteIndented = true
			};

		public JsonUsbMediaOperationStore(String? stateDirectory = null)
		{
			Boolean useDefaultDirectory =
				String.IsNullOrWhiteSpace(stateDirectory);
			String resolvedStateDirectory;

			if (useDefaultDirectory)
			{
				resolvedStateDirectory = Path.Combine(
					Environment.GetFolderPath(
						Environment.SpecialFolder.CommonApplicationData),
					"OSImageDeploy",
					"Service");
			}
			else
			{
				resolvedStateDirectory = stateDirectory!;
			}

			StateDirectory = resolvedStateDirectory;
			StatePath = Path.Combine(
				StateDirectory,
				"UsbMediaOperationStatus.json");

			if (useDefaultDirectory)
			{
				SecureStateDirectory();
			}
		}

		public String StateDirectory { get; }

		public String StatePath { get; }

		public UsbMediaOperationSnapshot? Load()
		{
			if (!File.Exists(StatePath))
			{
				return null;
			}

			String json = File.ReadAllText(StatePath);
			PersistedState? state =
				JsonSerializer.Deserialize<PersistedState>(json, _jsonOptions);

			if (state?.SchemaVersion != 1 || state.Operation == null)
			{
				throw new InvalidDataException(
					"The persisted USB operation status is invalid or unsupported.");
			}

			return state.Operation;
		}

		public void Save(UsbMediaOperationSnapshot snapshot)
		{
			ArgumentNullException.ThrowIfNull(snapshot);

			Directory.CreateDirectory(StateDirectory);

			String temporaryPath = Path.Combine(
				StateDirectory,
				$"UsbMediaOperationStatus.{Guid.NewGuid():N}.tmp");
			String json = JsonSerializer.Serialize(
				new PersistedState
				{
					SchemaVersion = 1,
					Operation = snapshot
				},
				_jsonOptions);

			try
			{
				File.WriteAllText(temporaryPath, json);
				File.Move(temporaryPath, StatePath, overwrite: true);
			}
			finally
			{
				if (File.Exists(temporaryPath))
				{
					File.Delete(temporaryPath);
				}
			}
		}

		private void SecureStateDirectory()
		{
			DirectoryInfo directory =
				Directory.CreateDirectory(StateDirectory);
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

		private sealed class PersistedState
		{
			public Int32 SchemaVersion { get; init; }

			public UsbMediaOperationSnapshot? Operation { get; init; }
		}
	}
}
