using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Services;

namespace Klikety.Navigation;

/// <summary>
/// LogCrosshair mode session. Implements <see cref="IModeSession"/> using
/// <see cref="LogCrosshairStateMachine"/> and <see cref="ILogCrosshairRenderer"/>.
/// Supports L2 level stack via <see cref="UniformGridSession"/>.
/// </summary>
public sealed class LogCrosshairSession : IModeSession {
    readonly VKey[] _horizKeys;
    readonly VKey[] _vertKeys;
    readonly ActionMapper _actionMapper;
    readonly bool _arrowKeysEnabled;
    readonly int _logBaseSize;
    readonly int _minCellPx;
    readonly ILogCrosshairRenderer? _renderer;
    readonly IGridRenderer? _gridRenderer;

    readonly LogCrosshairStateMachine _sm;
    LogCrosshairGrid? _grid;
    Point _origin;

    // L2 level stack
    UniformGridSession? _l2Session;
    int _lastHorizCol;
    int _lastVertRow;

    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action<Point>? CursorMoveRequested;

    public LogCrosshairSession(
        VKey[] horizKeys, VKey[] vertKeys, ActionMapper actionMapper,
        ModeConfig modeConfig, ILogCrosshairRenderer? renderer, int minCellPx = 5,
        IGridRenderer? gridRenderer = null) {
        _horizKeys = horizKeys;
        _vertKeys = vertKeys;
        _actionMapper = actionMapper;
        _arrowKeysEnabled = modeConfig.ArrowKeys;
        _logBaseSize = modeConfig.LogBaseSize;
        _minCellPx = minCellPx;
        _renderer = renderer;
        _gridRenderer = gridRenderer;

        _sm = new LogCrosshairStateMachine(
            _horizKeys, _vertKeys, actionMapper, modeConfig.ArrowKeys, minCellPx);

        _sm.HorizSelected += OnHorizSelected;
        _sm.VertSelected += OnVertSelected;
        _sm.CellSelected += OnCellSelected;
        _sm.SubgridEntered += OnSubgridEntered;
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

    public void OnKey(VKey key) {
        if (_l2Session is not null) {
            _l2Session.OnKey(key);
            return;
        }

        _sm.OnKey(key);
    }

    public void Deactivate() {
        PopL2();
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

    void OnSubgridEntered(GridCell cell) {
        CursorMoveRequested?.Invoke(LogGridCalculator.CenterOf(cell));

        _lastHorizCol = cell.Col;
        _lastVertRow = cell.Row;

        var hReduction = DynamicKeyReducer.ComputeActiveKeys(
            _horizKeys, cell.Bounds.Width, _minCellPx, hasCenterCell: false);
        var vReduction = DynamicKeyReducer.ComputeActiveKeys(
            _vertKeys, cell.Bounds.Height, _minCellPx, hasCenterCell: false);

        if (hReduction.IsDisabled || vReduction.IsDisabled) {
            if (_grid is not null) {
                _renderer?.HighlightCell(_grid, cell);
            }
            return;
        }

        var l2Mode = new ModeConfig { TwoKey = true, ArrowKeys = _arrowKeysEnabled };
        var l2 = new UniformGridSession(
            hReduction.ActiveKeys, vReduction.ActiveKeys,
            _actionMapper, l2Mode, level3Threshold: 0, _gridRenderer);

        l2.ActionRequested += OnL2ActionRequested;
        l2.Cancelled += OnL2Cancelled;
        l2.CursorMoveRequested += OnL2CursorMoveRequested;

        _l2Session = l2;
        l2.Activate(cell.Bounds, LogGridCalculator.CenterOf(cell));
    }

    void OnL2ActionRequested(Point point, MouseAction action) {
        ActionRequested?.Invoke(point, action);
    }

    void OnL2CursorMoveRequested(Point point) {
        CursorMoveRequested?.Invoke(point);
    }

    void OnL2Cancelled() {
        PopL2();

        if (_grid is not null) {
            var cell = _grid.CellAt(_lastVertRow, _lastHorizCol);
            CursorMoveRequested?.Invoke(LogGridCalculator.CenterOf(cell));
            _renderer?.HighlightCell(_grid, cell);
        }
    }

    void PopL2() {
        if (_l2Session is null) {
            return;
        }

        _l2Session.ActionRequested -= OnL2ActionRequested;
        _l2Session.Cancelled -= OnL2Cancelled;
        _l2Session.CursorMoveRequested -= OnL2CursorMoveRequested;
        _l2Session.Deactivate();
        _l2Session = null;
    }
}
