# Security policy

OSImageDeploy deliberately separates its normal-user desktop from a privileged
Windows service. Reports that could weaken the named-pipe boundary, physical
disk validation, destructive-operation confirmation, package validation, code
signing, or unattended-deployment safeguards are particularly important.

## Reporting a vulnerability

Please do not open a public issue for a suspected vulnerability or include
exploit details in a public discussion.

Use GitHub's **Report a vulnerability** facility on the repository Security
page. If that facility is unavailable, use the contact route at
[x9000.com](https://x9000.com) and state that the message concerns a private
OSImageDeploy security report.

Include the affected version or commit, the observed behaviour, reproduction
steps, potential impact, and any suggested mitigation. Do not include private
keys, access tokens, personal data, Windows images, OEM packages, or other
third-party material that you are not authorised to share.

The project will acknowledge a usable report, assess it, and coordinate a fix
and disclosure where appropriate. No particular response or remediation time
is guaranteed.

## Supported versions

Security fixes target the latest published release. Older releases may be
removed or superseded and should not be assumed to receive fixes.

## Safe research boundaries

Security research must use systems, accounts, data, and removable media that
you own or are explicitly authorised to test. Never exercise destructive disk
operations against a device containing required data. The source-visible
licence does not authorise access to third-party systems or data.
