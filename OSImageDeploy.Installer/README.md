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

Build a production installer:

```powershell
dotnet build OSImageDeploy.Installer\OSImageDeploy.Installer.wixproj `
    --configuration Release
```

The signed production package is written to:

```text
OSImageDeploy.Installer\bin\Release\OSImageDeploySuite.msi
```

## Publish and signing behavior

- Both applications are published as self-contained `win-x64` Release builds
  before the MSI is compiled.
- Publish output is staged under the current user's temporary directory and is
  recreated for every installer build.
- Debug installer builds skip code signing so packaging work does not require
  the signing certificate.
- Release installer builds sign the application payloads and the finished MSI.
- Release signing requires the configured certificate and private key to be
  available in the current user's Personal certificate store.

## Versioning and upgrades

The MSI uses the existing upgrade code so it can replace installations created
by the previous installer. Its three-part MSI version is generated from the
same project start date used by the applications.
