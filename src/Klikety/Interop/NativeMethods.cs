using System.Drawing;
using System.Runtime.InteropServices;

namespace Klikety.Interop;

/// <summary>
/// Thin Win32 P/Invoke wrappers for screen geometry and keyboard translation.
/// </summary>
internal static partial class NativeMethods {
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT {
        public int X;
        public int Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hWnd);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private static readonly int RectSize = Marshal.SizeOf<RECT>();

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyExW(uint uCode, uint uMapType, nint dwhkl);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(
        uint wVirtKey, uint wScanCode,
        byte[] lpKeyState,
        [Out] char[] pwszBuff,
        int cchBuff,
        uint wFlags,
        nint dwhkl);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    private enum MonitorDpiType { EffectiveDpi = 0 }

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint hMonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// Returns physical-pixel bounds of the primary monitor.
    /// </summary>
    public static Rectangle GetPrimaryScreenBounds() {
        var hMon = MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref info)) {
            return new Rectangle(0, 0, 1920, 1080); // safe fallback
        }
        var rc = info.rcMonitor;
        return new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
    }

    /// <summary>
    /// Returns the DPI scale factor for the primary monitor (1.0 = 96 DPI, 1.5 = 144 DPI, etc.).
    /// </summary>
    public static double GetPrimaryMonitorDpiScale() {
        var hMon = MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);
        int hr = GetDpiForMonitor(hMon, MonitorDpiType.EffectiveDpi, out uint dpiX, out _);
        if (hr != 0) {
            return 1.0; // S_OK = 0; fallback on failure
        }

        return dpiX / 96.0;
    }

    /// <summary>
    /// Gets the active keyboard layout handle for the foreground window's thread.
    /// Falls back to the current thread's layout if no foreground window exists.
    /// </summary>
    public static nint GetActiveKeyboardLayout() {
        var hwnd = GetForegroundWindow();
        if (hwnd != 0) {
            var threadId = GetWindowThreadProcessId(hwnd, out _);
            if (threadId != 0) {
                return GetKeyboardLayout(threadId);
            }
        }
        return GetKeyboardLayout(0);
    }

    /// <summary>
    /// Translates a virtual-key code to a display character using the given HKL.
    /// Returns null if no character mapping exists (dead key, unmapped).
    /// </summary>
    public static char? VKeyToChar(uint vkey, nint hkl) {
        const uint MAPVK_VK_TO_VSC = 0;
        uint scanCode = MapVirtualKeyExW(vkey, MAPVK_VK_TO_VSC, hkl);

        var keyState = new byte[256];
        var buffer = new char[4];
        int result = ToUnicodeEx(vkey, scanCode, keyState, buffer, buffer.Length, 0, hkl);

        // result > 0 = number of chars written; result == 0 = no translation; result < 0 = dead key
        if (result > 0) {
            return buffer[0];
        }

        // Dead key: call ToUnicodeEx again to clear the internal state
        if (result < 0) {
            _ = ToUnicodeEx(vkey, scanCode, keyState, buffer, buffer.Length, 0, hkl);
        }

        return null;
    }

    /// <summary>
    /// Returns the current cursor position in physical pixels.
    /// Falls back to (0, 0) if the API call fails.
    /// </summary>
    public static Point GetCursorPosition() {
        if (!GetCursorPos(out var pt)) {
            return Point.Empty;
        }
        return new Point(pt.X, pt.Y);
    }

    /// <summary>
    /// Returns the work area (physical pixels) of the monitor containing the foreground window.
    /// Falls back to primary monitor if foreground window is unavailable.
    /// </summary>
    public static Rectangle GetForegroundMonitorWorkArea() {
        var hwnd = GetForegroundWindow();
        nint hMon;
        if (hwnd != 0) {
            hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTOPRIMARY);
        } else {
            hMon = MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);
        }

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref info)) {
            return new Rectangle(0, 0, 1920, 1080);
        }
        var rc = info.rcWork;
        return new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>
    /// Applies WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE to make a window click-through.
    /// </summary>
    public static void SetClickThroughExStyle(nint hwnd) {
        var existing = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE,
            existing | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    /// <summary>
    /// Returns the HWND of the current foreground window, or zero if unavailable.
    /// </summary>
    public static nint GetForegroundWindowHandle() => GetForegroundWindow();

    /// <summary>
    /// Returns the visible bounds of the given window in physical pixels using DWM.
    /// Returns <see cref="Rectangle.Empty"/> if the window is minimized, invalid, or DWM fails.
    /// </summary>
    public static Rectangle GetWindowBounds(nint hwnd) {
        if (hwnd == 0) {
            return Rectangle.Empty;
        }

        if (IsIconic(hwnd)) {
            return Rectangle.Empty;
        }

        int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var rect, RectSize);
        if (hr != 0) {
            return Rectangle.Empty;
        }

        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) {
            return Rectangle.Empty;
        }

        return new Rectangle(rect.Left, rect.Top, w, h);
    }
}
