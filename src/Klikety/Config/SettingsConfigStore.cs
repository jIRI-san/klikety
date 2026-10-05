using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Klikety.Config;

/// <summary>Applies targeted edits to a captured JSONC document without serializing the model back.</summary>
internal sealed class SettingsConfigStore {
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions ModelOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly string _path;
    private readonly Action<string>? _fault;
    private byte[] _snapshot = [];
    private string _text = "";
    private bool _bom;
    private JsonObject _effectiveDefaults = [];
    private byte[]? _lastCommitOriginal;
    private byte[]? _lastCommitCandidate;

    public SettingsConfigStore(string path, Action<string>? fault = null) { _path = path; _fault = fault; }
    public string FilePath => _path;
    public IReadOnlyList<string> LastWarnings { get; private set; } = [];
    public bool HasUnacceptedCommit => _lastCommitOriginal is not null && _lastCommitCandidate is not null;
    public bool RequiresReload { get; private set; }

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
        RequiresReload = false;
        _text = text;
        _bom = bom;
        LastWarnings = result.SettingsWarnings;
        _effectiveDefaults = JsonSerializer.SerializeToNode(result.Config, ModelOptions)!.AsObject();
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
        CollectEdits(bytes, ReadTree(bytes), patch, edits, "");
        foreach (var edit in edits.OrderByDescending(e => e.Start)) {
            var replacement = Utf8.GetBytes(edit.Text);
            bytes = [.. bytes.AsSpan(0, edit.Start), .. replacement, .. bytes.AsSpan(edit.End)];
        }
        return Utf8.GetString(bytes);
    }

    public ConfigModel Save(IReadOnlyDictionary<string, JsonNode?> changes, Action<ConfigModel>? preflight = null) {
        var text = Preview(changes);
        var result = ConfigLoader.ReadSettings(text, changes.Keys);
        if (result.SettingsBlockingErrors.Count > 0) {
            throw new InvalidDataException(string.Join(Environment.NewLine, result.SettingsBlockingErrors));
        }
        preflight?.Invoke(result.Config);
        EnsureSnapshotUnchanged();
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
            EnsureSnapshotUnchanged();
            _fault?.Invoke("disk-save");
            File.Replace(temporary, _path, _path + ".settings.bak");
            _lastCommitOriginal = _snapshot;
            _lastCommitCandidate = bytes;
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

    public void AcceptLastCommit() {
        _lastCommitOriginal = null;
        _lastCommitCandidate = null;
    }

    public SettingsDiskRecoveryOutcome RestoreLastCommit() {
        var original = _lastCommitOriginal;
        var candidate = _lastCommitCandidate;
        AcceptLastCommit();
        if (original is null || candidate is null) {
            return new SettingsDiskRecoveryOutcome(false, "No unaccepted settings write is available to restore.");
        }

        var temporary = Path.Combine(System.IO.Path.GetDirectoryName(_path)!, System.IO.Path.GetRandomFileName());
        var outcome = new SettingsDiskRecoveryOutcome(false, "The previous config bytes were not restored.");
        try {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.Write(original);
                stream.Flush(flushToDisk: true);
            }
            if (!File.Exists(_path) || !File.ReadAllBytes(_path).AsSpan().SequenceEqual(candidate)) {
                RequiresReload = true;
                outcome = new SettingsDiskRecoveryOutcome(false,
                    "Config changed after Settings saved it. Newer external bytes were kept; reload before retrying.");
            } else {
                _fault?.Invoke("disk-restore");
                File.Replace(temporary, _path, null);
                _snapshot = original;
                var hasBom = original.AsSpan().StartsWith(Encoding.UTF8.Preamble);
                _text = Utf8.GetString(original, hasBom ? 3 : 0, original.Length - (hasBom ? 3 : 0));
                _bom = hasBom;
                outcome = new SettingsDiskRecoveryOutcome(true, "The previous config bytes were restored.");
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException) {
            outcome = new SettingsDiskRecoveryOutcome(false, $"Could not restore the previous config bytes: {ex.Message}");
        }

        try {
            if (File.Exists(temporary)) {
                File.Delete(temporary);
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            outcome = outcome with { Message = outcome.Message + $" Temporary-file cleanup failed: {ex.Message}" };
        }
        RequiresReload = !outcome.Succeeded;
        return outcome;
    }

    private void EnsureSnapshotUnchanged() {
        if (RequiresReload || !File.Exists(_path) || !File.ReadAllBytes(_path).AsSpan().SequenceEqual(_snapshot)) {
            RequiresReload = true;
            throw new IOException("Config changed on disk. Discard/reopen to load the external edits before saving.");
        }
    }

    private sealed class SpanNode {
        public int Start { get; init; }
        public int End { get; set; }
        public Dictionary<string, SpanNode>? Properties { get; init; }
        public Dictionary<string, MemberSpan>? Members { get; init; }
        public List<SpanNode>? Elements { get; init; }
    }

    private sealed record MemberSpan(int NameStart, SpanNode Value);
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
            Members = reader.TokenType == JsonTokenType.StartObject ? new(StringComparer.OrdinalIgnoreCase) : null,
            Elements = reader.TokenType == JsonTokenType.StartArray ? [] : null,
        };
        if (node.Properties is { } properties) {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject) {
                var nameStart = checked((int)reader.TokenStartIndex);
                var name = reader.GetString()!;
                reader.Read();
                var child = ReadValue(ref reader);
                if (!properties.TryAdd(name, child)) {
                    throw new InvalidDataException($"Duplicate config property '{name}' is ambiguous. Resolve it in the file first.");
                }
                node.Members!.Add(name, new MemberSpan(nameStart, child));
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

    private void CollectEdits(byte[] bytes, SpanNode node, JsonObject patch, List<Edit> edits, string path) {
        var properties = node.Properties ?? throw new InvalidDataException("Cannot edit a non-object config section.");
        var additions = new List<string>();
        foreach (var (name, value) in patch) {
            var childPath = path.Length == 0 ? name : path + "." + name;
            if (properties.TryGetValue(name, out var child)) {
                if (childPath == "actionBindings" && value is JsonObject objectCollection) {
                    PatchObject(bytes, child, objectCollection, edits);
                } else if (value is JsonObject nested && child.Properties is not null) {
                    CollectEdits(bytes, child, nested, edits, childPath);
                } else if (value is JsonArray array && child.Elements is not null) {
                    PatchArray(bytes, child, array, edits);
                } else {
                    edits.Add(new Edit(child.Start, child.End, value?.ToJsonString() ?? "null"));
                }
            } else {
                JsonNode? addition = value;
                if (value is JsonObject objectPatch) {
                    // Preserve effective defaults when inserting a previously absent object.
                    JsonNode? defaultsNode = _effectiveDefaults;
                    foreach (var segment in childPath.Split('.')) {
                        defaultsNode = (defaultsNode as JsonObject)?[segment];
                    }
                    var defaults = defaultsNode as JsonObject;
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

    private static void PatchArray(byte[] bytes, SpanNode node, JsonArray desired, List<Edit> edits) {
        var elements = node.Elements ?? throw new InvalidDataException("Cannot edit a non-array config section.");
        var unmatched = new HashSet<int>(Enumerable.Range(0, elements.Count));
        var rendered = new List<string>(desired.Count);
        foreach (var item in desired) {
            var match = -1;
            for (var i = 0; i < elements.Count; i++) {
                if (unmatched.Contains(i) && Equivalent(bytes, elements[i], item)) {
                    match = i;
                    break;
                }
            }
            if (match >= 0) {
                unmatched.Remove(match);
                rendered.Add(Utf8.GetString(bytes, elements[match].Start, elements[match].End - elements[match].Start));
            } else {
                rendered.Add(item?.ToJsonString() ?? "null");
            }
        }
        var comments = GetContainerComments(bytes, node, elements.Select(e => (e.Start, e.End))).ToList();
        foreach (var index in unmatched) {
            comments.AddRange(ReadComments(bytes, elements[index].Start, elements[index].End));
        }
        edits.Add(new Edit(node.Start, node.End, FormatContainer('[', ']', rendered, comments)));
    }

    private static void PatchObject(byte[] bytes, SpanNode node, JsonObject desired, List<Edit> edits) {
        var members = node.Members ?? throw new InvalidDataException("Cannot edit a non-object config section.");
        var rendered = new List<string>(desired.Count);
        foreach (var (name, value) in desired) {
            if (members.TryGetValue(name, out var member)) {
                var nameText = Utf8.GetString(bytes, member.NameStart, member.Value.Start - member.NameStart);
                var original = Utf8.GetString(bytes, member.Value.Start, member.Value.End - member.Value.Start);
                var replacement = JsonNode.DeepEquals(ParseValue(original), value)
                    ? original
                    : value?.ToJsonString() ?? "null";
                rendered.Add(nameText + replacement);
            } else {
                rendered.Add(JsonSerializer.Serialize(name) + ": " + (value?.ToJsonString() ?? "null"));
            }
        }
        var comments = GetContainerComments(bytes, node, members.Values.Select(m => (m.NameStart, m.Value.End))).ToList();
        var desiredNames = new HashSet<string>(desired.Select(pair => pair.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var (name, member) in members) {
            if (!desiredNames.Contains(name)) {
                comments.AddRange(ReadComments(bytes, member.NameStart, member.Value.End));
            }
        }
        edits.Add(new Edit(node.Start, node.End, FormatContainer('{', '}', rendered, comments)));
    }

    private static JsonNode? ParseValue(string text) => JsonNode.Parse(text, documentOptions: new JsonDocumentOptions {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    private static bool Equivalent(byte[] bytes, SpanNode original, JsonNode? desired) {
        var text = Utf8.GetString(bytes, original.Start, original.End - original.Start);
        var existing = ParseValue(text);
        if (existing is JsonValue existingValue && desired is JsonValue desiredValue &&
            existingValue.TryGetValue<string>(out var existingText) && desiredValue.TryGetValue<string>(out var desiredText)) {
            return string.Equals(existingText, desiredText, StringComparison.OrdinalIgnoreCase);
        }
        return JsonNode.DeepEquals(existing, desired);
    }

    private static List<string> GetContainerComments(byte[] bytes, SpanNode node, IEnumerable<(int Start, int End)> entries) {
        var comments = new List<string>();
        var cursor = node.Start + 1;
        foreach (var (start, end) in entries) {
            comments.AddRange(ReadComments(bytes, cursor, start));
            cursor = end;
        }
        comments.AddRange(ReadComments(bytes, cursor, node.End - 1));
        return comments;
    }

    private static IEnumerable<string> ReadComments(byte[] bytes, int start, int end) {
        var inString = false;
        var escaped = false;
        for (var i = start; i < end; i++) {
            var current = bytes[i];
            if (inString) {
                if (escaped) {
                    escaped = false;
                } else if (current == '\\') {
                    escaped = true;
                } else if (current == '"') {
                    inString = false;
                }
                continue;
            }
            if (current == '"') {
                inString = true;
                continue;
            }
            if (current != '/' || i + 1 >= end) {
                continue;
            }
            var next = bytes[i + 1];
            if (next == '/') {
                var commentStart = i;
                i += 2;
                while (i < end && bytes[i] is not ((byte)'\r') and not ((byte)'\n')) {
                    i++;
                }
                yield return Utf8.GetString(bytes, commentStart, i - commentStart);
            } else if (next == '*') {
                var commentStart = i;
                i += 2;
                while (i + 1 < end && !(bytes[i] == '*' && bytes[i + 1] == '/')) {
                    i++;
                }
                if (i + 1 < end) {
                    i++;
                    yield return Utf8.GetString(bytes, commentStart, i + 1 - commentStart);
                }
            }
        }
    }

    private static string FormatContainer(char open, char close, List<string> values, List<string> comments) {
        if (values.Count == 0 && comments.Count == 0) {
            return string.Concat(open, close);
        }
        var content = string.Join(", ", values);
        if (comments.Count > 0) {
            content += (content.Length > 0 ? "\n" : "") + string.Join("\n", comments) + "\n";
        }
        return open + content + close;
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
