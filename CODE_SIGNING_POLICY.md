# Code signing policy

**Free code signing provided by [SignPath.io](https://signpath.io), certificate by
[SignPath Foundation](https://signpath.org).**

RightClickMenuManager uses free code signing for open-source projects through SignPath Foundation.
This page describes the signing policy, project roles, build provenance, and privacy commitments
required by the programme.

## Project and license

- Project: RightClickMenuManager
- Source repository: https://github.com/eric-thy/right-click-menu-manager
- License: MIT, an OSI-approved license, without commercial dual licensing
- All source code and build scripts are maintained in this repository
- No proprietary components are included; the build uses only the Windows in-box .NET Framework
  compiler and Windows APIs

## Team roles

| Role | Member |
| --- | --- |
| Author / committer | [@eric-thy](https://github.com/eric-thy) |
| Reviewer | [@eric-thy](https://github.com/eric-thy) |
| Approver | [@eric-thy](https://github.com/eric-thy) |

This is currently a single-maintainer project. Each release is manually reviewed and approved by the
owner before a signing request is submitted. Contributions from other people are merged only after
review; the author and approver roles must remain held by a project member with multi-factor
authentication enabled on GitHub and SignPath. MFA is required for all accounts with commit or
signing access.

## Build provenance and signing

- Release executables are built from the source in this repository.
- The canonical build is `build.ps1`, which uses the Windows in-box C# compiler and embeds the XAML
  and icon resources into a single executable.
- The GitHub Actions workflow in `.github/workflows/build.yml` reproduces the build on
  `windows-latest`.
- Only artifacts produced from this source are submitted for signing.
- Every signing request is approved manually; signing secrets are never committed to the repository.
- Signed artifacts are published in GitHub Releases.

## What is modified on a user's computer

The application is a context-menu manager. When the user explicitly toggles a menu item it may:

- add `LegacyDisable` to a classic shell verb;
- add a CLSID to the per-user `Shell Extensions\Blocked` value list;
- rename a `ShellNew` key to `ShellNew.__cmm_disabled`;
- move a SendTo shortcut into the application's local backup directory; or
- create a user-scoped custom verb.

All changes are reversible through the application's journal. The app does not silently change
settings, install services or drivers, or uninstall software. See the README and [PRIVACY.md](PRIVACY.md).

## Privacy

The program does not transfer any information to other networked systems unless specifically
requested by the user. It does not collect telemetry or personal data. The full privacy policy is in
[PRIVACY.md](PRIVACY.md).

## Reporting a concern

If you believe a signed artifact violates this policy, open a security report in this repository or
contact the maintainer through GitHub. The SignPath Foundation certificate can be revoked if a
project violates its terms.
