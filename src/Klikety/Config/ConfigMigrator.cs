using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

using Klikety.Input;

namespace Klikety.Config;

/// <summary>
/// Result of a migration attempt.
/// </summary>
public sealed class MigrationResult {
    public required bool WasMigrated { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? BlockingError { get; init; }
}

/// <summary>
/// Migrates legacy config files (pre-modes shape) to the current schema via
/// a JsonDocument pre-pass. Performs atomic writes with .bak backup.
/// </summary>
public static class ConfigMigrator {
    public const int CurrentConfigVersion = 2;

    private static readonly VKey[] Default8FirstKeys =
        [VKey.A, VKey.S, VKey.D, VKey.F, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon];

    private static readonly VKey[] Default8SecondKeys =
        [VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I, VKey.O];

    private static readonly VKey[] Default10HorizontalKeys =
        [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon];

    private static readonly VKey[] Default10VerticalKeys =
        [VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P];

    private static readonly JsonDocumentOptions DocOptions = new() {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Inspects the config file at <paramref name="path"/> and migrates if needed.
    /// Returns without writing if the file is already current or absent.
    /// </summary>
    public static MigrationResult MigrateIfNeeded(string path) {
        if (!File.Exists(path)) {
            return new MigrationResult { WasMigrated = false };
        }

        string rawJson;
        try {
            rawJson = File.ReadAllText(path);
        } catch (IOException ex) {
            return new MigrationResult {
                WasMigrated = false,
                BlockingError = $"Cannot read config file: {ex.Message}",
            };
        }

        // Clean up stale .tmp files from prior failed migrations
        CleanStaleTempFiles(path);

        JsonNode? root;
        try {
            root = JsonNode.Parse(rawJson, documentOptions: DocOptions);
        } catch (JsonException ex) {
            return new MigrationResult {
                WasMigrated = false,
                BlockingError = $"Config file could not be parsed: {ex.Message}",
            };
        }

        if (root is not JsonObject obj) {
            return new MigrationResult {
                WasMigrated = false,
                BlockingError = "Config file root is not a JSON object.",
            };
        }

        // Check configVersion
        int version = 0;
        try {
            version = obj["configVersion"]?.GetValue<int>() ?? 0;
        } catch (Exception ex) when (ex is InvalidOperationException or FormatException) {
            return new MigrationResult {
                WasMigrated = false,
                BlockingError = $"Invalid configVersion value: {ex.Message}",
            };
        }
        if (version > CurrentConfigVersion) {
            return new MigrationResult {
                WasMigrated = false,
                BlockingError = $"Config version {version} is newer than supported version {CurrentConfigVersion}. Reset config to continue.",
            };
        }

        // If modes already present, migrate from v1 → v2 if needed
        if (obj.ContainsKey("modes")) {
            bool changed = false;
            var v1Warnings = new List<string>();

            // Strip navigationMode if still present
            if (obj.Remove("navigationMode")) {
                v1Warnings.Add("Both 'modes' and 'navigationMode' present; 'modes' takes precedence. 'navigationMode' removed.");
                changed = true;
            }

            // v1 → v2: lift per-mode horizontalKeys/verticalKeys to root, remove keySets
            if (version < 2) {
                // Remove dead keySets artifact
                if (obj.Remove("keySets")) {
                    changed = true;
                }

                // Promote horizontalKeys/verticalKeys from crosshair (or logCrosshair) to root
                if (!obj.ContainsKey("horizontalKeys")) {
                    VKey[]? promotedHoriz = null;
                    VKey[]? promotedVert = null;

                    if (obj["modes"] is JsonObject modesObj) {
                        // Try crosshair first, then logCrosshair
                        foreach (var modeName in new[] { "crosshair", "logCrosshair" }) {
                            if (modesObj[modeName] is JsonObject modeObj) {
                                promotedHoriz ??= ReadVKeyArrayFromNode(modeObj, "horizontalKeys");
                                promotedVert ??= ReadVKeyArrayFromNode(modeObj, "verticalKeys");
                                modeObj.Remove("horizontalKeys");
                                modeObj.Remove("verticalKeys");
                            }
                        }
                    }

                    // Fall back to firstKeys/secondKeys if present at root
                    promotedHoriz ??= ReadVKeyArray(obj, "firstKeys");
                    promotedVert ??= ReadVKeyArray(obj, "secondKeys");

                    // Write to root (use defaults if nothing found)
                    obj["horizontalKeys"] = ToJsonArray(promotedHoriz ?? Default10HorizontalKeys);
                    obj["verticalKeys"] = ToJsonArray(promotedVert ?? Default10VerticalKeys);
                    changed = true;
                }

                // Remove legacy root firstKeys/secondKeys
                if (obj.Remove("firstKeys")) { changed = true; }
                if (obj.Remove("secondKeys")) { changed = true; }

                obj["configVersion"] = CurrentConfigVersion;
                changed = true;
            }

            if (changed) {
                var writeError = AtomicWrite(path, obj);
                if (writeError is not null) {
                    return new MigrationResult { WasMigrated = false, BlockingError = writeError };
                }
                return new MigrationResult { WasMigrated = true, Warnings = v1Warnings };
            }
            return new MigrationResult { WasMigrated = false };
        }

        // No modes property — legacy shape. Migrate.
        var warnings = new List<string>();
        var actionKeys = CollectActionKeys(obj);

        // Read old navigationMode
        string? navModeStr = null;
        try {
            navModeStr = obj["navigationMode"]?.GetValue<string>();
        } catch (Exception ex) when (ex is InvalidOperationException or FormatException) {
            warnings.Add($"Invalid navigationMode value ({ex.Message}); defaulting to 'both'.");
        }
        var (twoKey, arrowKeys) = ParseLegacyNavMode(navModeStr);

        // Read old key sets
        var oldFirstKeys = ReadVKeyArray(obj, "firstKeys");
        var oldSecondKeys = ReadVKeyArray(obj, "secondKeys");

        // Determine shared axis keys (promote to root level)
        var horizKeys = Default10HorizontalKeys;
        var vertKeys = Default10VerticalKeys;

        // Check if new 10-key defaults conflict with action bindings
        var (_, horizConflict) = FilterConflictingKeys(horizKeys, actionKeys);
        var (_, vertConflict) = FilterConflictingKeys(vertKeys, actionKeys);

        if (horizConflict) {
            horizKeys = oldFirstKeys ?? Default8FirstKeys;
            warnings.Add("Default horizontalKeys conflict with actionBindings; using legacy firstKeys instead.");
        }
        if (vertConflict) {
            vertKeys = oldSecondKeys ?? Default8SecondKeys;
            warnings.Add("Default verticalKeys conflict with actionBindings; using legacy secondKeys instead.");
        }

        // Write shared axis keys at root (replacing firstKeys/secondKeys)
        obj.Remove("firstKeys");
        obj.Remove("secondKeys");
        obj["horizontalKeys"] = ToJsonArray(horizKeys);
        obj["verticalKeys"] = ToJsonArray(vertKeys);

        // Build modes object
        var modes = new JsonObject();

        // UniformGrid: preserve existing twoKey/arrowKeys
        var uniformGrid = new JsonObject {
            ["enabled"] = true,
            ["default"] = true,
            ["arrowKeys"] = arrowKeys,
            ["twoKey"] = twoKey,
        };
        modes["uniformGrid"] = uniformGrid;

        // Crosshair mode
        var chordN = VKey.N;
        var crosshairEnabled = !actionKeys.Contains(chordN);
        if (!crosshairEnabled) {
            warnings.Add($"Crosshair chord key '{chordN}' conflicts with actionBindings; Crosshair mode auto-disabled.");
        }

        var crosshair = new JsonObject {
            ["enabled"] = crosshairEnabled,
            ["chordKey"] = chordN.ToString(),
            ["arrowKeys"] = true,
            ["twoKey"] = true,
        };
        modes["crosshair"] = crosshair;

        // LogCrosshair mode
        var chordM = VKey.M;
        var logCrosshairEnabled = !actionKeys.Contains(chordM);
        if (!logCrosshairEnabled) {
            warnings.Add($"LogCrosshair chord key '{chordM}' conflicts with actionBindings; LogCrosshair mode auto-disabled.");
        }

        var logCrosshair = new JsonObject {
            ["enabled"] = logCrosshairEnabled,
            ["chordKey"] = chordM.ToString(),
            ["arrowKeys"] = true,
            ["twoKey"] = true,
            ["logBaseSize"] = 10,
        };
        modes["logCrosshair"] = logCrosshair;

        // Post-migration normalization: ensure at least one mode enabled and exactly one default
        EnsureDefaultMode(modes, warnings);

        obj["modes"] = modes;
        obj["configVersion"] = CurrentConfigVersion;

        // Remove legacy navigationMode
        obj.Remove("navigationMode");

        var migrationWriteError = AtomicWrite(path, obj);
        if (migrationWriteError is not null) {
            return new MigrationResult { WasMigrated = false, BlockingError = migrationWriteError };
        }

        return new MigrationResult {
            WasMigrated = true,
            Warnings = warnings,
        };
    }

    private static (bool TwoKey, bool ArrowKeys) ParseLegacyNavMode(string? mode) => mode?.ToLowerInvariant() switch {
        "twokey" => (true, false),
        "arrow" => (false, true),
        "both" or null => (true, true),
        _ => (true, true), // unknown → both
    };

    private static HashSet<VKey> CollectActionKeys(JsonObject obj) {
        var keys = new HashSet<VKey>();
        if (obj["actionBindings"] is JsonObject bindings) {
            foreach (var prop in bindings) {
                if (Enum.TryParse<VKey>(prop.Key, true, out var vkey)) {
                    keys.Add(vkey);
                }
            }
        }
        keys.Add(VKey.Space); // implicit default
        return keys;
    }

    private static VKey[]? ReadVKeyArray(JsonObject obj, string propertyName) {
        if (obj[propertyName] is not JsonArray arr) {
            return null;
        }

        var result = new List<VKey>();
        foreach (var item in arr) {
            if (item?.GetValue<string>() is { } s && Enum.TryParse<VKey>(s, true, out var vkey)) {
                result.Add(vkey);
            }
        }
        return result.Count > 0 ? result.ToArray() : null;
    }

    private static VKey[]? ReadVKeyArrayFromNode(JsonObject obj, string propertyName) {
        return ReadVKeyArray(obj, propertyName);
    }

    private static (VKey[] Safe, bool HadConflict) FilterConflictingKeys(VKey[] keys, HashSet<VKey> actionKeys) {
        bool conflict = false;
        foreach (var k in keys) {
            if (actionKeys.Contains(k)) {
                conflict = true;
                break;
            }
        }
        return (keys, conflict);
    }

    private static void EnsureDefaultMode(JsonObject modes, List<string> warnings) {
        // Count enabled and default modes
        var modeNames = new[] { "uniformGrid", "crosshair", "logCrosshair" };
        int enabledCount = 0;
        int defaultCount = 0;
        string? firstEnabled = null;

        foreach (var name in modeNames) {
            if (modes[name] is JsonObject m) {
                if (m["enabled"]?.GetValue<bool>() == true) {
                    enabledCount++;
                    firstEnabled ??= name;
                }
                if (m["default"]?.GetValue<bool>() == true) {
                    defaultCount++;
                }
            }
        }

        // If no modes enabled, force UniformGrid
        if (enabledCount == 0) {
            if (modes["uniformGrid"] is JsonObject ug) {
                ug["enabled"] = true;
                ug["default"] = true;
            }
            warnings.Add("All modes were disabled after migration; UniformGrid re-enabled as default.");
            return;
        }

        // If default mode was disabled, promote first enabled
        if (defaultCount == 0 || (modes["uniformGrid"] is JsonObject ugCheck
            && ugCheck["default"]?.GetValue<bool>() == true
            && ugCheck["enabled"]?.GetValue<bool>() != true)) {
            // Clear any existing defaults, set first enabled as default
            foreach (var name in modeNames) {
                if (modes[name] is JsonObject m) {
                    m["default"] = name == firstEnabled;
                }
            }
            if (firstEnabled != "uniformGrid") {
                warnings.Add($"Default mode (UniformGrid) was auto-disabled; promoted {firstEnabled} as default.");
            }
        }
    }

    private static JsonArray ToJsonArray(VKey[] keys) {
        var arr = new JsonArray();
        foreach (var k in keys) {
            arr.Add(k.ToString());
        }
        return arr;
    }

    private static string? AtomicWrite(string path, JsonObject obj) {
        try {
            var dir = Path.GetDirectoryName(path)!;
            var tmpPath = Path.Combine(dir, Path.GetRandomFileName());
            var bakPath = path + ".bak";

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = obj.ToJsonString(options);

            // Write to temp file first
            File.WriteAllText(tmpPath, json);

            // Backup existing config
            if (File.Exists(path)) {
                File.Copy(path, bakPath, overwrite: true);
            }

            // Rename temp → target
            File.Move(tmpPath, path, overwrite: true);
            return null;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return $"Failed to write migrated config: {ex.Message}";
        }
    }

    private static void CleanStaleTempFiles(string path) {
        try {
            var tmpPath = path + ".tmp";
            if (File.Exists(tmpPath)) {
                File.Delete(tmpPath);
            }
        } catch {
            // Best-effort cleanup
        }
    }
}
