using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Services;

namespace Klikety.Navigation;

/// <summary>
/// LogGrid mode session. Implements <see cref="IModeSession"/> using
/// <see cref="LogGridStateMachine"/> and <see cref="ILogGridRenderer"/>.
/// Iterative two-key selection with log-scaled grid and recentering.
/// No subgrid/L2 — flat single-level mode.
/// </summary>
public sealed class LogGridSession : IModeSession {
    readonly VKey[] _horizKeys;
    readonly VKey[] _vertKeys;
    readonly int _logGridBaseSize;
    readonly ILogGridRenderer? _renderer;

    readonly LogGridStateMachine _sm;
    LogGrid? _grid;
    Rectangle _screenBounds;
    Point _origin;
    Action? _redraw;

    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action<Point>? CursorMoveRequested;

    public LogGridSession(
        VKey[] horizKeys, VKey[] vertKeys, ActionMapper actionMapper,
        ModeConfig modeConfig, ILogGridRenderer? renderer) {
        _horizKeys = horizKeys;
        _vertKeys = vertKeys;
        _logGridBaseSize = modeConfig.LogGridBaseSize;
        _renderer = renderer;

        _sm = new LogGridStateMachine(horizKeys, vertKeys, actionMapper, modeConfig.ArrowKeys);

        _sm.FirstKeySelected += OnFirstKeySelected;
        _sm.CellSelected += OnCellSelected;
        _sm.ActionRequested += OnActionRequested;
        _sm.Cancelled += OnCancelled;
        _sm.InvalidKeyPressed += OnInvalidKeyPressed;
        _sm.ArrowMoved += OnArrowMoved;
        _sm.ArrowRecenterRequested += OnArrowRecenterRequested;
    }

    public void Activate(Rectangle screenBounds, Point origin) {
        _origin = origin;
        _screenBounds = screenBounds;
        _grid = LogScaleGridCalculator.Calculate(
            origin, screenBounds, _logGridBaseSize, _horizKeys.Length, _vertKeys.Length);
        _sm.Activate(_grid, origin);
        Render(renderer => renderer.RenderGrid(_grid));
    }

    public void OnKey(VKey key) {
        _sm.OnKey(key);
    }

    public void Redraw() {
        _redraw?.Invoke();
    }

    public void Deactivate() {
        _sm.Reset();
        _grid = null;
        _redraw = null;
    }

    void OnFirstKeySelected(int col) {
        if (_grid is null) {
            return;
        }

        // Show column highlight + first-key indicator in opposite corner
        _redraw = () => {
            if (_renderer is null || _grid is null) {
                return;
            }

            _renderer.HighlightColumn(_grid, col);
            _renderer.RenderFirstKeyIndicator(_grid, col, _screenBounds);
        };
        _redraw();
    }

    void OnCellSelected(GridCell cell) {
        if (_grid is null) {
            return;
        }

        _renderer?.HideFirstKeyIndicator();

        // Move cursor to cell center and recenter
        var center = CellCenter(cell);
        CursorMoveRequested?.Invoke(center);
        RecenterGrid(center);
    }

    void OnActionRequested(Point point, MouseAction action) {
        ActionRequested?.Invoke(point, action);
    }

    void OnCancelled() {
        CursorMoveRequested?.Invoke(_origin);
        Cancelled?.Invoke();
    }

    void OnInvalidKeyPressed() {
        _renderer?.FlashInvalidKey();
    }

    void OnArrowMoved(int row, int col) {
        if (_grid is null) {
            return;
        }

        // Highlight cell but do NOT recenter
        var cell = _grid.CellAt(row, col);
        CursorMoveRequested?.Invoke(CellCenter(cell));
        Render(renderer => renderer.HighlightCell(_grid, cell));
    }

    void OnArrowRecenterRequested(GridCell cell) {
        if (_grid is null) {
            return;
        }

        // Recenter at arrow-selected cell
        var center = CellCenter(cell);
        CursorMoveRequested?.Invoke(center);
        RecenterGrid(center);
    }

    void RecenterGrid(Point newCenter) {
        _grid = LogScaleGridCalculator.Calculate(
            newCenter, _screenBounds, _logGridBaseSize, _horizKeys.Length, _vertKeys.Length);
        _sm.UpdateGrid(_grid);
        Render(renderer => renderer.RenderGrid(_grid));
    }

    static Point CellCenter(GridCell cell) =>
        new(cell.Bounds.X + cell.Bounds.Width / 2, cell.Bounds.Y + cell.Bounds.Height / 2);

    private void Render(Action<ILogGridRenderer> render) {
        _redraw = () => {
            if (_renderer is not null) {
                render(_renderer);
            }
        };
        _redraw();
    }
}
