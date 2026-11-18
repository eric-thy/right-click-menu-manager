# RightClickMenuManager

A portable Windows 11 context-menu manager written in C# / WPF (.NET Framework 4.8).
It inventories the explorer context menu from registry and Appx sources, then lets you
enable or disable each entry with a reversible switch.

The UI is in Chinese; the source and documentation are in English so that the project can be
reviewed and code-signed as open source.

## Features

- **Windows 11 modern menu**: reads every installed Appx `windows.fileExplorerContextMenus`
  registration and resolves the real menu title through `IExplorerCommand`. This covers items such
  as "Ask Copilot", "Open with Code", "Open project in ChatGPT", and third-party packaged commands.
- **Classic shell verbs**: scans `*`, `AllFilesystemObjects`, `Directory`,
  `Directory\Background`, `Drive`, `DesktopBackground`, `Folder`, `LibraryFolder`, `Network`,
  and `SystemFileAssociations` scopes.
- **Shell extensions**: enumerates `shellex\ContextMenuHandlers` and shows the CLSID, vendor, and
  DLL path.
- **New menu**: enumerates `.ext\ShellNew` entries.
- **Send To**: enumerates the per-user and all-user SendTo directories.
- **Per-file-type inspection**: discovers 200+ registered extensions and shows commands that are
  specific to the selected file type.
- **Custom entries**: create user-scoped verbs for files, directories, directory backgrounds, and drives.
- **Reversible operations**: every change is journaled; undo one change or restore every change made
  by this tool.
- **Accessibility**: all interactive controls have automation names.

## How disabling works

| Source | Reversible mechanism |
| --- | --- |
| Classic verb | Adds the `LegacyDisable` string value to the verb key |
| Modern command / shell extension | Adds the CLSID to the per-user `Shell Extensions\Blocked` key |
| New menu entry | Renames `ShellNew` to `ShellNew.__cmm_disabled` |
| Send To item | Moves the shortcut into the application backup directory |
| Custom entry | Creates a user-scoped key; can be deleted with a registry backup |

No software is uninstalled and no original command is deleted.

## Requirements

- Windows 11 x64
- .NET Framework 4.8 (included with Windows 11)
- No installer, no third-party runtime, no network access

The application runs as a standard user. Entries under `HKEY_LOCAL_MACHINE`, the all-user SendTo
directory, or system-level block lists require administrator rights; the app offers a UAC restart
when such an entry is toggled.

## Build

Windows PowerShell 5.1 and the in-box C# compiler are sufficient. No NuGet packages or SDK are
required.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Output:

```
dist\右键菜单管理器.exe
```

The GitHub Actions workflow runs the same build on `windows-latest` and uploads the executable as a
workflow artifact. Signed releases are produced only after a SignPath signing request is approved.

## Usage

1. Run `右键菜单管理器.exe`.
2. Select a scope on the left, or search for a menu title.
3. Toggle the switch on a row.
4. If Explorer still shows the old menu, open **Settings & Backup** and restart Explorer.
5. Use the toast **Undo** action or the backup page to reverse a change.

System entries show a lock icon. The app writes only the documented reversible values above.

## Uninstall

The application is portable and does not install services or drivers.

1. Open **Settings & Backup** and choose **Restore all** to undo changes recorded by this tool.
2. Delete the executable.
3. Optionally delete `%LOCALAPPDATA%\RightClickMenuManager` (history, registry backups, SendTo backups).

## Privacy

This program does not transfer any information to other networked systems unless specifically
requested by the user. It reads local registry keys and Appx manifests, and writes only local backup
and history files under `%LOCALAPPDATA%\RightClickMenuManager`. See [PRIVACY.md](PRIVACY.md).

## Code signing policy

**Free code signing provided by [SignPath.io](https://signpath.io), certificate by
[SignPath Foundation](https://signpath.org).**

The full policy, team roles, privacy statement, and build provenance are in
[CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md). Until a signed release is published, builds are
unsigned and Windows SmartScreen may ask the user to confirm.

## Scope and limitations

- The tool manages Explorer context-menu registrations; it does not change default-application
  associations or the "Open with" list.
- Third-party software using undocumented, driver-level, or non-registry injection may not be
  discoverable.
- Registry writes can be blocked by enterprise policy or security software.
- The UI and some strings are currently Chinese; localization contributions are welcome.

## License

MIT. See [LICENSE](LICENSE).

This project is not affiliated with Microsoft. Windows is a trademark of Microsoft Corporation.
