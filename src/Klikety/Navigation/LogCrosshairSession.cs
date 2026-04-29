using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Services;

namespace Klikety.Navigation;

/// <summary>
/// LogCrosshair mode session. Flat navigation (no subgrid). Implements
/// <see cref="IModeSession"/> using <see cref="LogCrosshairStateMachine"/>
/// and <see cref="ILogCrosshairRenderer"/>.
/// </summary>
public sealed class LogCrosshairSession : IModeSession {
    readonly VKey[] _horizKeys;
    readonly VKey[] _vertKeys;
    readonly int _logBaseSize;
    readonly ILogCrosshairRenderer? _renderer;

    readonly LogCrosshairStateMachine _sm;
    LogCrosshairGrid? _grid;
    Point _origin;

    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action<Point>? CursorMoveRequested;

    public LogCrosshairSession(
        VKey[] horizKeys, VKey[] vertKeys, ActionMapper actionMapper,
        ModeConfig modeConfig, ILogCrosshairRenderer? renderer) {
        _horizKeys = horizKeys;
        _vertKeys = vertKeys;
        _logBaseSize = modeConfig.LogBaseSize;
        _renderer = renderer;

        _sm = new LogCrosshairStateMachine(
            _horizKeys, _vertKeys, actionMapper, modeConfig.ArrowKeys);

        _sm.HorizSelected += OnHorizSelected;
        _sm.VertSelected += OnVertSelected;
        _sm.CellSelected += OnCellSelected;
        _sm.ActionRequested += OnActionRequested;
        _sm.Cancelled += OnCancelled;
        _sm.InvalidKeyPressed += OnInvalidKeyPressed;
        _sm.ArrowMoved += OnArrowMoved;
        _sm.AxisCleared += OnAxisCleared;
    }

    public void Activate(Rectangle screenBounds, Point origin) {
        _origin = origin;
        _grid = LogGridCalculator.Calculate(
            origin, screenBounds, _logBaseSize, _horizKeys.Length, _vertKeys.Length);
        _sm.Activate(_grid, origin);
        _renderer?.RenderCross(_grid);
    }

    public void OnKey(VKey key) => _sm.OnKey(key);

    public void Deactivate() {
        _sm.Reset();
        _grid = null;
    }

    void OnHorizSelected(int col, int keyIndex) {
        if (_grid is null) {
            return;
        }

        CursorMoveRequested?.Invoke(
            LogGridCalculator.CenterOf(_grid.CellAt(_grid.CenterRow, col)));
        _renderer?.HighlightColumn(_grid, col);
    }

    void OnVertSelected(int row, int keyIndex) {
        if (_grid is null) {
            return;
        }

        CursorMoveRequested?.Invoke(
            LogGridCalculator.CenterOf(_grid.CellAt(row, _grid.CenterCol)));
        _renderer?.HighlightRow(_grid, row);
    }

    void OnCellSelected(GridCell cell) {
        CursorMoveRequested?.Invoke(LogGridCalculator.CenterOf(cell));
        if (_grid is not null) {
            _renderer?.HighlightCell(_grid, cell);
        }
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

        var cell = _grid.CellAt(row, col);
        CursorMoveRequested?.Invoke(LogGridCalculator.CenterOf(cell));
        _renderer?.HighlightCell(_grid, cell);
    }

    void OnAxisCleared() {
        if (_grid is null) {
            return;
        }

        CursorMoveRequested?.Invoke(LogGridCalculator.CenterOf(_grid.CenterCell));
        _renderer?.RenderCross(_grid);
    }
}
