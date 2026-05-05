using System.Drawing;

using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class LogGridSessionTests {
    static readonly VKey[] HorizKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon];
    static readonly VKey[] VertKeys = [VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P];

    static readonly Rectangle ScreenBounds = new(0, 0, 1100, 1100);
    static readonly Point Origin = new(550, 550);

    static (LogGridSession Session, FakeLogGridRenderer Renderer) Create(bool arrowKeys = true) {
        var renderer = new FakeLogGridRenderer();
        var mode = new ModeConfig { ArrowKeys = arrowKeys, TwoKey = true, LogGridBaseSize = 10 };
        var actionMapper = new ActionMapper(new Dictionary<string, MouseAction>());
        var session = new LogGridSession(HorizKeys, VertKeys, actionMapper, mode, renderer);
        return (session, renderer);
    }

    // --- Activation ---

    [Fact]
    public void Activate_RendersGrid() {
        var (session, renderer) = Create();
        session.Activate(ScreenBounds, Origin);

        Assert.Single(renderer.Calls);
        Assert.Equal("RenderGrid", renderer.Calls[0].Method);
        Assert.NotNull(renderer.Calls[0].Grid);
    }

    [Fact]
    public void Activate_GridDimensions() {
        var (session, renderer) = Create();
        session.Activate(ScreenBounds, Origin);

        var grid = renderer.Calls[0].Grid!;
        Assert.Equal(10, grid.Cols);
        Assert.Equal(10, grid.Rows);
    }

    // --- First key ---

    [Fact]
    public void FirstKey_HighlightsColumn() {
        var (session, renderer) = Create();
        session.Activate(ScreenBounds, Origin);

        session.OnKey(VKey.A); // col 0

        Assert.Contains(renderer.Calls, c => c.Method == "HighlightColumn" && c.Col == 0);
    }

    [Fact]
    public void FirstKey_RendersIndicator() {
        var (session, renderer) = Create();
        session.Activate(ScreenBounds, Origin);

        session.OnKey(VKey.A);

        Assert.Contains(renderer.Calls, c => c.Method == "RenderFirstKeyIndicator");
    }

    // --- Two-key selection ---

    [Fact]
    public void TwoKey_MovesCursor() {
        var (session, _) = Create();
        session.Activate(ScreenBounds, Origin);

        Point? cursor = null;
        session.CursorMoveRequested += pt => cursor = pt;

        session.OnKey(VKey.D); // col 2
        session.OnKey(VKey.R); // row 3

        Assert.NotNull(cursor);
    }

    [Fact]
    public void TwoKey_RecentersGrid() {
        var (session, renderer) = Create();
        session.Activate(ScreenBounds, Origin);

        session.OnKey(VKey.A); // first key
        session.OnKey(VKey.Q); // second key

        // After two-key, grid is recentered → new RenderGrid call
        var renderGridCalls = renderer.Calls.FindAll(c => c.Method == "RenderGrid");
        Assert.Equal(2, renderGridCalls.Count); // initial + recenter
    }

    [Fact]
    public void TwoKey_HidesIndicator() {
        var (session, renderer) = Create();
        session.Activate(ScreenBounds, Origin);

        session.OnKey(VKey.A);
        session.OnKey(VKey.Q);

        Assert.Contains(renderer.Calls, c => c.Method == "HideFirstKeyIndicator");
    }

    // --- Arrow navigation ---

    [Fact]
    public void ArrowKey_MovesCursorWithoutRecenter() {
        var (session, renderer) = Create();
        session.Activate(ScreenBounds, Origin);

        Point? cursor = null;
        session.CursorMoveRequested += pt => cursor = pt;

        session.OnKey(VKey.Right);

        Assert.NotNull(cursor);
        // Only one RenderGrid (initial) — no recenter
        var renderGridCalls = renderer.Calls.FindAll(c => c.Method == "RenderGrid");
        Assert.Single(renderGridCalls);
    }

    [Fact]
    public void ArrowKey_HighlightsCell() {
        var (session, renderer) = Create();
        session.Activate(ScreenBounds, Origin);

        session.OnKey(VKey.Right);

        Assert.Contains(renderer.Calls, c => c.Method == "HighlightCell");
    }

    // --- Enter on arrow ---

    [Fact]
    public void Enter_AfterArrow_Recenters() {
        var (session, renderer) = Create();
        session.Activate(ScreenBounds, Origin);

        session.OnKey(VKey.Right); // ArrowCellSet
        session.OnKey(VKey.Return); // recenter

        var renderGridCalls = renderer.Calls.FindAll(c => c.Method == "RenderGrid");
        Assert.Equal(2, renderGridCalls.Count); // initial + recenter
    }

    // --- Escape ---

    [Fact]
    public void Escape_RestoresOriginAndCancels() {
        var (session, _) = Create();
        session.Activate(ScreenBounds, Origin);

        Point? lastCursor = null;
        bool cancelled = false;
        session.CursorMoveRequested += pt => lastCursor = pt;
        session.Cancelled += () => cancelled = true;

        session.OnKey(VKey.A); // move cursor away
        session.OnKey(VKey.Escape);

        Assert.True(cancelled);
        Assert.Equal(Origin, lastCursor);
    }

    // --- Action key ---

    [Fact]
    public void ActionKey_FiresActionRequested() {
        var actionMapper = new ActionMapper(
            new Dictionary<string, MouseAction> { ["Space"] = MouseAction.LeftClick });
        var renderer = new FakeLogGridRenderer();
        var mode = new ModeConfig { ArrowKeys = true, TwoKey = true, LogGridBaseSize = 10 };
        var session = new LogGridSession(HorizKeys, VertKeys, actionMapper, mode, renderer);
        session.Activate(ScreenBounds, Origin);

        Point? actionPt = null;
        MouseAction? action = null;
        session.ActionRequested += (pt, a) => { actionPt = pt; action = a; };

        session.OnKey(VKey.Space);

        Assert.Equal(Origin, actionPt);
        Assert.Equal(MouseAction.LeftClick, action);
    }

    // --- Invalid key ---

    [Fact]
    public void InvalidKey_FlashesRenderer() {
        var (session, renderer) = Create();
        session.Activate(ScreenBounds, Origin);

        session.OnKey(VKey.Tab);

        Assert.Contains(renderer.Calls, c => c.Method == "FlashInvalidKey");
    }

    // --- Null renderer ---

    [Fact]
    public void NullRenderer_DoesNotThrow() {
        var mode = new ModeConfig { ArrowKeys = true, TwoKey = true, LogGridBaseSize = 10 };
        var actionMapper = new ActionMapper(new Dictionary<string, MouseAction>());
        var session = new LogGridSession(HorizKeys, VertKeys, actionMapper, mode, renderer: null);

        session.Activate(ScreenBounds, Origin);
        session.OnKey(VKey.A);
        session.OnKey(VKey.Q);
        session.OnKey(VKey.Escape);
        // No exception = pass
    }
}
