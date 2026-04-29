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
        // Run migration pre-pass before deserialization
        var migration = ConfigMigrator.MigrateIfNeeded(path);
        if (migration.BlockingError is not null) {
            return new ConfigLoadResult {
                Config = new ConfigModel(),
                Violations = [migration.BlockingError],
            };
        }

        var (config, parseError) = ReadConfig(path);
        var violations = Validate(config);
        if (parseError is not null) {
            violations.Insert(0, parseError);
        }
        // Append migration warnings as non-blocking violations
        foreach (var w in migration.Warnings) {
            violations.Add(w);
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

        // Collect all effective action keys (explicit bindings + implicit Space default)
        var actionKeys = new HashSet<VKey>();
        foreach (var binding in config.ActionBindings) {
            if (Enum.TryParse<VKey>(binding.Key, true, out var vkey)) {
                actionKeys.Add(vkey);
            }
        }
        actionKeys.Add(VKey.Space); // implicit default

        // Check for overlap between navigation keys and action keys
        var allNavKeys = new HashSet<VKey>(config.FirstKeys);
        allNavKeys.UnionWith(config.SecondKeys);

        foreach (var actionKey in actionKeys) {
            if (allNavKeys.Contains(actionKey)) {
                violations.Add($"Action key '{actionKey}' conflicts with a navigation key.");
            }
        }

        // Check hotkey trigger key is not in navigation or action sets
        if (allNavKeys.Contains(config.HotKey.Key)) {
            violations.Add($"Hotkey trigger '{config.HotKey.Key}' conflicts with a navigation key.");
        }

        // Hotkey modifier VKeys for conflict checking
        var hotkeyVKeys = new HashSet<VKey>();
        hotkeyVKeys.Add(config.HotKey.Key);
        if (config.HotKey.Modifiers.HasFlag(HotKeyModifiers.Alt)) { hotkeyVKeys.Add(VKey.Menu); hotkeyVKeys.Add(VKey.LMenu); hotkeyVKeys.Add(VKey.RMenu); }
        if (config.HotKey.Modifiers.HasFlag(HotKeyModifiers.Control)) { hotkeyVKeys.Add(VKey.Control); hotkeyVKeys.Add(VKey.LControl); hotkeyVKeys.Add(VKey.RControl); }
        if (config.HotKey.Modifiers.HasFlag(HotKeyModifiers.Shift)) { hotkeyVKeys.Add(VKey.Shift); hotkeyVKeys.Add(VKey.LShift); hotkeyVKeys.Add(VKey.RShift); }
        if (config.HotKey.Modifiers.HasFlag(HotKeyModifiers.Win)) { hotkeyVKeys.Add(VKey.LWin); hotkeyVKeys.Add(VKey.RWin); }

        // === Mode validation ===
        ValidateModes(config, violations, actionKeys, hotkeyVKeys);

        return violations;
    }

    private static void ValidateModes(ConfigModel config, List<string> violations, HashSet<VKey> actionKeys, HashSet<VKey> hotkeyVKeys) {
        var modes = config.Modes;
        var modeEntries = new (string Name, ModeConfig Config)[] {
            ("UniformGrid", modes.UniformGrid),
            ("Crosshair", modes.Crosshair),
            ("LogCrosshair", modes.LogCrosshair),
        };

        // Structural: at least one enabled
        if (!modeEntries.Any(m => m.Config.Enabled)) {
            violations.Add("At least one mode must be enabled.");
        }

        // Structural: exactly one default
        var defaultCount = modeEntries.Count(m => m.Config.Default);
        if (defaultCount == 0) {
            violations.Add("Exactly one mode must be marked as default.");
        } else if (defaultCount > 1) {
            violations.Add("Only one mode may be marked as default.");
        }

        // Default mode must be enabled
        foreach (var (name, mc) in modeEntries) {
            if (mc.Default && !mc.Enabled) {
                violations.Add($"{name}: default mode must be enabled.");
            }
        }

        // Every enabled mode must have twoKey || arrowKeys
        // Crosshair/LogCrosshair additionally require twoKey
        foreach (var (name, mc) in modeEntries) {
            if (!mc.Enabled) {
                continue;
            }

            if (name is "Crosshair" or "LogCrosshair" && !mc.TwoKey) {
                violations.Add($"{name}: Crosshair/LogCrosshair modes require twoKey: true.");
            } else if (!mc.TwoKey && !mc.ArrowKeys) {
                violations.Add($"{name}: enabled mode must have twoKey or arrowKeys (or both).");
            }
        }

        // Mode-specific: logBaseSize ∈ [2, 50]
        if (modes.LogCrosshair.Enabled && (modes.LogCrosshair.LogBaseSize < 2 || modes.LogCrosshair.LogBaseSize > 50)) {
            violations.Add($"LogCrosshair: logBaseSize must be between 2 and 50 (got {modes.LogCrosshair.LogBaseSize}).");
        }

        // Chord keys: collect and check mutual uniqueness
        var chordKeys = new Dictionary<VKey, string>();
        foreach (var (name, mc) in modeEntries) {
            if (!mc.Enabled) {
                continue;
            }

            // Enabled non-default mode must have a chord key
            if (!mc.Default && mc.ChordKey is null) {
                violations.Add($"{name}: enabled non-default mode must have a chordKey.");
                continue;
            }

            if (mc.ChordKey is not { } chord) {
                continue;
            }

            // Chord vs reserved
            if (ReservedKeys.Contains(chord)) {
                violations.Add($"{name}: chord key '{chord}' is reserved.");
            }

            // Chord vs action bindings
            if (actionKeys.Contains(chord)) {
                violations.Add($"{name}: chord key '{chord}' conflicts with an action binding.");
            }

            // Chord vs hotkey
            if (hotkeyVKeys.Contains(chord)) {
                violations.Add($"{name}: chord key '{chord}' conflicts with hotkey.");
            }

            // Cross-mode: chord keys mutually unique
            if (chordKeys.TryGetValue(chord, out var otherMode)) {
                violations.Add($"Chord key '{chord}' is used by both {otherMode} and {name}.");
            } else {
                chordKeys[chord] = name;
            }
        }

        // Per-mode axis key validation for Crosshair/LogCrosshair
        foreach (var (name, mc) in modeEntries) {
            if (name == "UniformGrid" || !mc.Enabled) {
                continue;
            }

            ValidateAxisKeys(name, "horizontalKeys", mc.HorizontalKeys, violations, actionKeys, hotkeyVKeys);
            ValidateAxisKeys(name, "verticalKeys", mc.VerticalKeys, violations, actionKeys, hotkeyVKeys);

            // No overlap between horiz and vert within same mode
            if (mc.HorizontalKeys is not null && mc.VerticalKeys is not null) {
                var horizSet = new HashSet<VKey>(mc.HorizontalKeys);
                foreach (var vk in mc.VerticalKeys) {
                    if (horizSet.Contains(vk)) {
                        violations.Add($"{name}: key '{vk}' appears in both horizontalKeys and verticalKeys.");
                    }
                }
            }

            // Axis keys vs chord keys
            if (mc.HorizontalKeys is not null) {
                foreach (var vk in mc.HorizontalKeys) {
                    if (chordKeys.ContainsKey(vk)) {
                        violations.Add($"{name}: horizontalKey '{vk}' conflicts with chord key.");
                    }
                }
            }
            if (mc.VerticalKeys is not null) {
                foreach (var vk in mc.VerticalKeys) {
                    if (chordKeys.ContainsKey(vk)) {
                        violations.Add($"{name}: verticalKey '{vk}' conflicts with chord key.");
                    }
                }
            }
        }

        // UniformGrid: firstKeys/secondKeys vs chord keys
        foreach (var fk in config.FirstKeys) {
            if (chordKeys.ContainsKey(fk)) {
                violations.Add($"UniformGrid firstKey '{fk}' conflicts with chord key.");
            }
        }
        foreach (var sk in config.SecondKeys) {
            if (chordKeys.ContainsKey(sk)) {
                violations.Add($"UniformGrid secondKey '{sk}' conflicts with chord key.");
            }
        }
    }

    private static void ValidateAxisKeys(string modeName, string arrayName, VKey[]? keys, List<string> violations, HashSet<VKey> actionKeys, HashSet<VKey> hotkeyVKeys) {
        if (keys is null || keys.Length == 0) {
            violations.Add($"{modeName}: {arrayName} must not be empty when mode is enabled.");
            return;
        }

        var seen = new HashSet<VKey>();
        foreach (var vk in keys) {
            // Reserved
            if (ReservedKeys.Contains(vk)) {
                violations.Add($"{modeName}: reserved key '{vk}' may not be used in {arrayName}.");
            }

            // Duplicates
            if (!seen.Add(vk)) {
                violations.Add($"{modeName}: {arrayName} contains duplicate key '{vk}'.");
            }

            // Vs action bindings
            if (actionKeys.Contains(vk)) {
                violations.Add($"{modeName}: {arrayName} key '{vk}' conflicts with an action binding.");
            }

            // Vs hotkey
            if (hotkeyVKeys.Contains(vk)) {
                violations.Add($"{modeName}: {arrayName} key '{vk}' conflicts with hotkey.");
            }
        }
    }
}
