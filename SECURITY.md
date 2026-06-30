# Security Policy

KingdomMod loads code into the Kingdom Two Crowns process via MelonLoader and
its installer builds DLLs locally and requests a Windows Defender exclusion.
Given that, please report security issues privately rather than as a public
issue — for example:

- A way for a malicious data pack, mod, or `.msi` argument to execute code
  outside the intended scope.
- A flaw in the installer's Defender-exclusion or download/build steps that
  could be abused.
- Any other vulnerability in the loader, SDK, or installer.

## Reporting a vulnerability

Use GitHub's private reporting for this repository:
[Report a vulnerability](https://github.com/FredApps/KingdomMod/security/advisories/new)
(Security tab -> "Report a vulnerability"). This opens a private advisory
visible only to maintainers until a fix is ready.

Please don't open a public issue for security reports.

## Supported versions

Only the latest released version on the
[Releases page](https://github.com/FredApps/KingdomMod/releases) is
supported. Please update before reporting, if possible.
