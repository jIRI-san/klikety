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
    public void BothAxes_HighlightsCell() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.A);
        session.OnKey(VKey.Q);

        Assert.Equal("HighlightCell", renderer.Calls[^1].Method);
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
    public void Enter_TriggersSubgridRender() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.Return);

        Assert.Equal("RenderSubgridCross", renderer.Calls[^1].Method);
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
}
