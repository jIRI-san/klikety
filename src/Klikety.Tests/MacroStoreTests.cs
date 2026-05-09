using System.IO;
using System.Text.Json;

using Klikety.Config;

namespace Klikety.Tests;

public class MacroStoreTests : IDisposable {
    private readonly string _tempDir;
    private readonly string _macrosPath;
    private readonly MacroStore _store;

    public MacroStoreTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), "klikety-macrostore-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _macrosPath = Path.Combine(_tempDir, "macros.json");
        _store = new MacroStore(_macrosPath);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyState() {
        var result = _store.Load();

        Assert.Empty(result.Errors);
        Assert.Equal(10, result.File.Macros.Length);
        Assert.All(result.File.Macros, m => Assert.Null(m));
    }

    [Fact]
    public void Load_ValidFile_ReturnsCorrectModel() {
        var file = new MacrosFile {
            Version = 1,
            Macros = CreateTestMacrosArray(),
        };
        WriteJson(file);

        var result = _store.Load();

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.File.Version);
        Assert.NotNull(result.File.Macros[0]);
        Assert.Equal("Test Macro", result.File.Macros[0]!.Name);
        Assert.Single(result.File.Macros[0]!.Steps);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmptyWithError() {
        File.WriteAllText(_macrosPath, "{ not valid json");

        var result = _store.Load();

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("could not be parsed"));
        Assert.Equal(10, result.File.Macros.Length);
    }

    [Fact]
    public void Load_EmptyJson_ReturnsEmptyState() {
        File.WriteAllText(_macrosPath, "null");

        var result = _store.Load();

        Assert.Empty(result.Errors);
        Assert.Equal(10, result.File.Macros.Length);
    }

    [Fact]
    public void Load_ArrayShorterThan10_PadsToTen() {
        var json = """{ "version": 1, "macros": [null, null, null] }""";
        File.WriteAllText(_macrosPath, json);

        var result = _store.Load();

        Assert.Empty(result.Errors);
        Assert.Equal(10, result.File.Macros.Length);
    }

    [Fact]
    public void Load_NullMacrosArray_PadsToTen() {
        var json = """{ "version": 1, "macros": null }""";
        File.WriteAllText(_macrosPath, json);

        var result = _store.Load();

        Assert.Empty(result.Errors);
        Assert.Equal(10, result.File.Macros.Length);
    }

    [Fact]
    public void Load_InvalidStep_QuarantinesSlot() {
        // DragDrop step without DragButton → invalid
        var json = """
        {
            "version": 1,
            "macros": [
                {
                    "name": "Bad Drag",
                    "screenWidth": 1920,
                    "screenHeight": 1080,
                    "dpiScale": 1.0,
                    "steps": [
                        {
                            "actionType": "dragDrop",
                            "x": 100, "y": 200,
                            "modifiers": "none",
                            "relativeTimeMs": 0,
                            "endX": 300, "endY": 400
                        }
                    ]
                },
                null, null, null, null, null, null, null, null, null
            ]
        }
        """;
        File.WriteAllText(_macrosPath, json);

        var result = _store.Load();

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("DragButton"));
        Assert.Null(result.File.Macros[0]); // quarantined
    }

    [Fact]
    public void Load_NegativeCoordinates_QuarantinesSlot() {
        var json = """
        {
            "version": 1,
            "macros": [
                {
                    "name": "Bad Coords",
                    "screenWidth": 1920,
                    "screenHeight": 1080,
                    "dpiScale": 1.0,
                    "steps": [
                        {
                            "actionType": "leftClick",
                            "x": -1, "y": 200,
                            "modifiers": "none",
                            "relativeTimeMs": 0
                        }
                    ]
                },
                null, null, null, null, null, null, null, null, null
            ]
        }
        """;
        File.WriteAllText(_macrosPath, json);

        var result = _store.Load();

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("negative"));
        Assert.Null(result.File.Macros[0]);
    }

    [Fact]
    public void Load_NegativeEndCoordinates_QuarantinesSlot() {
        var json = """
        {
            "version": 1,
            "macros": [
                {
                    "name": "Bad End",
                    "screenWidth": 1920,
                    "screenHeight": 1080,
                    "dpiScale": 1.0,
                    "steps": [
                        {
                            "actionType": "dragDrop",
                            "x": 100, "y": 200,
                            "modifiers": "none",
                            "relativeTimeMs": 0,
                            "endX": -5, "endY": 400,
                            "dragButton": "leftClick"
                        }
                    ]
                },
                null, null, null, null, null, null, null, null, null
            ]
        }
        """;
        File.WriteAllText(_macrosPath, json);

        var result = _store.Load();

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("endX is negative"));
        Assert.Null(result.File.Macros[0]);
    }

    [Fact]
    public void Load_NullSteps_QuarantinesSlot() {
        var json = """
        {
            "version": 1,
            "macros": [
                {
                    "name": "Null Steps",
                    "screenWidth": 1920,
                    "screenHeight": 1080,
                    "dpiScale": 1.0,
                    "steps": null
                },
                null, null, null, null, null, null, null, null, null
            ]
        }
        """;
        File.WriteAllText(_macrosPath, json);

        var result = _store.Load();

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("steps array is null"));
        Assert.Null(result.File.Macros[0]);
    }

    [Fact]
    public void Load_ScrollStepMissingDelta_QuarantinesSlot() {
        var json = """
        {
            "version": 1,
            "macros": [
                {
                    "name": "Bad Scroll",
                    "screenWidth": 1920,
                    "screenHeight": 1080,
                    "dpiScale": 1.0,
                    "steps": [
                        {
                            "actionType": "scroll",
                            "x": 100, "y": 200,
                            "modifiers": "none",
                            "relativeTimeMs": 0
                        }
                    ]
                },
                null, null, null, null, null, null, null, null, null
            ]
        }
        """;
        File.WriteAllText(_macrosPath, json);

        var result = _store.Load();

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("ScrollDelta"));
        Assert.Null(result.File.Macros[0]);
    }

    [Fact]
    public void Load_TimingExceeds10Minutes_QuarantinesSlot() {
        var json = """
        {
            "version": 1,
            "macros": [
                {
                    "name": "Slow",
                    "screenWidth": 1920,
                    "screenHeight": 1080,
                    "dpiScale": 1.0,
                    "steps": [
                        {
                            "actionType": "leftClick",
                            "x": 100, "y": 200,
                            "modifiers": "none",
                            "relativeTimeMs": 700000
                        }
                    ]
                },
                null, null, null, null, null, null, null, null, null
            ]
        }
        """;
        File.WriteAllText(_macrosPath, json);

        var result = _store.Load();

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("10 minutes"));
        Assert.Null(result.File.Macros[0]);
    }

    [Fact]
    public void Save_WritesAtomically_ThenLoadRoundTrips() {
        var file = new MacrosFile {
            Version = 1,
            Macros = CreateTestMacrosArray(),
        };

        var saveResult = _store.Save(file);
        Assert.True(saveResult.Success);
        Assert.Null(saveResult.Error);

        // No temp files left behind
        var leftover = Directory.GetFiles(_tempDir).Where(f =>
            Path.GetFileName(f) != "macros.json").ToArray();
        Assert.Empty(leftover);

        // Round-trip
        var loadResult = _store.Load();
        Assert.Empty(loadResult.Errors);
        Assert.Equal("Test Macro", loadResult.File.Macros[0]!.Name);
        Assert.Equal(1920, loadResult.File.Macros[0]!.ScreenWidth);
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing() {
        var nestedDir = Path.Combine(_tempDir, "sub", "dir");
        var nestedPath = Path.Combine(nestedDir, "macros.json");
        var nestedStore = new MacroStore(nestedPath);

        var result = nestedStore.Save(new MacrosFile());

        Assert.True(result.Success);
        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void Save_ReadOnlyTarget_ReturnsError() {
        // Write a file first, then make it read-only
        File.WriteAllText(_macrosPath, "{}");
        File.SetAttributes(_macrosPath, FileAttributes.ReadOnly);

        try {
            var result = _store.Save(new MacrosFile());

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        } finally {
            File.SetAttributes(_macrosPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Load_IoError_ReturnsError() {
        // Create file and hold an exclusive lock
        using var stream = new FileStream(_macrosPath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write("{}"u8);
        stream.Flush();

        var result = _store.Load();

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("Failed to read"));
    }

    [Fact]
    public void Load_ValidSlots_PreservedWhileInvalid_Quarantined() {
        var json = """
        {
            "version": 1,
            "macros": [
                {
                    "name": "Good",
                    "screenWidth": 1920,
                    "screenHeight": 1080,
                    "dpiScale": 1.0,
                    "steps": [
                        { "actionType": "leftClick", "x": 100, "y": 200, "modifiers": "none", "relativeTimeMs": 0 }
                    ]
                },
                {
                    "name": "Bad",
                    "screenWidth": 1920,
                    "screenHeight": 1080,
                    "dpiScale": 1.0,
                    "steps": [
                        { "actionType": "scroll", "x": 100, "y": 200, "modifiers": "none", "relativeTimeMs": 0 }
                    ]
                },
                null, null, null, null, null, null, null, null
            ]
        }
        """;
        File.WriteAllText(_macrosPath, json);

        var result = _store.Load();

        Assert.NotNull(result.File.Macros[0]); // good slot preserved
        Assert.Equal("Good", result.File.Macros[0]!.Name);
        Assert.Null(result.File.Macros[1]); // bad slot quarantined
        Assert.NotEmpty(result.Errors);
    }

    private static MacroDefinition?[] CreateTestMacrosArray() {
        var macros = new MacroDefinition?[10];
        macros[0] = new MacroDefinition {
            Name = "Test Macro",
            ScreenWidth = 1920,
            ScreenHeight = 1080,
            DpiScale = 1.0,
            Steps = [
                new MacroStep {
                    ActionType = MacroActionType.LeftClick,
                    X = 500,
                    Y = 300,
                    Modifiers = ActionModifiers.None,
                    RelativeTimeMs = 0,
                },
            ],
        };
        return macros;
    }

    private static readonly JsonSerializerOptions WriteOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private void WriteJson(MacrosFile file) {
        var json = JsonSerializer.Serialize(file, WriteOptions);
        File.WriteAllText(_macrosPath, json);
    }
}
