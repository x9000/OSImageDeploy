using OSImageDeploy.Contracts;

namespace OSImageDeploy.Engine
{
	public static class UsbTargetSafetyPolicy
	{
		public static UsbTargetValidationResult Validate(
			UsbTargetDescriptor expectedTarget,
			UsbTargetDescriptor? currentTarget)
		{
			ArgumentNullException.ThrowIfNull(expectedTarget);

			if (currentTarget == null)
			{
				return Invalid(
					"The selected USB target is no longer available.");
			}

			List<String> errors = new List<String>();
			List<String> warnings = new List<String>();

			if (!String.Equals(
				expectedTarget.TargetId,
				currentTarget.TargetId,
				StringComparison.Ordinal))
			{
				errors.Add(
					"The target identity no longer matches the selected device.");
			}

			if (!String.Equals(
				currentTarget.BusType,
				"USB",
				StringComparison.OrdinalIgnoreCase))
			{
				errors.Add("The selected target is not connected by USB.");
			}

			if (currentTarget.IsSystemDisk)
			{
				errors.Add("The selected target contains the system partition.");
			}

			if (currentTarget.IsBootDisk)
			{
				errors.Add("The selected target contains the current boot partition.");
			}

			if (currentTarget.IsClustered)
			{
				errors.Add("The selected target is used by a Windows cluster.");
			}

			if (currentTarget.IsReadOnly)
			{
				errors.Add("The selected target is currently read-only.");
			}

			if (currentTarget.IsOffline)
			{
				errors.Add("The selected target is currently offline.");
			}

			if (currentTarget.HealthStatus == 2)
			{
				errors.Add("Windows reports that the selected target is unhealthy.");
			}
			else if (currentTarget.HealthStatus != 0)
			{
				warnings.Add(
					"Windows does not report the selected target as fully healthy.");
			}

			if (currentTarget.SizeBytes == 0)
			{
				errors.Add("The selected target reports a size of zero bytes.");
			}
			else if (expectedTarget.SizeBytes != currentTarget.SizeBytes)
			{
				errors.Add("The target size has changed since it was selected.");
			}

			if (!IdentifiersMatch(
				expectedTarget.SerialNumber,
				currentTarget.SerialNumber))
			{
				errors.Add("The target serial number has changed since selection.");
			}

			if (!IdentifiersMatch(
				expectedTarget.Model,
				currentTarget.Model))
			{
				errors.Add("The target model has changed since selection.");
			}

			if (String.IsNullOrWhiteSpace(currentTarget.SerialNumber))
			{
				warnings.Add(
					"The device does not expose a hardware serial number; " +
					"its Windows storage identifier will be used instead.");
			}

			if (expectedTarget.DiskNumber != currentTarget.DiskNumber)
			{
				warnings.Add(
					$"Windows reassigned the target from disk " +
					$"{expectedTarget.DiskNumber} to disk " +
					$"{currentTarget.DiskNumber}. The current number will be used.");
			}

			if (errors.Count > 0)
			{
				return new UsbTargetValidationResult
					{
						IsValid = false,
						Summary = String.Join(" ", errors),
						Warnings = warnings,
						ResolvedTarget = currentTarget
					};
			}

			return new UsbTargetValidationResult
			{
				IsValid = true,
				Summary =
					$"Disk {currentTarget.DiskNumber} is an eligible USB target.",
				Warnings = warnings,
				ResolvedTarget = currentTarget
			};
		}

		private static Boolean IdentifiersMatch(
			String expected,
			String current)
		{
			if (String.IsNullOrWhiteSpace(expected) ||
				String.IsNullOrWhiteSpace(current))
			{
				return true;
			}

			return String.Equals(
				expected.Trim(),
				current.Trim(),
				StringComparison.OrdinalIgnoreCase);
		}

		private static UsbTargetValidationResult Invalid(String summary)
		{
			return new UsbTargetValidationResult
			{
				IsValid = false,
				Summary = summary
			};
		}
	}
}
