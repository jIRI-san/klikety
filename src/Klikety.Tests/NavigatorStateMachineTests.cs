using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public class NavigatorStateMachineTests {
    private static readonly VKey[] FirstKeys = [VKey.A, VKey.S, VKey.D];
    private static readonly VKey[] SecondKeys = [VKey.W, VKey.E];

    private static NavigatorStateMachine CreateMachine(NavigationMode mode = NavigationMode.Both, int level3Threshold = 0) {
        var mapper = new ActionMapper(new Dictionary<string, MouseAction>(StringComparer.OrdinalIgnoreCase));
        return new NavigatorStateMachine(FirstKeys, SecondKeys, mapper, mode, level3Threshold);
    }

    private static IReadOnlyList<GridCell> CreateGrid()
        => GridCalculator.Calculate(new Rectangle(0, 0, 300, 200), 3, 2);

    [Fact]
    public void Activate_TransitionsToL1AwaitFirst() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(100, 100));
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
    }

    [Fact]
    public void FirstKey_TransitionsToL1AwaitSecond() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));
        int? highlightedCol = null;
        sm.ColumnHighlighted += (col, cells, level) => highlightedCol = col;

        sm.OnKey(VKey.A);
        Assert.Equal(NavigatorState.L1_AwaitSecond, sm.State);
        Assert.Equal(0, highlightedCol);
    }

    [Fact]
    public void SecondKey_TransitionsToL1AwaitAction() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));
        GridCell? enteredCell = null;
        sm.CellEntered += (cell, subgridCells, level) => enteredCell = cell;

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        Assert.Equal(NavigatorState.L1_AwaitAction, sm.State);
        Assert.NotNull(enteredCell);
        Assert.Equal(0, enteredCell.Value.Row);
        Assert.Equal(0, enteredCell.Value.Col);
    }

    [Fact]
    public void ActionKey_FiresActionRequested() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));
        Point? actionPoint = null;
        MouseAction? actionType = null;
        sm.ActionRequested += (pt, action) => { actionPoint = pt; actionType = action; };

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        sm.OnKey(VKey.Space);

        Assert.Equal(NavigatorState.Idle, sm.State);
        Assert.NotNull(actionPoint);
        Assert.Equal(MouseAction.LeftClick, actionType);
    }

    [Fact]
    public void EscapeAtL1_RaisesCancelled() {
        var sm = CreateMachine();
        var origin = new Point(500, 300);
        sm.Activate(CreateGrid(), origin);
        Point? cancelledPoint = null;
        sm.Cancelled += pt => cancelledPoint = pt;

        sm.OnKey(VKey.Escape);
        Assert.Equal(NavigatorState.Idle, sm.State);
        Assert.Equal(origin, cancelledPoint);
    }

    [Fact]
    public void EscapeAtL1AwaitSecond_ResetsToAwaitFirst() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));
        int? unhighlightedLevel = null;
        sm.ColumnUnhighlighted += level => unhighlightedLevel = level;

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Escape);
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
        Assert.Equal(1, unhighlightedLevel);
    }

    [Fact]
    public void InvalidKey_NoTransition() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));

        sm.OnKey(VKey.Z);
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
    }

    [Fact]
    public void ArrowKeys_InTwoKeyMode_Ignored() {
        var sm = CreateMachine(NavigationMode.TwoKey);
        sm.Activate(CreateGrid(), new Point(0, 0));
        bool highlighted = false;
        sm.CellHighlighted += _ => highlighted = true;

        sm.OnKey(VKey.Right);
        Assert.False(highlighted);
    }

    [Fact]
    public void ArrowKeys_InBothMode_RaisesCellHighlighted() {
        var sm = CreateMachine(NavigationMode.Both);
        sm.Activate(CreateGrid(), new Point(0, 0));
        GridCell? highlighted = null;
        sm.CellHighlighted += cell => highlighted = cell;

        sm.OnKey(VKey.Right);
        Assert.NotNull(highlighted);
    }

    [Fact]
    public void Enter_InBothMode_EntersCell() {
        var sm = CreateMachine(NavigationMode.Both);
        sm.Activate(CreateGrid(), new Point(0, 0));
        GridCell? enteredCell = null;
        sm.CellEntered += (cell, subgridCells, level) => enteredCell = cell;

        sm.OnKey(VKey.Return);
        Assert.NotNull(enteredCell);
        Assert.Equal(NavigatorState.L2_AwaitFirst, sm.State);
    }

    [Fact]
    public void Enter_InTwoKeyMode_Ignored() {
        var sm = CreateMachine(NavigationMode.TwoKey);
        sm.Activate(CreateGrid(), new Point(0, 0));
        MouseAction? action = null;
        sm.ActionRequested += (_, a) => action = a;

        sm.OnKey(VKey.Return);
        Assert.Null(action);
    }

    [Fact]
    public void Reset_GoesToIdle() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));
        sm.OnKey(VKey.A);
        sm.Reset();
        Assert.Equal(NavigatorState.Idle, sm.State);
    }

    [Fact]
    public void OnKey_InIdleState_NoEffect() {
        var sm = CreateMachine();
        bool anyEvent = false;
        sm.ColumnHighlighted += (_, _, _) => anyEvent = true;
        sm.CellHighlighted += _ => anyEvent = true;
        sm.ActionRequested += (_, _) => anyEvent = true;
        sm.Cancelled += _ => anyEvent = true;

        sm.OnKey(VKey.A);
        Assert.False(anyEvent);
    }

    [Fact]
    public void InvalidKey_AtAwaitFirst_FiresInvalidKeyPressed() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));
        bool fired = false;
        sm.InvalidKeyPressed += () => fired = true;

        sm.OnKey(VKey.Z);
        Assert.True(fired);
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
    }

    [Fact]
    public void InvalidKey_AtAwaitSecond_FiresInvalidKeyPressed() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));
        sm.OnKey(VKey.A);
        bool fired = false;
        sm.InvalidKeyPressed += () => fired = true;

        sm.OnKey(VKey.Z);
        Assert.True(fired);
        Assert.Equal(NavigatorState.L1_AwaitSecond, sm.State);
    }

    [Fact]
    public void InvalidKey_AtAwaitAction_FiresInvalidKeyPressed() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        bool fired = false;
        sm.InvalidKeyPressed += () => fired = true;

        sm.OnKey(VKey.Z);
        Assert.True(fired);
    }

    [Fact]
    public void FirstKey_AtAwaitSecond_RestartsColumnSelection() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));
        sm.OnKey(VKey.A);

        int? newCol = null;
        sm.ColumnHighlighted += (col, cells, level) => newCol = col;

        sm.OnKey(VKey.S);
        Assert.Equal(NavigatorState.L1_AwaitSecond, sm.State);
        Assert.Equal(1, newCol);
    }

    [Fact]
    public void Activate_WithEmptyCells_StaysIdle() {
        var sm = CreateMachine();
        sm.Activate([], new Point(0, 0));
        Assert.Equal(NavigatorState.Idle, sm.State);
    }

    [Fact]
    public void EscapeFromL2_ResetsToL1AwaitFirst() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        sm.OnKey(VKey.A);

        int? unhighlightedLevel = null;
        sm.ColumnUnhighlighted += level => unhighlightedLevel = level;

        sm.OnKey(VKey.Escape);
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
        Assert.Equal(1, unhighlightedLevel);
    }

    [Fact]
    public void EscapeFromL3_GoesToL2AwaitFirst() {
        var sm = CreateMachine(NavigationMode.Both);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 6000, 4000), 3, 2);
        sm.Activate(grid, new Point(0, 0));
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        sm.OnKey(VKey.A);

        int? unhighlightedLevel = null;
        sm.ColumnUnhighlighted += level => unhighlightedLevel = level;

        sm.OnKey(VKey.Escape);
        Assert.Equal(NavigatorState.L2_AwaitFirst, sm.State);
        Assert.Equal(2, unhighlightedLevel);
    }

    [Fact]
    public void SecondKey_AtL1_ComputesSubgridCells() {
        var sm = CreateMachine();
        sm.Activate(CreateGrid(), new Point(0, 0));
        IReadOnlyList<GridCell>? subgrid = null;
        sm.CellEntered += (cell, subgridCells, level) => subgrid = subgridCells;

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        Assert.NotNull(subgrid);
        Assert.True(subgrid!.Count > 0);
        Assert.True(subgrid[0].Bounds.Width < 300);
    }

    [Fact]
    public void SecondKey_AtL2_ThresholdNotMet_EmptySubgrid() {
        var sm = CreateMachine(level3Threshold: 40000);
        sm.Activate(CreateGrid(), new Point(0, 0));
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        sm.OnKey(VKey.A);

        IReadOnlyList<GridCell>? subgrid = null;
        sm.CellEntered += (cell, subgridCells, level) => subgrid = subgridCells;

        sm.OnKey(VKey.W);
        Assert.NotNull(subgrid);
        Assert.Empty(subgrid!);
    }

    [Fact]
    public void ArrowNav_TraversesFullGrid() {
        var sm = CreateMachine(NavigationMode.Both);
        sm.Activate(CreateGrid(), new Point(0, 0));
        var highlights = new List<GridCell>();
        sm.CellHighlighted += cell => highlights.Add(cell);

        // Move right 3 times across full 3-column grid
        sm.OnKey(VKey.Right);
        sm.OnKey(VKey.Right);
        sm.OnKey(VKey.Right);
        Assert.Equal(3, highlights.Count);
        // With 3 cols: index 0→1→2→0 (wrap)
        Assert.Equal(0, highlights[2].Col);
    }

    [Fact]
    public void EscapeFromL2AwaitAction_GoesToL1AwaitFirst() {
        var sm = CreateMachine(NavigationMode.Both);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 6000, 4000), 3, 2);
        sm.Activate(grid, new Point(0, 0));
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);

        Assert.Equal(NavigatorState.L2_AwaitAction, sm.State);

        int? unhighlightedLevel = null;
        sm.ColumnUnhighlighted += level => unhighlightedLevel = level;

        sm.OnKey(VKey.Escape);
        Assert.Equal(NavigatorState.L1_AwaitFirst, sm.State);
        Assert.Equal(1, unhighlightedLevel);
    }

    [Fact]
    public void ActionAtL1_WithoutNavigation_FiresAtOrigin() {
        var origin = new Point(500, 300);
        var sm = CreateMachine(NavigationMode.Both);
        sm.Activate(CreateGrid(), origin);
        Point? actionPoint = null;
        sm.ActionRequested += (pt, _) => actionPoint = pt;

        sm.OnKey(VKey.Space);
        Assert.Equal(origin, actionPoint);
    }

    [Fact]
    public void ActionAtL2_WithoutL2Navigation_FiresAtL1CellCenter() {
        var sm = CreateMachine(NavigationMode.Both);
        sm.Activate(CreateGrid(), new Point(0, 0));
        GridCell? l1Cell = null;
        sm.CellEntered += (cell, _, level) => { if (level == 1) { l1Cell = cell; } };
        Point? actionPoint = null;
        sm.ActionRequested += (pt, _) => actionPoint = pt;

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        Assert.Equal(NavigatorState.L1_AwaitAction, sm.State);

        var expectedPoint = GridCalculator.CenterOf(l1Cell!.Value);
        sm.OnKey(VKey.Space);
        Assert.Equal(expectedPoint, actionPoint);
    }

    [Fact]
    public void ActionAtL2AwaitAction_WithoutL3Descent_FiresAtL2CellCenter() {
        var sm = CreateMachine(NavigationMode.Both);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 6000, 4000), 3, 2);
        sm.Activate(grid, new Point(0, 0));
        GridCell? l2Cell = null;
        sm.CellEntered += (cell, _, level) => { if (level == 2) { l2Cell = cell; } };
        Point? actionPoint = null;
        sm.ActionRequested += (pt, _) => actionPoint = pt;

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        Assert.Equal(NavigatorState.L2_AwaitAction, sm.State);

        var expectedPoint = GridCalculator.CenterOf(l2Cell!.Value);
        sm.OnKey(VKey.Space);
        Assert.Equal(expectedPoint, actionPoint);
    }

    [Fact]
    public void ActionAfterArrow_FiresAtArrowedCellCenter() {
        var sm = CreateMachine(NavigationMode.Both);
        var grid = CreateGrid();
        sm.Activate(grid, new Point(500, 300));
        Point? actionPoint = null;
        sm.ActionRequested += (pt, _) => actionPoint = pt;

        sm.OnKey(VKey.Right);
        var expectedPoint = GridCalculator.CenterOf(grid[1]);
        sm.OnKey(VKey.Space);
        Assert.Equal(expectedPoint, actionPoint);
    }

    [Fact]
    public void ActionAfterReselect_FiresAtReselectedCellCenter() {
        var sm = CreateMachine(NavigationMode.Both, level3Threshold: 40000);
        sm.Activate(CreateGrid(), new Point(0, 0));
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);

        // Now at L2_AwaitAction. Reselect a different cell in same column.
        GridCell? reselectedCell = null;
        sm.CellEntered += (cell, _, _) => reselectedCell = cell;
        sm.OnKey(VKey.E);

        Point? actionPoint = null;
        sm.ActionRequested += (pt, _) => actionPoint = pt;
        sm.OnKey(VKey.Space);

        var expected = GridCalculator.CenterOf(reselectedCell!.Value);
        Assert.Equal(expected, actionPoint);
    }

    [Fact]
    public void TwoKeyAction_AtL1AwaitAction_FiresAtCellCenter() {
        var sm = CreateMachine(NavigationMode.TwoKey);
        sm.Activate(CreateGrid(), new Point(500, 300));
        GridCell? l1Cell = null;
        sm.CellEntered += (cell, _, level) => { if (level == 1) { l1Cell = cell; } };
        Point? actionPoint = null;
        sm.ActionRequested += (pt, _) => actionPoint = pt;

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        sm.OnKey(VKey.Space);

        var expected = GridCalculator.CenterOf(l1Cell!.Value);
        Assert.Equal(expected, actionPoint);
    }

    [Fact]
    public void TwoKeyAction_AtL3_FiresAtL3CellCenter() {
        var sm = CreateMachine(NavigationMode.TwoKey);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 6000, 4000), 3, 2);
        sm.Activate(grid, new Point(0, 0));
        GridCell? l3Cell = null;
        sm.CellEntered += (cell, _, level) => { if (level == 3) { l3Cell = cell; } };
        Point? actionPoint = null;
        sm.ActionRequested += (pt, _) => actionPoint = pt;

        // L1
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        // L2
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        // L3
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        Assert.Equal(NavigatorState.L3_AwaitAction, sm.State);

        sm.OnKey(VKey.Space);

        var expected = GridCalculator.CenterOf(l3Cell!.Value);
        Assert.Equal(expected, actionPoint);
    }

    [Fact]
    public void EscapeFromL2_RestoresActionPointToOrigin() {
        var origin = new Point(500, 300);
        var sm = CreateMachine(NavigationMode.Both);
        sm.Activate(CreateGrid(), origin);

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        // At L1_AwaitAction, cursor moved to L1 cell center
        sm.OnKey(VKey.Escape);
        // Back to L1_AwaitFirst, actionPoint should be origin

        Point? actionPoint = null;
        sm.ActionRequested += (pt, _) => actionPoint = pt;
        sm.OnKey(VKey.Space);
        Assert.Equal(origin, actionPoint);
    }

    [Fact]
    public void EscapeFromL3_RestoresActionPointToL2CellCenter() {
        var sm = CreateMachine(NavigationMode.Both);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 6000, 4000), 3, 2);
        sm.Activate(grid, new Point(0, 0));
        GridCell? l2Cell = null;
        sm.CellEntered += (cell, _, level) => { if (level == 2) { l2Cell = cell; } };

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        // At L2_AwaitAction
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.W);
        // At L3_AwaitAction
        Assert.Equal(NavigatorState.L3_AwaitAction, sm.State);
        sm.OnKey(VKey.Escape);
        // Back to L2_AwaitFirst

        Point? actionPoint = null;
        sm.ActionRequested += (pt, _) => actionPoint = pt;
        sm.OnKey(VKey.Space);

        var expected = GridCalculator.CenterOf(l2Cell!.Value);
        Assert.Equal(expected, actionPoint);
    }

    [Fact]
    public void ActionAfterEnterZoom_FiresAtZoomedCellCenter() {
        var sm = CreateMachine(NavigationMode.Both);
        sm.Activate(CreateGrid(), new Point(500, 300));
        GridCell? enteredCell = null;
        sm.CellEntered += (cell, _, level) => { if (level == 1) { enteredCell = cell; } };
        Point? actionPoint = null;
        sm.ActionRequested += (pt, _) => actionPoint = pt;

        // Arrow to cell[1], then Enter to zoom into L2
        sm.OnKey(VKey.Right);
        sm.OnKey(VKey.Return);
        Assert.Equal(NavigatorState.L2_AwaitFirst, sm.State);

        // Immediate action at L2 without further navigation
        var expected = GridCalculator.CenterOf(enteredCell!.Value);
        sm.OnKey(VKey.Space);
        Assert.Equal(expected, actionPoint);
    }
}
