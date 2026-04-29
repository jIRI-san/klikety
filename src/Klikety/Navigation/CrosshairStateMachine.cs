using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;

namespace Klikety.Navigation;

/// <summary>
/// State machine for Crosshair mode L1 navigation.
/// Axis keys select horizontal/vertical offsets. Enter confirms selection.
/// Same-axis re-press overrides. Esc clears partial → cancels.
/// </summary>
public sealed class CrosshairStateMachine {
    private readonly VKey[] _horizKeys;
    private readonly VKey[] _vertKeys;
    private readonly ActionMapper _actionMapper;
    private readonly bool _arrowKeysEnabled;
    private readonly int _minCellPx;

    private CrosshairGrid? _grid;
    private int _horizIndex = -1; // -1 = not set
    private int _vertIndex = -1;  // -1 = not set
    private int _arrowRow;
    private int _arrowCol;
    private Point _actionPoint;
    private bool _lastSetWasHoriz; // tracks which axis was set last (for Escape from BothSet)

    // Key lookup tables (VKey → index in axis array)
    private readonly Dictionary<VKey, int> _horizKeyMap = [];
    private readonly Dictionary<VKey, int> _vertKeyMap = [];

    public enum State { Idle, AwaitInput, HorizSet, VertSet, BothSet }

    public State CurrentState { get; private set; } = State.Idle;

    // Events
    public event Action<int, int>? HorizSelected;      // (col, horizKeyIndex)
    public event Action<int, int>? VertSelected;       // (row, vertKeyIndex)
    public event Action<GridCell>? CellSelected;       // Both axes set → cell highlighted
    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action? InvalidKeyPressed;
    public event Action<int, int>? ArrowMoved;         // (row, col) after arrow nav
    public event Action<GridCell, CrosshairGrid>? SubgridEntered; // Cell selected + subgrid computed

    public CrosshairStateMachine(
        VKey[] horizKeys, VKey[] vertKeys, ActionMapper actionMapper,
        bool arrowKeysEnabled, int minCellPx = 5) {
        if (horizKeys.Length > 52) {
            throw new ArgumentOutOfRangeException(nameof(horizKeys), "Maximum 52 axis keys supported.");
        }

        if (vertKeys.Length > 52) {
            throw new ArgumentOutOfRangeException(nameof(vertKeys), "Maximum 52 axis keys supported.");
        }

        _horizKeys = horizKeys;
        _vertKeys = vertKeys;
        _actionMapper = actionMapper;
        _arrowKeysEnabled = arrowKeysEnabled;
        _minCellPx = minCellPx;

        for (int i = 0; i < horizKeys.Length; i++) {
            _horizKeyMap[horizKeys[i]] = i;
        }

        for (int i = 0; i < vertKeys.Length; i++) {
            _vertKeyMap[vertKeys[i]] = i;
        }
    }

    public void Activate(CrosshairGrid grid, Point origin) {
        _grid = grid;
        _horizIndex = -1;
        _vertIndex = -1;
        _arrowRow = grid.CenterRow;
        _arrowCol = grid.CenterCol;
        _actionPoint = origin;
        CurrentState = State.AwaitInput;
    }

    public void Reset() {
        _grid = null;
        _horizIndex = -1;
        _vertIndex = -1;
        CurrentState = State.Idle;
    }

    public void OnKey(VKey key) {
        if (_grid is null || CurrentState == State.Idle) {
            return;
        }

        // Action key — fires action at current point
        if (_actionMapper.IsActionKey(key)) {
            var action = _actionMapper.Map(key)!.Value;
            ActionRequested?.Invoke(_actionPoint, action);
            return;
        }

        // Escape
        if (key == VKey.Escape) {
            HandleEscape();
            return;
        }

        // Enter — confirm selection / enter subgrid
        if (key == VKey.Return) {
            HandleEnter();
            return;
        }

        // Arrow keys (only in AwaitInput, before any axis key)
        if (_arrowKeysEnabled && CurrentState == State.AwaitInput && IsArrowKey(key)) {
            HandleArrow(key);
            return;
        }

        // Horizontal axis key
        if (_horizKeyMap.TryGetValue(key, out var hIdx)) {
            HandleHorizKey(hIdx);
            return;
        }

        // Vertical axis key
        if (_vertKeyMap.TryGetValue(key, out var vIdx)) {
            HandleVertKey(vIdx);
            return;
        }

        InvalidKeyPressed?.Invoke();
    }

    private void HandleHorizKey(int keyIndex) {
        _horizIndex = keyIndex;
        _lastSetWasHoriz = true;

        // Column in grid: keys map to columns, skipping center column
        // Key index < centerCol → col = keyIndex
        // Key index >= centerCol → col = keyIndex + 1
        int col = KeyIndexToCol(keyIndex, _grid!.CenterCol);

        if (_vertIndex >= 0) {
            // Both set
            int row = KeyIndexToRow(_vertIndex, _grid.CenterRow);
            CurrentState = State.BothSet;
            var cell = _grid.CellAt(row, col);
            _actionPoint = CrosshairGridCalculator.CenterOf(cell);
            CellSelected?.Invoke(cell);
        } else {
            CurrentState = State.HorizSet;
            // Action point: center of center-row cell at this column
            var cell = _grid.CellAt(_grid.CenterRow, col);
            _actionPoint = CrosshairGridCalculator.CenterOf(cell);
            HorizSelected?.Invoke(col, keyIndex);
        }
    }

    private void HandleVertKey(int keyIndex) {
        _vertIndex = keyIndex;
        _lastSetWasHoriz = false;

        int row = KeyIndexToRow(keyIndex, _grid!.CenterRow);

        if (_horizIndex >= 0) {
            // Both set
            int col = KeyIndexToCol(_horizIndex, _grid.CenterCol);
            CurrentState = State.BothSet;
            var cell = _grid.CellAt(row, col);
            _actionPoint = CrosshairGridCalculator.CenterOf(cell);
            CellSelected?.Invoke(cell);
        } else {
            CurrentState = State.VertSet;
            // Action point: center of center-col cell at this row
            var cell = _grid.CellAt(row, _grid.CenterCol);
            _actionPoint = CrosshairGridCalculator.CenterOf(cell);
            VertSelected?.Invoke(row, keyIndex);
        }
    }

    private void HandleEnter() {
        if (_grid is null) {
            return;
        }

        switch (CurrentState) {
            case State.AwaitInput:
                // No axis set → use arrow-navigated position (defaults to center)
                EnterSubgrid(_grid.CellAt(_arrowRow, _arrowCol));
                break;

            case State.HorizSet: {
                // Only horiz → select center-row cell at that column → subgrid
                int col = KeyIndexToCol(_horizIndex, _grid.CenterCol);
                var cell = _grid.CellAt(_grid.CenterRow, col);
                EnterSubgrid(cell);
                break;
            }

            case State.VertSet: {
                // Only vert → select center-col cell at that row → subgrid
                int row = KeyIndexToRow(_vertIndex, _grid.CenterRow);
                var cell = _grid.CellAt(row, _grid.CenterCol);
                EnterSubgrid(cell);
                break;
            }

            case State.BothSet: {
                // Both set → enter subgrid at intersection
                int col = KeyIndexToCol(_horizIndex, _grid.CenterCol);
                int row = KeyIndexToRow(_vertIndex, _grid.CenterRow);
                var cell = _grid.CellAt(row, col);
                EnterSubgrid(cell);
                break;
            }
        }
    }

    private void EnterSubgrid(GridCell cell) {
        // Compute subgrid using dynamic key reduction
        var hReduction = DynamicKeyReducer.ComputeActiveKeys(
            _horizKeys, cell.Bounds.Width, _minCellPx, hasCenterCell: true);
        var vReduction = DynamicKeyReducer.ComputeActiveKeys(
            _vertKeys, cell.Bounds.Height, _minCellPx, hasCenterCell: true);

        if (hReduction.IsDisabled || vReduction.IsDisabled) {
            // Cell too small for subgrid — treat as action at center
            _actionPoint = CrosshairGridCalculator.CenterOf(cell);
            ActionRequested?.Invoke(_actionPoint, MouseAction.LeftClick);
            return;
        }

        var subgrid = CrosshairGridCalculator.Calculate(
            cell.Bounds, hReduction.ActiveKeys.Length, vReduction.ActiveKeys.Length);

        _actionPoint = CrosshairGridCalculator.CenterOf(cell);
        // TODO: step 3.5 — push L2/L3 session onto level stack for recursive subgrid navigation
        SubgridEntered?.Invoke(cell, subgrid);
    }

    private void HandleEscape() {
        switch (CurrentState) {
            case State.BothSet:
                // Clear the most recently set axis (LIFO undo)
                if (_lastSetWasHoriz) {
                    _horizIndex = -1;
                    int row = KeyIndexToRow(_vertIndex, _grid!.CenterRow);
                    var cell = _grid.CellAt(row, _grid.CenterCol);
                    _actionPoint = CrosshairGridCalculator.CenterOf(cell);
                    CurrentState = State.VertSet;
                    VertSelected?.Invoke(row, _vertIndex);
                } else {
                    _vertIndex = -1;
                    int col = KeyIndexToCol(_horizIndex, _grid!.CenterCol);
                    var cell = _grid.CellAt(_grid.CenterRow, col);
                    _actionPoint = CrosshairGridCalculator.CenterOf(cell);
                    CurrentState = State.HorizSet;
                    HorizSelected?.Invoke(col, _horizIndex);
                }
                break;

            case State.HorizSet:
                _horizIndex = -1;
                _actionPoint = CrosshairGridCalculator.CenterOf(_grid!.CenterCell);
                CurrentState = State.AwaitInput;
                break;

            case State.VertSet:
                _vertIndex = -1;
                _actionPoint = CrosshairGridCalculator.CenterOf(_grid!.CenterCell);
                CurrentState = State.AwaitInput;
                break;

            case State.AwaitInput:
                Cancelled?.Invoke();
                break;
        }
    }

    private void HandleArrow(VKey key) {
        if (_grid is null) {
            return;
        }

        var (newRow, newCol) = CrossArrowNavigator.Move(
            key, _arrowRow, _arrowCol,
            _grid.CenterRow, _grid.CenterCol,
            _grid.Rows, _grid.Cols);

        _arrowRow = newRow;
        _arrowCol = newCol;
        var cell = _grid.CellAt(newRow, newCol);
        _actionPoint = CrosshairGridCalculator.CenterOf(cell);
        ArrowMoved?.Invoke(newRow, newCol);
    }

    /// <summary>
    /// Maps a key index to a grid column, skipping the center column.
    /// Keys 0..centerCol-1 → columns 0..centerCol-1.
    /// Keys centerCol..N-1 → columns centerCol+1..N.
    /// </summary>
    private static int KeyIndexToCol(int keyIndex, int centerCol) {
        return keyIndex < centerCol ? keyIndex : keyIndex + 1;
    }

    /// <summary>
    /// Maps a key index to a grid row, skipping the center row.
    /// </summary>
    private static int KeyIndexToRow(int keyIndex, int centerRow) {
        return keyIndex < centerRow ? keyIndex : keyIndex + 1;
    }

    private static bool IsArrowKey(VKey key) =>
        key is VKey.Left or VKey.Right or VKey.Up or VKey.Down;
}
