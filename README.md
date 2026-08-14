# OSImageDeploy

OSImageDeploy creates bootable Windows deployment media through a non-elevated
desktop application and a privileged local Windows service. The UI communicates
with the service through gRPC over a secured Windows named pipe.

Production build, signing, MSI validation, installed-product testing, and
release steps are documented in [docs/RELEASE.md](docs/RELEASE.md).
Completed release-level hardware tests are recorded separately in
[docs/VALIDATION.md](docs/VALIDATION.md).
Preparation of operator-supplied OEM and custom WinPE driver packages is
documented in [docs/WINPE-DRIVERS.md](docs/WINPE-DRIVERS.md).
The current 1.83.1056 build still requires the externally maintained Dell and
HP archives described in the release procedure. CI uses non-deployable
placeholders and never publishes its MSI. A subsequent release will replace
that packaging with the external driver-package workflow.
