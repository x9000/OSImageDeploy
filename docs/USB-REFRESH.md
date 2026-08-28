# USB boot partition refresh

OSImageDeploy can refresh boot media on a USB disk that was previously created
by the application without formatting its `BuildData` partition. This is useful
when the WinPE client or optional WinPE drivers have changed but the Windows
images and device driver packs on the data partition should be retained.

Refresh remains a destructive operation: the FAT32 `WinPE` partition is
formatted. It therefore requires the same explicit operator confirmation,
stable physical-disk identity, service-side rediscovery, and safety validation
as a full USB rebuild.

## Eligibility rules

The service permits an in-place refresh only when all of the following are true:

- the selected physical disk is still the same healthy, writable, non-system,
  non-boot USB disk shown by the desktop;
- the disk uses GPT and contains exactly two partitions;
- exactly one partition is labelled `WinPE`, uses FAT32, is at least 4 GB, has a
  drive letter, and appears before the data partition;
- exactly one partition is labelled `BuildData`, uses NTFS, and has a drive
  letter;
- the `BuildData` partition contains both `DriverPacks` and `WindowsImages`.

An unexpected partition, label, filesystem, missing folder, unavailable drive
letter, or other ambiguous state rejects refresh. The operator can then leave
the disk unchanged or deliberately choose a separately confirmed full rebuild.

## Operation ordering

The service resolves and validates any selected optional WinPE driver packages,
then prepares a complete WinPE payload before touching the USB disk. Immediately
before formatting, it:

1. rediscovers the selected physical disk by its stable identity;
2. repeats the general USB target safety checks;
3. rereads and validates the complete partition layout;
4. verifies that the new payload fits the existing `WinPE` partition with a
   256 MB working margin and contains no file above FAT32's individual-file
   limit;
5. rechecks the boot partition number, size, and drive letter;
6. formats only that partition and copies the new WinPE media.

Cancellation is honoured until formatting begins. Once the boot partition has
been formatted, the service completes the copy instead of deliberately leaving
the partition empty in response to a late cancellation request.

No request or confirmation is retained across a service restart. An interrupted
operation is reported as failed and must be started again after the target has
been inspected.

## Data protection boundary

Refresh deliberately does not format, resize, or recreate `BuildData`, and does
not delete or rewrite its `DriverPacks`, `WindowsImages`, or other content.
Nevertheless, important source images should have another copy: power loss,
failing hardware, firmware faults, and operating-system storage errors can
affect any attached disk operation.

Automated policy and contract tests do not prove preserved-data behavior on real
media. Release validation for this feature should separately record an
installed-product refresh using a disposable USB target, before-and-after hashes
for representative `BuildData` files, and subsequent VMware and physical-device
boot results.
