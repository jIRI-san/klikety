using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Services;

namespace Klikety.Navigation;

/// <summary>
/// Renderer interface for Crosshair mode. Separate from <see cref="IGridRenderer"/>
/// because Crosshair has different visual semantics (cross + dimmed non-cross).
/// </summary>
public interface ICrosshairRenderer {
    void SetTransform(System.Windows.Media.Matrix transformFromDevice);
    void RenderCross(CrosshairGrid grid);
    void HighlightColumn(CrosshairGrid grid, int col);
    void HighlightRow(CrosshairGrid grid, int row);
    void HighlightCell(CrosshairGrid grid, GridCell cell);
    void RenderSubgridCross(CrosshairGrid parentGrid, CrosshairGrid subgrid, GridCell parentCell);
    void FlashInvalidKey();
}

/// <summary>
/// Crosshair mode session. Implements <see cref="IModeSession"/> using
/// <see cref="CrosshairStateMachine"/> and <see cref="ICrosshairRenderer"/>.
/// </summary>
public sealed class CrosshairSession : IModeSession {
    private readonly VKey[] _horizKeys;
    private readonly VKey[] _vertKeys;
    private readonly ActionMapper _actionMapper;
    private readonly bool _arrowKeysEnabled;
    private readonly int _minCellPx;
    private readonly ICrosshairRenderer? _renderer;

    private CrosshairStateMachine? _sm;
    private CrosshairGrid? _grid;
    private Point _origin;

    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action<Point>? CursorMoveRequested;

    public CrosshairSession(
        VKey[] horizKeys, VKey[] vertKeys, ActionMapper actionMapper,
        ModeConfig modeConfig, ICrosshairRenderer? renderer, int minCellPx = 5) {
        _horizKeys = horizKeys;
        _vertKeys = vertKeys;
        _actionMapper = actionMapper;
        _arrowKeysEnabled = modeConfig.ArrowKeys;
        _minCellPx = minCellPx;
        _renderer = renderer;
    }

    public void Activate(Rectangle screenBounds, Point origin) {
        _origin = origin;

        _grid = CrosshairGridCalculator.Calculate(
            screenBounds, _horizKeys.Length, _vertKeys.Length);

        _sm = new CrosshairStateMachine(
            _horizKeys, _vertKeys, _actionMapper, _arrowKeysEnabled, _minCellPx);

        _sm.HorizSelected += OnHorizSelected;
        _sm.VertSelected += OnVertSelected;
        _sm.CellSelected += OnCellSelected;
        _sm.ActionRequested += OnActionRequested;
        _sm.Cancelled += OnCancelled;
        _sm.InvalidKeyPressed += OnInvalidKeyPressed;
        _sm.ArrowMoved += OnArrowMoved;
        _sm.SubgridEntered += OnSubgridEntered;

        _sm.Activate(_grid, origin);
        _renderer?.RenderCross(_grid);
    }

    public void OnKey(VKey key) {
        _sm?.OnKey(key);
    }

    public void Deactivate() {
        if (_sm is not null) {
            _sm.HorizSelected -= OnHorizSelected;
            _sm.VertSelected -= OnVertSelected;
            _sm.CellSelected -= OnCellSelected;
            _sm.ActionRequested -= OnActionRequested;
            _sm.Cancelled -= OnCancelled;
            _sm.InvalidKeyPressed -= OnInvalidKeyPressed;
            _sm.ArrowMoved -= OnArrowMoved;
            _sm.SubgridEntered -= OnSubgridEntered;
            _sm.Reset();
            _sm = null;
        }

        _grid = null;
    }

    private void OnHorizSelected(int col, int keyIndex) {
        if (_grid is null) {
            return;
        }

        CursorMoveRequested?.Invoke(
            CrosshairGridCalculator.CenterOf(_grid.CellAt(_grid.CenterRow, col)));
        _renderer?.HighlightColumn(_grid, col);
    }

    private void OnVertSelected(int row, int keyIndex) {
        if (_grid is null) {
            return;
        }

        CursorMoveRequested?.Invoke(
            CrosshairGridCalculator.CenterOf(_grid.CellAt(row, _grid.CenterCol)));
        _renderer?.HighlightRow(_grid, row);
    }

    private void OnCellSelected(GridCell cell) {
        CursorMoveRequested?.Invoke(CrosshairGridCalculator.CenterOf(cell));
        if (_grid is not null) {
            _renderer?.HighlightCell(_grid, cell);
        }
    }

    private void OnActionRequested(Point point, MouseAction action) {
        ActionRequested?.Invoke(point, action);
    }

    private void OnCancelled() {
        CursorMoveRequested?.Invoke(_origin);
        Cancelled?.Invoke();
    }

    private void OnInvalidKeyPressed() {
        _renderer?.FlashInvalidKey();
    }

    private void OnArrowMoved(int row, int col) {
        if (_grid is null) {
            return;
        }

        var cell = _grid.CellAt(row, col);
        CursorMoveRequested?.Invoke(CrosshairGridCalculator.CenterOf(cell));
        _renderer?.HighlightCell(_grid, cell);
    }

    private void OnSubgridEntered(GridCell parentCell, CrosshairGrid subgrid) {
        CursorMoveRequested?.Invoke(CrosshairGridCalculator.CenterOf(parentCell));
        if (_grid is not null) {
            _renderer?.RenderSubgridCross(_grid, subgrid, parentCell);
        }
    }
}
