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

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hWnd, char[] lpString, int nMaxCount);

    /// <summary>
    /// Returns the title text of the given window, or empty string if the HWND is zero/invalid.
    /// </summary>
    public static string GetWindowTitle(nint hwnd) {
        if (hwnd == 0) {
            return string.Empty;
        }

        int length = GetWindowTextLength(hwnd);
        if (length <= 0) {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        int copied = GetWindowTextW(hwnd, buffer, buffer.Length);
        if (copied <= 0) {
            return string.Empty;
        }

        return new string(buffer, 0, copied).Trim();
    }

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

    /// <summary>
    /// Returns the DPI scale factor for the given monitor (1.0 = 96 DPI). Fallback: 1.0.
    /// </summary>
    public static double GetMonitorDpiScale(nint hMonitor) {
        int hr = GetDpiForMonitor(hMonitor, MonitorDpiType.EffectiveDpi, out uint dpiX, out _);
        if (hr != 0) {
            return 1.0;
        }

        return dpiX / 96.0;
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int CCHDEVICENAME = 32;

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    /// <summary>
    /// Virtual-desktop bounds in physical pixels, including negative origin.
    /// </summary>
    public static Rectangle GetVirtualScreenBounds() {
        return new Rectangle(
            GetSystemMetrics(SM_XVIRTUALSCREEN),
            GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN),
            GetSystemMetrics(SM_CYVIRTUALSCREEN));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, nint lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoEx(nint hMonitor, ref MONITORINFOEX lpmi);

    internal readonly record struct EnumeratedMonitorInfo(
        nint Handle,
        string GdiName,
        Rectangle MonitorBounds,
        double DpiScale);

    internal readonly record struct CcdPathInfo(string GdiName, string DevicePath);

    /// <summary>
    /// Enumerates attached desktop monitors with GDI name, physical <c>rcMonitor</c>, and DPI.
    /// Returns false when the API fails or yields no monitors.
    /// </summary>
    internal static bool TryEnumerateMonitors(out EnumeratedMonitorInfo[] monitors) {
        var handles = new List<nint>();
        MonitorEnumProc callback = (hMonitor, _, _, _) => {
            handles.Add(hMonitor);
            return true;
        };
        bool enumerated = EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);
        GC.KeepAlive(callback);
        if (!enumerated || handles.Count == 0) {
            monitors = [];
            return false;
        }

        monitors = new EnumeratedMonitorInfo[handles.Count];
        int infoSize = Marshal.SizeOf<MONITORINFOEX>();
        for (int i = 0; i < handles.Count; i++) {
            var info = new MONITORINFOEX { cbSize = infoSize };
            if (!GetMonitorInfoEx(handles[i], ref info)) {
                monitors = [];
                return false;
            }

            var rc = info.rcMonitor;
            monitors[i] = new EnumeratedMonitorInfo(
                handles[i],
                info.szDevice ?? string.Empty,
                new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top),
                GetMonitorDpiScale(handles[i]));
        }

        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)]
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO {
        public uint infoType;
        public uint id;
        public LUID adapterId;
    }

    private enum DISPLAYCONFIG_DEVICE_INFO_TYPE : int {
        GetSourceName = 1,
        GetTargetName = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER {
        public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        nint currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);

    /// <summary>
    /// Active CCD paths as GDI source name plus target <c>monitorDevicePath</c>.
    /// </summary>
    internal static bool TryQueryActiveCcdPaths(out CcdPathInfo[] paths, out string failure) {
        paths = [];
        int err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
        if (err != ERROR_SUCCESS) {
            failure = $"GetDisplayConfigBufferSizes failed ({err})";
            return false;
        }

        if (pathCount == 0) {
            failure = "CCD returned zero active paths";
            return false;
        }

        for (int attempt = 0; attempt < 3; attempt++) {
            var pathArray = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modeArray = new DISPLAYCONFIG_MODE_INFO[Math.Max(modeCount, 1u)];
            uint pathElements = pathCount;
            uint modeElements = (uint)modeArray.Length;
            err = QueryDisplayConfig(
                QDC_ONLY_ACTIVE_PATHS,
                ref pathElements,
                pathArray,
                ref modeElements,
                modeArray,
                nint.Zero);
            if (err == ERROR_INSUFFICIENT_BUFFER) {
                pathCount = pathElements == 0 ? pathCount + 4 : pathElements;
                modeCount = modeElements == 0 ? modeCount + 4 : modeElements;
                continue;
            }

            if (err != ERROR_SUCCESS) {
                failure = $"QueryDisplayConfig failed ({err})";
                return false;
            }

            var result = new CcdPathInfo[pathElements];
            for (uint i = 0; i < pathElements; i++) {
                var path = pathArray[i];
                if (!TryGetSourceGdiName(path.sourceInfo.adapterId, path.sourceInfo.id, out string gdiName) ||
                    !TryGetTargetDevicePath(path.targetInfo.adapterId, path.targetInfo.id, out string devicePath)) {
                    failure = "DisplayConfigGetDeviceInfo failed";
                    return false;
                }

                result[i] = new CcdPathInfo(gdiName, devicePath);
            }

            paths = result;
            failure = string.Empty;
            return true;
        }

        failure = "QueryDisplayConfig buffer retry exhausted";
        return false;
    }

    private static bool TryGetSourceGdiName(LUID adapterId, uint id, out string gdiName) {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetSourceName,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = adapterId,
                id = id,
            },
        };
        if (DisplayConfigGetDeviceInfo(ref request) != ERROR_SUCCESS) {
            gdiName = string.Empty;
            return false;
        }

        gdiName = request.viewGdiDeviceName ?? string.Empty;
        return true;
    }

    private static bool TryGetTargetDevicePath(LUID adapterId, uint id, out string devicePath) {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetTargetName,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = adapterId,
                id = id,
            },
        };
        if (DisplayConfigGetDeviceInfo(ref request) != ERROR_SUCCESS) {
            devicePath = string.Empty;
            return false;
        }

        devicePath = request.monitorDevicePath ?? string.Empty;
        return true;
    }
}
