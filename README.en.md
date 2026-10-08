English | [简体中文](README.md)

# AppHopper

**App-level `Alt+Tab` for Windows, styled after PowerToys Window Hopper.**

Two shortcuts, two distinct jobs — exactly like macOS:

| Shortcut | Switches between | Behavior |
|---|---|---|
| `Alt+Tab` | **applications** | one entry per app, MRU order; sibling windows of the focused app never appear, so `Alt+Tab` always lands on a *different* app |
| ``Alt+` `` | **windows of the focused app** | handled by [PowerToys Window Hopper](https://learn.microsoft.com/en-us/windows/powertoys/window-hopper) (or any per-app cycler) |

No more "walking through" five Explorer windows to reach the browser: `Alt+Tab` jumps straight to the next *app*, ``Alt+` `` cycles inside the current one.

## 1.2

- Adopt Window Hopper's foreground model: both overlay layers use `WS_EX_NOACTIVATE`. Showing, refreshing and cancelling do not claim foreground; low-level hooks post input to the message window.
- Hide the overlay before committing, attach briefly to the current foreground thread and the target thread when distinct, then call `SetFocus(saved target focus) → BringWindowToTop → SetForegroundWindow` and detach. Cross-app switching cannot assume a shared input queue. Capture the target's child focus before attaching rather than always focusing its top-level window. Long-held selection required restoring focus first in live checks, unlike Window Hopper's same-app call order. Remove host acquisition, deferred handback and the old `AllowSetForegroundWindow` path.
- Retain app grouping, MRU, live thumbnails, mouse selection and asynchronous restoration of minimized windows. Window Hopper's per-app window grouping is not copied.
- Synchronize with the source input window using bounded `WM_NULL` before activation; use the CoreWindow for a UWP source. A UWP queue attachment may return `ERROR_ACCESS_DENIED`; that alone does not veto activation. The actual foreground landing determines success.
- 21 self-tests, including foreground preservation across overlay display and hiding. `WS_EX_NOACTIVATE` does not prohibit explicit activation and is not proof of zero flashing. Beta sections below are historical, not constraints on 1.2.
- Final alternating input-desktop A/B, two rounds per version: beta8 had 39 sessions, 0 failures and 23 in-session flash notifications; 1.2 had 40 sessions, 0 failures and 0 in-session flashes. Flashes from the harness raising windows outside sessions are excluded.
- All 20 switches between two independent apps selected the correct target and delivered actual text input to its editor; a minimized target also received input after restoration. Live overlay checks passed held-Alt reverse selection, Tab/wheel selection, card clicks, Escape with Alt held, outside-click cancellation and 10 quick switches. A screenshot confirmed headers, selection borders and live previews.
- These are local, injected-input samples, not a guarantee of zero failures or flashes for every physical keyboard, game, UWP scenario, hung window or mixed-DPI multi-monitor setup.

## 1.1.2beta1

- Fix lost `Alt+Tab` on startup failure: failed message posts pass through, while aborted startup replays native input using a dedicated `SendInput` tag. Matching `Tab` releases are consumed; physical `Alt` releases always pass through.
- If disable, commit or a failed post forwards a held `Tab` repeat, its release also passes through so Windows can clear the key state.
- Honor the first `Alt+Shift+Tab` and its selected page. Partial input insertion releases only keys actually introduced by the replay and still held.
- Redact window titles and executable names by default. `--log-verbose` alone enables detailed diagnostics. Complete UTF-8 records are limited to 8 MiB.
- Remove F24 permission-key injection and input-queue attachment to activation targets. Restore minimized targets asynchronously and retain actual foreground landing checks.
- Check autostart paths, reparse points, file and ancestor ACLs. Reject locations writable by untrusted identities and report registration failure instead of marking the menu enabled.

This beta does not guarantee elimination of taskbar flashing. Regression tests cover input, logging and autostart protection; isolated-desktop smoke runs do not replace rapid switching, game or UWP checks on the input desktop.


## 1.1.2beta6

## 1.1.2beta8

- Fixed Foxmail being absent from the switcher entirely: its main frame (`TFoxMainFrm.UnicodeClass`) never calls `SetWindowText`, so its title is empty, and the eligibility rule treated "no title" as not switchable - the whole application was silently dropped, as if it were not installed. The rule now judges whether the window has a real on-screen size rather than whether it has a title; the hairline helpers this used to catch (tooltips, tray icons, input strips) are a few pixels and most were already excluded as toolwindows. Verified against a real enumeration: Foxmail appears, everything else stays a real application, nothing spurious got in.
- Also closed a latent hole: the desktop's real class is `#32769` (not `Progman`); it has no title, no TOOLWINDOW bit and full-screen size, and was previously excluded only because it happened to have an empty title. It is now excluded by class name explicitly.
- Regressions grew to 20.
- Historical correction: a successful activation can still generate a flash notification. Experiments did not establish that avoiding flashes requires accepting failed switches. Version 1.2 uses a non-activating overlay and measures failures and flashes separately.

- **Regression fix**: beta6 removed `AllowSetForegroundWindow`, which took switch failures from about 3% to about 28%. That call always returns ERROR_ACCESS_DENIED here and grants nothing by its documented meaning, so it reads like pure waste - but the few milliseconds it costs are what keep the host's foreground alive long enough for the handoff that follows. Alternating measurement: without it 8/20 and 7/20 failures; with it 1/20, 2/20 and 0/20. Restored, with the reason recorded at the call site: its value is timing, not permission, and it is not the same thing as the self-pid preflight that genuinely was wrong.
- Also confirmed the bounded `WM_NULL` barrier before the handoff must stay: removing it pins failures at 6/20, keeping it gives 1-4/20.
- beta6's other changes (message-chain exception containment, out-of-range click index guard, foreground handover on abort, hook-installation logging, drawing-resource safety on exception, owner-chain verdict reuse) showed no regression and are kept.
- Regressions grew to 19, adding boundary assertions for the handoff path.


- The message-handling chain now contains exceptions: `WndProc -> HandleAppMsg -> Commit` had no try/catch at all, so a throw from window enumeration, a cross-process title read or a COM call unwound out of the message loop and took the process with it. It is now caught centrally and ends the session safely; the `EnumWindows` callback is guarded too, because an exception there crosses the unmanaged frame.
- The index that arrives with a mouse click is treated as untrusted input: `WM_APP_COMMITAT`'s parameter was written straight into `_index` and `Commit()` immediately used it as `_apps[_index]`. It is range-checked now, and an invalid index is logged and ignored.
- `AbortSession` (every entry disappeared mid-cycle) used to just hide the overlay, leaving the foreground parked on one of our now-hidden windows with the keyboard going nowhere; it now shares `Cancel`'s handover path.
- Startup records whether the keyboard hook actually installed. A refused hook previously left the app running and completely unresponsive to Alt+Tab with no visible symptom.
- Each enumeration reuses the owner-chain verdict instead of re-querying the same root (DWM plus a virtual-desktop COM call) for every popup under it. Panel-up latency is unaffected (p50 59ms -> 61ms).
- Card drawing and the layered-window submission are separate methods now, so `GraphicsPath`, `StringFormat` and the memory DC are released on the exception path too; likewise the tray icon's arrow caps.
- Dead code removed: `AllowSetForegroundWindow` (it granted the target process while the call was ours, so it did nothing for this activation), `ShowWindow`, `GetClassLong`, `GetAncestor`, `FindWindowByClass`, and an unused `oldIndex`.
- Regressions grew from 14 to 18, adding out-of-range index, message-exception containment, icon independence and memoized eligibility - each verified by mutation testing (breaking the implementation fails the test).
- **Deliberately reverted**: review found that `Icon.FromHandle` does not take ownership and `Dispose` does not destroy, so icons leak slowly, one per executable. Fixing it costs an extra `Clone()` per icon fetch, and icon loading sits on the Alt+Tab startup path: measured, that raised switch failures from 0/25 to 10/25. The handle count is bounded (one per executable, reclaimed at process exit), so paying switch reliability for it is a bad trade - kept as is, with the reason recorded in the code.

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

`AppHopper.exe --self-test` or `self-test.bat` runs 21 regressions covering layout, logging, autostart protection, replay and hook routing, exception containment, eligibility and foreground preservation across overlay display/hiding. Tests run before the single-instance mutex and may coexist with the running switcher. Display checks briefly show non-activating windows; tests install no hooks and write no autostart registration. Exit code 0 means success.

`Start with Windows` requires a protected installation under `Program Files` or `Program Files (x86)`, without reparse points or file/ancestor ACLs permitting untrusted modification. HKCU Run registration does not bypass UAC; Windows may block elevated startup, so unattended launch is not guaranteed.

## How it works (short version)

- The keyboard hook consumes `Alt+Tab` only after posting succeeds and consumes its matching `Tab` release. Aborted startup replays tagged input to the native switcher; physical `Alt` releases are never intercepted.
- Top-level windows are enumerated in Z-order, filtered by the classic Alt-Tab eligibility rules plus a current-desktop check, then grouped by process image path (UWP windows are attributed to their hosted app via the child `Windows.UI.Core.CoreWindow`).
- Live previews are `DwmRegisterThumbnail` composites rendered into an opaque rounded panel; the card chrome (headers, strokes, focus ring, page indicator) is drawn with GDI+ into a premultiplied-alpha DIB and composited with `UpdateLayeredWindow` — the same two-layer design as the PowerToys module it was ported from.
- Neither overlay actively claims foreground. Commit hides both layers, attaches briefly to the foreground and target queues and calls `SetFocus → BringWindowToTop → SetForegroundWindow`, detaching in reverse order in `finally`. Capture target focus before attaching and restore it only if it belongs to the selected window or a child; otherwise use the selected window as the focus entry point. Bounded `WM_NULL` synchronization and foreground checks observe asynchronous landing; cancellation no longer activates the source. Minimized targets restore through `ShowWindowAsync`. These budgets are not hard timeouts for every Win32 call: cross-thread calls remain subject to target responsiveness and Windows foreground restrictions.

## Acknowledgements

- [PowerToys Window Hopper (`AltWindowCycle`)](https://github.com/microsoft/PowerToys) — the overlay UI and layout are ported from this module (MIT). Go star PowerToys and check out the rest of it.
- [alt-tab-macos](https://github.com/lwouis/alt-tab-macos) — the original inspiration for app-level switching with previews.
- [window-switcher](https://github.com/sigoden/window-switcher) — prior art for ``Alt+` ``-style per-app cycling.

## License

[MIT](LICENSE) © 2026 informalgit. PowerToys-derived UI code remains MIT-licensed; the complete third-party notice is in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
