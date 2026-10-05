using Microsoft.Extensions.Logging;

namespace Klikety.Config;

internal sealed class SettingsLoggerLifetime {
    private readonly HashSet<ILoggerFactory> _retained = new(ReferenceEqualityComparer.Instance);
    public void Retain(ILoggerFactory factory) => _retained.Add(factory);
    public void Complete(ILoggerFactory? current) {
        var failures = new List<string>();
        foreach (var factory in _retained.ToArray()) {
            if (ReferenceEquals(factory, current)) { _retained.Remove(factory); continue; }
            try {
                factory.Dispose();
                _retained.Remove(factory);
            }
            catch (Exception ex) { failures.Add("Logger cleanup failed: " + ex.Message); }
        }
        if (failures.Count > 0) { throw new InvalidOperationException(string.Join("\n", failures)); }
    }
}
