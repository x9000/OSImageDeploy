#nullable disable
using Imaging;
using Microsoft.Management.Infrastructure;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using CimType = Microsoft.Management.Infrastructure.CimType;

namespace x9000.Utilities
{
	public class FSManager
	{
		// --------------------------------------------------------------------
		// Win32 constants
		// --------------------------------------------------------------------
		#region "Win32 Constants"
		private const uint GENERIC_READ = 0x80000000;
		private const uint GENERIC_WRITE = 0x40000000;

		private const uint FILE_SHARE_READ = 0x00000001;
		private const uint FILE_SHARE_WRITE = 0x00000002;

		private const uint OPEN_EXISTING = 3;

		private const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
		private const uint IOCTL_DISK_GET_DRIVE_LAYOUT_EX = 0x00070050;
		private const uint IOCTL_DISK_SET_DRIVE_LAYOUT_EX = 0x0007C054;
		private const uint IOCTL_DISK_CREATE_DISK = 0x0007C058;
		private const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140;

		private const uint FSCTL_LOCK_VOLUME = 0x00090018;
		private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;

		private const int PARTITION_STYLE_MBR = 0;
		private const int PARTITION_STYLE_GPT = 1;
		private const int PARTITION_STYLE_RAW = 2;

		private const long ONE_MEGABYTE = 1024 * 1024;
		#endregion

		//public event EventHandler<FSManagerLogEventArgs> LogMessage;

		public sealed class FSManagerLogEventArgs : EventArgs
		{
			public FSManagerLogEventArgs(String level, String message, int timeout = 0)
			{
				Level = level;
				Message = message;
				Timeout = timeout;
			}

			public String Level { get; }
			public String Message { get; }
			public int Timeout { get; }
		}

		// --------------------------------------------------------------------
		// Public object types and enums
		// --------------------------------------------------------------------
		#region "Public custom classes and enums"
		public class DeploymentProgress
		{
			public String Stage { get; set; }
			public String Message { get; set; }
			public Int32 OverallProgress { get; set; }
			public Int32 StageProgress { get; set; }
			public String LogLevel { get; set; }
		}
		public enum DiskPartitionStyle
		{
			Mbr = PARTITION_STYLE_MBR,
			Gpt = PARTITION_STYLE_GPT,
			Raw = PARTITION_STYLE_RAW
		}

		public sealed class DiskInfo : IComparable<DiskInfo>
		{
			public uint DiskNumber { get; set; }
			public ulong SizeBytes { get; set; }
			public string InterfaceType { get; set; } = "";
			public DiskPartitionStyle PartitionStyle { get; set; }
			public List<PartitionInfo> Partitions { get; set; }
			public string Name { get; set; }

			public DiskInfo()
			{
				Name = "";
				Partitions = new List<PartitionInfo>();
			}

			public int CompareTo(DiskInfo other)
			{
				if (other == null)
					return 1;

				return string.Compare(Name, other.Name, StringComparison.OrdinalIgnoreCase);
			}
			public override string ToString()
			{
				String returnValue = this.Name;
				if(this.Partitions.Count > 0)
				{
					returnValue += " (";
					foreach (PartitionInfo partition in Partitions)
					{
						if (partition.DriveLetter != null)
						{
							returnValue += $"{partition.DriveLetter}:,";
						}
					}
					returnValue += ")";
				}
				
				returnValue = returnValue.Replace(",)", ")");
				returnValue.TrimEnd("()");
				return returnValue;
			}
		}

		public sealed class PartitionInfo
		{
			public int PartitionNumber { get; set; }
			public ulong OffsetBytes { get; set; }
			public ulong SizeBytes { get; set; }
			public Guid GptType { get; set; }
			public Guid GptId { get; set; }
			public String DriveLetter { get; set; }
		}
		#endregion
		// --------------------------------------------------------------------
		// Public methods
		// --------------------------------------------------------------------
		#region "Public Methods"
		public static List<DiskInfo> EnumerateDisks()
		{
			List<DiskInfo> disks = new List<DiskInfo>();
			ManagementObjectSearcher diskSearcher =	new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
			foreach (ManagementObject diskObject in diskSearcher.Get())
			{
				DiskInfo disk = new DiskInfo();

				disk.DiskNumber = Convert.ToUInt32(diskObject["Index"]);
				disk.SizeBytes = Convert.ToUInt64(diskObject["Size"] ?? 0);
				disk.InterfaceType = diskObject["InterfaceType"].ToString() + diskObject["Caption"].ToString();
				disk.Name = (Convert.ToString(diskObject["Caption"]) ?? "Unknown") + $" - Disk #{disk.DiskNumber}";
				disk.Partitions = new List<PartitionInfo>();

				ManagementObjectSearcher partitionSearcher = new ManagementObjectSearcher("ROOT\\Microsoft\\Windows\\Storage", $"SELECT * FROM MSFT_Partition WHERE Disknumber={disk.DiskNumber}");
				foreach (ManagementObject partitionObject in partitionSearcher.Get())
				{
					PartitionInfo partition = new PartitionInfo();
					partition.PartitionNumber = Convert.ToInt32(partitionObject["PartitionNumber"]);
					partition.GptType = new Guid("00000000-0000-0000-0000-000000000000");
					try
					{
						partition.GptType = new Guid(Convert.ToString(partitionObject["GptType"]) ?? "00000000-0000-0000-0000-000000000000");
					}
					catch (Exception)
					{
						Debug.WriteLine($"Warning: Failed to parse GPT type for partition {partition.PartitionNumber} on disk {disk.DiskNumber}. Defaulting to all-zero GUID.");
					}
					partition.SizeBytes = Convert.ToUInt64(partitionObject["Size"] ?? 0);
					partition.OffsetBytes = Convert.ToUInt64(partitionObject["Offset"] ?? 0);
					partition.DriveLetter = Convert.ToString(partitionObject["DriveLetter"]);
					disk.Partitions.Add(partition);
				}
				disks.Add(disk);
				disks.Sort();
			}
			return disks;
		}



		public static void CleanDisk(int diskNumber)
		{
			using (SafeFileHandle diskHandle = OpenPhysicalDisk(diskNumber, true))
			{
				long diskSize = GetDiskSize(diskHandle);

				TryDeviceIoControl(diskHandle, FSCTL_LOCK_VOLUME);
				TryDeviceIoControl(diskHandle, FSCTL_DISMOUNT_VOLUME);

				using (FileStream diskStream = new FileStream(diskHandle, FileAccess.ReadWrite))
				{
					WipeBeginningOfDisk(diskStream);
					WipeBackupGptHeader(diskStream, diskSize);

					diskStream.Flush(true);

					// Do this while the FileStream still has the handle open.
					RefreshDiskProperties(diskHandle);
				}
			}
		}

		public static void InitializeGpt(int diskNumber)
		{
			using (SafeFileHandle diskHandle = OpenPhysicalDisk(diskNumber, true))
			{
				CREATE_DISK_GPT createDisk = new CREATE_DISK_GPT();
				createDisk.PartitionStyle = PARTITION_STYLE_GPT;
				createDisk.DiskId = Guid.NewGuid();
				createDisk.MaxPartitionCount = 128;
				SendStructureToDevice(diskHandle, IOCTL_DISK_CREATE_DISK, createDisk);
				RefreshDiskProperties(diskHandle);
			}
		}

		public static void InitializeMbr(int diskNumber)
		{
			using (SafeFileHandle diskHandle = OpenPhysicalDisk(diskNumber, true))
			{
				Random random = new Random();

				CREATE_DISK_MBR createDisk = new CREATE_DISK_MBR();
				createDisk.PartitionStyle = PARTITION_STYLE_MBR;
				createDisk.Signature = Convert.ToUInt32(random.Next());

				SendStructureToDevice(diskHandle, IOCTL_DISK_CREATE_DISK, createDisk);
				RefreshDiskProperties(diskHandle);
			}
		}

		private static ManagementObject GetMsftDisk(ManagementScope scope, int diskNumber)
		{
			ObjectQuery query = new ObjectQuery(
				"SELECT * FROM MSFT_Disk WHERE Number = " + diskNumber.ToString());

			using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
			{
				foreach (ManagementObject disk in searcher.Get())
				{
					return disk;
				}
			}

			throw new InvalidOperationException(
				"Could not find MSFT_Disk number " + diskNumber.ToString());
		}

		public bool CreateSimpleGptLayoutForWindowsUefi(int diskNumber, long windowsPartitionSizeBytes = -1)
		{
			try
			{
				CleanDisk(diskNumber);
				InitializeGpt(diskNumber);

				ulong efiPartitionSize = 260 * ONE_MEGABYTE;
				ulong msrPartitionSize = 128 * ONE_MEGABYTE;
				ulong recoveryPartitionSize = 1024 * ONE_MEGABYTE;

				List<DiskInfo> disks = EnumerateDisks();
				ulong diskSize = 0;
				foreach (DiskInfo disk in disks)
				{
					if (disk.DiskNumber == diskNumber)
					{
						diskSize = disk.SizeBytes;
					}
				}
				ulong windowsPartitionSize = diskSize - efiPartitionSize - msrPartitionSize - recoveryPartitionSize - (1024 * ONE_MEGABYTE);

				string efiType = "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}";
				string msrType = "{e3c9e316-0b5c-4db8-817d-f92df00215ae}";
				string recoveryType = "{de94bba4-06d1-4d40-a16a-bfd50179d6ac}";
				bool partitionResult = false;
				try
				{
					partitionResult = CreatePartition(diskNumber: 0, size: efiPartitionSize, useMaximumSize: false, offsetBytes: null, alignmentBytes: null, driveLetter: null, assignDriveLetter: false, gptType: efiType, isHidden: false, formatPartition: true, fileSystem: "FAT32", fileSystemLabel: "System", quickFormat: true); // EFI Partition
					if (!partitionResult)
					{
						throw new IOException();
					}
					partitionResult = CreatePartition(diskNumber: 0, size: msrPartitionSize, useMaximumSize: false, offsetBytes: null, alignmentBytes: null, driveLetter: Convert.ToChar("S"), assignDriveLetter: false, gptType: msrType, isHidden: false); // MSR Partition
					if (!partitionResult)
					{
						throw new IOException();
					}
					partitionResult = CreatePartition(diskNumber: 0, size: recoveryPartitionSize, useMaximumSize: false, offsetBytes: null, alignmentBytes: null, driveLetter: Convert.ToChar("R"), assignDriveLetter: false, gptType: recoveryType, isHidden: false, formatPartition: true, fileSystem: "NTFS", fileSystemLabel: "Recovery", quickFormat: true); // Recovery Partition
					if (!partitionResult)
					{
						throw new IOException();
					}
					partitionResult = CreatePartition(diskNumber: 0, size: null, useMaximumSize: true, offsetBytes: null, alignmentBytes: null, driveLetter: Convert.ToChar("W"), assignDriveLetter: false, gptType: null, isHidden: false, formatPartition: true, fileSystem: "NTFS", fileSystemLabel: "Windows", quickFormat: true); // Windows partition
					if (!partitionResult)
					{
						throw new IOException();
					}
				}
				catch (IOException)
				{
					return false;
				}
				catch (Exception)
				{
					return false;
				}
			}
			catch
			{
				return false;
			}
			return true;
		}

		//public Task CreateSimpleGptLayoutForWindowsUefiAsync(int diskNumber, long windowsPartitionSizeBytes = -1, CancellationToken cancellationToken = default)
		//{
		//	return Task.Run(delegate
		//	{
		//		try
		//		{
		//			CleanDisk(diskNumber);
		//			LogMessage(this, new FSManagerLogEventArgs("SUCCESS", $"Disk {diskNumber} has been cleaned...", 0));
		//			InitializeGpt(diskNumber);
		//			LogMessage(this, new FSManagerLogEventArgs("SUCCESS", $"Disk {diskNumber} has been initialised...", 0));

		//			ulong efiPartitionSize = 260 * ONE_MEGABYTE;
		//			ulong msrPartitionSize = 128 * ONE_MEGABYTE;
		//			ulong recoveryPartitionSize = 1024 * ONE_MEGABYTE;

		//			List<DiskInfo> disks = EnumerateDisks();
		//			ulong diskSize = 0;
		//			foreach (DiskInfo disk in disks)
		//			{
		//				if (disk.DiskNumber == diskNumber)
		//				{
		//					diskSize = disk.SizeBytes;
		//				}
		//			}
		//			ulong windowsPartitionSize = diskSize - efiPartitionSize - msrPartitionSize - recoveryPartitionSize - (1024 * ONE_MEGABYTE);

		//			string efiType = "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}";
		//			string msrType = "{e3c9e316-0b5c-4db8-817d-f92df00215ae}";
		//			string recoveryType = "{de94bba4-06d1-4d40-a16a-bfd50179d6ac}";
		//			bool partitionResult = false;
		//			try
		//			{
		//				partitionResult = CreatePartition(diskNumber: 0, size: efiPartitionSize, useMaximumSize: false, offsetBytes: null, alignmentBytes: null, driveLetter: null, assignDriveLetter: false, gptType: efiType, isHidden: false, formatPartition: true, fileSystem: "FAT32", fileSystemLabel: "System", quickFormat: true); // EFI Partition
		//				if (partitionResult)
		//				{
		//					LogMessage(this, new FSManagerLogEventArgs("SUCCESS", "Created EFI Partition", 20));
		//				}
		//				else
		//				{
		//					LogMessage(this, new FSManagerLogEventArgs("ERROR", "Unable to create an EFI Partition"));
		//					throw new IOException();
		//				}
		//				partitionResult = CreatePartition(diskNumber: 0, size: msrPartitionSize, useMaximumSize: false, offsetBytes: null, alignmentBytes: null, driveLetter: Convert.ToChar("S"), assignDriveLetter: false, gptType: msrType, isHidden: false); // MSR Partition
		//				if (partitionResult)
		//				{
		//					LogMessage(this, new FSManagerLogEventArgs("SUCCESS", "Created MSR Partition", 20));
		//				}
		//				else
		//				{
		//					LogMessage(this, new FSManagerLogEventArgs("ERROR", "Unable to create the MSR Partition"));
		//					throw new IOException();
		//				}
		//				partitionResult = CreatePartition(diskNumber: 0, size: recoveryPartitionSize, useMaximumSize: false, offsetBytes: null, alignmentBytes: null, driveLetter: Convert.ToChar("R"), assignDriveLetter: false, gptType: recoveryType, isHidden: false, formatPartition: true, fileSystem: "NTFS", fileSystemLabel: "Recovery", quickFormat: true); // Recovery Partition
		//				if (partitionResult)
		//				{
		//					LogMessage(this, new FSManagerLogEventArgs("SUCCESS", "Created the Recovery Partition", 20));
		//				}
		//				else
		//				{
		//					LogMessage(this, new FSManagerLogEventArgs("ERROR", "Unable to create the Recovery Partition"));
		//					throw new IOException();
		//				}
		//				partitionResult = CreatePartition(diskNumber: 0, size: null, useMaximumSize: true, offsetBytes: null, alignmentBytes: null, driveLetter: Convert.ToChar("W"), assignDriveLetter: false, gptType: null, isHidden: false, formatPartition: true, fileSystem: "NTFS", fileSystemLabel: "Windows", quickFormat: true); // Windows partition
		//				if (partitionResult)
		//				{
		//					LogMessage(this, new FSManagerLogEventArgs("SUCCESS", "Created the Windows Partition", 20));
		//				}
		//				else
		//				{
		//					LogMessage(this, new FSManagerLogEventArgs("ERROR", "Unable to create the Windows Partition"));
		//					throw new IOException();
		//				}
		//			}
		//			catch (IOException)
		//			{
		//				LogMessage(this, new FSManagerLogEventArgs("ERROR", "Fatal Error - Unable to create essential partitions. Process cannot continue."));
		//			}
		//			catch (Exception)
		//			{
		//				LogMessage(this, new FSManagerLogEventArgs("ERROR", "Fatal Error - Process cannot continue."));
		//			}
		//		}
		//		catch
		//		{
		//			throw;
		//		}
		//	}, cancellationToken);			
		//}
		public Task<Boolean> CreateSimpleGptLayoutForWindowsUefiAsync(
			uint diskNumber,
			Int64 windowsPartitionSizeBytes = -1,
			IProgress<DeploymentProgress> progress = null,
			CancellationToken cancellationToken = default)
		{
			return Task.Run(() =>
			{
				return CreateSimpleGptLayoutForWindowsUefiInternal(
					diskNumber,
					windowsPartitionSizeBytes,
					progress,
					cancellationToken);
			}, cancellationToken);
		}

		private Boolean CreateSimpleGptLayoutForWindowsUefiInternal(
			uint diskNumber,
			Int64 windowsPartitionSizeBytes,
			IProgress<DeploymentProgress> progress,
			CancellationToken cancellationToken)
		{
			try
			{
				Report(progress, "Cleaning disk", "Cleaning target disk.", 3, 5, "INFO");
				cancellationToken.ThrowIfCancellationRequested();
				RemoveDriveLettersFromDisk(diskNumber);
				CleanDisk(Convert.ToInt32(diskNumber));

				Report(progress, "Initialising disk", "Initialising disk as GPT.", 6, 15, "INFO");
				cancellationToken.ThrowIfCancellationRequested();

				InitializeGpt(Convert.ToInt32(diskNumber));

				UInt64 efiPartitionSize = 260 * ONE_MEGABYTE;
				UInt64 msrPartitionSize = 128 * ONE_MEGABYTE;
				UInt64 recoveryPartitionSize = 1024 * ONE_MEGABYTE;

				Report(progress, "Reading disk information", "Enumerating disks.", 10, 25, "INFO");
				cancellationToken.ThrowIfCancellationRequested();

				List<DiskInfo> disks = EnumerateDisks();
				UInt64 diskSize = 0;

				foreach (DiskInfo disk in disks)
				{
					if (disk.DiskNumber == diskNumber)
					{
						diskSize = disk.SizeBytes;
						break;
					}
				}

				if (diskSize == 0)
				{
					Report(progress, "Failed", $"Disk {diskNumber} could not be found.", 100, 100, "ERROR");
					return false;
				}

				UInt64 reservedSpace = 1024 * ONE_MEGABYTE;
				UInt64 calculatedWindowsPartitionSize = diskSize - efiPartitionSize - msrPartitionSize - recoveryPartitionSize - reservedSpace;

				if (windowsPartitionSizeBytes > 0)
				{
					calculatedWindowsPartitionSize = Convert.ToUInt64(windowsPartitionSizeBytes);
				}

				String efiType = "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}";
				String msrType = "{e3c9e316-0b5c-4db8-817d-f92df00215ae}";
				String recoveryType = "{de94bba4-06d1-4d40-a16a-bfd50179d6ac}";

				Report(progress, "Creating EFI partition", "Creating and formatting EFI system partition.", 12, 35, "INFO");
				cancellationToken.ThrowIfCancellationRequested();

				Boolean partitionResult = CreatePartition(
					diskNumber: diskNumber,
					size: efiPartitionSize,
					useMaximumSize: false,
					offsetBytes: null,
					alignmentBytes: null,
					driveLetter: 'S',
					assignDriveLetter: false,
					gptType: efiType,
					isHidden: false,
					formatPartition: true,
					fileSystem: "FAT32",
					fileSystemLabel: "System",
					quickFormat: true);

				if (!partitionResult)
				{
					throw new IOException("Failed to create EFI partition.");
				}

				Report(progress, "Creating MSR partition", "Creating Microsoft Reserved partition.", 15, 50, "INFO");
				cancellationToken.ThrowIfCancellationRequested();

				partitionResult = CreatePartition(
					diskNumber: diskNumber,
					size: msrPartitionSize,
					useMaximumSize: false,
					offsetBytes: null,
					alignmentBytes: null,
					driveLetter: null,
					assignDriveLetter: false,
					gptType: msrType,
					isHidden: false);

				if (!partitionResult)
				{
					throw new IOException("Failed to create MSR partition.");
				}

				Report(progress, "Creating recovery partition", "Creating and formatting recovery partition.", 17, 60, "INFO");
				cancellationToken.ThrowIfCancellationRequested();

				partitionResult = CreatePartition(
					diskNumber: diskNumber,
					size: recoveryPartitionSize,
					useMaximumSize: false,
					offsetBytes: null,
					alignmentBytes: null,
					driveLetter: 'R',
					assignDriveLetter: false,
					gptType: recoveryType,
					isHidden: false,
					formatPartition: true,
					fileSystem: "NTFS",
					fileSystemLabel: "Recovery",
					quickFormat: true);

				if (!partitionResult)
				{
					throw new IOException("Failed to create recovery partition.");
				}

				Report(progress, "Creating Windows partition", "Creating and formatting Windows partition.", 20, 80, "INFO");
				cancellationToken.ThrowIfCancellationRequested();

				partitionResult = CreatePartition(
					diskNumber: diskNumber,
					size: calculatedWindowsPartitionSize,
					useMaximumSize: false,
					offsetBytes: null,
					alignmentBytes: null,
					driveLetter: 'W',
					assignDriveLetter: false,
					gptType: null,
					isHidden: false,
					formatPartition: true,
					fileSystem: "NTFS",
					fileSystemLabel: "Windows",
					quickFormat: true);

				if (!partitionResult)
				{
					throw new IOException("Failed to create Windows partition.");
				}

				Report(progress, "Complete", "Disk layout created successfully.", 20, 100, "SUCCESS");
				return true;
			}
			catch (OperationCanceledException)
			{
				Report(progress, "Cancelled", "Disk layout operation was cancelled.", 100, 100, "WARN");
				return false;
			}
			catch (Exception ex)
			{
				Report(progress, "Failed", ex.Message, 100, 100, "ERROR");
				return false;
			}
		}

		private void Report(
			IProgress<DeploymentProgress> progress,
			String stage,
			String message,
			Int32 overallProgress,
			Int32 stageProgress,
			String logLevel)
		{
			progress?.Report(new DeploymentProgress
			{
				Stage = stage,
				Message = message,
				OverallProgress = overallProgress,
				StageProgress = stageProgress,
				LogLevel = logLevel
			});
		}

		public static void CopyDirectory(string sourceDir, string destDir, bool overwrite = true)
		{
			Directory.CreateDirectory(destDir);
			// Copy all files
			foreach (string filePath in Directory.GetFiles(sourceDir))
			{
				string fileName = Path.GetFileName(filePath);
				string destFilePath = Path.Combine(destDir, fileName);
				File.Copy(filePath, destFilePath, overwrite);
			}

			// Recursively copy subdirectories
			foreach (string subDirPath in Directory.GetDirectories(sourceDir))
			{
				string subDirName = Path.GetFileName(subDirPath);
				string destSubDirPath = Path.Combine(destDir, subDirName);
				CopyDirectory(subDirPath, destSubDirPath, overwrite);
			}
		}

		public static void RefreshDisk(int diskNumber)
		{
			using (SafeFileHandle diskHandle = OpenPhysicalDisk(diskNumber, true))
			{
				RefreshDiskProperties(diskHandle);
			}
		}

		public static void FormatVolume(char driveLetter, string fileSystem, string label, bool quickFormat, int clusterSize)
		{
			string driveRoot = Char.ToUpperInvariant(driveLetter) + @":\";

			bool formatFinished = false;
			bool formatSucceeded = false;

			FormatCallback callback = delegate (uint command, uint modifier, IntPtr arg)
			{
				const uint FORMAT_DONE = 11;

				if (command == FORMAT_DONE)
				{
					formatFinished = true;
					formatSucceeded = Marshal.ReadInt32(arg) != 0;
				}
			};

			FormatEx(
				driveRoot,
				0,
				fileSystem,
				label,
				quickFormat,
				clusterSize,
				callback);

			if (!formatFinished || !formatSucceeded)
			{
				throw new IOException("Format failed for " + driveRoot);
			}
		}

		public static void FormatVolume(char driveLetter, string fileSystem, string label)
		{
			FormatVolume(driveLetter, fileSystem, label, true, 0);
		}

		public static void FormatNtfs(char driveLetter, string label)
		{
			FormatVolume(driveLetter, "NTFS", label, true, 0);
		}

		public static void FormatFat32(char driveLetter, string label)
		{
			FormatVolume(driveLetter, "FAT32", label, true, 0);
		}

		public static void AssignDriveLetterByVolumeName(string volumeName, char driveLetter)
		{
			string mountPoint = Char.ToUpperInvariant(driveLetter) + @":\";

			bool success = SetVolumeMountPoint(mountPoint, volumeName);

			if (!success)
			{
				ThrowLastWin32Error(
					"Failed to assign " + mountPoint + " to " + volumeName);
			}
		}

		public static void RemoveDriveLetter(char driveLetter)
		{
			string mountPoint = Char.ToUpperInvariant(driveLetter) + @":\";

			bool success = DeleteVolumeMountPoint(mountPoint);

			if (!success)
			{
				ThrowLastWin32Error("Failed to remove mount point " + mountPoint);
			}
		}

		public static void RemoveDriveLettersFromDisk(uint diskNumber)
		{
			ManagementScope scope =
				new ManagementScope(@"\\.\root\microsoft\windows\storage");

			scope.Connect();

			string query =
				"SELECT * FROM MSFT_Partition WHERE DiskNumber = " +
				diskNumber.ToString();

			using (ManagementObjectSearcher searcher =
				new ManagementObjectSearcher(scope, new ObjectQuery(query)))
			{
				foreach (ManagementObject partition in searcher.Get())
				{
					string driveLetter =
						Convert.ToString(partition["DriveLetter"]);

					if (String.IsNullOrWhiteSpace(driveLetter))
					{
						continue;
					}

					Console.WriteLine(
						"Removing drive letter " +
						driveLetter +
						" from partition " +
						partition["PartitionNumber"]);

					ManagementBaseObject inParams =
						partition.GetMethodParameters("RemoveAccessPath");

					inParams["AccessPath"] = driveLetter + @":\";

					ManagementBaseObject outParams =
						partition.InvokeMethod(
							"RemoveAccessPath",
							inParams,
							null);

					uint returnValue =
						Convert.ToUInt32(outParams["ReturnValue"]);

					if (returnValue != 0)
					{
						Console.WriteLine(
							"Failed to remove drive letter. Error: " +
							returnValue.ToString());
					}
				}
			}
		}

		public static bool HidePartition(int diskNumber, int partitionNumber)
		{
			try
			{
				ManagementScope scope = new ManagementScope(@"\\.\root\microsoft\windows\storage");
				scope.Connect();

				string query = "SELECT * FROM MSFT_Partition WHERE DiskNumber = " + diskNumber.ToString() + " AND PartitionNumber = " + partitionNumber.ToString();

				using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query)))
				{
					foreach (ManagementObject partition in searcher.Get())
					{
						ManagementBaseObject inParams = partition.GetMethodParameters("SetAttributes");
						inParams["IsHidden"] = true;

						ManagementBaseObject outParams = partition.InvokeMethod("SetAttributes", inParams, null);

						uint returnWMIValue = Convert.ToUInt32(outParams["ReturnValue"]);

						if (returnWMIValue != 0)
						{
							Console.WriteLine("Failed to hide partition. Error code: " + returnWMIValue.ToString());
							return false;
						}

						Console.WriteLine("Partition hidden successfully.");
						return true;
					}
				}
				return false;
			}
			catch (UnauthorizedAccessException)
			{
				Console.WriteLine("ERROR: This program must be run as Administrator.");
				return false;
			}
			catch (Exception ex)
			{
				Console.WriteLine("Unexpected error: " + ex.Message);
				return false;
			}
		}

		public static bool CreatePartition(uint diskNumber,	ulong? size, bool useMaximumSize, ulong? offsetBytes, uint? alignmentBytes,	char? driveLetter, bool assignDriveLetter, string gptType,	bool isHidden, bool formatPartition = false, string fileSystem = "NTFS", string fileSystemLabel = "", bool quickFormat = true)
		{
			try
			{
				ManagementScope scope = new ManagementScope(@"\\.\root\microsoft\windows\storage");
				scope.Connect();

				string query = "SELECT * FROM MSFT_Disk WHERE Number = " + diskNumber.ToString();

				using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query)))
				{
					foreach (ManagementObject disk in searcher.Get())
					{
						ManagementBaseObject inParams = disk.GetMethodParameters("CreatePartition");
						if (useMaximumSize)
						{
							inParams["UseMaximumSize"] = useMaximumSize;
						}
						if (size.HasValue)
						{
							inParams["Size"] = size.Value;
						}

						if (offsetBytes.HasValue)
						{
							inParams["Offset"] = offsetBytes.Value;
						}

						if (alignmentBytes.HasValue)
						{
							inParams["Alignment"] = alignmentBytes.Value;
						}

						if (assignDriveLetter)
						{
							inParams["AssignDriveLetter"] = true;
						}
						else if (driveLetter.HasValue)
						{
							inParams["DriveLetter"] = driveLetter.Value;
						}

						if (!String.IsNullOrWhiteSpace(gptType))
						{
							inParams["GptType"] = EnsureBracedGuid(gptType);
						}

						inParams["IsHidden"] = isHidden;

						ManagementBaseObject outParams = disk.InvokeMethod("CreatePartition", inParams, null);

						uint returnWMIValue = Convert.ToUInt32(outParams["ReturnValue"]);

						if (returnWMIValue != 0)
						{
							Console.WriteLine("Failed to create partition. Error code: " + returnWMIValue.ToString());
							return false;
						}

						Console.WriteLine("Partition created successfully.");

						ManagementBaseObject createdPartitionInfo =	(ManagementBaseObject)outParams["CreatedPartition"];

						ManagementObject createdPartition = GetMsftPartition(scope,	Convert.ToUInt32(createdPartitionInfo["DiskNumber"]), Convert.ToUInt32(createdPartitionInfo["PartitionNumber"]));

						if (formatPartition)
						{
							FormatPartition(createdPartition, fileSystem, fileSystemLabel, quickFormat);
						}
						return true;
					}
				}
				return false;
			}
			catch (UnauthorizedAccessException)
			{
				Console.WriteLine("ERROR: This program must be run as Administrator.");
				return false;
			}
			catch (Exception ex)
			{
				Console.WriteLine("Unexpected error: " + ex.Message);
				return false;
			}
		}
		private static ManagementObject GetMsftPartition(ManagementScope scope,	uint diskNumber, uint partitionNumber)
		{
			string query = "SELECT * FROM MSFT_Partition WHERE DiskNumber = " +	diskNumber.ToString() +" AND PartitionNumber = " +	partitionNumber.ToString();

			using (ManagementObjectSearcher searcher =	new ManagementObjectSearcher(scope, new ObjectQuery(query)))
			{
				foreach (ManagementObject partition in searcher.Get())
				{
					return partition;
				}
			}

			throw new InvalidOperationException(
				"Could not find MSFT_Partition for disk " +
				diskNumber.ToString() +
				", partition " +
				partitionNumber.ToString());
		}

		private static void FormatPartition(ManagementObject partition, string fileSystem, string fileSystemLabel, bool quickFormat)
		{
			ManagementObject volume = GetVolumeForPartition(partition);

			if (volume == null)
			{
				throw new InvalidOperationException("No MSFT_Volume was found for the created partition.");
			}

			ManagementBaseObject inParams = volume.GetMethodParameters("Format");

			inParams["FileSystem"] = fileSystem;
			inParams["FileSystemLabel"] = fileSystemLabel;
			inParams["Full"] = !quickFormat;
			inParams["Force"] = true;

			ManagementBaseObject outParams = volume.InvokeMethod("Format", inParams, null);

			uint returnValue = Convert.ToUInt32(outParams["ReturnValue"]);

			if (returnValue != 0)
			{
				throw new InvalidOperationException(
					"MSFT_Volume.Format failed. Error code: " + returnValue.ToString());
			}

			Console.WriteLine("Partition formatted successfully.");
		}

		private static ManagementObject GetVolumeForPartition(ManagementObject partition)
		{
			foreach (ManagementObject volume in partition.GetRelated("MSFT_Volume"))
			{
				return volume;
			}
			return null;
		}

		private static string EnsureBracedGuid(string guid)
		{
			if (String.IsNullOrWhiteSpace(guid))
			{
				return guid;
			}

			guid = guid.Trim();

			if (guid.StartsWith("{") && guid.EndsWith("}"))
			{
				return guid;
			}

			return "{" + guid.Trim('{', '}') + "}";
		}

		public static void CreatePartition(uint diskNumber, ulong size)
		{
			try
			{
				// Connect to WMI namespace for storage
				ManagementScope scope = new ManagementScope(@"\\.\root\microsoft\windows\storage");
				scope.Connect();
				// Query the target disk
				string query = $"SELECT * FROM MSFT_Disk WHERE Number = {diskNumber}";
				using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query)))
				{
					foreach (ManagementObject disk in searcher.Get())
					{
						// Prepare parameters for CreatePartition
						ManagementBaseObject inParams = disk.GetMethodParameters("CreatePartition");
						inParams["UseMaximumSize"] = false;
						inParams["Size"] = size;
						inParams["AssignDriveLetter"] = true;

						// Execute method
						ManagementBaseObject outParams = disk.InvokeMethod("CreatePartition", inParams, null);

						uint returnValue = (uint)outParams["ReturnValue"];
						if (returnValue == 0)
						{
							Console.WriteLine("Partition created successfully.");
						}
						else
						{
							Console.WriteLine($"Failed to create partition. Error code: {returnValue}");
						}
					}
				}
			}
			catch (UnauthorizedAccessException)
			{
				Console.WriteLine("ERROR: This program must be run as Administrator.");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Unexpected error: {ex.Message}");
			}
		}
		#endregion
		// --------------------------------------------------------------------
		// Internal disk helpers
		// --------------------------------------------------------------------
		#region "Internal Disk Helpers"
		private static SafeFileHandle OpenPhysicalDisk(int diskNumber, bool openForWrite)
		{
			SafeFileHandle diskHandle = TryOpenPhysicalDisk(diskNumber, openForWrite);

			if (diskHandle == null || diskHandle.IsInvalid)
			{
				string diskPath = @"\\.\PhysicalDrive" + diskNumber.ToString();
				diskHandle = new SafeFileHandle();
				ThrowLastWin32Error("Could not open " + diskPath);
			}
			return diskHandle;
		}
		private static SafeFileHandle TryOpenPhysicalDisk(int diskNumber, bool openForWrite)
		{
			string diskPath = @"\\.\PhysicalDrive" + diskNumber.ToString();

			uint accessFlags;

			if (openForWrite)
			{
				accessFlags = GENERIC_READ | GENERIC_WRITE;
			}
			else
			{
				accessFlags = GENERIC_READ;
			}

			SafeFileHandle diskHandle = CreateFile(
				diskPath,
				accessFlags,
				FILE_SHARE_READ | FILE_SHARE_WRITE,
				IntPtr.Zero,
				OPEN_EXISTING,
				0,
				IntPtr.Zero);

			return diskHandle;
		}

		private static long GetDiskSize(SafeFileHandle diskHandle)
		{
			int bufferSize = Marshal.SizeOf(typeof(DISK_GEOMETRY_EX));
			IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

			try
			{
				int bytesReturned;

				bool success = DeviceIoControl(
					diskHandle,
					IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
					IntPtr.Zero,
					0,
					buffer,
					bufferSize,
					out bytesReturned,
					IntPtr.Zero);

				if (!success)
				{
					ThrowLastWin32Error("Failed to get disk geometry");
				}

				DISK_GEOMETRY_EX geometry =	Marshal.PtrToStructure<DISK_GEOMETRY_EX>(buffer);

				return geometry.DiskSize;
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}

		private static void WipeBeginningOfDisk(FileStream diskStream)
		{
			byte[] emptyBuffer = new byte[ONE_MEGABYTE];

			diskStream.Seek(0, SeekOrigin.Begin);
			diskStream.Write(emptyBuffer, 0, emptyBuffer.Length);
		}

		private static void WipeBackupGptHeader(FileStream diskStream, long diskSize)
		{
			int backupGptBytes = 33 * 512;

			if (diskSize <= backupGptBytes)
			{
				return;
			}

			byte[] emptyBuffer = new byte[backupGptBytes];

			diskStream.Seek(diskSize - backupGptBytes, SeekOrigin.Begin);
			diskStream.Write(emptyBuffer, 0, emptyBuffer.Length);
		}

		private static void RefreshDiskProperties(SafeFileHandle diskHandle)
		{
			TryDeviceIoControl(diskHandle, IOCTL_DISK_UPDATE_PROPERTIES);
		}
		#endregion
		// --------------------------------------------------------------------
		// GPT layout helpers
		// --------------------------------------------------------------------
		#region "GPT Layout Helpers"
		private static unsafe PARTITION_INFORMATION_EX CreateGptPartition(
			long offsetBytes,
			long sizeBytes,
			Guid partitionType,
			string name)
		{
			PARTITION_INFORMATION_GPT gptInfo = new PARTITION_INFORMATION_GPT();

			gptInfo.PartitionType = partitionType;
			gptInfo.PartitionId = Guid.NewGuid();
			gptInfo.Attributes = 0;

			int maximumLength = 35;

			if (name.Length < maximumLength)
			{
				maximumLength = name.Length;
			}

			for (int index = 0; index < maximumLength; index++)
			{
				gptInfo.Name[index] = name[index];
			}

			gptInfo.Name[maximumLength] = '\0';

			PARTITION_INFORMATION_UNION partitionUnion = new PARTITION_INFORMATION_UNION();
			partitionUnion.Gpt = gptInfo;

			PARTITION_INFORMATION_EX partition = new PARTITION_INFORMATION_EX();

			partition.PartitionStyle = PARTITION_STYLE_GPT;
			partition.StartingOffset = offsetBytes;
			partition.PartitionLength = sizeBytes;
			partition.PartitionNumber = 0;
			partition.RewritePartition = true;
			partition.PartitionInformation = partitionUnion;

			return partition;
		}

		private static void SetGptLayout(SafeFileHandle diskHandle,	PARTITION_INFORMATION_EX[] partitions)
		{
			int layoutSize = Marshal.SizeOf(typeof(DRIVE_LAYOUT_INFORMATION_EX));
			int partitionSize = Marshal.SizeOf(typeof(PARTITION_INFORMATION_EX));
			int totalBufferSize = layoutSize + partitionSize * partitions.Length;

			IntPtr buffer = Marshal.AllocHGlobal(totalBufferSize);

			try
			{
				DRIVE_LAYOUT_INFORMATION_GPT gptLayout =
					new DRIVE_LAYOUT_INFORMATION_GPT();

				gptLayout.DiskId = Guid.NewGuid();
				gptLayout.StartingUsableOffset = ONE_MEGABYTE;
				gptLayout.UsableLength = GetDiskSize(diskHandle) - (2 * ONE_MEGABYTE);
				gptLayout.MaxPartitionCount = 128;

				DRIVE_LAYOUT_INFORMATION_UNION layoutUnion =
					new DRIVE_LAYOUT_INFORMATION_UNION();

				layoutUnion.Gpt = gptLayout;

				DRIVE_LAYOUT_INFORMATION_EX layout =
					new DRIVE_LAYOUT_INFORMATION_EX();

				layout.PartitionStyle = PARTITION_STYLE_GPT;
				layout.PartitionCount = partitions.Length;
				layout.DriveLayoutInformation = layoutUnion;

				Marshal.StructureToPtr(layout, buffer, false);

				IntPtr partitionBasePointer = IntPtr.Add(buffer, layoutSize);

				for (int index = 0; index < partitions.Length; index++)
				{
					IntPtr partitionPointer =
						IntPtr.Add(partitionBasePointer, index * partitionSize);

					Marshal.StructureToPtr(partitions[index], partitionPointer, false);
				}

				int bytesReturned;

				bool success = DeviceIoControl(
					diskHandle,
					IOCTL_DISK_SET_DRIVE_LAYOUT_EX,
					buffer,
					totalBufferSize,
					IntPtr.Zero,
					0,
					out bytesReturned,
					IntPtr.Zero);

				if (!success)
				{
					ThrowLastWin32Error("Failed to set GPT drive layout");
				}
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}
		#endregion
		// --------------------------------------------------------------------
		// Generic Win32 helpers
		// --------------------------------------------------------------------
		#region "Win32 Helpers"
		private static void SendStructureToDevice<T>(SafeFileHandle diskHandle, uint ioctlCode, T structure) where T : struct
		{
			int structureSize = Marshal.SizeOf(typeof(T));
			IntPtr structurePointer = Marshal.AllocHGlobal(structureSize);

			try
			{
				Marshal.StructureToPtr(structure, structurePointer, false);

				int bytesReturned;

				bool success = DeviceIoControl(
					diskHandle,
					ioctlCode,
					structurePointer,
					structureSize,
					IntPtr.Zero,
					0,
					out bytesReturned,
					IntPtr.Zero);

				if (!success)
				{
					ThrowLastWin32Error(
						"DeviceIoControl failed. IOCTL: 0x" +
						ioctlCode.ToString("X8"));
				}
			}
			finally
			{
				Marshal.FreeHGlobal(structurePointer);
			}
		}

		private static void TryDeviceIoControl(SafeFileHandle diskHandle, uint ioctlCode)
		{
			int bytesReturned;

			DeviceIoControl(
				diskHandle,
				ioctlCode,
				IntPtr.Zero,
				0,
				IntPtr.Zero,
				0,
				out bytesReturned,
				IntPtr.Zero);
		}

		private static long AlignUp(long value, long alignment)
		{
			long remainder = value % alignment;

			if (remainder == 0)
			{
				return value;
			}

			return value + alignment - remainder;
		}

		private static long AlignDown(long value, long alignment)
		{
			return value - value % alignment;
		}

		private static void ThrowLastWin32Error(string message)
		{
			int errorCode = Marshal.GetLastWin32Error();
			throw new Win32Exception(errorCode, message);
		}
		#endregion
		// --------------------------------------------------------------------
		// Known GPT partition type GUIDs
		// --------------------------------------------------------------------
		#region "Known GPT partition GUIDs"
		public static class KnownGptTypes
		{
			public static readonly Guid EfiSystemPartition =
				new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");

			public static readonly Guid MicrosoftReserved =
				new Guid("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");

			public static readonly Guid BasicData =
				new Guid("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");

			public static readonly Guid WindowsRecovery =
				new Guid("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC");
		}
		#endregion
		// --------------------------------------------------------------------
		// Native structures
		// --------------------------------------------------------------------
		#region "Native Structures"
		[StructLayout(LayoutKind.Sequential)]
		private struct CREATE_DISK_GPT
		{
			public int PartitionStyle;
			public Guid DiskId;
			public long MaxPartitionCount;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct CREATE_DISK_MBR
		{
			public int PartitionStyle;
			public uint Signature;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct DRIVE_LAYOUT_INFORMATION_GPT
		{
			public Guid DiskId;
			public long StartingUsableOffset;
			public long UsableLength;
			public int MaxPartitionCount;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct DRIVE_LAYOUT_INFORMATION_MBR
		{
			public uint Signature;
			public uint CheckSum;
		}

		[StructLayout(LayoutKind.Explicit)]
		private struct DRIVE_LAYOUT_INFORMATION_UNION
		{
			[FieldOffset(0)]
			public DRIVE_LAYOUT_INFORMATION_MBR Mbr;

			[FieldOffset(0)]
			public DRIVE_LAYOUT_INFORMATION_GPT Gpt;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct DRIVE_LAYOUT_INFORMATION_EX
		{
			public int PartitionStyle;
			public int PartitionCount;
			public DRIVE_LAYOUT_INFORMATION_UNION DriveLayoutInformation;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private unsafe struct PARTITION_INFORMATION_GPT
		{
			public Guid PartitionType;
			public Guid PartitionId;
			public ulong Attributes;

			public fixed char Name[36];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct PARTITION_INFORMATION_MBR
		{
			public byte PartitionType;

			[MarshalAs(UnmanagedType.Bool)]
			public bool BootIndicator;

			[MarshalAs(UnmanagedType.Bool)]
			public bool RecognizedPartition;

			public uint HiddenSectors;
			public Guid PartitionId;
		}

		[StructLayout(LayoutKind.Explicit)]
		private struct PARTITION_INFORMATION_UNION
		{
			[FieldOffset(0)]
			public PARTITION_INFORMATION_MBR Mbr;

			[FieldOffset(0)]
			public PARTITION_INFORMATION_GPT Gpt;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct PARTITION_INFORMATION_EX
		{
			public int PartitionStyle;
			public long StartingOffset;
			public long PartitionLength;
			public int PartitionNumber;

			[MarshalAs(UnmanagedType.Bool)]
			public bool RewritePartition;

			public PARTITION_INFORMATION_UNION PartitionInformation;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct DISK_GEOMETRY_EX
		{
			public long Cylinders;
			public int MediaType;
			public int TracksPerCylinder;
			public int SectorsPerTrack;
			public int BytesPerSector;
			public long DiskSize;
		}
		#endregion
		// --------------------------------------------------------------------
		// P/Invoke declarations
		// --------------------------------------------------------------------
		#region "P/Invoke declarations"
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern SafeFileHandle CreateFile(
			string lpFileName,
			uint dwDesiredAccess,
			uint dwShareMode,
			IntPtr lpSecurityAttributes,
			uint dwCreationDisposition,
			uint dwFlagsAndAttributes,
			IntPtr hTemplateFile);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool DeviceIoControl(
			SafeFileHandle hDevice,
			uint dwIoControlCode,
			IntPtr lpInBuffer,
			int nInBufferSize,
			IntPtr lpOutBuffer,
			int nOutBufferSize,
			out int lpBytesReturned,
			IntPtr lpOverlapped);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool SetVolumeMountPoint(
			string lpszVolumeMountPoint,
			string lpszVolumeName);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool DeleteVolumeMountPoint(
			string lpszVolumeMountPoint);

		private delegate void FormatCallback(
			uint command,
			uint modifier,
			IntPtr arg);

		[DllImport("fmifs.dll", CharSet = CharSet.Unicode)]
		private static extern void FormatEx(
			string driveRoot,
			int mediaFlag,
			string fileSystemName,
			string label,
			bool quickFormat,
			int clusterSize,
			FormatCallback callback);
	}
		#endregion
}