English | [简体中文](README.md)

# AppHopper

**App-level `Alt+Tab` for Windows, styled after PowerToys Window Hopper.**

Two shortcuts, two distinct jobs — exactly like macOS:

| Shortcut | Switches between | Behavior |
|---|---|---|
| `Alt+Tab` | **applications** | one entry per app, MRU order; sibling windows of the focused app never appear, so `Alt+Tab` always lands on a *different* app |
| ``Alt+` `` | **windows of the focused app** | handled by [PowerToys Window Hopper](https://learn.microsoft.com/en-us/windows/powertoys/window-hopper) (or any per-app cycler) |

No more "walking through" five Explorer windows to reach the browser: `Alt+Tab` jumps straight to the next *app*, ``Alt+` `` cycles inside the current one.

## 1.1.2beta1

- Fix lost `Alt+Tab` on startup failure: failed message posts pass through, while aborted startup replays native input using a dedicated `SendInput` tag. Matching `Tab` releases are consumed; physical `Alt` releases always pass through.
- If disable, commit or a failed post forwards a held `Tab` repeat, its release also passes through so Windows can clear the key state.
- Honor the first `Alt+Shift+Tab` and its selected page. Partial input insertion releases only keys actually introduced by the replay and still held.
- Redact window titles and executable names by default. `--log-verbose` alone enables detailed diagnostics. Complete UTF-8 records are limited to 8 MiB.
- Remove F24 permission-key injection and input-queue attachment to activation targets. Restore minimized targets asynchronously and retain actual foreground landing checks.
- Check autostart paths, reparse points, file and ancestor ACLs. Reject locations writable by untrusted identities and report registration failure instead of marking the menu enabled.

This beta does not guarantee elimination of taskbar flashing. Regression tests cover input, logging and autostart protection; isolated-desktop smoke runs do not replace rapid switching, game or UWP checks on the input desktop.

## 1.1.2beta2

- Capture the original foreground, then claim a zero-sized, activatable, taskbar-free host before enumeration and rendering. Check the current foreground window's responsiveness with a 50ms timeout, briefly join its input queue to activate our host, and detach in `finally` before proceeding. Do not make a rejected background activation first or attach the target thread.
- Keep WinForms and native visibility synchronized for both layers. Own the card chrome with the host so titles, card borders and selection remain above the activated thumbnail host.
- Before committing, require our host to remain foreground and preflight this process with `AllowSetForegroundWindow`. Without eligibility, do not restore or request the target. Otherwise restore minimized targets asynchronously, request `SetForegroundWindow` once, synchronize with bounded `WM_NULL`, and verify the actual foreground. Remove the `SwitchToThisWindow` fallback.
- `--log` records host acquisition, queue detachment, permission preflight, target acceptance and read-only `HSHELL_FLASH` HWNDs. Startup reports `shell flash observer=True; activation=foreground-handoff`. Failed observer registration, a full log or an unwritable log invalidates a no-notifications conclusion.
- Windows 10 input-desktop smoke: 20 system-input-injected Alt+Tab gestures between Orca and ZCode, all handled by AppHopper without native fallback; every target request accepted, Alt released after every gesture, zero target Shell flash notifications, and the starting application restored. This does not cover every application, physical hardware input, games or UWP.

## Highlights

- **True app-level `Alt+Tab`** — windows are grouped by process, ordered by Z-order (MRU). The group's representative is its most recently used window, so returning to an app puts you back where you left it.
- **Window Hopper-style UI** — the overlay is ported from PowerToys' `AltWindowCycle` module: WinUI-style rounded cards, per-card icon + title header, the two-ring accent focus outline, light/dark theme following the OS, paging with a page indicator, and **no background dimming**.
- **Live DWM thumbnails** — real-time composite previews (the same mechanism as taskbar peek), center-cropped to the card ratio so nothing is stretched.
- **Full mouse support** — click a card to switch, click anywhere outside the panel to cancel, mouse wheel to cycle. (`Esc` also cancels.)
- **Virtual-desktop aware** — only windows on the *current* desktop are listed (via the public `IVirtualDesktopManager`), and switching never yanks windows across desktops.
- **Low intrusion** — no changes to other windows' styles, ownership or taskbar attributes; switching only requests restoration of a minimized target. Aborted startup replays that `Tab` to Windows' native switcher.
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

The app list is computed each time the switcher opens; there is nothing to configure. Launch with `--log` for an 8 MiB log that redacts window titles and executable names while retaining HWNDs, window classes and timing. Use `--log-verbose` for full enumeration details; inspect sensitive content before sharing it. `hotkey: alt+tab -> start`, `start aborted:`, and `alt+tab fallback:` identify message dispatch, startup failure and intentional fallback. Missing records alone do not prove a hook miss: the log may also be full or unwritable.

`AppHopper.exe --self-test` runs pure layout checks. `self-test.bat` compiles the current source, `tests/RegressionTests.cs` and `tests/ActivationTests.cs` into a temporary directory, runs input-state, log-boundary, ACL and real cross-thread synchronization regressions, then removes the outputs. Synchronization uses hidden windows on a non-input desktop. It does not replace the running exe, install hooks, change foreground focus or write an autostart registration. Exit code 0 means success.

`Start with Windows` requires a protected installation under `Program Files` or `Program Files (x86)`, without reparse points or file/ancestor ACLs permitting untrusted modification. HKCU Run registration does not bypass UAC; Windows may block elevated startup, so unattended launch is not guaranteed.

## How it works (short version)

- The keyboard hook consumes `Alt+Tab` only after posting succeeds and consumes its matching `Tab` release. Aborted startup replays tagged input to the native switcher; physical `Alt` releases are never intercepted.
- Top-level windows are enumerated in Z-order, filtered by the classic Alt-Tab eligibility rules plus a current-desktop check, then grouped by process image path (UWP windows are attributed to their hosted app via the child `Windows.UI.Core.CoreWindow`).
- Live previews are `DwmRegisterThumbnail` composites rendered into an opaque rounded panel; the card chrome (headers, strokes, focus ring, page indicator) is drawn with GDI+ into a premultiplied-alpha DIB and composited with `UpdateLayeredWindow` — the same two-layer design as the PowerToys module it was ported from.
- Activation hands off from our foreground host without injecting permission keys. Only acquisition of our host briefly joins the current foreground thread; the queues are detached before target activation. Minimized targets restore via `ShowWindowAsync`. A bounded `WM_NULL` synchronizes cross-queue activation before checking the actual foreground; see [Microsoft's explanation](https://devblogs.microsoft.com/oldnewthing/20161118-00/?p=94745/). The 50ms responsiveness check and 200ms/300ms synchronization/landing budgets are not hard timeouts for every Win32 call. The foreground thread can still hang between its check and queue attachment, and Windows foreground restrictions still apply.

## Acknowledgements

- [PowerToys Window Hopper (`AltWindowCycle`)](https://github.com/microsoft/PowerToys) — the overlay UI and layout are ported from this module (MIT). Go star PowerToys and check out the rest of it.
- [alt-tab-macos](https://github.com/lwouis/alt-tab-macos) — the original inspiration for app-level switching with previews.
- [window-switcher](https://github.com/sigoden/window-switcher) — prior art for ``Alt+` ``-style per-app cycling.

## License

[MIT](LICENSE) © 2026 informalgit. PowerToys-derived UI code remains MIT-licensed; the complete third-party notice is in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
