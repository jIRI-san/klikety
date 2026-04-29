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
/// A cancellable one-shot timer.
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
}

/// <summary>
/// Provides the active keyboard layout handle.
/// </summary>
public interface IKeyboardLayoutProvider {
    nint GetActiveKeyboardLayout();
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
}
