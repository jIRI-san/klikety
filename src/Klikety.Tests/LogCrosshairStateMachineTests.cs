using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public class LogCrosshairStateMachineTests {
    static readonly VKey[] HorizKeys = [
        VKey.A, VKey.S, VKey.D, VKey.F, VKey.G,
        VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon,
    ];

    static readonly VKey[] VertKeys = [
        VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T,
        VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P,
    ];

    static LogCrosshairStateMachine CreateSM(bool arrowKeys = true) {
        var actionMapper = new ActionMapper(new Dictionary<string, MouseAction>());
        return new LogCrosshairStateMachine(HorizKeys, VertKeys, actionMapper, arrowKeys);
    }

    static LogCrosshairGrid CreateGrid(Point? center = null) {
        var c = center ?? new Point(550, 550);
        return LogGridCalculator.Calculate(
            c, new Rectangle(0, 0, 1100, 1100), logBaseSize: 5,
            HorizKeys.Length, VertKeys.Length);
    }

    // --- Basic state transitions ---

    [Fact]
    public void AfterActivate_StateIsAwaitInput() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        Assert.Equal(LogCrosshairStateMachine.State.AwaitInput, sm.CurrentState);
    }

    [Fact]
    public void HorizKey_TransitionsToHorizSet() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.A);
        Assert.Equal(LogCrosshairStateMachine.State.HorizSet, sm.CurrentState);
    }

    [Fact]
    public void VertKey_TransitionsToVertSet() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.Q);
        Assert.Equal(LogCrosshairStateMachine.State.VertSet, sm.CurrentState);
    }

    [Fact]
    public void HorizThenVert_BothSet_TransitionsToBothSet() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.F); // horiz
        sm.OnKey(VKey.R); // vert
        Assert.Equal(LogCrosshairStateMachine.State.BothSet, sm.CurrentState);
    }

    [Fact]
    public void VertThenHoriz_BothSet_TransitionsToBothSet() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.R); // vert
        sm.OnKey(VKey.F); // horiz
        Assert.Equal(LogCrosshairStateMachine.State.BothSet, sm.CurrentState);
    }

    // --- Events ---

    [Fact]
    public void HorizKey_FiresHorizSelected() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        int? selectedCol = null;
        sm.HorizSelected += (col, _) => selectedCol = col;

        sm.OnKey(VKey.A); // key index 0 → col 0

        Assert.Equal(0, selectedCol);
    }

    [Fact]
    public void VertKey_FiresVertSelected() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        int? selectedRow = null;
        sm.VertSelected += (row, _) => selectedRow = row;

        sm.OnKey(VKey.Q); // key index 0 → row 0

        Assert.Equal(0, selectedRow);
    }

    [Fact]
    public void BothAxes_FiresSubgridEntered() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        GridCell? cell = null;
        sm.SubgridEntered += c => cell = c;

        sm.OnKey(VKey.F); // horiz
        sm.OnKey(VKey.R); // vert → both set → auto-L2

        Assert.NotNull(cell);
    }

    // --- Same-axis re-press ---

    [Fact]
    public void SameHorizRePress_UpdatesColumn() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        int lastCol = -1;
        sm.HorizSelected += (col, _) => lastCol = col;

        sm.OnKey(VKey.A); // col 0
        Assert.Equal(0, lastCol);

        sm.OnKey(VKey.S); // col 1
        Assert.Equal(1, lastCol);
    }

    // --- LIFO Escape ---

    [Fact]
    public void Escape_FromHorizOnly_GoesToAwaitInput() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.F); // HorizSet
        sm.OnKey(VKey.Escape);
        Assert.Equal(LogCrosshairStateMachine.State.AwaitInput, sm.CurrentState);
    }

    [Fact]
    public void Escape_FromVertOnly_GoesToAwaitInput() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.R); // VertSet
        sm.OnKey(VKey.Escape);
        Assert.Equal(LogCrosshairStateMachine.State.AwaitInput, sm.CurrentState);
    }

    [Fact]
    public void Escape_FromBothSet_ClearsLastSetAxis_HorizLast() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.R); // vert first
        sm.OnKey(VKey.F); // horiz last → BothSet
        Assert.Equal(LogCrosshairStateMachine.State.BothSet, sm.CurrentState);

        sm.OnKey(VKey.Escape); // clears horiz (LIFO)
        Assert.Equal(LogCrosshairStateMachine.State.VertSet, sm.CurrentState);
    }

    [Fact]
    public void Escape_FromBothSet_ClearsLastSetAxis_VertLast() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.F); // horiz first
        sm.OnKey(VKey.R); // vert last → BothSet
        Assert.Equal(LogCrosshairStateMachine.State.BothSet, sm.CurrentState);

        sm.OnKey(VKey.Escape); // clears vert (LIFO)
        Assert.Equal(LogCrosshairStateMachine.State.HorizSet, sm.CurrentState);
    }

    [Fact]
    public void Escape_FromAwaitInput_FiresCancelled() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        bool cancelled = false;
        sm.Cancelled += () => cancelled = true;

        sm.OnKey(VKey.Escape);
        Assert.True(cancelled);
    }

    // --- Action keys ---

    [Fact]
    public void ActionKey_FiresActionRequested() {
        var actionMapper = new ActionMapper(
            new Dictionary<string, MouseAction> { ["Space"] = MouseAction.LeftClick });
        var sm = new LogCrosshairStateMachine(HorizKeys, VertKeys, actionMapper, true);
        sm.Activate(CreateGrid(), new Point(550, 550));

        Point? actionPt = null;
        MouseAction? action = null;
        sm.ActionRequested += (pt, a) => { actionPt = pt; action = a; };

        sm.OnKey(VKey.Space);

        Assert.NotNull(actionPt);
        Assert.Equal(MouseAction.LeftClick, action);
    }

    // --- Enter behavior ---

    [Fact]
    public void Enter_WithNoAxis_EntersSubgridOrCellSelectedAtCenter() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        bool entered = false;
        sm.SubgridEntered += _ => entered = true;
        sm.CellSelected += _ => entered = true;

        sm.OnKey(VKey.Return);

        Assert.True(entered);
        Assert.Equal(LogCrosshairStateMachine.State.BothSet, sm.CurrentState);
    }

    [Fact]
    public void Enter_WithAxisSet_EntersSubgridOrCellSelectedAtCenterOfUnsetAxis() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.F); // HorizSet

        bool entered = false;
        sm.SubgridEntered += _ => entered = true;
        sm.CellSelected += _ => entered = true;

        sm.OnKey(VKey.Return);

        Assert.True(entered);
        Assert.Equal(LogCrosshairStateMachine.State.BothSet, sm.CurrentState);
    }

    // --- Arrow navigation ---

    [Fact]
    public void ArrowRight_InAwaitInput_MovesOnCross() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        int? movedRow = null, movedCol = null;
        sm.ArrowMoved += (r, c) => { movedRow = r; movedCol = c; };

        sm.OnKey(VKey.Right);

        Assert.NotNull(movedRow);
        Assert.Equal(5, movedRow); // center row stays
        Assert.Equal(6, movedCol); // one right
    }

    [Fact]
    public void Arrow_AfterAxisKey_IsInvalid() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.F); // HorizSet

        bool invalid = false;
        sm.InvalidKeyPressed += () => invalid = true;

        sm.OnKey(VKey.Right);
        Assert.True(invalid);
    }

    [Fact]
    public void Arrow_Disabled_FiresInvalid() {
        var sm = CreateSM(arrowKeys: false);
        sm.Activate(CreateGrid(), new Point(550, 550));

        bool invalid = false;
        sm.InvalidKeyPressed += () => invalid = true;

        sm.OnKey(VKey.Right);
        Assert.True(invalid);
    }

    // --- Invalid keys ---

    [Fact]
    public void UnknownKey_FiresInvalidKeyPressed() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        bool invalid = false;
        sm.InvalidKeyPressed += () => invalid = true;

        sm.OnKey(VKey.Tab);
        Assert.True(invalid);
    }

    // --- Degenerate cells ---

    [Fact]
    public void DegenerateCell_FiresInvalidKeyPressed() {
        var sm = CreateSM();
        // Grid with center very near left edge → left columns are degenerate
        var grid = LogGridCalculator.Calculate(
            new Point(2, 550), new Rectangle(0, 0, 1100, 1100),
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);
        sm.Activate(grid, new Point(2, 550));

        bool invalid = false;
        sm.InvalidKeyPressed += () => invalid = true;

        sm.OnKey(VKey.A); // leftmost key → likely degenerate column

        Assert.True(invalid);
    }

    // --- Reset ---

    [Fact]
    public void Reset_GoesToIdle() {
        var sm = CreateSM();
        sm.Activate(CreateGrid(), new Point(550, 550));
        sm.OnKey(VKey.F);

        sm.Reset();

        Assert.Equal(LogCrosshairStateMachine.State.Idle, sm.CurrentState);
    }

    [Fact]
    public void KeysInIdle_AreIgnored() {
        var sm = CreateSM();
        bool anyEvent = false;
        sm.HorizSelected += (_, _) => anyEvent = true;
        sm.InvalidKeyPressed += () => anyEvent = true;

        sm.OnKey(VKey.A);

        Assert.False(anyEvent);
    }

    // --- Constructor validation ---

    [Fact]
    public void NullHorizKeys_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new LogCrosshairStateMachine(null!, VertKeys, new ActionMapper([]), true));

    [Fact]
    public void NullVertKeys_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new LogCrosshairStateMachine(HorizKeys, null!, new ActionMapper([]), true));

    [Fact]
    public void OverlappingKeys_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            new LogCrosshairStateMachine(HorizKeys, HorizKeys, new ActionMapper([]), true));

    [Fact]
    public void TooManyKeys_Throws() {
        var tooMany = new VKey[53];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LogCrosshairStateMachine(tooMany, VertKeys, new ActionMapper([]), true));
    }

    // --- Fix #1: AxisCleared event ---

    [Fact]
    public void Escape_FromHorizOnly_FiresAxisCleared() {
        var sm = CreateSM();
    sm.Activate(CreateGrid(), new Point(550, 550));

    bool cleared = false;
        sm.AxisCleared += () => cleared = true;

    sm.OnKey(VKey.F); // HorizSet
    sm.OnKey(VKey.Escape);

        Assert.True(cleared);
        Assert.Equal(LogCrosshairStateMachine.State.AwaitInput, sm.CurrentState);
    }

    [Fact]
    public void Escape_FromVertOnly_FiresAxisCleared() {
        var sm = CreateSM();
    sm.Activate(CreateGrid(), new Point(550, 550));

    bool cleared = false;
        sm.AxisCleared += () => cleared = true;

    sm.OnKey(VKey.R); // VertSet
    sm.OnKey(VKey.Escape);

        Assert.True(cleared);
        Assert.Equal(LogCrosshairStateMachine.State.AwaitInput, sm.CurrentState);
    }

    // --- Fix #4: Action after both axes ---

    [Fact]
    public void ActionKey_AfterBothAxes_FiresAtCellCenter() {
        var actionMapper = new ActionMapper(
            new Dictionary<string, MouseAction> { ["Space"] = MouseAction.LeftClick });
        var sm = new LogCrosshairStateMachine(HorizKeys, VertKeys, actionMapper, true);
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        sm.OnKey(VKey.F); // horiz key index 3 → col 3
        sm.OnKey(VKey.R); // vert key index 3 → row 3

        Point? actionPt = null;
        sm.ActionRequested += (pt, _) => actionPt = pt;
        sm.OnKey(VKey.Space);

        Assert.NotNull(actionPt);
        // Should be at the center of cell(3,3), not at origin (550,550)
        var expectedCell = grid.CellAt(3, 3);
        Assert.Equal(LogGridCalculator.CenterOf(expectedCell), actionPt.Value);
    }

    // --- Fix #5: Arrow into degenerate cell ---

    [Fact]
    public void Arrow_IntoDegenerateCell_FiresInvalidKeyPressed() {
        var sm = CreateSM();
        // Grid with center at col=5 very near left edge → leftward cells degenerate
        var grid = LogGridCalculator.Calculate(
            new Point(2, 550), new Rectangle(0, 0, 1100, 1100),
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);
        sm.Activate(grid, new Point(2, 550));

        bool invalid = false;
        sm.InvalidKeyPressed += () => invalid = true;

        // Arrow left from center (center col = 5) repeatedly to hit degenerate
        for (int i = 0; i < 5; i++) {
            sm.OnKey(VKey.Left);
        }

        Assert.True(invalid);
    }

    // --- Fix #6: Arrow → axis → escape → action sequence ---

    [Fact]
    public void Arrow_ThenAxis_ThenEscape_ActionFiresAtCenterCellCenter() {
        var actionMapper = new ActionMapper(
            new Dictionary<string, MouseAction> { ["Space"] = MouseAction.LeftClick });
        var sm = new LogCrosshairStateMachine(HorizKeys, VertKeys, actionMapper, true);
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        // Arrow right (moves to center row, col 6)
        sm.OnKey(VKey.Right);

        // Select horiz axis
        sm.OnKey(VKey.F); // HorizSet

        // Escape back to AwaitInput
        sm.OnKey(VKey.Escape);
        Assert.Equal(LogCrosshairStateMachine.State.AwaitInput, sm.CurrentState);

        // Action fires at center cell center (not at arrow position or axis position)
        Point? actionPt = null;
        sm.ActionRequested += (pt, _) => actionPt = pt;
        sm.OnKey(VKey.Space);

        var expectedCenter = LogGridCalculator.CenterOf(grid.CenterCell);
        Assert.Equal(expectedCenter, actionPt);
    }

    // --- ResetToAwaitInput (L2 pop) tests ---

    [Fact]
    public void ResetToAwaitInput_SetsStateToAwaitInput() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        // Enter BothSet
        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q);
        Assert.Equal(LogCrosshairStateMachine.State.BothSet, sm.CurrentState);

        sm.ResetToAwaitInput(2, 3);

        Assert.Equal(LogCrosshairStateMachine.State.AwaitInput, sm.CurrentState);
    }

    [Fact]
    public void ResetToAwaitInput_FiresSubgridExited() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q);

        bool exited = false;
        sm.SubgridExited += () => exited = true;

        sm.ResetToAwaitInput(2, 3);

        Assert.True(exited);
    }

    [Fact]
    public void ResetToAwaitInput_ArrowWorksImmediately() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q);

        sm.ResetToAwaitInput(grid.CenterRow, grid.CenterCol);

        (int, int)? arrowResult = null;
        sm.ArrowMoved += (r, c) => arrowResult = (r, c);
        sm.OnKey(VKey.Right);

        Assert.NotNull(arrowResult);
    }

    [Fact]
    public void EnterSubgrid_WhenHandlerThrows_FallsBackToCellSelected() {
        var sm = CreateSM();
        var grid = CreateGrid();
        sm.Activate(grid, new Point(550, 550));

        GridCell? cellSelected = null;
        sm.CellSelected += cell => cellSelected = cell;
        sm.SubgridEntered += _ => throw new InvalidOperationException("L2 construction failed");

        sm.OnKey(VKey.A);
        sm.OnKey(VKey.Q);

        Assert.NotNull(cellSelected);
        Assert.Equal(LogCrosshairStateMachine.State.BothSet, sm.CurrentState);
    }
}
