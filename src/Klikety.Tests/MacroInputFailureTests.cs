using Klikety.Config;
using Klikety.Navigation;
using Klikety.Services;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging;

namespace Klikety.Tests;

public class MacroInputFailureTests {
    [Theory]
    [InlineData(MacroActionType.LeftClick)]
    [InlineData(MacroActionType.RightClick)]
    [InlineData(MacroActionType.MiddleClick)]
    [InlineData(MacroActionType.DoubleClick)]
    [InlineData(MacroActionType.MoveOnly)]
    [InlineData(MacroActionType.DragDrop)]
    [InlineData(MacroActionType.Scroll)]
    public async Task FailedStep_ReturnsInputFailed_NoProgressOrLaterSteps(MacroActionType action) {
        var failure = new InputResult([new(InputStage.Click, 2, 1, 5)], new(InputStage.ReleaseCleanup, 1, 0, 87));
        var mouse = new FakeMouseActionService { Result = failure };
        var player = new MacroPlayer(mouse, new FakeScreenBoundsProvider(), new FakeDelayProvider(), 1);
        var progress = new List<int>();
        player.StepCompleted += (completed, _) => progress.Add(completed);
        var macro = MacroCancellationTests.Macro(action);
        macro.Steps.Add(new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 100 });
        var result = await player.Play(macro, CancellationToken.None);
        Assert.Equal(PlaybackResultKind.InputFailed, result.Kind);
        Assert.Same(failure, result.InputFailure);
        Assert.Contains("5", result.Message);
        Assert.Contains("87", result.Message);
        Assert.Empty(progress);
        Assert.Equal(1, mouse.Calls.Count + mouse.DragCalls.Count + mouse.ScrollCalls.Count);
    }

    private static readonly int[] expected = new[] { 1 };

    [Fact]
    public async Task FailedSecondStep_PreservesOnlyEarlierSuccessfulProgress() {
        var mouse = new FakeMouseActionService();
        var player = new MacroPlayer(mouse, new FakeScreenBoundsProvider(), new FakeDelayProvider(), 1);
        var progress = new List<int>();
        player.StepCompleted += (completed, _) => {
            progress.Add(completed);
            mouse.Result = new([new(InputStage.Scroll, 1, 0)]);
        };
        var macro = MacroCancellationTests.Macro(MacroActionType.LeftClick);
        macro.Steps.Add(new MacroStep { ActionType = MacroActionType.Scroll, ScrollDelta = 120, RelativeTimeMs = 100 });
        macro.Steps.Add(new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 100 });
        Assert.Equal(PlaybackResultKind.InputFailed, (await player.Play(macro, CancellationToken.None)).Kind);
        Assert.Equal(expected, progress);
        Assert.Single(mouse.Calls);
        Assert.Single(mouse.ScrollCalls);
    }

    [Fact]
    public void Handler_InputFailed_ReturnsIdle_ClosesProgressAndLogsOnePlaybackError() {
        var mouse = new FakeMouseActionService { Result = new([new(InputStage.Click, 2, 0, 5)]) };
        var logger = new CapturingLogger();
        var window = new FakeMacroPlaybackWindow();
        using var handler = MacroPlaybackTeardownTests.CreateHandler(mouse, window, new FakeClickIndicator(), logger);
        MacroPlaybackTeardownTests.Start(handler);
        Assert.Equal(MacroState.Idle, handler.State);
        Assert.False(window.IsShown);
        Assert.Equal(0, window.LastCompletedSteps);
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("Macro playback failed", error.Message);
        Assert.Contains("Click: 0/2", error.Message);
    }

    [Fact]
    public void DragFault_IsNotReportedAsUserCancellation() {
        var mouse = new FakeMouseActionService {
            DragResult = () => Task.FromException<InputResult>(new InvalidOperationException("native transport fault")),
        };
        var logger = new CapturingLogger();
        var window = new FakeMacroPlaybackWindow();
        using var handler = MacroPlaybackTeardownTests.CreateHandler(mouse, window, new FakeClickIndicator(), logger,
            MacroCancellationTests.Macro(MacroActionType.DragDrop));
        MacroPlaybackTeardownTests.Start(handler);
        Assert.Equal(MacroState.Idle, handler.State);
        Assert.False(window.IsShown);
        Assert.Contains(logger.Entries, entry => entry.Message == "Playback finished: InputFailed");
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("native transport fault", error.Message);
    }
}
