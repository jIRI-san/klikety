using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

/// <summary>
/// Integration/contract tests for CrosshairSession (SM + renderer + events).
/// </summary>
public class CrosshairSessionTests {
    private static readonly VKey[] HorizKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon];
    private static readonly VKey[] VertKeys = [VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P];

    private static (CrosshairSession Session, FakeCrosshairRenderer Renderer) Create(bool arrowKeys = true) {
        var renderer = new FakeCrosshairRenderer();
        var mode = new ModeConfig { ArrowKeys = arrowKeys, TwoKey = true };
        var actionMapper = new ActionMapper(new Dictionary<string, MouseAction>());
        var session = new CrosshairSession(HorizKeys, VertKeys, actionMapper, mode, renderer);
        return (session, renderer);
    }

    [Fact]
    public void Activate_RendersInitialCross() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        Assert.Single(renderer.Calls);
        Assert.Equal("RenderCross", renderer.Calls[0].Method);
    }

    [Fact]
    public void HorizKey_FiresCursorMove_AndHighlightsColumn() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        Point? cursorTarget = null;
        session.CursorMoveRequested += pt => cursorTarget = pt;

        session.OnKey(VKey.A);

        Assert.NotNull(cursorTarget);
        Assert.Equal("HighlightColumn", renderer.Calls[^1].Method);
    }

    [Fact]
    public void VertKey_FiresCursorMove_AndHighlightsRow() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        Point? cursorTarget = null;
        session.CursorMoveRequested += pt => cursorTarget = pt;

        session.OnKey(VKey.Q);

        Assert.NotNull(cursorTarget);
        Assert.Equal("HighlightRow", renderer.Calls[^1].Method);
    }

    [Fact]
    public void BothAxes_EntersL2_RoutesKeysToL2() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        var actions = new List<(Point, MouseAction)>();
        session.ActionRequested += (pt, act) => actions.Add((pt, act));

        // Two axis keys → auto-enters L2
        session.OnKey(VKey.A);
        session.OnKey(VKey.Q);

        // Now in L2 — action key fires from L2
        session.OnKey(VKey.Space);

        Assert.Single(actions);
    }

    [Fact]
    public void ActionKey_FiresActionRequested() {
        var (session, _) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        Point? pt = null;
        MouseAction? action = null;
        session.ActionRequested += (p, a) => { pt = p; action = a; };

        session.OnKey(VKey.Space);

        Assert.Equal(new Point(550, 550), pt);
        Assert.Equal(MouseAction.LeftClick, action);
    }

    [Fact]
    public void Escape_FiresCancelled_RestoresOrigin() {
        var (session, _) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        bool cancelled = false;
        Point? cursorTarget = null;
        session.Cancelled += () => cancelled = true;
        session.CursorMoveRequested += pt => cursorTarget = pt;

        session.OnKey(VKey.Escape);

        Assert.True(cancelled);
        Assert.Equal(new Point(550, 550), cursorTarget);
    }

    [Fact]
    public void ArrowNav_FiresCursorMove_HighlightsCell() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        Point? cursorTarget = null;
        session.CursorMoveRequested += pt => cursorTarget = pt;

        session.OnKey(VKey.Right);

        Assert.NotNull(cursorTarget);
        Assert.Equal("HighlightCell", renderer.Calls[^1].Method);
    }

    [Fact]
    public void ArrowNav_HighlightsCellAtShiftedPosition() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        // Arrow right from center should shift to col+1
        session.OnKey(VKey.Right);

        var lastCall = renderer.Calls[^1];
        Assert.Equal("HighlightCell", lastCall.Method);
        Assert.NotNull(lastCall.Cell);
        // Grid is 11×11, center at (5,5). Right → (5,6)
        Assert.Equal(5, lastCall.Cell!.Value.Row);
        Assert.Equal(6, lastCall.Cell.Value.Col);
    }

    [Fact]
    public void ArrowKey_AfterHorizSet_Ignored() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.A); // Horiz set

        session.OnKey(VKey.Right); // Arrow should be ignored (not in AwaitInput)

        // No HighlightCell call — arrow didn't trigger navigation
        Assert.DoesNotContain(renderer.Calls, c => c.Method == "HighlightCell");
    }

    [Fact]
    public void ArrowKey_AfterVertSet_Ignored() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.Q); // Vert set

        session.OnKey(VKey.Down); // Arrow should be ignored (not in AwaitInput)

        Assert.DoesNotContain(renderer.Calls, c => c.Method == "HighlightCell");
    }

    [Fact]
    public void HorizKey_RendererReceivesCorrectColumn() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        // Key A is index 0, maps to col 0 (grid center col is 5, key 0 < 5 → col 0)
        session.OnKey(VKey.A);

        var lastCall = renderer.Calls[^1];
        Assert.Equal("HighlightColumn", lastCall.Method);
        Assert.Equal(0, lastCall.Col);
    }

    [Fact]
    public void VertKey_RendererReceivesCorrectRow() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        // Key Q is index 0, maps to row 0 (grid center row is 5, key 0 < 5 → row 0)
        session.OnKey(VKey.Q);

        var lastCall = renderer.Calls[^1];
        Assert.Equal("HighlightRow", lastCall.Method);
        Assert.Equal(0, lastCall.Row);
    }

    [Fact]
    public void Deactivate_DisconnectsEvents() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.Deactivate();

        // After deactivate, keys should not fire events
        bool eventFired = false;
        session.CursorMoveRequested += _ => eventFired = true;
        session.ActionRequested += (_, _) => eventFired = true;
        session.Cancelled += () => eventFired = true;

        session.OnKey(VKey.A);
        session.OnKey(VKey.Space);
        session.OnKey(VKey.Escape);

        Assert.False(eventFired);
    }

    [Fact]
    public void InvalidKey_FlashesRenderer() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.Z);

        Assert.Equal("FlashInvalidKey", renderer.Calls[^1].Method);
    }

    // --- Custom axis keys (small set) ---

    [Fact]
    public void SmallAxisKeys_CreatesSmallerGrid() {
        var renderer = new FakeCrosshairRenderer();
        var mode = new ModeConfig { ArrowKeys = true, TwoKey = true };
        var actionMapper = new ActionMapper(new Dictionary<string, MouseAction>());
        VKey[] smallHoriz = [VKey.A, VKey.S, VKey.D, VKey.F];
        VKey[] smallVert = [VKey.Q, VKey.W, VKey.E, VKey.R];
        var session = new CrosshairSession(smallHoriz, smallVert, actionMapper, mode, renderer);

        session.Activate(new Rectangle(0, 0, 500, 500), new Point(250, 250));

        // Grid should be 5×5 (4+1 cols, 4+1 rows)
        Assert.Equal("RenderCross", renderer.Calls[0].Method);
        var grid = renderer.Calls[0].Grid;
        Assert.NotNull(grid);
        Assert.Equal(5, grid!.Cols);
        Assert.Equal(5, grid.Rows);
    }

    // --- Enter with subgrid ---

    [Fact]
    public void Enter_EntersL2_RoutesKeysToL2() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        var actions = new List<(Point, MouseAction)>();
        session.ActionRequested += (pt, act) => actions.Add((pt, act));

        session.OnKey(VKey.Return); // Enter at AwaitInput → L2

        // Now in L2 — action key fires
        session.OnKey(VKey.Space);

        Assert.Single(actions);
    }

    // --- Origin restore after axis selection + Escape (#8) ---

    [Fact]
    public void Escape_AfterAxisSelection_RestoresOriginalOrigin() {
        var (session, _) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.A); // Changes cursor to horiz-selected cell

        Point? lastCursor = null;
        session.CursorMoveRequested += pt => lastCursor = pt;

        session.OnKey(VKey.Escape); // Clear axis → back to AwaitInput
        session.OnKey(VKey.Escape); // Cancel from AwaitInput → restore origin

        Assert.Equal(new Point(550, 550), lastCursor);
    }

    // --- Factory wiring (#8) ---

    [Fact]
    public void ModeSessionFactory_CreatesCrosshairSession() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                Crosshair = new ModeConfig {
                    Enabled = true, ChordKey = VKey.N, ArrowKeys = true, TwoKey = true,
                },
            },
        };
        var actionMapper = new ActionMapper(config.ActionBindings);
        var factory = new ModeSessionFactory(config, actionMapper, null, null);

        var session = factory.Create("Crosshair");

        Assert.IsType<CrosshairSession>(session);
    }

    // --- L2 level stack integration tests (step 4.4) ---

    [Fact]
    public void L2_TwoAxisKeys_ThenL2Action_FiresAction() {
        var (session, _) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        var actions = new List<(Point Pt, MouseAction Act)>();
        session.ActionRequested += (pt, act) => actions.Add((pt, act));

        // L1: two axis keys → auto-enters L2
        session.OnKey(VKey.A); // horiz col 0
        session.OnKey(VKey.Q); // vert row 0 → L2

        // L2: two-key sequence → fires action
        session.OnKey(VKey.A); // L2 first key
        session.OnKey(VKey.Q); // L2 second key → action
        session.OnKey(VKey.Space); // action key at L2 position

        Assert.Single(actions);
    }

    [Fact]
    public void L2_EscapeFromL2_ReturnsToL1_RendersCrossAndHighlightsCell() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        // Enter L2
        session.OnKey(VKey.A);
        session.OnKey(VKey.Q);

        // Escape from L2 → back to L1
        session.OnKey(VKey.Escape);

        // Should render full cross then highlight the arrow-position cell
        var lastCalls = renderer.Calls.TakeLast(2).ToList();
        Assert.Equal("RenderCross", lastCalls[0].Method);
        Assert.Equal("HighlightCell", lastCalls[1].Method);
    }

    [Fact]
    public void L2_EscapeFromL2_CursorMovesToCellCenter() {
        var (session, _) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        var cursorMoves = new List<Point>();
        session.CursorMoveRequested += pt => cursorMoves.Add(pt);

        // Enter L2
        session.OnKey(VKey.A);
        session.OnKey(VKey.Q);

        cursorMoves.Clear();

        // Escape from L2
        session.OnKey(VKey.Escape);

        // Cursor should end at the L2-entry cell center
        Assert.NotEmpty(cursorMoves);
        // All moves should be to the same cell center
        Assert.All(cursorMoves, pt => Assert.Equal(cursorMoves[0], pt));
    }

    [Fact]
    public void L2_EscapeFromL2_ThenAxisKey_WorksImmediately() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        // Enter L2
        session.OnKey(VKey.A);
        session.OnKey(VKey.Q);

        // Escape from L2 → back to L1 (AwaitInput)
        session.OnKey(VKey.Escape);

        // Axis key should work on L1 immediately (SM is in AwaitInput)
        session.OnKey(VKey.S); // horiz key → HorizSet

        Assert.Equal("HighlightColumn", renderer.Calls[^1].Method);
    }

    [Fact]
    public void L2_EscapeFromL2_ThenArrow_WorksImmediately() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        // Enter L2
        session.OnKey(VKey.A);
        session.OnKey(VKey.Q);

        // Escape from L2 → back to L1 (AwaitInput)
        session.OnKey(VKey.Escape);

        // Arrow should work immediately
        session.OnKey(VKey.Right);

        Assert.Equal("HighlightCell", renderer.Calls[^1].Method);
    }

    [Fact]
    public void L2_EscapeFromL2_ThenReenterL2_Works() {
        var (session, _) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        // Enter L2
        session.OnKey(VKey.A);
        session.OnKey(VKey.Q);

        // Escape from L2
        session.OnKey(VKey.Escape);

        // Re-enter L2 with new axis keys
        session.OnKey(VKey.S);
        session.OnKey(VKey.W);

        // Should be back in L2 — action fires from L2
        var actions = new List<(Point, MouseAction)>();
        session.ActionRequested += (pt, act) => actions.Add((pt, act));
        session.OnKey(VKey.Space);
        Assert.Single(actions);
    }

    [Fact]
    public void L2_CursorMoveRequested_BubblesFromL2() {
        var (session, _) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        var cursorMoves = new List<Point>();
        session.CursorMoveRequested += pt => cursorMoves.Add(pt);

        // Enter L2
        session.OnKey(VKey.A);
        session.OnKey(VKey.Q);

        int movesAfterL2Entry = cursorMoves.Count;

        // L2: select horiz axis (G is in reduced L2 key set) → cursor move
        session.OnKey(VKey.G);

        Assert.True(cursorMoves.Count > movesAfterL2Entry);
    }
}
