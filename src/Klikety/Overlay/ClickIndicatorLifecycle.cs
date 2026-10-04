namespace Klikety.Overlay;

internal interface IIndicatorDispatcher {
    void Post(Action action);
}

internal interface IClickIndicatorView {
    void Start(double x, double y, Action completed);
    void StopAndHide();
    void Close();
}

internal sealed class ClickIndicatorLifecycle(IIndicatorDispatcher dispatcher, IClickIndicatorView view) : IDisposable {
    private sealed class Operation(CancellationToken token) {
        public CancellationToken Token { get; } = token;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Registration;
    }

    private Operation? _active;
    private bool _disposed;

    public Task ShowAndWait(double x, double y, CancellationToken ct) {
        var operation = new Operation(ct);
        dispatcher.Post(() => Start(operation, x, y));
        return operation.Completion.Task;
    }

    private void Start(Operation operation, double x, double y) {
        if (_disposed) {
            operation.Completion.TrySetException(new ObjectDisposedException(nameof(ClickIndicatorLifecycle)));
            return;
        }
        if (operation.Token.IsCancellationRequested) {
            operation.Completion.TrySetCanceled(operation.Token);
            return;
        }
        if (_active is { } previous) {
            Finish(previous, cancelled: true);
        }
        _active = operation;
        operation.Registration = operation.Token.Register(() =>
            dispatcher.Post(() => Finish(operation, cancelled: true)));
        try {
            view.Start(x, y, () => dispatcher.Post(() => Finish(operation, cancelled: false)));
        } catch (Exception ex) {
            Finish(operation, cancelled: false, ex);
        }
    }

    private void Finish(Operation operation, bool cancelled, Exception? error = null) {
        if (!ReferenceEquals(_active, operation)) {
            return;
        }
        _active = null;
        operation.Registration.Unregister();
        view.StopAndHide();
        if (error is not null) {
            operation.Completion.TrySetException(error);
        } else if (cancelled || operation.Token.IsCancellationRequested) {
            operation.Completion.TrySetCanceled(operation.Token);
        } else {
            operation.Completion.TrySetResult();
        }
    }

    public void Dispose() => dispatcher.Post(() => {
        if (_disposed) {
            return;
        }
        _disposed = true;
        if (_active is { } operation) {
            Finish(operation, cancelled: true);
        }
        view.Close();
    });
}
