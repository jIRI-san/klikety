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
    public IDebounceTimer Create() => new ThreadingTimer();
}

internal sealed class ThreadingTimer : IDebounceTimer {
    private System.Threading.Timer? _timer;

    public event Action? Elapsed;

    public void Start(TimeSpan interval) {
        Stop();
        _timer = new System.Threading.Timer(_ => Elapsed?.Invoke(), null, interval, Timeout.InfiniteTimeSpan);
    }

    public void Stop() {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();
}

internal sealed class Win32CursorPositionProvider : ICursorPositionProvider {
    public Point GetCursorPosition() => NativeMethods.GetCursorPosition();
}

internal sealed class Win32ScreenBoundsProvider : IScreenBoundsProvider {
    public Rectangle GetPrimaryScreenBounds() => NativeMethods.GetPrimaryScreenBounds();
}

internal sealed class Win32KeyboardLayoutProvider : IKeyboardLayoutProvider {
    public nint GetActiveKeyboardLayout() => NativeMethods.GetActiveKeyboardLayout();
}
