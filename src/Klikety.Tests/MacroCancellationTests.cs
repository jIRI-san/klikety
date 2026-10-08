using System.Drawing;

using Klikety.Config;
using Klikety.Navigation;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class MacroCancellationTests {
    internal sealed class PendingIndicator : IClickIndicator {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public Action? AfterCompletion { get; set; }

        public async Task ShowAndWait(double screenX, double screenY, CancellationToken ct) {
            Entered.TrySetResult();
            await Completion.Task.WaitAsync(ct);
            AfterCompletion?.Invoke();
        }

        public void Dispose() => Disposed = true;
    }

    internal static MacroDefinition Macro(MacroActionType action) => new() {
        Name = "Cancellation",
        ScreenWidth = 1920, ScreenHeight = 1080, DpiScale = 1,
        Steps = [new MacroStep {
            ActionType = action, X = 10, Y = 20, EndX = 30, EndY = 40,
            DragButton = MouseAction.LeftClick, ScrollDelta = 120, RelativeTimeMs = 50,
        }],
    };

    [Theory]
    [InlineData(MacroActionType.LeftClick)]
    [InlineData(MacroActionType.DragDrop)]
    [InlineData(MacroActionType.Scroll)]
    public async Task PendingIndicator_Cancel_SkipsDispatchAndProgress(MacroActionType action) {
        using var cts = new CancellationTokenSource();
        var indicator = new PendingIndicator();
        var mouse = new FakeMouseActionService();
        var player = new MacroPlayer(mouse, new FakeScreenBoundsProvider(), new FakeDelayProvider(), 1, indicator);
        var progress = 0;
        player.StepCompleted += (_, _) => progress++;

        var playback = player.Play(Macro(action), cts.Token);
        await indicator.Entered.Task;
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playback);

        Assert.Empty(mouse.Calls);
        Assert.Empty(mouse.DragCalls);
        Assert.Empty(mouse.ScrollCalls);
        Assert.Equal(0, progress);
    }

    [Fact]
    public async Task CancelImmediatelyAfterIndicatorCompletion_SkipsDispatch() {
        using var cts = new CancellationTokenSource();
        var indicator = new PendingIndicator { AfterCompletion = cts.Cancel };
        var mouse = new FakeMouseActionService();
        var player = new MacroPlayer(mouse, new FakeScreenBoundsProvider(), new FakeDelayProvider(), 1, indicator);
        var playback = player.Play(Macro(MacroActionType.LeftClick), cts.Token);
        indicator.Completion.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playback);
        Assert.Empty(mouse.Calls);
    }

    [Theory]
    [InlineData(MacroActionType.MoveOnly)]
    [InlineData(MacroActionType.LeftClick)]
    public async Task NoIndicator_CancellationAfterDelay_SkipsDispatch(MacroActionType action) {
        using var cts = new CancellationTokenSource();
        var mouse = new FakeMouseActionService();
        var player = new MacroPlayer(mouse, new FakeScreenBoundsProvider(), new CancellingDelay(cts), 1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => player.Play(Macro(action), cts.Token));
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public async Task PreCancelled_SkipsIndicatorAndDispatch() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var mouse = new FakeMouseActionService();
        var indicator = new FakeClickIndicator();
        var player = new MacroPlayer(mouse, new FakeScreenBoundsProvider(), new FakeDelayProvider(), 1, indicator);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => player.Play(Macro(MacroActionType.LeftClick), cts.Token));
        Assert.Empty(indicator.ShownPositions);
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public async Task CompletedIndicator_CanPlayAgain() {
        var mouse = new FakeMouseActionService();
        var indicator = new FakeClickIndicator();
        var player = new MacroPlayer(mouse, new FakeScreenBoundsProvider(), new FakeDelayProvider(), 1, indicator);
        for (var i = 0; i < 2; i++) {
            Assert.Equal(PlaybackResultKind.Completed, (await player.Play(Macro(MacroActionType.LeftClick), CancellationToken.None)).Kind);
        }
        Assert.Equal(2, mouse.Calls.Count);
        Assert.All(mouse.Calls, call => Assert.Equal(new Point(10, 20), call.Point));
    }

    private sealed class CancellingDelay(CancellationTokenSource cancellation) : IDelayProvider {
        public Task Delay(int milliseconds, CancellationToken ct) {
            cancellation.Cancel();
            return Task.CompletedTask;
        }
    }
}
