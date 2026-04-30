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

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint idThread);

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
    /// Gets the active keyboard layout handle for the current thread.
    /// </summary>
    public static nint GetActiveKeyboardLayout() {
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
    /// </summary>
    public static Point GetCursorPosition() {
        GetCursorPos(out var pt);
        return new Point(pt.X, pt.Y);
    }
}
