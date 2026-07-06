#nullable disable

namespace Imaging
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.IO.Compression;
	using System.Linq;
	using System.Management;

	public static class DriverPackHelper
	{
		public static String[] GetValidDriverPacks(String rootFolder, Action<String> log = null)
		{
			Action<String> logger = log ?? delegate { };

			String manufacturerName = GetWmiValue("Win32_ComputerSystem", "Manufacturer").ToUpperInvariant();
			String modelName = GetWmiValue("Win32_ComputerSystem", "Model").ToUpperInvariant();

			logger("Manufacturer: " + manufacturerName);
			logger("Model: " + modelName);

			modelName = NormalizeModelName(manufacturerName, modelName);

			logger("Normalized model: " + modelName);

			List<String> returnValue = new List<String>();
			String[] driverPacks = Directory.GetFiles(rootFolder, "*.zip", SearchOption.AllDirectories);

			foreach (String driverPack in driverPacks)
			{
				String descriptionFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

				try
				{
					logger("Testing DriverPack: " + driverPack);

					Boolean descriptionFound = TryCopyExternalDescriptionFile(driverPack, descriptionFile, logger);

					if (!descriptionFound)
					{
						descriptionFound = TryExtractDescriptionFileFromZip(driverPack, descriptionFile, logger);
					}

					if (!descriptionFound)
					{
						continue;
					}

					String[] fileLines = File.ReadAllLines(descriptionFile);
					List<String> supportedModels = GetSupportedModels(fileLines);

					foreach (String supportedModelItem in supportedModels)
					{
						String supportedModel = supportedModelItem.Trim().ToUpperInvariant();

						if (String.IsNullOrWhiteSpace(supportedModel))
						{
							continue;
						}

						if (supportedModel.Contains(modelName))
						{
							logger("DriverPack is valid for this model");
							returnValue.Add(driverPack);
							break;
						}
					}
				}
				finally
				{
					if (File.Exists(descriptionFile))
					{
						File.Delete(descriptionFile);
					}
				}
			}

			return returnValue.ToArray();
		}

		private static String NormalizeModelName(String manufacturerName, String modelName)
		{
			if (manufacturerName.StartsWith("DELL ", StringComparison.OrdinalIgnoreCase) || manufacturerName.Equals("DELL", StringComparison.OrdinalIgnoreCase))
			{
				String[] itemsToRemove =
				{
					"Optiplex",
					"Latitude",
					"Precision",
					"XPS",
					"Inspiron",
					"Dimension",
					"Studio",
					"Vostro",
					"Venue",
					"Alienware",
					",",
					" ",
					"(",
					")",
					"-",
					"/",
					"\\",
					"Dell",
					"Tower",
					"Rack",
					"Work Station",
					"Workstation",
					"Systems",
					"System",
					"Streak",
					"Hybrid",
					"Extreme",
					"12Rugged",
					"14Rugged",
					"8PRO",
					"10PRO",
					"11PRO",
					"XL",
					"MS",
					"NonvPro",
					"Non-vPro",
					"vPro",
					"AIO"
				};

				foreach (String itemToRemove in itemsToRemove)
				{
					modelName = modelName.Replace(itemToRemove.ToUpperInvariant(), String.Empty);
				}
			}

			if (manufacturerName.StartsWith("VMWARE", StringComparison.OrdinalIgnoreCase))
			{
				modelName = "VMWARE";
			}

			return modelName;
		}

		private static Boolean TryCopyExternalDescriptionFile(String driverPackPath, String destinationPath, Action<String> logger)
		{
			String driverPackDirectory = Path.GetDirectoryName(driverPackPath);
			String nameWithoutExtension = Path.GetFileNameWithoutExtension(driverPackPath);
			String basePath = Path.Combine(driverPackDirectory, nameWithoutExtension);

			String[] potentialFiles =
			{
				basePath + ".txt",
				basePath + ".config"
			};

			foreach (String potentialFile in potentialFiles)
			{
				if (File.Exists(potentialFile))
				{
					logger(potentialFile + " file found");
					File.Copy(potentialFile, destinationPath, true);
					return true;
				}
			}

			return false;
		}

		private static Boolean TryExtractDescriptionFileFromZip(String driverPackPath, String destinationPath, Action<String> logger)
		{
			using ZipArchive zipArchive = ZipFile.OpenRead(driverPackPath);

			foreach (ZipArchiveEntry zipEntry in zipArchive.Entries)
			{
				if (zipEntry.FullName.EndsWith("supportedsystems.txt", StringComparison.OrdinalIgnoreCase))
				{
					logger("SupportedSystems.txt file found");
					zipEntry.ExtractToFile(destinationPath, true);
					return true;
				}
			}

			return false;
		}

		private static List<String> GetSupportedModels(String[] fileLines)
		{
			List<String> supportedModels = new List<String>();

			foreach (String fileLine in fileLines)
			{
				supportedModels.AddRange(fileLine.Split(','));
				supportedModels.AddRange(fileLine.Split(';'));
				supportedModels.AddRange(fileLine.Split(':'));
				supportedModels.Add(fileLine);
			}

			return supportedModels;
		}

		private static String GetWmiValue(String wmiClass, String propertyName)
		{
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM " + wmiClass);

			foreach (ManagementObject managementObject in searcher.Get())
			{
				Object value = managementObject[propertyName];

				if (value != null)
				{
					return value.ToString();
				}
			}

			return String.Empty;
		}
	}
}