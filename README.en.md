English | [简体中文](README.md)

# AppHopper

**App-level `Alt+Tab` for Windows, styled after PowerToys Window Hopper.**

Two shortcuts, two distinct jobs — exactly like macOS:

| Shortcut | Switches between | Behavior |
|---|---|---|
| `Alt+Tab` | **applications** | one entry per app, MRU order; sibling windows of the focused app never appear, so `Alt+Tab` always lands on a *different* app |
| ``Alt+` `` | **windows of the focused app** | handled by [PowerToys Window Hopper](https://learn.microsoft.com/en-us/windows/powertoys/window-hopper) (or any per-app cycler) |

No more "walking through" five Explorer windows to reach the browser: `Alt+Tab` jumps straight to the next *app*, ``Alt+` `` cycles inside the current one.

## 1.3

### bug fix
- None.

### features
- Add `Get updates...` to the tray: download a stable GitHub release after confirmation, verify it, replace the executable in place and restart; attempt rollback on replacement or startup failure.


## Build

1. **The easiest route: double-click `build.bat` in the repository** — it terminates any running instance and embeds `app.manifest` so the resulting exe requests administrator privileges on launch. If an elevated instance is already running, quit it from the tray first; the locked output file would break the build.
2. Or run the compiler manually:
    ```bat
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe -platform:anycpu -optimize+ -win32manifest:app.manifest ^
      -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll ^
      -out:AppHopper.exe AppHopper.cs
    ```
Works on Windows 10 and 11.

## Get updates

Right-click the tray icon and choose `Get updates...`. Checking happens only on demand against GitHub's latest stable release. A newer version requires confirmation before download, replacement and restart. Repeated update requests are blocked during checking, confirmation and download. The switcher remains usable during download; no resident update service or separate server is required.

Downloads must match the GitHub asset's size, SHA-256 digest and executable version. Missing integrity metadata, network errors and incomplete downloads do not exit the running app. A temporary helper runs in an administrator/SYSTEM-only `ProgramData` directory. For another installation drive, it first creates and locks a protected staging directory beside the executable so replacement stays on one volume. Once preparation succeeds, the old process exits and the helper atomically replaces the original path and starts the new version. If startup is not confirmed within 15 seconds, it attempts to restore and restart the old version. A failed recovery reports the backup path for manual recovery.

The executable path and autostart registration remain unchanged; Enabled state and logging mode are preserved. Network paths and installation paths through reparse points are unsupported. Locked files or unsafe replacement report failure. The running temporary helper is scheduled for deletion at the next Windows reboot. Version 1.2 has no update menu: install an update-capable version manually once before using in-place updates.

## Acknowledgements

- [PowerToys Window Hopper (`AltWindowCycle`)](https://github.com/microsoft/PowerToys) — the overlay UI and layout are ported from this module (MIT). Go star PowerToys and check out the rest of it.
- [alt-tab-macos](https://github.com/lwouis/alt-tab-macos) — the original inspiration for app-level switching with previews.
- [window-switcher](https://github.com/sigoden/window-switcher) — prior art for ``Alt+` ``-style per-app cycling.

## License

[MIT](LICENSE) © 2026 informalgit. PowerToys-derived UI code remains MIT-licensed; the complete third-party notice is in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
