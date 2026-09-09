using System.Drawing;
using System.Runtime.InteropServices;

using Klikety.Input;
using Klikety.Interop;

namespace Klikety.Services;

/// <summary>
/// Production implementations of platform services wrapping Win32 P/Invoke.
/// </summary>
public sealed class PlatformServices : IPlatformServices {
    public static PlatformServices Instance { get; } = new();

    public IKeyStateProvider KeyState { get; } = new Win32KeyStateProvider();
    public ITimerFactory Timers { get; } = new ThreadingTimerFactory();
    public ICursorPositionProvider Cursor { get; } = new Win32CursorPositionProvider();
    public IScreenBoundsProvider Screen { get; } = new Win32ScreenBoundsProvider();
    public IKeyboardLayoutProvider KeyboardLayout { get; } = new Win32KeyboardLayoutProvider();
    public IForegroundWindowProvider ForegroundWindow { get; } = new Win32ForegroundWindowProvider();
    public IDisplayCatalog DisplayCatalog { get; } = new DisplayCatalog();
}

internal sealed partial class Win32KeyStateProvider : IKeyStateProvider {
    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    public bool IsKeyDown(VKey key) {
        // High bit set = key is currently down
        return (GetAsyncKeyState((int)key) & 0x8000) != 0;
    }
}

internal sealed class ThreadingTimerFactory : ITimerFactory {
    public IDebounceTimer Create() => new DispatcherDebounceTimer();
}

/// <summary>
/// Debounce timer that marshals callbacks to the WPF dispatcher thread.
/// Uses a cancelled flag to prevent post-dispose firing.
/// </summary>
internal sealed class DispatcherDebounceTimer : IDebounceTimer {
    private System.Windows.Threading.DispatcherTimer? _timer;
    private bool _disposed;

    public event Action? Elapsed;

    public void Start(TimeSpan interval) {
        Stop();
        _disposed = false;
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = interval };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e) {
        _timer?.Stop();
        if (!_disposed) {
            Elapsed?.Invoke();
        }
    }

    public void Stop() {
        _disposed = true;
        if (_timer is not null) {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    public void Dispose() => Stop();
}

internal sealed class Win32CursorPositionProvider : ICursorPositionProvider {
    public Point GetCursorPosition() => NativeMethods.GetCursorPosition();
}

internal sealed class Win32ScreenBoundsProvider : IScreenBoundsProvider {
    public Rectangle GetPrimaryScreenBounds() => NativeMethods.GetPrimaryScreenBounds();
    public double GetDpiScale() => NativeMethods.GetPrimaryMonitorDpiScale();
}

internal sealed class Win32KeyboardLayoutProvider : IKeyboardLayoutProvider {
    public nint GetActiveKeyboardLayout() => NativeMethods.GetActiveKeyboardLayout();

    public bool IsQwertyCompatible() {
        var hkl = GetActiveKeyboardLayout();
        // Probe: does VK_Q map to 'Q'? If yes, layout is QWERTY-compatible.
        var ch = NativeMethods.VKeyToChar((uint)VKey.Q, hkl);
        return ch is 'Q' or 'q';
    }
}

internal sealed class Win32ForegroundWindowProvider : IForegroundWindowProvider {
    public nint GetForegroundWindowHandle() => NativeMethods.GetForegroundWindowHandle();
    public void SetForegroundWindow(nint hwnd) => NativeMethods.SetForegroundWindow(hwnd);
    public Rectangle GetWindowBounds(nint hwnd) => NativeMethods.GetWindowBounds(hwnd);
    public string GetWindowTitle(nint hwnd) => NativeMethods.GetWindowTitle(hwnd);
}
