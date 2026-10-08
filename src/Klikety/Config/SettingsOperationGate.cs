namespace Klikety.Config;

internal sealed class SettingsOperationGate(Func<bool> isIdle) {
    private int _active;

    public IDisposable Enter() {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) {
            throw new InvalidOperationException("A configuration operation is already in progress.");
        }
        try {
            if (!isIdle()) {
                throw new InvalidOperationException("Configuration changes require idle navigation and macro activity.");
            }
            return new Lease(this);
        } catch {
            Volatile.Write(ref _active, 0);
            throw;
        }
    }

    private sealed class Lease(SettingsOperationGate owner) : IDisposable {
        private SettingsOperationGate? _owner = owner;
        public void Dispose() {
            if (Interlocked.Exchange(ref _owner, null) is { } gate) {
                Volatile.Write(ref gate._active, 0);
            }
        }
    }
}
