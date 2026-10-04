using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Klikety.Config;

/// <summary>Prototype leaf edits, not a ConfigModel round-trip.</summary>
internal sealed class SettingsConfigStore {
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _path;
    private byte[] _snapshot = [];
    private string _text = "";
    private bool _bom;
    private JsonObject _modeDefaults = [];

    public SettingsConfigStore(string path) => _path = path;
    public string Path => _path;
    public IReadOnlyList<string> LastWarnings { get; private set; } = [];

    public ConfigLoadResult Open() {
        var bytes = File.ReadAllBytes(_path);
        var bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        string text;
        try {
            text = Utf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        } catch (DecoderFallbackException ex) {
            throw new InvalidDataException("Settings requires a UTF-8 config file.", ex);
        }
        var result = ConfigLoader.ReadSettings(text);
        ReadTree(Utf8.GetBytes(text)); // Reject ambiguous duplicate properties before editing.
        _snapshot = bytes;
        _text = text;
        _bom = bom;
        LastWarnings = result.SettingsWarnings;
        var options = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        };
        _modeDefaults = new JsonObject {
            ["uniformGrid"] = JsonSerializer.SerializeToNode(result.Config.Modes.UniformGrid, options),
            ["crosshair"] = JsonSerializer.SerializeToNode(result.Config.Modes.Crosshair, options),
            ["logCrosshair"] = JsonSerializer.SerializeToNode(result.Config.Modes.LogCrosshair, options),
            ["logGrid"] = JsonSerializer.SerializeToNode(result.Config.Modes.LogGrid, options),
        };
        return result;
    }

    public string SchemaReference {
        get {
            using var document = JsonDocument.Parse(_text, new JsonDocumentOptions {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return document.RootElement.TryGetProperty("$schema", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()!
                : "(not specified)";
        }
    }

    public string Preview(IReadOnlyDictionary<string, JsonNode?> changes) {
        var patch = new JsonObject();
        foreach (var (path, value) in changes) {
            var segments = path.Split('.');
            var parent = patch;
            foreach (var segment in segments[..^1]) {
                if (parent[segment] is not JsonObject child) {
                    child = new JsonObject();
                    parent.Add(segment, child);
                }
                parent = child;
            }
            parent.Add(segments[^1], value?.DeepClone());
        }
        var bytes = Utf8.GetBytes(_text);
        var edits = new List<Edit>();
        CollectEdits(ReadTree(bytes), patch, edits, "");
        foreach (var edit in edits.OrderByDescending(e => e.Start)) {
            var replacement = Utf8.GetBytes(edit.Text);
            bytes = [.. bytes.AsSpan(0, edit.Start), .. replacement, .. bytes.AsSpan(edit.End)];
        }
        return Utf8.GetString(bytes);
    }

    public ConfigModel Save(IReadOnlyDictionary<string, JsonNode?> changes, Action<ConfigModel>? preflight = null) {
        var text = Preview(changes);
        var result = ConfigLoader.ReadSettings(text);
        if (result.SettingsBlockingErrors.Count > 0) {
            throw new InvalidDataException(string.Join(Environment.NewLine, result.SettingsBlockingErrors));
        }
        preflight?.Invoke(result.Config);
        if (!File.Exists(_path) || !File.ReadAllBytes(_path).AsSpan().SequenceEqual(_snapshot)) {
            throw new IOException("Config changed on disk. Discard/reopen to load the external edits before saving.");
        }
        if (changes.Count == 0 || text == _text) {
            LastWarnings = result.SettingsWarnings;
            return result.Config;
        }

        var bytes = Utf8.GetBytes(text);
        if (_bom) {
            bytes = [.. Encoding.UTF8.Preamble, .. bytes];
        }
        var temporary = Path.Combine(Path.GetDirectoryName(_path)!, Path.GetRandomFileName());
        try {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (!File.Exists(_path) || !File.ReadAllBytes(_path).AsSpan().SequenceEqual(_snapshot)) {
                throw new IOException("Config changed on disk. Discard/reopen to load the external edits before saving.");
            }
            File.Replace(temporary, _path, _path + ".settings.bak");
            _snapshot = bytes;
            _text = text;
            LastWarnings = result.SettingsWarnings;
            return result.Config;
        } finally {
            if (File.Exists(temporary)) {
                File.Delete(temporary);
            }
        }
    }

    private sealed class SpanNode {
        public int Start { get; init; }
        public int End { get; set; }
        public Dictionary<string, SpanNode>? Properties { get; init; }
        public List<SpanNode>? Elements { get; init; }
    }

    private sealed record Edit(int Start, int End, string Text);

    private static SpanNode ReadTree(byte[] bytes) {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        if (!reader.Read()) {
            throw new InvalidDataException("Settings config is empty.");
        }
        var root = ReadValue(ref reader);
        if (reader.Read()) {
            throw new InvalidDataException("Unexpected data after config.");
        }
        return root;
    }

    private static SpanNode ReadValue(ref Utf8JsonReader reader) {
        var node = new SpanNode {
            Start = checked((int)reader.TokenStartIndex),
            Properties = reader.TokenType == JsonTokenType.StartObject ? new(StringComparer.OrdinalIgnoreCase) : null,
            Elements = reader.TokenType == JsonTokenType.StartArray ? [] : null,
        };
        if (node.Properties is { } properties) {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject) {
                var name = reader.GetString()!;
                reader.Read();
                var child = ReadValue(ref reader);
                if (!properties.TryAdd(name, child)) {
                    throw new InvalidDataException($"Duplicate config property '{name}' is ambiguous. Resolve it in the file first.");
                }
            }
        } else if (node.Elements is { } elements) {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) {
                elements.Add(ReadValue(ref reader));
            }
        } else {
            reader.Skip();
        }
        node.End = checked((int)reader.BytesConsumed);
        return node;
    }

    private void CollectEdits(SpanNode node, JsonObject patch, List<Edit> edits, string path) {
        var properties = node.Properties ?? throw new InvalidDataException("Cannot edit a non-object config section.");
        var additions = new List<string>();
        foreach (var (name, value) in patch) {
            var childPath = path.Length == 0 ? name : path + "." + name;
            if (properties.TryGetValue(name, out var child)) {
                if (value is JsonObject nested) {
                    CollectEdits(child, nested, edits, childPath);
                } else {
                    edits.Add(new Edit(child.Start, child.End, value?.ToJsonString() ?? "null"));
                }
            } else {
                JsonNode? addition = value;
                if (value is JsonObject objectPatch) {
                    // A newly inserted ModeConfig would otherwise reset omitted bools to false.
                    var defaults = childPath == "modes" ? _modeDefaults
                        : path == "modes" ? _modeDefaults[name] as JsonObject : null;
                    if (defaults is not null) {
                        var merged = (JsonObject)defaults.DeepClone();
                        Merge(merged, objectPatch);
                        addition = merged;
                    }
                }
                additions.Add(JsonSerializer.Serialize(name) + ": " + (addition?.ToJsonString() ?? "null"));
            }
        }
        if (additions.Count > 0) {
            // Insert after the last value, before its trailing comma/comments (if any).
            var position = properties.Count == 0 ? node.End - 1 : properties.Values.Max(p => p.End);
            var prefix = properties.Count == 0 ? "" : ",";
            edits.Add(new Edit(position, position, prefix + "\n    " + string.Join(",\n    ", additions)));
        }
    }

    private static void Merge(JsonObject target, JsonObject patch) {
        foreach (var (name, value) in patch) {
            if (value is JsonObject nested && target[name] is JsonObject child) {
                Merge(child, nested);
            } else {
                target[name] = value?.DeepClone();
            }
        }
    }
}
