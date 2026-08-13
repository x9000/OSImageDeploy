# OSImageDeploy WiX installer

This SDK-style WiX project replaces the Visual Studio Installer project in the
solution. The previous `.vdproj` remains in the repository as a migration
reference, but is no longer loaded by `OSImageDeploy.slnx`.

## Building

Build a test installer:

```powershell
dotnet build OSImageDeploy.Installer\OSImageDeploy.Installer.wixproj `
    --configuration Debug
```

The unsigned test package is written to:

```text
OSImageDeploy.Installer\bin\Debug\OSImageDeploySuite.msi
```

Build an unsigned Release installer for repeatable packaging validation:

```powershell
dotnet build OSImageDeploy.Installer\OSImageDeploy.Installer.wixproj `
	--configuration Release `
	--property:EnableCodeSigning=false `
	--nodeReuse:false
```

The package is written to:

```text
OSImageDeploy.Installer\bin\Release\OSImageDeploySuite.msi
```

## Publish and signing behavior

- The desktop application, Windows service, and WinPE client are published as
  self-contained `win-x64` Release builds before the MSI is compiled.
- Publish output is staged under the current user's temporary directory and is
  recreated for every installer build.
- Builds are unsigned by default so packaging does not depend on one developer's
  certificate store.
- Supplying `CodeSigningCertificateThumbprint` signs Release application
  payloads and the finished MSI. The certificate and private key must be
  available in the current user's Personal certificate store:

```powershell
dotnet build OSImageDeploy.Installer\OSImageDeploy.Installer.wixproj `
	--configuration Release `
	--property:CodeSigningCertificateThumbprint=<thumbprint> `
	--nodeReuse:false
```

- Signing can be forced off even when a thumbprint is supplied by setting
  `EnableCodeSigning=false`.
- Signed installer validation must verify the project-owned binaries inside
  each publish directory as well as the MSI. Signing the MSI alone does not
  provide individual Authenticode signatures for extracted application and
  service files.

After a signed installer build, run the repeatable signature and ICE checks:

```powershell
.\Build\Test-SignedInstaller.ps1 `
	-CertificateThumbprint <thumbprint>
```

The script requires Microsoft MsiVal2 and its standard `darice.cub` rules. The
Windows SDK supplies the MsiVal2 installer beneath its versioned `bin\...\x86`
directory. The script validates a temporary copy of the MSI so the signed build
artifact remains untouched.

## Windows service policy

The MSI installs `OSImageDeploy.Service` as an automatic LocalSystem service
and starts it during installation. LocalSystem is required for the validated
physical-disk and WinPE operations that are kept out of the desktop process.

If the service process fails, Windows waits 120 seconds and restarts it after
the first and second failures. No automatic action is taken after a third
failure, which avoids an unbounded crash loop. The failure count resets after
one failure-free day.

The service is not configured for delayed automatic start because the desktop
client expects the local named-pipe endpoint to be available when a user signs
in and launches the application.

## USB operation status after a service restart

The service persists only the latest USB operation snapshot under:

```text
%ProgramData%\OSImageDeploy\Service\UsbMediaOperationStatus.json
```

The snapshot contains operation identity, progress, timestamps, and the final
result. It does not contain the selected disk, the original build request, or a
destructive-operation confirmation. A service restart therefore cannot resume
or authorize disk work. The service-state directory is restricted to LocalSystem
and administrators so a standard user cannot replace or spoof its contents.

If the service starts with a non-terminal snapshot, it records the operation as
failed with an `Interrupted` stage. The desktop application displays that result
and tells the user to inspect the USB target before starting again. A new build
still requires target rediscovery, safety validation, and explicit confirmation.

## Installed-product smoke test

Install or upgrade the MSI from an elevated administrator context. Silent
per-machine upgrades cannot prompt for elevation and fail with Windows Installer
error 1730 when started from a non-elevated console.

After installation, run the repeatable smoke test from a normal,
Medium-integrity PowerShell session:

```powershell
.\Build\Test-InstalledProduct.ps1 `
	-CertificateThumbprint <thumbprint> `
	-InstallerPath .\OSImageDeploy.Installer\bin\Release\OSImageDeploySuite.msi `
	-RunLiveServiceChecks `
	-LaunchUi
```

The script checks Installed Apps registration, service runtime and recovery
policy, installed signatures and OEM payloads, Medium-integrity named-pipe
access, and non-elevated UI startup. The live checks call service status,
read-only USB enumeration, active- and last-operation status, and WinPE cache
status. They also verify that unconfirmed USB-build and cache-clear requests are
rejected. They do not start a destructive USB build or modify the WinPE cache.

## Versioning and upgrades

The MSI uses the existing upgrade code so it can replace installations created
by the previous installer. Its three-part MSI version is generated from the
same project start date used by the applications.
