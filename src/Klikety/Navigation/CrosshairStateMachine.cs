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
    private readonly int _subgridMinCellPx;

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
    public event Action<GridCell>? SubgridEntered;      // Cell selected for L2 entry
    public event Action? SubgridExited;                 // L2 popped, returned to L1 cross

    public CrosshairStateMachine(
        VKey[] horizKeys, VKey[] vertKeys, ActionMapper actionMapper,
        bool arrowKeysEnabled, int minCellPx = 10, int subgridMinCellPx = 0) {
        ArgumentNullException.ThrowIfNull(horizKeys);
        ArgumentNullException.ThrowIfNull(vertKeys);

        if (horizKeys.Length > 52) {
            throw new ArgumentOutOfRangeException(nameof(horizKeys), "Maximum 52 axis keys supported.");
        }

        if (vertKeys.Length > 52) {
            throw new ArgumentOutOfRangeException(nameof(vertKeys), "Maximum 52 axis keys supported.");
        }

        // Validate disjointness — overlapping keys would silently prefer horiz
        var horizSet = new HashSet<VKey>(horizKeys);
        foreach (var vk in vertKeys) {
            if (horizSet.Contains(vk)) {
                throw new ArgumentException($"Key {vk} appears in both horizontal and vertical arrays.", nameof(vertKeys));
            }
        }

        _horizKeys = horizKeys;
        _vertKeys = vertKeys;
        _actionMapper = actionMapper;
        _arrowKeysEnabled = arrowKeysEnabled;
        _minCellPx = minCellPx;
        _subgridMinCellPx = subgridMinCellPx > 0 ? subgridMinCellPx : minCellPx * 3;

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
            // Both set — auto-enter L2 subgrid
            int row = KeyIndexToRow(_vertIndex, _grid.CenterRow);
            var cell = _grid.CellAt(row, col);
            EnterSubgrid(cell);
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
            // Both set — auto-enter L2 subgrid
            int col = KeyIndexToCol(_horizIndex, _grid.CenterCol);
            var cell = _grid.CellAt(row, col);
            EnterSubgrid(cell);
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
                    // Re-enter L2 at current intersection
                    int row2 = _vertIndex >= 0 ? KeyIndexToRow(_vertIndex, _grid.CenterRow) : _grid.CenterRow;
                    int col2 = _horizIndex >= 0 ? KeyIndexToCol(_horizIndex, _grid.CenterCol) : _grid.CenterCol;
                    EnterSubgrid(_grid.CellAt(row2, col2));
                    break;
                }
        }
    }

    private void EnterSubgrid(GridCell cell) {
        var hReduction = DynamicKeyReducer.ComputeActiveKeys(
            _horizKeys, cell.Bounds.Width, _subgridMinCellPx, hasCenterCell: true);
        var vReduction = DynamicKeyReducer.ComputeActiveKeys(
            _vertKeys, cell.Bounds.Height, _subgridMinCellPx, hasCenterCell: true);

        if (hReduction.IsDisabled || vReduction.IsDisabled) {
            // Cell too small for subgrid — position cursor at center, await user action key
            _actionPoint = CrosshairGridCalculator.CenterOf(cell);
            CellSelected?.Invoke(cell);
            CurrentState = State.BothSet;
            return;
        }

        _actionPoint = CrosshairGridCalculator.CenterOf(cell);
        CurrentState = State.BothSet;
        try {
            SubgridEntered?.Invoke(cell);
        } catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException) {
            // L2 session construction failed — fall back to CellSelected
            CellSelected?.Invoke(cell);
        }
    }

    private void HandleEscape() {
        switch (CurrentState) {
            case State.BothSet:
                // LIFO undo — clear last-set axis, or fall to AwaitInput if only one was set
                if (_lastSetWasHoriz && _horizIndex >= 0) {
                    _horizIndex = -1;
                    if (_vertIndex >= 0) {
                        int row = KeyIndexToRow(_vertIndex, _grid!.CenterRow);
                        var cell = _grid.CellAt(row, _grid.CenterCol);
                        _actionPoint = CrosshairGridCalculator.CenterOf(cell);
                        CurrentState = State.VertSet;
                        VertSelected?.Invoke(row, _vertIndex);
                    } else {
                        _actionPoint = CrosshairGridCalculator.CenterOf(_grid!.CenterCell);
                        CurrentState = State.AwaitInput;
                    }
                } else if (!_lastSetWasHoriz && _vertIndex >= 0) {
                    _vertIndex = -1;
                    if (_horizIndex >= 0) {
                        int col = KeyIndexToCol(_horizIndex, _grid!.CenterCol);
                        var cell = _grid.CellAt(_grid.CenterRow, col);
                        _actionPoint = CrosshairGridCalculator.CenterOf(cell);
                        CurrentState = State.HorizSet;
                        HorizSelected?.Invoke(col, _horizIndex);
                    } else {
                        _actionPoint = CrosshairGridCalculator.CenterOf(_grid!.CenterCell);
                        CurrentState = State.AwaitInput;
                    }
                } else {
                    // Fallback: both or neither set via Enter path → return to AwaitInput
                    _horizIndex = -1;
                    _vertIndex = -1;
                    _actionPoint = CrosshairGridCalculator.CenterOf(_grid!.CenterCell);
                    CurrentState = State.AwaitInput;
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

    /// <summary>
    /// Resets state to AwaitInput at the given grid position.
    /// Used when L2 session is popped — returns to navigable L1 cross.
    /// </summary>
    public void ResetToAwaitInput(int row, int col) {
        if (_grid is null) {
            return;
        }

        _horizIndex = -1;
        _vertIndex = -1;
        _arrowRow = row;
        _arrowCol = col;
        _actionPoint = CrosshairGridCalculator.CenterOf(_grid.CellAt(row, col));
        CurrentState = State.AwaitInput;
        SubgridExited?.Invoke();
    }
}
