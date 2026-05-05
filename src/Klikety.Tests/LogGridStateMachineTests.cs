using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public class LogGridStateMachineTests {
    static readonly VKey[] HorizKeys = [
        VKey.A, VKey.S, VKey.D, VKey.F, VKey.G,
        VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon,
    ];

    static readonly VKey[] VertKeys = [
        VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T,
        VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P,
    ];

    static LogGridStateMachine CreateSM(bool arrowKeys = true) {
        var actionMapper = new ActionMapper(new Dictionary<string, MouseAction>());
        return new LogGridStateMachine(HorizKeys, VertKeys, actionMapper, arrowKeys);
    }

    static LogGrid CreateGrid(Point? center = null) {
        var c = center ?? new Point(550, 550);
        return LogScaleGridCalculator.Calculate(
            c, new Rectangle(0, 0, 1100, 1100), logBaseSize: 10,
            HorizKeys.Length, VertKeys.Length);
    }

    // --- Basic state transitions ---

    [Fact]
    public void AfterActivate_StateIsAwaitInput() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        Assert.Equal(LogGridStateMachine.State.AwaitInput, sm.CurrentState);
    }

    [Fact]
    public void FirstKey_TransitionsToFirstKeySet() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.A);
        Assert.Equal(LogGridStateMachine.State.FirstKeySet, sm.CurrentState);
    }

    [Fact]
    public void SecondKey_InFirstKeySet_TransitionsToPostTwoKeyRecenter() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.A); // first key (col)
        sm.OnKey(VKey.Q); // second key (row)
        Assert.Equal(LogGridStateMachine.State.PostTwoKeyRecenter, sm.CurrentState);
    }

    [Fact]
    public void SecondKey_InAwaitInput_FiresInvalid() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        bool invalid = false;
        sm.InvalidKeyPressed += () => invalid = true;
        sm.OnKey(VKey.Q); // vert key in AwaitInput without first key
        Assert.True(invalid);
    }

    [Fact]
    public void ArrowKey_InAwaitInput_TransitionsToArrowCellSet() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.Right);
        Assert.Equal(LogGridStateMachine.State.ArrowCellSet, sm.CurrentState);
    }

    [Fact]
    public void ArrowKey_InFirstKeySet_FiresInvalid() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.A); // FirstKeySet
        bool invalid = false;
        sm.InvalidKeyPressed += () => invalid = true;
        sm.OnKey(VKey.Right); // arrow in FirstKeySet
        Assert.True(invalid);
    }

    [Fact]
    public void ArrowKey_InPostTwoKeyRecenter_TransitionsToArrowCellSet() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q);
        // Now in PostTwoKeyRecenter — give it a fresh grid (simulating recenter)
        sm.UpdateGrid(CreateGrid(new Point(100, 100)));
        // Now in AwaitInput after UpdateGrid
        sm.OnKey(VKey.Down);
        Assert.Equal(LogGridStateMachine.State.ArrowCellSet, sm.CurrentState);
    }

    // --- Events ---

    [Fact]
    public void FirstKey_FiresFirstKeySelected() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        int? selectedCol = null;
        sm.FirstKeySelected += col => selectedCol = col;

        sm.OnKey(VKey.A); // key index 0 → col 0

        Assert.Equal(0, selectedCol);
    }

    [Fact]
    public void SecondKey_FiresCellSelected() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        GridCell? selectedCell = null;
        sm.CellSelected += cell => selectedCell = cell;

        sm.OnKey(VKey.D); // col 2
        sm.OnKey(VKey.R); // row 3

        Assert.NotNull(selectedCell);
        Assert.Equal(3, selectedCell.Value.Row);
        Assert.Equal(2, selectedCell.Value.Col);
    }

    [Fact]
    public void ArrowKey_FiresArrowMoved() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        int? movedRow = null, movedCol = null;
        sm.ArrowMoved += (row, col) => { movedRow = row; movedCol = col; };

        sm.OnKey(VKey.Right);

        Assert.NotNull(movedRow);
        Assert.NotNull(movedCol);
        // Started at center (rows/2=5, cols/2=5), moved right → col 6
        Assert.Equal(5, movedRow);
        Assert.Equal(6, movedCol);
    }

    // --- Enter behavior ---

    [Fact]
    public void Enter_InArrowCellSet_FiresArrowRecenterRequested() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.Right); // ArrowCellSet
        GridCell? recenterCell = null;
        sm.ArrowRecenterRequested += cell => recenterCell = cell;

        sm.OnKey(VKey.Return);

        Assert.NotNull(recenterCell);
        Assert.Equal(LogGridStateMachine.State.PostTwoKeyRecenter, sm.CurrentState);
    }

    [Fact]
    public void Enter_InPostTwoKeyRecenter_IsIgnored() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q); // PostTwoKeyRecenter
        bool anyEvent = false;
        sm.InvalidKeyPressed += () => anyEvent = true;
        sm.ArrowRecenterRequested += _ => anyEvent = true;

        sm.OnKey(VKey.Return);

        Assert.False(anyEvent);
        Assert.Equal(LogGridStateMachine.State.PostTwoKeyRecenter, sm.CurrentState);
    }

    [Fact]
    public void Enter_InAwaitInput_FiresInvalid() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        bool invalid = false;
        sm.InvalidKeyPressed += () => invalid = true;

        sm.OnKey(VKey.Return);

        Assert.True(invalid);
    }

    // --- Escape ---

    [Fact]
    public void Escape_FromAnyState_FiresCancelled() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        bool cancelled = false;
        sm.Cancelled += () => cancelled = true;

        sm.OnKey(VKey.Escape);
        Assert.True(cancelled);
    }

    [Fact]
    public void Escape_FromFirstKeySet_FiresCancelled() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.A); // FirstKeySet
        bool cancelled = false;
        sm.Cancelled += () => cancelled = true;

        sm.OnKey(VKey.Escape);
        Assert.True(cancelled);
    }

    [Fact]
    public void Escape_FromArrowCellSet_FiresCancelled() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.Right); // ArrowCellSet
        bool cancelled = false;
        sm.Cancelled += () => cancelled = true;

        sm.OnKey(VKey.Escape);
        Assert.True(cancelled);
    }

    // --- Action keys ---

    [Fact]
    public void ActionKey_FromAwaitInput_FiresActionRequested() {
        var actionMapper = new ActionMapper(
            new Dictionary<string, MouseAction> { ["Space"] = MouseAction.LeftClick });
        var sm = new LogGridStateMachine(HorizKeys, VertKeys, actionMapper, true);
        sm.Activate(CreateGrid(), new Point(550, 550));

        Point? actionPt = null;
        MouseAction? action = null;
        sm.ActionRequested += (pt, a) => { actionPt = pt; action = a; };

        sm.OnKey(VKey.Space);

        Assert.NotNull(actionPt);
        Assert.Equal(MouseAction.LeftClick, action);
        // Action point should be the origin (no selection yet)
        Assert.Equal(new Point(550, 550), actionPt);
    }

    [Fact]
    public void ActionKey_AfterFirstKey_UsesColumnCenter() {
        var actionMapper = new ActionMapper(
            new Dictionary<string, MouseAction> { ["Space"] = MouseAction.LeftClick });
        var sm = new LogGridStateMachine(HorizKeys, VertKeys, actionMapper, true);
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        Point? actionPt = null;
        sm.ActionRequested += (pt, _) => actionPt = pt;

        sm.OnKey(VKey.A); // col 0
        sm.OnKey(VKey.Space);

        Assert.NotNull(actionPt);
        // Should be at center of cell at (rows/2, col=0)
        var expectedCell = grid.CellAt(grid.Rows / 2, 0);
        var expected = new Point(
            expectedCell.Bounds.X + expectedCell.Bounds.Width / 2,
            expectedCell.Bounds.Y + expectedCell.Bounds.Height / 2);
        Assert.Equal(expected, actionPt);
    }

    // --- Arrow edge clamping ---

    [Fact]
    public void ArrowLeft_AtLeftEdge_DoesNotMove() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        // Move to left edge
        for (int i = 0; i < grid.Cols; i++) {
            sm.OnKey(VKey.Left);
        }

        int? lastCol = null;
        sm.ArrowMoved += (_, col) => lastCol = col;
        sm.OnKey(VKey.Left); // should not move

        Assert.Null(lastCol); // no event fired
    }

    [Fact]
    public void ArrowRight_AtRightEdge_DoesNotMove() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        for (int i = 0; i < grid.Cols; i++) {
            sm.OnKey(VKey.Right);
        }

        int? lastCol = null;
        sm.ArrowMoved += (_, col) => lastCol = col;
        sm.OnKey(VKey.Right);

        Assert.Null(lastCol);
    }

    // --- Arrow disabled ---

    [Fact]
    public void ArrowKey_WhenDisabled_FiresInvalid() {
        var sm = CreateSM(arrowKeys: false);
        sm.Activate(CreateGrid(), new Point(550, 550));
        bool invalid = false;
        sm.InvalidKeyPressed += () => invalid = true;

        sm.OnKey(VKey.Right);

        Assert.True(invalid);
    }

    // --- UpdateGrid resets state ---

    [Fact]
    public void UpdateGrid_ResetsToAwaitInput() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.A); // FirstKeySet
        sm.UpdateGrid(CreateGrid(new Point(100, 100)));
        Assert.Equal(LogGridStateMachine.State.AwaitInput, sm.CurrentState);
    }

    // --- Invalid key ---

    [Fact]
    public void UnknownKey_FiresInvalidKeyPressed() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        bool invalid = false;
        sm.InvalidKeyPressed += () => invalid = true;

        sm.OnKey(VKey.Tab); // not a navigation key

        Assert.True(invalid);
    }

    // --- First key re-press updates column ---

    [Fact]
    public void FirstKey_RePress_UpdatesColumn() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        int lastCol = -1;
        sm.FirstKeySelected += col => lastCol = col;

        sm.OnKey(VKey.A); // col 0
        Assert.Equal(0, lastCol);

        sm.OnKey(VKey.S); // col 1 — re-press first key
        Assert.Equal(1, lastCol);
        Assert.Equal(LogGridStateMachine.State.FirstKeySet, sm.CurrentState);
    }

    // --- Two-key recenter loop ---

    [Fact]
    public void TwoKeyRecenter_AfterUpdateGrid_AcceptsNewFirstKey() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        sm.OnKey(VKey.A); // first key
        sm.OnKey(VKey.Q); // second key → PostTwoKeyRecenter

        // Simulate recenter
        sm.UpdateGrid(CreateGrid(new Point(200, 200)));
        Assert.Equal(LogGridStateMachine.State.AwaitInput, sm.CurrentState);

        // New first key
        int? col = null;
        sm.FirstKeySelected += c => col = c;
        sm.OnKey(VKey.D); // col 2
        Assert.Equal(2, col);
        Assert.Equal(LogGridStateMachine.State.FirstKeySet, sm.CurrentState);
    }

    // --- Idle state ignores keys ---

    [Fact]
    public void OnKey_InIdleState_DoesNothing() {
        var sm = CreateSM();
        bool anyEvent = false;
        sm.FirstKeySelected += _ => anyEvent = true;
        sm.InvalidKeyPressed += () => anyEvent = true;
        sm.Cancelled += () => anyEvent = true;

        sm.OnKey(VKey.A);

        Assert.False(anyEvent);
        Assert.Equal(LogGridStateMachine.State.Idle, sm.CurrentState);
    }

    // --- Reset ---

    [Fact]
    public void Reset_ReturnsToIdle() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.A);
        sm.Reset();
        Assert.Equal(LogGridStateMachine.State.Idle, sm.CurrentState);
    }
}
