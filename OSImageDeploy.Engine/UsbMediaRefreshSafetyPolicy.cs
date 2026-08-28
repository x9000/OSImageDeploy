using OSImageDeploy.Contracts;

namespace OSImageDeploy.Engine
{
	public static class UsbMediaRefreshSafetyPolicy
	{
		public const UInt64 MinimumBootPartitionSizeBytes =
			4UL * 1024 * 1024 * 1024;

		public static UsbMediaRefreshValidationResult Validate(
			UsbMediaLayoutDescriptor layout)
		{
			ArgumentNullException.ThrowIfNull(layout);

			List<String> errors = new List<String>();

			if (!String.Equals(
				layout.PartitionStyle,
				"GPT",
				StringComparison.OrdinalIgnoreCase))
			{
				errors.Add("The disk is not partitioned using GPT.");
			}

			if (layout.Partitions.Count != 2)
			{
				errors.Add(
					"Refresh requires exactly the two partitions created by OSImageDeploy.");
			}

			List<UsbMediaPartitionDescriptor> bootCandidates =
				layout.Partitions
					.Where(partition => String.Equals(
						partition.Label,
						"WinPE",
						StringComparison.OrdinalIgnoreCase))
					.ToList();
			List<UsbMediaPartitionDescriptor> dataCandidates =
				layout.Partitions
					.Where(partition => String.Equals(
						partition.Label,
						"BuildData",
						StringComparison.OrdinalIgnoreCase))
					.ToList();

			if (bootCandidates.Count != 1)
			{
				errors.Add(
					"Exactly one partition labelled 'WinPE' is required.");
			}

			if (dataCandidates.Count != 1)
			{
				errors.Add(
					"Exactly one partition labelled 'BuildData' is required.");
			}

			UsbMediaPartitionDescriptor? bootPartition =
				bootCandidates.Count == 1 ? bootCandidates[0] : null;
			UsbMediaPartitionDescriptor? dataPartition =
				dataCandidates.Count == 1 ? dataCandidates[0] : null;

			if (bootPartition != null)
			{
				if (!String.Equals(
					bootPartition.FileSystem,
					"FAT32",
					StringComparison.OrdinalIgnoreCase))
				{
					errors.Add("The 'WinPE' partition is not FAT32.");
				}

				if (bootPartition.SizeBytes < MinimumBootPartitionSizeBytes)
				{
					errors.Add(
						"The 'WinPE' partition is smaller than the required 4 GB.");
				}

				if (String.IsNullOrWhiteSpace(bootPartition.DriveLetter))
				{
					errors.Add("The 'WinPE' partition does not have a drive letter.");
				}
			}

			if (dataPartition != null)
			{
				if (!String.Equals(
					dataPartition.FileSystem,
					"NTFS",
					StringComparison.OrdinalIgnoreCase))
				{
					errors.Add("The 'BuildData' partition is not NTFS.");
				}

				if (String.IsNullOrWhiteSpace(dataPartition.DriveLetter))
				{
					errors.Add("The 'BuildData' partition does not have a drive letter.");
				}

				if (!dataPartition.HasDriverPacksFolder)
				{
					errors.Add(
						"The 'BuildData' partition does not contain the DriverPacks folder.");
				}

				if (!dataPartition.HasWindowsImagesFolder)
				{
					errors.Add(
						"The 'BuildData' partition does not contain the WindowsImages folder.");
				}
			}

			if (bootPartition != null &&
				dataPartition != null &&
				bootPartition.PartitionNumber >= dataPartition.PartitionNumber)
			{
				errors.Add(
					"The 'WinPE' partition is not positioned before the 'BuildData' partition.");
			}

			return new UsbMediaRefreshValidationResult
			{
				IsEligible = errors.Count == 0,
				Summary = errors.Count == 0
					? "The existing OSImageDeploy layout can be refreshed without formatting the BuildData partition."
					: String.Join(" ", errors),
				BootPartition = bootPartition,
				DataPartition = dataPartition
			};
		}
	}
}
