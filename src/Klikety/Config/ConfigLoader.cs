using System.IO;

using Klikety.Input;

namespace Klikety.Config;

/// <summary>
/// Result of loading and validating configuration.
/// </summary>
public sealed class ConfigLoadResult {
    public required ConfigModel Config { get; init; }
    public IReadOnlyList<string> Violations { get; init; } = [];
    public IReadOnlyList<string> SettingsBlockingErrors { get; init; } = [];
    public IReadOnlyList<string> SettingsWarnings { get; init; } = [];
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
        VKey.D1, VKey.D2, VKey.D3, VKey.D4, VKey.D5,
        VKey.D6, VKey.D7, VKey.D8, VKey.D9,
    ];

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() {
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) },
    };

    public static ConfigLoadResult Load() => Load(ConfigPath);

    // Settings must never migrate or substitute defaults for an unreadable document.
    internal static ConfigLoadResult ReadSettings(string json, IEnumerable<string>? editedPaths = null) {
        using var document = System.Text.Json.JsonDocument.Parse(json, new() {
            CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) {
            throw new InvalidDataException("Settings config must be a JSON object.");
        }
        var config = System.Text.Json.JsonSerializer.Deserialize<ConfigModel>(json, JsonOptions)
            ?? throw new InvalidDataException("Settings config is null.");
        if (config.ConfigVersion != 7) {
            throw new InvalidDataException("Settings edits version 7 only. Reload/migrate the config before opening Settings.");
        }
        if (config.HotKey is null || config.Modes is null ||
            config.Modes.UniformGrid is null || config.Modes.Crosshair is null ||
            config.Modes.LogCrosshair is null || config.Modes.LogGrid is null ||
            config.HorizontalKeys is null || config.VerticalKeys is null ||
            config.ActionBindings is null || config.ScrollHotKeys is null ||
            config.ScrollHotKeys.ScrollUpKey is null || config.ScrollHotKeys.ScrollDownKey is null ||
            config.KeyPressVisualization is null || config.Macros is null ||
            config.Macros.PlaybackIndicator is null || config.AppScope is null ||
            config.Theme is null || config.LogLevel is null) {
            throw new InvalidDataException("Required config sections/values cannot be null.");
        }
        var violations = Validate(config);
        if (!Enum.GetNames<Microsoft.Extensions.Logging.LogLevel>().Contains(config.LogLevel, StringComparer.OrdinalIgnoreCase)) {
            violations.Add($"logLevel must be one of Trace, Debug, Information, Warning, Error, Critical, or None (got '{config.LogLevel}').");
        }
        if (config.RetainedLogFileCount < 1) {
            violations.Add($"retainedLogFileCount must be at least 1 (got {config.RetainedLogFileCount}).");
        }
        if (!double.IsFinite(config.MinLabelFontSize) || config.MinLabelFontSize <= 0) {
            violations.Add("Minimum label size must be a finite number greater than zero.");
        }
        if (config.Level3CellSizeThreshold < 0) {
            violations.Add("Level-3 area threshold must be zero or greater.");
        }
        ValidateSettingsFields(config, violations);
        var changed = editedPaths?.ToArray() ?? [];
        var warnings = GetSettingsWarnings(config, changed);
        return new ConfigLoadResult {
            Config = config,
            Violations = violations,
            SettingsBlockingErrors = violations.Where(error =>
                !warnings.Contains(error, StringComparer.Ordinal) && !IsUneditedLegacyFloor(config, error, changed)).ToArray(),
            SettingsWarnings = warnings,
        };
    }

    private static void ValidateSettingsFields(ConfigModel config, List<string> errors) {
        void Key(string path, VKey key) {
            if (!Enum.IsDefined(key)) { errors.Add($"{path}: unrecognized physical key."); }
        }
        void Hotkey(string path, HotKeyConfig hotkey) {
            Key(path + ".key", hotkey.Key);
            if (((int)hotkey.Modifiers & ~15) != 0) { errors.Add($"{path}.modifiers: invalid modifier flags."); }
        }
        Hotkey("hotKey", config.HotKey);
        Hotkey("scrollHotkeys.scrollUpKey", config.ScrollHotKeys.ScrollUpKey);
        Hotkey("scrollHotkeys.scrollDownKey", config.ScrollHotKeys.ScrollDownKey);
        if (config.ScrollHotKeys.ScrollAmount is < 1 or > 100) {
            errors.Add("scrollHotkeys.scrollAmount must be between 1 and 100.");
        }
        foreach (var (name, mode) in new[] {
            ("uniformGrid", config.Modes.UniformGrid), ("crosshair", config.Modes.Crosshair),
            ("logCrosshair", config.Modes.LogCrosshair), ("logGrid", config.Modes.LogGrid),
        }) {
            if (mode.ChordKey is { } chord) { Key($"modes.{name}.chordKey", chord); }
            if (mode.LogBaseSize is < 2 or > 50) { errors.Add($"modes.{name}.logBaseSize must be between 2 and 50."); }
            if (mode.LogGridBaseSize is < 2 or > 50) { errors.Add($"modes.{name}.logGridBaseSize must be between 2 and 50."); }
        }
        if (config.AppScope.ChordKey is { } scope) { Key("appScope.chordKey", scope); }
        foreach (var key in config.HorizontalKeys) { Key("horizontalKeys", key); }
        foreach (var key in config.VerticalKeys) { Key("verticalKeys", key); }
        foreach (var (name, action) in config.ActionBindings) {
            if (!Enum.TryParse<VKey>(name, true, out var key) || !Enum.IsDefined(key)) {
                errors.Add($"actionBindings: unrecognized key '{name}'.");
            }
            if (!Enum.IsDefined(action)) { errors.Add($"actionBindings: unrecognized action for '{name}'."); }
        }
        Key("macros.recordKey", config.Macros.RecordKey);
        Key("macros.helperKey", config.Macros.HelperKey);
        if (config.Macros.GlobalHotKey is { } global) { Hotkey("macros.globalHotKey", global); }
        if (config.Macros.SlotKeys is null) { errors.Add("macros.slotKeys cannot be null."); }
        else { foreach (var key in config.Macros.SlotKeys) { Key("macros.slotKeys", key); } }
    }

    private static List<string> GetSettingsWarnings(ConfigModel config, IReadOnlyCollection<string>? editedPaths = null) {
        var warnings = new List<string>();
        if (config.Modes.LogGrid.Enabled &&
            LogGridKeyPolicy.Evaluate(config.HorizontalKeys, config.VerticalKeys).Warning is { } keyWarning) {
            warnings.Add(keyWarning);
        }
        if (config.Macros.Enabled && config.Macros.SlotKeys is { } keys) {
            if (keys.Length < 10) {
                warnings.Add($"macros.slotKeys has fewer than 10 entries (got {keys.Length}); missing slots will use defaults.");
            } else if (keys.Length > 10) {
                warnings.Add($"macros.slotKeys has more than 10 entries (got {keys.Length}); extra entries ignored.");
            }
        }
        var indicator = config.Macros.PlaybackIndicator;
        AddLegacyFloorWarning("macros.playbackIndicator.initialRadius", indicator.InitialRadius is > 0 and < 1,
            IndicatorFloorError("macros.playbackIndicator.initialRadius", "1 DIP", indicator.InitialRadius), editedPaths);
        AddLegacyFloorWarning("macros.playbackIndicator.finalRadius", indicator.FinalRadius is > 0 and < 1,
            IndicatorFloorError("macros.playbackIndicator.finalRadius", "1 DIP", indicator.FinalRadius), editedPaths);
        AddLegacyFloorWarning("macros.playbackIndicator.animationDurationMs", indicator.AnimationDurationMs is > 0 and < 100,
            IndicatorFloorError("macros.playbackIndicator.animationDurationMs", "100 ms", indicator.AnimationDurationMs), editedPaths);
        return warnings;

        void AddLegacyFloorWarning(string path, bool belowFloor, string message, IReadOnlyCollection<string>? changedPaths) {
            if (belowFloor && !IsEdited(path, changedPaths)) {
                warnings.Add(message + "; existing legacy value is retained until edited.");
            }
        }
    }

    private static bool IsEdited(string path, IReadOnlyCollection<string>? changedPaths) =>
        changedPaths?.Any(changed => path.Equals(changed, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(changed + ".", StringComparison.OrdinalIgnoreCase) ||
            changed.StartsWith(path + ".", StringComparison.OrdinalIgnoreCase)) == true;

    private static bool IsUneditedLegacyFloor(ConfigModel config, string error, IReadOnlyCollection<string> editedPaths) {
        var indicator = config.Macros.PlaybackIndicator;
        return !IsEdited("macros.playbackIndicator.initialRadius", editedPaths) && indicator.InitialRadius is > 0 and < 1 &&
                   error == IndicatorFloorError("macros.playbackIndicator.initialRadius", "1 DIP", indicator.InitialRadius) ||
               !IsEdited("macros.playbackIndicator.finalRadius", editedPaths) && indicator.FinalRadius is > 0 and < 1 &&
                   error == IndicatorFloorError("macros.playbackIndicator.finalRadius", "1 DIP", indicator.FinalRadius) ||
               !IsEdited("macros.playbackIndicator.animationDurationMs", editedPaths) && indicator.AnimationDurationMs is > 0 and < 100 &&
                   error == IndicatorFloorError("macros.playbackIndicator.animationDurationMs", "100 ms", indicator.AnimationDurationMs);
    }

    private static string IndicatorFloorError(string path, string floor, double value) =>
        $"{path} must be at least {floor} (got {value}).";

    private static string IndicatorFloorError(string path, string floor, int value) =>
        $"{path} must be at least {floor} (got {value}).";

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
        if (config.Macros?.PlaybackIndicator is { } indicator) {
            ValidatePlaybackIndicator(indicator, violations);
        } else {
            violations.Add("macros.playbackIndicator cannot be null.");
        }

        // === App-scope validation ===
        ValidateAppScope(config, violations, actionKeys, allNavKeys, hotkeyVKeys);

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
        if (macros is null) {
            return;
        }

        if (!double.IsFinite(macros.SpeedModifier) || macros.SpeedModifier < 0) {
            violations.Add($"macros.speedModifier must be finite and >= 0 (got {macros.SpeedModifier}).");
        }
        if (!macros.Enabled) {
            return;
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
            if (ReservedKeys.Contains(sk)) {
                violations.Add($"macros.slotKeys contains reserved display-switch key '{sk}'.");
            }
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

    private static void ValidatePlaybackIndicator(PlaybackIndicatorConfig indicator, List<string> violations) {
        if (!IsValidHexColor(indicator.FillColor)) {
            violations.Add("macros.playbackIndicator.fillColor must be a valid hex color.");
        }
        if (!IsValidHexColor(indicator.StrokeColor)) {
            violations.Add("macros.playbackIndicator.strokeColor must be a valid hex color.");
        }
        if (!double.IsFinite(indicator.StrokeThickness) || indicator.StrokeThickness < 0) {
            violations.Add("macros.playbackIndicator.strokeThickness must be a finite number greater than or equal to 0.");
        }
        if (!double.IsFinite(indicator.InitialRadius * 2) || indicator.InitialRadius <= 0) {
            violations.Add("macros.playbackIndicator.initialRadius must be a finite number greater than 0.");
        } else if (indicator.InitialRadius < 1) {
            violations.Add(IndicatorFloorError("macros.playbackIndicator.initialRadius", "1 DIP", indicator.InitialRadius));
        }
        if (!double.IsFinite(indicator.FinalRadius * 2) || indicator.FinalRadius <= 0) {
            violations.Add("macros.playbackIndicator.finalRadius must be a finite number greater than 0.");
        } else if (indicator.FinalRadius < 1) {
            violations.Add(IndicatorFloorError("macros.playbackIndicator.finalRadius", "1 DIP", indicator.FinalRadius));
        }
        if (indicator.AnimationDurationMs <= 0) {
            violations.Add("macros.playbackIndicator.animationDurationMs must be greater than 0.");
        } else if (indicator.AnimationDurationMs < 100) {
            violations.Add(IndicatorFloorError("macros.playbackIndicator.animationDurationMs", "100 ms", indicator.AnimationDurationMs));
        }
    }

    private static void ValidateKeyPressVisualization(KeyPressVisualizationConfig kpv, List<string> violations) {
        if (!double.IsFinite(kpv.FontSize) || kpv.FontSize <= 0) {
            violations.Add($"keyPressVisualization.fontSize must be finite and > 0 (got {kpv.FontSize}).");
        }

        if (!double.IsFinite(kpv.OutlineThickness) || kpv.OutlineThickness < 0) {
            violations.Add($"keyPressVisualization.outlineThickness must be finite and >= 0 (got {kpv.OutlineThickness}).");
        }

        if (!double.IsFinite(kpv.Margin) || kpv.Margin < 0) {
            violations.Add($"keyPressVisualization.margin must be finite and >= 0 (got {kpv.Margin}).");
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

    internal static bool IsValidHexColor(string color) {
        if (string.IsNullOrEmpty(color) || color[0] != '#') {
            return false;
        }

        return color.Length is 7 or 9 && color[1..].All(c => char.IsAsciiHexDigit(c));
    }

    private static void ValidateAppScope(ConfigModel config, List<string> violations, HashSet<VKey> actionKeys, HashSet<VKey> navKeys, HashSet<VKey> hotkeyVKeys) {
        if (config.AppScope is null) {
            return;
        }

        var chordKey = config.AppScope.ChordKey;
        if (chordKey is null) {
            return; // feature disabled
        }

        var key = chordKey.Value;
        var label = "appScope.chordKey";

        if (ReservedKeys.Contains(key)) {
            violations.Add($"{label}: key '{key}' is reserved.");
        }

        if (actionKeys.Contains(key)) {
            violations.Add($"{label}: key '{key}' conflicts with an action binding.");
        }

        if (navKeys.Contains(key)) {
            violations.Add($"{label}: key '{key}' conflicts with a navigation key.");
        }

        if (hotkeyVKeys.Contains(key)) {
            violations.Add($"{label}: key '{key}' conflicts with the hotkey.");
        }

        // Chord keys from modes
        var modes = config.Modes;
        foreach (var mc in new[] { modes.UniformGrid, modes.Crosshair, modes.LogCrosshair, modes.LogGrid }) {
            if (mc is { Enabled: true, ChordKey: { } chord } && chord == key) {
                violations.Add($"{label}: key '{key}' conflicts with a mode chord key.");
                break;
            }
        }

        // Scroll hotkeys
        if (config.ScrollHotKeys.Enabled) {
            if (key == config.ScrollHotKeys.ScrollUpKey.Key || key == config.ScrollHotKeys.ScrollDownKey.Key) {
                violations.Add($"{label}: key '{key}' conflicts with a scroll hotkey.");
            }
        }

        // Macro keys
        var macros = config.Macros;
        if (macros is { Enabled: true }) {
            if (key == macros.RecordKey || key == macros.HelperKey) {
                violations.Add($"{label}: key '{key}' conflicts with a macro key.");
            }
            if (macros.SlotKeys?.Contains(key) == true) {
                violations.Add($"{label}: key '{key}' conflicts with a macro slot key.");
            }
            if (macros.GlobalHotKey is { } ghk && ghk.Key == key) {
                violations.Add($"{label}: key '{key}' conflicts with macro globalHotKey.");
            }
        }
    }
}
