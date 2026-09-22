English | [简体中文](README.md)

# AppHopper

**App-level `Alt+Tab` for Windows, styled after PowerToys Window Hopper.**

Two shortcuts, two distinct jobs — exactly like macOS:

| Shortcut | Switches between | Behavior |
|---|---|---|
| `Alt+Tab` | **applications** | one entry per app, MRU order; sibling windows of the focused app never appear, so `Alt+Tab` always lands on a *different* app |
| ``Alt+` `` | **windows of the focused app** | handled by [PowerToys Window Hopper](https://learn.microsoft.com/en-us/windows/powertoys/window-hopper) (or any per-app cycler) |

No more "walking through" five Explorer windows to reach the browser: `Alt+Tab` jumps straight to the next *app*, ``Alt+` `` cycles inside the current one.

## Highlights

- **True app-level `Alt+Tab`** — windows are grouped by process, ordered by Z-order (MRU). The group's representative is its most recently used window, so returning to an app puts you back where you left it.
- **Window Hopper-style UI** — the overlay is ported from PowerToys' `AltWindowCycle` module: WinUI-style rounded cards, per-card icon + title header, the two-ring accent focus outline, light/dark theme following the OS, paging with a page indicator, and **no background dimming**.
- **Live DWM thumbnails** — real-time composite previews (the same mechanism as taskbar peek), center-cropped to the card ratio so nothing is stretched.
- **Full mouse support** — click a card to switch, click anywhere outside the panel to cancel, mouse wheel to cycle. (`Esc` also cancels.)
- **Virtual-desktop aware** — only windows on the *current* desktop are listed (via the public `IVirtualDesktopManager`), and switching never yanks windows across desktops.
- **Low intrusion** — the switcher is pure floating UI: it never changes another window's styles, visibility, ownership or taskbar attributes. If fewer than two candidate apps are available, it replays that one `Tab` to Windows' native switcher and leaves no cleanup state behind.
- **Per-monitor, DPI-aware** — the panel is centered on the monitor of the foreground window, scaled by that monitor's DPI, capped at 6 columns with automatic paging.
- **Single-file, zero-dependency** — one C# source that builds with the compiler already shipped in Windows. No installer, no runtime to install, green portable exe.

## Build

1. **The easiest route: double-click `build.bat` in the repository** — it terminates any running instance and embeds `app.manifest` so the resulting exe requests administrator privileges on launch. If an elevated instance is already running, quit it from the tray first; the locked output file would break the build.
2. Or run the compiler manually:
    ```bat
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe -platform:anycpu -optimize+ -win32manifest:app.manifest ^
      -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll ^
      -out:AppHopper.exe AppHopper.cs
    ```
Works on Windows 10 and 11.

## Usage

Run `AppHopper.exe` — a tray icon appears (right-click: *Enabled*, *Start with Windows*, *Exit*). Then:

| Input | Action |
|---|---|
| `Alt+Tab` | open the switcher, preselect the next app |
| hold `Alt`, tap `Tab` / `Shift+Tab` | cycle forward / backward |
| release `Alt` | switch to the selected app (minimized windows are restored) |
| `Esc` / click outside the panel | cancel |
| click a card | switch to that app immediately |
| mouse wheel | cycle |

Everything about the app list is computed on the fly each time the switcher opens; there is nothing to configure. Diagnostics: launch with `--log`; the log is capped at 8 MiB and redacts window titles and executable names by default. Use `--log-verbose` for full enumeration details. If the Windows switcher still appears, `hotkey: alt+tab -> start`, `start aborted:`, and `alt+tab fallback:` distinguish a hook miss from an abort and the intentional native fallback. `AppHopper.exe --self-test` (or double-click `self-test.bat`) runs the pure-logic regression checks and exits 0 on success. `Start with Windows` is allowed only when the executable is installed under a protected `Program Files` directory; because the program requires administrator rights, Windows may show UAC at logon.

## How it works (short version)

- The low-level keyboard hook swallows `Alt+Tab`/`Esc` during a switcher session. If startup conditions are not met, it hands that one `Tab` back to Windows' native switcher; all other keys pass through untouched.
- Top-level windows are enumerated in Z-order, filtered by the classic Alt-Tab eligibility rules plus a current-desktop check, then grouped by process image path (UWP windows are attributed to their hosted app via the child `Windows.UI.Core.CoreWindow`).
- Live previews are `DwmRegisterThumbnail` composites rendered into an opaque rounded panel; the card chrome (headers, strokes, focus ring, page indicator) is drawn with GDI+ into a premultiplied-alpha DIB and composited with `UpdateLayeredWindow` — the same two-layer design as the PowerToys module it was ported from.
- Activation uses the classic `AttachThreadInput` foreground handoff; minimized windows are restored first.

## Acknowledgements

- [PowerToys Window Hopper (`AltWindowCycle`)](https://github.com/microsoft/PowerToys) — the overlay UI and layout are ported from this module (MIT). Go star PowerToys and check out the rest of it.
- [alt-tab-macos](https://github.com/lwouis/alt-tab-macos) — the original inspiration for app-level switching with previews.
- [window-switcher](https://github.com/sigoden/window-switcher) — prior art for ``Alt+` ``-style per-app cycling.

## License

[MIT](LICENSE) © 2026 informalgit. PowerToys-derived UI code remains MIT-licensed; the complete third-party notice is in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
