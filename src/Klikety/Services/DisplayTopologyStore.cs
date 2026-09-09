using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klikety.Services;

/// <summary>
/// Persists display numbers by topology fingerprint in
/// <c>%APPDATA%\Klikety\display-topologies.json</c>, not <c>config.json</c>.
/// Overlay-host display is not part of the fingerprint and does not rewrite numbers.
/// </summary>
public sealed class DisplayTopologyStore {
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Klikety",
        "display-topologies.json");

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;

    public DisplayTopologyStore() : this(DefaultPath) { }

    internal DisplayTopologyStore(string path) => _path = path;

    public IReadOnlyDictionary<string, int> Resolve(IReadOnlyList<DisplayInfo> displays) {
        if (displays.Count == 0) {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var file = Load();
        var known = file.Topologies.Select(t => (
            t.Fingerprint ?? [],
            (IReadOnlyDictionary<string, int>)NormalizeNumbers(t.Numbers)));
        var numbers = DisplayNumbering.Resolve(displays, known, out bool isNew);
        if (isNew) {
            file.Topologies.Add(new TopologyRecord {
                Fingerprint = DisplayNumbering.Fingerprint(displays),
                Numbers = numbers.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            });
            try {
                Save(file);
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            }
        }

        return numbers;
    }

    private DisplayTopologiesFile Load() {
        if (!File.Exists(_path)) {
            return new DisplayTopologiesFile();
        }

        try {
            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize<DisplayTopologiesFile>(json, JsonOptions);
            if (file is null || file.Topologies is null) {
                return new DisplayTopologiesFile();
            }

            return file;
        } catch (IOException) {
            return new DisplayTopologiesFile();
        } catch (UnauthorizedAccessException) {
            return new DisplayTopologiesFile();
        } catch (JsonException) {
            return new DisplayTopologiesFile();
        }
    }

    private void Save(DisplayTopologiesFile file) {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, Path.GetRandomFileName());
        try {
            var json = JsonSerializer.Serialize(file, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _path, overwrite: true);
        } catch {
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }

    private static Dictionary<string, int> NormalizeNumbers(Dictionary<string, int>? numbers) {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (numbers is null) {
            return result;
        }

        foreach (var pair in numbers) {
            if (pair.Value is >= 1 and <= DisplayNumbering.MaxNumbered) {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    private sealed class DisplayTopologiesFile {
        public int Version { get; set; } = 1;
        public List<TopologyRecord> Topologies { get; set; } = [];
    }

    private sealed class TopologyRecord {
        public string[] Fingerprint { get; set; } = [];
        public Dictionary<string, int> Numbers { get; set; } = new(StringComparer.Ordinal);
    }
}
