#if DEBUG
using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Overlay;
using Klikety.Services;

namespace Klikety.Navigation;

/// <summary>
/// Debug-only session that renders a log-scale grid at the cursor position on activation.
/// Bypasses ModeSessionFactory and coordinator chord dispatch — wired directly in App.xaml.cs
/// via a dedicated debug hotkey. Press Escape to dismiss.
/// </summary>
internal sealed class DebugLogGridSession : IModeSession {
    readonly ILogGridRenderer _renderer;
    readonly ICursorPositionProvider _cursor;
    readonly IScreenBoundsProvider _screen;

    const int LogBaseSize = 10;
    const int Cols = 10;
    const int Rows = 10;

    public event Action<Point, MouseAction>? ActionRequested { add { } remove { } }
    public event Action? Cancelled;
    public event Action<Point>? CursorMoveRequested { add { } remove { } }

    public DebugLogGridSession(
        ILogGridRenderer renderer,
        ICursorPositionProvider cursor,
        IScreenBoundsProvider screen) {
        _renderer = renderer;
        _cursor = cursor;
        _screen = screen;
    }

    public void Activate(Rectangle screenBounds, Point origin) {
        var grid = LogScaleGridCalculator.Calculate(origin, screenBounds, LogBaseSize, Cols, Rows);
        _renderer.RenderGrid(grid);
    }

    public void OnKey(VKey key) {
        if (key == VKey.Escape) {
            _renderer.ClearCanvas();
            Cancelled?.Invoke();
        }
    }

    public void Deactivate() {
        _renderer.ClearCanvas();
    }
}
#endif
