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
