using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public class NavigatorStateMachineTests {
    private static readonly VKey[] HorizontalKeys = [VKey.A, VKey.S, VKey.D];
    private static readonly VKey[] VerticalKeys = [VKey.W, VKey.E];

    private static NavigatorStateMachine CreateMachine(NavigationMode mode = NavigationMode.Both, int level3Threshold = 0) {
        var mapper = new ActionMapper(new Dictionary<string, MouseAction>(StringComparer.OrdinalIgnoreCase));
        return new NavigatorStateMachine(HorizontalKeys, VerticalKeys, mapper, mode, level3Threshold);
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

    // --- DynamicKeyReducer integration tests (step 2.2) ---

    private static readonly VKey[] TenHorizKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon];
    private static readonly VKey[] TenVertKeys = [VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P];

    private static NavigatorStateMachine CreateMachineWithReduction(
        NavigationMode mode = NavigationMode.Both, int level3Threshold = 0, int minCellPx = 5) {
        var mapper = new ActionMapper(new Dictionary<string, MouseAction>(StringComparer.OrdinalIgnoreCase));
        return new NavigatorStateMachine(TenHorizKeys, TenVertKeys, mapper, mode, level3Threshold, minCellPx);
    }

    [Fact]
    public void L2_UsesReducedKeyCount_SubgridHasFewerCells() {
        // 1000x1000 screen, 10x10 grid → L1 cells = 100x100
        // At L2 with minCellPx=20: 100/20 = 5 maxKeys → reduced from 10 to 5 per axis
        // L2 subgrid: 5×5 = 25 cells
        var sm = CreateMachineWithReduction(NavigationMode.TwoKey, minCellPx: 20);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 1000, 1000), 10, 10);
        sm.Activate(grid, new Point(0, 0));

        IReadOnlyList<GridCell>? subgridCells = null;
        sm.CellEntered += (_, sub, level) => { if (level == 1) subgridCells = sub; };

        // Select cell (0,0) — 100×100 parent
        sm.OnKey(VKey.A); // first horiz key
        sm.OnKey(VKey.Q); // first vert key

        Assert.Equal(NavigatorState.L1_AwaitAction, sm.State);
        Assert.NotNull(subgridCells);
        Assert.Equal(25, subgridCells!.Count); // 5 cols × 5 rows
    }

    [Fact]
    public void L2_ReducedKeys_OnlyCenteredKeysAccepted() {
        // With 10 keys reduced to 5, centered: drop 2.5 from each side → drop 2 left, 3 right
        // Active keys: indices 2..6 → D, F, G, H, J (horiz); E, R, T, Y, U (vert)
        var sm = CreateMachineWithReduction(NavigationMode.TwoKey, minCellPx: 20);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 1000, 1000), 10, 10);
        sm.Activate(grid, new Point(0, 0));

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q); // Enter L2

        // At L2, 'A' is not in the reduced key set — should fire InvalidKeyPressed
        bool invalidFired = false;
        sm.InvalidKeyPressed += () => invalidFired = true;
        sm.OnKey(VKey.A);
        Assert.True(invalidFired);

        // 'F' (index 3 in full, index 1 in reduced) should work
        int? highlightedCol = null;
        sm.ColumnHighlighted += (col, _, level) => { if (level == 2) highlightedCol = col; };
        sm.OnKey(VKey.F);
        Assert.Equal(1, highlightedCol);
    }

    [Fact]
    public void L3_UsesFurtherReducedKeyCount() {
        // 2000x2000 screen, 10x10 → L1 cells = 200×200
        // L2: 200/20 = 10 maxKeys → no reduction (all 10 fit) → L2 cells = 10×10 = 100
        // L2 cell = 200/10 = 20×20. With level3Threshold=0 (always activate L3):
        // L3: 20/20 = 1 maxKey → <2 → disabled
        // Need bigger: 3000x3000 → L1 = 300×300 → L2: 300/20 = 15 ≥ 10 → no reduction → L2 cells 30×30
        // L2 cell = 30×30 → L3: 30/20 = 1 → <2 → disabled. Still too small.
        // Use minCellPx=10: 3000x3000 → L1=300, L2: 300/10=30≥10 → L2 cell=30x30. L3: 30/10=3 → 3 keys
        var sm = CreateMachineWithReduction(NavigationMode.TwoKey, level3Threshold: 0, minCellPx: 10);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 3000, 3000), 10, 10);
        sm.Activate(grid, new Point(0, 0));

        IReadOnlyList<GridCell>? l2Subgrid = null;
        IReadOnlyList<GridCell>? l3Subgrid = null;
        sm.CellEntered += (_, sub, level) => {
            if (level == 1) l2Subgrid = sub;
            if (level == 2) l3Subgrid = sub;
        };

        // L1 → L2 (no reduction since 300/10 = 30 ≥ 10 keys)
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q);
        Assert.Equal(100, l2Subgrid!.Count); // 10×10

        // L2 → L3: L2 cell = 30×30, 30/10 = 3 maxKeys → reduced to 3
        sm.OnKey(VKey.A); // L2 first (no reduction at L2 so A is valid)
        sm.OnKey(VKey.Q);

        Assert.NotNull(l3Subgrid);
        Assert.Equal(9, l3Subgrid!.Count); // 3×3
    }

    [Fact]
    public void L3_Disabled_WhenParentCellTooSmall() {
        // 1000x1000 screen, 10x10 → L1 cells = 100×100
        // L2 with minCellPx=20: 100/20=5 → reduced to 5 → L2 cells = 5×5 = 25
        // L2 cell = 100/5 = 20×20. L3: 20/20=1 → <2 → disabled
        var sm = CreateMachineWithReduction(NavigationMode.TwoKey, level3Threshold: 0, minCellPx: 20);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 1000, 1000), 10, 10);
        sm.Activate(grid, new Point(0, 0));

        IReadOnlyList<GridCell>? l3Subgrid = null;
        sm.CellEntered += (_, sub, level) => { if (level == 2) l3Subgrid = sub; };

        // L1 → L2
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q);

        // L2 cell entry — centered keys at L2: D, F, G, H, J (horiz); E, R, T, Y, U (vert)
        sm.OnKey(VKey.F);
        sm.OnKey(VKey.R);

        // L3 should be disabled — no subgrid, state stays at L2_AwaitAction
        Assert.Equal(NavigatorState.L2_AwaitAction, sm.State);
        Assert.Empty(l3Subgrid!);
    }

    [Fact]
    public void L2_Disabled_WhenParentCellTooSmall_StaysAtL1AwaitAction() {
        // 200x200 screen, 10x10 → L1 cells = 20×20
        // L2 with minCellPx=20: 20/20=1 → <2 → disabled
        var sm = CreateMachineWithReduction(NavigationMode.TwoKey, minCellPx: 20);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 200, 200), 10, 10);
        sm.Activate(grid, new Point(0, 0));

        IReadOnlyList<GridCell>? subgrid = null;
        sm.CellEntered += (_, sub, level) => { if (level == 1) subgrid = sub; };

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q);

        Assert.Equal(NavigatorState.L1_AwaitAction, sm.State);
        Assert.Empty(subgrid!);
    }

    [Fact]
    public void L3_Activates_OnStandardScreen_WithMinCellPx5() {
        // 1920x1080 screen, 10x10 → L1 cells = 192×108
        // L2 with minCellPx=5: 192/5=38 ≥ 10 → no reduction → 10×10 = 100 L2 cells
        // L2 cell = 192/10 = ~19×10. L3: min(19,10)/5=2 → 2 keys → 2×2 = 4 cells
        var sm = CreateMachineWithReduction(NavigationMode.TwoKey, level3Threshold: 0);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 1920, 1080), 10, 10);
        sm.Activate(grid, new Point(0, 0));

        IReadOnlyList<GridCell>? l2Subgrid = null;
        IReadOnlyList<GridCell>? l3Subgrid = null;
        sm.CellEntered += (_, sub, level) => {
            if (level == 1) l2Subgrid = sub;
            if (level == 2) l3Subgrid = sub;
        };

        // L1 → L2 (no reduction with minCellPx=5)
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q);
        Assert.Equal(100, l2Subgrid!.Count); // 10×10

        // L2 → L3: L2 cell ≥ 5px both axes → L3 activates
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q);

        Assert.NotNull(l3Subgrid);
        Assert.True(l3Subgrid!.Count >= 4); // at least 2×2
    }

    [Fact]
    public void ArrowNav_AtL2_UsesReducedColumnCount() {
        // 1000x1000, 10x10 → L1 = 100×100 → L2 with minCellPx=20: 5×5 = 25 cells
        var sm = CreateMachineWithReduction(NavigationMode.Both, minCellPx: 20);
        var grid = GridCalculator.Calculate(new Rectangle(0, 0, 1000, 1000), 10, 10);
        sm.Activate(grid, new Point(0, 0));

        // Enter L1 cell via Enter (arrow mode)
        sm.OnKey(VKey.Return); // Enter cell[0] → L2

        Assert.Equal(NavigatorState.L2_AwaitFirst, sm.State);

        // Arrow right wraps within 5 cols (not 10)
        // Right 4 times → col 4. One more right → wraps to col 0 (same row)
        GridCell? highlighted = null;
        sm.CellHighlighted += cell => highlighted = cell;

        for (int i = 0; i < 4; i++) {
            sm.OnKey(VKey.Right);
        }
        // Now at index 4 (row 0, col 4)
        Assert.Equal(4, highlighted!.Value.Col);
        Assert.Equal(0, highlighted.Value.Row);

        // One more right wraps to col 0 within same row
        sm.OnKey(VKey.Right);
        Assert.Equal(0, highlighted.Value.Col);
        Assert.Equal(0, highlighted.Value.Row);

        // Down should move to row 1 col 0 (index 5)
        sm.OnKey(VKey.Down);
        Assert.Equal(0, highlighted.Value.Col);
        Assert.Equal(1, highlighted.Value.Row);
    }
}
