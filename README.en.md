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


## 1.1.2beta5

- Fixed the taskbar blink when the switcher is closed with ESC while Alt is still held: `Cancel` used to hand the foreground back to the source window mid-gesture. Releasing the foreground in the middle of the Alt+Tab gesture is recorded by Windows as a foreground ownership change, and the blink comes from exactly that. The handover is now deferred until Alt is released and completed by the watchdog (within 30ms); if the foreground has already returned by then - to the system or because the user moved on - nothing is forced.
- While claiming the foreground the host is now parked at the source window's position instead of the origin, so a source-sized host window never flashes in the top-left corner during the session.
- Input-desktop smoke: three consecutive "hold Alt + ESC to cancel" gestures produced zero flashes, and the foreground returned on its own within 50-70ms of the Alt release.
- **Not fixed**: with an elevated console as the switch target, roughly 25% of commits are still refused by Windows. A controlled experiment rules out the host's position (keeping the previous position and parking on the source window refuse at the same rate); the failures concentrate on particular target windows and are unrelated to the ESC fix. This needs separate analysis.

## 1.1.2beta4

- **Root cause fixed: the host must have a real size while claiming the foreground.** The host was shown as a zero-sized window. It does become `GetForegroundWindow()`, but Windows then refuses to let it hand the foreground on: every `SetForegroundWindow` issued from the session was rejected, and the rejection is exactly what flashes the target's taskbar button. The log matched precisely - `activation foreground queue flags=0x0 focus=0x0`, `accepted=False`, while the host HWND was the foreground window. The host is now shown at the source window's size (with `SWP_NOZORDER`); the real panel geometry is still applied once the layout is computed.
- Activation no longer gives up after a single refused `SetForegroundWindow`: while the host still owns the foreground it falls back to an input-queue handoff as the last resort, and never re-requests afterwards (retrying only adds flashes one for one). A bounded `WM_NULL` sync before the handoff keeps a hung window from being treated as activatable.
- A minimized target is now restored and then activated: `SW_RESTORE` is asynchronous, and activating while the window is still minimized is refused (again a flash without coming up). The commit waits, bounded at 250ms, for it to leave the minimized state.
- `BringWindowToTop` is deliberately not used: it bypasses the foreground lock and is precisely what makes a window flash without coming up.
- Input-desktop smoke (same script, same foreground target, 25 Alt+Tab gestures): 11 successes, 19 failures and 44 flashes before the fix; after it **25/25 succeeded, 0 failures, 0 flashes**, with `SetForegroundWindow` refusals down from 34 to 0. This does not cover physical hardware input, games or UWP.

## 1.1.2beta3

- Before committing, require only that our host still holds the foreground. The previous build also preflighted this process with `AllowSetForegroundWindow`, but that API accepts only `ASFW_ANY` or *another* process id and returns ERROR_ACCESS_DENIED for our own: 26 of 124 commits on this machine were vetoed by that preflight, so `SetForegroundWindow` was never called at all - the "sometimes Alt+Tab does not switch" symptom. The preflight is gone; async restore, the single target request, bounded `WM_NULL` synchronization and the real-foreground check are unchanged, and `SwitchToThisWindow` is still not used.
- The card chrome built one `Font` per card; it now builds one per repaint. Per-window `skip` diagnostics moved behind `--log-verbose`. Both sit on the `Alt+Tab` keypress path, and the latter used to flush roughly 900 lines to disk per session, exhausting the 8 MiB log cap within minutes.
- The tests now live in `AppHopper.cs` and the separate `tests/` project is gone: `AppHopper.exe --self-test` and `self-test.bat` run the same 14 regressions.
- Windows 10 input-desktop A/B (same script, same foreground target, 30 Alt+Tab gestures each): 11 successes / 19 failures before, 21 successes / 9 failures after, with the same failure signature in the log (target `SetForegroundWindow` rejected after the host claimed the foreground, or the host never got it). Panel-up latency fell from a p50 of 76ms to 57-67ms warm, and per-session logging from about 900 lines to 12. The residual failures correlate with using an elevated console as the switch target, which is a test-environment artifact and not settled. This does not cover physical hardware input, games or UWP.

## 1.1.2beta2

- Capture the original foreground, then claim a zero-sized, activatable, taskbar-free host before enumeration and rendering. Check the current foreground window's responsiveness with a 50ms timeout, briefly join its input queue to activate our host, and detach in `finally` before proceeding. Do not make a rejected background activation first or attach the target thread.
- Keep WinForms and native visibility synchronized for both layers. Own the card chrome with the host so titles, card borders and selection remain above the activated thumbnail host.
- `--log` records host acquisition, queue detachment, target acceptance and read-only `HSHELL_FLASH` HWNDs. Startup reports `shell flash observer=True; activation=foreground-handoff`. Failed observer registration, a full log or an unwritable log invalidates a no-notifications conclusion.
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

`AppHopper.exe --self-test` runs the whole suite; `self-test.bat` just compiles it to a temporary directory and calls it. The tests live in `AppHopper.cs` - there is no separate test project: layout/paging/cropping, single-monitor centring, log redaction and the UTF-8 cap, behaviour after a log stream fails, autostart path and ACL checks, native replay not disturbing the physical modifiers, hook pass-through/swallow/repeat/replay-tag routing, and refusing to activate without foreground ownership. The self-test runs ahead of the single-instance mutex, so it works while the switcher is running; it installs no hooks, changes no foreground, writes no autostart registration, and exit code 0 means success.

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
