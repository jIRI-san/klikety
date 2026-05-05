using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Services;

namespace Klikety.Navigation;

/// <summary>
/// State machine for LogGrid mode.
/// States: AwaitInput, FirstKeySet, ArrowCellSet, PostTwoKeyRecenter.
/// Two-key selection recenters immediately. Arrow navigation moves selection
/// without recentering. Enter recenters on arrow-selected cell. Escape always exits.
/// Action keys dispatch explicitly from any state.
/// </summary>
public sealed class LogGridStateMachine {
    readonly VKey[] _horizKeys;
    readonly VKey[] _vertKeys;
    readonly ActionMapper _actionMapper;
    readonly bool _arrowKeysEnabled;

    LogGrid? _grid;
    int _selectedCol = -1;
    int _arrowRow = -1;
    int _arrowCol = -1;
    Point _actionPoint;

    readonly Dictionary<VKey, int> _horizKeyMap = [];
    readonly Dictionary<VKey, int> _vertKeyMap = [];

    public enum State { Idle, AwaitInput, FirstKeySet, ArrowCellSet, PostTwoKeyRecenter }

    public State CurrentState { get; private set; } = State.Idle;

    // Events
    public event Action<int>? FirstKeySelected;         // col index
    public event Action<GridCell>? CellSelected;        // two-key selection complete
    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action? InvalidKeyPressed;
    public event Action<int, int>? ArrowMoved;          // (row, col) after arrow nav
    public event Action<GridCell>? ArrowRecenterRequested; // Enter in ArrowCellSet

    public LogGridStateMachine(
        VKey[] horizKeys, VKey[] vertKeys, ActionMapper actionMapper,
        bool arrowKeysEnabled) {
        ArgumentNullException.ThrowIfNull(horizKeys);
        ArgumentNullException.ThrowIfNull(vertKeys);

        _horizKeys = horizKeys;
        _vertKeys = vertKeys;
        _actionMapper = actionMapper;
        _arrowKeysEnabled = arrowKeysEnabled;

        for (int i = 0; i < horizKeys.Length; i++) {
            _horizKeyMap[horizKeys[i]] = i;
        }

        for (int i = 0; i < vertKeys.Length; i++) {
            _vertKeyMap[vertKeys[i]] = i;
        }
    }

    public void Activate(LogGrid grid, Point origin) {
        _grid = grid;
        _selectedCol = -1;
        _arrowRow = grid.Rows / 2;
        _arrowCol = grid.Cols / 2;
        _actionPoint = origin;
        CurrentState = State.AwaitInput;
    }

    public void Reset() {
        _grid = null;
        _selectedCol = -1;
        _arrowRow = -1;
        _arrowCol = -1;
        CurrentState = State.Idle;
    }

    public void OnKey(VKey key) {
        if (_grid is null || CurrentState == State.Idle) {
            return;
        }

        // Action key — fires from any state
        if (_actionMapper.IsActionKey(key)) {
            ActionRequested?.Invoke(_actionPoint, _actionMapper.Map(key)!.Value);
            return;
        }

        if (key == VKey.Escape) {
            Cancelled?.Invoke();
            return;
        }

        if (key == VKey.Return) {
            HandleEnter();
            return;
        }

        // Arrow keys — in AwaitInput, ArrowCellSet, or PostTwoKeyRecenter
        if (_arrowKeysEnabled && IsArrowKey(key)
            && CurrentState is State.AwaitInput or State.ArrowCellSet or State.PostTwoKeyRecenter) {
            HandleArrow(key);
            return;
        }

        // First key (horizontal)
        if (_horizKeyMap.TryGetValue(key, out var hIdx)) {
            HandleFirstKey(hIdx);
            return;
        }

        // Second key (vertical) — only valid in FirstKeySet
        if (_vertKeyMap.TryGetValue(key, out var vIdx)) {
            HandleSecondKey(vIdx);
            return;
        }

        InvalidKeyPressed?.Invoke();
    }

    void HandleFirstKey(int colIndex) {
        if (_grid is null) {
            return;
        }

        if (colIndex >= _grid.Cols) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        _selectedCol = colIndex;
        _actionPoint = CellCenter(_grid.CellAt(_grid.Rows / 2, colIndex));
        CurrentState = State.FirstKeySet;
        FirstKeySelected?.Invoke(colIndex);
    }

    void HandleSecondKey(int rowIndex) {
        if (_grid is null) {
            return;
        }

        if (CurrentState != State.FirstKeySet) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        if (rowIndex >= _grid.Rows) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        var cell = _grid.CellAt(rowIndex, _selectedCol);
        _actionPoint = CellCenter(cell);
        CurrentState = State.PostTwoKeyRecenter;
        CellSelected?.Invoke(cell);
    }

    void HandleEnter() {
        switch (CurrentState) {
            case State.ArrowCellSet:
                var cell = _grid!.CellAt(_arrowRow, _arrowCol);
                _actionPoint = CellCenter(cell);
                CurrentState = State.PostTwoKeyRecenter;
                ArrowRecenterRequested?.Invoke(cell);
                break;

            case State.PostTwoKeyRecenter:
                // Enter ignored after two-key recenter
                break;

            default:
                InvalidKeyPressed?.Invoke();
                break;
        }
    }

    void HandleArrow(VKey key) {
        if (_grid is null) {
            return;
        }

        int newRow = _arrowRow;
        int newCol = _arrowCol;

        switch (key) {
            case VKey.Left:
                newCol = Math.Max(0, _arrowCol - 1);
                break;
            case VKey.Right:
                newCol = Math.Min(_grid.Cols - 1, _arrowCol + 1);
                break;
            case VKey.Up:
                newRow = Math.Max(0, _arrowRow - 1);
                break;
            case VKey.Down:
                newRow = Math.Min(_grid.Rows - 1, _arrowRow + 1);
                break;
        }

        if (newRow == _arrowRow && newCol == _arrowCol) {
            // At edge, no movement
            return;
        }

        _arrowRow = newRow;
        _arrowCol = newCol;
        _actionPoint = CellCenter(_grid.CellAt(newRow, newCol));
        CurrentState = State.ArrowCellSet;
        ArrowMoved?.Invoke(newRow, newCol);
    }

    /// <summary>
    /// Updates the grid and resets to AwaitInput at the grid center.
    /// Used after recentering.
    /// </summary>
    public void UpdateGrid(LogGrid grid) {
        _grid = grid;
        _selectedCol = -1;
        _arrowRow = grid.Rows / 2;
        _arrowCol = grid.Cols / 2;
        _actionPoint = grid.CenterPoint;
        CurrentState = State.AwaitInput;
    }

    static Point CellCenter(GridCell cell) =>
        new(cell.Bounds.X + cell.Bounds.Width / 2, cell.Bounds.Y + cell.Bounds.Height / 2);

    static bool IsArrowKey(VKey key) =>
        key is VKey.Left or VKey.Right or VKey.Up or VKey.Down;
}
