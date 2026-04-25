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

/// <summary>
/// Core navigation state machine. Processes VKey inputs and raises events
/// for overlay visual updates, cursor movement, and action dispatch.
/// Unified grid: single set of first/second keys for the full screen.
/// </summary>
public sealed class NavigatorStateMachine {
    private readonly VKey[] _firstKeys;
    private readonly VKey[] _secondKeys;
    private readonly ActionMapper _actionMapper;
    private readonly NavigationMode _navigationMode;
    private readonly int _level3Threshold;

    // State
    public NavigatorState State { get; private set; } = NavigatorState.Idle;
    private Point _originPoint;
    private int _selectedCol;

    // Grid cells per level
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

    // Events
    public event Action<int, IReadOnlyList<GridCell>, int>? ColumnHighlighted;
    public event Action<GridCell>? CellHighlighted;
    public event Action<GridCell, IReadOnlyList<GridCell>, int>? CellEntered;
    public event Action<Point, MouseAction>? ActionRequested;
    public event Action<Point>? Cancelled;
    public event Action<GridCell, IReadOnlyList<GridCell>, int>? LevelExited;
    public event Action<int>? ColumnUnhighlighted;
    public event Action? InvalidKeyPressed;

    public NavigatorStateMachine(
        VKey[] firstKeys,
        VKey[] secondKeys,
        ActionMapper actionMapper,
        NavigationMode navigationMode,
        int level3Threshold) {
        _firstKeys = firstKeys;
        _secondKeys = secondKeys;
        _actionMapper = actionMapper;
        _navigationMode = navigationMode;
        _level3Threshold = level3Threshold;
    }

    /// <summary>
    /// Activates the navigator with full-screen L1 cells and current cursor position.
    /// </summary>
    public void Activate(IReadOnlyList<GridCell> l1Cells, Point cursorOrigin) {
        if (l1Cells.Count == 0) {
            return;
        }

        _l1Cells = l1Cells;
        _originPoint = cursorOrigin;
        _arrowIndex = 0;
        _currentLevelCells = l1Cells;
        State = NavigatorState.L1_AwaitFirst;
    }

    /// <summary>
    /// Processes a key input.
    /// </summary>
    public void OnKey(VKey vkey) {
        if (State == NavigatorState.Idle) {
            return;
        }

        if (vkey == VKey.Escape) {
            HandleEscape();
            return;
        }

        if (IsArrowKey(vkey) && _navigationMode != NavigationMode.TwoKey) {
            HandleArrow(vkey);
            return;
        }

        if (vkey == VKey.Return && _navigationMode != NavigationMode.TwoKey) {
            HandleEnter();
            return;
        }

        // In Both mode, action keys (Space, etc.) fire immediately on the
        // arrow-selected cell — even from AwaitFirst/AwaitSecond states.
        if (_navigationMode == NavigationMode.Both && TryHandleActionKey(vkey)) {
            return;
        }

        if (_navigationMode != NavigationMode.Arrow) {
            HandleTwoKey(vkey);
        }
    }

    /// <summary>
    /// Resets to Idle without raising events.
    /// </summary>
    public void Reset() {
        State = NavigatorState.Idle;
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
                var l3Parent = _l2SelectedCell;
                _l3Cells = [];
                _currentLevelCells = _l2Cells;
                _arrowIndex = 0;
                State = NavigatorState.L2_AwaitFirst;
                LevelExited?.Invoke(l3Parent, _l2Cells, 3);
                ColumnUnhighlighted?.Invoke(2);
                break;

            case NavigatorState.L2_AwaitFirst:
            case NavigatorState.L2_AwaitSecond:
            case NavigatorState.L2_AwaitAction:
                ResetToL1AwaitFirst();
                break;

            case NavigatorState.L1_AwaitAction:
            case NavigatorState.L1_AwaitSecond:
                ResetToL1AwaitFirst();
                break;

            default:
                State = NavigatorState.Idle;
                Cancelled?.Invoke(_originPoint);
                break;
        }
    }

    private void ResetToL1AwaitFirst() {
        _currentLevelCells = _l1Cells;
        _arrowIndex = 0;
        State = NavigatorState.L1_AwaitFirst;
        ColumnUnhighlighted?.Invoke(1);
    }

    private void HandleArrow(VKey vkey) {
        if (_currentLevelCells.Count == 0 || _firstKeys.Length == 0) {
            return;
        }

        int cols = _firstKeys.Length;
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

        // Determine current level from state
        int level;
        switch (State) {
            case NavigatorState.L1_AwaitFirst:
            case NavigatorState.L1_AwaitSecond:
            case NavigatorState.L1_AwaitAction:
                level = 1;
                _l1SelectedCell = cell;
                _selectedCol = cell.Col;
                break;
            case NavigatorState.L2_AwaitFirst:
            case NavigatorState.L2_AwaitSecond:
            case NavigatorState.L2_AwaitAction:
                level = 2;
                _l2SelectedCell = cell;
                _selectedCol = cell.Col;
                break;
            case NavigatorState.L3_AwaitFirst:
            case NavigatorState.L3_AwaitSecond:
            case NavigatorState.L3_AwaitAction:
                // L3 is the deepest level — Enter does nothing during arrow nav
                return;
            default:
                return;
        }

        // Compute subgrid and enter cell (same as two-key cell entry)
        IReadOnlyList<GridCell> subgridCells = [];
        if (level == 1) {
            subgridCells = SubgridCalculator.Calculate(cell, _firstKeys.Length, _secondKeys.Length);
            _l2Cells = subgridCells;
            _currentLevelCells = subgridCells;
            _arrowIndex = 0;
            State = NavigatorState.L2_AwaitFirst;
        } else if (level == 2) {
            if (SubgridCalculator.ShouldActivateLevel3(cell, _level3Threshold)) {
                subgridCells = SubgridCalculator.Calculate(cell, _firstKeys.Length, _secondKeys.Length);
                _l3Cells = subgridCells;
                _currentLevelCells = subgridCells;
                _arrowIndex = 0;
                State = NavigatorState.L3_AwaitFirst;
            } else {
                State = NavigatorState.L2_AwaitAction;
            }
        }

        CellEntered?.Invoke(cell, subgridCells, level);
    }

    private void HandleTwoKey(VKey vkey) {
        switch (State) {
            case NavigatorState.L1_AwaitFirst:
                HandleFirstKey(vkey, NavigatorState.L1_AwaitSecond, _l1Cells, 1);
                break;
            case NavigatorState.L1_AwaitSecond:
                HandleSecondKey(vkey, NavigatorState.L1_AwaitSecond, NavigatorState.L1_AwaitAction, _l1Cells, 1, ref _l1SelectedCell);
                break;
            case NavigatorState.L1_AwaitAction:
                HandleNavFirstKey(vkey, _l1SelectedCell, 2);
                break;
            case NavigatorState.L2_AwaitFirst:
                HandleFirstKey(vkey, NavigatorState.L2_AwaitSecond, _l2Cells, 2);
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
                HandleFirstKey(vkey, NavigatorState.L3_AwaitSecond, _l3Cells, 3);
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

    private void HandleFirstKey(VKey vkey, NavigatorState nextState, IReadOnlyList<GridCell> cells, int level) {
        int col = Array.IndexOf(_firstKeys, vkey);
        if (col < 0) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        _selectedCol = col;
        State = nextState;
        ColumnHighlighted?.Invoke(col, cells, level);
    }

    private void HandleSecondKey(VKey vkey, NavigatorState currentState, NavigatorState nextState, IReadOnlyList<GridCell> cells, int level, ref GridCell selectedCell) {
        int row = Array.IndexOf(_secondKeys, vkey);
        if (row < 0) {
            // Re-entry: if it's a valid first key, restart column selection
            int col = Array.IndexOf(_firstKeys, vkey);
            if (col >= 0) {
                _selectedCol = col;
                ColumnHighlighted?.Invoke(col, cells, level);
                return;
            }

            InvalidKeyPressed?.Invoke();
            return;
        }

        int index = row * _firstKeys.Length + _selectedCol;
        if (index >= cells.Count) {
            return;
        }

        selectedCell = cells[index];
        _arrowIndex = index;

        State = nextState;

        // Compute subgrid for next level
        IReadOnlyList<GridCell> subgridCells = [];
        if (level == 1) {
            subgridCells = SubgridCalculator.Calculate(selectedCell, _firstKeys.Length, _secondKeys.Length);
            _l2Cells = subgridCells;
            _currentLevelCells = subgridCells;
            _arrowIndex = 0;
        } else if (level == 2) {
            if (SubgridCalculator.ShouldActivateLevel3(selectedCell, _level3Threshold)) {
                subgridCells = SubgridCalculator.Calculate(selectedCell, _firstKeys.Length, _secondKeys.Length);
                _l3Cells = subgridCells;
                _currentLevelCells = subgridCells;
                _arrowIndex = 0;
            }
        }

        CellEntered?.Invoke(selectedCell, subgridCells, level);
    }

    private bool TryReselectCell(VKey vkey, IReadOnlyList<GridCell> cells, int level, ref GridCell selectedCell) {
        int row = Array.IndexOf(_secondKeys, vkey);
        if (row < 0) {
            return false;
        }

        int index = row * _firstKeys.Length + _selectedCol;
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
            return;
        }

        int col = Array.IndexOf(_firstKeys, vkey);
        if (col < 0) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        _currentLevelCells = cells;
        _arrowIndex = 0;
        _selectedCol = col;

        State = nextLevel == 2 ? NavigatorState.L2_AwaitSecond : NavigatorState.L3_AwaitSecond;
        ColumnHighlighted?.Invoke(col, cells, nextLevel);
    }

    private void HandleActionFinal(VKey vkey) {
        var action = _actionMapper.Map(vkey);
        if (!action.HasValue) {
            InvalidKeyPressed?.Invoke();
            return;
        }

        var cells = _l3Cells.Count > 0 ? _l3Cells : _l2Cells;
        if (_arrowIndex >= 0 && _arrowIndex < cells.Count) {
            var center = GridCalculator.CenterOf(cells[_arrowIndex]);
            State = NavigatorState.Idle;
            ActionRequested?.Invoke(center, action.Value);
        }
    }

    /// <summary>
    /// Checks if the key is an action key and fires the action on the current
    /// arrow-selected cell. Returns true if handled.
    /// </summary>
    private bool TryHandleActionKey(VKey vkey) {
        var action = _actionMapper.Map(vkey);
        if (!action.HasValue) {
            return false;
        }

        if (_arrowIndex < 0 || _arrowIndex >= _currentLevelCells.Count) {
            return false;
        }

        var center = GridCalculator.CenterOf(_currentLevelCells[_arrowIndex]);
        State = NavigatorState.Idle;
        ActionRequested?.Invoke(center, action.Value);
        return true;
    }

    private static bool IsArrowKey(VKey vkey) =>
        vkey is VKey.Left or VKey.Right or VKey.Up or VKey.Down;
}
