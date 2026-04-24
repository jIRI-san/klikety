using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public class NavigatorStateMachineTests {
    private static readonly VKey[] FirstKeys = [VKey.A, VKey.S, VKey.D];
    private static readonly VKey[] SecondKeys = [VKey.W, VKey.E];

    private static NavigatorStateMachine CreateMachine(NavigationMode mode = NavigationMode.Both) {
        var mapper = new ActionMapper(new Dictionary<string, MouseAction>(StringComparer.OrdinalIgnoreCase));
        var left = new HalfKeySetsConfig { FirstKeys = FirstKeys, SecondKeys = SecondKeys };
        var right = new HalfKeySetsConfig { FirstKeys = [VKey.J, VKey.K, VKey.L], SecondKeys = [VKey.U, VKey.I] };
        return new NavigatorStateMachine(left, right, mapper, mode, 40000);
    }

    private static IReadOnlyList<GridCell> CreateLeftGrid()
        => GridCalculator.Calculate(new Rectangle(0, 0, 150, 200), 3, 2);

    private static IReadOnlyList<GridCell> CreateRightGrid()
        => GridCalculator.Calculate(new Rectangle(150, 0, 150, 200), 3, 2);

    [Fact]
    public void Activate_TransitionsToL1AwaitFirst() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(100, 100));
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
    }

    [Fact]
    public void FirstKey_TransitionsToL1AwaitSecond() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        int? highlightedCol = null;
        sm.ColumnHighlighted += (half, col, cells, level) => highlightedCol = col;

        sm.OnKey(VKey.A); // firstKeys[0]
        Assert.Equal(NavigatorState.L1_AwaitSecond, sm.State);
        Assert.Equal(0, highlightedCol);
    }

    [Fact]
    public void SecondKey_TransitionsToL1AwaitAction() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        GridCell? enteredCell = null;
        sm.CellEntered += (cell, level) => enteredCell = cell;

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W); // secondKeys[0] → row 0, col 0
        Assert.Equal(NavigatorState.L1_AwaitAction, sm.State);
        Assert.NotNull(enteredCell);
        Assert.Equal(0, enteredCell.Value.Row);
        Assert.Equal(0, enteredCell.Value.Col);
    }

    [Fact]
    public void ActionKey_FiresActionRequested() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        Point? actionPoint = null;
        MouseAction? actionType = null;
        sm.ActionRequested += (pt, action) => { actionPoint = pt; actionType = action; };

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        sm.OnKey(VKey.Space); // default → LeftClick

        Assert.Equal(NavigatorState.Idle, sm.State);
        Assert.NotNull(actionPoint);
        Assert.Equal(MouseAction.LeftClick, actionType);
    }

    [Fact]
    public void EscapeAtL1_RaisesCancelled() {
        var sm = CreateMachine();
        var origin = new Point(500, 300);
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), origin);
        Point? cancelledPoint = null;
        sm.Cancelled += pt => cancelledPoint = pt;

        sm.OnKey(VKey.Escape);
        Assert.Equal(NavigatorState.Idle, sm.State);
        Assert.Equal(origin, cancelledPoint);
    }

    [Fact]
    public void EscapeAtL1AwaitSecond_RaisesCancelled() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        bool cancelled = false;
        sm.Cancelled += _ => cancelled = true;

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Escape);
        Assert.Equal(NavigatorState.Idle, sm.State);
        Assert.True(cancelled);
    }

    [Fact]
    public void InvalidKey_NoTransition() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));

        sm.OnKey(VKey.Z); // not in firstKeys
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
    }

    [Fact]
    public void ArrowKeys_InTwoKeyMode_Ignored() {
        var sm = CreateMachine(NavigationMode.TwoKey);
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        bool highlighted = false;
        sm.CellHighlighted += _ => highlighted = true;

        sm.OnKey(VKey.Right);
        Assert.False(highlighted);
    }

    [Fact]
    public void ArrowKeys_InBothMode_RaisesCellHighlighted() {
        var sm = CreateMachine(NavigationMode.Both);
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        GridCell? highlighted = null;
        sm.CellHighlighted += cell => highlighted = cell;

        sm.OnKey(VKey.Right);
        Assert.NotNull(highlighted);
    }

    [Fact]
    public void Enter_InBothMode_FiresAction() {
        var sm = CreateMachine(NavigationMode.Both);
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        MouseAction? action = null;
        sm.ActionRequested += (_, a) => action = a;

        sm.OnKey(VKey.Return);
        Assert.Equal(MouseAction.LeftClick, action);
    }

    [Fact]
    public void Enter_InTwoKeyMode_Ignored() {
        var sm = CreateMachine(NavigationMode.TwoKey);
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        MouseAction? action = null;
        sm.ActionRequested += (_, a) => action = a;

        sm.OnKey(VKey.Return);
        Assert.Null(action);
    }

    [Fact]
    public void Reset_GoesToIdle() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        sm.OnKey(VKey.A);
        sm.Reset();
        Assert.Equal(NavigatorState.Idle, sm.State);
    }

    [Fact]
    public void OnKey_InIdleState_NoEffect() {
        var sm = CreateMachine();
        bool anyEvent = false;
        sm.ColumnHighlighted += (_, _, _, _) => anyEvent = true;
        sm.CellHighlighted += _ => anyEvent = true;
        sm.ActionRequested += (_, _) => anyEvent = true;
        sm.Cancelled += _ => anyEvent = true;

        sm.OnKey(VKey.A);
        Assert.False(anyEvent);
    }

    [Fact]
    public void InvalidKey_AtAwaitFirst_FiresInvalidKeyPressed() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        bool fired = false;
        sm.InvalidKeyPressed += () => fired = true;

        sm.OnKey(VKey.Z); // not in firstKeys
        Assert.True(fired);
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
    }

    [Fact]
    public void InvalidKey_AtAwaitSecond_FiresInvalidKeyPressed() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        sm.OnKey(VKey.A); // valid first key → AwaitSecond
        bool fired = false;
        sm.InvalidKeyPressed += () => fired = true;

        sm.OnKey(VKey.Z); // not first or second key
        Assert.True(fired);
        Assert.Equal(NavigatorState.L1_AwaitSecond, sm.State);
    }

    [Fact]
    public void InvalidKey_AtAwaitAction_FiresInvalidKeyPressed() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W); // → AwaitAction
        bool fired = false;
        sm.InvalidKeyPressed += () => fired = true;

        sm.OnKey(VKey.Z); // not action key, not first key
        Assert.True(fired);
    }

    [Fact]
    public void Backspace_AtAwaitSecond_GoesBackToAwaitFirst() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        sm.OnKey(VKey.A); // → AwaitSecond
        Assert.Equal(NavigatorState.L1_AwaitSecond, sm.State);

        sm.OnKey(VKey.Back);
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
    }

    [Fact]
    public void Backspace_AtAwaitFirst_NoEffect() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));

        sm.OnKey(VKey.Back);
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
    }

    [Fact]
    public void FirstKey_AtAwaitSecond_RestartsColumnSelection() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        sm.OnKey(VKey.A); // col 0 → AwaitSecond

        int? newCol = null;
        sm.ColumnHighlighted += (half, col, cells, level) => newCol = col;

        sm.OnKey(VKey.S); // first key (col 1) at AwaitSecond → re-entry
        Assert.Equal(NavigatorState.L1_AwaitSecond, sm.State);
        Assert.Equal(1, newCol); // column changed to S's index
    }

    [Fact]
    public void RightHalfFirstKey_SelectsRightCells() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        ScreenHalf? half = null;
        IReadOnlyList<GridCell>? cells = null;
        int? level = null;
        sm.ColumnHighlighted += (h, col, c, l) => { half = h; cells = c; level = l; };

        sm.OnKey(VKey.J); // right-half first key
        Assert.Equal(ScreenHalf.Right, half);
        Assert.Equal(1, level);
        // Right grid cells have X >= 150
        Assert.True(cells![0].Bounds.X >= 150);
    }

    [Fact]
    public void RightHalfSecondKey_SelectsFromRightCellList() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        GridCell? enteredCell = null;
        sm.CellEntered += (cell, _) => enteredCell = cell;

        sm.OnKey(VKey.J); // right first key
        sm.OnKey(VKey.U); // right second key
        Assert.NotNull(enteredCell);
        Assert.True(enteredCell!.Value.Bounds.X >= 150);
    }

    [Fact]
    public void HalfSwitching_AtL1AwaitSecond() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        sm.OnKey(VKey.A); // left half

        ScreenHalf? half = null;
        IReadOnlyList<GridCell>? cells = null;
        sm.ColumnHighlighted += (h, col, c, l) => { half = h; cells = c; };

        sm.OnKey(VKey.J); // switch to right half
        Assert.Equal(ScreenHalf.Right, half);
        Assert.True(cells![0].Bounds.X >= 150);
    }

    [Fact]
    public void Backspace_FiresColumnUnhighlighted_ResetsToLeft() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        sm.OnKey(VKey.J); // right half
        Assert.Equal(NavigatorState.L1_AwaitSecond, sm.State);

        int? unhighlightedLevel = null;
        sm.ColumnUnhighlighted += level => unhighlightedLevel = level;

        sm.OnKey(VKey.Back);
        Assert.Equal(1, unhighlightedLevel);
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
    }

    [Fact]
    public void Backspace_AtL2AwaitSecond_FiresColumnUnhighlighted() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        sm.OnKey(VKey.A); // L1 first
        sm.OnKey(VKey.W); // L1 second → AwaitAction
        sm.OnKey(VKey.A); // Navigate into L2 → L2_AwaitSecond

        int? unhighlightedLevel = null;
        sm.ColumnUnhighlighted += level => unhighlightedLevel = level;

        sm.OnKey(VKey.Back);
        Assert.Equal(2, unhighlightedLevel);
        Assert.Equal(NavigatorState.L2_AwaitFirst, sm.State);
    }

    [Fact]
    public void Activate_WithEmptyCells_StaysIdle() {
        var sm = CreateMachine();
        sm.Activate([], CreateRightGrid(), new Point(0, 0));
        Assert.Equal(NavigatorState.Idle, sm.State);
    }

    [Fact]
    public void LevelExited_FromL2_FiresWithCorrectCells() {
        var sm = CreateMachine();
        sm.Activate(CreateLeftGrid(), CreateRightGrid(), new Point(0, 0));
        sm.OnKey(VKey.A); // L1 first
        sm.OnKey(VKey.W); // L1 second → L1_AwaitAction
        sm.OnKey(VKey.A); // Navigate into L2

        GridCell? parentCell = null;
        IReadOnlyList<GridCell>? exitedCells = null;
        int? exitedLevel = null;
        sm.LevelExited += (cell, cells, level) => { parentCell = cell; exitedCells = cells; exitedLevel = level; };

        sm.OnKey(VKey.Escape); // Escape from L2 → back to L1
        Assert.Equal(NavigatorState.L1_AwaitAction, sm.State);
        Assert.Equal(1, exitedLevel);
        Assert.NotNull(parentCell);
        Assert.NotNull(exitedCells);
    }

    [Fact]
    public void LevelExited_FromL3_CarriesL2Cells() {
        var sm = CreateMachine(NavigationMode.Both);
        // Use a large grid so L3 threshold is met
        var leftGrid = GridCalculator.Calculate(new Rectangle(0, 0, 3000, 2000), 3, 2);
        var rightGrid = GridCalculator.Calculate(new Rectangle(3000, 0, 3000, 2000), 3, 2);
        sm.Activate(leftGrid, rightGrid, new Point(0, 0));
        sm.OnKey(VKey.A); // L1 first
        sm.OnKey(VKey.W); // L1 second → AwaitAction
        sm.OnKey(VKey.A); // L2 first (navigate into subgrid)
        sm.OnKey(VKey.W); // L2 second → AwaitAction
        sm.OnKey(VKey.A); // L3 first (navigate into sub-subgrid)

        GridCell? parentCell = null;
        int? exitedLevel = null;
        sm.LevelExited += (cell, cells, level) => { parentCell = cell; exitedLevel = level; };

        sm.OnKey(VKey.Escape); // Escape from L3 → back to L2
        Assert.Equal(NavigatorState.L2_AwaitAction, sm.State);
        Assert.Equal(2, exitedLevel);
        Assert.NotNull(parentCell);
    }
}
