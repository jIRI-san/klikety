using System.Drawing;

using Klikety.Input;

namespace Klikety.Services;

/// <summary>
/// Checks physical key state via GetAsyncKeyState.
/// </summary>
public interface IKeyStateProvider {
    bool IsKeyDown(VKey key);
}

/// <summary>
/// A cancellable one-shot timer. Implementations MUST guarantee that
/// <see cref="Elapsed"/> fires on the same thread that called <see cref="Start"/>.
/// </summary>
public interface IDebounceTimer : IDisposable {
    event Action? Elapsed;
    void Start(TimeSpan interval);
    void Stop();
}

/// <summary>
/// Factory for creating timers (testable abstraction over DispatcherTimer / Threading.Timer).
/// </summary>
public interface ITimerFactory {
    IDebounceTimer Create();
}

/// <summary>
/// Provides cursor position in physical pixels.
/// </summary>
public interface ICursorPositionProvider {
    Point GetCursorPosition();
}

/// <summary>
/// Provides primary screen bounds in physical pixels.
/// </summary>
public interface IScreenBoundsProvider {
    Rectangle GetPrimaryScreenBounds();
    double GetDpiScale();
}

/// <summary>
/// Provides the active keyboard layout handle and QWERTY detection.
/// </summary>
public interface IKeyboardLayoutProvider {
    nint GetActiveKeyboardLayout();
    bool IsQwertyCompatible();
}

/// <summary>
/// Provides foreground window handle and bounds in physical pixels.
/// </summary>
public interface IForegroundWindowProvider {
    nint GetForegroundWindowHandle();
    void SetForegroundWindow(nint hwnd);
    Rectangle GetWindowBounds(nint hwnd);
    string GetWindowTitle(nint hwnd);
}

/// <summary>
/// Groups all platform-level abstractions for testability.
/// </summary>
public interface IPlatformServices {
    IKeyStateProvider KeyState { get; }
    ITimerFactory Timers { get; }
    ICursorPositionProvider Cursor { get; }
    IScreenBoundsProvider Screen { get; }
    IKeyboardLayoutProvider KeyboardLayout { get; }
    IForegroundWindowProvider ForegroundWindow { get; }
}
