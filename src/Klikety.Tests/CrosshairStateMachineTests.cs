using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public class CrosshairStateMachineTests {
    private static readonly VKey[] HorizKeys = [
        VKey.A, VKey.S, VKey.D, VKey.F, VKey.G,
        VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon,
    ];

    private static readonly VKey[] VertKeys = [
        VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T,
        VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P,
    ];

    private static CrosshairStateMachine CreateSM(bool arrowKeys = true) {
        var actionMapper = new ActionMapper(new Dictionary<string, MouseAction>());
        return new CrosshairStateMachine(HorizKeys, VertKeys, actionMapper, arrowKeys);
    }

    private static CrosshairGrid CreateGrid() {
        return CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 1100, 1100), HorizKeys.Length, VertKeys.Length);
    }

    // --- Basic state transitions ---

    [Fact]
    public void AfterActivate_StateIsAwaitInput() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        Assert.Equal(CrosshairStateMachine.State.AwaitInput, sm.CurrentState);
    }

    [Fact]
    public void HorizKey_TransitionsToHorizSet() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        sm.OnKey(VKey.A); // First horiz key

        Assert.Equal(CrosshairStateMachine.State.HorizSet, sm.CurrentState);
    }

    [Fact]
    public void VertKey_TransitionsToVertSet() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        sm.OnKey(VKey.Q); // First vert key

        Assert.Equal(CrosshairStateMachine.State.VertSet, sm.CurrentState);
    }

    [Fact]
    public void HorizThenVert_TransitionsToBothSet() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q);

        Assert.Equal(CrosshairStateMachine.State.BothSet, sm.CurrentState);
    }

    [Fact]
    public void VertThenHoriz_TransitionsToBothSet() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        sm.OnKey(VKey.Q);
        sm.OnKey(VKey.A);

        Assert.Equal(CrosshairStateMachine.State.BothSet, sm.CurrentState);
    }

    // --- Same-axis re-press ---

    [Fact]
    public void HorizRePress_OverridesPrevious() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        int lastCol = -1;
        sm.HorizSelected += (col, _) => lastCol = col;

        sm.OnKey(VKey.A); // col 0
        Assert.Equal(0, lastCol);

        sm.OnKey(VKey.S); // col 1
        Assert.Equal(1, lastCol);
        Assert.Equal(CrosshairStateMachine.State.HorizSet, sm.CurrentState);
    }

    [Fact]
    public void VertRePress_OverridesPrevious() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        int lastRow = -1;
        sm.VertSelected += (row, _) => lastRow = row;

        sm.OnKey(VKey.Q); // row 0
        Assert.Equal(0, lastRow);

        sm.OnKey(VKey.W); // row 1
        Assert.Equal(1, lastRow);
        Assert.Equal(CrosshairStateMachine.State.VertSet, sm.CurrentState);
    }

    // --- Key-to-grid mapping (skipping center) ---

    [Fact]
    public void HorizKeys_SkipCenterCol() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        // 10 horiz keys → 11 cols, center at col 5
        // Key indices 0-4 → cols 0-4, key indices 5-9 → cols 6-10
        var cols = new List<int>();
        sm.HorizSelected += (col, _) => cols.Add(col);

        foreach (var key in HorizKeys) {
            sm.OnKey(key);
        }

        // Keys map to: 0,1,2,3,4,6,7,8,9,10 (skipping center col 5)
        Assert.Equal([0, 1, 2, 3, 4, 6, 7, 8, 9, 10], cols);
    }

    [Fact]
    public void VertKeys_SkipCenterRow() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        var rows = new List<int>();
        sm.VertSelected += (row, _) => rows.Add(row);

        foreach (var key in VertKeys) {
            sm.OnKey(key);
        }

        Assert.Equal([0, 1, 2, 3, 4, 6, 7, 8, 9, 10], rows);
    }

    // --- Cell selection ---

    [Fact]
    public void BothAxes_FiresCellSelected() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        GridCell? selected = null;
        sm.CellSelected += cell => selected = cell;

        sm.OnKey(VKey.A); // col 0
        sm.OnKey(VKey.Q); // row 0

        Assert.NotNull(selected);
        Assert.Equal(0, selected.Value.Row);
        Assert.Equal(0, selected.Value.Col);
    }

    // --- Enter behavior ---

    [Fact]
    public void Enter_NoAxis_EntersSubgridAtCenter() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        GridCell? parentCell = null;
        sm.SubgridEntered += (cell, _) => parentCell = cell;

        sm.OnKey(VKey.Return);

        Assert.NotNull(parentCell);
        Assert.Equal(grid.CenterRow, parentCell.Value.Row);
        Assert.Equal(grid.CenterCol, parentCell.Value.Col);
    }

    [Fact]
    public void Enter_HorizOnly_EntersSubgridAtCenterRowSelectedCol() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        GridCell? parentCell = null;
        sm.SubgridEntered += (cell, _) => parentCell = cell;

        sm.OnKey(VKey.A); // col 0
        sm.OnKey(VKey.Return);

        Assert.NotNull(parentCell);
        Assert.Equal(grid.CenterRow, parentCell.Value.Row);
        Assert.Equal(0, parentCell.Value.Col);
    }

    [Fact]
    public void Enter_VertOnly_EntersSubgridAtSelectedRowCenterCol() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        GridCell? parentCell = null;
        sm.SubgridEntered += (cell, _) => parentCell = cell;

        sm.OnKey(VKey.Q); // row 0
        sm.OnKey(VKey.Return);

        Assert.NotNull(parentCell);
        Assert.Equal(0, parentCell.Value.Row);
        Assert.Equal(grid.CenterCol, parentCell.Value.Col);
    }

    [Fact]
    public void Enter_BothSet_EntersSubgridAtIntersection() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        GridCell? parentCell = null;
        sm.SubgridEntered += (cell, _) => parentCell = cell;

        sm.OnKey(VKey.A); // col 0
        sm.OnKey(VKey.Q); // row 0
        sm.OnKey(VKey.Return);

        Assert.NotNull(parentCell);
        Assert.Equal(0, parentCell.Value.Row);
        Assert.Equal(0, parentCell.Value.Col);
    }

    // --- Escape behavior ---

    [Fact]
    public void Escape_FromAwaitInput_Cancels() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        bool cancelled = false;
        sm.Cancelled += () => cancelled = true;

        sm.OnKey(VKey.Escape);

        Assert.True(cancelled);
    }

    [Fact]
    public void Escape_FromHorizSet_ClearsToAwaitInput() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        sm.OnKey(VKey.A);
        Assert.Equal(CrosshairStateMachine.State.HorizSet, sm.CurrentState);

        sm.OnKey(VKey.Escape);
        Assert.Equal(CrosshairStateMachine.State.AwaitInput, sm.CurrentState);
    }

    [Fact]
    public void Escape_FromVertSet_ClearsToAwaitInput() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        sm.OnKey(VKey.Q);
        Assert.Equal(CrosshairStateMachine.State.VertSet, sm.CurrentState);

        sm.OnKey(VKey.Escape);
        Assert.Equal(CrosshairStateMachine.State.AwaitInput, sm.CurrentState);
    }

    [Fact]
    public void Escape_FromBothSet_ClearsLastSetAxis_HorizFirst() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        sm.OnKey(VKey.A); // horiz first
        sm.OnKey(VKey.Q); // vert second (last set)
        Assert.Equal(CrosshairStateMachine.State.BothSet, sm.CurrentState);

        sm.OnKey(VKey.Escape);
        // LIFO: vert was last set → clears vert → keeps horiz
        Assert.Equal(CrosshairStateMachine.State.HorizSet, sm.CurrentState);
    }

    [Fact]
    public void Escape_FromBothSet_ClearsLastSetAxis_VertFirst() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        sm.OnKey(VKey.Q); // vert first
        sm.OnKey(VKey.A); // horiz second (last set)
        Assert.Equal(CrosshairStateMachine.State.BothSet, sm.CurrentState);

        sm.OnKey(VKey.Escape);
        // LIFO: horiz was last set → clears horiz → keeps vert
        Assert.Equal(CrosshairStateMachine.State.VertSet, sm.CurrentState);
    }

    // --- Action key ---

    [Fact]
    public void ActionKey_FiresActionRequested() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        Point? actionPoint = null;
        MouseAction? actionType = null;
        sm.ActionRequested += (pt, act) => { actionPoint = pt; actionType = act; };

        sm.OnKey(VKey.Space);

        Assert.Equal(new Point(550, 550), actionPoint);
        Assert.Equal(MouseAction.LeftClick, actionType);
    }

    [Fact]
    public void ActionKey_AfterAxisSelection_UsesUpdatedPoint() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        Point? actionPoint = null;
        sm.ActionRequested += (pt, _) => actionPoint = pt;

        sm.OnKey(VKey.A); // Select col 0
        sm.OnKey(VKey.Space);

        // Action point should be center of (centerRow, col 0) cell
        var expectedCell = grid.CellAt(grid.CenterRow, 0);
        var expected = CrosshairGridCalculator.CenterOf(expectedCell);
        Assert.Equal(expected, actionPoint);
    }

    // --- Arrow navigation ---

    [Fact]
    public void Arrow_MovesOnCross() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        var moves = new List<(int Row, int Col)>();
        sm.ArrowMoved += (r, c) => moves.Add((r, c));

        sm.OnKey(VKey.Right);
        // From center (5,5) → right on center row → (5,6)
        Assert.Equal((5, 6), moves[0]);
    }

    [Fact]
    public void Arrow_DisabledWhenArrowKeysFalse() {
        var sm = CreateSM(arrowKeys: false);
        sm.Activate(CreateGrid(), new Point(550, 550));

        bool invalidFired = false;
        sm.InvalidKeyPressed += () => invalidFired = true;

        sm.OnKey(VKey.Right);

        // Arrow key not handled → falls through to invalid
        Assert.True(invalidFired);
    }

    [Fact]
    public void Arrow_NotAvailableAfterAxisKey() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        sm.OnKey(VKey.A); // HorizSet

        bool invalidFired = false;
        sm.InvalidKeyPressed += () => invalidFired = true;
        var moved = new List<(int, int)>();
        sm.ArrowMoved += (r, c) => moved.Add((r, c));

        sm.OnKey(VKey.Right);

        // Arrow keys disabled after axis key (only in AwaitInput)
        Assert.True(invalidFired);
        Assert.Empty(moved);
    }

    // --- Invalid key ---

    [Fact]
    public void UnknownKey_FiresInvalidKeyPressed() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));

        bool fired = false;
        sm.InvalidKeyPressed += () => fired = true;

        sm.OnKey(VKey.Z);

        Assert.True(fired);
    }

    // --- Reset ---

    [Fact]
    public void Reset_SetsIdle() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.A);

        sm.Reset();

        Assert.Equal(CrosshairStateMachine.State.Idle, sm.CurrentState);
    }

    // --- Enter after arrow navigation (#3) ---

    [Fact]
    public void Enter_AfterArrowNav_UsesArrowPosition() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        GridCell? parentCell = null;
        sm.SubgridEntered += (cell, _) => parentCell = cell;

        // Move right from center (5,5) → (5,6)
        sm.OnKey(VKey.Right);
        sm.OnKey(VKey.Return);

        Assert.NotNull(parentCell);
        Assert.Equal(5, parentCell.Value.Row); // center row
        Assert.Equal(6, parentCell.Value.Col); // one right of center
    }

    // --- EnterSubgrid disabled path (#4) ---

    [Fact]
    public void Enter_CellTooSmallForSubgrid_PositionsCursorAndAwaitAction() {
        var actionMapper = new ActionMapper(new Dictionary<string, MouseAction>());
        // minCellPx = 2000 ensures any sub-cell would be too small
        var sm = new CrosshairStateMachine(HorizKeys, VertKeys, actionMapper, true, minCellPx: 2000);
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        GridCell? selectedCell = null;
        sm.CellSelected += cell => selectedCell = cell;

        GridCell? subgridCell = null;
        sm.SubgridEntered += (cell, _) => subgridCell = cell;

        sm.OnKey(VKey.Return);

        // Subgrid disabled → fires CellSelected, transitions to BothSet, awaits action key
        Assert.Null(subgridCell);
        Assert.NotNull(selectedCell);
        Assert.Equal(CrosshairStateMachine.State.BothSet, sm.CurrentState);
    }
}
