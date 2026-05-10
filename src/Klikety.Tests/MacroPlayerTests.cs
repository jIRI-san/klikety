using System.Drawing;

using Klikety.Config;
using Klikety.Navigation;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class MacroPlayerTests {
    private static MacroDefinition CreateMacro(params MacroStep[] steps) => new() {
        Name = "Test",
        ScreenWidth = 1920,
        ScreenHeight = 1080,
        DpiScale = 1.0,
        Steps = [.. steps],
    };

    private static (MacroPlayer Player, FakeMouseActionService Mouse, FakeDelayProvider Delay,
        FakeScreenBoundsProvider Screen) CreatePlayer(double speedModifier = 1.0) {
        var mouse = new FakeMouseActionService();
        var screen = new FakeScreenBoundsProvider {
            Bounds = new Rectangle(0, 0, 1920, 1080),
            DpiScale = 1.0,
        };
        var delay = new FakeDelayProvider();
        var player = new MacroPlayer(mouse, screen, delay, speedModifier);
        return (player, mouse, delay, screen);
    }

    [Fact]
    public async Task Play_LeftClick_CallsSendAction() {
        var (player, mouse, _, _) = CreatePlayer();
        var macro = CreateMacro(new MacroStep {
            ActionType = MacroActionType.LeftClick, X = 100, Y = 200, RelativeTimeMs = 500,
        });

        var result = await player.Play(macro, CancellationToken.None);

        Assert.Equal(PlaybackResultKind.Completed, result.Kind);
        Assert.Single(mouse.Calls);
        Assert.Equal(new Point(100, 200), mouse.Calls[0].Point);
        Assert.Equal(MouseAction.LeftClick, mouse.Calls[0].Action);
    }

    [Fact]
    public async Task Play_RightClick_CallsSendAction() {
        var (player, mouse, _, _) = CreatePlayer();
        var macro = CreateMacro(new MacroStep {
            ActionType = MacroActionType.RightClick, X = 50, Y = 60, RelativeTimeMs = 100,
        });

        await player.Play(macro, CancellationToken.None);

        Assert.Equal(MouseAction.RightClick, mouse.Calls[0].Action);
    }

    [Fact]
    public async Task Play_MiddleClick_CallsSendAction() {
        var (player, mouse, _, _) = CreatePlayer();
        var macro = CreateMacro(new MacroStep {
            ActionType = MacroActionType.MiddleClick, X = 10, Y = 20, RelativeTimeMs = 100,
        });

        await player.Play(macro, CancellationToken.None);

        Assert.Equal(MouseAction.MiddleClick, mouse.Calls[0].Action);
    }

    [Fact]
    public async Task Play_DoubleClick_CallsSendAction() {
        var (player, mouse, _, _) = CreatePlayer();
        var macro = CreateMacro(new MacroStep {
            ActionType = MacroActionType.DoubleClick, X = 10, Y = 20, RelativeTimeMs = 100,
        });

        await player.Play(macro, CancellationToken.None);

        Assert.Equal(MouseAction.DoubleClick, mouse.Calls[0].Action);
    }

    [Fact]
    public async Task Play_MoveOnly_CallsSendActionWithNoModifiers() {
        var (player, mouse, _, _) = CreatePlayer();
        var macro = CreateMacro(new MacroStep {
            ActionType = MacroActionType.MoveOnly, X = 300, Y = 400, RelativeTimeMs = 100,
            Modifiers = ActionModifiers.Shift, // should be ignored for MoveOnly
        });

        await player.Play(macro, CancellationToken.None);

        Assert.Equal(MouseAction.MoveOnly, mouse.Calls[0].Action);
        Assert.Equal(ActionModifiers.None, mouse.Calls[0].Modifiers);
    }

    [Fact]
    public async Task Play_DragDrop_CallsSendDrag() {
        var (player, mouse, _, _) = CreatePlayer();
        var macro = CreateMacro(new MacroStep {
            ActionType = MacroActionType.DragDrop, X = 10, Y = 20, EndX = 100, EndY = 200,
            DragButton = MouseAction.LeftClick, RelativeTimeMs = 100,
        });

        await player.Play(macro, CancellationToken.None);

        Assert.Single(mouse.DragCalls);
        Assert.Equal(new Point(10, 20), mouse.DragCalls[0].Start);
        Assert.Equal(new Point(100, 200), mouse.DragCalls[0].End);
        Assert.Equal(MouseAction.LeftClick, mouse.DragCalls[0].Button);
    }

    [Fact]
    public async Task Play_Scroll_CallsSendScroll() {
        var (player, mouse, _, _) = CreatePlayer();
        var macro = CreateMacro(new MacroStep {
            ActionType = MacroActionType.Scroll, ScrollDelta = -120, RelativeTimeMs = 100,
            Modifiers = ActionModifiers.Ctrl,
        });

        await player.Play(macro, CancellationToken.None);

        Assert.Single(mouse.ScrollCalls);
        Assert.Equal(-120, mouse.ScrollCalls[0].WheelDelta);
        Assert.Equal(ActionModifiers.Ctrl, mouse.ScrollCalls[0].Modifiers);
    }

    [Fact]
    public async Task Play_SpeedModifier1_CorrectDelays() {
        var (player, _, delay, _) = CreatePlayer(speedModifier: 1.0);
        var macro = CreateMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 200 },
            new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 500 });

        await player.Play(macro, CancellationToken.None);

        Assert.Equal(200 + 500, delay.RecordedDelays.Sum());
    }

    [Fact]
    public async Task Play_SpeedModifierZero_FixedDelay100() {
        var (player, _, delay, _) = CreatePlayer(speedModifier: 0);
        var macro = CreateMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 5000 });

        await player.Play(macro, CancellationToken.None);

        Assert.Equal(100, delay.RecordedDelays.Sum());
    }

    [Fact]
    public async Task Play_SpeedModifierHalf_DelaysHalvedWithFloor() {
        var (player, _, delay, _) = CreatePlayer(speedModifier: 0.5);
        var macro = CreateMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 200 },
            new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 60 });

        await player.Play(macro, CancellationToken.None);

        // 200*0.5=100, 60*0.5=30 clamped to 50 → total 150
        Assert.Equal(150, delay.RecordedDelays.Sum());
    }

    [Fact]
    public async Task Play_TinySpeedModifier_GlobalMinFloor50ms() {
        var (player, _, delay, _) = CreatePlayer(speedModifier: 0.01);
        var macro = CreateMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 100 });

        await player.Play(macro, CancellationToken.None);

        Assert.Equal(50, delay.RecordedDelays.Sum());
    }

    [Fact]
    public async Task Play_ScreenMismatch_NoActionsAndReturnsMismatch() {
        var (player, mouse, _, screen) = CreatePlayer();
        screen.Bounds = new Rectangle(0, 0, 2560, 1440); // different from macro's 1920x1080
        var macro = CreateMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 100 });

        var result = await player.Play(macro, CancellationToken.None);

        Assert.Equal(PlaybackResultKind.ScreenMismatch, result.Kind);
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public async Task Play_DpiMismatch_ReturnsMismatch() {
        var (player, mouse, _, screen) = CreatePlayer();
        screen.DpiScale = 1.5;
        var macro = CreateMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 100 });

        var result = await player.Play(macro, CancellationToken.None);

        Assert.Equal(PlaybackResultKind.ScreenMismatch, result.Kind);
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public async Task Play_CancellationMidPlayback_SkipsRemainingSteps() {
        var (player, mouse, _, _) = CreatePlayer();
        var macro = CreateMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 10, Y = 10, RelativeTimeMs = 100 },
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 20, Y = 20, RelativeTimeMs = 100 });

        var cts = new CancellationTokenSource();
        var stepCount = 0;
        player.StepCompleted += (_, _) => {
            stepCount++;
            if (stepCount == 1) {
                cts.Cancel();
            }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => player.Play(macro, cts.Token));

        Assert.Single(mouse.Calls); // only first step executed
    }

    [Fact]
    public async Task Play_StepCompletedEvent_FiresCorrectly() {
        var (player, _, _, _) = CreatePlayer();
        var macro = CreateMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 100 },
            new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 100 },
            new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 100 });

        var events = new List<(int completed, int total)>();
        player.StepCompleted += (c, t) => events.Add((c, t));

        await player.Play(macro, CancellationToken.None);

        Assert.Equal([(1, 3), (2, 3), (3, 3)], events);
    }

    [Fact]
    public async Task Play_WithModifiers_PassedThrough() {
        var (player, mouse, _, _) = CreatePlayer();
        var macro = CreateMacro(new MacroStep {
            ActionType = MacroActionType.LeftClick, X = 50, Y = 60, RelativeTimeMs = 100,
            Modifiers = ActionModifiers.Shift | ActionModifiers.Ctrl,
        });

        await player.Play(macro, CancellationToken.None);

        Assert.Equal(ActionModifiers.Shift | ActionModifiers.Ctrl, mouse.Calls[0].Modifiers);
    }

    // --- Window-relative playback tests ---

    private static MacroDefinition CreateWindowRelativeMacro(params MacroStep[] steps) => new() {
        Name = "WR-Test",
        PositionMode = MacroPositionMode.WindowRelative,
        WindowWidth = 800,
        WindowHeight = 600,
        WindowTitlePattern = "Notepad",
        DpiScale = 1.0,
        ScreenWidth = 1920,
        ScreenHeight = 1080,
        Steps = [.. steps],
    };

    private static (MacroPlayer Player, FakeMouseActionService Mouse, FakeDelayProvider Delay,
        FakeScreenBoundsProvider Screen, FakeForegroundWindowProvider Fg) CreatePlayerWithFg(double speedModifier = 1.0) {
        var mouse = new FakeMouseActionService();
        var screen = new FakeScreenBoundsProvider {
            Bounds = new Rectangle(0, 0, 1920, 1080),
            DpiScale = 1.0,
        };
        var delay = new FakeDelayProvider();
        var fg = new FakeForegroundWindowProvider {
            Handle = 42,
            Bounds = new Rectangle(100, 50, 800, 600),
            Title = "My Notepad",
        };
        var player = new MacroPlayer(mouse, screen, delay, speedModifier, foregroundWindow: fg);
        return (player, mouse, delay, screen, fg);
    }

    private static PlaybackContext CreateWrContext(FakeForegroundWindowProvider fg) =>
        new(MacroPositionMode.WindowRelative, fg.Bounds, fg.Title, fg.Handle, new Point(150, 100));

    [Fact]
    public async Task Play_WindowRelative_TitleMismatch_ReturnsWindowMismatch() {
        var (player, mouse, _, _, fg) = CreatePlayerWithFg();
        fg.Title = "Chrome";
        var macro = CreateWindowRelativeMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 10, Y = 10, RelativeTimeMs = 100 });

        var result = await player.Play(macro, CreateWrContext(fg), CancellationToken.None);

        Assert.Equal(PlaybackResultKind.WindowMismatch, result.Kind);
        Assert.Contains("does not match pattern", result.Message);
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public async Task Play_WindowRelative_SizeMismatch_ReturnsWindowMismatch() {
        var (player, mouse, _, _, fg) = CreatePlayerWithFg();
        fg.Bounds = new Rectangle(100, 50, 1024, 768);
        var macro = CreateWindowRelativeMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 10, Y = 10, RelativeTimeMs = 100 });

        var result = await player.Play(macro, new PlaybackContext(
            MacroPositionMode.WindowRelative, fg.Bounds, fg.Title, fg.Handle, Point.Empty), CancellationToken.None);

        Assert.Equal(PlaybackResultKind.WindowMismatch, result.Kind);
        Assert.Contains("Expected window", result.Message);
    }

    [Fact]
    public async Task Play_WindowRelative_DpiMismatch_ReturnsWindowMismatch() {
        var (player, mouse, _, screen, fg) = CreatePlayerWithFg();
        screen.DpiScale = 1.5;
        var macro = CreateWindowRelativeMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 10, Y = 10, RelativeTimeMs = 100 });

        var result = await player.Play(macro, CreateWrContext(fg), CancellationToken.None);

        Assert.Equal(PlaybackResultKind.WindowMismatch, result.Kind);
        Assert.Contains("DPI mismatch", result.Message);
    }

    [Fact]
    public async Task Play_WindowRelative_ResolvesCoordinatesFromWindowOrigin() {
        var (player, mouse, _, _, fg) = CreatePlayerWithFg();
        var macro = CreateWindowRelativeMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 50, Y = 30, RelativeTimeMs = 100 });

        var result = await player.Play(macro, CreateWrContext(fg), CancellationToken.None);

        Assert.Equal(PlaybackResultKind.Completed, result.Kind);
        Assert.Single(mouse.Calls);
        // Window at (100, 50), step at (50, 30) → screen (150, 80)
        Assert.Equal(new Point(150, 80), mouse.Calls[0].Point);
    }

    [Fact]
    public async Task Play_WindowRelative_DriftFocusLost_ReturnsWindowDrift() {
        var (player, mouse, _, _, fg) = CreatePlayerWithFg();
        var macro = CreateWindowRelativeMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 10, Y = 10, RelativeTimeMs = 100 },
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 20, Y = 20, RelativeTimeMs = 100 });

        var stepCount = 0;
        player.StepCompleted += (_, _) => {
            stepCount++;
            if (stepCount == 1) {
                fg.Handle = 99; // Different HWND — focus lost
            }
        };

        var result = await player.Play(macro, CreateWrContext(fg), CancellationToken.None);

        Assert.Equal(PlaybackResultKind.WindowDrift, result.Kind);
        Assert.Contains("lost focus", result.Message);
    }

    [Fact]
    public async Task Play_WindowRelative_DriftResized_ReturnsWindowDrift() {
        var (player, _, _, _, fg) = CreatePlayerWithFg();
        var macro = CreateWindowRelativeMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 10, Y = 10, RelativeTimeMs = 100 },
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 20, Y = 20, RelativeTimeMs = 100 });

        var stepCount = 0;
        player.StepCompleted += (_, _) => {
            stepCount++;
            if (stepCount == 1) {
                fg.Bounds = new Rectangle(100, 50, 900, 700); // Resized
            }
        };

        var result = await player.Play(macro, CreateWrContext(fg), CancellationToken.None);

        Assert.Equal(PlaybackResultKind.WindowDrift, result.Kind);
        Assert.Contains("resized", result.Message);
    }

    [Fact]
    public async Task Play_WindowRelative_WindowMoved_UpdatesCoordinates() {
        var (player, mouse, _, _, fg) = CreatePlayerWithFg();
        var macro = CreateWindowRelativeMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 10, Y = 10, RelativeTimeMs = 100 },
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 10, Y = 10, RelativeTimeMs = 100 });

        var stepCount = 0;
        player.StepCompleted += (_, _) => {
            stepCount++;
            if (stepCount == 1) {
                fg.Bounds = new Rectangle(200, 100, 800, 600); // Moved, same size
            }
        };

        var result = await player.Play(macro, CreateWrContext(fg), CancellationToken.None);

        Assert.Equal(PlaybackResultKind.Completed, result.Kind);
        // First step: window at (100,50), click at (10,10) → (110, 60)
        Assert.Equal(new Point(110, 60), mouse.Calls[0].Point);
        // Second step: window moved to (200,100), click at (10,10) → (210, 110)
        Assert.Equal(new Point(210, 110), mouse.Calls[1].Point);
    }

    [Fact]
    public async Task Play_WindowRelative_StartFromCursor_UsesCursorForDragStart() {
        var (player, mouse, _, _, fg) = CreatePlayerWithFg();
        var macro = CreateWindowRelativeMacro(
            new MacroStep {
                ActionType = MacroActionType.DragDrop, X = 10, Y = 10,
                EndX = 50, EndY = 50, StartFromCursor = true,
                DragButton = MouseAction.LeftClick, RelativeTimeMs = 100,
            });

        var context = new PlaybackContext(
            MacroPositionMode.WindowRelative, fg.Bounds, fg.Title, fg.Handle,
            new Point(300, 200)); // cursor position

        var result = await player.Play(macro, context, CancellationToken.None);

        Assert.Equal(PlaybackResultKind.Completed, result.Kind);
        // Start should be cursor position (300, 200), not resolved step position
        Assert.Equal(new Point(300, 200), mouse.DragCalls[0].Start);
        // End should be resolved: window(100,50) + (50,50) = (150, 100)
        Assert.Equal(new Point(150, 100), mouse.DragCalls[0].End);
    }

    [Fact]
    public async Task Play_WindowRelative_CoordinateOutOfBounds_ReturnsError() {
        var (player, mouse, _, _, fg) = CreatePlayerWithFg();
        var macro = CreateWindowRelativeMacro(
            new MacroStep { ActionType = MacroActionType.LeftClick, X = 900, Y = 10, RelativeTimeMs = 100 });

        var result = await player.Play(macro, CreateWrContext(fg), CancellationToken.None);

        Assert.Equal(PlaybackResultKind.CoordinateOutOfBounds, result.Kind);
        Assert.Contains("outside window bounds", result.Message);
        Assert.Empty(mouse.Calls);
    }
}
