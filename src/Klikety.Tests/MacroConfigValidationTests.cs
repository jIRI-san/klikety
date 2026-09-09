using System.IO;

using Klikety.Config;

namespace Klikety.Tests;

public class MacroConfigValidationTests {
    private static readonly string BaseConfig = """
        {
            "configVersion": 5,
            "horizontalKeys": ["A","S","D","F","G","H","J","K","L","OemSemicolon"],
            "verticalKeys": ["Q","W","E","R","T","Y","U","I","O","P"],
            "modes": { "uniformGrid": { "enabled": true, "default": true, "twoKey": true } },
            "scrollHotkeys": { "enabled": false },
        """;

    [Fact]
    public void MacrosDefaults_NoViolations() {
        var json = BaseConfig + """
            "macros": {
                "enabled": true,
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10"],
                "speedModifier": 1.0
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            var macroViolations = result.Violations.Where(v => v.Contains("macro", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Empty(macroViolations);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void RecordKeyEqualsHelperKey_ReportsViolation() {
        var json = BaseConfig + """
            "macros": {
                "enabled": true,
                "recordKey": "OemPipe",
                "helperKey": "OemPipe",
                "slotKeys": ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("recordKey") && v.Contains("helperKey") && v.Contains("different"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void RecordKeyConflictsWithActionBinding_ReportsViolation() {
        var json = BaseConfig + """
            "actionBindings": { "OemPipe": "leftClick" },
            "macros": {
                "enabled": true,
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("macros.recordKey") && v.Contains("action binding"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void HelperKeyConflictsWithChordKey_ReportsViolation() {
        var json = """
        {
            "configVersion": 5,
            "horizontalKeys": ["A","S","D","F","G","H","J","K","L","OemSemicolon"],
            "verticalKeys": ["Q","W","E","R","T","Y","U","I","O","P"],
            "modes": {
                "uniformGrid": { "enabled": true, "default": true, "twoKey": true },
                "crosshair": { "enabled": true, "twoKey": true, "chordKey": "OemTilde" }
            },
            "scrollHotkeys": { "enabled": false },
            "macros": {
                "enabled": true,
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("macros.helperKey") && v.Contains("chord key"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void SlotKeyConflictsWithNavKey_ReportsViolation() {
        // A is in horizontalKeys — conflict with slot key
        var json = BaseConfig + """
            "macros": {
                "enabled": true,
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["A","F2","F3","F4","F5","F6","F7","F8","F9","F10"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("slotKeys[0]") && v.Contains("navigation key"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void DuplicateSlotKeys_ReportsViolation() {
        var json = BaseConfig + """
            "macros": {
                "enabled": true,
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F1","F3","F4","F5","F6","F7","F8","F9","F10"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("duplicate") && v.Contains("F1"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void SlotKeyConflictsWithRecordKey_ReportsViolation() {
        var json = BaseConfig + """
            "macros": {
                "enabled": true,
                "recordKey": "F1",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("slot key") && v.Contains("recordKey"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void NegativeSpeedModifier_ReportsViolation() {
        var json = BaseConfig + """
            "macros": {
                "enabled": true,
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10"],
                "speedModifier": -0.5
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("speedModifier") && v.Contains(">= 0"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void ZeroSpeedModifier_NoViolation() {
        var json = BaseConfig + """
            "macros": {
                "enabled": true,
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10"],
                "speedModifier": 0
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            var macroViolations = result.Violations.Where(v => v.Contains("speedModifier")).ToList();
            Assert.Empty(macroViolations);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void SlotKeysFewerThan10_ReportsWarning() {
        var json = BaseConfig + """
            "macros": {
                "enabled": true,
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F2","F3"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("slotKeys") && v.Contains("fewer than 10"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void SlotKeysMoreThan10_ReportsWarning() {
        var json = BaseConfig + """
            "macros": {
                "enabled": true,
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","Z"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("slotKeys") && v.Contains("more than 10"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void NullGlobalHotKey_NoViolation() {
        var json = BaseConfig + """
            "macros": {
                "enabled": true,
                "globalHotKey": null,
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            var macroViolations = result.Violations.Where(v => v.Contains("globalHotKey")).ToList();
            Assert.Empty(macroViolations);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void GlobalHotKeyConflictsWithMainHotKey_ReportsViolation() {
        var json = """
        {
            "configVersion": 5,
            "hotKey": { "modifiers": "Alt", "key": "Space" },
            "horizontalKeys": ["A","S","D","F","G","H","J","K","L","OemSemicolon"],
            "verticalKeys": ["Q","W","E","R","T","Y","U","I","O","P"],
            "modes": { "uniformGrid": { "enabled": true, "default": true, "twoKey": true } },
            "scrollHotkeys": { "enabled": false },
            "macros": {
                "enabled": true,
                "globalHotKey": { "modifiers": "Alt", "key": "Space" },
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("globalHotKey") && v.Contains("main application hotkey"));
        } finally { Cleanup(path); }
    }

    [Fact]
    public void MacrosDisabled_SkipsValidation() {
        var json = BaseConfig + """
            "macros": {
                "enabled": false,
                "recordKey": "Escape",
                "helperKey": "Escape"
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            var macroViolations = result.Violations.Where(v => v.Contains("macro", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Empty(macroViolations);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void ScrollKeyConflictsWithMacroKey_ReportsViolation() {
        var json = """
        {
            "configVersion": 5,
            "horizontalKeys": ["A","S","D","F","G","H","J","K","L","OemSemicolon"],
            "verticalKeys": ["Q","W","E","R","T","Y","U","I","O","P"],
            "modes": { "uniformGrid": { "enabled": true, "default": true, "twoKey": true } },
            "scrollHotkeys": {
                "enabled": true,
                "scrollUpKey": { "modifiers": "Control, Alt", "key": "OemPipe" },
                "scrollDownKey": { "modifiers": "Control, Alt", "key": "Next" }
            },
            "macros": {
                "enabled": true,
                "recordKey": "OemPipe",
                "helperKey": "OemTilde",
                "slotKeys": ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10"]
            }
        }
        """;
        var path = WriteTempFile(json);
        try {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("macros.recordKey") && v.Contains("scroll hotkey"));
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
    }
}
