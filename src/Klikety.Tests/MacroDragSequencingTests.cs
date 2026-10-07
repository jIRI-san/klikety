using Klikety.Config;
using Klikety.Navigation;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class MacroDragSequencingTests {
    private static readonly int[] expected = new[] { 1, 2 };
    private static readonly int[] expectedArray = new[] { 100, 200 };

    [Theory]
    [InlineData(1.0, 100, 300)]
    [InlineData(0.5, 50, 150)]
    [InlineData(0.0, 100, 200)]
    public async Task NextSavedInterval_StartsOnlyAfterDragCompletes(double speed, int firstDelay, int totalDelay) {
        var drag = new TaskCompletionSource<InputResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mouse = new FakeMouseActionService { DragResult = () => drag.Task };
        var delay = new FakeDelayProvider();
        var player = new MacroPlayer(mouse, new FakeScreenBoundsProvider(), delay, speed);
        var progress = new List<int>();
        player.StepCompleted += (completed, _) => progress.Add(completed);
        var macro = MacroCancellationTests.Macro(MacroActionType.DragDrop);
        macro.Steps[0] = new MacroStep {
            ActionType = MacroActionType.DragDrop, X = 10, Y = 20, EndX = 30, EndY = 40,
            DragButton = MouseAction.LeftClick, RelativeTimeMs = 100,
        };
        macro.Steps.Add(new MacroStep { ActionType = MacroActionType.LeftClick, RelativeTimeMs = 200 });

        var playback = player.Play(macro, CancellationToken.None);

        Assert.Single(mouse.DragCalls);
        Assert.False(playback.IsCompleted);
        Assert.Empty(mouse.Calls);
        Assert.Empty(progress);
        Assert.Equal(firstDelay, delay.RecordedDelays.Sum());
        drag.SetResult(new([new(InputStage.DragEnd, 2, 2)]));
        Assert.Equal(PlaybackResultKind.Completed, (await playback).Kind);
        Assert.Equal(totalDelay, delay.RecordedDelays.Sum());
        Assert.Equal(expected, progress);
        Assert.Single(mouse.Calls);
        Assert.Equal(expectedArray, macro.Steps.Select(step => step.RelativeTimeMs));
    }
}
