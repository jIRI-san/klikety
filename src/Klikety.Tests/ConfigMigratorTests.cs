using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

using Klikety.Config;
using Klikety.Input;

namespace Klikety.Tests;

public class ConfigMigratorTests {
    [Fact]
    public void MigrateIfNeeded_MissingFile_NoMigration() {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var result = ConfigMigrator.MigrateIfNeeded(path);
        Assert.False(result.WasMigrated);
        Assert.Null(result.BlockingError);
    }

    [Fact]
    public void MigrateIfNeeded_OldShape_MigratesToNewShape() {
        var json = """
        {
            "navigationMode": "both",
            "horizontalKeys": ["A", "S", "D", "F", "J", "K", "L", "OemSemicolon"],
            "verticalKeys": ["W", "E", "R", "T", "Y", "U", "I", "O"],
            "actionBindings": { "X": "DoubleClick", "C": "MiddleClick", "V": "RightClick" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);
            Assert.Null(result.BlockingError);

            var migrated = ReadJsonObject(path);
            Assert.True(migrated.ContainsKey("modes"));
            Assert.False(migrated.ContainsKey("navigationMode"));
            Assert.Equal(5, migrated["configVersion"]!.GetValue<int>());

            var ug = migrated["modes"]!["uniformGrid"]!;
            Assert.True(ug["enabled"]!.GetValue<bool>());
            Assert.True(ug["default"]!.GetValue<bool>());
            Assert.True(ug["twoKey"]!.GetValue<bool>());
            Assert.True(ug["arrowKeys"]!.GetValue<bool>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_ArrowOnly_MapsTwoKeyFalse() {
        var json = """
        {
            "navigationMode": "arrow",
            "horizontalKeys": ["A", "S", "D", "F"],
            "verticalKeys": ["W", "E", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);

            var migrated = ReadJsonObject(path);
            var ug = migrated["modes"]!["uniformGrid"]!;
            Assert.False(ug["twoKey"]!.GetValue<bool>());
            Assert.True(ug["arrowKeys"]!.GetValue<bool>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_TwoKeyOnly_MapsArrowKeysFalse() {
        var json = """
        {
            "navigationMode": "twoKey",
            "horizontalKeys": ["A", "S", "D", "F"],
            "verticalKeys": ["W", "E", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);

            var migrated = ReadJsonObject(path);
            var ug = migrated["modes"]!["uniformGrid"]!;
            Assert.True(ug["twoKey"]!.GetValue<bool>());
            Assert.False(ug["arrowKeys"]!.GetValue<bool>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_NChordConflict_DisablesCrosshair() {
        var json = """
        {
            "navigationMode": "both",
            "horizontalKeys": ["A", "S", "D", "F"],
            "verticalKeys": ["W", "E", "R", "T"],
            "actionBindings": { "N": "RightClick" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);
            Assert.Contains(result.Warnings, w => w.Contains("Crosshair") && w.Contains("auto-disabled"));

            var migrated = ReadJsonObject(path);
            Assert.False(migrated["modes"]!["crosshair"]!["enabled"]!.GetValue<bool>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_MChordConflict_DisablesLogCrosshair() {
        var json = """
        {
            "navigationMode": "both",
            "horizontalKeys": ["A", "S", "D", "F"],
            "verticalKeys": ["W", "E", "R", "T"],
            "actionBindings": { "M": "DoubleClick" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);
            Assert.Contains(result.Warnings, w => w.Contains("LogCrosshair") && w.Contains("auto-disabled"));

            var migrated = ReadJsonObject(path);
            Assert.False(migrated["modes"]!["logCrosshair"]!["enabled"]!.GetValue<bool>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_AxisKeyConflictWithActionBindings_UsesLegacyKeys() {
        // G is in default 10-key horizontal set; bind it as action → should fall back to old firstKeys
        var json = """
        {
            "navigationMode": "both",
            "horizontalKeys": ["A", "S", "D", "F", "J", "K", "L", "OemSemicolon"],
            "verticalKeys": ["W", "E", "R", "T", "Y", "U", "I", "O"],
            "actionBindings": { "G": "RightClick" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);
            Assert.Contains(result.Warnings, w => w.Contains("horizontalKeys") && w.Contains("legacy"));

            var migrated = ReadJsonObject(path);
            var horizKeys = migrated["horizontalKeys"]!.AsArray();
            // Should be the old 8-key set, not 10-key
            Assert.Equal(8, horizKeys.Count);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_VersionTooHigh_BlockingError() {
        var json = """{ "configVersion": 999, "modes": {} }""";
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.False(result.WasMigrated);
            Assert.Contains("newer than supported", result.BlockingError!);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_AlreadyMigrated_NoMutation() {
        var json = """
        {
            "configVersion": 5,
            "horizontalKeys": ["A","S","D","F"],
            "verticalKeys": ["W","E","R","T"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true },
                "logGrid": { "enabled": true, "twoKey": true, "chordKey": "OemComma", "logGridBaseSize": 10 }
            },
            "scrollHotkeys": { "enabled": false },
            "macros": { "enabled": true }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var originalContent = File.ReadAllText(path);
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.False(result.WasMigrated);

            // File should not have been rewritten
            Assert.Equal(originalContent, File.ReadAllText(path));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_MixedShape_ModesWins() {
        var json = """
        {
            "navigationMode": "arrow",
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true, "arrowKeys": true }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);
            Assert.Contains(result.Warnings, w => w.Contains("modes") && w.Contains("precedence"));

            var migrated = ReadJsonObject(path);
            Assert.False(migrated.ContainsKey("navigationMode"));
            // uniformGrid should retain twoKey: true (from modes, not from "arrow" nav mode)
            Assert.True(migrated["modes"]!["uniformGrid"]!["twoKey"]!.GetValue<bool>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_MalformedJson_BlockingError() {
        var path = WriteTempFile("{invalid json}}}");
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.False(result.WasMigrated);
            Assert.Contains("could not be parsed", result.BlockingError!);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_MissingNavigationMode_DefaultsToBoth() {
        var json = """
        {
            "horizontalKeys": ["A", "S", "D", "F"],
            "verticalKeys": ["W", "E", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);

            var migrated = ReadJsonObject(path);
            var ug = migrated["modes"]!["uniformGrid"]!;
            Assert.True(ug["twoKey"]!.GetValue<bool>());
            Assert.True(ug["arrowKeys"]!.GetValue<bool>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_Idempotent_SecondRunNoChange() {
        var json = """
        {
            "navigationMode": "both",
            "horizontalKeys": ["A", "S", "D", "F"],
            "verticalKeys": ["W", "E", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            ConfigMigrator.MigrateIfNeeded(path);
            var afterFirst = File.ReadAllText(path);

            var result2 = ConfigMigrator.MigrateIfNeeded(path);
            Assert.False(result2.WasMigrated);
            Assert.Equal(afterFirst, File.ReadAllText(path));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_CreatesBackupFile() {
        var json = """{ "navigationMode": "both" }""";
        var path = WriteTempFile(json);
        try {
            ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(File.Exists(path + ".bak"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_PreservesUnknownFields() {
        var json = """
        {
            "navigationMode": "both",
            "customField": "preserved",
            "horizontalKeys": ["A", "S"],
            "verticalKeys": ["W", "E"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            ConfigMigrator.MigrateIfNeeded(path);
            var migrated = ReadJsonObject(path);
            Assert.Equal("preserved", migrated["customField"]!.GetValue<string>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_BothChordsConflict_PromotesUniformGrid() {
        var json = """
        {
            "navigationMode": "both",
            "horizontalKeys": ["A", "S", "D", "F"],
            "verticalKeys": ["W", "E", "R", "T"],
            "actionBindings": { "N": "RightClick", "M": "DoubleClick" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);

            var migrated = ReadJsonObject(path);
            // UniformGrid should still be default
            Assert.True(migrated["modes"]!["uniformGrid"]!["default"]!.GetValue<bool>());
            Assert.True(migrated["modes"]!["uniformGrid"]!["enabled"]!.GetValue<bool>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_V2ToV3_AddsLogGridBlock() {
        var json = """
        {
            "configVersion": 2,
            "horizontalKeys": ["A","S","D","F","G","H","J","K","L","OemSemicolon"],
            "verticalKeys": ["Q","W","E","R","T","Y","U","I","O","P"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N" },
                "logCrosshair": { "enabled": true, "twoKey": true, "chordKey": "M" }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);
            Assert.Null(result.BlockingError);

            var migrated = ReadJsonObject(path);
            Assert.Equal(5, migrated["configVersion"]!.GetValue<int>());

            var logGrid = migrated["modes"]!["logGrid"]!;
            Assert.True(logGrid["enabled"]!.GetValue<bool>());
            Assert.Equal("OemComma", logGrid["chordKey"]!.GetValue<string>());
            Assert.True(logGrid["twoKey"]!.GetValue<bool>());
            Assert.True(logGrid["arrowKeys"]!.GetValue<bool>());
            Assert.Equal(10, logGrid["logGridBaseSize"]!.GetValue<int>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_V2ToV3_OemCommaConflict_DisablesLogGrid() {
        var json = """
        {
            "configVersion": 2,
            "horizontalKeys": ["A","S","D","F"],
            "verticalKeys": ["W","E","R","T"],
            "actionBindings": { "OemComma": "MiddleClick" },
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": false },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);
            Assert.Contains(result.Warnings, w => w.Contains("LogGrid") && w.Contains("auto-disabled"));

            var migrated = ReadJsonObject(path);
            Assert.False(migrated["modes"]!["logGrid"]!["enabled"]!.GetValue<bool>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_V2ToV3_PreservesExistingLogGrid() {
        var json = """
        {
            "configVersion": 2,
            "horizontalKeys": ["A","S","D","F","G","H","J","K","L","OemSemicolon"],
            "verticalKeys": ["Q","W","E","R","T","Y","U","I","O","P"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "logGrid": { "enabled": false, "chordKey": "OemComma", "twoKey": true, "logGridBaseSize": 15 }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);

            var migrated = ReadJsonObject(path);
            // User override preserved — still disabled with custom baseSize
            Assert.False(migrated["modes"]!["logGrid"]!["enabled"]!.GetValue<bool>());
            Assert.Equal(15, migrated["modes"]!["logGrid"]!["logGridBaseSize"]!.GetValue<int>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_LegacyShape_IncludesLogGrid() {
        var json = """
        {
            "navigationMode": "both",
            "horizontalKeys": ["A", "S", "D", "F"],
            "verticalKeys": ["W", "E", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);

            var migrated = ReadJsonObject(path);
            var logGrid = migrated["modes"]!["logGrid"]!;
            Assert.True(logGrid["enabled"]!.GetValue<bool>());
            Assert.Equal("OemComma", logGrid["chordKey"]!.GetValue<string>());
            Assert.Equal(10, logGrid["logGridBaseSize"]!.GetValue<int>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_V3ToV4_AddsScrollHotKeys() {
        var json = """
        {
            "configVersion": 3,
            "horizontalKeys": ["A","S","D","F","G","H","J","K","L","OemSemicolon"],
            "verticalKeys": ["Q","W","E","R","T","Y","U","I","O","P"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "logGrid": { "enabled": true, "twoKey": true, "chordKey": "OemComma", "logGridBaseSize": 10 }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);
            Assert.Null(result.BlockingError);

            var migrated = ReadJsonObject(path);
            Assert.Equal(5, migrated["configVersion"]!.GetValue<int>());

            var scroll = migrated["scrollHotkeys"]!;
            Assert.False(scroll["enabled"]!.GetValue<bool>());
            Assert.Equal("Prior", scroll["scrollUpKey"]!["key"]!.GetValue<string>());
            Assert.Equal("Next", scroll["scrollDownKey"]!["key"]!.GetValue<string>());
            Assert.Equal(3, scroll["scrollAmount"]!.GetValue<int>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_V5Config_NoMutation() {
        var json = """
        {
            "configVersion": 5,
            "horizontalKeys": ["A","S","D","F"],
            "verticalKeys": ["W","E","R","T"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true }
            },
            "scrollHotkeys": { "enabled": true, "scrollAmount": 5 },
            "macros": { "enabled": true }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var originalContent = File.ReadAllText(path);
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.False(result.WasMigrated);

            Assert.Equal(originalContent, File.ReadAllText(path));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_V3WithExistingScrollHotKeys_PreservesUserValues() {
        var json = """
        {
            "configVersion": 3,
            "horizontalKeys": ["A","S","D","F"],
            "verticalKeys": ["W","E","R","T"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true }
            },
            "scrollHotkeys": { "enabled": true, "scrollAmount": 10 }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);

            var migrated = ReadJsonObject(path);
            Assert.Equal(5, migrated["configVersion"]!.GetValue<int>());
            // User's existing scrollHotkeys preserved (not overwritten with defaults)
            Assert.True(migrated["scrollHotkeys"]!["enabled"]!.GetValue<bool>());
            Assert.Equal(10, migrated["scrollHotkeys"]!["scrollAmount"]!.GetValue<int>());
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MigrateIfNeeded_V3ToV4_PreservesUnknownFields() {
        var json = """
        {
            "configVersion": 3,
            "horizontalKeys": ["A","S","D","F"],
            "verticalKeys": ["W","E","R","T"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true }
            },
            "customUserField": "preserved"
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);

            var migrated = ReadJsonObject(path);
            Assert.Equal(5, migrated["configVersion"]!.GetValue<int>());
            Assert.Equal("preserved", migrated["customUserField"]!.GetValue<string>());
        } finally { Cleanup(path); }
    }

    private static string WriteTempFile(string content) {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, content);
        return path;
    }

    private static JsonObject ReadJsonObject(string path) {
        var options = new JsonDocumentOptions {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        return JsonNode.Parse(File.ReadAllText(path), documentOptions: options)!.AsObject();
    }

    private static void Cleanup(string path) {
        try { File.Delete(path); } catch { }
        try { File.Delete(path + ".bak"); } catch { }
        try { File.Delete(path + ".tmp"); } catch { }
    }
}
