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
            "firstKeys": ["A", "S", "D", "F", "J", "K", "L", "OemSemicolon"],
            "secondKeys": ["W", "E", "R", "T", "Y", "U", "I", "O"],
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
            Assert.Equal(1, migrated["configVersion"]!.GetValue<int>());

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
            "firstKeys": ["A", "S", "D", "F"],
            "secondKeys": ["W", "E", "R", "T"]
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
            "firstKeys": ["A", "S", "D", "F"],
            "secondKeys": ["W", "E", "R", "T"]
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
            "firstKeys": ["A", "S", "D", "F"],
            "secondKeys": ["W", "E", "R", "T"],
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
            "firstKeys": ["A", "S", "D", "F"],
            "secondKeys": ["W", "E", "R", "T"],
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
            "firstKeys": ["A", "S", "D", "F", "J", "K", "L", "OemSemicolon"],
            "secondKeys": ["W", "E", "R", "T", "Y", "U", "I", "O"],
            "actionBindings": { "G": "RightClick" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigMigrator.MigrateIfNeeded(path);
            Assert.True(result.WasMigrated);
            Assert.Contains(result.Warnings, w => w.Contains("horizontalKeys") && w.Contains("legacy"));

            var migrated = ReadJsonObject(path);
            var horizKeys = migrated["modes"]!["crosshair"]!["horizontalKeys"]!.AsArray();
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
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true }
            }
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
            "firstKeys": ["A", "S", "D", "F"],
            "secondKeys": ["W", "E", "R", "T"]
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
            "firstKeys": ["A", "S", "D", "F"],
            "secondKeys": ["W", "E", "R", "T"]
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
            "firstKeys": ["A", "S"],
            "secondKeys": ["W", "E"]
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
            "firstKeys": ["A", "S", "D", "F"],
            "secondKeys": ["W", "E", "R", "T"],
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
