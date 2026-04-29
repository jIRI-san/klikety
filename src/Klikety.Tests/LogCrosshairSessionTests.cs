using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class LogCrosshairSessionTests {
    static readonly VKey[] HorizKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon];
    static readonly VKey[] VertKeys = [VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P];

    static (LogCrosshairSession Session, FakeLogCrosshairRenderer Renderer) Create(bool arrowKeys = true) {
        var renderer = new FakeLogCrosshairRenderer();
        var mode = new ModeConfig { ArrowKeys = arrowKeys, TwoKey = true, LogBaseSize = 5 };
        var actionMapper = new ActionMapper(new Dictionary<string, MouseAction>());
        var session = new LogCrosshairSession(HorizKeys, VertKeys, actionMapper, mode, renderer);
        return (session, renderer);
    }

    // --- Activation ---

    [Fact]
    public void Activate_RendersInitialCross() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        Assert.Single(renderer.Calls);
        Assert.Equal("RenderCross", renderer.Calls[0].Method);
        Assert.NotNull(renderer.Calls[0].Grid);
    }

    [Fact]
    public void Activate_GridHasCorrectDimensions() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        var grid = renderer.Calls[0].Grid!;
        Assert.Equal(11, grid.Cols);
        Assert.Equal(11, grid.Rows);
    }

    // --- Horiz key ---

    [Fact]
    public void HorizKey_RendersHighlightColumn() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.A);

        Assert.Equal("HighlightColumn", renderer.Calls[^1].Method);
        Assert.Equal(0, renderer.Calls[^1].Col); // key 0 → col 0
    }

    [Fact]
    public void HorizKey_MovesCursor() {
        var (session, _) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        Point? cursor = null;
        session.CursorMoveRequested += pt => cursor = pt;
        session.OnKey(VKey.A);

        Assert.NotNull(cursor);
    }

    // --- Vert key ---

    [Fact]
    public void VertKey_RendersHighlightRow() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.Q);

        Assert.Equal("HighlightRow", renderer.Calls[^1].Method);
        Assert.Equal(0, renderer.Calls[^1].Row);
    }

    // --- Both axes ---

    [Fact]
    public void BothAxes_RendersHighlightCell() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.F); // horiz
        session.OnKey(VKey.R); // vert

        Assert.Equal("HighlightCell", renderer.Calls[^1].Method);
    }

    // --- Cancel ---

    [Fact]
    public void Escape_RestoresOrigin() {
        var (session, _) = Create();
        var origin = new Point(550, 550);
        session.Activate(new Rectangle(0, 0, 1100, 1100), origin);

        Point? lastCursor = null;
        session.CursorMoveRequested += pt => lastCursor = pt;
        bool cancelled = false;
        session.Cancelled += () => cancelled = true;

        session.OnKey(VKey.Escape);

        Assert.Equal(origin, lastCursor);
        Assert.True(cancelled);
    }

    // --- Action ---

    [Fact]
    public void ActionKey_FiresActionRequested() {
        var renderer = new FakeLogCrosshairRenderer();
        var mode = new ModeConfig { ArrowKeys = true, TwoKey = true, LogBaseSize = 5 };
        var actionMapper = new ActionMapper(
            new Dictionary<string, MouseAction> { ["Space"] = MouseAction.LeftClick });
        var session = new LogCrosshairSession(HorizKeys, VertKeys, actionMapper, mode, renderer);
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        MouseAction? action = null;
        session.ActionRequested += (_, a) => action = a;

        session.OnKey(VKey.Space);

        Assert.Equal(MouseAction.LeftClick, action);
    }

    // --- Invalid key flashes ---

    [Fact]
    public void InvalidKey_FlashesRenderer() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.Tab);

        Assert.Equal("FlashInvalidKey", renderer.Calls[^1].Method);
    }

    // --- Arrow key ---

    [Fact]
    public void ArrowKey_RendersHighlightCell() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.Right);

        Assert.Equal("HighlightCell", renderer.Calls[^1].Method);
    }

    // --- Deactivate + re-activate ---

    [Fact]
    public void Deactivate_Reactivate_Works() {
        var (session, renderer) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.Deactivate();
        renderer.Calls.Clear();

        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));
        Assert.Equal("RenderCross", renderer.Calls[0].Method);
    }

    // --- Escape after axis selection restores position ---

    [Fact]
    public void Escape_AfterAxisSelection_RestoresPosition() {
        var (session, _) = Create();
        session.Activate(new Rectangle(0, 0, 1100, 1100), new Point(550, 550));

        session.OnKey(VKey.F); // changes cursor

        Point? cursor = null;
        session.CursorMoveRequested += pt => cursor = pt;

        session.OnKey(VKey.Escape); // back to AwaitInput
        session.OnKey(VKey.Escape); // cancel → restore origin

        Assert.Equal(new Point(550, 550), cursor);
    }

    // --- Custom key arrays ---

    [Fact]
    public void SmallKeyArrays_GridDimensionsMatch() {
        var renderer = new FakeLogCrosshairRenderer();
        var mode = new ModeConfig { ArrowKeys = true, TwoKey = true, LogBaseSize = 5 };
        var actionMapper = new ActionMapper(new Dictionary<string, MouseAction>());
        VKey[] smallHoriz = [VKey.A, VKey.S, VKey.D, VKey.F];
        VKey[] smallVert = [VKey.Q, VKey.W, VKey.E, VKey.R];
        var session = new LogCrosshairSession(smallHoriz, smallVert, actionMapper, mode, renderer);
        session.Activate(new Rectangle(0, 0, 500, 500), new Point(250, 250));

        var grid = renderer.Calls[0].Grid!;
        Assert.Equal(5, grid.Cols);
        Assert.Equal(5, grid.Rows);
    }

    // --- Factory wiring ---

    [Fact]
    public void ModeSessionFactory_CreatesLogCrosshairSession() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                LogCrosshair = new ModeConfig {
                    Enabled = true, ChordKey = VKey.M, ArrowKeys = true, TwoKey = true, LogBaseSize = 5,
                    HorizontalKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon],
                    VerticalKeys = [VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P],
                },
            },
        };
        var actionMapper = new ActionMapper(config.ActionBindings);
        var factory = new ModeSessionFactory(config, actionMapper, null, null, null);

        var session = factory.Create("LogCrosshair");

        Assert.IsType<LogCrosshairSession>(session);
    }

    [Fact]
    public void ModeSessionFactory_LogCrosshairWithNullKeys_Throws() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                LogCrosshair = new ModeConfig {
                    Enabled = true, ChordKey = VKey.M, ArrowKeys = true, TwoKey = true,
                },
            },
        };
        var actionMapper = new ActionMapper(config.ActionBindings);
        var factory = new ModeSessionFactory(config, actionMapper, null, null, null);

        Assert.Throws<InvalidOperationException>(() => factory.Create("LogCrosshair"));
    }
}
