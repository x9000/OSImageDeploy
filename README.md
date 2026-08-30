# OSImageDeploy

OSImageDeploy is a Windows deployment-media creation and WinPE deployment suite
developed by [x9000.com Consulting Services Limited](https://x9000.com). It is
designed to keep its interactive desktop in the normal user context while a
local Windows service performs narrowly defined privileged operations.

The desktop communicates with the service using gRPC over a secured local
Windows named pipe. Shared contracts, general workflow and safety policy, and
Windows-specific implementation are kept in separate projects so the
privileged boundary remains explicit and testable.

## What it does

- discovers USB deployment targets and rejects system, boot, unhealthy,
  offline, read-only, or otherwise unsuitable disks;
- creates or safely refreshes bootable WinPE media while preserving a compatible
  `BuildData` volume;
- installs the Microsoft ADK and WinPE add-on when required;
- prepares operator-supplied Dell, HP, and custom WinPE driver packages;
- injects selected WinPE drivers through the privileged service;
- deploys a selected Windows image and applicable device driver pack; and
- supports an explicitly enabled JSON-based unattended deployment workflow.

## Safety model

USB preparation can destroy data. A destructive operation is accepted only
after the service has rediscovered the selected physical disk, revalidated its
stable identity and safety properties, and received the required explicit
confirmation. Automatic Windows deployment does not weaken the USB-media
creation confirmation boundary.

Always maintain independent backups and confirm the exact physical target.
Successful testing on one enclosure, firmware, or computer model is not a claim
of universal compatibility.

## Requirements

- Windows 11 x64 for the installed desktop and service;
- a supported .NET 10 SDK and Visual Studio 2026 workload for development;
- administrator rights to install the MSI and Windows service; and
- internet access when Microsoft ADK, WinPE, or manufacturer packages must be
  downloaded.

Windows installation images, Microsoft ADK/WinPE content, OEM driver packages,
product keys, and code-signing credentials are not stored in this repository or
published by CI.

## Build and validation

Restore and run the portable Release checks with:

```powershell
dotnet restore OSImageDeploy.slnx

dotnet build OSImageDeploy.slnx `
	--configuration Release `
	--property:EnableCodeSigning=false `
	--nodeReuse:false

dotnet run --project OSImageDeploy.Engine.Tests\OSImageDeploy.Engine.Tests.csproj `
	--configuration Release --no-build

dotnet run --project OSImageDeploy.Service.Tests\OSImageDeploy.Service.Tests.csproj `
	--configuration Release --no-build
```

These checks do not replace signed-installer, installed-product, destructive
USB, virtual-machine boot, or physical-hardware validation.

## Documentation

Production build, signing, MSI validation, installed-product testing, and
release steps are documented in [docs/RELEASE.md](docs/RELEASE.md).
Completed release-level hardware tests are recorded separately in
[docs/VALIDATION.md](docs/VALIDATION.md).
Preparation of operator-supplied OEM and custom WinPE driver packages is
documented in [docs/WINPE-DRIVERS.md](docs/WINPE-DRIVERS.md).
Safe reuse of an existing OSImageDeploy USB layout is documented in
[docs/USB-REFRESH.md](docs/USB-REFRESH.md).
Opt-in unattended Windows deployment through the generated manual-by-default
JSON file is documented in
[docs/AUTOMATIC-DEPLOYMENT.md](docs/AUTOMATIC-DEPLOYMENT.md).
The desktop lists those packages through the Windows service and carries only
explicitly selected package IDs across the privileged boundary.
Current development builds no longer embed Dell or HP driver archives. Selected
operator-prepared packages are revalidated by the service and injected into the
WinPE image; builds with no selection include no optional OEM drivers.

## Licence, suggestions, and security

OSImageDeploy is proprietary source-visible software, not an open-source
project. The repository may be studied and evaluated under [LICENSE](LICENSE),
but it does not grant general permission to reuse, modify, redistribute, or
develop the software independently.

Suggestions and reproducible bug reports are welcome through GitHub Issues.
Unsolicited pull requests and source-code contributions are not accepted; see
[CONTRIBUTING.md](CONTRIBUTING.md). Report suspected vulnerabilities privately
using [SECURITY.md](SECURITY.md). Support expectations are described in
[SUPPORT.md](SUPPORT.md), and dependency attribution is recorded in
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
