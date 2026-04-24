using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;

namespace Klikety.Navigation;

public enum NavigatorState {
    Idle,
    L1_AwaitFirst,
    L1_AwaitSecond,
    L1_AwaitAction,
    L2_AwaitFirst,
    L2_AwaitSecond,
    L2_AwaitAction,
    L3_AwaitFirst,
    L3_AwaitSecond,
    L3_AwaitAction,
}

public enum ScreenHalf { Left, Right }

/// <summary>
/// Core navigation state machine. Processes VKey inputs and raises events
/// for overlay visual updates, cursor movement, and action dispatch.
/// Supports split-screen: left-hand keys control the left half, right-hand keys the right.
/// </summary>
public sealed class NavigatorStateMachine {
    private readonly HalfKeySetsConfig _leftKeys;
    private readonly HalfKeySetsConfig _rightKeys;
    private readonly ActionMapper _actionMapper;
    private readonly NavigationMode _navigationMode;
    private readonly int _level3Threshold;

    // Active half — determined by first key press
    private ScreenHalf _activeHalf;
    private VKey[] _activeFirstKeys = [];
    private VKey[] _activeSecondKeys = [];

    // State
    public NavigatorState State { get; private set; } = NavigatorState.Idle;
    private Point _originPoint;
    private int _selectedCol;

    // Grid cells per level
    private IReadOnlyList<GridCell> _leftL1Cells = [];
    private IReadOnlyList<GridCell> _rightL1Cells = [];
    private IReadOnlyList<GridCell> _l1Cells = [];
    private IReadOnlyList<GridCell> _l2Cells = [];
    private IReadOnlyList<GridCell> _l3Cells = [];

    // Parent cell at each level (for Escape back-navigation)
    private GridCell _l1SelectedCell;
    private GridCell _l2SelectedCell;
    private GridCell _l3SelectedCell;

    // Arrow navigation
    private int _arrowIndex;
    private IReadOnlyList<GridCell> _currentLevelCells = [];

    // Events — ColumnHighlighted carries half, col, cells, and level
    public event Action<ScreenHalf, int, IReadOnlyList<GridCell>, int>? ColumnHighlighted;
    public event Action<GridCell>? CellHighlighted;
    public event Action<GridCell, IReadOnlyList<GridCell>, int>? CellEntered;
    public event Action<Point, MouseAction>? ActionRequested;
    public event Action<Point>? Cancelled;
    public event Action<GridCell, IReadOnlyList<GridCell>, int>? LevelExited;
    public event Action<int>? ColumnUnhighlighted;
    public event Action? InvalidKeyPressed;

    public NavigatorStateMachine(
        HalfKeySetsConfig leftKeys,
        HalfKeySetsConfig rightKeys,
        ActionMapper actionMapper,
        NavigationMode navigationMode,
        int level3Threshold) {
        _leftKeys = leftKeys;
        _rightKeys = rightKeys;
        _actionMapper = actionMapper;
        _navigationMode = navigationMode;
        _level3Threshold = level3Threshold;
    }

    /// <summary>
    /// Activates the navigator with separate left/right L1 cell lists and current cursor position.
    /// The active half is determined by the first key pressed.
    /// </summary>
    public void Activate(IReadOnlyList<GridCell> leftL1Cells, IReadOnlyList<GridCell> rightL1Cells, Point cursorOrigin) {
        if (leftL1Cells.Count == 0 || rightL1Cells.Count == 0) {
            return;
        }

        _leftL1Cells = leftL1Cells;
        _rightL1Cells = rightL1Cells;
        _l1Cells = leftL1Cells; // default for arrow nav before half is selected
        _originPoint = cursorOrigin;
        _arrowIndex = 0;
        _currentLevelCells = leftL1Cells;
        // Default to left half for arrow navigation at L1 (half is selected on first key press)
        _activeHalf = ScreenHalf.Left;
        _activeFirstKeys = _leftKeys.FirstKeys;
        _activeSecondKeys = _leftKeys.SecondKeys;
        State = NavigatorState.L1_AwaitFirst;
    }

    /// <summary>
    /// Processes a key input.
    /// </summary>
    public void OnKey(VKey vkey) {
        if (State == NavigatorState.Idle) {
            return;
        }

        // Escape handling
        if (vkey == VKey.Escape) {
            HandleEscape();
            return;
        }

        // Backspace at AwaitSecond → undo first key, back to AwaitFirst
        if (vkey == VKey.Back) {
            HandleBackspace();
            return;
        }

        // Arrow key handling
        if (IsArrowKey(vkey) && _navigationMode != NavigationMode.TwoKey) {
            HandleArrow(vkey);
            return;
        }

        // Enter = fire action at current arrow-selected cell
        if (vkey == VKey.Return && _navigationMode != NavigationMode.TwoKey) {
            HandleEnter();
            return;
        }

        // Two-key grid handling
        if (_navigationMode != NavigationMode.Arrow) {
            HandleTwoKey(vkey);
        }
    }

    /// <summary>
    /// Resets to Idle without raising events.
    /// </summary>
    public void Reset() {
        State = NavigatorState.Idle;
        _leftL1Cells = [];
        _rightL1Cells = [];
        _l1Cells = [];
        _l2Cells = [];
        _l3Cells = [];
        _currentLevelCells = [];
    }

    private void HandleEscape() {
        switch (State) {
            case NavigatorState.L3_AwaitFirst:
            case NavigatorState.L3_AwaitSecond:
            case NavigatorState.L3_AwaitAction:
                // Back to L2
                State = NavigatorState.L2_AwaitAction;
                _currentLevelCells = _l2Cells;
                _arrowIndex = 0;
                LevelExited?.Invoke(_l2SelectedCell, _l2Cells, 2);
                break;

            case NavigatorState.L2_AwaitFirst:
            case NavigatorState.L2_AwaitSecond:
            case NavigatorState.L2_AwaitAction:
                // Back to L1 — reset to AwaitFirst with both halves visible
                ResetToL1AwaitFirst();
                break;

            case NavigatorState.L1_AwaitAction:
            case NavigatorState.L1_AwaitSecond:
                // Reset both key presses — back to AwaitFirst
                ResetToL1AwaitFirst();
                break;

            default:
                // L1_AwaitFirst — cancel entirely
                State = NavigatorState.Idle;
                Cancelled?.Invoke(_originPoint);
                break;
        }
    }

    private void ResetToL1AwaitFirst() {
        _activeHalf = ScreenHalf.Left;
        _activeFirstKeys = _leftKeys.FirstKeys;
        _activeSecondKeys = _leftKeys.SecondKeys;
        _l1Cells = _leftL1Cells;
        _currentLevelCells = _leftL1Cells;
        _arrowIndex = 0;
        State = NavigatorState.L1_AwaitFirst;
        ColumnUnhighlighted?.Invoke(1);
    }

    private void HandleArrow(VKey vkey) {
        if (_currentLevelCells.Count == 0 || _activeFirstKeys.Length == 0) {
            return;
        }

        int cols = _activeFirstKeys.Length;
        int total = _currentLevelCells.Count;

        _arrowIndex = vkey switch {
            VKey.Left => ArrowNavigator.MoveLeft(_arrowIndex, cols, total),
            VKey.Right => ArrowNavigator.MoveRight(_arrowIndex, cols, total),
            VKey.Up => ArrowNavigator.MoveUp(_arrowIndex, cols, total),
            VKey.Down => ArrowNavigator.MoveDown(_arrowIndex, cols, total),
            _ => _arrowIndex,
        };

        if (_arrowIndex >= 0 && _arrowIndex < _currentLevelCells.Count) {
            CellHighlighted?.Invoke(_currentLevelCells[_arrowIndex]);
        }
    }

    private void HandleEnter() {
        if (_currentLevelCells.Count == 0 || _arrowIndex < 0 || _arrowIndex >= _currentLevelCells.Count) {
            return;
        }

        var cell = _currentLevelCells[_arrowIndex];
        var center = GridCalculator.CenterOf(cell);
        State = NavigatorState.Idle;
        ActionRequested?.Invoke(center, MouseAction.LeftClick);
    }

    private void HandleTwoKey(VKey vkey) {
        switch (State) {
            case NavigatorState.L1_AwaitFirst:
                HandleL1FirstKey(vkey);
                break;
            case NavigatorState.L1_AwaitSecond:
                HandleSecondKey(vkey, NavigatorState.L1_AwaitSecond, NavigatorState.L1_AwaitAction, _l1Cells, 1, ref _l1SelectedCell);
                break;
            case NavigatorState.L1_AwaitAction:
                HandleNavFirstKey(vkey, _l1SelectedCell, 2);
                break;
            case NavigatorState.L2_AwaitFirst:
                HandleFirstKey(vkey, NavigatorState.L2_AwaitSecond, _l2Cells);
                break;
            case NavigatorState.L2_AwaitSecond:
                HandleSecondKey(vkey, NavigatorState.L2_AwaitSecond, NavigatorState.L2_AwaitAction, _l2Cells, 2, ref _l2SelectedCell);
                break;
            case NavigatorState.L2_AwaitAction:
                if (_l3Cells.Count == 0 && TryReselectCell(vkey, _l2Cells, 2, ref _l2SelectedCell)) {
                    break;
                }
                HandleNavFirstKey(vkey, _l2SelectedCell, 3);
                break;
            case NavigatorState.L3_AwaitFirst:
                HandleFirstKey(vkey, NavigatorState.L3_AwaitSecond, _l3Cells);
                break;
            case NavigatorState.L3_AwaitSecond:
                HandleSecondKey(vkey, NavigatorState.L3_AwaitSecond, NavigatorState.L3_AwaitAction, _l3Cells, 3, ref _l3SelectedCell);
                break;
            case NavigatorState.L3_AwaitAction:
                if (TryReselectCell(vkey, _l3Cells, 3, ref _l3SelectedCell)) {
                    break;
                }
                HandleActionFinal(vkey);
                break;
        }
    }

    /// <summary>
    /// Handles first key at L1 — determines which screen half based on the key.
    /// </summary>
    private void HandleL1FirstKey(VKey vkey) {
        int col = Array.IndexOf(_leftKeys.FirstKeys, vkey);
        if (col >= 0) {
            _activeHalf = ScreenHalf.Left;
            _activeFirstKeys = _leftKeys.FirstKeys;
            _activeSecondKeys = _leftKeys.SecondKeys;
            _l1Cells = _leftL1Cells;
            _currentLevelCells = _leftL1Cells;
            _selectedCol = col;
            State = NavigatorState.L1_AwaitSecond;
            ColumnHighlighted?.Invoke(_activeHalf, col, _leftL1Cells, 1);
            return;
        }

        col = Array.IndexOf(_rightKeys.FirstKeys, vkey);
        if (col >= 0) {
            _activeHalf = ScreenHalf.Right;
            _activeFirstKeys = _rightKeys.FirstKeys;
            _activeSecondKeys = _rightKeys.SecondKeys;
            _l1Cells = _rightL1Cells;
            _currentLevelCells = _rightL1Cells;
            _selectedCol = col;
            State = NavigatorState.L1_AwaitSecond;
            ColumnHighlighted?.Invoke(_activeHalf, col, _rightL1Cells, 1);
            return;
        }

        InvalidKeyPressed?.Invoke();
    }

    private void HandleFirstKey(VKey vkey, NavigatorState nextState, IReadOnlyList<GridCell> cells) {
        int col = Array.IndexOf(_activeFirstKeys, vkey);
        if (col < 0) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        _selectedCol = col;
        State = nextState;
        int level = nextState == NavigatorState.L2_AwaitSecond ? 2 : 3;
        ColumnHighlighted?.Invoke(_activeHalf, col, cells, level);
    }

    private void HandleSecondKey(VKey vkey, NavigatorState currentState, NavigatorState nextState, IReadOnlyList<GridCell> cells, int level, ref GridCell selectedCell) {
        int row = Array.IndexOf(_activeSecondKeys, vkey);
        if (row < 0) {
            // Re-entry: if it's a valid first key for the active half, restart column selection
            int col = Array.IndexOf(_activeFirstKeys, vkey);
            if (col >= 0) {
                _selectedCol = col;
                ColumnHighlighted?.Invoke(_activeHalf, col, cells, level);
                return;
            }

            // At L1, also allow switching half
            if (level == 1) {
                col = Array.IndexOf(_leftKeys.FirstKeys, vkey);
                if (col >= 0) {
                    _activeHalf = ScreenHalf.Left;
                    _activeFirstKeys = _leftKeys.FirstKeys;
                    _activeSecondKeys = _leftKeys.SecondKeys;
                    _l1Cells = _leftL1Cells;
                    _currentLevelCells = _leftL1Cells;
                    _selectedCol = col;
                    ColumnHighlighted?.Invoke(_activeHalf, col, _leftL1Cells, 1);
                    return;
                }
                col = Array.IndexOf(_rightKeys.FirstKeys, vkey);
                if (col >= 0) {
                    _activeHalf = ScreenHalf.Right;
                    _activeFirstKeys = _rightKeys.FirstKeys;
                    _activeSecondKeys = _rightKeys.SecondKeys;
                    _l1Cells = _rightL1Cells;
                    _currentLevelCells = _rightL1Cells;
                    _selectedCol = col;
                    ColumnHighlighted?.Invoke(_activeHalf, col, _rightL1Cells, 1);
                    return;
                }
            }

            InvalidKeyPressed?.Invoke();
            return;
        }

        int index = row * _activeFirstKeys.Length + _selectedCol;
        if (index >= cells.Count) {
            return;
        }

        selectedCell = cells[index];
        _arrowIndex = index;
        State = nextState;

        // Compute subgrid for next level
        IReadOnlyList<GridCell> subgridCells = [];
        if (level == 1) {
            subgridCells = SubgridCalculator.Calculate(selectedCell, _activeFirstKeys.Length, _activeSecondKeys.Length);
            _l2Cells = subgridCells;
            _currentLevelCells = subgridCells;
            _arrowIndex = 0;
        } else if (level == 2) {
            if (SubgridCalculator.ShouldActivateLevel3(selectedCell, _level3Threshold)) {
                subgridCells = SubgridCalculator.Calculate(selectedCell, _activeFirstKeys.Length, _activeSecondKeys.Length);
                _l3Cells = subgridCells;
                _currentLevelCells = subgridCells;
                _arrowIndex = 0;
            }
        }

        CellEntered?.Invoke(selectedCell, subgridCells, level);
    }

    /// <summary>
    /// At the deepest level's AwaitAction, a valid second key re-selects
    /// a different cell in the same column (using _selectedCol from the first key).
    /// </summary>
    private bool TryReselectCell(VKey vkey, IReadOnlyList<GridCell> cells, int level, ref GridCell selectedCell) {
        int row = Array.IndexOf(_activeSecondKeys, vkey);
        if (row < 0) {
            return false;
        }

        int index = row * _activeFirstKeys.Length + _selectedCol;
        if (index >= cells.Count) {
            return false;
        }

        selectedCell = cells[index];
        _arrowIndex = index;
        CellEntered?.Invoke(selectedCell, [], level);
        return true;
    }

    private void HandleNavFirstKey(VKey vkey, GridCell parentCell, int nextLevel) {
        var action = _actionMapper.Map(vkey);
        if (action.HasValue) {
            var center = GridCalculator.CenterOf(parentCell);
            State = NavigatorState.Idle;
            ActionRequested?.Invoke(center, action.Value);
            return;
        }

        var cells = nextLevel == 2 ? _l2Cells : _l3Cells;
        if (cells.Count == 0) {
            return; // L3 not available (threshold not met)
        }

        int col = Array.IndexOf(_activeFirstKeys, vkey);
        if (col < 0) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        _currentLevelCells = cells;
        _arrowIndex = 0;
        _selectedCol = col;

        State = nextLevel == 2 ? NavigatorState.L2_AwaitSecond : NavigatorState.L3_AwaitSecond;
        ColumnHighlighted?.Invoke(_activeHalf, col, cells, nextLevel);
    }

    private void HandleActionFinal(VKey vkey) {
        var action = _actionMapper.Map(vkey);
        if (!action.HasValue) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        // Find the last selected cell at L3
        // Use the L3 cells' current arrow index or last entered cell
        var cells = _l3Cells.Count > 0 ? _l3Cells : _l2Cells;
        if (_arrowIndex >= 0 && _arrowIndex < cells.Count) {
            var center = GridCalculator.CenterOf(cells[_arrowIndex]);
            State = NavigatorState.Idle;
            ActionRequested?.Invoke(center, action.Value);
        }
    }

    private static bool IsArrowKey(VKey vkey) =>
        vkey is VKey.Left or VKey.Right or VKey.Up or VKey.Down;

    private void HandleBackspace() {
        switch (State) {
            case NavigatorState.L1_AwaitSecond:
                // Reset to left-half defaults (matching Activate initial state)
                _activeHalf = ScreenHalf.Left;
                _activeFirstKeys = _leftKeys.FirstKeys;
                _activeSecondKeys = _leftKeys.SecondKeys;
                _l1Cells = _leftL1Cells;
                _currentLevelCells = _leftL1Cells;
                _arrowIndex = 0;
                State = NavigatorState.L1_AwaitFirst;
                ColumnUnhighlighted?.Invoke(1);
                break;
            case NavigatorState.L2_AwaitSecond:
                _arrowIndex = 0;
                State = NavigatorState.L2_AwaitFirst;
                ColumnUnhighlighted?.Invoke(2);
                break;
            case NavigatorState.L3_AwaitSecond:
                _arrowIndex = 0;
                State = NavigatorState.L3_AwaitFirst;
                ColumnUnhighlighted?.Invoke(3);
                break;
            default:
                // Backspace at other states does nothing
                break;
        }
    }
}
