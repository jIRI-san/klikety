using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;

namespace Klikety.Navigation;

/// <summary>
/// State machine for LogCrosshair mode. Axis keys select horizontal/vertical offsets.
/// Same-axis re-press overrides. Both axes set → auto-enters L2 subgrid.
/// Esc clears the last-set axis (LIFO). Enter enters subgrid at current position.
/// Degenerate cells (from log grid edge) produce <see cref="InvalidKeyPressed"/>.
/// </summary>
public sealed class LogCrosshairStateMachine {
    readonly VKey[] _horizKeys;
    readonly VKey[] _vertKeys;
    readonly ActionMapper _actionMapper;
    readonly bool _arrowKeysEnabled;
    readonly int _minCellPx;

    LogCrosshairGrid? _grid;
    int _horizIndex = -1; // -1 = not set
    int _vertIndex = -1;
    bool _lastSetWasHoriz;
    int _arrowRow;
    int _arrowCol;
    Point _actionPoint;

    readonly Dictionary<VKey, int> _horizKeyMap = [];
    readonly Dictionary<VKey, int> _vertKeyMap = [];

    public enum State { Idle, AwaitInput, HorizSet, VertSet, BothSet }

    public State CurrentState { get; private set; } = State.Idle;

    // Events
    public event Action<int, int>? HorizSelected;      // (col, keyIndex)
    public event Action<int, int>? VertSelected;        // (row, keyIndex)
    public event Action<GridCell>? CellSelected;        // Both axes set but subgrid disabled
    public event Action<GridCell>? SubgridEntered;      // Both axes set → L2 at this cell
    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action? InvalidKeyPressed;
    public event Action<int, int>? ArrowMoved;          // (row, col) after arrow nav
    public event Action? AxisCleared;                   // Escape cleared last axis → back to base cross

    public LogCrosshairStateMachine(
        VKey[] horizKeys, VKey[] vertKeys, ActionMapper actionMapper,
        bool arrowKeysEnabled, int minCellPx = 5) {
        ArgumentNullException.ThrowIfNull(horizKeys);
        ArgumentNullException.ThrowIfNull(vertKeys);

        if (horizKeys.Length > 52) {
            throw new ArgumentOutOfRangeException(nameof(horizKeys), "Maximum 52 axis keys supported.");
        }

        if (vertKeys.Length > 52) {
            throw new ArgumentOutOfRangeException(nameof(vertKeys), "Maximum 52 axis keys supported.");
        }

        var horizSet = new HashSet<VKey>(horizKeys);
        foreach (var vk in vertKeys) {
            if (horizSet.Contains(vk)) {
                throw new ArgumentException(
                    $"Key {vk} appears in both horizontal and vertical arrays.", nameof(vertKeys));
            }
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

    public void Activate(LogCrosshairGrid grid, Point origin) {
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
            ActionRequested?.Invoke(_actionPoint, _actionMapper.Map(key)!.Value);
            return;
        }

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

        if (_horizKeyMap.TryGetValue(key, out var hIdx)) {
            HandleHorizKey(hIdx);
            return;
        }

        if (_vertKeyMap.TryGetValue(key, out var vIdx)) {
            HandleVertKey(vIdx);
            return;
        }

        InvalidKeyPressed?.Invoke();
    }

    void HandleHorizKey(int keyIndex) {
        int col = KeyIndexToCol(keyIndex, _grid!.CenterCol);
        int row = _vertIndex >= 0
            ? KeyIndexToRow(_vertIndex, _grid.CenterRow)
            : _grid.CenterRow;

        if (_grid.IsDegenerate(row, col)) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        _horizIndex = keyIndex;
        _lastSetWasHoriz = true;

        var cell = _grid.CellAt(row, col);
        _actionPoint = LogGridCalculator.CenterOf(cell);

        if (_vertIndex >= 0) {
            EnterSubgrid(cell);
        } else {
            CurrentState = State.HorizSet;
            HorizSelected?.Invoke(col, keyIndex);
        }
    }

    void HandleVertKey(int keyIndex) {
        int row = KeyIndexToRow(keyIndex, _grid!.CenterRow);
        int col = _horizIndex >= 0
            ? KeyIndexToCol(_horizIndex, _grid.CenterCol)
            : _grid.CenterCol;

        if (_grid.IsDegenerate(row, col)) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        _vertIndex = keyIndex;
        _lastSetWasHoriz = false;

        var cell = _grid.CellAt(row, col);
        _actionPoint = LogGridCalculator.CenterOf(cell);

        if (_horizIndex >= 0) {
            EnterSubgrid(cell);
        } else {
            CurrentState = State.VertSet;
            VertSelected?.Invoke(row, keyIndex);
        }
    }

    void HandleEscape() {
        switch (CurrentState) {
            case State.BothSet:
                // LIFO undo — clear last-set axis, or fall to AwaitInput if only one was set
                if (_lastSetWasHoriz && _horizIndex >= 0) {
                    _horizIndex = -1;
                    if (_vertIndex >= 0) {
                        int row = KeyIndexToRow(_vertIndex, _grid!.CenterRow);
                        var cell = _grid.CellAt(row, _grid.CenterCol);
                        _actionPoint = LogGridCalculator.CenterOf(cell);
                        CurrentState = State.VertSet;
                        VertSelected?.Invoke(row, _vertIndex);
                    } else {
                        _actionPoint = LogGridCalculator.CenterOf(_grid!.CenterCell);
                        _arrowRow = _grid.CenterRow;
                        _arrowCol = _grid.CenterCol;
                        CurrentState = State.AwaitInput;
                        AxisCleared?.Invoke();
                    }
                } else if (!_lastSetWasHoriz && _vertIndex >= 0) {
                    _vertIndex = -1;
                    if (_horizIndex >= 0) {
                        int col = KeyIndexToCol(_horizIndex, _grid!.CenterCol);
                        var cell = _grid.CellAt(_grid.CenterRow, col);
                        _actionPoint = LogGridCalculator.CenterOf(cell);
                        CurrentState = State.HorizSet;
                        HorizSelected?.Invoke(col, _horizIndex);
                    } else {
                        _actionPoint = LogGridCalculator.CenterOf(_grid!.CenterCell);
                        _arrowRow = _grid.CenterRow;
                        _arrowCol = _grid.CenterCol;
                        CurrentState = State.AwaitInput;
                        AxisCleared?.Invoke();
                    }
                } else {
                    // Fallback: entered via Enter path → return to AwaitInput
                    _horizIndex = -1;
                    _vertIndex = -1;
                    _actionPoint = LogGridCalculator.CenterOf(_grid!.CenterCell);
                    _arrowRow = _grid.CenterRow;
                    _arrowCol = _grid.CenterCol;
                    CurrentState = State.AwaitInput;
                    AxisCleared?.Invoke();
                }
                break;

            case State.HorizSet:
                _horizIndex = -1;
                _actionPoint = LogGridCalculator.CenterOf(_grid!.CenterCell);
                _arrowRow = _grid.CenterRow;
                _arrowCol = _grid.CenterCol;
                CurrentState = State.AwaitInput;
                AxisCleared?.Invoke();
                break;

            case State.VertSet:
                _vertIndex = -1;
                _actionPoint = LogGridCalculator.CenterOf(_grid!.CenterCell);
                _arrowRow = _grid.CenterRow;
                _arrowCol = _grid.CenterCol;
                CurrentState = State.AwaitInput;
                AxisCleared?.Invoke();
                break;

            case State.AwaitInput:
                Cancelled?.Invoke();
                break;
        }
    }

    void HandleArrow(VKey key) {
        if (_grid is null) {
            return;
        }

        var (newRow, newCol) = CrossArrowNavigator.Move(
            key, _arrowRow, _arrowCol,
            _grid.CenterRow, _grid.CenterCol,
            _grid.Rows, _grid.Cols);

        if (_grid.IsDegenerate(newRow, newCol)) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        _arrowRow = newRow;
        _arrowCol = newCol;
        var cell = _grid.CellAt(newRow, newCol);
        _actionPoint = LogGridCalculator.CenterOf(cell);
        ArrowMoved?.Invoke(newRow, newCol);
    }

    void HandleEnter() {
        if (_grid is null) {
            return;
        }

        switch (CurrentState) {
            case State.AwaitInput:
                EnterSubgrid(_grid.CellAt(_arrowRow, _arrowCol));
                break;

            case State.HorizSet: {
                    int col = KeyIndexToCol(_horizIndex, _grid.CenterCol);
                    var cell = _grid.CellAt(_grid.CenterRow, col);
                    EnterSubgrid(cell);
                    break;
                }

            case State.VertSet: {
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

    void EnterSubgrid(GridCell cell) {
        var hReduction = DynamicKeyReducer.ComputeActiveKeys(
            _horizKeys, cell.Bounds.Width, _minCellPx, hasCenterCell: false);
        var vReduction = DynamicKeyReducer.ComputeActiveKeys(
            _vertKeys, cell.Bounds.Height, _minCellPx, hasCenterCell: false);

        _actionPoint = LogGridCalculator.CenterOf(cell);

        if (hReduction.IsDisabled || vReduction.IsDisabled) {
            CellSelected?.Invoke(cell);
            CurrentState = State.BothSet;
            return;
        }

        CurrentState = State.BothSet;
        SubgridEntered?.Invoke(cell);
    }

    static int KeyIndexToCol(int keyIndex, int centerCol) =>
        keyIndex < centerCol ? keyIndex : keyIndex + 1;

    static int KeyIndexToRow(int keyIndex, int centerRow) =>
        keyIndex < centerRow ? keyIndex : keyIndex + 1;

    static bool IsArrowKey(VKey key) =>
        key is VKey.Left or VKey.Right or VKey.Up or VKey.Down;
}
