# Privacy policy

RightClickMenuManager is a local Windows desktop utility.

## Data collection

- The application does not collect telemetry, analytics, crash reports, or usage data.
- It does not make network requests. It does not contact the author or any third party.
- It has no account system and does not ask for personal information.

## Local data

The application reads local registry keys, installed Appx package manifests, the SendTo directories,
and file metadata. When the user changes a menu item, it writes a local journal so the change can be
undone. These files are stored under:

```
%LOCALAPPDATA%\RightClickMenuManager\
```

The directory contains only the change history (`history.xml`), optional registry backups, and
shortcuts moved out of the SendTo directory. It is never uploaded. The user can delete it after using
**Restore all**.

## System modifications

Changes are made only in response to an explicit toggle or form submission by the user. The app
writes reversible registry values and local files; see the project README for the exact mechanisms.
It does not install services, drivers, scheduled tasks, or startup entries.

## Third-party components

The application is built against the .NET Framework and Windows APIs that are part of Windows. It
does not bundle third-party libraries that collect data.

## Contact

Open an issue in this repository for privacy questions.

_Last updated: 2026-11-18._
