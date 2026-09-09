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
    private readonly int _subgridMinCellPx;
    private readonly int _horizLabelBase;
    private readonly int _vertLabelBase;
    private readonly bool _useUniformSubgrid;
    private readonly ICrosshairRenderer? _renderer;
    private readonly IGridRenderer? _gridRenderer;

    private readonly CrosshairStateMachine _sm;
    private CrosshairGrid? _grid;
    private Point _origin;

    // L2 level stack (can be CrosshairSession or UniformGridSession)
    private IModeSession? _l2Session;
    private int _lastHorizCol;
    private int _lastVertRow;
    private Action? _redraw;

    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action<Point>? CursorMoveRequested;

    public CrosshairSession(
        VKey[] horizKeys, VKey[] vertKeys, ActionMapper actionMapper,
        ModeConfig modeConfig, ICrosshairRenderer? renderer, int minCellPx = 10,
        IGridRenderer? gridRenderer = null, int subgridMinCellPx = 0,
        int horizLabelBase = 0, int vertLabelBase = 0, bool useUniformSubgrid = false) {
        _horizKeys = horizKeys;
        _vertKeys = vertKeys;
        _actionMapper = actionMapper;
        _arrowKeysEnabled = modeConfig.ArrowKeys;
        _minCellPx = minCellPx;
        _subgridMinCellPx = subgridMinCellPx > 0 ? subgridMinCellPx : minCellPx * 3;
        _horizLabelBase = horizLabelBase;
        _vertLabelBase = vertLabelBase;
        _useUniformSubgrid = useUniformSubgrid;
        _renderer = renderer;
        _gridRenderer = gridRenderer;

        // Construct SM once — reuse across activations
        _sm = new CrosshairStateMachine(
            _horizKeys, _vertKeys, _actionMapper, _arrowKeysEnabled, _minCellPx,
            _subgridMinCellPx);

        _sm.HorizSelected += OnHorizSelected;
        _sm.VertSelected += OnVertSelected;
        _sm.CellSelected += OnCellSelected;
        _sm.ActionRequested += OnActionRequested;
        _sm.Cancelled += OnCancelled;
        _sm.InvalidKeyPressed += OnInvalidKeyPressed;
        _sm.ArrowMoved += OnArrowMoved;
        _sm.SubgridEntered += OnSubgridEntered;
        _sm.SubgridExited += OnSubgridExited;
    }

    public void Activate(Rectangle screenBounds, Point origin) {
        _origin = origin;

        _grid = CrosshairGridCalculator.Calculate(
            screenBounds, _horizKeys.Length, _vertKeys.Length);

        _sm.Activate(_grid, origin);
        Render(renderer => renderer.RenderCross(_grid), _horizLabelBase, _vertLabelBase);
    }

    public void OnKey(VKey key) {
        if (_l2Session is not null) {
            _l2Session.OnKey(key);
            return;
        }

        _sm.OnKey(key);
    }

    public void Redraw() {
        if (_l2Session is not null) {
            _l2Session.Redraw();
            return;
        }

        _redraw?.Invoke();
    }

    public void Deactivate() {
        PopL2();
        _sm.Reset();
        _grid = null;
        _renderer?.SetLabelOffset(0, 0);
        _redraw = null;
    }

    private void OnHorizSelected(int col, int keyIndex) {
        if (_grid is null) {
            return;
        }

        CursorMoveRequested?.Invoke(
            CrosshairGridCalculator.CenterOf(_grid.CellAt(_grid.CenterRow, col)));
        Render(renderer => renderer.HighlightColumn(_grid, col), _horizLabelBase, _vertLabelBase);
    }

    private void OnVertSelected(int row, int keyIndex) {
        if (_grid is null) {
            return;
        }

        CursorMoveRequested?.Invoke(
            CrosshairGridCalculator.CenterOf(_grid.CellAt(row, _grid.CenterCol)));
        Render(renderer => renderer.HighlightRow(_grid, row), _horizLabelBase, _vertLabelBase);
    }

    private void OnCellSelected(GridCell cell) {
        CursorMoveRequested?.Invoke(CrosshairGridCalculator.CenterOf(cell));
        if (_grid is not null) {
            Render(renderer => renderer.HighlightCell(_grid, cell), _horizLabelBase, _vertLabelBase);
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
        Render(renderer => renderer.HighlightCell(_grid, cell), _horizLabelBase, _vertLabelBase);
    }

    private void OnSubgridEntered(GridCell parentCell) {
        CursorMoveRequested?.Invoke(CrosshairGridCalculator.CenterOf(parentCell));

        // Track L1 axis positions for restore on pop
        _lastHorizCol = parentCell.Col;
        _lastVertRow = parentCell.Row;

        if (_useUniformSubgrid) {
            CreateUniformL2(parentCell);
        } else {
            CreateCrosshairL2(parentCell);
        }
    }

    private void CreateCrosshairL2(GridCell parentCell) {
        var hReduction = DynamicKeyReducer.ComputeActiveKeys(
            _horizKeys, parentCell.Bounds.Width, _subgridMinCellPx, hasCenterCell: true);
        var vReduction = DynamicKeyReducer.ComputeActiveKeys(
            _vertKeys, parentCell.Bounds.Height, _subgridMinCellPx, hasCenterCell: true);

        if (hReduction.IsDisabled || vReduction.IsDisabled) {
            if (_grid is not null) {
                Render(renderer => renderer.HighlightCell(_grid, parentCell), _horizLabelBase, _vertLabelBase);
            }
            return;
        }

        int newHorizOffset = _horizLabelBase + hReduction.OriginalStartIndex;
        int newVertOffset = _vertLabelBase + vReduction.OriginalStartIndex;
        _renderer?.SetLabelOffset(newHorizOffset, newVertOffset);

        // Create nested crosshair session — L3 will use uniform grid
        var l2Mode = new ModeConfig { TwoKey = true, ArrowKeys = _arrowKeysEnabled };
        var l2 = new CrosshairSession(
            hReduction.ActiveKeys, vReduction.ActiveKeys,
            _actionMapper, l2Mode, _renderer,
            minCellPx: _minCellPx,
            gridRenderer: _gridRenderer,
            subgridMinCellPx: _minCellPx,
            horizLabelBase: newHorizOffset,
            vertLabelBase: newVertOffset,
            useUniformSubgrid: true);

        l2.ActionRequested += OnL2ActionRequested;
        l2.Cancelled += OnL2Cancelled;
        l2.CursorMoveRequested += OnL2CursorMoveRequested;

        _l2Session = l2;
        l2.Activate(parentCell.Bounds, CrosshairGridCalculator.CenterOf(parentCell));
    }

    private void CreateUniformL2(GridCell parentCell) {
        // Use uniform grid reduction (no center cell)
        var hReduction = DynamicKeyReducer.ComputeActiveKeys(
            _horizKeys, parentCell.Bounds.Width, _minCellPx, hasCenterCell: false);
        var vReduction = DynamicKeyReducer.ComputeActiveKeys(
            _vertKeys, parentCell.Bounds.Height, _minCellPx, hasCenterCell: false);

        if (hReduction.IsDisabled || vReduction.IsDisabled) {
            if (_grid is not null) {
                Render(renderer => renderer.HighlightCell(_grid, parentCell), _horizLabelBase, _vertLabelBase);
            }
            return;
        }

        int newHorizOffset = _horizLabelBase + hReduction.OriginalStartIndex;
        int newVertOffset = _vertLabelBase + vReduction.OriginalStartIndex;

        var l2Mode = new ModeConfig { TwoKey = true, ArrowKeys = _arrowKeysEnabled };
        var l2 = new UniformGridSession(
            hReduction.ActiveKeys, vReduction.ActiveKeys,
            _actionMapper, l2Mode,
            level3Threshold: 0,
            _gridRenderer,
            minCellPx: _minCellPx,
            baseLabelColOffset: newHorizOffset,
            baseLabelRowOffset: newVertOffset);

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
        _sm.ResetToAwaitInput(_lastVertRow, _lastHorizCol);
    }

    private void OnSubgridExited() {
        if (_grid is null) {
            return;
        }

        var cell = _grid.CellAt(_lastVertRow, _lastHorizCol);
        Render(renderer => renderer.RenderCross(_grid), _horizLabelBase, _vertLabelBase);
        Render(renderer => renderer.HighlightCell(_grid, cell), _horizLabelBase, _vertLabelBase);
        CursorMoveRequested?.Invoke(CrosshairGridCalculator.CenterOf(cell));
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

    private void Render(Action<ICrosshairRenderer> render, int horizOffset, int vertOffset) {
        _redraw = () => {
            if (_renderer is null) {
                return;
            }

            _renderer.SetLabelOffset(horizOffset, vertOffset);
            render(_renderer);
        };
        _redraw();
    }
}
