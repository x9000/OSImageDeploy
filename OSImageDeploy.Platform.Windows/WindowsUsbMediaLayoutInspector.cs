using Microsoft.Management.Infrastructure;
using OSImageDeploy.Contracts;
using System.Globalization;

namespace OSImageDeploy.Platform.Windows
{
	public sealed class WindowsUsbMediaLayoutInspector
	{
		private const String StorageNamespace =
			@"root\Microsoft\Windows\Storage";

		public UsbMediaLayoutDescriptor Inspect(UInt32 diskNumber)
		{
			using CimSession session = CimSession.Create(null);
			CimInstance disk = session.QueryInstances(
				StorageNamespace,
				"WQL",
				$"SELECT * FROM MSFT_Disk WHERE Number = {diskNumber}")
				.Single();
			UInt16 partitionStyle = GetValue(
				disk,
				"PartitionStyle",
				(UInt16)0);
			List<UsbMediaPartitionDescriptor> partitions =
				new List<UsbMediaPartitionDescriptor>();

			foreach (CimInstance partition in session.QueryInstances(
				StorageNamespace,
				"WQL",
				$"SELECT * FROM MSFT_Partition WHERE DiskNumber = {diskNumber}"))
			{
				String driveLetter = GetString(partition, "DriveLetter");
				CimInstance? volume = GetVolume(session, driveLetter);
				String rootPath = String.IsNullOrWhiteSpace(driveLetter)
					? String.Empty
					: driveLetter + @":\";

				partitions.Add(
					new UsbMediaPartitionDescriptor
					{
						PartitionNumber = GetValue(
							partition,
							"PartitionNumber",
							0U),
						SizeBytes = GetValue(
							partition,
							"Size",
							0UL),
						FileSystem = volume == null
							? String.Empty
							: GetString(volume, "FileSystem"),
						Label = volume == null
							? String.Empty
							: GetString(volume, "FileSystemLabel"),
						DriveLetter = driveLetter,
						HasDriverPacksFolder =
							!String.IsNullOrWhiteSpace(rootPath) &&
							Directory.Exists(Path.Combine(rootPath, "DriverPacks")),
						HasWindowsImagesFolder =
							!String.IsNullOrWhiteSpace(rootPath) &&
							Directory.Exists(Path.Combine(rootPath, "WindowsImages"))
					});
			}

			return new UsbMediaLayoutDescriptor
			{
				PartitionStyle = partitionStyle switch
				{
					2 => "GPT",
					1 => "MBR",
					_ => "RAW"
				},
				Partitions = partitions
					.OrderBy(partition => partition.PartitionNumber)
					.ToList()
			};
		}

		private static CimInstance? GetVolume(
			CimSession session,
			String driveLetter)
		{
			if (driveLetter.Length != 1 ||
				!Char.IsLetter(driveLetter[0]))
			{
				return null;
			}

			Char normalizedDriveLetter = Char.ToUpperInvariant(driveLetter[0]);

			return session.QueryInstances(
				StorageNamespace,
				"WQL",
				$"SELECT * FROM MSFT_Volume WHERE DriveLetter = '{normalizedDriveLetter}'")
				.SingleOrDefault();
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
