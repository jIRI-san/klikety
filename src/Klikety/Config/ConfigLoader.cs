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
        } catch (IOException ex) {
            return (new ConfigModel(), $"Config file could not be read: {ex.Message}");
        } catch (UnauthorizedAccessException ex) {
            return (new ConfigModel(), $"Config file could not be read: {ex.Message}");
        }
    }

    private static List<string> Validate(ConfigModel config) {
        var violations = new List<string>();

        var horizSet = new HashSet<VKey>(config.HorizontalKeys);
        var vertSet = new HashSet<VKey>(config.VerticalKeys);

        // Check horizontalKeys for reserved keys
        foreach (var key in config.HorizontalKeys) {
            if (ReservedKeys.Contains(key)) {
                violations.Add($"Reserved key '{key}' may not be used in horizontalKeys.");
            }
        }

        // Check verticalKeys for reserved keys
        foreach (var key in config.VerticalKeys) {
            if (ReservedKeys.Contains(key)) {
                violations.Add($"Reserved key '{key}' may not be used in verticalKeys.");
            }
        }

        // Check horizontalKeys ∩ verticalKeys = ∅
        foreach (var overlap in horizSet.Intersect(vertSet)) {
            violations.Add($"Key '{overlap}' appears in both horizontalKeys and verticalKeys.");
        }

        // Validate non-empty
        if (config.HorizontalKeys.Length == 0) {
            violations.Add("horizontalKeys must not be empty.");
        }

        if (config.VerticalKeys.Length == 0) {
            violations.Add("verticalKeys must not be empty.");
        }

        // Validate no duplicates
        if (config.HorizontalKeys.Length != horizSet.Count) {
            violations.Add("horizontalKeys contains duplicate keys.");
        }

        if (config.VerticalKeys.Length != vertSet.Count) {
            violations.Add("verticalKeys contains duplicate keys.");
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
        var allNavKeys = new HashSet<VKey>(config.HorizontalKeys);
        allNavKeys.UnionWith(config.VerticalKeys);

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

        // Check hotkey modifier VKeys don't appear in navigation or action keys
        foreach (var modKey in hotkeyVKeys) {
            if (modKey == config.HotKey.Key) {
                continue;
            }

            if (allNavKeys.Contains(modKey)) {
                violations.Add($"Hotkey modifier '{modKey}' conflicts with a navigation key.");
            }

            if (actionKeys.Contains(modKey)) {
                violations.Add($"Hotkey modifier '{modKey}' conflicts with an action key.");
            }
        }

        // === Mode validation ===
        ValidateModes(config, violations, actionKeys, hotkeyVKeys);

        // === Scroll hotkey validation ===
        ValidateScrollHotKeys(config, violations, actionKeys, allNavKeys, hotkeyVKeys);

        // === Key press visualization validation ===
        ValidateKeyPressVisualization(config.KeyPressVisualization, violations);

        // === Macro key validation ===
        ValidateMacros(config, violations, actionKeys, allNavKeys, hotkeyVKeys);

        return violations;
    }

    private static void ValidateModes(ConfigModel config, List<string> violations, HashSet<VKey> actionKeys, HashSet<VKey> hotkeyVKeys) {
        var modes = config.Modes;
        var modeEntries = new (string Name, ModeConfig Config)[] {
            ("UniformGrid", modes.UniformGrid),
            ("Crosshair", modes.Crosshair),
            ("LogCrosshair", modes.LogCrosshair),
            ("LogGrid", modes.LogGrid),
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
        // Crosshair/LogCrosshair/LogGrid additionally require twoKey
        foreach (var (name, mc) in modeEntries) {
            if (!mc.Enabled) {
                continue;
            }

            if (name is "Crosshair" or "LogCrosshair" or "LogGrid" && !mc.TwoKey) {
                violations.Add($"{name}: Crosshair/LogCrosshair/LogGrid modes require twoKey: true.");
            } else if (!mc.TwoKey && !mc.ArrowKeys) {
                violations.Add($"{name}: enabled mode must have twoKey or arrowKeys (or both).");
            }
        }

        // Mode-specific: logBaseSize ∈ [2, 50]
        if (modes.LogCrosshair.Enabled && (modes.LogCrosshair.LogBaseSize < 2 || modes.LogCrosshair.LogBaseSize > 50)) {
            violations.Add($"LogCrosshair: logBaseSize must be between 2 and 50 (got {modes.LogCrosshair.LogBaseSize}).");
        }

        // Mode-specific: logGridBaseSize ∈ [2, 50]
        if (modes.LogGrid.Enabled && (modes.LogGrid.LogGridBaseSize < 2 || modes.LogGrid.LogGridBaseSize > 50)) {
            violations.Add($"LogGrid: logGridBaseSize must be between 2 and 50 (got {modes.LogGrid.LogGridBaseSize}).");
        }

        // LogGrid key-policy: evaluate axis key counts and emit warning if applicable
        if (modes.LogGrid.Enabled) {
            var keyPolicy = LogGridKeyPolicy.Evaluate(config.HorizontalKeys, config.VerticalKeys);
            if (keyPolicy.Warning is not null) {
                violations.Add(keyPolicy.Warning);
            }
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

        // Navigation keys vs chord keys
        foreach (var hk in config.HorizontalKeys) {
            if (chordKeys.ContainsKey(hk)) {
                violations.Add($"HorizontalKey '{hk}' conflicts with chord key.");
            }
        }
        foreach (var vk in config.VerticalKeys) {
            if (chordKeys.ContainsKey(vk)) {
                violations.Add($"VerticalKey '{vk}' conflicts with chord key.");
            }
        }
    }

    private static void ValidateScrollHotKeys(ConfigModel config, List<string> violations, HashSet<VKey> actionKeys, HashSet<VKey> navKeys, HashSet<VKey> hotkeyVKeys) {
        var scroll = config.ScrollHotKeys;
        if (!scroll.Enabled) {
            return;
        }

        // scrollAmount range
        if (scroll.ScrollAmount < 1 || scroll.ScrollAmount > 100) {
            violations.Add($"scrollAmount must be between 1 and 100 (got {scroll.ScrollAmount}).");
        }

        // Duplicate up/down key
        if (scroll.ScrollUpKey.Key == scroll.ScrollDownKey.Key &&
            scroll.ScrollUpKey.Modifiers == scroll.ScrollDownKey.Modifiers) {
            violations.Add("Scroll up and scroll down hotkeys must be different.");
        }

        // Collect chord keys for conflict checking
        var chordKeys = new HashSet<VKey>();
        var modes = config.Modes;
        foreach (var mc in new[] { modes.UniformGrid, modes.Crosshair, modes.LogCrosshair, modes.LogGrid }) {
            if (mc is { Enabled: true, ChordKey: { } chord }) {
                chordKeys.Add(chord);
            }
        }

        // Validate each scroll key
        foreach (var (label, hkc) in new[] { ("scrollUpKey", scroll.ScrollUpKey), ("scrollDownKey", scroll.ScrollDownKey) }) {
            var key = hkc.Key;

            if (ReservedKeys.Contains(key)) {
                violations.Add($"{label}: key '{key}' is reserved.");
            }

            if (actionKeys.Contains(key)) {
                violations.Add($"{label}: key '{key}' conflicts with an action binding.");
            }

            if (chordKeys.Contains(key)) {
                violations.Add($"{label}: key '{key}' conflicts with a chord key.");
            }

            if (navKeys.Contains(key)) {
                violations.Add($"{label}: key '{key}' conflicts with a navigation key.");
            }

            if (hotkeyVKeys.Contains(key)) {
                violations.Add($"{label}: key '{key}' conflicts with hotkey.");
            }
        }
    }

    private static void ValidateMacros(ConfigModel config, List<string> violations, HashSet<VKey> actionKeys, HashSet<VKey> navKeys, HashSet<VKey> hotkeyVKeys) {
        var macros = config.Macros;
        if (macros is null || !macros.Enabled) return;

        // SpeedModifier validation
        if (macros.SpeedModifier < 0) {
            violations.Add($"macros.speedModifier must be >= 0 (got {macros.SpeedModifier}); clamped to 0.");
        }

        // Collect chord keys for conflict checking
        var chordKeys = new HashSet<VKey>();
        var modes = config.Modes;
        foreach (var mc in new[] { modes.UniformGrid, modes.Crosshair, modes.LogCrosshair, modes.LogGrid }) {
            if (mc is { Enabled: true, ChordKey: { } chord }) {
                chordKeys.Add(chord);
            }
        }

        // Collect scroll keys
        var scrollKeys = new HashSet<VKey>();
        if (config.ScrollHotKeys.Enabled) {
            scrollKeys.Add(config.ScrollHotKeys.ScrollUpKey.Key);
            scrollKeys.Add(config.ScrollHotKeys.ScrollDownKey.Key);
        }

        // Intra-macro uniqueness
        if (macros.RecordKey == macros.HelperKey) {
            violations.Add("macros: recordKey and helperKey must be different.");
        }

        // SlotKeys null/length validation
        if (macros.SlotKeys is null) {
            violations.Add("macros.slotKeys is null.");
            return;
        }

        if (macros.SlotKeys.Length < 10) {
            violations.Add($"macros.slotKeys has fewer than 10 entries (got {macros.SlotKeys.Length}); missing slots will use defaults.");
        } else if (macros.SlotKeys.Length > 10) {
            violations.Add($"macros.slotKeys has more than 10 entries (got {macros.SlotKeys.Length}); extra entries ignored.");
        }

        // SlotKeys duplicate check
        var slotKeySet = new HashSet<VKey>();
        foreach (var sk in macros.SlotKeys) {
            if (!slotKeySet.Add(sk)) {
                violations.Add($"macros.slotKeys contains duplicate key '{sk}'.");
            }
        }

        // SlotKeys vs RecordKey/HelperKey
        foreach (var sk in macros.SlotKeys) {
            if (sk == macros.RecordKey) {
                violations.Add($"macros: slot key '{sk}' conflicts with recordKey.");
            }
            if (sk == macros.HelperKey) {
                violations.Add($"macros: slot key '{sk}' conflicts with helperKey.");
            }
        }

        // GlobalHotKey base key vs macro keys
        if (macros.GlobalHotKey is { } ghk) {
            if (ghk.Key == macros.RecordKey) {
                violations.Add($"macros: globalHotKey key '{ghk.Key}' conflicts with recordKey.");
            }
            if (ghk.Key == macros.HelperKey) {
                violations.Add($"macros: globalHotKey key '{ghk.Key}' conflicts with helperKey.");
            }
            if (slotKeySet.Contains(ghk.Key)) {
                violations.Add($"macros: globalHotKey key '{ghk.Key}' conflicts with a slot key.");
            }
            // GlobalHotKey vs main hotkey
            if (ghk.Key == config.HotKey.Key && ghk.Modifiers == config.HotKey.Modifiers) {
                violations.Add("macros: globalHotKey conflicts with the main application hotkey.");
            }
        }

        // Full collision matrix: each macro key vs existing key sets
        var macroKeysToCheck = new List<(string Label, VKey Key)> {
            ("macros.recordKey", macros.RecordKey),
            ("macros.helperKey", macros.HelperKey),
        };
        for (var i = 0; i < Math.Min(macros.SlotKeys.Length, 10); i++) {
            macroKeysToCheck.Add(($"macros.slotKeys[{i}]", macros.SlotKeys[i]));
        }

        foreach (var (label, key) in macroKeysToCheck) {
            if (ReservedKeys.Contains(key)) {
                violations.Add($"{label}: key '{key}' is reserved.");
            }
            if (actionKeys.Contains(key)) {
                violations.Add($"{label}: key '{key}' conflicts with an action binding.");
            }
            if (navKeys.Contains(key)) {
                violations.Add($"{label}: key '{key}' conflicts with a navigation key.");
            }
            if (chordKeys.Contains(key)) {
                violations.Add($"{label}: key '{key}' conflicts with a chord key.");
            }
            if (scrollKeys.Contains(key)) {
                violations.Add($"{label}: key '{key}' conflicts with a scroll hotkey.");
            }
            if (hotkeyVKeys.Contains(key)) {
                violations.Add($"{label}: key '{key}' conflicts with hotkey.");
            }
        }
    }

    private static readonly HashSet<string> ValidCorners = new(StringComparer.OrdinalIgnoreCase) {
        "TopLeft", "TopRight", "BottomLeft", "BottomRight",
    };

    private static void ValidateKeyPressVisualization(KeyPressVisualizationConfig kpv, List<string> violations) {
        if (kpv.FontSize <= 0) {
            violations.Add($"keyPressVisualization.fontSize must be > 0 (got {kpv.FontSize}).");
        }

        if (kpv.OutlineThickness < 0) {
            violations.Add($"keyPressVisualization.outlineThickness must be >= 0 (got {kpv.OutlineThickness}).");
        }

        if (kpv.Margin < 0) {
            violations.Add($"keyPressVisualization.margin must be >= 0 (got {kpv.Margin}).");
        }

        if (!IsValidHexColor(kpv.FontColor)) {
            violations.Add($"keyPressVisualization.fontColor is not a valid hex color (got '{kpv.FontColor}').");
        }

        if (!IsValidHexColor(kpv.OutlineColor)) {
            violations.Add($"keyPressVisualization.outlineColor is not a valid hex color (got '{kpv.OutlineColor}').");
        }

        if (kpv.FadeTimeoutMs < 0) {
            violations.Add($"keyPressVisualization.fadeTimeoutMs must be >= 0 (got {kpv.FadeTimeoutMs}).");
        }

        if (kpv.FadeDurationMs <= 0) {
            violations.Add($"keyPressVisualization.fadeDurationMs must be > 0 (got {kpv.FadeDurationMs}).");
        }

        if (kpv.MaxVisibleKeys < 1 || kpv.MaxVisibleKeys > 10) {
            violations.Add($"keyPressVisualization.maxVisibleKeys must be between 1 and 10 (got {kpv.MaxVisibleKeys}).");
        }

        if (!ValidCorners.Contains(kpv.Corner)) {
            violations.Add($"keyPressVisualization.corner must be one of TopLeft, TopRight, BottomLeft, BottomRight (got '{kpv.Corner}').");
        }

        if (kpv.RepeatWindowMs < 50 || kpv.RepeatWindowMs > 1000) {
            violations.Add($"keyPressVisualization.repeatWindowMs must be between 50 and 1000 (got {kpv.RepeatWindowMs}).");
        }
    }

    private static bool IsValidHexColor(string color) {
        if (string.IsNullOrEmpty(color) || color[0] != '#') return false;
        return color.Length is 7 or 9 && color[1..].All(c => char.IsAsciiHexDigit(c));
    }
}
