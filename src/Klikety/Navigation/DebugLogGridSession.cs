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
    private LogGrid? _grid;

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
        _grid = LogScaleGridCalculator.Calculate(origin, screenBounds, LogBaseSize, Cols, Rows);
        _renderer.RenderGrid(_grid);
    }

    public void OnKey(VKey key) {
        if (key == VKey.Escape) {
            _renderer.ClearCanvas();
            Cancelled?.Invoke();
        }
    }

    public void Redraw() {
        if (_grid is not null) {
            _renderer.RenderGrid(_grid);
        }
    }

    public void Deactivate() {
        _renderer.ClearCanvas();
        _grid = null;
    }
}
#endif
