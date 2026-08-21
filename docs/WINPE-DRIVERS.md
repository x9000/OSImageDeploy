# WinPE driver packages

OSImageDeploy is moving to operator-supplied WinPE driver packages. OEM driver
binaries are not stored in Git and will not be embedded in future public-safe
installers. A package is included in WinPE only when it is available in the
service package store and explicitly selected for a USB build.

This document describes the package format and preparation workflow. The
service publishes the catalog to the non-elevated desktop UI, which shows
available packages as optional selections and unavailable built-in entries as
download and preparation guidance.

Selected package IDs are re-read and revalidated by the service immediately
before it starts a USB operation. Only those archives are extracted and
injected into the WinPE image. The WinPE cache identity includes their contents,
so changing the selection or updating a package cannot reuse an incompatible
cache. Builds with no selected package contain no optional OEM drivers.

## Package store

The service-owned package store is:

```text
%ProgramData%\OSImageDeploy\DriverPackages
```

Each package has a stable lowercase ID and its own directory:

```text
DriverPackages\
  dell-winpe\
    package.json
    drivers.zip
  hp-winpe\
    package.json
    drivers.zip
  example-manufacturer-winpe\
    package.json
    drivers.zip
```

The default store is restricted to LocalSystem and administrators. The desktop
UI receives package metadata through the service and sends only selected stable
IDs. It never sends an arbitrary archive or filesystem path to the privileged
build operation.

## Prepare a package

Run the preparation script from an elevated PowerShell session. The source can
be an existing ZIP containing INF files:

```powershell
.\Build\Import-WinPeDriverPackage.ps1 `
  -PackageId dell-winpe `
  -DisplayName 'Dell WinPE driver pack' `
  -Manufacturer Dell `
  -SourceVersion '<vendor version>' `
  -SourcePageUrl 'https://www.dell.com/support/kbdoc/en-us/000107478/dell-command-deploy-winpe-driver-packs' `
  -ArchivePath '<path-to-prepared-zip>'
```

Or it can package a previously extracted directory:

```powershell
.\Build\Import-WinPeDriverPackage.ps1 `
  -PackageId hp-winpe `
  -DisplayName 'HP WinPE driver pack' `
  -Manufacturer HP `
  -SourceVersion '<vendor version>' `
  -SourcePageUrl 'https://ftp.ext.hp.com/pub/caps-softpaq/cmit/HP_WinPE_DriverPack.html' `
  -SourceDirectory '<path-to-extracted-folder>'
```

The script refuses archives without INF files, CI placeholders, unsafe archive
paths, invalid package IDs, and accidental replacement. Supply `-Replace` only
when intentionally updating an existing package.

## Manufacturer preparation

Always download drivers from the manufacturer's official site and review the
terms accompanying the exact package.

### Dell

Use the [Dell Command | Deploy WinPE driver-pack page](https://www.dell.com/support/kbdoc/en-us/000107478/dell-command-deploy-winpe-driver-packs).
If the downloaded package is already a ZIP containing INF files, it can be
prepared directly. Current Dell packs may instead be Dell Update Package
executables; Dell documents extraction with:

```text
packagename.exe /s /e="<destination folder>"
```

Prepare the extracted folder with `Import-WinPeDriverPackage.ps1`.

### HP

Use the [HP Client Windows PE driver-pack page](https://ftp.ext.hp.com/pub/caps-softpaq/cmit/HP_WinPE_DriverPack.html).
HP distributes the pack as a SoftPaq executable. Follow its extraction UI, or
use the supported silent extraction form where applicable:

```text
spxxxxx.exe /s /e /f "<destination folder>"
```

Prepare the extracted folder, not the SoftPaq executable.

### Other manufacturers

Extract the manufacturer's WinPE driver package to a directory containing its
INF files, choose a unique stable package ID such as `lenovo-winpe`, and run the
same preparation script. The package manifest retains the manufacturer,
version, source page, preparation time, INF count, archive size, and SHA-256
evidence that the UI and service will report.

Do not use a full Windows model-driver pack as a substitute for a WinPE pack
unless its manufacturer documents that use. WinPE normally needs only storage,
network, and other boot-critical drivers.
