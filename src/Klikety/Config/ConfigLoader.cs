using System.IO;

using Klikety.Input;

namespace Klikety.Config;

/// <summary>
/// Result of loading and validating configuration.
/// </summary>
public sealed class ConfigLoadResult {
    public required ConfigModel Config { get; init; }
    public IReadOnlyList<string> Violations { get; init; } = [];
}

/// <summary>
/// Loads config from %APPDATA%\Klikety\config.json (JSONC).
/// Returns defaults when file is absent or fields are missing.
/// Validates key-binding constraints and collects violations.
/// </summary>
public static class ConfigLoader {
    private static readonly string ConfigFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety");

    private static readonly string ConfigPath = Path.Combine(ConfigFolder, "config.json");

    /// <summary>
    /// VKeys that are permanently reserved and may not appear in firstKeys, secondKeys, or ActionBindings.
    /// </summary>
    private static readonly HashSet<VKey> ReservedKeys =
    [
        VKey.Escape,
        VKey.Left, VKey.Right, VKey.Up, VKey.Down,
        VKey.Return,
    ];

    public static ConfigLoadResult Load() => Load(ConfigPath);

    public static ConfigLoadResult Load(string path) {
        var config = ReadConfig(path);
        var violations = Validate(config);
        return new ConfigLoadResult { Config = config, Violations = violations };
    }

    private static ConfigModel ReadConfig(string path) {
        if (!File.Exists(path)) {
            return new ConfigModel();
        }

        try {
            var json = File.ReadAllText(path);
            var options = new System.Text.Json.JsonSerializerOptions {
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) },
            };
            return System.Text.Json.JsonSerializer.Deserialize<ConfigModel>(json, options) ?? new ConfigModel();
        } catch (System.Text.Json.JsonException) {
            // Malformed JSON → fall back to defaults; caller sees violation list
            return new ConfigModel();
        }
    }

    private static List<string> Validate(ConfigModel config) {
        var violations = new List<string>();

        var firstSet = new HashSet<VKey>(config.FirstKeys);
        var secondSet = new HashSet<VKey>(config.SecondKeys);

        // Check firstKeys for reserved keys
        foreach (var key in config.FirstKeys) {
            if (ReservedKeys.Contains(key)) {
                violations.Add($"Reserved key '{key}' may not be used in firstKeys.");
            }
        }

        // Check secondKeys for reserved keys
        foreach (var key in config.SecondKeys) {
            if (ReservedKeys.Contains(key)) {
                violations.Add($"Reserved key '{key}' may not be used in secondKeys.");
            }
        }

        // Check firstKeys ∩ secondKeys = ∅
        foreach (var overlap in firstSet.Intersect(secondSet)) {
            violations.Add($"Key '{overlap}' appears in both firstKeys and secondKeys.");
        }

        // Validate non-empty
        if (config.FirstKeys.Length == 0) {
            violations.Add("firstKeys must not be empty.");
        }

        if (config.SecondKeys.Length == 0) {
            violations.Add("secondKeys must not be empty.");
        }

        // Validate no duplicates
        if (config.FirstKeys.Length != firstSet.Count) {
            violations.Add("firstKeys contains duplicate keys.");
        }

        if (config.SecondKeys.Length != secondSet.Count) {
            violations.Add("secondKeys contains duplicate keys.");
        }

        // Check actionBindings for reserved keys
        foreach (var binding in config.ActionBindings) {
            if (Enum.TryParse<VKey>(binding.Key, true, out var vkey) && ReservedKeys.Contains(vkey)) {
                violations.Add($"Reserved key '{binding.Key}' may not be used in actionBindings.");
            }
        }

        // Check for overlap between navigation keys and action keys
        var allNavKeys = new HashSet<VKey>(config.FirstKeys);
        allNavKeys.UnionWith(config.SecondKeys);
        foreach (var binding in config.ActionBindings) {
            if (Enum.TryParse<VKey>(binding.Key, true, out var vkey) && allNavKeys.Contains(vkey)) {
                violations.Add($"Action key '{binding.Key}' conflicts with a navigation key.");
            }
        }

        // Check hotkey trigger key is not in navigation or action sets
        if (allNavKeys.Contains(config.HotKey.Key)) {
            violations.Add($"Hotkey trigger '{config.HotKey.Key}' conflicts with a navigation key.");
        }

        return violations;
    }
}
