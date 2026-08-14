# Validation history

This file records release-level validation that cannot be reproduced fully by
the automated test suite. Each result applies only to the identified artifact,
hardware, and configuration; it is not a claim of compatibility with every
device or peripheral.

## 2026-08-14 — 1.83.1056 RC1

### Release identity

- Tag: `v1.83.1056-rc.1`
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
- Driver result: the major hardware devices were installed with appropriate
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
