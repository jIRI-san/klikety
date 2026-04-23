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

    ValidateHalf(config.KeySets.Left, "left", violations);
    ValidateHalf(config.KeySets.Right, "right", violations);

    // Check for overlap between left and right first keys
    var leftFirstSet = new HashSet<VKey>(config.KeySets.Left.FirstKeys);
    var rightFirstSet = new HashSet<VKey>(config.KeySets.Right.FirstKeys);
    foreach (var overlap in leftFirstSet.Intersect(rightFirstSet))
    {
      violations.Add($"Key '{overlap}' appears in both left.firstKeys and right.firstKeys.");
    }

        // Check actionBindings for reserved keys
        foreach (var binding in config.ActionBindings)
        {
            if (Enum.TryParse<VKey>(binding.Key, true, out var vkey) && ReservedKeys.Contains(vkey))
                violations.Add($"Reserved key '{binding.Key}' may not be used in actionBindings.");
        }

    // Check for overlap between all navigation keys and action keys
    var allNavKeys = new HashSet<VKey>(config.KeySets.Left.FirstKeys);
    allNavKeys.UnionWith(config.KeySets.Left.SecondKeys);
    allNavKeys.UnionWith(config.KeySets.Right.FirstKeys);
    allNavKeys.UnionWith(config.KeySets.Right.SecondKeys);
    foreach (var binding in config.ActionBindings)
        {
      if (Enum.TryParse<VKey>(binding.Key, true, out var vkey) && allNavKeys.Contains(vkey))
        violations.Add($"Action key '{binding.Key}' conflicts with a navigation key.");
        }

    // Check hotkey trigger key is not in navigation or action sets
    if (allNavKeys.Contains(config.HotKey.Key))
      violations.Add($"Hotkey trigger '{config.HotKey.Key}' conflicts with a navigation key.");

    return violations;
  }

  private static void ValidateHalf(HalfKeySetsConfig half, string name, List<string> violations)
  {
    // Check firstKeys for reserved keys
    foreach (var key in half.FirstKeys)
    {
      if (ReservedKeys.Contains(key))
        violations.Add($"Reserved key '{key}' may not be used in {name}.firstKeys.");
    }

    // Check secondKeys for reserved keys
    foreach (var key in half.SecondKeys)
    {
      if (ReservedKeys.Contains(key))
        violations.Add($"Reserved key '{key}' may not be used in {name}.secondKeys.");
    }

    // Check for overlap between firstKeys and secondKeys within half
    var firstSet = new HashSet<VKey>(half.FirstKeys);
    var secondSet = new HashSet<VKey>(half.SecondKeys);
    foreach (var overlap in firstSet.Intersect(secondSet))
    {
      violations.Add($"Key '{overlap}' appears in both {name}.firstKeys and {name}.secondKeys.");
    }

    // Validate non-empty
    if (half.FirstKeys.Length == 0)
      violations.Add($"{name}.firstKeys must not be empty.");
    if (half.SecondKeys.Length == 0)
      violations.Add($"{name}.secondKeys must not be empty.");

    // Validate no duplicates
    if (half.FirstKeys.Length != firstSet.Count)
      violations.Add($"{name}.firstKeys contains duplicate keys.");
    if (half.SecondKeys.Length != secondSet.Count)
      violations.Add($"{name}.secondKeys contains duplicate keys.");
  }
}
