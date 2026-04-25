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

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() {
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) },
    };

    public static ConfigLoadResult Load() => Load(ConfigPath);

    public static ConfigLoadResult Load(string path) {
        var (config, parseError) = ReadConfig(path);
        var violations = Validate(config);
        if (parseError is not null) {
            violations.Insert(0, parseError);
        }
        return new ConfigLoadResult { Config = config, Violations = violations };
    }

    private static (ConfigModel Config, string? ParseError) ReadConfig(string path) {
        if (!File.Exists(path)) {
            return (new ConfigModel(), null);
        }

        try {
            var json = File.ReadAllText(path);
            return (System.Text.Json.JsonSerializer.Deserialize<ConfigModel>(json, JsonOptions) ?? new ConfigModel(), null);
        } catch (System.Text.Json.JsonException ex) {
            return (new ConfigModel(), $"Config file could not be parsed: {ex.Message}");
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

        // Check actionBindings for reserved keys and unrecognized VKey names
        foreach (var binding in config.ActionBindings) {
            if (!Enum.TryParse<VKey>(binding.Key, true, out var vkey)) {
                violations.Add($"Unrecognized VKey name '{binding.Key}' in actionBindings.");
                continue;
            }
            if (ReservedKeys.Contains(vkey)) {
                violations.Add($"Reserved key '{binding.Key}' may not be used in actionBindings.");
            }
        }

        // Check for overlap between navigation keys and action keys (explicit + implicit Space)
        var allNavKeys = new HashSet<VKey>(config.FirstKeys);
        allNavKeys.UnionWith(config.SecondKeys);

        // Collect all effective action keys (explicit bindings + implicit Space default)
        var actionKeys = new HashSet<VKey>();
        foreach (var binding in config.ActionBindings) {
            if (Enum.TryParse<VKey>(binding.Key, true, out var vkey)) {
                actionKeys.Add(vkey);
            }
        }
        actionKeys.Add(VKey.Space); // implicit default

        foreach (var actionKey in actionKeys) {
            if (allNavKeys.Contains(actionKey)) {
                violations.Add($"Action key '{actionKey}' conflicts with a navigation key.");
            }
        }

        // Check hotkey trigger key is not in navigation or action sets
        if (allNavKeys.Contains(config.HotKey.Key)) {
            violations.Add($"Hotkey trigger '{config.HotKey.Key}' conflicts with a navigation key.");
        }

        return violations;
    }
}
