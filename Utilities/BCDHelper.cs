#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilities
{
	internal sealed class BcdEntry
	{
		public String Identifier { get; set; }
		public String Description { get; set; }
		public Dictionary<String, String> Values { get; } = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
	}
	public class BCDHelper
	{
		public static String GetWindowsGUID()
		{
			String returnValue = "";
			List<BcdEntry> entries = GetBcdEntries();

			foreach (BcdEntry entry in entries.Where(e => String.Equals(e.Description, "Windows 11", StringComparison.OrdinalIgnoreCase)
				|| String.Equals(e.Description, "Windows 10", StringComparison.OrdinalIgnoreCase)
				|| String.Equals(e.Description, "Windows", StringComparison.OrdinalIgnoreCase)))
			{
				returnValue = $"{entry.Identifier}";
			}
			return returnValue;
		}

		private static List<BcdEntry> GetBcdEntries()
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = "bcdedit.exe",
				Arguments = "/enum /v",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using Process process = Process.Start(startInfo);

			String output = process.StandardOutput.ReadToEnd();
			String error = process.StandardError.ReadToEnd();

			process.WaitForExit();

			if (process.ExitCode != 0)
			{
				throw new InvalidOperationException(error);
			}

			return ParseBcdEditOutput(output);
		}

		private static List<BcdEntry> ParseBcdEditOutput(String output)
		{
			List<BcdEntry> entries = new List<BcdEntry>();
			BcdEntry currentEntry = null;

			foreach (String rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
			{
				String line = rawLine.TrimEnd();

				if (String.IsNullOrWhiteSpace(line))
				{
					continue;
				}

				if (line.StartsWith("Windows Boot Loader", StringComparison.OrdinalIgnoreCase)
					|| line.StartsWith("Windows Boot Manager", StringComparison.OrdinalIgnoreCase)
					|| line.StartsWith("Firmware Application", StringComparison.OrdinalIgnoreCase)
					|| line.StartsWith("Resume from Hibernate", StringComparison.OrdinalIgnoreCase))
				{
					currentEntry = new BcdEntry();
					entries.Add(currentEntry);
					continue;
				}

				if (currentEntry == null)
				{
					continue;
				}

				Match match = Regex.Match(line, @"^(?<key>\S+)\s+(?<value>.+)$");

				if (!match.Success)
				{
					continue;
				}

				String key = match.Groups["key"].Value;
				String value = match.Groups["value"].Value.Trim();

				currentEntry.Values[key] = value;

				if (String.Equals(key, "identifier", StringComparison.OrdinalIgnoreCase))
				{
					currentEntry.Identifier = value;
				}
				else if (String.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
				{
					currentEntry.Description = value;
				}
			}
			return entries;
		}
	}
}

