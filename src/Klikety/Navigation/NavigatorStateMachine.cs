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

public enum ScreenHalf { Left, Right }

/// <summary>
/// Core navigation state machine. Processes VKey inputs and raises events
/// for overlay visual updates, cursor movement, and action dispatch.
/// Supports split-screen: left-hand keys control the left half, right-hand keys the right.
/// </summary>
public sealed class NavigatorStateMachine
{
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
    private IReadOnlyList<GridCell> _l1Cells = [];
    private IReadOnlyList<GridCell> _l2Cells = [];
    private IReadOnlyList<GridCell> _l3Cells = [];

    // Parent cell at each level (for Escape back-navigation)
    private GridCell _l1SelectedCell;
    private GridCell _l2SelectedCell;

    // Arrow navigation
    private int _arrowIndex;
    private IReadOnlyList<GridCell> _currentLevelCells = [];

    // Events — HalfSelected carries which half was chosen (raised once at L1)
    public event Action<ScreenHalf, int>? ColumnHighlighted;
    public event Action<GridCell>? CellHighlighted;
    public event Action<GridCell, int>? CellEntered;
    public event Action<Point, MouseAction>? ActionRequested;
    public event Action<Point>? Cancelled;
    public event Action? InvalidKeyPressed;

    public NavigatorStateMachine(
        HalfKeySetsConfig leftKeys,
        HalfKeySetsConfig rightKeys,
        ActionMapper actionMapper,
        NavigationMode navigationMode,
        int level3Threshold)
    {
        _leftKeys = leftKeys;
        _rightKeys = rightKeys;
        _actionMapper = actionMapper;
        _navigationMode = navigationMode;
        _level3Threshold = level3Threshold;
    }

    /// <summary>
    /// Activates the navigator with the given L1 cell list and current cursor position.
    /// The active half is determined by the first key pressed.
    /// </summary>
    public void Activate(IReadOnlyList<GridCell> l1Cells, Point cursorOrigin)
    {
        _l1Cells = l1Cells;
        _originPoint = cursorOrigin;
        _arrowIndex = 0;
        _currentLevelCells = l1Cells;
        // Default to left half for arrow navigation at L1 (half is selected on first key press)
        _activeHalf = ScreenHalf.Left;
        _activeFirstKeys = _leftKeys.FirstKeys;
        _activeSecondKeys = _leftKeys.SecondKeys;
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

    // Backspace at AwaitSecond → undo first key, back to AwaitFirst
    if (vkey == VKey.Back)
    {
      HandleBackspace();
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
        if (_currentLevelCells.Count == 0 || _activeFirstKeys.Length == 0) return;

        int cols = _activeFirstKeys.Length;
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
                HandleL1FirstKey(vkey);
                break;
            case NavigatorState.L1_AwaitSecond:
                HandleSecondKey(vkey, NavigatorState.L1_AwaitSecond, NavigatorState.L1_AwaitAction, _l1Cells, 1, ref _l1SelectedCell);
                break;
            case NavigatorState.L1_AwaitAction:
                HandleActionOrNav(vkey, NavigatorState.L2_AwaitFirst, _l1SelectedCell, 2);
                break;
            case NavigatorState.L2_AwaitFirst:
                HandleFirstKey(vkey, NavigatorState.L2_AwaitSecond, _l2Cells);
                break;
            case NavigatorState.L2_AwaitSecond:
                HandleSecondKey(vkey, NavigatorState.L2_AwaitSecond, NavigatorState.L2_AwaitAction, _l2Cells, 2, ref _l2SelectedCell);
                break;
            case NavigatorState.L2_AwaitAction:
                HandleActionOrNav(vkey, NavigatorState.L3_AwaitFirst, _l2SelectedCell, 3);
                break;
            case NavigatorState.L3_AwaitFirst:
                HandleFirstKey(vkey, NavigatorState.L3_AwaitSecond, _l3Cells);
                break;
            case NavigatorState.L3_AwaitSecond:
                HandleSecondKey(vkey, NavigatorState.L3_AwaitSecond, NavigatorState.L3_AwaitAction, _l3Cells, 3, ref _l2SelectedCell);
                break;
            case NavigatorState.L3_AwaitAction:
                HandleActionFinal(vkey);
                break;
        }
    }

    /// <summary>
    /// Handles first key at L1 — determines which screen half based on the key.
    /// </summary>
    private void HandleL1FirstKey(VKey vkey)
    {
        int col = Array.IndexOf(_leftKeys.FirstKeys, vkey);
        if (col >= 0)
        {
            _activeHalf = ScreenHalf.Left;
            _activeFirstKeys = _leftKeys.FirstKeys;
            _activeSecondKeys = _leftKeys.SecondKeys;
            _selectedCol = col;
            State = NavigatorState.L1_AwaitSecond;
            ColumnHighlighted?.Invoke(_activeHalf, col);
            return;
        }

        col = Array.IndexOf(_rightKeys.FirstKeys, vkey);
        if (col >= 0)
        {
            _activeHalf = ScreenHalf.Right;
            _activeFirstKeys = _rightKeys.FirstKeys;
            _activeSecondKeys = _rightKeys.SecondKeys;
            _selectedCol = col;
            State = NavigatorState.L1_AwaitSecond;
            ColumnHighlighted?.Invoke(_activeHalf, col);
            return;
        }

        InvalidKeyPressed?.Invoke();
    }

    private void HandleFirstKey(VKey vkey, NavigatorState nextState, IReadOnlyList<GridCell> cells)
    {
        int col = Array.IndexOf(_activeFirstKeys, vkey);
        if (col < 0)
        {
            InvalidKeyPressed?.Invoke();
            return;
        }

        _selectedCol = col;
        State = nextState;
        ColumnHighlighted?.Invoke(_activeHalf, col);
    }

    private void HandleSecondKey(VKey vkey, NavigatorState currentState, NavigatorState nextState, IReadOnlyList<GridCell> cells, int level, ref GridCell selectedCell)
    {
        int row = Array.IndexOf(_activeSecondKeys, vkey);
        if (row < 0)
        {
            // Re-entry: if it's a valid first key for the active half, restart column selection
            int col = Array.IndexOf(_activeFirstKeys, vkey);
            if (col >= 0)
            {
                _selectedCol = col;
                ColumnHighlighted?.Invoke(_activeHalf, col);
                return;
            }

            // At L1, also allow switching half
            if (level == 1)
            {
                col = Array.IndexOf(_leftKeys.FirstKeys, vkey);
                if (col >= 0)
                {
                    _activeHalf = ScreenHalf.Left;
                    _activeFirstKeys = _leftKeys.FirstKeys;
                    _activeSecondKeys = _leftKeys.SecondKeys;
                    _selectedCol = col;
                    ColumnHighlighted?.Invoke(_activeHalf, col);
                    return;
                }
                col = Array.IndexOf(_rightKeys.FirstKeys, vkey);
                if (col >= 0)
                {
                    _activeHalf = ScreenHalf.Right;
                    _activeFirstKeys = _rightKeys.FirstKeys;
                    _activeSecondKeys = _rightKeys.SecondKeys;
                    _selectedCol = col;
                    ColumnHighlighted?.Invoke(_activeHalf, col);
                    return;
                }
            }

            InvalidKeyPressed?.Invoke();
            return;
        }

        int index = row * _activeFirstKeys.Length + _selectedCol;
        if (index >= cells.Count) return;

        selectedCell = cells[index];
        State = nextState;
        CellEntered?.Invoke(selectedCell, level);
    }

    private void HandleActionOrNav(VKey vkey, NavigatorState nextNavState, GridCell parentCell, int nextLevel)
    {
        var action = _actionMapper.Map(vkey);
        if (action.HasValue)
        {
            var center = GridCalculator.CenterOf(parentCell);
            State = NavigatorState.Idle;
            ActionRequested?.Invoke(center, action.Value);
            return;
        }

        int col = Array.IndexOf(_activeFirstKeys, vkey);
        if (col < 0)
        {
            InvalidKeyPressed?.Invoke();
            return;
        }

        var subCells = SubgridCalculator.Calculate(parentCell, _activeFirstKeys.Length, _activeSecondKeys.Length);

        if (nextLevel == 2)
        {
            _l2Cells = subCells;
        }
        else if (nextLevel == 3)
        {
            if (!SubgridCalculator.ShouldActivateLevel3(parentCell, _level3Threshold))
                return;
            _l3Cells = subCells;
        }

        _currentLevelCells = subCells;
        _arrowIndex = 0;
        _selectedCol = col;
        State = nextNavState;

        State = nextLevel == 2 ? NavigatorState.L2_AwaitSecond : NavigatorState.L3_AwaitSecond;
        ColumnHighlighted?.Invoke(_activeHalf, col);
    }

    private void HandleActionFinal(VKey vkey)
    {
        var action = _actionMapper.Map(vkey);
    if (!action.HasValue)
    {
      InvalidKeyPressed?.Invoke();
      return;
    }

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

  private void HandleBackspace()
  {
    switch (State)
    {
      case NavigatorState.L1_AwaitSecond:
        State = NavigatorState.L1_AwaitFirst;
        break;
      case NavigatorState.L2_AwaitSecond:
        State = NavigatorState.L2_AwaitFirst;
        break;
      case NavigatorState.L3_AwaitSecond:
        State = NavigatorState.L3_AwaitFirst;
        break;
      default:
        // Backspace at other states does nothing
        break;
    }
  }
}
