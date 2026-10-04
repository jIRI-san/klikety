namespace Klikety.Overlay;

internal interface IIndicatorDispatcher {
    bool CheckAccess();
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

    private readonly object _gate = new();
    private readonly HashSet<Operation> _pending = [];
    private Operation? _active;
    private bool _disposed;

    public Task ShowAndWait(double x, double y, CancellationToken ct) {
        var operation = new Operation(ct);
        lock (_gate) {
            if (_disposed) {
                return Task.FromException(new ObjectDisposedException(nameof(ClickIndicatorLifecycle)));
            }
            _pending.Add(operation);
            operation.Registration = ct.Register(() => Cancel(operation));
            if (operation.Completion.Task.IsCompleted) {
                operation.Registration.Unregister();
            }
        }
        dispatcher.Post(() => Start(operation, x, y));
        return operation.Completion.Task;
    }

    private void Cancel(Operation operation) {
        lock (_gate) {
            if (_pending.Remove(operation)) {
                operation.Registration.Unregister();
                operation.Completion.TrySetCanceled(operation.Token);
                return;
            }
        }
        dispatcher.Post(() => Finish(operation, cancelled: true));
    }

    private void Start(Operation operation, double x, double y) {
        lock (_gate) {
            if (!_pending.Remove(operation)) {
                return;
            }
        }
        if (_active is { } previous) {
            Finish(previous, cancelled: true);
        }
        _active = operation;
        if (operation.Token.IsCancellationRequested) {
            Finish(operation, cancelled: true);
            return;
        }
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

    public void Dispose() {
        if (dispatcher.CheckAccess()) {
            DisposeOnDispatcher();
        } else {
            dispatcher.Post(DisposeOnDispatcher);
        }
    }

    private void DisposeOnDispatcher() {
        lock (_gate) {
            if (_disposed) {
                return;
            }
            _disposed = true;
            foreach (var pending in _pending) {
                pending.Registration.Unregister();
                pending.Completion.TrySetCanceled(pending.Token);
            }
            _pending.Clear();
        }
        if (_active is { } operation) {
            Finish(operation, cancelled: true);
        }
        view.Close();
    }
}
