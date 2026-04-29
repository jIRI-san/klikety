using System.IO;

using Klikety.Config;

namespace Klikety.Tests;

public class ConfigLoaderTests {
    [Fact]
    public void Load_MissingFile_ReturnsDefaults() {
        var result = ConfigLoader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        Assert.Empty(result.Violations);
        Assert.True(result.Config.Modes.UniformGrid.Enabled);
        Assert.True(result.Config.Modes.UniformGrid.Default);
        Assert.Equal(8, result.Config.FirstKeys.Length);
        Assert.Equal(8, result.Config.SecondKeys.Length);
    }

    [Fact]
    public void Load_ValidJsonc_ParsesCorrectly() {
        var json = """
        {
            // comment
            "hotKey": { "modifiers": "Control", "key": "OemTilde" },
            "navigationMode": "twoKey",
            "level3CellSizeThreshold": 25000
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Equal(HotKeyModifiers.Control, result.Config.HotKey.Modifiers);
            Assert.Equal(Input.VKey.OemTilde, result.Config.HotKey.Key);
            // After migration, navigationMode is removed; check modes shape instead
            Assert.True(result.Config.Modes.UniformGrid.TwoKey);
            Assert.False(result.Config.Modes.UniformGrid.ArrowKeys);
            Assert.Equal(25000, result.Config.Level3CellSizeThreshold);
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaultsWithError() {
        var path = WriteTempFile("{invalid json}}}");
        try {
            var result = ConfigLoader.Load(path);
            Assert.True(result.Config.Modes.UniformGrid.Enabled);
            Assert.Contains(result.Violations, v => v.Contains("could not be parsed"));
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ReservedKeyInFirstKeys_ReportsViolation() {
        var json = """
        {
            "firstKeys": ["A", "S", "Escape", "F"],
            "secondKeys": ["W", "E", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Escape") && v.Contains("firstKeys"));
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ReservedKeyInSecondKeys_ReportsViolation() {
        var json = """
        {
            "firstKeys": ["A", "S", "D", "F"],
            "secondKeys": ["W", "Return", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Return") && v.Contains("secondKeys"));
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_OverlappingFirstAndSecondKeys_ReportsViolation() {
        var json = """
        {
            "firstKeys": ["A", "S", "D", "W"],
            "secondKeys": ["W", "E", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains('W') && v.Contains("both"));
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ActionKeyConflictsWithNavKey_ReportsViolation() {
        var json = """
        {
            "firstKeys": ["A", "S", "D", "F"],
            "secondKeys": ["W", "E", "R", "T"],
            "actionBindings": {
                "A": "RightClick"
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains('A') && v.Contains("conflicts"));
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_DuplicateFirstKeys_ReportsViolation() {
        var json = """
        {
            "firstKeys": ["A", "S", "A", "F"],
            "secondKeys": ["W", "E", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("firstKeys") && v.Contains("duplicate"));
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_VersionTooHigh_BlockingErrorPropagated() {
        var json = """{ "configVersion": 999, "modes": {} }""";
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("newer than supported"));
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_LegacyConfig_MigrationWarningsAppended() {
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
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Crosshair") && v.Contains("auto-disabled"));
        } finally { File.Delete(path); try { File.Delete(path + ".bak"); } catch { } }
    }

    private static string WriteTempFile(string content) {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, content);
        return path;
    }
}
