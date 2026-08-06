using Microsoft.Management.Infrastructure;
using OSImageDeploy.Contracts;
using OSImageDeploy.Engine;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OSImageDeploy.Platform.Windows
{
	public sealed class WindowsUsbTargetProvider :
		IUsbTargetDiscovery,
		IUsbTargetValidator
	{
		private const String StorageNamespace =
			@"root\Microsoft\Windows\Storage";

		private const UInt16 UsbBusType = 7;

		public Task<IReadOnlyList<UsbTargetDescriptor>> GetEligibleTargetsAsync(
			CancellationToken cancellationToken = default)
		{
			return Task.Run<IReadOnlyList<UsbTargetDescriptor>>(
				() => EnumerateTargets(cancellationToken)
					.GroupBy(target => target.TargetId)
					.Where(group => group.Count() == 1)
					.Select(group => group.Single())
					.Where(target => UsbTargetSafetyPolicy
						.Validate(target, target)
						.IsValid)
					.OrderBy(target => target.DiskNumber)
					.ToList(),
				cancellationToken);
		}

		public Task<UsbTargetValidationResult> ValidateTargetAsync(
			UsbTargetDescriptor target,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(target);

			return Task.Run(
				() =>
				{
					List<UsbTargetDescriptor> matchingTargets =
						EnumerateTargets(cancellationToken)
							.Where(candidate =>
								String.Equals(
									candidate.TargetId,
									target.TargetId,
									StringComparison.Ordinal))
							.Take(2)
							.ToList();

					if (matchingTargets.Count > 1)
					{
						return new UsbTargetValidationResult
						{
							IsValid = false,
							Summary =
								"The selected storage identity is not unique. " +
								"No destructive operation can be authorised."
						};
					}

					return UsbTargetSafetyPolicy.Validate(
						target,
						matchingTargets.SingleOrDefault());
				},
				cancellationToken);
		}

		private static IReadOnlyList<UsbTargetDescriptor> EnumerateTargets(
			CancellationToken cancellationToken)
		{
			List<UsbTargetDescriptor> targets =
				new List<UsbTargetDescriptor>();

			using CimSession session = CimSession.Create(null);

			IEnumerable<CimInstance> disks = session.QueryInstances(
				StorageNamespace,
				"WQL",
				"SELECT * FROM MSFT_Disk");

			foreach (CimInstance disk in disks)
			{
				cancellationToken.ThrowIfCancellationRequested();

				UInt32 diskNumber = GetValue(disk, "Number", UInt32.MaxValue);
				UInt64 sizeBytes = GetValue(disk, "Size", 0UL);
				UInt16 busType = GetValue(disk, "BusType", (UInt16)0);
				String model = GetString(disk, "Model");
				String serialNumber = GetString(disk, "SerialNumber");
				String friendlyName = GetString(disk, "FriendlyName");
				String uniqueId = GetString(disk, "UniqueId");
				String location = GetString(disk, "Location");
				String path = GetString(disk, "Path");

				String displayName = String.IsNullOrWhiteSpace(friendlyName)
					? String.IsNullOrWhiteSpace(model) ? "USB storage device" : model
					: friendlyName;

				targets.Add(
					new UsbTargetDescriptor
					{
						TargetId = CreateTargetId(
							uniqueId,
							serialNumber,
							model,
							location,
							path,
							sizeBytes,
							busType),
						DiskNumber = diskNumber,
						DisplayName =
							$"{displayName} - Disk #{diskNumber}",
						Model = model,
						SerialNumber = serialNumber,
						BusType = GetBusTypeName(busType),
						SizeBytes = sizeBytes,
						IsSystemDisk = GetValue(disk, "IsSystem", false),
						IsBootDisk = GetValue(disk, "IsBoot", false),
						IsReadOnly = GetValue(disk, "IsReadOnly", false),
						IsOffline = GetValue(disk, "IsOffline", false),
						IsClustered = GetValue(disk, "IsClustered", false),
						HealthStatus = GetValue(disk, "HealthStatus", (UInt16)0)
					});
			}

			return targets;
		}

		private static String CreateTargetId(
			String uniqueId,
			String serialNumber,
			String model,
			String location,
			String path,
			UInt64 sizeBytes,
			UInt16 busType)
		{
			String primaryIdentifier = !String.IsNullOrWhiteSpace(uniqueId)
				? uniqueId
				: !String.IsNullOrWhiteSpace(serialNumber)
					? serialNumber
					: !String.IsNullOrWhiteSpace(location)
						? location
						: path;

			String identityMaterial = String.Join(
				"|",
				primaryIdentifier.Trim(),
				serialNumber.Trim(),
				model.Trim(),
				sizeBytes.ToString(CultureInfo.InvariantCulture),
				busType.ToString(CultureInfo.InvariantCulture));

			Byte[] identityBytes = Encoding.UTF8.GetBytes(
				identityMaterial.ToUpperInvariant());

			return "disk-" + Convert.ToHexString(
				SHA256.HashData(identityBytes));
		}

		private static String GetBusTypeName(UInt16 busType)
		{
			return busType switch
			{
				UsbBusType => "USB",
				1 => "SCSI",
				2 => "ATAPI",
				3 => "ATA",
				4 => "IEEE 1394",
				6 => "Fibre Channel",
				8 => "RAID",
				9 => "iSCSI",
				10 => "SAS",
				11 => "SATA",
				12 => "SD",
				13 => "MMC",
				14 => "Virtual",
				15 => "File-backed virtual",
				16 => "Storage Spaces",
				17 => "NVMe",
				_ => "Unknown"
			};
		}

		private static String GetString(
			CimInstance instance,
			String propertyName)
		{
			return Convert.ToString(
				instance.CimInstanceProperties[propertyName]?.Value,
				CultureInfo.InvariantCulture)?.Trim() ?? String.Empty;
		}

		private static T GetValue<T>(
			CimInstance instance,
			String propertyName,
			T defaultValue)
		{
			Object? value =
				instance.CimInstanceProperties[propertyName]?.Value;

			if (value == null)
			{
				return defaultValue;
			}

			return (T)Convert.ChangeType(
				value,
				typeof(T),
				CultureInfo.InvariantCulture);
		}
	}
}
