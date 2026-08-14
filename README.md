# OSImageDeploy

OSImageDeploy creates bootable Windows deployment media through a non-elevated
desktop application and a privileged local Windows service. The UI communicates
with the service through gRPC over a secured Windows named pipe.

Production build, signing, MSI validation, installed-product testing, and
release steps are documented in [docs/RELEASE.md](docs/RELEASE.md).
Completed release-level hardware tests are recorded separately in
[docs/VALIDATION.md](docs/VALIDATION.md).
Release builds also require the externally maintained Dell and HP WinPE driver
archives described there; CI uses non-deployable placeholders and never
publishes its MSI.
