using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Klikety.Config;

internal sealed class SettingsDraft {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
    private JsonObject _baseline = [];
    private readonly Dictionary<string, JsonNode?> _changes = new(StringComparer.Ordinal);

    public SettingsDraft(ConfigModel config) => Rebase(config);

    public bool IsDirty => _changes.Count > 0;
    public IReadOnlyDictionary<string, JsonNode?> Changes => _changes;

    public void Rebase(ConfigModel config) {
        _baseline = JsonSerializer.SerializeToNode(config, Options) as JsonObject
            ?? throw new InvalidDataException("Could not create settings draft baseline.");
        _changes.Clear();
    }

    public void Set(string path, JsonNode? value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        JsonNode? original = _baseline;
        foreach (var segment in path.Split('.')) {
            if (original is not JsonObject parent || !parent.TryGetPropertyValue(segment, out original)) {
                throw new InvalidDataException($"Unknown settings draft field '{path}'.");
            }
        }

        if (JsonNode.DeepEquals(original, value)) {
            _changes.Remove(path);
        } else {
            _changes[path] = value?.DeepClone();
        }
    }

    public void Clear(string path) => _changes.Remove(path);
}
