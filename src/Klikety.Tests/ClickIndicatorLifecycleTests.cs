using Klikety.Overlay;

namespace Klikety.Tests;

public class ClickIndicatorLifecycleTests {
    private sealed class Dispatcher : IIndicatorDispatcher {
        public Queue<Action> Pending { get; } = new();
        public void Post(Action action) => Pending.Enqueue(action);
        public void Drain() {
            while (Pending.TryDequeue(out var action)) {
                action();
            }
        }
    }

    private sealed class View : IClickIndicatorView {
        public Action? Completion { get; private set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int Closes { get; private set; }
        public bool Visible { get; private set; }
        public void Start(double x, double y, Action completed) {
            Starts++;
            Visible = true;
            Completion = completed;
        }
        public void StopAndHide() {
            Stops++;
            Visible = false;
            Completion = null;
        }
        public void Close() => Closes++;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationCompletionRace_CleansOnceOnDispatcher(bool cancellationFirst) {
        var dispatcher = new Dispatcher();
        var view = new View();
        using var lifecycle = new ClickIndicatorLifecycle(dispatcher, view);
        using var cts = new CancellationTokenSource();
        var task = lifecycle.ShowAndWait(1, 2, cts.Token);
        dispatcher.Drain();
        var completion = view.Completion!;
        if (cancellationFirst) {
            cts.Cancel();
            completion();
        } else {
            completion();
            cts.Cancel();
        }
        Assert.True(view.Visible);
        Assert.False(task.IsCompleted);
        dispatcher.Drain();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(view.Visible);
        Assert.Null(view.Completion);
        Assert.Equal(1, view.Stops);
        cts.Cancel();
        Assert.Empty(dispatcher.Pending);
    }

    [Fact]
    public async Task Reuse_StaleCompletionCannotHideReplacement() {
        var dispatcher = new Dispatcher();
        var view = new View();
        using var lifecycle = new ClickIndicatorLifecycle(dispatcher, view);
        var first = lifecycle.ShowAndWait(1, 2, CancellationToken.None);
        dispatcher.Drain();
        var stale = view.Completion!;
        var second = lifecycle.ShowAndWait(3, 4, CancellationToken.None);
        dispatcher.Drain();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        stale();
        dispatcher.Drain();
        Assert.True(view.Visible);
        Assert.False(second.IsCompleted);
        view.Completion!();
        dispatcher.Drain();
        await second;
        Assert.Equal(2, view.Stops);
        Assert.Null(view.Completion);
    }

    [Fact]
    public async Task Dispose_IsNonblockingAndCancelsPendingOperation() {
        var dispatcher = new Dispatcher();
        var view = new View();
        var lifecycle = new ClickIndicatorLifecycle(dispatcher, view);
        var task = lifecycle.ShowAndWait(1, 2, CancellationToken.None);
        dispatcher.Drain();
        lifecycle.Dispose();
        Assert.False(task.IsCompleted);
        Assert.True(view.Visible);
        dispatcher.Drain();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, view.Stops);
        Assert.Equal(1, view.Closes);
        lifecycle.Dispose();
        dispatcher.Drain();
        Assert.Equal(1, view.Closes);
        var afterDispose = lifecycle.ShowAndWait(1, 2, CancellationToken.None);
        dispatcher.Drain();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => afterDispose);
    }

    [Fact]
    public async Task PreCancelled_DoesNotShowOrSubscribe() {
        var dispatcher = new Dispatcher();
        var view = new View();
        using var lifecycle = new ClickIndicatorLifecycle(dispatcher, view);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var task = lifecycle.ShowAndWait(1, 2, cts.Token);
        dispatcher.Drain();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, view.Starts);
        Assert.Null(view.Completion);
    }
}
