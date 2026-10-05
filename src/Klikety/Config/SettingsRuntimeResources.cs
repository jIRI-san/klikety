namespace Klikety.Config;

internal sealed class SettingsRuntimeResources {
    private readonly List<(string Name, Action Release)> _resources = [];
    private readonly Action<string>? _checkpoint;

    public SettingsRuntimeResources(Action<string>? checkpoint = null) => _checkpoint = checkpoint;
    public bool HasResources => _resources.Count > 0;

    public T Own<T>(string name, T resource) where T : IDisposable {
        Own(name, resource.Dispose);
        return resource;
    }

    public void Own(string name, Action release) => _resources.Add((name, release));
    public void Checkpoint(string name) => _checkpoint?.Invoke(name);
    public void Transfer(string name) => _resources.RemoveAll(resource => resource.Name == name);
    public void ReleaseFirst(string name) {
        var index = _resources.FindIndex(resource => resource.Name == name);
        var resource = _resources[index];
        _resources.RemoveAt(index);
        _resources.Add(resource);
    }

    public static SettingsRuntimeResources Create(Action<SettingsRuntimeResources> compose,
        Action<string>? checkpoint = null, Action<SettingsRuntimeResources>? retainFailed = null) {
        var resources = new SettingsRuntimeResources(checkpoint);
        try {
            compose(resources);
            return resources;
        } catch (Exception ex) {
            var cleanup = resources.Release();
            if (resources.HasResources) { retainFailed?.Invoke(resources); }
            throw new InvalidOperationException(string.Join(Environment.NewLine, new[] { ex.Message }.Concat(cleanup)), ex);
        }
    }

    public IReadOnlyList<string> Release() {
        var failures = new List<string>();
        for (var index = _resources.Count - 1; index >= 0; index--) {
            var resource = _resources[index];
            try {
                resource.Release();
                _resources.RemoveAt(index);
            } catch (Exception ex) {
                // Continue releasing independent resources, but never report a clean teardown.
                failures.Add($"{resource.Name} cleanup failed: {ex.Message}");
            }
        }
        return failures;
    }
}
