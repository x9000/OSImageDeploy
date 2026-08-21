# Validation history

This file records release-level validation that cannot be reproduced fully by
the automated test suite. Each result applies only to the identified artifact,
hardware, and configuration; it is not a claim of compatibility with every
device or peripheral.

## 2026-08-21 — 1.90.1152 installed-product candidate

### Candidate identity

- Commit: `7e51aa738303d5a535d53eb68947b57185924828`
- Installer: `OSImageDeploySuite.msi`
- Installer size: 163,311,616 bytes
- Installer SHA-256:
  `5994B0C3F2CF06B1810852A4A59EB5796985400104850BB12410EA62DEEF89ED`
- Authenticode signer thumbprint:
  `F2E897E8C120F3D58CB8E8BF99F1FE56E36FC907`
- Sectigo RFC 3161 timestamp: present and valid

### Automated and artifact validation

- Canonical x64 signed Release build completed with zero warnings and errors.
- All project-owned packaged executables and DLLs had valid timestamped
  signatures from the expected certificate.
- MSI structure validation passed and reported product version `1.90.1152`.
- Microsoft MsiVal2 ICE validation completed without findings.

### Installed-product validation

- A clean install of the candidate line and an in-place upgrade to the
  canonical x64 artifact both completed successfully.
- Installed Apps registration matched version `1.90.1152` and the candidate
  MSI product code.
- `OSImageDeploy.Service` was Running, Automatic, LocalSystem, and configured
  with two 120-second restart actions and a one-day recovery reset.
- Installed project-owned binaries retained valid timestamped signatures.
- The installed product contained no retired embedded Dell or HP WinPE driver
  archives.
- From a normal Medium-integrity process, the live service suite passed named
  pipe status, external driver-package catalog, read-only USB enumeration,
  operation-status, WinPE-cache status, and destructive-operation confirmation
  guard checks.
- The installed desktop UI opened directly from Medium integrity, created a
  responsive main window, and closed normally.

### Installer lifecycle validation

The lifecycle harness used the signed `v1.83.1056` production MSI as the older
input. Its 608,940,032-byte size, published SHA-256, expected signer, and
timestamp were independently verified before use. The current and older MSIs
reported the same UpgradeCode.

- current-version installation: exit code `0`;
- forced repair: exit code `0`;
- older-version downgrade attempt: correctly rejected with exit code `1603`
  and the expected newer-product condition;
- current-version uninstall and removal checks: exit code `0`;
- current-version clean reinstall: exit code `0`;
- final result: Passed.

After the lifecycle run, the complete installed-product validation was repeated
from Medium integrity. Registration, service configuration, installed
signatures, live named-pipe checks, confirmation guards, read-only USB
enumeration, and responsive non-elevated UI startup all passed again.

### External Dell WinPE driver and USB-media validation

The installed product completed a destructive USB build after the target was
rediscovered, revalidated, and explicitly confirmed. The non-elevated UI sent
only the selected stable package ID; the service re-read the package and target
before proceeding.

- target: Disk 3, `WDC WDS 100T1R0B-68A`, 1,000,204,886,016 bytes, USB bus;
- stable target identity:
  `USBSTOR\DISK&VEN_WDC__WDS&PROD_100T1R0B-68A&REV_1.00\01293800008D&0:KEPLER`;
- pre-operation state: GPT, Online, Healthy, writable, three partitions, and
  reported by Windows as neither the system disk nor the boot disk;
- selected package ID: `dell-winpe`;
- package source version: Dell WinPE 11 A10;
- package contents: 70 INF files;
- package archive SHA-256:
  `6064D439DC5D4B7A60A5660533C5AAB07F095B09CFD3E1146C4CEBDC2C194512`.

The service invalidated the previous cache identity, built a new WinPE cache,
reported all 70 Dell drivers being added, committed `Boot.wim`, and then
prepared the confirmed disk. The resulting cache manifest recorded non-empty
driver-package and package-configuration hashes. The UI reached
`Complete - USB media creation completed. 100%`.

Post-build inspection found the same healthy USB identity with a GPT layout,
a FAT32 `WINPE` volume and an NTFS `BuildData` volume. The complete installed
product and live service checks passed afterward, including no active
operation, last-operation status, named-pipe calls, read-only enumeration, and
confirmation guards.

### VMware Workstation boot validation

The exact Dell-enabled candidate media created above booted successfully in a
VMware Workstation virtual machine. The observed virtual-machine boot path
worked without a reported error. This validates that the completed media is
bootable in that virtual environment; it does not validate Dell hardware,
physical firmware variations, or the applicability of the injected Dell
drivers to a particular physical model.

### Dell Pro 14 Plus physical boot validation

The same Dell-enabled candidate media also booted successfully on a physical
Dell Pro 14 Plus laptop. This validates the physical UEFI boot path for that
model with the Dell A10 WinPE package included.

This result is physical-hardware boot validation only. It does not by itself
record a complete Windows deployment or post-installation driver validation
for this particular candidate media.

## 2026-08-14 — 1.83.1056 RC1 and production release

### Release identity

- Candidate tag: `v1.83.1056-rc.1`
- Production tag: `v1.83.1056`
- Commit: `74b41b9df237de95ac41e52728af65406601837e`
- Installer: `OSImageDeploySuite-1.83.1056.msi`
- Installer SHA-256:
  `77F376194E58880A56C0611A8A7935C34577BCD6119F6A2E99CF92FBCF276E1C`

### USB media creation

- Result: completed successfully through the installed product.
- Target: Disk 2, `WDC WDS 100T1R0B-68A`, 1,000,204,886,016 bytes, USB bus.
- Stable identity used during validation:
  `USBSTOR\DISK&VEN_WDC__WDS&PROD_100T1R0B-68A&REV_1.00\01293800008D&0:KEPLER`.
- Pre-operation state: GPT, Online, Healthy, writable, three partitions, and
  reported by Windows as neither the system disk nor the boot disk.
- The device-reported serial number was all zeroes, so it was not treated as a
  reliable identity by itself.

### Physical-machine deployment

- Result: Windows 11 installed successfully on a Dell Pro Plus 14 laptop.
- Firmware configuration: UEFI with Secure Boot enabled.
- Driver source: device-specific driver pack.
- Driver result: all device drivers supplied by the pack installed and worked.
  Windows servicing subsequently updated some devices to later driver versions;
  these were updates rather than remediation for missing or failed drivers.
- Overall result: the completed installation presented as a fully provisioned
  OEM-quality Windows installation, with no known missing or non-working device
  drivers.

This physical test validates the complete workflow on the hardware and
configuration above. It does not constitute exhaustive testing of every
peripheral, firmware revision, optional component, or Dell Pro Plus 14 variant.

### Other validation classes

The automated build/test, signed-installer, installed-product, and installer
lifecycle results for this candidate are recorded in its GitHub release notes.
A service-created USB had also booted successfully in VMware Workstation during
earlier service-architecture validation; that virtual-machine result was not a
substitute for the physical-machine test recorded here.

### Production promotion audit

The production release reuses the exact signed and physically tested RC1 MSI;
it was not rebuilt. Immediately before promotion, the MSI was downloaded from
the RC1 GitHub release and independently checked:

- file size: 608,940,032 bytes;
- SHA-256 matched the value above and GitHub's release-asset digest;
- Authenticode status: Valid;
- signer thumbprint:
  `F2E897E8C120F3D58CB8E8BF99F1FE56E36FC907`;
- Sectigo RFC 3161 timestamp: present and valid;
- tagged-commit and post-documentation GitHub CI runs: passed;
- installed service remained Running, Automatic, and LocalSystem;
- open GitHub pull requests and issues at promotion time: none.
