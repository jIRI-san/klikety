using System.Drawing;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;

namespace Klikety.Navigation;

public enum NavigatorState
{
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
/// </summary>
public sealed class NavigatorStateMachine
{
    private readonly VKey[] _firstKeys;
    private readonly VKey[] _secondKeys;
    private readonly ActionMapper _actionMapper;
    private readonly NavigationMode _navigationMode;
    private readonly int _level3Threshold;

    // State
    public NavigatorState State { get; private set; } = NavigatorState.Idle;
    private Point _originPoint;
    private int _selectedCol; // first key → column index

    // Grid cells per level
    private IReadOnlyList<GridCell> _l1Cells = [];
    private IReadOnlyList<GridCell> _l2Cells = [];
    private IReadOnlyList<GridCell> _l3Cells = [];

    // Parent cell at each level (for Escape back-navigation)
    private GridCell _l1SelectedCell;
    private GridCell _l2SelectedCell;

    // Arrow navigation
    private int _arrowIndex;
    private IReadOnlyList<GridCell> _currentLevelCells = [];

    // Events
    public event Action<int>? ColumnHighlighted;
    public event Action<GridCell>? CellHighlighted;
    public event Action<GridCell, int>? CellEntered;
    public event Action<Point, MouseAction>? ActionRequested;
    public event Action<Point>? Cancelled;

    public NavigatorStateMachine(
        VKey[] firstKeys,
        VKey[] secondKeys,
        ActionMapper actionMapper,
        NavigationMode navigationMode,
        int level3Threshold)
    {
        _firstKeys = firstKeys;
        _secondKeys = secondKeys;
        _actionMapper = actionMapper;
        _navigationMode = navigationMode;
        _level3Threshold = level3Threshold;
    }

    /// <summary>
    /// Activates the navigator with the given screen grid cells and current cursor position.
    /// </summary>
    public void Activate(IReadOnlyList<GridCell> l1Cells, Point cursorOrigin)
    {
        _l1Cells = l1Cells;
        _originPoint = cursorOrigin;
        _arrowIndex = 0;
        _currentLevelCells = l1Cells;
        State = NavigatorState.L1_AwaitFirst;
    }

    /// <summary>
    /// Processes a key input.
    /// </summary>
    public void OnKey(VKey vkey)
    {
        if (State == NavigatorState.Idle) return;

        // Escape handling
        if (vkey == VKey.Escape)
        {
            HandleEscape();
            return;
        }

        // Arrow key handling
        if (IsArrowKey(vkey) && _navigationMode != NavigationMode.TwoKey)
        {
            HandleArrow(vkey);
            return;
        }

        // Enter = fire action at current arrow-selected cell
        if (vkey == VKey.Return && _navigationMode != NavigationMode.TwoKey)
        {
            HandleEnter();
            return;
        }

        // Two-key grid handling
        if (_navigationMode != NavigationMode.Arrow)
        {
            HandleTwoKey(vkey);
        }
    }

    /// <summary>
    /// Resets to Idle without raising events.
    /// </summary>
    public void Reset()
    {
        State = NavigatorState.Idle;
        _l1Cells = [];
        _l2Cells = [];
        _l3Cells = [];
        _currentLevelCells = [];
    }

    private void HandleEscape()
    {
        switch (State)
        {
            case NavigatorState.L3_AwaitFirst:
            case NavigatorState.L3_AwaitSecond:
            case NavigatorState.L3_AwaitAction:
                // Back to L2
                State = NavigatorState.L2_AwaitAction;
                _currentLevelCells = _l2Cells;
                _arrowIndex = 0;
                CellEntered?.Invoke(_l2SelectedCell, 2);
                break;

            case NavigatorState.L2_AwaitFirst:
            case NavigatorState.L2_AwaitSecond:
            case NavigatorState.L2_AwaitAction:
                // Back to L1
                State = NavigatorState.L1_AwaitAction;
                _currentLevelCells = _l1Cells;
                _arrowIndex = 0;
                CellEntered?.Invoke(_l1SelectedCell, 1);
                break;

            default:
                // L1 — cancel entirely
                State = NavigatorState.Idle;
                Cancelled?.Invoke(_originPoint);
                break;
        }
    }

    private void HandleArrow(VKey vkey)
    {
        if (_currentLevelCells.Count == 0) return;

        int cols = _firstKeys.Length;
        int total = _currentLevelCells.Count;

        _arrowIndex = vkey switch
        {
            VKey.Left => ArrowNavigator.MoveLeft(_arrowIndex, cols, total),
            VKey.Right => ArrowNavigator.MoveRight(_arrowIndex, cols, total),
            VKey.Up => ArrowNavigator.MoveUp(_arrowIndex, cols, total),
            VKey.Down => ArrowNavigator.MoveDown(_arrowIndex, cols, total),
            _ => _arrowIndex,
        };

        if (_arrowIndex >= 0 && _arrowIndex < _currentLevelCells.Count)
        {
            CellHighlighted?.Invoke(_currentLevelCells[_arrowIndex]);
        }
    }

    private void HandleEnter()
    {
        if (_currentLevelCells.Count == 0 || _arrowIndex < 0 || _arrowIndex >= _currentLevelCells.Count)
            return;

        var cell = _currentLevelCells[_arrowIndex];
        var center = GridCalculator.CenterOf(cell);
        State = NavigatorState.Idle;
        ActionRequested?.Invoke(center, MouseAction.LeftClick);
    }

    private void HandleTwoKey(VKey vkey)
    {
        switch (State)
        {
            case NavigatorState.L1_AwaitFirst:
                HandleFirstKey(vkey, NavigatorState.L1_AwaitSecond, _l1Cells);
                break;
            case NavigatorState.L1_AwaitSecond:
                HandleSecondKey(vkey, NavigatorState.L1_AwaitAction, _l1Cells, 1, ref _l1SelectedCell);
                break;
            case NavigatorState.L1_AwaitAction:
                HandleActionOrNav(vkey, NavigatorState.L2_AwaitFirst, _l1SelectedCell, 2);
                break;
            case NavigatorState.L2_AwaitFirst:
                HandleFirstKey(vkey, NavigatorState.L2_AwaitSecond, _l2Cells);
                break;
            case NavigatorState.L2_AwaitSecond:
                HandleSecondKey(vkey, NavigatorState.L2_AwaitAction, _l2Cells, 2, ref _l2SelectedCell);
                break;
            case NavigatorState.L2_AwaitAction:
                HandleActionOrNav(vkey, NavigatorState.L3_AwaitFirst, _l2SelectedCell, 3);
                break;
            case NavigatorState.L3_AwaitFirst:
                HandleFirstKey(vkey, NavigatorState.L3_AwaitSecond, _l3Cells);
                break;
            case NavigatorState.L3_AwaitSecond:
                HandleSecondKey(vkey, NavigatorState.L3_AwaitAction, _l3Cells, 3, ref _l2SelectedCell /* L3 has no child */);
                break;
            case NavigatorState.L3_AwaitAction:
                HandleActionFinal(vkey);
                break;
        }
    }

    private void HandleFirstKey(VKey vkey, NavigatorState nextState, IReadOnlyList<GridCell> cells)
    {
        int col = Array.IndexOf(_firstKeys, vkey);
        if (col < 0) return; // not a first-key

        _selectedCol = col;
        State = nextState;
        ColumnHighlighted?.Invoke(col);
    }

    private void HandleSecondKey(VKey vkey, NavigatorState nextState, IReadOnlyList<GridCell> cells, int level, ref GridCell selectedCell)
    {
        int row = Array.IndexOf(_secondKeys, vkey);
        if (row < 0) return; // not a second-key

        int index = row * _firstKeys.Length + _selectedCol;
        if (index >= cells.Count) return;

        selectedCell = cells[index];
        var center = GridCalculator.CenterOf(selectedCell);
        State = nextState;
        CellEntered?.Invoke(selectedCell, level);
    }

    private void HandleActionOrNav(VKey vkey, NavigatorState nextNavState, GridCell parentCell, int nextLevel)
    {
        // Check if it's an action key
        var action = _actionMapper.Map(vkey);
        if (action.HasValue)
        {
            var center = GridCalculator.CenterOf(parentCell);
            State = NavigatorState.Idle;
            ActionRequested?.Invoke(center, action.Value);
            return;
        }

        // Check if it's a first key → start next level
        int col = Array.IndexOf(_firstKeys, vkey);
        if (col < 0) return;

        // Calculate subgrid
        var subCells = SubgridCalculator.Calculate(parentCell, _firstKeys.Length, _secondKeys.Length);

        if (nextLevel == 2)
        {
            _l2Cells = subCells;
        }
        else if (nextLevel == 3)
        {
            // Check L3 threshold
            if (!SubgridCalculator.ShouldActivateLevel3(parentCell, _level3Threshold))
                return;
            _l3Cells = subCells;
        }

        _currentLevelCells = subCells;
        _arrowIndex = 0;
        _selectedCol = col;
        State = nextNavState;

        // Transition past AwaitFirst since we already have the first key
        State = nextLevel == 2 ? NavigatorState.L2_AwaitSecond : NavigatorState.L3_AwaitSecond;
        ColumnHighlighted?.Invoke(col);
    }

    private void HandleActionFinal(VKey vkey)
    {
        var action = _actionMapper.Map(vkey);
        if (!action.HasValue) return;

        // Find the last selected cell at L3
        // Use the L3 cells' current arrow index or last entered cell
        var cells = _l3Cells.Count > 0 ? _l3Cells : _l2Cells;
        if (_arrowIndex >= 0 && _arrowIndex < cells.Count)
        {
            var center = GridCalculator.CenterOf(cells[_arrowIndex]);
            State = NavigatorState.Idle;
            ActionRequested?.Invoke(center, action.Value);
        }
    }

    private static bool IsArrowKey(VKey vkey) =>
        vkey is VKey.Left or VKey.Right or VKey.Up or VKey.Down;
}
