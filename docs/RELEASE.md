# Release procedure

This procedure creates and validates a release candidate from an exact commit.
It deliberately separates repeatable CI checks from hardware-token signing and
installed-product validation.

## 1. Choose the release commit

Start from a clean, synchronized `main` branch. Record the commit before any
build begins:

```powershell
git checkout main
git pull --ff-only
git status --short --branch
git rev-parse HEAD
```

Do not build a release from a working tree containing uncommitted changes.

## 2. Run the unsigned CI-equivalent checks

Use one timestamp throughout the build so every project and the MSI receive the
same version:

```powershell
$buildTimestampUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffffff')

dotnet restore OSImageDeploy.slnx

dotnet build OSImageDeploy.slnx `
	--configuration Release `
	--no-restore `
	--property:EnableCodeSigning=false `
	--property:BuildTimestampUtc=$buildTimestampUtc `
	--nodeReuse:false

dotnet run --project OSImageDeploy.Engine.Tests\OSImageDeploy.Engine.Tests.csproj `
	--configuration Release --no-build

dotnet run --project OSImageDeploy.Service.Tests\OSImageDeploy.Service.Tests.csproj `
	--configuration Release --no-build

.\Build\Test-InstallerPackage.ps1 `
	-InstallerPath .\OSImageDeploy.Installer\bin\x64\Release\OSImageDeploySuite.msi `
	-ExpectedSignatureStatus NotSigned
```

These checks are automated by `.github/workflows/ci.yml`. They do not start the
Windows service or perform a destructive USB operation.

## 3. Build and validate the signed MSI

Connect and unlock the hardware token. Supply the certificate thumbprint from
the build environment; never store it, a PIN, or private certificate material
in the repository.

```powershell
dotnet build OSImageDeploy.Installer\OSImageDeploy.Installer.wixproj `
	--configuration Release `
	--property:CodeSigningCertificateThumbprint=<thumbprint> `
	--property:BuildTimestampUtc=$buildTimestampUtc `
	--nodeReuse:false

.\Build\Test-SignedInstaller.ps1 `
	-CertificateThumbprint <thumbprint>

.\Build\Test-InstallerPackage.ps1 `
	-ExpectedSignatureStatus Valid
```

`Test-SignedInstaller.ps1` verifies every project-owned publish payload and the
MSI, including timestamp presence, then runs Microsoft MsiVal2 ICE validation.

## 4. Install and test the release candidate

Install or upgrade from an elevated administrator context. Save a verbose MSI
log outside the repository.

Validate these installed-product behaviours separately from development-build
testing:

- installed product version matches the MSI;
- `OSImageDeploy.Service` is Running, Automatic, and LocalSystem;
- installed project-owned executables and DLLs retain valid timestamped
  signatures;
- the live non-destructive service suite passes from a normal Medium-integrity
  user context;
- the installed desktop UI opens without elevation, connects to the service,
  reports WinPE cache state, enumerates USB targets, and closes normally;
- service recovery configuration still reports two delayed restart actions;
- upgrade, repair, uninstall, and downgrade rejection behave as documented.

The live suite is:

```powershell
dotnet run --project OSImageDeploy.Service.Tests\OSImageDeploy.Service.Tests.csproj `
	--configuration Release --no-build -- --live
```

It performs read-only USB enumeration and confirmation-guard checks. It does
not start USB media creation.

## 5. Record and publish the artifact

Record the exact commit, MSI version, signer, timestamp, and SHA-256 hash:

```powershell
$msi = 'OSImageDeploy.Installer\bin\Release\OSImageDeploySuite.msi'
git rev-parse HEAD
Get-FileHash -LiteralPath $msi -Algorithm SHA256
Get-AuthenticodeSignature -LiteralPath $msi |
	Select-Object Status, SignerCertificate, TimeStamperCertificate
```

Create a release tag only after installed-product validation passes. Build and
publish from that exact tag. Attach the signed MSI and its SHA-256 value to a
GitHub prerelease before promoting it to a full release.

## 6. Destructive and boot validation

A complete USB creation test is a separate validation class. Before starting
one, identify the exact physical disk and obtain explicit confirmation. Record:

- target model, serial identity, disk number, size, and USB bus;
- service/UI versions and MSI hash;
- successful completion status;
- VMware boot result;
- physical-machine boot result, hardware model, storage visibility, and network
  availability.

Never infer permission for this step from approval to build, sign, install, or
release the software.

## Rollback

If an upgrade fails, preserve the MSI log and confirm whether Windows Installer
restored the previous product. Do not manually delete the service or installed
files unless normal MSI repair/uninstall has been exhausted and the exact state
has been recorded.

To return to an earlier release, uninstall the newer product normally and then
install the earlier signed MSI. The major-upgrade policy intentionally rejects
installing an older MSI directly over a newer installed version.
