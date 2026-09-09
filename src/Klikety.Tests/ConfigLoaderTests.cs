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
        Assert.Equal(10, result.Config.HorizontalKeys.Length);
        Assert.Equal(10, result.Config.VerticalKeys.Length);
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
            "configVersion": 2,
            "modes": { "uniformGrid": { "enabled": true, "default": true, "twoKey": true } },
            "horizontalKeys": ["A", "S", "Escape", "F"],
            "verticalKeys": ["W", "E", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Escape") && v.Contains("horizontalKeys"));
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ReservedKeyInSecondKeys_ReportsViolation() {
        var json = """
        {
            "configVersion": 2,
            "modes": { "uniformGrid": { "enabled": true, "default": true, "twoKey": true } },
            "horizontalKeys": ["A", "S", "D", "F"],
            "verticalKeys": ["W", "Return", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Return") && v.Contains("verticalKeys"));
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_OverlappingFirstAndSecondKeys_ReportsViolation() {
        var json = """
        {
            "configVersion": 2,
            "modes": { "uniformGrid": { "enabled": true, "default": true, "twoKey": true } },
            "horizontalKeys": ["A", "S", "D", "W"],
            "verticalKeys": ["W", "E", "R", "T"]
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
            "configVersion": 2,
            "modes": { "uniformGrid": { "enabled": true, "default": true, "twoKey": true } },
            "horizontalKeys": ["A", "S", "D", "F"],
            "verticalKeys": ["W", "E", "R", "T"],
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
            "configVersion": 2,
            "modes": { "uniformGrid": { "enabled": true, "default": true, "twoKey": true } },
            "horizontalKeys": ["A", "S", "A", "F"],
            "verticalKeys": ["W", "E", "R", "T"]
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("horizontalKeys") && v.Contains("duplicate"));
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
            "horizontalKeys": ["A", "S", "D", "F"],
            "verticalKeys": ["W", "E", "R", "T"],
            "actionBindings": { "N": "RightClick" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Crosshair") && v.Contains("auto-disabled"));
        } finally { File.Delete(path); try { File.Delete(path + ".bak"); } catch { } }
    }

    // --- Mode validation tests ---

    [Fact]
    public void Validate_NoEnabledModes_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": false },
                "crosshair": { "enabled": false },
                "logCrosshair": { "enabled": false },
                "logGrid": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("At least one mode must be enabled"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_MultipleDefaults_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "default": true, "twoKey": true, "chordKey": "N", "horizontalKeys": ["A","S"], "verticalKeys": ["W","E"] },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Only one mode may be marked as default"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_EnabledModeWithoutInput_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": false, "arrowKeys": false },
                "crosshair": { "enabled": false },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("twoKey or arrowKeys"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_CrosshairWithoutTwoKey_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": false, "arrowKeys": true, "chordKey": "N", "horizontalKeys": ["A","S"], "verticalKeys": ["W","E"] },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Crosshair") && v.Contains("require twoKey"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_LogBaseSizeOutOfRange_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": false },
                "logCrosshair": { "enabled": true, "twoKey": true, "chordKey": "M", "logBaseSize": 1, "horizontalKeys": ["A","S"], "verticalKeys": ["W","E"] }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("logBaseSize") && v.Contains("between 2 and 50"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_LogGridBaseSizeBelowMinimum_ReportsViolation() {
        var json = """
        {
            "configVersion": 2,
            "horizontalKeys": ["A","S","D","F","G","H","J","K","L","OemSemicolon"],
            "verticalKeys": ["Q","W","E","R","T","Y","U","I","O","P"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": false },
                "logCrosshair": { "enabled": false },
                "logGrid": { "enabled": true, "twoKey": true, "chordKey": "OemComma", "logGridBaseSize": 1 }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("logGridBaseSize") && v.Contains("between 2 and 50"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_LogGridBaseSizeAboveMaximum_ReportsViolation() {
        var json = """
        {
            "configVersion": 2,
            "horizontalKeys": ["A","S","D","F","G","H","J","K","L","OemSemicolon"],
            "verticalKeys": ["Q","W","E","R","T","Y","U","I","O","P"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": false },
                "logCrosshair": { "enabled": false },
                "logGrid": { "enabled": true, "twoKey": true, "chordKey": "OemComma", "logGridBaseSize": 51 }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("logGridBaseSize") && v.Contains("between 2 and 50"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_LogGridWithoutTwoKey_ReportsViolation() {
        var json = """
        {
            "configVersion": 2,
            "horizontalKeys": ["A","S","D","F","G","H","J","K","L","OemSemicolon"],
            "verticalKeys": ["Q","W","E","R","T","Y","U","I","O","P"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": false },
                "logCrosshair": { "enabled": false },
                "logGrid": { "enabled": true, "twoKey": false, "arrowKeys": true, "chordKey": "OemComma" }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("LogGrid") && v.Contains("require twoKey"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_DuplicateChordKeys_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N", "horizontalKeys": ["A","S"], "verticalKeys": ["W","E"] },
                "logCrosshair": { "enabled": true, "twoKey": true, "chordKey": "N", "horizontalKeys": ["D","F"], "verticalKeys": ["R","T"] }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Chord key") && v.Contains("both"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_ChordKeyConflictsWithAction_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "actionBindings": { "N": "RightClick" },
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N", "horizontalKeys": ["A","S"], "verticalKeys": ["W","E"] },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Crosshair") && v.Contains("chord key") && v.Contains("action"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_AxisKeyDuplicateWithinArray_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N", "horizontalKeys": ["A","A","S"], "verticalKeys": ["W","E","R"] },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("duplicate") && v.Contains("horizontalKeys"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_AxisKeyOverlapBetweenHorizAndVert_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N", "horizontalKeys": ["A","S","D"], "verticalKeys": ["D","E","R"] },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("both horizontalKeys and verticalKeys"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_AxisKeyConflictsWithChord_ReportsViolation() {
        var json = """
        {
            "configVersion": 2,
            "horizontalKeys": ["N","S"],
            "verticalKeys": ["W","E"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N" },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("HorizontalKey") && v.Contains("chord"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_FirstKeyConflictsWithChord_ReportsViolation() {
        var json = """
        {
            "configVersion": 2,
            "horizontalKeys": ["A","S","D","N"],
            "verticalKeys": ["W","E","R","T"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N" },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("HorizontalKey") && v.Contains("chord"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_ValidModesConfig_NoViolations() {
        var json = """
        {
            "configVersion": 1,
            "horizontalKeys": ["A","S","D","F"],
            "verticalKeys": ["W","E","R","T"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true, "arrowKeys": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N", "horizontalKeys": ["G","H","J","K"], "verticalKeys": ["Y","U","I","O"] },
                "logCrosshair": { "enabled": true, "twoKey": true, "chordKey": "M", "logBaseSize": 5, "horizontalKeys": ["G","H","J","K"], "verticalKeys": ["Y","U","I","O"] },
                "logGrid": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Empty(result.Violations);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_DefaultModeNotEnabled_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": false, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N", "horizontalKeys": ["A","S"], "verticalKeys": ["W","E"] },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("default mode must be enabled"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_EnabledNonDefaultWithoutChord_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "horizontalKeys": ["A","S"], "verticalKeys": ["W","E"] },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("must have a chordKey"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_LogBaseSizeTooHigh_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": false },
                "logCrosshair": { "enabled": true, "twoKey": true, "chordKey": "M", "logBaseSize": 51, "horizontalKeys": ["A","S"], "verticalKeys": ["W","E"] }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("logBaseSize") && v.Contains("between 2 and 50"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_ChordKeyIsReserved_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "Escape", "horizontalKeys": ["A","S"], "verticalKeys": ["W","E"] },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("chord key") && v.Contains("reserved"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_SecondKeyConflictsWithChord_ReportsViolation() {
        var json = """
        {
            "configVersion": 2,
            "horizontalKeys": ["A","S","D","F"],
            "verticalKeys": ["W","E","R","N"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N" },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("VerticalKey") && v.Contains("chord"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_AxisKeyConflictsWithAction_ReportsViolation() {
        var json = """
        {
            "configVersion": 2,
            "horizontalKeys": ["G","H"],
            "verticalKeys": ["Y","U"],
            "actionBindings": { "G": "RightClick" },
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N" },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("'G'") && v.Contains("conflicts"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_LogCrosshairWithoutTwoKey_ReportsViolation() {
        var json = """
        {
            "configVersion": 1,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": false },
                "logCrosshair": { "enabled": true, "twoKey": false, "arrowKeys": true, "chordKey": "M", "horizontalKeys": ["A","S"], "verticalKeys": ["W","E"] }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("LogCrosshair") && v.Contains("require twoKey"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_EmptyAxisKeysWhenEnabled_ReportsViolation() {
        var json = """
        {
            "configVersion": 2,
            "horizontalKeys": [],
            "verticalKeys": ["W","E"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "N" },
                "logCrosshair": { "enabled": false }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("horizontalKeys") && v.Contains("must not be empty"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Load_ScrollHotKeysSection_Deserializes() {
        var json = """
        {
            "configVersion": 3,
            "scrollHotkeys": {
                "enabled": true,
                "scrollUpKey": { "modifiers": "Control, Alt", "key": "Prior" },
                "scrollDownKey": { "modifiers": "Control, Alt", "key": "Next" },
                "scrollAmount": 5
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.True(result.Config.ScrollHotKeys.Enabled);
            Assert.Equal(HotKeyModifiers.Control | HotKeyModifiers.Alt, result.Config.ScrollHotKeys.ScrollUpKey.Modifiers);
            Assert.Equal(Input.VKey.Prior, result.Config.ScrollHotKeys.ScrollUpKey.Key);
            Assert.Equal(Input.VKey.Next, result.Config.ScrollHotKeys.ScrollDownKey.Key);
            Assert.Equal(5, result.Config.ScrollHotKeys.ScrollAmount);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Load_MissingScrollHotKeys_UsesDefaults() {
        var json = """
        {
            "configVersion": 3
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.False(result.Config.ScrollHotKeys.Enabled);
            Assert.Equal(3, result.Config.ScrollHotKeys.ScrollAmount);
            Assert.Equal(Input.VKey.Prior, result.Config.ScrollHotKeys.ScrollUpKey.Key);
            Assert.Equal(Input.VKey.Next, result.Config.ScrollHotKeys.ScrollDownKey.Key);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_ScrollAmountBelowMin_ReportsViolation() {
        var json = """
        {
            "configVersion": 3,
            "scrollHotkeys": { "enabled": true, "scrollAmount": 0 }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("scrollAmount") && v.Contains("between 1 and 100"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_ScrollAmountAboveMax_ReportsViolation() {
        var json = """
        {
            "configVersion": 3,
            "scrollHotkeys": { "enabled": true, "scrollAmount": 101 }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("scrollAmount") && v.Contains("between 1 and 100"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_DuplicateScrollKeys_ReportsViolation() {
        var json = """
        {
            "configVersion": 3,
            "scrollHotkeys": {
                "enabled": true,
                "scrollUpKey": { "modifiers": "Control, Alt", "key": "Prior" },
                "scrollDownKey": { "modifiers": "Control, Alt", "key": "Prior" }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Scroll up and scroll down") && v.Contains("different"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_ScrollKeyConflictsWithAction_ReportsViolation() {
        var json = """
        {
            "configVersion": 3,
            "actionBindings": { "Prior": "RightClick" },
            "scrollHotkeys": {
                "enabled": true,
                "scrollUpKey": { "modifiers": "Control, Alt", "key": "Prior" },
                "scrollDownKey": { "modifiers": "Control, Alt", "key": "Next" }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("scrollUpKey") && v.Contains("action"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_ScrollKeyConflictsWithNavigation_ReportsViolation() {
        var json = """
        {
            "configVersion": 3,
            "scrollHotkeys": {
                "enabled": true,
                "scrollUpKey": { "modifiers": "Control, Alt", "key": "A" },
                "scrollDownKey": { "modifiers": "Control, Alt", "key": "Next" }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("scrollUpKey") && v.Contains("navigation"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_ScrollDisabled_SkipsValidation() {
        var json = """
        {
            "configVersion": 3,
            "scrollHotkeys": { "enabled": false, "scrollAmount": 0 }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.DoesNotContain(result.Violations, v => v.Contains("scrollAmount"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_ScrollKeyConflictsWithHotkey_ReportsViolation() {
        var json = """
        {
            "configVersion": 4,
            "hotKey": { "modifiers": "Alt", "key": "Prior" },
            "scrollHotkeys": {
                "enabled": true,
                "scrollUpKey": { "modifiers": "Control, Alt", "key": "Prior" },
                "scrollDownKey": { "modifiers": "Control, Alt", "key": "Next" }
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("scrollUpKey") && v.Contains("hotkey"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_AppScopeChordConflictsWithAction_ReportsViolation() {
        var json = """
        {
            "configVersion": 6,
            "actionBindings": { "B": "DoubleClick" },
            "appScope": { "chordKey": "B" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("appScope") && v.Contains("action"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_AppScopeChordConflictsWithModeChord_ReportsViolation() {
        var json = """
        {
            "configVersion": 6,
            "modes": {
                "uniformGrid": { "enabled": true, "default": true },
                "crosshair": { "enabled": true, "chordKey": "B", "twoKey": true }
            },
            "appScope": { "chordKey": "B" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("appScope") && v.Contains("mode chord"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_AppScopeChordConflictsWithNavKey_ReportsViolation() {
        var json = """
        {
            "configVersion": 6,
            "horizontalKeys": ["A","S","D","B"],
            "verticalKeys": ["W","E","R","T"],
            "modes": { "uniformGrid": { "enabled": true, "default": true } },
            "appScope": { "chordKey": "B" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("appScope") && v.Contains("navigation"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_AppScopeChordNull_NoViolations() {
        var json = """
        {
            "configVersion": 6,
            "appScope": { "chordKey": null }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.DoesNotContain(result.Violations, v => v.Contains("appScope"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_AppScopeChordReserved_ReportsViolation() {
        var json = """
        {
            "configVersion": 6,
            "appScope": { "chordKey": "Escape" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("appScope") && v.Contains("reserved"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_AppScopeChordConflictsWithScrollKey_ReportsViolation() {
        var json = """
        {
            "configVersion": 6,
            "scrollHotkeys": {
                "enabled": true,
                "scrollUpKey": { "modifiers": "Control, Alt", "key": "B" },
                "scrollDownKey": { "modifiers": "Control, Alt", "key": "Next" }
            },
            "appScope": { "chordKey": "B" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("appScope") && v.Contains("scroll"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Validate_AppScopeChordConflictsWithMacroKey_ReportsViolation() {
        var json = """
        {
            "configVersion": 6,
            "macros": { "enabled": true, "recordKey": "B" },
            "appScope": { "chordKey": "B" }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("appScope") && v.Contains("macro"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void RejectsDigitDisplayKeysInSlotKeys() {
        var json = """
        {
            "configVersion": 7,
            "macros": {
                "enabled": true,
                "slotKeys": ["D1","F2","F3","F4","F5","F6","F7","F8","F9","F10"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("slotKeys") && v.Contains("D1"));
        } finally { Cleanup(path); }
    }

    private static string WriteTempFile(string content) {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, content);
        return path;
    }

    private static void Cleanup(string path) {
        try { File.Delete(path); } catch { }
        try { File.Delete(path + ".bak"); } catch { }
        try { File.Delete(path + ".tmp"); } catch { }
    }
}
