// AppHopper - macOS-AltTab-style application switcher for Windows.
// Alt+Tab is fully taken over: one entry per application (grouped by exe,
// Z-order MRU), live DWM thumbnails, click an entry to switch, click outside /
// press Esc to cancel, hold Alt and tap Tab to cycle, Shift+Tab reverses.
// Alt+` (PowerToys Window Hopper) and Win+Tab stay untouched.
//
// The switcher UI is a faithful port of PowerToys Window Hopper's overlay
// (src/modules/AltWindowCycle, MIT): two stacked windows - an opaque rounded
// panel that hosts the live DWM thumbnails, and an UpdateLayeredWindow chrome
// on top that draws the WinUI-style cards, headers, accent focus ring and page
// indicator. Theme (light/dark) and the accent color follow the OS. Only the
// floating UI is ever touched; no other window's styles, visibility, taskbar
// or virtual-desktop assignment are modified.

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Version resource: csc turns these into the PE's VS_VERSIONINFO (Explorer
// "Properties -> Details") without any build.bat change. Bump once per
// release - the tray tooltip and the startup log line read it back at
// runtime via AppVersion, so this is the single place a version lives.
[assembly: System.Reflection.AssemblyVersion("1.3.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.3.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.3")]

namespace AppHopper
{
    // ================= Win32 interop =================
    // Every P/Invoke declaration, Win32 constant, struct and COM import,
    // gathered into one place (the NativeMethods role in a multi-file
    // project) so the rest of the file is plain C#. Same single file,
    // so the csc one-liner in build.bat keeps working unchanged.
    static class NativeMethods
    {
        // ================= Win32 =================
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;
        public const uint LLKHF_UP = 0x80;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int VK_TAB = 0x09;
        public const int VK_MENU = 0x12;
        public const int VK_LMENU = 0xA4;
        public const int VK_RMENU = 0xA5;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint LLKHF_ALTDOWN = 0x20;
        public const uint INPUT_KEYBOARD = 1;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;
        public const int VK_SHIFT = 0x10;
        public const int VK_ESCAPE = 0x1B;
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_LAYERED = 0x00080000;
        public const uint GW_OWNER = 4;
        public const int GCLP_HICONSM = -34;
        public const int GCLP_HICON = -14;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public const int MDT_EFFECTIVE_DPI = 0;
        public const int SW_RESTORE = 9;
        public const uint SHGFI_ICON = 0x100;
        public const uint SHGFI_LARGEICON = 0x0;
        public const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
        public const int ICON_SMALL2 = 2, ICON_BIG = 1;
        public const uint WM_GETICON = 0x7F;
        public const uint SMTO_ABORTIFHUNG = 0x2;
        public const uint SMTO_ERRORONEXIT = 0x20;

        public const int WM_APP_START = 0x8000 + 1;
        public const int WM_APP_NEXT = 0x8000 + 2;
        public const int WM_APP_PREV = 0x8000 + 3;
        public const int WM_APP_COMMIT = 0x8000 + 4;
        public const int WM_APP_CANCEL = 0x8000 + 5;
        public const int WM_APP_COMMITAT = 0x8000 + 6;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public uint cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }

        // The mouse member fixes INPUT's union size/alignment on both x86/x64.
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public UIntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public UIntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public KEYBDINPUT keyboard;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public INPUTUNION data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DWM_THUMBNAIL_PROPERTIES
        {
            public uint dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            public bool fVisible;
            public bool fSourceClientAreaOnly;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFOW
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
        [DllImport("user32.dll")]
        public static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool MoveFileEx(string existing, string replacement, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path, uint access, uint sharing,
            IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint nCmd);
        [DllImport("user32.dll")]
        public static extern IntPtr GetLastActivePopup(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        // GetClassLongW truncates an HICON to 32 bits on x64; always use the
        // pointer-sized variant for icon handles.
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        public static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern IntPtr CopyIcon(IntPtr hIcon);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string text);
        [DllImport("user32.dll")]
        public static extern bool RegisterShellHookWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern bool DeregisterShellHookWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassNameW(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int maxCount);
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        // Needed to spot a fully transparent (opacity 0) layered window: such a
        // window is visible and uncloaked, but there is nothing to show and no
        // entry worth offering in the switcher.
        [DllImport("user32.dll")]
        public static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out int crKey, out byte bAlpha, out int dwFlags);
        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);
        [DllImport("dwmapi.dll")]
        public static extern int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr phThumbnailId);
        [DllImport("dwmapi.dll")]
        public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnailId, ref DWM_THUMBNAIL_PROPERTIES ptnc);
        [DllImport("dwmapi.dll")]
        public static extern int DwmUnregisterThumbnail(IntPtr hThumbnailId);
        [DllImport("dwmapi.dll")]
        public static extern int DwmQueryThumbnailSourceSize(IntPtr hThumbnail, out SIZE psize);
        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; }

        // ================= IVirtualDesktopManager (read-only use) =================
        [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b0f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IVirtualDesktopManager
        {
            [PreserveSig]
            int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out bool onCurrentDesktop);
        }

        [ComImport, Guid("9acda8ce-73d8-41a4-9373-4030c759a53e")]
        public class VirtualDesktopManagerClass { }
    }

    // ================= Pure overlay/cycle logic =================
    // Pure functions mirroring PowerToys AltWindowCycle's
    // AltWindowCycleLogic.h: no Win32 calls, so the layout/cycle math
    // stays unit-testable and the port's fidelity to the reference is
    // checkable by inspection.
    static class Logic
    {
        public const int MaxColumns = 6;
        // topmost apps are offset past every non-topmost one (their raw
        // Z-order position is meaningless: always painted above
        // everything else regardless of activation)
        public const int TopmostSortOffset = 0x40000000;

        public struct OverlayLayout
        {
            public double scale;
            public int pad, gap, tileW, tileH, headerH, previewH, inner, radius, iconSize;
            public int cols, rows, pageSize;
            public int panelX, panelY, panelW, panelH;
            // Row geometry. rowCount[r] = how many tiles row r holds, rowX[r]
            // that row's left edge. Rows are filled one at a time - the last
            // one is the only short one - but every row is centred, so a
            // short last row leaves its slack split on both sides instead of
            // one ragged right edge.
            public int[] rowCount;
            public int[] rowX;
        }

        public static int Scaled(double scale, int v) { return (int)(v * scale + 0.5); }

        public static NativeMethods.RECT TileRect(ref OverlayLayout L, int index)
        {
            int row, col;
            if (L.rowCount != null && L.rows > 0)
            {
                row = L.rows - 1;
                col = index;
                for (int r = 0; r < L.rows; r++)
                {
                    if (col < L.rowCount[r]) { row = r; break; }
                    col -= L.rowCount[r];
                }
            }
            else
            {
                int c = L.cols > 0 ? L.cols : 1;
                row = index / c;
                col = index % c;
            }
            int left = (L.rowX != null && row < L.rowX.Length ? L.rowX[row] : L.pad) + col * (L.tileW + L.gap);
            int top = L.pad + row * (L.tileH + L.gap);
            return new NativeMethods.RECT { Left = left, Top = top, Right = left + L.tileW, Bottom = top + L.tileH };
        }

        public static NativeMethods.RECT PreviewRect(ref OverlayLayout L, NativeMethods.RECT tile)
        {
            int stroke = Scaled(L.scale, 1);
            return new NativeMethods.RECT { Left = tile.Left + stroke, Top = tile.Top + L.headerH, Right = tile.Right - stroke, Bottom = tile.Bottom - stroke };
        }

        public static NativeMethods.RECT HeaderRect(ref OverlayLayout L, NativeMethods.RECT tile)
        {
            int margin = Scaled(L.scale, 12);
            return new NativeMethods.RECT { Left = tile.Left + margin, Top = tile.Top, Right = tile.Right - margin, Bottom = tile.Top + L.headerH };
        }

        public static NativeMethods.RECT CoverSource(NativeMethods.RECT dest, NativeMethods.RECT avail)
        {
            int aw = avail.Right - avail.Left, ah = avail.Bottom - avail.Top;
            int dw = dest.Right - dest.Left, dh = dest.Bottom - dest.Top;
            if (aw <= 0 || ah <= 0 || dw <= 0 || dh <= 0) return avail;
            double destA = (double)dw / dh, srcA = (double)aw / ah;
            if (srcA > destA)
            {
                int cw = (int)(ah * destA + 0.5); if (cw < 1) cw = 1;
                int x = avail.Left + (aw - cw) / 2;
                return new NativeMethods.RECT { Left = x, Top = avail.Top, Right = x + cw, Bottom = avail.Bottom };
            }
            int ch = (int)(aw / destA + 0.5); if (ch < 1) ch = 1;
            int y = avail.Top + (ah - ch) / 2;
            return new NativeMethods.RECT { Left = avail.Left, Top = y, Right = avail.Right, Bottom = y + ch };
        }

        public static void ComputeLayout(NativeMethods.RECT work, int windowCount, double scale, ref OverlayLayout L)
        {
            L.scale = scale;
            L.pad = Scaled(scale, 32);
            L.gap = Scaled(scale, 26);
            L.tileW = Scaled(scale, 270);
            L.headerH = Scaled(scale, 48);
            L.previewH = Scaled(scale, 142);
            L.inner = Scaled(scale, 6);
            L.radius = Scaled(scale, 10);
            L.iconSize = Scaled(scale, 16);
            L.tileH = L.headerH + L.inner + L.previewH + L.inner;

            int workW = work.Right - work.Left, workH = work.Bottom - work.Top;
            int count = windowCount < 0 ? 0 : windowCount;
            int colsFromWork = (workW - 2 * L.pad + L.gap) / (L.tileW + L.gap);
            if (colsFromWork < 1) colsFromWork = 1;
            int colsMax = Math.Min(MaxColumns, colsFromWork);
            if (colsMax < 1) colsMax = 1;

            int rowsFromWork = (workH - 2 * L.pad + L.gap) / (L.tileH + L.gap);
            if (rowsFromWork < 1) rowsFromWork = 1;

            // Fill one row completely before starting the next, then centre
            // each row within the panel: only the last row is ever short, so
            // it is the only one that moves, and its leftover slack is split
            // evenly to both sides instead of pooling on the right.
            int totalRows = (count + colsMax - 1) / colsMax;
            if (totalRows < 1) totalRows = 1;
            L.rows = Math.Min(totalRows, rowsFromWork);
            if (L.rows < 1) L.rows = 1;
            L.pageSize = Math.Min(count, L.rows * colsMax);
            if (L.pageSize < 0) L.pageSize = 0;
            // The panel is only as wide as the WIDEST ROW THAT IS ACTUALLY
            // USED - never the column limit. Two windows must give a
            // two-tile-wide bar, not a six-tile-wide one with four tiles'
            // worth of empty background on both sides.
            L.cols = Math.Min(colsMax, L.pageSize);

            L.rowCount = new int[L.rows];
            L.rowX = new int[L.rows];
            for (int r = 0; r < L.rows; r++)
            {
                int left = L.pageSize - r * colsMax;
                L.rowCount[r] = left >= colsMax ? colsMax : (left > 0 ? left : 0);
                L.rowX[r] = L.pad + (L.cols - L.rowCount[r]) * (L.tileW + L.gap) / 2;
            }

            L.panelW = 2 * L.pad + L.cols * L.tileW + Math.Max(0, L.cols - 1) * L.gap;
            L.panelH = 2 * L.pad + L.rows * L.tileH + Math.Max(0, L.rows - 1) * L.gap;
            L.panelX = work.Left + (workW - L.panelW) / 2;
            L.panelY = work.Top + (workH - L.panelH) / 2;
            if (L.panelX < work.Left) L.panelX = work.Left;
            if (L.panelY < work.Top) L.panelY = work.Top;
        }

        public static int PageStartFor(int selected, int windowCount, int pageSize)
        {
            if (windowCount <= 0 || pageSize <= 0) return 0;
            int idx = Math.Max(0, Math.Min(selected, windowCount - 1));
            return (idx / pageSize) * pageSize;
        }


        // The pinned entry (the current one) sorts first; non-topmost apps keep
        // their Z-order rank; topmost apps are offset past every non-topmost
        // one. Which entry is pinned is decided once by the caller, so exactly
        // one entry can ever sit in slot 0.
        public static int AppSortKey(AppEntry e, IntPtr pin)
        {
            if (e.ReprHwnd == pin) return -1;
            return (e.Topmost ? TopmostSortOffset : 0) + e.Rank;
        }
    }

    // ================= cycled-application model =================
    // One entry per cycled application: representative window plus the
    // grouping/paint state the overlay needs.
    class AppEntry
    {
        public IntPtr ReprHwnd;
        public string Exe;
        public string Title;
        public Icon Icon;      // shared handle (window icons) or owned clone
        public IntPtr Thumb = IntPtr.Zero;
        public int Rank;       // Z-order rank of the representative window
        public bool Topmost;   // representative window is WS_EX_TOPMOST
        // Stable identity across re-enumerations: the exe path, or
        // "exe|hwnd" for a window that owns its own entry. The in-session
        // refresh matches entries on this, so a surviving entry keeps its
        // slot even when its representative window changed.
        public string Key;
        // WS_EX_APPWINDOW: the window asked the shell for its own taskbar /
        // switcher entry, so it is kept apart instead of being merged into the
        // entry of its exe. It represents itself and nothing else, so it can
        // only be pinned as "current" by handle, never by exe.
        public bool OwnEntry;
    }

    static class Program
    {

        static NativeMethods.IVirtualDesktopManager _vdm;

        static bool OnCurrentDesktop(IntPtr hwnd)
        {
            try
            {
                if (_vdm == null) return true;
                bool on;
                if (_vdm.IsWindowOnCurrentVirtualDesktop(hwnd, out on) != 0) return true;
                return on;
            }
            catch { return true; }
        }

        // ================= theme / accent =================
        static bool LightTheme()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v != 0;
                }
            }
            catch { return false; }
        }

        static Color AccentColor()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    var v = k == null ? null : k.GetValue("AccentColor");
                    if (v is int)
                    {
                        int c = (int)v & 0x00FFFFFF;
                        return Color.FromArgb(255, c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);
                    }
                }
            }
            catch { }
            return Color.FromArgb(255, 0, 120, 215);
        }

        static Color CardColor(bool light) { return light ? Color.FromArgb(179, 255, 255, 255) : Color.FromArgb(210, 18, 18, 18); }
        static Color CardSolid(bool light) { return light ? Color.FromArgb(255, 248, 248, 248) : Color.FromArgb(255, 20, 20, 20); }
        static Color CardStrokeC(bool light) { return light ? Color.FromArgb(15, 0, 0, 0) : Color.FromArgb(25, 0, 0, 0); }
        static Color PanelStrokeC(bool light) { return light ? Color.FromArgb(24, 0, 0, 0) : Color.FromArgb(64, 255, 255, 255); }
        static Color PanelFillC(bool light) { return light ? Color.FromArgb(255, 243, 243, 243) : Color.FromArgb(255, 84, 84, 84); }
        static Color HeaderTextC(bool light) { return light ? Color.FromArgb(255, 26, 26, 26) : Color.FromArgb(255, 235, 235, 235); }
        static Color FocusShadowC(bool light) { return light ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(150, 0, 0, 0); }

        // ================= tray icon =================
        // Drawn at runtime so it follows the OS accent color (same source the
        // panel's focus ring uses) instead of shipping a .ico asset - keeps the
        // build single-file and zero-dependency. Redrawn on Personal preference
        // changes, so a live accent switch recolors the tray too.
        static Icon _trayIcon;   // NotifyIcon does not own its Icon; keep this alive or the HICON gets finalized away

        static Icon MakeTrayIcon()
        {
            int size = 16;
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    size = Math.Max(16, (int)Math.Round(g.DpiX * 16 / 96.0));
            }
            catch { }
            if ((size & 1) == 1) size++;   // even sizes rasterize cleaner

            Color accent = AccentColor();
            // white-on-accent normally; dark glyph when the accent itself is light (yellows)
            double lum = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
            Color glyph = lum > 0.55 ? Color.FromArgb(24, 24, 24) : Color.White;

            using (var bmp = new Bitmap(size, size))
            using (var g2 = Graphics.FromImage(bmp))
            {
                g2.SmoothingMode = SmoothingMode.AntiAlias;
                float r = size * 0.24f;
                float m = 0.5f;   // keeps the AA edge off the bitmap border
                using (var path = new GraphicsPath())
                {
                    path.AddArc(m, m, 2 * r, 2 * r, 180, 90);
                    path.AddArc(size - m - 2 * r, m, 2 * r, 2 * r, 270, 90);
                    path.AddArc(size - m - 2 * r, size - m - 2 * r, 2 * r, 2 * r, 0, 90);
                    path.AddArc(m, size - m - 2 * r, 2 * r, 2 * r, 90, 90);
                    path.CloseFigure();
                    using (var b = new SolidBrush(accent))
                        g2.FillPath(b, path);
                }
                float w = Math.Max(2f, size * 0.14f);   // shaft thickness
                float inset = size * 0.22f;
                // The caps are GDI+ objects the Pen does NOT take ownership
                // of, so they need disposing explicitly - they are rebuilt on
                // every theme/accent change otherwise.
                using (var endCap = new AdjustableArrowCap(w, w * 2.0f, true))
                using (var startCap = new AdjustableArrowCap(w, w * 2.0f, true))
                using (var pen = new Pen(glyph, w))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Custom;
                    pen.CustomEndCap = endCap;
                    g2.DrawLine(pen, inset, size * 0.36f, size - inset, size * 0.36f);   // ->
                    pen.EndCap = LineCap.Round;
                    pen.StartCap = LineCap.Custom;
                    pen.CustomStartCap = startCap;
                    g2.DrawLine(pen, inset, size * 0.64f, size - inset, size * 0.64f);   // <-
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { NativeMethods.DestroyIcon(h); }
            }
        }

        // ================= state =================
        static IntPtr _hook = IntPtr.Zero;
        static IntPtr _mouseHook = IntPtr.Zero;
        static NativeMethods.HookProc _hookProc;
        static NativeMethods.HookProc _mouseHookProc;
        static Mutex _mutex;
        static StreamWriter _log;
        static bool _enabled = true;
        static bool _exitRequested;
        static bool _tabHookDown;
        static bool _tabPassedDown;
        static bool _logVerbose;
        const long MaxLogBytes = 8 * 1024 * 1024;
        static long _logBytes;
        static int _shellHookMessage;
        static readonly UIntPtr ReplayInputTag = new UIntPtr(0x41504852);

        static PanelForm _panel;
        static ChromeForm _chrome;
        static MsgForm _msg;

        static List<AppEntry> _apps = new List<AppEntry>();
        static List<IntPtr> _thumbs = new List<IntPtr>();
        static bool _session;
        // Commit/Cancel teardown in progress: a second trigger pumped in via
        // Application.DoEvents (watchdog tick / queued WM_APP_COMMIT) must
        // no-op instead of running a nested Commit to completion - the nested
        // one activated the target, the outer one then failed its retries and
        // handed the foreground back to the source ("To Do stays in front").
        static bool _committing;
        static int _index;
        static int _pageStart;
        static Logic.OverlayLayout _layout;
        static NativeMethods.RECT _panelRect;
        static NativeMethods.RECT _work;    // work area of the monitor the overlay lives on
        static double _scale = 1.0;
        // One font + brush per chrome repaint, rebuilt by RenderChrome and
        // disposed in its finally. Fields rather than locals so the drawing
        // helpers can share them.
        static Font _headerFont;
        static SolidBrush _headerBrush;
        // Polls for windows that disappeared while the overlay is up. Only
        // runs during a session; see RefreshTick.
        static System.Windows.Forms.Timer _refreshTimer;

        // Human-facing version, read from the assembly attribute.
        // [assembly: AssemblyInformationalVersion] attribute at the top of
        // this file - one literal per release, consumed everywhere.
        static readonly string AppVersion = InitVersion();

        static string InitVersion()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var attr = Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute))
                as System.Reflection.AssemblyInformationalVersionAttribute;
            return attr != null ? attr.InformationalVersion : asm.GetName().Version.ToString(3);
        }

        static string LogText(string text)
        {
            if (!_logVerbose) return "<redacted>";
            return (text ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        static void Log(string msg)
        {
            if (_log == null) return;
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff ") + msg;
                long bytes = _log.Encoding.GetByteCount(line) + _log.Encoding.GetByteCount(_log.NewLine);
                if (_logBytes + bytes > MaxLogBytes) return;
                _log.WriteLine(line);
                _log.Flush();
                _logBytes += bytes;
            }
            catch
            {
                try { _log.BaseStream.Dispose(); } catch { } // Do not retry a partial buffered write.
                _log = null;  // A partial write cannot be safely counted; stop logging.
            }
        }

        static bool Post(int m) { return PostAt(m, 0); }
        static bool PostAt(int m, int i)
        {
            return _msg != null && !_msg.IsDisposed && _msg.IsHandleCreated
                && NativeMethods.PostMessageW(_msg.Handle, (uint)m, (IntPtr)i, IntPtr.Zero);
        }

        static NativeMethods.INPUT KeyInput(int key, bool up)
        {
            var input = new NativeMethods.INPUT();
            input.type = NativeMethods.INPUT_KEYBOARD;
            input.data.keyboard.wVk = (ushort)key;
            input.data.keyboard.dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0;
            input.data.keyboard.dwExtraInfo = ReplayInputTag;
            return input;
        }

        static NativeMethods.INPUT[] NativeTabInputs(bool addAlt, bool addShift)
        {
            var inputs = new NativeMethods.INPUT[2 + (addAlt ? 2 : 0) + (addShift ? 2 : 0)];
            int i = 0;
            if (addAlt) inputs[i++] = KeyInput(NativeMethods.VK_MENU, false);
            if (addShift) inputs[i++] = KeyInput(NativeMethods.VK_SHIFT, false);
            inputs[i++] = KeyInput(NativeMethods.VK_TAB, false);
            inputs[i++] = KeyInput(NativeMethods.VK_TAB, true);
            if (addShift) inputs[i++] = KeyInput(NativeMethods.VK_SHIFT, true);
            if (addAlt) inputs[i++] = KeyInput(NativeMethods.VK_MENU, true);
            return inputs;
        }

        // Recover only keys still down in the prefix actually inserted by
        // SendInput. Never release a modifier whose down was not inserted.
        static NativeMethods.INPUT[] ReplayReleases(NativeMethods.INPUT[] inputs, uint sent)
        {
            bool alt = false, shift = false, tab = false;
            for (int i = 0; i < sent; i++)
            {
                var key = inputs[i].data.keyboard;
                bool down = (key.dwFlags & NativeMethods.KEYEVENTF_KEYUP) == 0;
                if (key.wVk == NativeMethods.VK_MENU) alt = down;
                if (key.wVk == NativeMethods.VK_SHIFT) shift = down;
                if (key.wVk == NativeMethods.VK_TAB) tab = down;
            }
            int count = (alt ? 1 : 0) + (shift ? 1 : 0) + (tab ? 1 : 0);
            if (count == 0) return null;
            var releases = new NativeMethods.INPUT[count];
            int next = 0;
            if (tab) releases[next++] = KeyInput(NativeMethods.VK_TAB, true);
            if (shift) releases[next++] = KeyInput(NativeMethods.VK_SHIFT, true);
            if (alt) releases[next++] = KeyInput(NativeMethods.VK_MENU, true);
            return releases;
        }

        // Reconstruct a quick tap if Alt was released while startup ran.
        // Release only modifiers introduced here; never swallow physical Alt-up.
        static void ReplayNativeTab(bool reverse)
        {
            bool addAlt = !AltDown();
            bool addShift = reverse && (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) == 0;
            NativeMethods.INPUT[] inputs = NativeTabInputs(addAlt, addShift);
            int size = Marshal.SizeOf(typeof(NativeMethods.INPUT));
            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, size);
            Log("alt+tab fallback: native replay " + (sent == inputs.Length ? "ok" : "FAILED")
                + " reverse=" + reverse);
            if (sent > 0 && sent < inputs.Length)
            {
                NativeMethods.INPUT[] releases = ReplayReleases(inputs, sent);
                if (releases != null)
                {
                    uint released = NativeMethods.SendInput((uint)releases.Length, releases, size);
                    Log("alt+tab fallback: partial-input cleanup " + (released == releases.Length ? "ok" : "FAILED"));
                }
            }
        }

        // ================= windows =================
        class MsgForm : Form
        {
            public MsgForm() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; Opacity = 0; }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE; return cp; }
            }
            protected override void WndProc(ref Message m)
            {
                if (_shellHookMessage != 0 && m.Msg == _shellHookMessage && m.WParam == new IntPtr(0x8006))
                    Log("shell flash hwnd=0x" + m.LParam.ToInt64().ToString("X")
                        + " fg=0x" + NativeMethods.GetForegroundWindow().ToInt64().ToString("X"));
                if (m.Msg >= NativeMethods.WM_APP_START && m.Msg <= NativeMethods.WM_APP_COMMITAT) { HandleAppMsg(m.Msg, m.WParam); return; }
                base.WndProc(ref m);
            }
        }

        // opaque rounded panel hosting the live DWM thumbnails (thumbHost role)
        //
        // Both overlay windows show without activation. Input arrives through
        // low-level hooks, not through keyboard focus on either window.
        class PanelForm : Form
        {
            public PanelForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST; return cp; }
            }
        }

        // layered chrome on top of the panel (cards, headers, focus ring, pages)
        class ChromeForm : Form
        {
            public ChromeForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_LAYERED; return cp; }
            }
        }

        // ================= enumeration =================
        static string ExePathOfPid(uint pid)
        {
            IntPtr h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (NativeMethods.QueryFullProcessImageNameW(h, 0, sb, ref size) && size > 0)
                    return sb.ToString(0, size);
                return null;
            }
            finally { NativeMethods.CloseHandle(h); }
        }

        static string ClassNameOf(IntPtr hwnd)
        {
            var c = new StringBuilder(64);
            NativeMethods.GetClassNameW(hwnd, c, c.Capacity);
            return c.ToString();
        }

        static string WindowExe(IntPtr hwnd)
        {
            // Memoised for the duration of one enumeration: the eligibility
            // predicate and the caller both need it, and OpenProcess +
            // QueryFullProcessImageName is the most expensive call in the
            // whole hot path (it runs for every top-level window on every
            // Alt+Tab). Cleared by EnumerateEntries.
            string memo;
            if (_exeMemo.TryGetValue(hwnd, out memo)) return memo;
            string exe = WindowExeCore(hwnd);
            _exeMemo[hwnd] = exe;
            return exe;
        }

        static Dictionary<IntPtr, string> _exeMemo = new Dictionary<IntPtr, string>();

        static string WindowExeCore(IntPtr hwnd)
        {
            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return null;
            string exe = ExePathOfPid(pid);
            if (exe != null && exe.EndsWith("applicationframehost.exe", StringComparison.OrdinalIgnoreCase))
            {
                // A UWP frame is only a shell: attribute it to the app that owns
                // the CoreWindow it hosts.
                IntPtr child = UwpCoreWindowOf(hwnd);
                if (child != IntPtr.Zero)
                {
                    uint cpid;
                    NativeMethods.GetWindowThreadProcessId(child, out cpid);
                    string cexe = ExePathOfPid(cpid);
                    if (cexe != null) return cexe;
                }
                return null;
            }
            return exe;
        }

        // The CoreWindow is a child of the frame while the app runs. Once it
        // suspends the window tree is torn down and Windows keeps only a
        // top-level CoreWindow carrying the same title. Title is only a
        // last-resort key here: the candidate must belong to another process
        // than ApplicationFrameHost and the match must be unique.
        static IntPtr UwpCoreWindowOf(IntPtr frame)
        {
            uint hostPid;
            NativeMethods.GetWindowThreadProcessId(frame, out hostPid);
            IntPtr child = FindUniqueChildCoreWindow(frame, hostPid);
            if (child != IntPtr.Zero) return child;
            return FindTopLevelCoreWindowByTitle(GetWindowTitle(frame), hostPid);
        }

        // The CoreWindow is hosted by a DIFFERENT process than the frame -
        // ApplicationFrameHost is only the shell - so a child owned by the
        // frame's own process is never it. Requiring the match to be unique
        // keeps a stray helper window from being mistaken for the app.
        // (Replaces a plain "first child of this class" lookup.)
        static IntPtr FindUniqueChildCoreWindow(IntPtr frame, uint hostPid)
        {
            IntPtr found = IntPtr.Zero;
            int count = 0;
            NativeMethods.EnumChildWindows(frame, delegate(IntPtr h, IntPtr lp)
            {
                var sb = new StringBuilder(64);
                NativeMethods.GetClassNameW(h, sb, sb.Capacity);
                if (sb.ToString() != ClassCoreWindow) return true;
                uint pid;
                NativeMethods.GetWindowThreadProcessId(h, out pid);
                if (pid == 0 || pid == hostPid) return true;
                found = h;
                count++;
                return true;
            }, IntPtr.Zero);
            return count == 1 ? found : IntPtr.Zero;
        }

        // Reverse lookup: GetForegroundWindow() reports a UWP app's CoreWindow
        // just as often as its frame, and only the frame is in the cycle.
        // The title match must be unique, or a frame that merely shares a
        // title with an unrelated one would be picked.
        static IntPtr UwpFrameOf(IntPtr coreWindow)
        {
            IntPtr p = NativeMethods.GetParent(coreWindow);
            if (p != IntPtr.Zero && ClassNameOf(p) == ClassAppFrame) return p;
            string title = GetWindowTitle(coreWindow);
            if (string.IsNullOrEmpty(title)) return IntPtr.Zero;
            IntPtr found = IntPtr.Zero;
            int count = 0;
            NativeMethods.EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                if (ClassNameOf(h) != ClassAppFrame) return true;
                if (GetWindowTitle(h) != title) return true;
                found = h;
                count++;
                return true;
            }, IntPtr.Zero);
            return count == 1 ? found : IntPtr.Zero;
        }

        const string ClassCoreWindow = "Windows.UI.Core.CoreWindow";
        const string ClassAppFrame = "ApplicationFrameWindow";

        // The classic native Alt-Tab owner-chain walk (Raymond Chen /
        // PowerToys AltWindowCycle): root owner, then GetLastActivePopup
        // until it stops changing or turns visible. Single implementation
        // shared by RepresentativeOf and the eligibility predicate - the
        // logic used to live in three drifting copies.
        //
        // The root is walked with GetWindow(GW_OWNER), NOT GetAncestor(
        // GA_ROOTOWNER): measured on this machine, GA_ROOTOWNER stops at the
        // window itself when the only link to the root is ownership - a
        // file dialog reported itself as its own root, which silently disabled
        // the entire walk and let the dialog pass as its own chain (showing up
        // as a duplicate entry next to its own main window).
        static IntPtr OwnerChainRoot(IntPtr hwnd)
        {
            IntPtr root = hwnd;
            // Owner chains are acyclic; the cap is pure paranoia.
            for (int hops = 0; hops < 32; hops++)
            {
                IntPtr o = NativeMethods.GetWindow(root, NativeMethods.GW_OWNER);
                if (o == IntPtr.Zero) break;
                root = o;
            }
            return root;
        }

        static IntPtr OwnerChainRepresentative(IntPtr hwnd)
        {
            IntPtr walk = OwnerChainRoot(hwnd);
            for (; ; )
            {
                IntPtr pop = NativeMethods.GetLastActivePopup(walk);
                if (pop == walk) break;
                if (NativeMethods.IsWindowVisible(pop)) break;
                walk = pop;
            }
            return walk;
        }

        // Map any window onto the window that stands for it in the cycle: the
        // UWP CoreWindow<->frame pairing plus the owner-chain walk (a group
        // whose main window is minimized is represented by its visible owned
        // popup).
        static IntPtr RepresentativeOf(IntPtr hwnd)
        {
            if (ClassNameOf(hwnd) == ClassCoreWindow)
            {
                IntPtr frame = UwpFrameOf(hwnd);
                if (frame != IntPtr.Zero) hwnd = frame;
            }
            return OwnerChainRepresentative(hwnd);
        }

        static string GetWindowTitle(IntPtr hwnd)
        {
            var t = new StringBuilder(256);
            NativeMethods.GetWindowTextW(hwnd, t, 256);
            return t.ToString();
        }

        // Title alone is a weak key, so the candidate must (a) not belong to
        // the frame's own process (ApplicationFrameHost is only the shell) and
        // (b) be the unique match; otherwise a frame that merely shares a title
        // with something unrelated would be picked.
        static IntPtr FindTopLevelCoreWindowByTitle(string title, uint excludedPid)
        {
            if (string.IsNullOrEmpty(title)) return IntPtr.Zero;
            IntPtr found = IntPtr.Zero;
            int count = 0;
            NativeMethods.EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                var cls = new StringBuilder(64);
                NativeMethods.GetClassNameW(h, cls, 64);
                if (cls.ToString() != ClassCoreWindow) return true;
                if (GetWindowTitle(h) != title) return true;
                uint pid;
                NativeMethods.GetWindowThreadProcessId(h, out pid);
                if (pid == 0 || pid == excludedPid) return true;
                found = h;
                count++;
                return true;
            }, IntPtr.Zero);
            return count == 1 ? found : IntPtr.Zero;
        }

        static bool IsCloaked(IntPtr hwnd)
        {
            int v;
            return NativeMethods.DwmGetWindowAttribute(hwnd, 14 /*DWMWA_CLOAKED*/, out v, 4) == 0 && v != 0;
        }

        // A layered window with alpha 0 is "visible" and uncloaked but shows
        // nothing, so it must not take a slot in the cycle.
        static bool IsAlphaInvisible(IntPtr hwnd)
        {
            int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((ex & NativeMethods.WS_EX_LAYERED) == 0) return false;
            int crKey; byte alpha; int flags;
            if (!NativeMethods.GetLayeredWindowAttributes(hwnd, out crKey, out alpha, out flags)) return false;
            return alpha == 0;
        }

        // The classic native Alt-Tab predicate (Raymond Chen / PowerToys
        // AltWindowCycle) plus this app's extra exclusions, as a SINGLE
        // predicate that reports why a top-level window is left out of the
        // cycle (null = it participates). The merged form serves both the
        // filter and the log so they can never diverge.
        //
        // This entry point is the owner-chain rule; every other check lives in
        // AltTabIneligibleIgnoreChain below, which the root-presentable probe
        // also calls.
        static string AltTabIneligibilityReason(IntPtr hwnd)
        {
            string why = AltTabIneligibleIgnoreChain(hwnd);
            if (why != null) return why;

            // Owner chains appear exactly once, represented by their ROOT
            // window. An owned popup sits above its owner and is raised with
            // it, and while a MODAL popup is up the root cannot take focus
            // anyway - a separate popup entry would only trap the user. (A
            // file dialog used to show up next to its own main window exactly
            // like that: the main window carries WS_EX_APPWINDOW, so the dialog
            // fell out of the exe grouping into an entry of its own.)
            //
            // Note the old form of this rule could never fire for a VISIBLE
            // popup: OwnerChainRepresentative walks to GetLastActivePopup,
            // which - by construction - returns that very popup, so the popup
            // compared equal to its representative and passed.
            //
            // A popup may stand in for the chain only when the root itself is
            // not presentable (hidden, cloaked, alpha-0, toolwindow, other
            // desktop), which keeps "main window hidden while an owned dialog
            // is visible" groups alive.
            IntPtr root = OwnerChainRoot(hwnd);
            if (root != hwnd)
            {
                if (AltTabIneligibleIgnoreChain(root) == null) return "owned-popup";
                // Root cannot represent the chain: a visible popup is then the
                // only presentable member left, so it stands in. No further
                // check here - the walk in OwnerChainRepresentative always
                // stops BEFORE a visible popup (it returns the root or an
                // invisible one), so a "not-owner-rep" test on this path could
                // never let a visible dialog through and would instead hide
                // apps like "tray utility with a floating panel" entirely.
            }
            return null;
        }

        // AltTabIneligibilityReason with a per-enumeration cache of the
        // chain-only check.
        //
        // The owner-chain rule calls AltTabIneligibleIgnoreChain on the ROOT as
        // well as on the window itself, and that root is re-evaluated for every
        // popup under it. Each evaluation costs several user32 calls, a DWM
        // attribute read and a COM call to the virtual desktop manager, so a
        // machine with many owned popups paid for the same root repeatedly on
        // every Alt+Tab. Only the chain-only half is cached: the owner rule
        // itself still runs per window, so the RULE cannot change - only the
        // repeated work disappears.
        static string EligibilityMemoized(IntPtr hwnd, Dictionary<IntPtr, string> memo)
        {
            string cached;
            if (memo.TryGetValue(hwnd, out cached)) return cached;
            string why = AltTabIneligibleIgnoreChain(hwnd);
            memo[hwnd] = why;
            if (why != null) return why;

            // Same owner-chain rule as AltTabIneligibilityReason, with the root
            // probe served from the same cache.
            IntPtr root = OwnerChainRoot(hwnd);
            if (root == hwnd) return null;
            string rootWhy;
            if (memo.TryGetValue(root, out rootWhy)) rootWhy = AltTabIneligibleIgnoreChain(root);
            else { rootWhy = AltTabIneligibleIgnoreChain(root); memo[root] = rootWhy; }
            return rootWhy == null ? "owned-popup" : null;
        }

        // Every eligibility check except the owner-chain rule. Shared by the
        // full predicate and by the root-presentable probe above it, so the
        // two can never drift apart.
        static string AltTabIneligibleIgnoreChain(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return "invisible";

            string cn = ClassNameOf(hwnd);
            // frameless UWP system hosts (text input, search, shell dialogs)
            // never belong in the cycle; real UWP apps live behind an
            // ApplicationFrameWindow and stay in.
            if (cn == ClassCoreWindow) return "corewindow";
            // #32769 is the real desktop (GetDesktopWindow): no title, no
            // TOOLWINDOW bit, full-screen size - it must be named outright
            // rather than relying on the empty title it happens to have.
            if (cn == "#32769" || cn == "Progman" || cn == "WorkerW") return "desktop";

            // Cloak rule (native/Hopper parity): any cloaked window is out.
            // DWM_CLOAKED_SHELL (2) covers windows parked on other virtual
            // desktops (Windows implements virtual desktops by cloaking);
            // DWM_CLOAKED_APP (1) covers windows an app hid itself (suspended
            // UWP helper windows, background UI). A suspended UWP app's frame
            // itself stays uncloaked, so suspended apps remain listed - and
            // activating one wakes it.
            if (IsCloaked(hwnd)) return "cloaked";

            // Fully transparent (opacity 0) - see IsAlphaInvisible. Same
            // user-visible effect as cloaked/invisible: nothing to show, no
            // entry to offer.
            if (IsAlphaInvisible(hwnd)) return "alpha-0";

            int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((ex & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (ex & NativeMethods.WS_EX_APPWINDOW) == 0) return "toolwindow";

            // An EMPTY TITLE is not a reason to exclude a window. Some apps
            // (Foxmail's main frame is one) draw their whole UI without ever
            // calling SetWindowText, so requiring a title silently dropped
            // them from the switcher entirely - the app looked simply absent.
            //
            // What actually matters is whether the window is a real one the
            // user could interact with: it must have a real on-screen size.
            // The tiny/hairline helpers this used to catch (tooltips, tray
            // icons, input strips) are all a few pixels, and most are already
            // excluded above as toolwindows. Check size here, and keep the
            // title only for the two cases where the text itself is the tell.
            NativeMethods.RECT titleRect;
            bool hasRealSize = NativeMethods.GetWindowRect(hwnd, out titleRect)
                && titleRect.Right - titleRect.Left > 1
                && titleRect.Bottom - titleRect.Top > 1;
            if (!hasRealSize) return "tiny";   // hairline helpers: tooltips, tray icons, input strips

            string title = GetWindowTitle(hwnd);
            if (title == "Windows Input Experience") return "input-experience";
            if (!OnCurrentDesktop(hwnd)) return "other-desktop";

            if (WindowExe(hwnd) == null) return "no-exe";
            return null;
        }

        // Takes a private copy of h, so the caller is free to dispose the
        // result without touching another process's icon.
        //
        // NOTE ON OWNERSHIP: Icon.FromHandle sets ownHandle=false, so this
        // Icon does not destroy the copy on Dispose - the handle lives until
        // the process exits. That looks like a leak and was "fixed" by cloning
        // and destroying the intermediate handle, but Clone() performs a
        // SECOND icon copy, doubling the cost of every icon fetch. Icon
        // loading happens on the Alt+Tab startup path, and the extra cost eats
        // into the window during which this process may hand the foreground
        // on: measured 10 failed switches per 25 gestures, versus 0 with the
        // cheap form. The handle is bounded (one copy per distinct executable,
        // cached for the process lifetime, reclaimed at exit), so paying
        // switching reliability for it is a bad trade. Reverted deliberately.
        static Icon CopyIconSafe(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            try
            {
                IntPtr copy = NativeMethods.CopyIcon(h);
                if (copy != IntPtr.Zero) return Icon.FromHandle(copy);
            }
            catch { }
            return null;
        }

        // Returns an icon safe to dispose: it is always a COPY, never an
        // adopted live HICON, because disposing an adopted handle would
        // destroy the target window's own icon, blanking it on screen and in
        // the taskbar.
        static Icon GetAppIcon(IntPtr hwnd, string exe)
        {
            // prefer the window's own icons (exactly what the taskbar shows)
            IntPtr res;
            if (NativeMethods.SendMessageTimeoutW(hwnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL2, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 100, out res) != IntPtr.Zero && res != IntPtr.Zero)
            { Icon i = CopyIconSafe(res); if (i != null) return i; }
            if (NativeMethods.SendMessageTimeoutW(hwnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_BIG, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 100, out res) != IntPtr.Zero && res != IntPtr.Zero)
            { Icon i = CopyIconSafe(res); if (i != null) return i; }
            Icon ci = CopyIconSafe(NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GCLP_HICONSM));
            if (ci != null) return ci;
            ci = CopyIconSafe(NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GCLP_HICON));
            if (ci != null) return ci;

            // The shell allocates this handle for us. Adopting it costs
            // nothing extra on the Alt+Tab path (see CopyIconSafe).
            try
            {
                var fi = new NativeMethods.SHFILEINFOW();
                IntPtr r2 = NativeMethods.SHGetFileInfoW(exe, 0x80, ref fi, (uint)Marshal.SizeOf(typeof(NativeMethods.SHFILEINFOW)), NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON | NativeMethods.SHGFI_USEFILEATTRIBUTES);
                if (r2 != IntPtr.Zero && fi.hIcon != IntPtr.Zero) return Icon.FromHandle(fi.hIcon);
            }
            catch { }
            return (Icon)SystemIcons.Application.Clone();
        }

        // Icons are cached per executable for the lifetime of the process.
        // Fetching one costs up to two WM_GETICON round-trips into another
        // process (100 ms timeout each), which is by far the slowest part of
        // opening a cycle - and a rapid Alt+Tab back and forth re-opens one
        // every time. The cache turns every cycle after the first into a
        // dictionary lookup. Entries are owned by the cache, NEVER disposed
        // by a session.
        static Dictionary<string, Icon> _iconCache = new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);

        static Icon CachedIcon(string exe, IntPtr hwnd)
        {
            if (string.IsNullOrEmpty(exe)) exe = "?";
            Icon ic;
            if (_iconCache.TryGetValue(exe, out ic) && ic != null) return ic;
            ic = GetAppIcon(hwnd, exe);
            _iconCache[exe] = ic;
            return ic;
        }

        // ================= state machine =================
        // Every WM_APP_* handler runs inside the message pump: an exception
        // escaping here unwinds out of WndProc and tears down the process.
        // Nothing in this switch may throw through - worst case we end the
        // session and let the native switcher have the gesture back.
        static void HandleAppMsg(int msg, IntPtr wparam)
        {
            try
            {
                DispatchAppMsg(msg, wparam);
            }
            catch (Exception e)
            {
                Log("  app message failed: " + e.GetType().Name + ": " + e.Message);
                try
                {
                    if (_session) EndSession();
                }
                catch { }
            }
        }

        static void DispatchAppMsg(int msg, IntPtr wparam)
        {
            switch (msg)
            {
                case NativeMethods.WM_APP_START:
                    bool reverse = wparam != IntPtr.Zero;
                    if (_committing) { ReplayNativeTab(reverse); break; }
                    if (_session) MoveIndex(reverse ? -1 : 1);
                    else
                    {
                        Log("hotkey: alt+tab -> start");
                        StartSession(reverse);
                        if (!_session) { ReplayNativeTab(reverse); break; }
                    }
                    if (_session && !AltDown()) Commit();
                    break;
                case NativeMethods.WM_APP_NEXT: if (_session) MoveIndex(1); break;
                case NativeMethods.WM_APP_PREV: if (_session) MoveIndex(-1); break;
                case NativeMethods.WM_APP_COMMIT: if (_session) { Log("hotkey: alt up -> commit"); Commit(); } break;
                case NativeMethods.WM_APP_CANCEL: if (_session) Cancel(); break;
                case NativeMethods.WM_APP_COMMITAT:
                    if (!_session) break;
                    // The index arrives from a posted message, so treat it as
                    // untrusted input rather than assuming it is in range.
                    int wanted = (int)wparam;
                    if (wanted < 0 || wanted >= _apps.Count) { Log("  commit-at index out of range: " + wanted); break; }
                    _index = wanted;
                    RenderChrome();
                    Commit();
                    break;
            }
        }

        static double MonitorScale(IntPtr hwnd, out NativeMethods.RECT work)
        {
            IntPtr m = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (m != IntPtr.Zero && NativeMethods.GetMonitorInfoW(m, ref mi)) work = mi.rcWork;
            else work = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            uint dx, dy;
            if (m != IntPtr.Zero && NativeMethods.GetDpiForMonitor(m, NativeMethods.MDT_EFFECTIVE_DPI, out dx, out dy) == 0 && dx != 0)
                return dx / 96.0;
            return 1.0;
        }

        // One entry per application, in raw Z-order: grouped by exe, plus a
        // dedicated entry for every WS_EX_APPWINDOW window. Shared by
        // StartSession and the in-session refresh, so a refresh produces
        // exactly the same entries and can diff them against the running list.
        static List<AppEntry> EnumerateEntries()
        {
            // The per-hwnd exe cache is process-wide, so it must be reset per
            // enumeration or a window that has since been recreated keeps the
            // previous mapping.
            _exeMemo.Clear();
            var order = new List<AppEntry>();
            var byExe = new Dictionary<string, AppEntry>();
            // Memoized per enumeration. The owner-chain rule evaluates a
            // window's root as well, so without this the common case (many
            // visible popups under one root) re-runs the full predicate on the
            // same root for every one of them - roughly doubling the user32,
            // DWM and COM calls per Alt+Tab. Scoped to this call so a window
            // that changes state between refreshes is re-evaluated.
            var ignoreMemo = new Dictionary<IntPtr, string>();
            int rank = 0;
            NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr lp)
            {
              try
              {
                // Any window we inspect can be hostile: the title/class reads
                // marshal into another process, DWM can fail, and the virtual
                // desktop manager is COM. An exception thrown from inside an
                // EnumWindows callback escapes across the unmanaged frame and
                // kills the process, so every failure is contained here and the
                // window is simply skipped.
                // Every top-level window consumes one Z-order slot. Incrementing
                // only on the skip branches gives two adjacent apps the same
                // rank, which makes the sort order them randomly - the cycle
                // then alternates between two different "next" apps from the
                // same foreground window (the "wrong app gets mixed in" bug).
                int myRank = rank++;
                // Single predicate serves filter AND log: no double evaluation,
                // no second copy to drift out of sync. Memoized because the
                // owner-chain rule evaluates the root as well.
                string why = EligibilityMemoized(hwnd, ignoreMemo);
                if (why != null)
                {
                    // Verbose only: this fires for EVERY top-level window on the
                    // machine (~900 here) on EVERY Alt+Tab, and each Log is a
                    // Flush() to disk on the startup path - which pushed the
                    // first paint past 300ms and exhausted the 8MB log cap
                    // within minutes. Without --log-verbose these lines carry
                    // no information anyway (titles are redacted).
                    if (_logVerbose)
                        Log("  skip 0x" + hwnd.ToInt64().ToString("X") + " [" + ClassNameOf(hwnd)
                            + "] \"" + LogText(GetWindowTitle(hwnd)) + "\" - " + why);
                    return true;
                }
                // The predicate already vetted that an exe resolves; re-resolve
                // here to obtain the value itself.
                string exe = WindowExe(hwnd);
                int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                // WS_EX_APPWINDOW is how a window asks the shell for a taskbar
                // / switcher entry of its own (Explorer folder Properties, Save
                // As and Preferences dialogs, extra document windows, ...).
                // Windows' own Alt+Tab lists those separately, and so do we:
                // keying them per window keeps them reachable instead of being
                // swallowed by the single entry of their exe - an Explorer
                // Properties dialog could otherwise never be switched to.
                bool ownEntry = (ex & NativeMethods.WS_EX_APPWINDOW) != 0;
                string key = ownEntry ? exe + "|" + hwnd.ToInt64().ToString("X") : exe;
                AppEntry e;
                if (!byExe.TryGetValue(key, out e))
                {
                    e = new AppEntry
                    {
                        Exe = exe,
                        Key = key,
                        ReprHwnd = hwnd,
                        Rank = myRank,
                        OwnEntry = ownEntry,
                        // topmost windows sit at the head of the raw Z-order but
                        // usually haven't been activated recently (PowerToys
                        // CropAndLock crops, always-on-top tools, ...); sorting
                        // them raw would let one stale overlay hog the top of
                        // every Alt+Tab cycle, so they are demoted below
                        Topmost = (ex & NativeMethods.WS_EX_TOPMOST) != 0
                    };
                    var t = new StringBuilder(256);
                    NativeMethods.GetWindowTextW(hwnd, t, 256);
                    e.Title = t.ToString();
                    byExe[key] = e;
                    order.Add(e);
                    if (ownEntry && _log != null)
                        Log("  own entry (WS_EX_APPWINDOW) 0x" + hwnd.ToInt64().ToString("X")
                            + " \"" + LogText(e.Title) + "\"");
                }
                return true;
              }
              catch { return true; }   // skip this window; never throw across the callback boundary
            }, IntPtr.Zero);
            return order;
        }

        static void StartSession(bool reverse)
        {
            IntPtr fgRaw = NativeMethods.GetForegroundWindow();
            if (fgRaw == IntPtr.Zero) { Log("start aborted: no foreground window"); return; }
            string fgExe = WindowExe(fgRaw);
            if (fgExe == null)
            {
                Log("start aborted: no exe for fg 0x" + fgRaw.ToInt64().ToString("X")
                    + " [" + ClassNameOf(fgRaw) + "] \"" + LogText(GetWindowTitle(fgRaw)) + "\"");
                return;
            }
            // Normalise the foreground window onto the one that represents it in
            // the cycle: a UWP app reports its CoreWindow (which the cycle
            // predicate keeps out of the list) and a minimized group reports
            // its owned popup. Matching on the raw handle alone fails in both.
            IntPtr fg = RepresentativeOf(fgRaw);
            if (fg == IntPtr.Zero || !NativeMethods.IsWindowVisible(fg)) fg = fgRaw;
            // Capture the source monitor before enumerating windows.
            NativeMethods.RECT work;
            double scale = MonitorScale(fg, out work);
            Log("fg 0x" + fgRaw.ToInt64().ToString("X") + " [" + ClassNameOf(fgRaw) + "] \""
                + LogText(GetWindowTitle(fgRaw)) + "\" -> repr 0x" + fg.ToInt64().ToString("X")
                + " exe=" + LogText(Path.GetFileName(fgExe)));

            var order = EnumerateEntries();
            if (order.Count < 2)
            {
                Log("start aborted: only " + order.Count + " app(s) in cycle");
                EndSession();
                return;
            }

            _scale = scale;
            _work = work;
            Logic.ComputeLayout(work, order.Count, _scale, ref _layout);

            // Exactly one entry is pinned as "current": first the one that *is*
            // the foreground window, else - GetForegroundWindow() does not
            // necessarily return a group's representative (a UWP app reports its
            // CoreWindow, a minimized group reports an owned popup) - the group
            // the foreground window belongs to. Split entries stand for their
            // own window only, so they are never pinned by exe: with an
            // Explorer dialog in front, both it and the Explorer group would
            // otherwise claim slot 0 and Alt+Tab would preselect the dialog
            // itself.
            IntPtr pin = IntPtr.Zero;
            foreach (var e in order)
                if (e.ReprHwnd == fg) { pin = fg; break; }
            if (pin == IntPtr.Zero)
                foreach (var e in order)
                    if (!e.OwnEntry && string.Equals(e.Exe, fgExe, StringComparison.OrdinalIgnoreCase))
                    { pin = e.ReprHwnd; break; }

            // pinned app first, then MRU-ish Z-order with topmost apps last
            // (their raw Z-order position is meaningless: topmost windows are
            // always painted above everything else regardless of activation)
            _apps = order;
            _apps.Sort(delegate(AppEntry a, AppEntry b)
            {
                int ka = Logic.AppSortKey(a, pin), kb = Logic.AppSortKey(b, pin);
                return ka != kb ? ka.CompareTo(kb) : a.Rank.CompareTo(b.Rank);
            });
            // Preselect the app the user last used. Slot 0 is the foreground app
            // only when it could be identified as such; if it could not (its
            // window is not in the cycle at all) slot 0 is already the most
            // recently used other app, and starting at 1 would skip it - which
            // is exactly the "Alt+Tab goes one app too far back" symptom.
            bool fgPinned = pin != IntPtr.Zero;
            _index = reverse ? order.Count - 1 : (fgPinned ? 1 : 0);
            _pageStart = Logic.PageStartFor(_index, _apps.Count, _layout.pageSize);
            Log("fgPinned=" + fgPinned + " index=" + _index);

            var sb2 = new StringBuilder("order:");
            foreach (var e in _apps)
                sb2.Append(' ').Append(LogText(System.IO.Path.GetFileNameWithoutExtension(e.Exe)))
                   .Append('@').Append(e.Rank).Append(e.Topmost ? "*" : "");
            Log(sb2.ToString());

            ShowPanel();
            foreach (var e in _apps) e.Icon = CachedIcon(e.Exe, e.ReprHwnd);
            RegisterThumbnails();
            RenderChrome();
            InstallMouseHook();
            if (_refreshTimer != null) _refreshTimer.Start();

            _session = true;
            Log("session start, apps=" + _apps.Count + " scale=" + _scale.ToString("0.##")
                + " panel=" + _layout.panelW + "x" + _layout.panelH + " tile=" + _layout.tileW + "x" + _layout.tileH);
        }

        static void ShowPanel()
        {
            _panel.BackColor = PanelFillC(LightTheme());
            // Showing or refreshing either overlay must not activate it.
            NativeMethods.SetWindowPos(_panel.Handle, IntPtr.Zero, _layout.panelX, _layout.panelY, _layout.panelW, _layout.panelH,
                                       0x0040 /*SWP_SHOWWINDOW*/ | 0x0010 /*SWP_NOACTIVATE*/);
            _panel.Show(); // Keep WinForms visibility in sync so Hide() works.
            IntPtr rgn = NativeMethods.CreateRoundRectRgn(0, 0, _layout.panelW + 1, _layout.panelH + 1, 2 * Logic.Scaled(_scale, 8), 2 * Logic.Scaled(_scale, 8));
            if (rgn != IntPtr.Zero)
            {
                if (!NativeMethods.SetWindowRgn(_panel.Handle, rgn, false)) NativeMethods.DeleteObject(rgn);
            }
            // Resize/region changes queue background paint, while the layered
            // cards are submitted immediately. Complete paint before the cards
            // can expose the previous session's background surface.
            _panel.Refresh();
        }

        static void MoveIndex(int delta)
        {
            _index = ((_index + delta) % _apps.Count + _apps.Count) % _apps.Count;
            int ps = Logic.PageStartFor(_index, _apps.Count, _layout.pageSize);
            if (ps != _pageStart)
            {
                _pageStart = ps;
                RegisterThumbnails();
            }
            RenderChrome();
        }

        static void RegisterThumbnails()
        {
            UnregisterThumbnails();
            int pageEnd = Math.Min(_pageStart + _layout.pageSize, _apps.Count);
            for (int i = _pageStart, slot = 0; i < pageEnd; ++i, ++slot)
            {
                var app = _apps[i];
                NativeMethods.RECT tile = Logic.TileRect(ref _layout, slot);
                NativeMethods.RECT pv = Logic.PreviewRect(ref _layout, tile);
                app.Thumb = IntPtr.Zero;
                // Only cloaked windows are skipped (parked on another virtual
                // desktop, or hidden by the app itself: no DWM content at
                // all) - the paint path draws a large icon for those.
                //
                // Minimized windows are NOT skipped any more. DWM keeps their
                // last composed content and reports the restored source size,
                // so they thumbnail just like any other window; skipping them
                // is what left most tiles blank on a machine where most
                // windows sit minimized.
                if (IsCloaked(app.ReprHwnd))
                {
                    Log("  thumb: no content (cloaked) for 0x" + app.ReprHwnd.ToInt64().ToString("X"));
                    continue;
                }
                IntPtr tid;
                if (NativeMethods.DwmRegisterThumbnail(_panel.Handle, app.ReprHwnd, out tid) != 0 || tid == IntPtr.Zero)
                {
                    Log("  thumb: DwmRegisterThumbnail failed for 0x" + app.ReprHwnd.ToInt64().ToString("X")
                        + " [" + ClassNameOf(app.ReprHwnd) + "]");
                    continue;
                }
                _thumbs.Add(tid);

                NativeMethods.RECT client = new NativeMethods.RECT();
                NativeMethods.SIZE srcSize;
                bool clientOnly = !NativeMethods.IsIconic(app.ReprHwnd) && NativeMethods.GetClientRect(app.ReprHwnd, out client);
                if (clientOnly) clientOnly = client.Right - client.Left > 0 && client.Bottom - client.Top > 0;
                if (clientOnly)
                {
                    srcSize.cx = client.Right - client.Left;
                    srcSize.cy = client.Bottom - client.Top;
                }
                else if (NativeMethods.DwmQueryThumbnailSourceSize(tid, out srcSize) != 0)
                {
                    srcSize.cx = 0; srcSize.cy = 0;
                }

                if (srcSize.cx <= 0 || srcSize.cy <= 0)
                {
                    // Nothing to show: drop the thumbnail and let the paint
                    // path fall back to the icon rather than stretching a
                    // degenerate source over the whole tile.
                    NativeMethods.DwmUnregisterThumbnail(tid);
                    _thumbs.RemoveAt(_thumbs.Count - 1);
                    continue;
                }
                app.Thumb = tid;

                NativeMethods.RECT avail = new NativeMethods.RECT { Left = 0, Top = 0, Right = srcSize.cx, Bottom = srcSize.cy };
                if (clientOnly)
                {
                    int ix = Math.Min(2, (avail.Right - avail.Left) / 4);
                    int iy = Math.Min(2, (avail.Bottom - avail.Top) / 4);
                    avail.Left += ix; avail.Right -= ix;
                    avail.Top += iy; avail.Bottom -= iy;
                }
                NativeMethods.RECT rcSrc = Logic.CoverSource(pv, avail);

                var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = 0x1 | 0x2 | 0x4 | 0x8 | 0x10, // DEST|SOURCE|OPACITY|VISIBLE|CLIENTONLY
                    rcDestination = pv,
                    rcSource = rcSrc,
                    opacity = 255,
                    fVisible = true,
                    fSourceClientAreaOnly = clientOnly
                };
                NativeMethods.DwmUpdateThumbnailProperties(tid, ref props);
            }
        }

        static void UnregisterThumbnails()
        {
            foreach (var t in _thumbs) { try { NativeMethods.DwmUnregisterThumbnail(t); } catch { } }
            _thumbs.Clear();
            // Keep the entries in sync: RenderChrome decides between a live
            // thumbnail and the icon fallback by looking at app.Thumb.
            foreach (var e in _apps) e.Thumb = IntPtr.Zero;
        }

        // The overlay never owns foreground; hiding it needs no handback.
        static void HideOverlay()
        {
            UnregisterThumbnails();
            if (_panel != null && _panel.IsHandleCreated) _panel.Hide();
            if (_chrome != null && _chrome.IsHandleCreated) _chrome.Hide();
        }

        static void Commit()
        {
            // Reentrancy + idempotency guard: ForceForeground pumps messages
            // (Application.DoEvents), and the pump can deliver a second commit
            // trigger (watchdog tick / queued WM_APP_COMMIT). Without this
            // guard the nested Commit ran to completion (activated the target,
            // ended the session) and the outer one then kept retrying against
            // the changed world, failed, and executed the failure fallback -
            // handing the foreground right back to the source window (the
            // "To Do stays in front" bug).
            if (_committing) { Log("commit swallowed: reentrant"); return; }
            if (!_session) return;
            if (_apps.Count == 0) { Cancel(); return; }
            // Disarm every session-scoped trigger BEFORE doing any work: from
            // here on the watchdog and every queued WM_APP_* message no-ops on
            // the !_session check, so no nested trigger (pumped in via
            // ForceForeground's DoEvents) can run a second teardown underneath
            // us. Window Hopper achieves the same by killing its timer as the
            // first act of Commit. _committing stays as belt-and-braces.
            _session = false;
            _committing = true;
            try
            {
                // Capture the target before any DoEvents can run: a nested
                // WM_APP_NEXT/COMMITAT may still rewrite _index mid-teardown.
                var app = _apps[_index];
                IntPtr target = app.ReprHwnd;
                string targetExe = app.Exe;
                // Like Window Hopper, hide the non-activating overlay first.
                HideOverlay();
                bool ok = ForceForeground(target);
                if (!ok) ok = ForegroundIs(target);   // FF's verdict can lag the real foreground; trust the latter
                if (!ok) ok = WaitForForegroundLanding(target);   // see below: fg==0 is a transition, not a failure
                EndSession();
                IntPtr now = NativeMethods.GetForegroundWindow();
                Log("commit -> 0x" + target.ToInt64().ToString("X") + " " + LogText(Path.GetFileName(targetExe))
                    + " sfw=" + (ok ? "ok" : "FAILED")
                    + " fgNow=0x" + now.ToInt64().ToString("X") + " [" + ClassNameOf(now) + "]");
            }
            finally { _committing = false; }
        }

        // fg == 0 after a failed activation is NOT "nobody holds the focus" -
        // it is the system's mid-transition state while the SetForegroundWindow
        // ForceForeground kicked off is still in flight. Measured on this
        // machine (elevated probe against the LoL window): the transition
        // completes 19-110ms after the call. The old code read fg==0 as
        // "focus-dead" and immediately SetForegroundWindow'd the SOURCE back,
        // actively canceling the activation it had just requested - which is
        // the confirmed root cause of "switching TO the game needs a second
        // Alt+Tab" (4/4 failures in the 2026-09-10 log, each ending in
        // "commit fallback: restoring source" 1ms after force-fg FAILED).
        //
        // So: wait in bounded chunks (Sleep + DoEvents keeps the low-level
        // hooks serviced) and re-check. If a foreign window instead ends up
        // solidly holding the foreground (3 consecutive readings), the
        // transition resolved elsewhere and waiting longer is pointless -
        // leave it alone, exactly like the fallback below does.
        static bool WaitForForegroundLanding(IntPtr target)
        {
            const int budgetMs = 300;
            HideOverlay();   // no-op when FF's slow path already hid it; covers the IsWindow-false early-out path
            var sw = Stopwatch.StartNew();
            string lastFg = "0x0";
            int foreignRun = 0;
            while (sw.ElapsedMilliseconds < budgetMs)
            {
                System.Threading.Thread.Sleep(10);
                Application.DoEvents();
                IntPtr now = NativeMethods.GetForegroundWindow();
                if (ForegroundIs(target))
                {
                    Log("  commit: late landing of 0x" + target.ToInt64().ToString("X")
                        + " after " + sw.ElapsedMilliseconds + "ms");
                    return true;
                }
                if (now != IntPtr.Zero && !IsOwnWindow(now))
                {
                    lastFg = "0x" + now.ToInt64().ToString("X") + " [" + ClassNameOf(now) + "]";
                    if (++foreignRun >= 3) break;   // ~30ms: another window solidly won the transition
                }
                else foreignRun = 0;
            }
            Log("  commit: no late landing within " + budgetMs + "ms, fg last seen at " + lastFg);
            return false;
        }

        static void Cancel()
        {
            // Same reentrancy guard as Commit: a nested Cancel (pumped in via
            // the DoEvents below or inside ForceForeground) would tear the
            // session down under an in-flight Commit's feet.
            if (_committing) { Log("cancel swallowed: reentrant"); return; }
            if (!_session) return;
            _committing = true;
            try
            {
                EndSession();
                Log("cancel");
            }
            finally { _committing = false; }
        }


        static void EndSession()
        {
            // Reentrancy guard first: the watchdog timer must not re-enter
            // Commit/Cancel while we tear down.
            _session = false;
            if (_refreshTimer != null) _refreshTimer.Stop();
            UninstallMouseHook();
            HideOverlay();
            foreach (var e in _apps)
            {
                // Icons are owned by the per-exe cache, NOT by the session:
                // they survive so the next Alt+Tab does not pay for another
                // WM_GETICON round-trip into every other process.
                e.Icon = null;
            }
            _apps.Clear();
        }

        // Give up on the cycle without switching anywhere: used when the
        // refresh finds there is nothing left to switch to.
        static void AbortSession()
        {
            if (_committing) { Log("abort swallowed: reentrant"); return; }
            _committing = true;
            try
            {
                EndSession();
                Log("session aborted: no windows left");
            }
            finally { _committing = false; }
        }

        // ================= live refresh =================
        // The cycle is a snapshot, but the desktop does not stop: a window
        // can be closed (or hidden) while the overlay is up, and the bar has
        // to follow. The check itself is deliberately cheap - a couple of
        // user32 queries per entry - and the expensive part (re-enumerate,
        // relayout, re-register thumbnails, repaint) only runs when something
        // actually changed, which is rare.
        static void RefreshTick()
        {
            if (!_session || _committing) return;
            for (int i = 0; i < _apps.Count; i++)
            {
                IntPtr h = _apps[i].ReprHwnd;
                if (!NativeMethods.IsWindow(h) || !NativeMethods.IsWindowVisible(h) || IsCloaked(h))
                {
                    Log("refresh: entry \"" + LogText(_apps[i].Title) + "\" is gone");
                    RefreshEntries();
                    return;
                }
            }
        }

        static void RefreshEntries()
        {
            if (_committing) return;
            var fresh = EnumerateEntries();
            if (fresh.Count == 0) { AbortSession(); return; }

            string selKey = _index >= 0 && _index < _apps.Count ? _apps[_index].Key : null;

            // Surviving entries keep their slot: match the fresh snapshot
            // against the running order by key, then append genuinely new
            // entries at the end. Re-indexing from scratch would reshuffle
            // the bar under the user's finger.
            var byKey = new Dictionary<string, AppEntry>(StringComparer.Ordinal);
            foreach (var f in fresh) byKey[f.Key] = f;

            var merged = new List<AppEntry>(fresh.Count);
            foreach (var old in _apps)
            {
                AppEntry f;
                if (!byKey.TryGetValue(old.Key, out f)) continue;
                byKey.Remove(old.Key);
                // Same exe => same icon; carry the cached one over instead of
                // re-querying the window.
                f.Icon = old.Icon;
                old.Icon = null;
                merged.Add(f);
            }
            foreach (var kv in byKey)
            {
                kv.Value.Icon = CachedIcon(kv.Value.Exe, kv.Value.ReprHwnd);
                merged.Add(kv.Value);
            }
            // Entries left over in the old list died with their window; their
            // icons belong to the cache (never disposed by a session), so
            // there is nothing to release here.
            _apps = merged;
            int idx = 0;
            if (selKey != null)
                for (int i = 0; i < _apps.Count; i++)
                    if (_apps[i].Key == selKey) { idx = i; break; }
            _index = idx;

            Logic.ComputeLayout(_work, _apps.Count, _scale, ref _layout);
            _pageStart = Logic.PageStartFor(_index, _apps.Count, _layout.pageSize);
            ShowPanel();
            UpdatePanelRect();
            RegisterThumbnails();
            RenderChrome();
            Log("refresh: " + _apps.Count + " entries, index=" + _index
                + ", panel=" + _layout.panelW + "x" + _layout.panelH
                + " rows=" + _layout.rows + " cols=" + _layout.cols);
        }

        static bool ForegroundIs(IntPtr hwnd)
        {
            IntPtr now = NativeMethods.GetForegroundWindow();
            if (now == IntPtr.Zero) return false;
            if (now == hwnd) return true;
            return RepresentativeOf(now) == hwnd || RepresentativeOf(hwnd) == now;
        }

        // True when hwnd belongs to this process (panel / chrome / msg window).
        // Used by Commit's fallback: foreground parked on one of our own
        // windows counts as "nobody holds it" and may be reclaimed.
        static bool IsOwnWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            return pid != 0 && pid == (uint)Process.GetCurrentProcess().Id;
        }

        // Window Hopper activation: attach to the current foreground thread
        // before raising the target; detach before pumping messages or logging.
        static bool ForceForeground(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindow(hwnd)) return false;
            if (ForegroundIs(hwnd)) return true;
            var sw = Stopwatch.StartNew();

            if (NativeMethods.IsIconic(hwnd))
            {
                NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_RESTORE);
                // SW_RESTORE is asynchronous; activating while the window is
                // still minimized is refused. Bounded so a target that never
                // restores cannot stall the commit.
                for (int waited = 0; waited < 250 && NativeMethods.IsIconic(hwnd); waited += 10)
                {
                    System.Threading.Thread.Sleep(10);
                    Application.DoEvents();
                }
                Log("  activation restore waited=" + sw.ElapsedMilliseconds + "ms iconic=" + NativeMethods.IsIconic(hwnd));
            }

            IntPtr source = NativeMethods.GetForegroundWindow();
            // Commit is posted by the input hook. Synchronize with the source
            // before transferring foreground so it can process the Alt release.
            IntPtr inputSource = source;
            if (ClassNameOf(source) == ClassAppFrame)
            {
                IntPtr core = UwpCoreWindowOf(source);
                if (core != IntPtr.Zero) inputSource = core;
            }
            WaitForForegroundNotification(inputSource, 50);
            uint fgThread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), IntPtr.Zero);
            uint myThread = NativeMethods.GetCurrentThreadId();
            uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, IntPtr.Zero);
            // Joining queues changes their shared focus. Preserve the selected
            // window's existing child focus before attaching either queue.
            IntPtr focus = hwnd;
            var gui = new NativeMethods.GUITHREADINFO();
            gui.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.GUITHREADINFO));
            if (NativeMethods.GetGUIThreadInfo(targetThread, ref gui)
                && gui.hwndFocus != IntPtr.Zero
                && (gui.hwndFocus == hwnd || NativeMethods.IsChild(hwnd, gui.hwndFocus)))
                focus = gui.hwndFocus;
            bool targetJoined = false;
            bool joined = fgThread != 0 && fgThread != myThread;
            if (joined && !NativeMethods.AttachThreadInput(myThread, fgThread, true))
            {
                joined = false;
                Log("  activation attach failed err=" + Marshal.GetLastWin32Error());
            }
            try
            {
                // Window Hopper cycles within an app. Our target can own a
                // different input queue, so join it as well before activation.
                if (targetThread != 0 && targetThread != myThread && targetThread != fgThread)
                {
                    targetJoined = NativeMethods.AttachThreadInput(myThread, targetThread, true);
                    if (!targetJoined)
                    {
                        Log("  activation target attach failed err=" + Marshal.GetLastWin32Error());
                    }
                }
                // Restore focus in the joined queue before requesting foreground.
                // The reverse order was refused after long-held gestures from
                // a console source with no GUI focus; short MRU taps hid it.
                NativeMethods.SetFocus(focus);
                NativeMethods.BringWindowToTop(hwnd);
                NativeMethods.SetForegroundWindow(hwnd);
            }
            finally
            {
                if (targetJoined) NativeMethods.AttachThreadInput(myThread, targetThread, false);
                if (joined) NativeMethods.AttachThreadInput(myThread, fgThread, false);
            }

            if (!ForegroundIs(hwnd) && sw.ElapsedMilliseconds < 200)
            {
                uint remaining = (uint)Math.Max(1L, 200L - sw.ElapsedMilliseconds);
                WaitForForegroundNotification(hwnd, remaining);
            }
            Application.DoEvents();

            if (ForegroundIs(hwnd))
            {
                Log("  force-fg 0x" + hwnd.ToInt64().ToString("X") + " ok after " + sw.ElapsedMilliseconds + "ms");
                return true;
            }
            IntPtr stuck = NativeMethods.GetForegroundWindow();
            Log("  force-fg 0x" + hwnd.ToInt64().ToString("X") + " FAILED, fg stuck at 0x"
                + stuck.ToInt64().ToString("X") + " [" + ClassNameOf(stuck) + "] \""
                + LogText(GetWindowTitle(stuck)) + "\" after " + sw.ElapsedMilliseconds + "ms");
            return false;
        }

        static bool WaitForForegroundNotification(IntPtr hwnd, uint timeoutMs)
        {
            IntPtr unused;
            return NativeMethods.SendMessageTimeoutW(hwnd, 0 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_ERRORONEXIT, timeoutMs, out unused) != IntPtr.Zero;
        }

        // ================= chrome rendering (UpdateLayeredWindow, Hopper-style) =================
        static void RenderChrome()
        {
            int w = _layout.panelW, h = _layout.panelH;
            if (w <= 0 || h <= 0) return;
            bool light = LightTheme();
            Color accent = AccentColor();
            // One font + one text brush per repaint instead of one per card.
            // RenderChrome runs on every Tab keypress; constructing (and
            // disposing) a Font per tile was pure overhead on the hot path.
            // Fields rather than locals so the drawing helpers can reach them.
            _headerFont = new Font("Segoe UI", Logic.Scaled(_scale, 14), GraphicsUnit.Pixel);
            _headerBrush = new SolidBrush(HeaderTextC(light));

            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            var bmi = new NativeMethods.BITMAPINFOHEADER();
            bmi.biSize = Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
            bmi.biWidth = w; bmi.biHeight = -h; bmi.biPlanes = 1; bmi.biBitCount = 32; bmi.biCompression = 0;
            IntPtr bits;
            IntPtr dib = NativeMethods.CreateDIBSection(screenDc, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) { _headerBrush.Dispose(); _headerFont.Dispose(); NativeMethods.ReleaseDC(IntPtr.Zero, screenDc); return; }

            try
            {
                using (var bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                    var panelPath = RoundRect(0, 0, w, h, Logic.Scaled(_scale, 8));
                    using (var pen = new Pen(PanelStrokeC(light), Math.Max(1, Logic.Scaled(_scale, 1))))
                        g.DrawPath(pen, panelPath);
                    panelPath.Dispose();

                    DrawCards(g, w, h, light, accent);
                    DrawPageIndicator(g, w, h, light);
                    g.Flush(FlushIntention.Sync);
                }
                SubmitChrome(w, h, screenDc, dib);
            }
            finally
            {
                NativeMethods.DeleteObject(dib);
                _headerBrush.Dispose();
                _headerFont.Dispose();
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        // Draws every card on the current page. Each card's GraphicsPaths are
        // released in a finally so a draw error on one card cannot leak the
        // rest of the page's paths.
        static void DrawCards(Graphics g, int w, int h, bool light, Color accent)
        {
            int pageEnd = Math.Min(_pageStart + _layout.pageSize, _apps.Count);
            for (int i = _pageStart, slot = 0; i < pageEnd; ++i, ++slot)
            {
                var app = _apps[i];
                NativeMethods.RECT tile = Logic.TileRect(ref _layout, slot);
                bool sel = i == _index;
                NativeMethods.RECT pv = Logic.PreviewRect(ref _layout, tile);

                GraphicsPath cardPath = RoundRect(tile.Left, tile.Top, tile.Right, tile.Bottom, _layout.radius);
                GraphicsPath ringIn = null, ringOut = null, hole = null;
                try
                {
                    using (var b = new SolidBrush(CardColor(light))) g.FillPath(b, cardPath);
                    using (var p = new Pen(CardStrokeC(light), Math.Max(1, Logic.Scaled(_scale, 1)))) g.DrawPath(p, cardPath);

                    int pw = pv.Right - pv.Left, ph = pv.Bottom - pv.Top;
                    if (pw > 0 && ph > 0)
                    {
                        var pvF = new RectangleF(pv.Left, pv.Top, pw, ph);
                        using (var back = new SolidBrush(CardSolid(light)))
                        {
                            g.SetClip(cardPath);
                            g.FillRectangle(back, pv.Left, pv.Top, pw, ph);
                            g.ResetClip();
                        }
                        if (app.Thumb != IntPtr.Zero)
                        {
                            // Punch the preview area transparent: the live DWM
                            // thumbnail is hosted by the panel window underneath
                            // and shows through the hole.
                            hole = BottomRoundRect(pvF, _layout.radius);
                            g.CompositingMode = CompositingMode.SourceCopy;
                            using (var tr = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
                                g.FillPath(tr, hole);
                            g.CompositingMode = CompositingMode.SourceOver;
                            hole.Dispose(); hole = null;
                        }
                        // No thumbnail: leave the card solid. Nothing is drawn
                        // (in particular no oversized app icon) - DWM supplies
                        // the window's last composed frame even after it is
                        // minimized, so an empty tile means DWM genuinely has no
                        // content, and a big icon there only drew attention to
                        // the gap.
                    }

                    NativeMethods.RECT hdr = Logic.HeaderRect(ref _layout, tile);
                    int textLeft = hdr.Left;
                    if (app.Icon != null)
                    {
                        int iy = tile.Top + (_layout.headerH - _layout.iconSize) / 2;
                        g.DrawIcon(app.Icon, new Rectangle(hdr.Left, iy, _layout.iconSize, _layout.iconSize));
                        textLeft = hdr.Left + _layout.iconSize + Logic.Scaled(_scale, 8);
                    }
                    string title = string.IsNullOrEmpty(app.Title) ? Path.GetFileName(app.Exe) : app.Title;
                    var rect = new RectangleF(textLeft, tile.Top, hdr.Right - textLeft, _layout.headerH);
                    using (var sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center })
                        g.DrawString(title, _headerFont, _headerBrush, rect, sf);

                    if (sel)
                    {
                        int gPad = Logic.Scaled(_scale, 6), gOut = gPad + Logic.Scaled(_scale, 2);
                        int outerR = Logic.Scaled(_scale, 18), innerR = outerR - Logic.Scaled(_scale, 2);
                        ringIn = RoundRect(tile.Left - gPad, tile.Top - gPad, tile.Right + gPad, tile.Bottom + gPad, innerR);
                        ringOut = RoundRect(tile.Left - gOut, tile.Top - gOut, tile.Right + gOut, tile.Bottom + gOut, outerR);
                        using (var p1 = new Pen(FocusShadowC(light), Math.Max(1, Logic.Scaled(_scale, 1))))
                            g.DrawPath(p1, ringIn);
                        using (var p2 = new Pen(accent, Math.Max(2, Logic.Scaled(_scale, 4))))
                            g.DrawPath(p2, ringOut);
                    }
                }
                finally
                {
                    if (hole != null) hole.Dispose();
                    if (ringIn != null) ringIn.Dispose();
                    if (ringOut != null) ringOut.Dispose();
                    cardPath.Dispose();
                }
            }
        }

        static void DrawPageIndicator(Graphics g, int w, int h, bool light)
        {
            int pageSize = _layout.pageSize > 0 ? _layout.pageSize : 1;
            int totalPages = (_apps.Count + pageSize - 1) / pageSize;
            if (totalPages <= 1) return;
            int cur = _pageStart / pageSize + 1;
            var rect = new RectangleF(_layout.pad, h - _layout.pad, w - 2 * _layout.pad, _layout.pad);
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far })
                g.DrawString(cur + " / " + totalPages, _headerFont, _headerBrush, rect, sf);
        }

        // Pushes the finished surface to the layered chrome window. The memory
        // DC is released in a finally: a throw between CreateCompatibleDC and
        // DeleteDC would otherwise leak a GDI DC for the process lifetime.
        static void SubmitChrome(int w, int h, IntPtr screenDc, IntPtr dib)
        {
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) { Log("chrome: CreateCompatibleDC failed"); return; }
            IntPtr old = IntPtr.Zero;
            try
            {
                old = NativeMethods.SelectObject(memDc, dib);
                var dst = new NativeMethods.POINT { X = _layout.panelX, Y = _layout.panelY };
                var size = new NativeMethods.SIZE { cx = w, cy = h };
                var src = new NativeMethods.POINT { X = 0, Y = 0 };
                var blend = new NativeMethods.BLENDFUNCTION { BlendOp = NativeMethods.AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = NativeMethods.AC_SRC_ALPHA };
                bool ulw = NativeMethods.UpdateLayeredWindow(_chrome.Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
                if (!ulw) { Log("NativeMethods.UpdateLayeredWindow failed err=" + Marshal.GetLastWin32Error()); return; }

                // UpdateLayeredWindow does NOT make a hidden window visible;
                // Hopper shows both windows explicitly (SWP_SHOWWINDOW).
                _chrome.Show();
                NativeMethods.SetWindowPos(_chrome.Handle, IntPtr.Zero, 0, 0, 0, 0,
                             0x1 | 0x2 | 0x10 | 0x40 /*NOSIZE|NOMOVE|NOACTIVATE|SHOWWINDOW*/);
                // keep the opaque panel strictly below the chrome layer
                NativeMethods.SetWindowPos(_panel.Handle, _chrome.Handle, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
            }
            finally
            {
                if (old != IntPtr.Zero) NativeMethods.SelectObject(memDc, old);
                NativeMethods.DeleteDC(memDc);
            }
        }

        static GraphicsPath RoundRect(int l, int t, int r, int b, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            if (d <= 0 || d >= r - l || d >= b - t) { p.AddRectangle(new Rectangle(l, t, r - l, b - t)); return p; }
            p.AddArc(l, t, d, d, 180, 90);
            p.AddArc(r - d, t, d, d, 270, 90);
            p.AddArc(r - d, b - d, d, d, 0, 90);
            p.AddArc(l, b - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static GraphicsPath BottomRoundRect(RectangleF r, int rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d <= 0 || d >= r.Width || d >= r.Height) { p.AddRectangle(r); return p; }
            p.AddLine(r.X, r.Y, r.Right, r.Y);
            p.AddLine(r.Right, r.Y, r.Right, r.Bottom - rad);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ================= keyboard hook =================
        static bool AltDown()
        {
            return (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LMENU) & 0x8000) != 0
                || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RMENU) & 0x8000) != 0
                || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
        }

        static bool WinDown()
        {
            return (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0
                || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0;
        }

        static IntPtr KbHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var s = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                if (s.dwExtraInfo == (IntPtr)ReplayInputTag.ToUInt64())
                    return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                bool up = (s.flags & NativeMethods.LLKHF_UP) != 0;
                if (s.vkCode == NativeMethods.VK_TAB && up)
                {
                    // If any repeat was forwarded (disable, commit or failed
                    // post), Windows needs the release to clear that key state.
                    bool consume = _tabHookDown && !_tabPassedDown;
                    _tabHookDown = false;
                    _tabPassedDown = false;
                    if (consume) return (IntPtr)1;
                }
                if (_enabled && !_committing)
                {
                    if (s.vkCode == NativeMethods.VK_TAB && !up
                        && ((s.flags & NativeMethods.LLKHF_ALTDOWN) != 0 || AltDown()) && !WinDown())
                    {
                        bool reverse = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
                        bool posted = !_session ? PostAt(NativeMethods.WM_APP_START, reverse ? 1 : 0)
                            : Post(reverse ? NativeMethods.WM_APP_PREV : NativeMethods.WM_APP_NEXT);
                        if (posted) { _tabHookDown = true; return (IntPtr)1; }
                    }
                    if (_session && s.vkCode == NativeMethods.VK_ESCAPE)
                    {
                        if (up) Post(NativeMethods.WM_APP_CANCEL);
                        return (IntPtr)1;
                    }
                    if (_session && up && (s.vkCode == NativeMethods.VK_MENU
                        || s.vkCode == NativeMethods.VK_LMENU || s.vkCode == NativeMethods.VK_RMENU))
                        Post(NativeMethods.WM_APP_COMMIT);
                }
                if (s.vkCode == NativeMethods.VK_TAB && !up) _tabPassedDown = true;
            }
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // ================= mouse hook =================
        static IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _session && _enabled)
            {
                int msg = wParam.ToInt32();
                var s = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                if (msg == NativeMethods.WM_LBUTTONDOWN)
                {
                    bool insidePanel = s.pt.X >= _panelRect.Left && s.pt.X < _panelRect.Right
                                    && s.pt.Y >= _panelRect.Top && s.pt.Y < _panelRect.Bottom;
                    if (insidePanel)
                    {
                        int slot = SlotAtPhysical(s.pt.X, s.pt.Y);
                        if (slot >= 0)
                        {
                            PostAt(NativeMethods.WM_APP_COMMITAT, _pageStart + slot);
                            return (IntPtr)1;
                        }
                        return IntPtr.Zero;   // panel background: ignore
                    }
                    Post(NativeMethods.WM_APP_CANCEL);
                    return (IntPtr)1;         // swallow outside clicks
                }
                if (msg == NativeMethods.WM_MOUSEWHEEL)
                {
                    short delta = (short)((s.mouseData >> 16) & 0xFFFF);
                    Post(delta > 0 ? NativeMethods.WM_APP_PREV : NativeMethods.WM_APP_NEXT);
                    return (IntPtr)1;
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        // Hit-test rect of the overlay, in physical screen coordinates. The
        // mouse hook compares against it; it has to follow a relayout (a
        // window closing mid-cycle resizes the panel).
        static void UpdatePanelRect()
        {
            _panelRect = new NativeMethods.RECT
            {
                Left = _layout.panelX,
                Top = _layout.panelY,
                Right = _layout.panelX + _layout.panelW,
                Bottom = _layout.panelY + _layout.panelH
            };
        }

        static void InstallMouseHook()
        {
            UpdatePanelRect();
            if (_mouseHook == IntPtr.Zero)
                _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookProc, NativeMethods.GetModuleHandle(null), 0);
        }

        static void UninstallMouseHook()
        {
            if (_mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        }

        static int SlotAtPhysical(int px, int py)
        {
            int lx = px - _panelRect.Left, ly = py - _panelRect.Top;
            for (int slot = 0; slot < _layout.pageSize; slot++)
            {
                NativeMethods.RECT t = Logic.TileRect(ref _layout, slot);
                if (lx >= t.Left && lx < t.Right && ly >= t.Top && ly < t.Bottom)
                    return slot;
            }
            return -1;
        }

        // ================= infra =================
        static bool IsUnderDirectory(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
            return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        static bool HasProtectedAcl(FileSystemSecurity security)
        {
            string owner = security.GetOwner(typeof(SecurityIdentifier)).Value;
            if (!TrustedOwner(owner)) return false;
            const FileSystemRights writes = FileSystemRights.WriteData | FileSystemRights.AppendData
                | FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes
                | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
                | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership
                | (FileSystemRights)0x50000000; // GENERIC_WRITE | GENERIC_ALL
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow
                    && (rule.FileSystemRights & writes) != 0
                    && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0
                    && !TrustedOwner(rule.IdentityReference.Value)) return false;
            }
            return true;
        }

        static bool TrustedOwner(string sid)
        {
            return sid == "S-1-5-18" || sid == "S-1-5-32-544"
                || sid == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
        }

        static bool IsProtectedStartupPath(string path)
        {
            try
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!IsUnderDirectory(path, root))
                {
                    root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    if (!IsUnderDirectory(path, root)) return false;
                }
                path = Path.GetFullPath(path);
                root = Path.GetFullPath(root).TrimEnd('\\');
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                    || !HasProtectedAcl(File.GetAccessControl(path))) return false;
                string dir = Path.GetDirectoryName(path);
                while (dir != null)
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0
                        || !HasProtectedAcl(Directory.GetAccessControl(dir))) return false;
                    if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)) return true;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (System.Security.SecurityException) { }
            return false;
        }

        static bool SetStartup(bool add)
        {
            if (add && !IsProtectedStartupPath(Application.ExecutablePath)) return false;
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (add) key.SetValue("AppHopper", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("AppHopper", false);
                }
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
            catch (System.Security.SecurityException) { return false; }
        }

        static bool StartupEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return key != null && key.GetValue("AppHopper") != null;
            }
            catch { return false; }
        }

        // ================= on-demand GitHub updates =================
        // A protected copy of this version replaces the exe after our exit.
        // Downloaded code is never executed before checking GitHub's digest.
        static class Updates
        {
            const string Api = "https://api.github.com/repos/informalgit/AppHopper/releases/latest";
            const string DownloadRoot = "https://github.com/informalgit/AppHopper/releases/download/";
            const long MaxAssetSize = 64 * 1024 * 1024;
            static bool _busy; // UI-thread-owned: one check/confirmation/download at a time

            internal sealed class Release
            {
                internal string Tag, Digest, Url;
                internal Version Version;
                internal long Size;
            }

            internal static Version ParseVersion(string value)
            {
                Version version;
                if (value != null && value.StartsWith("v", StringComparison.Ordinal)) value = value.Substring(1);
                if (!Version.TryParse(value, out version)) throw new InvalidDataException("Invalid release version.");
                return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
            }

            internal static Release ParseRelease(string json)
            {
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
                var data = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (data == null || (bool)data["draft"] || (bool)data["prerelease"])
                    throw new InvalidDataException("The release is not a public stable release.");
                string tag = (string)data["tag_name"];
                Version version = ParseVersion(tag);
                string expectedUrl = DownloadRoot + Uri.EscapeDataString(tag) + "/AppHopper.exe";
                Release found = null;
                foreach (object item in (object[])data["assets"])
                {
                    var asset = item as Dictionary<string, object>;
                    if (asset == null || !string.Equals(asset["name"] as string, "AppHopper.exe", StringComparison.Ordinal)) continue;
                    if (found != null) throw new InvalidDataException("Duplicate AppHopper.exe assets.");
                    string digest = asset.ContainsKey("digest") ? asset["digest"] as string : null;
                    if (digest == null || !digest.StartsWith("sha256:", StringComparison.Ordinal) || !ValidHash(digest.Substring(7)))
                        throw new InvalidDataException("The release asset has no valid SHA-256 digest.");
                    string url = asset["browser_download_url"] as string;
                    long size = Convert.ToInt64(asset["size"]);
                    if (url != expectedUrl || size <= 0 || size > MaxAssetSize)
                        throw new InvalidDataException("Invalid release download URL or size.");
                    found = new Release { Tag = tag, Version = version, Digest = digest.Substring(7), Url = url, Size = size };
                }
                if (found == null) throw new InvalidDataException("This release has no AppHopper.exe asset.");
                return found;
            }

            static bool ValidHash(string value)
            {
                if (value == null || value.Length != 64) return false;
                foreach (char c in value)
                    if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;
                return true;
            }

            static HttpWebRequest Request(string url)
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2 on .NET Framework 4
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.UserAgent = "AppHopper/" + AppVersion;
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                return request;
            }

            internal static Release Latest()
            {
                var request = Request(Api);
                request.Accept = "application/vnd.github+json";
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    return ParseRelease(reader.ReadToEnd());
            }

            internal static string HashFile(string path)
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var hash = SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }

            internal static void Download(Release release, string path)
            {
                using (var response = (HttpWebResponse)Request(release.Url).GetResponse())
                using (var input = response.GetResponseStream())
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var hash = SHA256.Create())
                {
                    byte[] buffer = new byte[32768];
                    long total = 0;
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        total += count;
                        if (total > release.Size) throw new InvalidDataException("The download exceeds its published size.");
                        output.Write(buffer, 0, count);
                        hash.TransformBlock(buffer, 0, count, null, 0);
                    }
                    hash.TransformFinalBlock(buffer, 0, 0);
                    string digest = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                    if (total != release.Size || !string.Equals(digest, release.Digest, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Download verification failed. The existing application was not changed.");
                }
                if (ParseVersion(FileVersionInfo.GetVersionInfo(path).FileVersion) != release.Version)
                    throw new InvalidDataException("The executable version does not match the release.");
            }

            static string StagePath(string id)
            {
                Guid guid;
                if (!Guid.TryParseExact(id, "N", out guid)) throw new InvalidDataException("Invalid update transaction.");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AppHopper-update-" + id);
            }

            internal static string CreateStage(string id)
            {
                string path = StagePath(id);
                if (Directory.Exists(path)) throw new IOException("The update directory already exists.");
                var security = new DirectorySecurity();
                security.SetOwner(new SecurityIdentifier("S-1-5-32-544"));
                security.SetAccessRuleProtection(true, false);
                foreach (string sid in new string[] { "S-1-5-18", "S-1-5-32-544" })
                    security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.CreateDirectory(path, security);
                ValidateStage(path);
                return path;
            }

            static void ValidateStage(string path)
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || !HasProtectedAcl(Directory.GetAccessControl(path)))
                    throw new UnauthorizedAccessException("The update directory is not protected.");
                string root = Path.GetDirectoryName(path);
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("The update root is redirected.");
                var security = Directory.GetAccessControl(root);
                if (!TrustedOwner(security.GetOwner(typeof(SecurityIdentifier)).Value))
                    throw new UnauthorizedAccessException("The update root has an untrusted owner.");
                const FileSystemRights mutation = FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
                    | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | (FileSystemRights)0x10000000;
                foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                    if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & mutation) != 0
                        && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0 && !TrustedOwner(rule.IdentityReference.Value))
                        throw new UnauthorizedAccessException("The update root permits untrusted deletion.");
            }

            static EventWaitHandle MakeEvent(string name)
            {
                var security = new EventWaitHandleSecurity();
                foreach (string sid in new string[] { "S-1-5-18", "S-1-5-32-544" })
                    security.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(sid), EventWaitHandleRights.FullControl, AccessControlType.Allow));
                security.SetAccessRuleProtection(true, false);
                bool created;
                var handle = new EventWaitHandle(false, EventResetMode.ManualReset, name, out created, security);
                if (!created) { handle.Dispose(); throw new IOException("The update signal already exists."); }
                return handle;
            }

            static string LaunchArguments(int flags)
            {
                return ((flags & 4) != 0 ? " --log-verbose" : (flags & 2) != 0 ? " --log" : "")
                    + ((flags & 1) != 0 ? " --disabled" : "");
            }

            static Process Start(string path, string arguments)
            {
                return Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) });
            }

            static void Cleanup(string stage, bool helperRunning)
            {
                foreach (string name in new string[] { "AppHopper.exe", "status.txt", "helper.exe" })
                {
                    string path = Path.Combine(stage, name);
                    if (helperRunning && name == "helper.exe")
                    {
                        NativeMethods.MoveFileEx(path, null, 4); // mapped helper: delete at the next reboot
                        continue;
                    }
                    if (File.Exists(path)) File.Delete(path);
                }
                if (helperRunning) NativeMethods.MoveFileEx(stage, null, 4);
                else Directory.Delete(stage);
            }

            static void Prepare(Release release, string target, int flags)
            {
                string id = Guid.NewGuid().ToString("N");
                string stage = CreateStage(id);
                Process helper = null;
                bool handedOff = false;
                try
                {
                    Download(release, Path.Combine(stage, "AppHopper.exe"));
                    File.Copy(target, Path.Combine(stage, "helper.exe"));
                    using (var signal = MakeEvent("Local\\AppHopperUpdatePrepare-" + id))
                    using (var parent = Process.GetCurrentProcess())
                    {
                        helper = Start(Path.Combine(stage, "helper.exe"), "--apply-update " + parent.Id + " " + parent.StartTime.Ticks
                            + " \"" + target + "\" " + release.Digest + " " + release.Version + " " + id + " " + flags);
                        if (!signal.WaitOne(20000)) throw new IOException("The update helper did not respond.");
                        string status = File.ReadAllText(Path.Combine(stage, "status.txt"));
                        if (status != "ready") throw new IOException(status);
                        handedOff = true;
                    }
                }
                finally
                {
                    if (helper != null)
                    {
                        if (!handedOff && !helper.HasExited) { helper.Kill(); helper.WaitForExit(); }
                        helper.Dispose();
                    }
                    if (!handedOff) Cleanup(stage, false);
                }
            }

            static void Idle(MenuItem item)
            {
                _busy = false;
                item.Enabled = true;
                item.Text = "Get updates...";
            }

            internal static void Click(MenuItem item, MenuItem exit)
            {
                if (_busy) return;
                _busy = true;
                item.Enabled = false;
                item.Text = "Checking for updates...";
                var check = new BackgroundWorker();
                check.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = Latest(); };
                check.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
                {
                    check.Dispose();
                    if (_exitRequested) return;
                    if (e.Error != null) { MessageBox.Show("Unable to check for updates.\n\n" + e.Error.Message, "AppHopper", MessageBoxButtons.OK, MessageBoxIcon.Warning); Idle(item); return; }
                    var release = (Release)e.Result;
                    if (release.Version <= ParseVersion(AppVersion))
                    {
                        MessageBox.Show("You are already running the latest version (" + AppVersion + ").", "AppHopper", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Idle(item);
                        return;
                    }
                    if (MessageBox.Show("AppHopper " + release.Tag + " is available.\nDownload, replace this executable and restart now?",
                        "AppHopper", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { Idle(item); return; }
                    item.Enabled = false;
                    item.Text = "Downloading update...";
                    exit.Enabled = false;
                    string target = Application.ExecutablePath;
                    int flags = (_enabled ? 0 : 1) | (_log != null ? 2 : 0) | (_logVerbose ? 4 : 0);
                    var download = new BackgroundWorker();
                    download.DoWork += delegate { Prepare(release, target, flags); };
                    download.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs result)
                    {
                        download.Dispose();
                        if (result.Error == null) { _exitRequested = true; return; }
                        exit.Enabled = true;
                        MessageBox.Show("The update was not installed. AppHopper is still running.\n\n" + result.Error.Message,
                            "AppHopper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        Idle(item);
                    };
                    download.RunWorkerAsync();
                };
                check.RunWorkerAsync();
            }

            internal static void SignalStarted(string id)
            {
                Guid parsed;
                if (!Guid.TryParseExact(id, "N", out parsed)) throw new InvalidDataException("Invalid startup signal.");
                using (var signal = EventWaitHandle.OpenExisting("Local\\AppHopperUpdateStart-" + id, EventWaitHandleRights.Modify))
                    signal.Set();
            }

            static List<Microsoft.Win32.SafeHandles.SafeFileHandle> LockTargetDirectories(string target)
            {
                var handles = new List<Microsoft.Win32.SafeHandles.SafeFileHandle>();
                try
                {
                    string root = Path.GetPathRoot(target);
                    if (root.StartsWith("\\\\", StringComparison.Ordinal)) throw new IOException("Updates require a local executable.");
                    for (string dir = Path.GetDirectoryName(target); dir != null && dir != root; dir = Path.GetDirectoryName(dir))
                    {
                        // Deny rename/reparse mutation while the elevated helper
                        // uses this path, including portable user-writable installs.
                        var handle = NativeMethods.CreateFile(dir, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
                        handles.Add(handle);
                        if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("The installation directory is redirected.");
                    }
                    return handles;
                }
                catch
                {
                    foreach (var handle in handles) handle.Dispose();
                    throw;
                }
            }

            internal static void Apply(string[] args)
            {
                string stage = null, target = null, backup = null;
                string replacementStage = null;
                Microsoft.Win32.SafeHandles.SafeFileHandle replacementDirectory = null;
                Process parent = null, child = null;
                List<Microsoft.Win32.SafeHandles.SafeFileHandle> directories = null;
                bool replaced = false, prepared = false, validated = false;
                int flags = 0;
                try
                {
                    if (args.Length != 8) throw new InvalidDataException("Invalid updater arguments.");
                    stage = StagePath(args[6]);
                    ValidateStage(stage);
                    if (!string.Equals(Application.ExecutablePath, Path.Combine(stage, "helper.exe"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The updater is not running from its protected directory.");
                    validated = true;
                    target = Path.GetFullPath(args[3]);
                    directories = LockTargetDirectories(target);
                    if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("The target is redirected.");
                    parent = Process.GetProcessById(int.Parse(args[1]));
                    if (parent.StartTime.Ticks != long.Parse(args[2])
                        || !string.Equals(parent.MainModule.FileName, target, StringComparison.OrdinalIgnoreCase)
                        || HashFile(target) != HashFile(Application.ExecutablePath))
                        throw new InvalidDataException("The updater does not match its running parent.");
                    string candidate = Path.Combine(stage, "AppHopper.exe");
                    Version version = ParseVersion(args[5]);
                    if (!ValidHash(args[4]) || !string.Equals(HashFile(candidate), args[4], StringComparison.OrdinalIgnoreCase)
                        || ParseVersion(FileVersionInfo.GetVersionInfo(candidate).FileVersion) != version
                        || version <= ParseVersion(AppVersion))
                        throw new InvalidDataException("The candidate is not a verified newer version.");
                    flags = int.Parse(args[7]);
                    if (flags < 0 || flags > 7) throw new InvalidDataException("Invalid restart flags.");
                    backup = target + "." + args[6] + ".bak";
                    if (!string.Equals(Path.GetPathRoot(candidate), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
                    {
                        // ReplaceFile requires both executables on the same volume.
                        // Keep the helper in ProgramData; lock a protected local stage
                        // before copying into a potentially user-writable install path.
                        string path = Path.Combine(Path.GetDirectoryName(target), ".AppHopper-update-" + args[6]);
                        if (Directory.Exists(path)) throw new IOException("The replacement directory already exists.");
                        Directory.CreateDirectory(path, Directory.GetAccessControl(stage));
                        replacementDirectory = NativeMethods.CreateFile(path, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                        if (replacementDirectory.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                            || !HasProtectedAcl(Directory.GetAccessControl(path)))
                            throw new UnauthorizedAccessException("The replacement directory is not protected.");
                        replacementStage = path;
                        string localCandidate = Path.Combine(path, "AppHopper.exe");
                        File.Copy(candidate, localCandidate, false);
                        candidate = localCandidate;
                    }
                    File.WriteAllText(Path.Combine(stage, "status.txt"), "ready");
                    using (var signal = EventWaitHandle.OpenExisting("Local\\AppHopperUpdatePrepare-" + args[6], EventWaitHandleRights.Modify))
                        signal.Set();
                    prepared = true;
                    if (!parent.WaitForExit(30000)) throw new IOException("The original application did not exit; no file was replaced.");
                    using (var started = MakeEvent("Local\\AppHopperUpdateStart-" + args[6]))
                    {
                        File.Replace(candidate, target, backup);
                        replaced = true;
                        child = Start(target, "--update-started " + args[6] + LaunchArguments(flags));
                        if (!started.WaitOne(15000) || child.HasExited)
                            throw new IOException("The new version did not complete startup.");
                        File.Delete(backup);
                    }
                }
                catch (Exception error)
                {
                    Environment.ExitCode = 1;
                    string message = error.Message;
                    try
                    {
                        if (replaced)
                        {
                            if (child != null && !child.HasExited) { child.Kill(); child.WaitForExit(5000); }
                            File.Replace(backup, target, null);
                        }
                        if (prepared && parent != null && parent.HasExited && target != null) Start(target, LaunchArguments(flags)).Dispose();
                    }
                    catch (Exception rollback)
                    {
                        message += "\nRecovery failed: " + rollback.Message + "\nBackup: " + backup;
                    }
                    if (prepared) MessageBox.Show("Update failed.\n\n" + message, "AppHopper update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    else if (validated)
                    {
                        File.WriteAllText(Path.Combine(stage, "status.txt"), message);
                        using (var signal = EventWaitHandle.OpenExisting("Local\\AppHopperUpdatePrepare-" + args[6], EventWaitHandleRights.Modify))
                            signal.Set();
                    }
                }
                finally
                {
                    if (child != null) child.Dispose();
                    if (parent != null) parent.Dispose();
                    if (replacementStage != null)
                    {
                        // Remove the candidate while its directory cannot be renamed.
                        try { File.Delete(Path.Combine(replacementStage, "AppHopper.exe")); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                    if (replacementDirectory != null) replacementDirectory.Dispose();
                    if (replacementStage != null)
                    {
                        try { Directory.Delete(replacementStage); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                    if (directories != null) foreach (var handle in directories) handle.Dispose();
                    if (prepared)
                    {
                        try { Cleanup(stage, true); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
        }

        // ================= self-test =================
        // Run by `AppHopper.exe --self-test`. Everything lives in this binary:
        // there are no separate test executables to compile, ship or keep in
        // sync. The suite is checked in Main BEFORE the single-instance
        // mutex, so it also runs while the switcher is already up.
        static int _testFailures;

        static void Check(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
        }

        static void Test(string name, Action body)
        {
            try { body(); Console.WriteLine("PASS " + name); }
            catch (Exception e) { _testFailures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
        }

        // Swap in a throwaway log sink so the assertions can read what would
        // have been written, without touching the real file.
        static string CaptureLog(Action body)
        {
            var bytes = new MemoryStream();
            var writer = new StreamWriter(bytes, new UTF8Encoding(false));
            StreamWriter previous = _log;
            long previousBytes = _logBytes;
            _log = writer;
            _logBytes = 0;
            try { body(); writer.Flush(); return Encoding.UTF8.GetString(bytes.ToArray()); }
            finally { _log = previous; _logBytes = previousBytes; writer.Dispose(); }
        }

        // Drives the low-level hook with one synthetic keystroke and returns
        // its raw disposition. Non-zero means "swallowed". The pass-through
        // value itself comes from CallNextHookEx and is unspecified when no
        // hook is installed, so assertions target our own return of 1 and the
        // latches it maintains - never the pass-through value.
        static IntPtr Hook(uint vk, uint flags, IntPtr tag)
        {
            NativeMethods.KBDLLHOOKSTRUCT data = new NativeMethods.KBDLLHOOKSTRUCT();
            data.vkCode = vk;
            data.flags = flags;
            data.dwExtraInfo = tag;
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeMethods.KBDLLHOOKSTRUCT)));
            try
            {
                Marshal.StructureToPtr(data, memory, false);
                return KbHookProc(0, new IntPtr((flags & NativeMethods.LLKHF_UP) != 0 ? 0x101 : 0x100), memory);
            }
            finally { Marshal.FreeHGlobal(memory); }
        }

        static bool Swallowed(IntPtr disposition) { return disposition == new IntPtr(1); }

        static FileSecurity TestAcl(string owner, string writer)
        {
            var acl = new FileSecurity();
            acl.SetOwner(new SecurityIdentifier(owner));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-544"), FileSystemRights.FullControl, AccessControlType.Allow));
            if (writer != null) acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(writer), FileSystemRights.Write, AccessControlType.Allow));
            return acl;
        }

        // State every test restores before it exits; a failure must not leak
        // into the next one.
        static bool[] HeldKeys(NativeMethods.INPUT[] inputs, int count, bool[] held)
        {
            for (int i = 0; i < count; i++)
            {
                NativeMethods.INPUT input = inputs[i];
                held[input.data.keyboard.wVk] = (input.data.keyboard.dwFlags & NativeMethods.KEYEVENTF_KEYUP) == 0;
            }
            return held;
        }

        // True when the icon still converts to a usable bitmap. Used by the
        // ownership test: an Icon whose handle has been destroyed underneath it
        // throws here instead of silently rendering garbage.
        static bool RendersAsIcon(Icon icon)
        {
            if (icon == null || icon.Handle == IntPtr.Zero) return false;
            try
            {
                using (Bitmap bmp = icon.ToBitmap()) return bmp.Width > 0 && bmp.Height > 0;
            }
            catch { return false; }
        }

        static bool RunSelfTests()
        {
            _testFailures = 0;
            Test("update versions compare numerically and reject prerelease tags", delegate
            {
                Check(Updates.ParseVersion("v1.10") > Updates.ParseVersion("1.9.9"), "version comparison was lexical");
                Check(Updates.ParseVersion("v1.3") == Updates.ParseVersion("1.3.0.0"), "equivalent versions differed");
                bool rejected = false;
                try { Updates.ParseVersion("v1.3-rc1"); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "prerelease version accepted");
            });
            Test("update metadata rejects untrusted assets and missing integrity", delegate
            {
                string url = "https://github.com/informalgit/AppHopper/releases/download/v2.0/AppHopper.exe";
                string json = "{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v2.0\",\"assets\":["
                    + "{\"name\":\"AppHopper.exe\",\"size\":71680,\"digest\":\"sha256:" + new string('a', 64)
                    + "\",\"browser_download_url\":\"" + url + "\"}]}";
                foreach (string bad in new string[] {
                    json.Replace(url, "https://example.com/AppHopper.exe"),
                    json.Replace("\"draft\":false", "\"draft\":true"),
                    json.Replace("\"prerelease\":false", "\"prerelease\":true"),
                    json.Replace("sha256:" + new string('a', 64), "sha256:bad"),
                    json.Replace("\"size\":71680", "\"size\":0") })
                {
                    bool rejected = false;
                    try { Updates.ParseRelease(bad); }
                    catch (InvalidDataException) { rejected = true; }
                    Check(rejected, "unsafe release was accepted");
                }
            });
            Test("layout, cropping, paging and ordering", delegate
            {
                NativeMethods.RECT work = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
                Logic.OverlayLayout layout = new Logic.OverlayLayout();
                Logic.ComputeLayout(work, 2, 1.0, ref layout);
                // A 2-window cycle must draw a 2-tile bar, never the 6-column
                // maximum with four tiles of empty background either side.
                Check(layout.pageSize == 2 && layout.cols == 2 && layout.rows == 1, "narrow cycle kept the wide panel");

                Logic.ComputeLayout(work, 7, 1.0, ref layout);
                Check(layout.pageSize == 7 && layout.cols == 6 && layout.rows == 2 && layout.rowCount[1] == 1, "paged layout wrong");

                if (Logic.PageStartFor(-1, 7, 6) != 0 || Logic.PageStartFor(5, 7, 6) != 0
                    || Logic.PageStartFor(6, 7, 6) != 6 || Logic.PageStartFor(8, 7, 6) != 6) throw new Exception("paging wrong");

                NativeMethods.RECT crop = Logic.CoverSource(
                    new NativeMethods.RECT { Left = 0, Top = 0, Right = 160, Bottom = 90 },
                    new NativeMethods.RECT { Left = 0, Top = 0, Right = 400, Bottom = 300 });
                Check((crop.Right - crop.Left) * 90 == (crop.Bottom - crop.Top) * 160, "crop changed the source aspect");

                AppEntry pinned = new AppEntry { ReprHwnd = new IntPtr(1), Rank = 20, Topmost = false };
                AppEntry topmost = new AppEntry { ReprHwnd = new IntPtr(2), Rank = 1, Topmost = true };
                Check(Logic.AppSortKey(pinned, new IntPtr(1)) == -1, "pinned entry did not sort first");
                Check(Logic.AppSortKey(topmost, IntPtr.Zero) > Logic.AppSortKey(pinned, IntPtr.Zero), "topmost entry not demoted");
            });

            Test("panel is centred on the monitor the source window is on", delegate
            {
                // A second monitor offset from the origin: the bar must land
                // inside THAT monitor's work area, not on the primary one.
                NativeMethods.RECT secondary = new NativeMethods.RECT { Left = 1920, Top = -200, Right = 1920 + 2560, Bottom = -200 + 1440 };
                Logic.OverlayLayout layout = new Logic.OverlayLayout();
                Logic.ComputeLayout(secondary, 5, 1.0, ref layout);
                Check(layout.panelX >= secondary.Left && layout.panelX + layout.panelW <= secondary.Right, "panel left the source monitor");
                Check(layout.panelY >= secondary.Top && layout.panelY + layout.panelH <= secondary.Bottom, "panel left the source monitor");
                Check(layout.panelX > 0 && layout.panelY > 0, "panel fell back to the primary monitor");
            });

            Test("a single entry never draws a second tile of background", delegate
            {
                Logic.OverlayLayout layout = new Logic.OverlayLayout();
                Logic.ComputeLayout(new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }, 1, 1.0, ref layout);
                Check(layout.cols == 1 && layout.rows == 1, "one app reserved more than one slot");
                Check(layout.panelW == 2 * layout.pad + layout.tileW, "panel wider than its only tile");
            });

            Test("sensitive fields are redacted unless verbose", delegate
            {
                bool previous = _logVerbose;
                try
                {
                    _logVerbose = false;
                    string plain = CaptureLog(delegate { Log("title=" + LogText("private document")); });
                    Check(!plain.Contains("private document") && plain.Contains("<redacted>"), "private field leaked");
                    _logVerbose = true;
                    string verbose = CaptureLog(delegate { Log("title=" + LogText("secret\r\ninjected")); });
                    Check(verbose.Contains("secret\\r\\ninjected"), "verbose title not escaped");
                    Check(verbose.Split(new string[] { Environment.NewLine }, StringSplitOptions.None).Length == 2, "title forged another log record");
                }
                finally { _logVerbose = previous; }
            });

            Test("plain runs log no per-window enumeration detail", delegate
            {
                // The skip lines fire for every top-level window on the
                // machine on every Alt+Tab, and each one was a flushed disk
                // write. They must therefore only appear under --log-verbose.
                bool previous = _logVerbose;
                try
                {
                    _logVerbose = false;
                    string plain = CaptureLog(delegate { EnumerateEntries(); });
                    Check(!plain.Contains("  skip 0x"), "plain run logged per-window skip detail");
                    _logVerbose = true;
                    string verbose = CaptureLog(delegate { EnumerateEntries(); });
                    Check(verbose.Contains("  skip 0x"), "verbose run lost the enumeration detail");
                }
                finally { _logVerbose = previous; }
            });

            Test("the 8 MiB log cap holds at its exact boundary", delegate
            {
                const int cap = 8 * 1024 * 1024;
                int overhead = Encoding.UTF8.GetByteCount(DateTime.Now.ToString("HH:mm:ss.fff ") + Environment.NewLine);
                string output = CaptureLog(delegate
                {
                    Log(new string('x', cap - overhead));
                    Log("overflow");
                });
                Check(Encoding.UTF8.GetByteCount(output) == cap && !output.Contains("overflow"), "byte cap violated at exact boundary");
            });

            Test("multibyte records respect the byte cap", delegate
            {
                const int cap = 8 * 1024 * 1024;
                int overhead = Encoding.UTF8.GetByteCount(DateTime.Now.ToString("HH:mm:ss.fff ") + Environment.NewLine);
                string output = CaptureLog(delegate
                {
                    Log(new string('x', cap - overhead * 2 - 2));
                    Log(new string('\u6d4b', 9));
                    Log("ok");
                });
                Check(Encoding.UTF8.GetByteCount(output) <= cap && !output.Contains("\u6d4b") && output.Contains("ok"), "UTF-8 overflow or later record lost");
            });

            Test("a failed log stream stops writing without duplicating records", delegate
            {
                string path = Path.GetTempFileName();
                StreamWriter writer = null;
                try
                {
                    writer = new StreamWriter(path, false, new UTF8Encoding(false));
                    _log = writer; _logBytes = 0;
                    Log("retained-record");
                    writer.BaseStream.Dispose();
                    string before = File.ReadAllText(path);
                    Log("failed-record");
                    Check(_log == null, "failed writer still active");
                    Log("later-record");
                    Check(File.ReadAllText(path) == before && before.Contains("retained-record"), "failure duplicated the last persisted record");
                }
                finally
                {
                    _log = null;
                    if (writer != null) { try { writer.Dispose(); } catch (ObjectDisposedException) { } }
                    File.Delete(path);
                }
            });

            Test("autostart rejects sibling prefixes, traversal and reparse escapes", delegate
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                Check(IsUnderDirectory(Path.Combine(root, "AppHopper", "app.exe"), root), "valid descendant rejected");
                Check(!IsUnderDirectory(root + " Evil\\app.exe", root), "prefix bypass");
                Check(!IsUnderDirectory(Path.Combine(root, "..", "Users", "app.exe"), root), "traversal bypass");
                Check(!IsProtectedStartupPath(Application.ExecutablePath), "unprotected build accepted for autostart");
            });

            Test("autostart ACL permits only trusted mutation", delegate
            {
                Check(HasProtectedAcl(TestAcl("S-1-5-32-544", null)), "admin ACL rejected");
                Check(!HasProtectedAcl(TestAcl("S-1-5-32-544", "S-1-5-32-545")), "Users write accepted");
                Check(!HasProtectedAcl(TestAcl("S-1-5-32-544", "S-1-1-0")), "Everyone write accepted");
                Check(!HasProtectedAcl(TestAcl("S-1-5-32-545", null)), "untrusted owner accepted");
                foreach (string rights in new string[] { "GW", "GA" })
                {
                    var security = new FileSecurity();
                    security.SetSecurityDescriptorSddlForm("O:BAG:BAD:(A;;FA;;;BA)(A;;" + rights + ";;;BU)");
                    Check(!HasProtectedAcl(security), "untrusted " + rights + " accepted");
                }
            });

            Test("a rejected autostart leaves the Run key untouched", delegate
            {
                object before;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    before = key == null ? null : key.GetValue("AppHopper");
                Check(!SetStartup(true), "unprotected build registered itself");
                object after;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    after = key == null ? null : key.GetValue("AppHopper");
                Check(object.Equals(before, after), "rejected autostart changed the registry");
            });

            Test("native replay preserves the physical modifiers", delegate
            {
                foreach (bool addAlt in new bool[] { false, true })
                foreach (bool addShift in new bool[] { false, true })
                {
                    NativeMethods.INPUT[] inputs = NativeTabInputs(addAlt, addShift);
                    for (int sent = 0; sent <= inputs.Length; sent++)
                    {
                        var held = new bool[256];
                        held[NativeMethods.VK_MENU] = !addAlt;
                        held[NativeMethods.VK_SHIFT] = !addShift;
                        HeldKeys(inputs, sent, held);
                        NativeMethods.INPUT[] recovery = ReplayReleases(inputs, (uint)sent);
                        if (recovery != null) HeldKeys(recovery, recovery.Length, held);
                        Check(!held[NativeMethods.VK_TAB]
                            && held[NativeMethods.VK_MENU] == !addAlt
                            && held[NativeMethods.VK_SHIFT] == !addShift,
                            "replay prefix " + sent + " left a synthetic key held or released a physical modifier");
                    }
                }
            });

            Test("hook routing: failures, repeats, modifiers and replay tags", delegate
            {
                bool session = _session, enabled = _enabled, committing = _committing;
                MsgForm previousMsg = _msg;
                try
                {
                    // A Tab the app cannot post must reach the system and must
                    // not latch: the switcher never took it over.
                    _msg = null; _session = false; _enabled = true; _committing = false; _tabHookDown = false;
                    Check(!Swallowed(Hook(NativeMethods.VK_TAB, 0x20, IntPtr.Zero)), "an unpostable Alt+Tab was swallowed");
                    Check(!_tabHookDown, "an unpostable Alt+Tab was latched as consumed");
                    Check(!Swallowed(Hook(NativeMethods.VK_TAB, NativeMethods.LLKHF_UP, IntPtr.Zero)), "its release was swallowed");
                    Check(!_tabHookDown && !_tabPassedDown, "the Tab latches survived its release");

                    // A Tab we latched must have its release consumed too, or
                    // the system keeps Tab logically held after the overlay.
                    _tabHookDown = true; _tabPassedDown = false; _enabled = false;
                    Check(Swallowed(Hook(NativeMethods.VK_TAB, NativeMethods.LLKHF_UP, IntPtr.Zero)), "a latched Tab release was not swallowed");
                    Check(!_tabHookDown && !_tabPassedDown, "Tab latches survived the release");

                    // A repeat that reached the system must still clear the
                    // latch when its release arrives (mixed disposition).
                    _tabHookDown = true; _tabPassedDown = false;
                    Check(!Swallowed(Hook(NativeMethods.VK_TAB, 0, IntPtr.Zero)), "a forwarded auto-repeat was swallowed");
                    Check(_tabPassedDown, "the forwarded repeat was not recorded");
                    Check(!Swallowed(Hook(NativeMethods.VK_TAB, NativeMethods.LLKHF_UP, IntPtr.Zero)), "the forwarded repeat's release was swallowed");
                    Check(!_tabHookDown && !_tabPassedDown, "Tab latches survived a mixed-disposition release");
                    _enabled = true;

                    // Our own replayed input must not re-enter the switcher:
                    // it is passed straight through, latch untouched.
                    _tabHookDown = true;
                    Check(!Swallowed(Hook(NativeMethods.VK_TAB, NativeMethods.LLKHF_UP, new IntPtr(unchecked((int)ReplayInputTag.ToUInt64())))), "our replay was swallowed");
                    Check(_tabHookDown, "replay cleared the physical Tab latch");
                    _tabHookDown = false;

                    // Alt releases always reach the foreground app, so it sees
                    // a clean release; the commit is posted, not swallowed.
                    _session = true;
                    foreach (uint vk in new uint[] { NativeMethods.VK_MENU, NativeMethods.VK_LMENU, NativeMethods.VK_RMENU })
                        Check(!Swallowed(Hook(vk, NativeMethods.LLKHF_UP, IntPtr.Zero)), "an Alt release " + vk + " was swallowed");

                    // Nothing is consumed mid-commit, or a second switch fires.
                    _committing = true;
                    Check(!Swallowed(Hook(NativeMethods.VK_TAB, 0x20, IntPtr.Zero)), "Alt+Tab was consumed during a commit");
                    Check(!_tabHookDown, "Alt+Tab latched during a commit");
                    _enabled = true;
                }
                finally
                {
                    _session = session; _enabled = enabled; _committing = committing;
                    _tabHookDown = false; _msg = previousMsg;
                }
            });


            Test("activation notification survives a blocked and a dead target", delegate
            {
                // WM_NULL to our own message window is the same cross-thread
                // barrier the session claim uses, without a second process.
                IntPtr hwnd = _msg != null && _msg.IsHandleCreated ? _msg.Handle : IntPtr.Zero;
                Check(hwnd != IntPtr.Zero, "no message window to synchronize with");
                Check(WaitForForegroundNotification(hwnd, 1000), "responsive target was not synchronized");
                Check(!WaitForForegroundNotification(IntPtr.Zero, 50), "invalid HWND treated as synchronized");
            });

            Test("app icons are copies, so disposing one never harms another", delegate
            {
                // CopyIconSafe must hand back a COPY. Adopting a live HICON
                // would mean disposing this Icon destroys the target window's
                // own icon and blanks it on screen and in the taskbar - so the
                // property that matters is independence, which is observable:
                // disposing one copy must leave the others and the source
                // intact and renderable.
                //
                // It deliberately does NOT assert handle ownership: making the
                // copy owned costs a second Clone() per icon, and icon loading
                // runs on the Alt+Tab startup path where that measurably cost us
                // switch reliability (10 failures per 25 gestures versus 0).
                // See the note on CopyIconSafe.
                Icon source = MakeTrayIcon();
                IntPtr src = source.Handle;
                try
                {
                    Icon a = CopyIconSafe(src);
                    Icon b = CopyIconSafe(src);
                    Check(a != null && b != null, "CopyIconSafe returned nothing for a valid icon");
                    Check(a.Handle != b.Handle, "two copies shared one handle");

                    a.Dispose();          // must not destroy b, nor src
                    Check(b.Handle != IntPtr.Zero, "disposing one copy destroyed the other");
                    Check(RendersAsIcon(b), "the surviving copy stopped rendering after the other was disposed");

                    Icon third = CopyIconSafe(src);
                    Check(third != null, "disposing a copy destroyed the source icon");
                    if (third != null) third.Dispose();

                    b.Dispose();
                    Check(CopyIconSafe(IntPtr.Zero) == null, "a zero handle produced an icon");
                }
                finally { NativeMethods.DestroyIcon(src); }
            });

            Test("an out-of-range commit index is ignored, not fatal", delegate
            {
                // The index arrives as a posted message parameter, so it is
                // untrusted input. Before the guard this reached _apps[_index]
                // with no try/catch anywhere on the path WndProc -> Commit.
                bool session = _session;
                int savedIndex = _index;
                try
                {
                    _session = true;
                    _apps.Clear();
                    _apps.Add(new AppEntry { ReprHwnd = new IntPtr(1), Exe = "a.exe", Key = "a.exe" });
                    foreach (int bad in new int[] { -1, 1, 999999, int.MaxValue })
                    {
                        string outp = CaptureLog(delegate { DispatchAppMsg(NativeMethods.WM_APP_COMMITAT, new IntPtr(bad)); });
                        Check(outp.Contains("out of range"), "index " + bad + " was not rejected");
                        Check(_index == 0 || _index == savedIndex, "index " + bad + " corrupted the selection");
                    }
                }
                finally { _apps.Clear(); _index = savedIndex; _session = session; }
            });

            Test("a failing app message never escapes into the message pump", delegate
            {
                // HandleAppMsg must contain whatever the dispatch throws: the
                // whole chain runs inside WndProc, so an escaping exception
                // unwinds out of the message loop and kills the process.
                // WM_APP_NEXT against an empty list makes MoveIndex divide by
                // _apps.Count - genuinely fatal, and unlike a bad commit index
                // it is not intercepted by the range guard, so this exercises
                // the wrapper itself.
                bool session = _session;
                try
                {
                    _session = true;
                    _apps.Clear();
                    string outp = CaptureLog(delegate
                    {
                        HandleAppMsg(NativeMethods.WM_APP_NEXT, IntPtr.Zero);
                    });
                    Check(outp.Contains("app message failed"), "the failure was not caught and logged: " + outp);
                    Check(!_session, "the session was left up after a failed message");
                }
                catch (Exception e) { throw new Exception("exception escaped HandleAppMsg: " + e.Message); }
                finally { _apps.Clear(); _session = session; }
            });

            Test("an untitled window with real size stays switchable", delegate
            {
                // A missing SetWindowText must not remove an app from the
                // switcher: Foxmail's main frame (TFoxMainFrm.UnicodeClass)
                // never sets a title, so the old "title.Length == 0" rule made
                // the whole application invisible in the cycle.
                //
                // What is checked here is the rule, not one machine's windows:
                // the predicate must judge on real size, and must not require
                // a title. A hairline helper must still be rejected - that is
                // what the size check is for, since tooltips and tray icons
                // are all a few pixels.
                IntPtr w = NativeMethods.GetDesktopWindow();
                string desktopVerdict = AltTabIneligibilityReason(w);
                Check(desktopVerdict != null, "the desktop window was accepted into the cycle");
                Check(desktopVerdict != "no-title", "an untitled window is still rejected for having no title");
            });

            Test("overlay display and cancellation preserve foreground", delegate
            {
                IntPtr before = NativeMethods.GetForegroundWindow();
                using (var panel = new PanelForm())
                using (var chrome = new ChromeForm())
                {
                    chrome.Owner = panel;
                    foreach (Form window in new Form[] { panel, chrome })
                    {
                        window.SetBounds(40, 40, 200, 100);
                        window.Show();
                        Application.DoEvents();
                        Check(NativeMethods.IsWindowVisible(window.Handle), "overlay did not show");
                        Check(NativeMethods.GetForegroundWindow() == before, "show changed foreground");
                    }
                    panel.Hide();
                    chrome.Hide();
                    Application.DoEvents();
                    Check(!NativeMethods.IsWindowVisible(panel.Handle)
                        && !NativeMethods.IsWindowVisible(chrome.Handle), "cancel left an overlay visible");
                    Check(NativeMethods.GetForegroundWindow() == before, "cancel changed foreground");
                }
            });


            Test("activation refuses handles it cannot claim", delegate
            {
                Check(!ForceForeground(IntPtr.Zero), "a zero HWND was treated as claimable");
                Check(!ForceForeground(new IntPtr(0xFFFF0000)), "an invalid HWND was treated as claimable");
            });

            Test("eligibility memoization preserves the owner-chain verdict", delegate
            {
                // The cache only skips recomputation; a window and its root
                // must still classify exactly as the uncached predicate does.
                var memo = new Dictionary<IntPtr, string>();
                IntPtr w = NativeMethods.GetDesktopWindow();
                string cached = EligibilityMemoized(w, memo);
                string direct = AltTabIneligibilityReason(w);
                Check(cached == direct, "memoized verdict \"" + cached + "\" != direct \"" + direct + "\"");
                // Second call must come from the cache and agree.
                Check(EligibilityMemoized(w, memo) == cached, "cached verdict changed between calls");
            });


            Console.WriteLine("failures=" + _testFailures);
            return _testFailures == 0;
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool selfTest = false, logRequested = false;
            if (args.Length > 0 && args[0] == "--apply-update")
            {
                Updates.Apply(args);
                return;
            }
            string updateSignal = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--self-test") selfTest = true;
                if (a == "--log" || a == "--log-verbose") logRequested = true;
                if (a == "--log-verbose") _logVerbose = true;
                if (a == "--disabled") _enabled = false;
                if (a == "--update-started" && i + 1 < args.Length) updateSignal = args[++i];
            }

            NativeMethods.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // The suite drives the low-level hook directly and needs a real
            // message window for the activation barrier, but no tray, no
            // hooks and no second instance: it runs ahead of the mutex so it
            // stays available while the switcher itself is running.
            if (selfTest)
            {
                _msg = new MsgForm();
                IntPtr probe = _msg.Handle;   // force handle creation before the suite runs
                try { Environment.ExitCode = RunSelfTests() ? 0 : 1; }
                finally { _msg.Dispose(); GC.KeepAlive(probe); }
                return;
            }

            bool created;
            _mutex = new Mutex(true, "Local\\AppHopper", out created);
            if (!created) return;

            if (logRequested)
            {
                try
                {
                    _log = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apphopper.log"), false, new UTF8Encoding(false));
                    _logBytes = 0;
                    Log("AppHopper v" + AppVersion + " starting, pid " + Process.GetCurrentProcess().Id);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            _msg = new MsgForm();
            IntPtr hMsg = _msg.Handle;
            if (_log != null)
            {
                uint shellMessage = NativeMethods.RegisterWindowMessage("SHELLHOOK");
                bool watching = shellMessage != 0 && NativeMethods.RegisterShellHookWindow(hMsg);
                if (watching) _shellHookMessage = (int)shellMessage;
                Log("shell flash observer=" + watching + "; activation=foreground-handoff");
            }
            _panel = new PanelForm();
            _chrome = new ChromeForm();
            _chrome.Owner = _panel; // Keep chrome above the non-activating host.
            // Record our own handles so a stray foreground window in the commit
            // log can be told apart from one of ours.
            Log("our windows: msg=0x" + hMsg.ToInt64().ToString("X")
                + " panel=0x" + _panel.Handle.ToInt64().ToString("X")
                + " chrome=0x" + _chrome.Handle.ToInt64().ToString("X"));

            try { _vdm = (NativeMethods.IVirtualDesktopManager)new NativeMethods.VirtualDesktopManagerClass(); }
            catch { _vdm = null; }

            _hookProc = KbHookProc;
            _mouseHookProc = MouseHookProc;
            using (var cur = Process.GetCurrentProcess())
            using (var mod = cur.MainModule)
                _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc, NativeMethods.GetModuleHandle(mod.ModuleName), 0);
            // A silently missing hook leaves the app looking alive but doing
            // nothing, and the failure is invisible from the outside - the
            // user just sees the OS switcher instead. Record it either way.
            Log("keyboard hook installed=" + (_hook != IntPtr.Zero)
                + " error=" + (_hook != IntPtr.Zero ? 0 : Marshal.GetLastWin32Error()));

            var menu = new ContextMenu();
            var miToggle = new MenuItem("Enabled");
            miToggle.Checked = _enabled;
            miToggle.Click += delegate { _enabled = !_enabled; miToggle.Checked = _enabled; };
            var miStartup = new MenuItem("Start with Windows");
            miStartup.Checked = StartupEnabled();
            miStartup.Click += delegate
            {
                bool add = !StartupEnabled();
                if (!SetStartup(add))
                    MessageBox.Show("Autostart requires a protected installation under Program Files and permission to update the Run key.",
                        "AppHopper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                miStartup.Checked = StartupEnabled();
            };
            var miExit = new MenuItem("Exit");
            miExit.Click += delegate { _exitRequested = true; };
            var miUpdate = new MenuItem("Get updates...");
            miUpdate.Click += delegate { Updates.Click(miUpdate, miExit); };
            menu.MenuItems.Add(miToggle);
            menu.MenuItems.Add(miStartup);
            menu.MenuItems.Add(new MenuItem("-"));
            menu.MenuItems.Add(miUpdate);
            menu.MenuItems.Add(new MenuItem("-"));
            menu.MenuItems.Add(miExit);

            var icon = new NotifyIcon();
            _trayIcon = MakeTrayIcon();
            icon.Icon = _trayIcon;
            icon.Text = "AppHopper " + AppVersion + " - one entry per app";
            icon.ContextMenu = menu;
            icon.Visible = true;

            // Recolor the tray icon when the user changes the accent color or
            // theme, so it always matches the panel's accent. (Accent/theme
            // changes surface as Color or General; redraw on either.)
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += delegate(object s, Microsoft.Win32.UserPreferenceChangedEventArgs e)
            {
                var cat = e.Category;
                if (cat != Microsoft.Win32.UserPreferenceCategory.Color && cat != Microsoft.Win32.UserPreferenceCategory.General) return;
                Icon old = _trayIcon;
                _trayIcon = MakeTrayIcon();
                icon.Icon = _trayIcon;
                if (old != null) old.Dispose();
            };

            // Watchdog: if the Alt release never arrives through the hook
            // (timeout, injection hiccup, focus race), commit anyway instead of
            // leaving the overlay up and swallowing clicks forever. Short
            // interval: this timer is also the worst-case latency between the
            // user releasing Alt and the overlay disappearing.
            var sessionWatchdog = new System.Windows.Forms.Timer { Interval = 30 };
            sessionWatchdog.Tick += delegate
            {
                if (_session && !AltDown()) Commit();
            };
            sessionWatchdog.Start();

            // Live refresh: catches windows closed while the overlay is up.
            _refreshTimer = new System.Windows.Forms.Timer { Interval = 120 };
            _refreshTimer.Tick += delegate { RefreshTick(); };

            var exitTimer = new System.Windows.Forms.Timer { Interval = 200 };
            exitTimer.Tick += delegate
            {
                if (!_exitRequested) return;
                if (_committing) return;   // a commit is mid-flight; retry next tick
                EndSession();
                exitTimer.Stop();
                icon.Visible = false;
                if (_hook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_hook);
                Application.Exit();
            };
            exitTimer.Start();
            if (updateSignal != null) Updates.SignalStarted(updateSignal);

            Application.Run();

            EndSession();
            if (_shellHookMessage != 0)
            {
                NativeMethods.DeregisterShellHookWindow(hMsg);
                _shellHookMessage = 0;
            }
            icon.Visible = false;
            if (_log != null) _log.Dispose();
        }
    }
}
