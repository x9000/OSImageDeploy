# Automatic Windows deployment

Every newly prepared OSImageDeploy USB contains this file on its `BuildData`
volume:

```text
WindowsImages\OSImageDeploy.json
```

The generated file is deliberately set to manual mode. It includes readable
instructions and placeholders showing exactly where the WIM file name and
image index belong. A boot-partition-only refresh preserves the existing file
and does not overwrite an operator's configuration.

## Manual mode — the default

The generated configuration resembles this example:

```json
{
  "Instructions": [
    "MANUAL MODE IS THE SAFE DEFAULT. Leave AutomaticDeployment set to false to choose the Windows image interactively.",
    "To enable unattended deployment, set AutomaticDeployment to true.",
    "Replace REPLACE-WITH-YOUR-WIM-FILE.wim with the exact WIM file name stored in this WindowsImages folder.",
    "Replace the 0 beside WimIndex with the positive image index to apply. Confirm the index with DISM /Get-WimInfo before enabling automation.",
    "Automatic deployment erases internal Disk 0, applies the matching driver pack when found, and reboots after successful completion."
  ],
  "AutomaticDeployment": false,
  "WimFileName": "REPLACE-WITH-YOUR-WIM-FILE.wim",
  "WimIndex": 0
}
```

With `AutomaticDeployment` set to `false`, the WinPE client continues to show
the normal image-selection dialog. The placeholder file name and index are not
used in manual mode.

## Enabling automatic deployment

1. Copy the required `.wim` file into the same `WindowsImages` folder as
   `OSImageDeploy.json`.
2. Determine the required image index before enabling automation. For example:

   ```powershell
   Dism.exe /Get-WimInfo /WimFile:E:\WindowsImages\Windows11.wim
   ```

   Replace `E:` with the actual `BuildData` drive letter.
3. Edit only these three values as required:

   ```json
   {
     "AutomaticDeployment": true,
     "WimFileName": "Windows11.wim",
     "WimIndex": 6
   }
   ```

   `WimFileName` must contain only the file name, not a drive letter, directory,
   or relative path. `WimIndex` must be a positive index present in that WIM.
4. Save valid JSON and safely eject the USB media.

When the WinPE client starts, it validates the JSON, confirms that the named
WIM is a non-empty file inside the same `WindowsImages` directory, and reads
the WIM metadata to confirm the configured index. It also completes the normal
hardware and driver-pack scan. Only after all of those preflight checks pass
does it enter the existing deployment workflow.

## Destructive behavior and safety boundaries

Automatic mode is intentionally opt-in because it does not display the WIM
selection dialog or ask for confirmation. A valid enabled configuration causes
the WinPE client to:

- erase and repartition internal **Disk 0**;
- apply the configured WIM image and index;
- install every driver pack that matches the detected computer model;
- configure Windows boot and recovery files; and
- reboot only after the complete deployment succeeds.

If the JSON is missing or remains in manual mode, deployment is interactive.
If automatic configuration is malformed, points outside `WindowsImages`, names
a missing or empty WIM, specifies an unavailable index, or is enabled on more
than one attached deployment volume, unattended deployment does not start.

Always validate an automatic configuration in a disposable virtual machine or
on non-production hardware before using it broadly. Confirm the firmware boot
order and verify that the intended target computer exposes its deployment disk
as Disk 0.
