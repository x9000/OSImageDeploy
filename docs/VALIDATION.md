# Validation history

This file records release-level validation that cannot be reproduced fully by
the automated test suite. Each result applies only to the identified artifact,
hardware, and configuration; it is not a claim of compatibility with every
device or peripheral.

## 2026-08-25 — 1.94.1526 HP WinPE 3.40 preparation validation

### Candidate identity

- Commit: `71f258909bfc16a98281dcd82d7de7af35fb16ee`
- Installer: `OSImageDeploySuite.msi`
- Installer size: 163,344,384 bytes
- Installer SHA-256:
  `63860AFB782D0BA75C2D0E154C83D0F3F76D16EC03DE30008D639661566D39DF`
- Authenticode signer thumbprint:
  `F2E897E8C120F3D58CB8E8BF99F1FE56E36FC907`
- Sectigo RFC 3161 timestamp: present and valid

### Failure reproduction and correction

- The real HP SoftPaq `sp173204.exe` returned Windows exit code `1168` from
  its supported unpack-only command even though it had extracted 912 files,
  including 195 INF drivers, under `WinPE10_3.40`.
- Both supported `/f` argument forms produced byte-for-byte identical extracted
  trees. The alternate legacy `-pdf` form returned exit code `87` and produced
  no files with this wrapper.
- The service now recognizes only HP's `1168` post-extraction result in addition
  to exit code `0`. It still rejects every other nonzero result and still
  requires the extracted tree to pass the existing reparse-point, INF, file
  count, and expanded-size checks before installation.

### Automated, artifact, and installed-product validation

- The serialized x64 Release build completed with zero warnings and errors.
- The Engine suite passed all 19 checks. The service suite, WinPE driver-package
  tooling, MSI structure checks, project-owned signature checks, and Microsoft
  MsiVal2 ICE validation all passed.
- The exact candidate was signed and timestamped with the hardware-token
  certificate, then installed as an upgrade with Windows Installer exit code
  `0`.
- Installed Apps reported version `1.94.1526`. The service was Running,
  Automatic, LocalSystem, used the installed executable, and retained its two
  delayed restart recovery actions.
- The live named-pipe suite passed from Medium integrity, including read-only USB
  enumeration and destructive-operation confirmation guards.

### Installer lifecycle and final installed-product validation

On 2026-08-26, the exact signed `1.94.1526` candidate completed the release
lifecycle harness using the signed `v1.83.1056` production MSI as the older
input. Both installers had valid timestamped signatures from the expected
certificate and shared UpgradeCode
`{64552430-8321-4966-B779-B464DF8D6E86}`. The older installer SHA-256 was
`77F376194E58880A56C0611A8A7935C34577BCD6119F6A2E99CF92FBCF276E1C`.

- current-version installation: exit code `0`;
- forced repair: exit code `0`;
- older-version downgrade attempt: correctly rejected with exit code `1603`
  and newer-product evidence;
- current-version uninstall and removal checks: exit code `0`;
- current-version clean reinstall: exit code `0`;
- final result: Passed.

After the final reinstall, the complete installed-product validation passed
from Medium integrity. Installed Apps reported `1.94.1526`; the service was
Running, Automatic, LocalSystem, used the installed executable, and retained
its two delayed restart recovery actions. All project-owned installed files
had valid timestamped signatures. The live named-pipe, package-catalog,
read-only USB-enumeration, persisted-operation, WinPE-cache, and destructive-
operation guard checks passed, and the installed non-elevated UI opened and
responded normally. Microsoft MsiVal2 ICE validation also completed without
findings. No destructive USB operation was performed during this lifecycle
run.

### Real HP package result

- Source: HP Client WinPE 10.0 x64 Driver Pack 3.40, `sp173204.exe`.
- Source SHA-256:
  `66BAD758AEB46C72ED1A4A4DE3244D635169771DD4705B9C230E2B4A42AFCD66`
- Source Authenticode status: Valid; signer organization: HP Inc.
- The installed LocalSystem service prepared the package successfully through
  the named pipe from a non-elevated client.
- The resulting managed package was Available, reported version `3.40`, and
  contained 195 INF drivers. Its managed archive SHA-256 was
  `8C61331303BD66FE6CB6E27E6F04526A30D7BE26268ACE65833DEE52F09F8E24`.
- No USB media creation or boot test was performed during the initial correction
  validation. The subsequent combined-package result is recorded below.

### Combined Dell and HP USB-media validation

- The installed product subsequently created new USB media successfully with
  both the prepared Dell and HP WinPE driver packages selected.
- The completed media booted successfully in a VMware Workstation virtual
  machine.
- The same media also booted successfully on the physical Dell laptop used for
  the preceding Dell validation.
- On 2026-08-26, the same media booted successfully on a physical HP EliteDesk
  800 G3 PC.
- These results validate the observed combined-package media-creation path, the
  VMware virtual boot path, the physical Dell boot path, and the physical HP
  EliteDesk 800 G3 boot path.
- The HP result is physical boot validation. No full Windows deployment or
  post-installation driver validation on the HP device is claimed at this
  stage.

## 2026-08-25 — 1.94.1328 WinPE driver preparation and lifecycle validation

### Candidate identity

- Feature commit: `ad75017b202907bcc0cb728503a17a4512ccbe59`
- Merge commit: `898b319b92e2f148b16cc2f43a1911e200348900`
- Installer: `OSImageDeploySuite.msi`
- Installer size: 163,336,192 bytes
- Installer SHA-256:
  `656B65E687EFD3198F4D1160B35A336CD6662C599DA8EFDACC2BB3125DDC85C0`
- Authenticode signer thumbprint:
  `F2E897E8C120F3D58CB8E8BF99F1FE56E36FC907`
- Sectigo RFC 3161 timestamp: present and valid

### Automated, artifact, and installed-product validation

- The canonical signed x64 Release build completed with zero warnings and
  errors.
- The Engine suite passed all 19 safety and persisted-operation checks. The
  service suite passed the gRPC, package-preparation, WinPE-cache, target-safety,
  and destructive-confirmation checks.
- Driver-package tooling passed in both PowerShell 7 and Windows PowerShell
  5.1.
- Every project-owned packaged executable and DLL had a valid timestamped
  signature from the expected certificate.
- MSI structure validation and Microsoft MsiVal2 ICE validation passed without
  findings.
- Pull-request CI passed before merge, and the independent post-merge `main`
  CI run passed afterward.
- The installed UI displayed `OS Image Deployment Tool v1.94.1328`, remained
  non-elevated, connected to the LocalSystem service, and displayed the new
  official-download, package-preparation, and driver-support controls without
  clipping at the tested window size.

### Real manufacturer-package checks

- A real Dell WinPE 11 A10 multi-file CAB was expanded and validated during
  installed-candidate testing, producing 70 INF files. The corrected CAB
  extraction explicitly selects all files rather than relying on the misleading
  zero exit code returned by `expand.exe` when a multi-file CAB is given without
  a file specification.
- That Dell preparation completed on the immediately preceding installed
  candidate. Version 1.94.1328 retained and revalidated the service-owned
  package after upgrade and after lifecycle reinstall. The final code change
  between those candidates only corrected the preparation dialog close order.
- The real signed HP executable `sp172427.exe` was tested against the installed
  1.94.1328 named-pipe service. Its signed HP product metadata identified a full
  Windows model driver pack rather than a WinPE driver pack, so the service
  rejected it before extraction or execution. The HP package remained
  unavailable afterward.

### Installer lifecycle validation

The lifecycle harness used the signed `v1.83.1056` production MSI as the older
input. Its published SHA-256, timestamp, signer, and shared UpgradeCode were
verified before changing installed state.

- current-version installation: exit code `0`;
- forced repair: exit code `0`;
- older-version downgrade attempt: correctly rejected with exit code `1603`
  and the expected newer-product evidence;
- current-version uninstall and removal checks: exit code `0`;
- current-version clean reinstall: exit code `0`;
- final result: Passed.

After reinstall, installed Apps registration, service startup and LocalSystem
identity, recovery configuration, installed signatures, named-pipe calls,
package catalog, persisted-operation status, WinPE-cache boundaries, read-only
USB enumeration, and destructive-operation confirmation guards all passed from
Medium integrity. The lifecycle evidence and verbose MSI logs were retained in
the validation machine's temporary evidence directory.

No destructive USB operation was performed for this candidate, and no VMware
or physical-machine boot result is claimed for version 1.94.1328. The complete
USB, VMware, and Dell physical boot evidence below applies to the exact
1.90.1456 artifact identified in its own section.

## 2026-08-21 — 1.90.1456 installed-product USB validation

### Candidate identity

- Commit: `10b52541cd41c1b296fd631d7e95eead59483c56`
- Installer: `OSImageDeploySuite.msi`
- Installer size: 163,328,000 bytes
- Installer SHA-256:
  `023719DCD6845713EF037440659CF9A3E15442BB87C54C52938287DFAE27105B`
- Authenticode signer thumbprint:
  `F2E897E8C120F3D58CB8E8BF99F1FE56E36FC907`
- Sectigo RFC 3161 timestamp: present and valid

### Automated, artifact, and installed-product validation

- The canonical signed x64 Release build completed with zero warnings and
  errors.
- Every project-owned packaged executable and DLL had a valid timestamped
  signature from the expected certificate.
- MSI structure validation and Microsoft MsiVal2 ICE validation passed without
  findings.
- An in-place upgrade from `1.90.1152` to `1.90.1456` completed successfully.
- Installed Apps registration, service startup mode and LocalSystem identity,
  service recovery policy, installed signatures, named-pipe communication,
  external package catalog, operation persistence round trip, WinPE cache
  boundaries, target enumeration, and destructive-operation guards all passed.
- The installed UI launched directly from Medium integrity and remained
  responsive throughout the USB build.

### Dell-enabled USB-media validation

Immediately before the destructive operation, the target was independently
rediscovered and then shown again by the installed application's confirmation
dialog:

- target: Disk 3, `WDC WDS 100T1R0B-68A`, 1,000,204,886,016 bytes, USB bus;
- stable target identity:
  `USBSTOR\DISK&VEN_WDC__WDS&PROD_100T1R0B-68A&REV_1.00\01293800008D&0:KEPLER`;
- pre-operation state: GPT, Online, Healthy, writable, three partitions, and
  reported by Windows as neither the system disk nor the boot disk;
- selected package: `Dell WinPE driver pack`, containing 70 INF files.

The installed UI displayed the generated title `OS Image Deployment Tool
v1.90.1456` and the redesigned resizable two-column layout. During the build it
showed continuous, phase-aware progress, including selected-driver counts such
as `Adding selected WinPE drivers: 18 / 70`, an elapsed-time heartbeat while
DISM had no native percentage such as `Committing and dismounting Boot.wim
(00:10 elapsed)`, and native DISM progress when it became available.

The cache creation timestamp changed during the run, confirming that the
Dell-selected cache was rebuilt. The operation reached
`Complete - USB media creation completed. 100%`.

Post-build inspection found the same healthy Disk 3 identity with the expected
three-partition GPT layout, a healthy FAT32 `WINPE` volume and a healthy NTFS
`BuildData` volume. The complete installed-product and live-service checks
passed again afterward from Medium integrity.

This exact `1.90.1456` artifact has completed automated, signed-artifact,
installed-product, destructive USB-media, VMware Workstation boot, and physical
hardware boot validation. The resulting USB booted successfully in a VMware
Workstation virtual machine and on a Dell laptop. These results validate the
observed virtual UEFI path and that physical Dell boot path; they do not claim
compatibility with every firmware, model, or peripheral.

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
