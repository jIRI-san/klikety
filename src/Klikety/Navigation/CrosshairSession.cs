using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Services;

namespace Klikety.Navigation;

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
    private readonly IGridRenderer? _gridRenderer;

    private readonly CrosshairStateMachine _sm;
    private CrosshairGrid? _grid;
    private Point _origin;

    // L2 level stack
    private UniformGridSession? _l2Session;
    private int _lastHorizCol;
    private int _lastVertRow;

    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action<Point>? CursorMoveRequested;

    public CrosshairSession(
        VKey[] horizKeys, VKey[] vertKeys, ActionMapper actionMapper,
        ModeConfig modeConfig, ICrosshairRenderer? renderer, int minCellPx = 5,
        IGridRenderer? gridRenderer = null) {
        _horizKeys = horizKeys;
        _vertKeys = vertKeys;
        _actionMapper = actionMapper;
        _arrowKeysEnabled = modeConfig.ArrowKeys;
        _minCellPx = minCellPx;
        _renderer = renderer;
        _gridRenderer = gridRenderer;

        // Construct SM once — reuse across activations
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
    }

    public void Activate(Rectangle screenBounds, Point origin) {
        _origin = origin;

        _grid = CrosshairGridCalculator.Calculate(
            screenBounds, _horizKeys.Length, _vertKeys.Length);

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

    private void OnSubgridEntered(GridCell parentCell) {
        CursorMoveRequested?.Invoke(CrosshairGridCalculator.CenterOf(parentCell));

        // Track L1 axis positions for restore on pop
        _lastHorizCol = parentCell.Col;
        _lastVertRow = parentCell.Row;

        // Compute reduced keys for L2
        var hReduction = DynamicKeyReducer.ComputeActiveKeys(
            _horizKeys, parentCell.Bounds.Width, _minCellPx, hasCenterCell: false);
        var vReduction = DynamicKeyReducer.ComputeActiveKeys(
            _vertKeys, parentCell.Bounds.Height, _minCellPx, hasCenterCell: false);

        if (hReduction.IsDisabled || vReduction.IsDisabled) {
            // Can't enter L2 — highlight the cell
            if (_grid is not null) {
                _renderer?.HighlightCell(_grid, parentCell);
            }
            return;
        }

        // Create L2 uniform grid session
        var l2Mode = new ModeConfig { TwoKey = true, ArrowKeys = _arrowKeysEnabled };
        var l2 = new UniformGridSession(
            hReduction.ActiveKeys, vReduction.ActiveKeys,
            _actionMapper, l2Mode, level3Threshold: 0, _gridRenderer);

        l2.ActionRequested += OnL2ActionRequested;
        l2.Cancelled += OnL2Cancelled;
        l2.CursorMoveRequested += OnL2CursorMoveRequested;

        _l2Session = l2;
        l2.Activate(parentCell.Bounds, CrosshairGridCalculator.CenterOf(parentCell));
    }

    private void OnL2ActionRequested(Point point, MouseAction action) {
        ActionRequested?.Invoke(point, action);
    }

    private void OnL2CursorMoveRequested(Point point) {
        CursorMoveRequested?.Invoke(point);
    }

    private void OnL2Cancelled() {
        PopL2();

        // Restore L1 cross at previously-selected axis positions
        if (_grid is not null) {
            var cell = _grid.CellAt(_lastVertRow, _lastHorizCol);
            CursorMoveRequested?.Invoke(CrosshairGridCalculator.CenterOf(cell));
            _renderer?.HighlightCell(_grid, cell);
        }
    }

    private void PopL2() {
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
