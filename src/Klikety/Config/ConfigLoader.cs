using System.IO;
using Klikety.Input;

namespace Klikety.Config;

/// <summary>
/// Result of loading and validating configuration.
/// </summary>
public sealed class ConfigLoadResult
{
    public required ConfigModel Config { get; init; }
    public IReadOnlyList<string> Violations { get; init; } = [];
}

/// <summary>
/// Loads config from %APPDATA%\Klikety\config.json (JSONC).
/// Returns defaults when file is absent or fields are missing.
/// Validates key-binding constraints and collects violations.
/// </summary>
public static class ConfigLoader
{
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

    public static ConfigLoadResult Load(string path)
    {
        var config = ReadConfig(path);
        var violations = Validate(config);
        return new ConfigLoadResult { Config = config, Violations = violations };
    }

    private static ConfigModel ReadConfig(string path)
    {
        if (!File.Exists(path))
            return new ConfigModel();

        try
        {
            var json = File.ReadAllText(path);
            var options = new System.Text.Json.JsonSerializerOptions
            {
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) },
            };
            return System.Text.Json.JsonSerializer.Deserialize<ConfigModel>(json, options) ?? new ConfigModel();
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed JSON → fall back to defaults; caller sees violation list
            return new ConfigModel();
        }
    }

    private static List<string> Validate(ConfigModel config)
    {
        var violations = new List<string>();

        // Check firstKeys for reserved keys
        foreach (var key in config.KeySets.FirstKeys)
        {
            if (ReservedKeys.Contains(key))
                violations.Add($"Reserved key '{key}' may not be used in firstKeys.");
        }

        // Check secondKeys for reserved keys
        foreach (var key in config.KeySets.SecondKeys)
        {
            if (ReservedKeys.Contains(key))
                violations.Add($"Reserved key '{key}' may not be used in secondKeys.");
        }

        // Check actionBindings for reserved keys
        foreach (var binding in config.ActionBindings)
        {
            if (Enum.TryParse<VKey>(binding.Key, true, out var vkey) && ReservedKeys.Contains(vkey))
                violations.Add($"Reserved key '{binding.Key}' may not be used in actionBindings.");
        }

        // Check for overlap between firstKeys and secondKeys
        var firstSet = new HashSet<VKey>(config.KeySets.FirstKeys);
        var secondSet = new HashSet<VKey>(config.KeySets.SecondKeys);
        foreach (var overlap in firstSet.Intersect(secondSet))
        {
            violations.Add($"Key '{overlap}' appears in both firstKeys and secondKeys.");
        }

        // Check for overlap between navigation keys (first + second) and action keys
        var navKeys = new HashSet<VKey>(firstSet);
        navKeys.UnionWith(secondSet);
        foreach (var binding in config.ActionBindings)
        {
            if (Enum.TryParse<VKey>(binding.Key, true, out var vkey) && navKeys.Contains(vkey))
                violations.Add($"Action key '{binding.Key}' conflicts with a navigation key.");
        }

        // Check hotkey trigger key is not in navigation or action sets
        if (navKeys.Contains(config.HotKey.Key))
            violations.Add($"Hotkey trigger '{config.HotKey.Key}' conflicts with a navigation key.");

        // Validate firstKeys and secondKeys are non-empty
        if (config.KeySets.FirstKeys.Length == 0)
            violations.Add("firstKeys must not be empty.");
        if (config.KeySets.SecondKeys.Length == 0)
            violations.Add("secondKeys must not be empty.");

        // Validate firstKeys has no duplicates
        if (config.KeySets.FirstKeys.Length != new HashSet<VKey>(config.KeySets.FirstKeys).Count)
            violations.Add("firstKeys contains duplicate keys.");
        if (config.KeySets.SecondKeys.Length != new HashSet<VKey>(config.KeySets.SecondKeys).Count)
            violations.Add("secondKeys contains duplicate keys.");

        return violations;
    }
}
