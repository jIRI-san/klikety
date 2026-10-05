using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Klikety.Config;
using Klikety.Input;

namespace Klikety.Tests;

public sealed class SettingsConfigStoreTests : IDisposable {
    [Theory]
    [InlineData("hotKey.key", "\"Pause\"")]
    [InlineData("macros.globalHotKey.key", "\"Pause\"")]
    [InlineData("scrollHotkeys.enabled", "true")]
    [InlineData("macros.playbackIndicator.strokeThickness", "2.5")]
    [InlineData("keyPressVisualization.margin", "50")]
    [InlineData("appScope.chordKey", "null")]
    [InlineData("modes.uniformGrid.logBaseSize", "11")]
    [InlineData("modes.logGrid.logGridBaseSize", "15")]
    public void InsertedNestedObjectsKeepEveryOtherEffectiveDefault(string path, string json) {
        File.WriteAllText(_path, "{\"configVersion\":7}");
        var store = Open();
        var expected = SettingsFieldCases.Serialize(new SettingsConfigStore(_path).Open().Config);
        SettingsValidationTests.Set(expected, path, JsonNode.Parse(json));
        store.Save(new Dictionary<string, JsonNode?> { [path] = JsonNode.Parse(json) });
        var actual = SettingsFieldCases.Serialize(new SettingsConfigStore(_path).Open().Config);
        Assert.True(JsonNode.DeepEquals(expected, actual), path);
    }

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Klikety-settings-tests-" + Guid.NewGuid());
    private readonly string _path;

    public SettingsConfigStoreTests() {
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "config.json");
    }

    [Fact]
    public void Save_ChangesOnlyLeaves_PreservesJsoncUnknownsAndBackup() {
        const string original = """
        {
          // user's overview
          "configVersion": 7,
          "hotKey": { "modifiers": "Alt", /* keep */ "key": "Space", "future": 1, },
          "theme": "dark", // favorite
          "unknown": {"exact": [1,  2,], "note": "héllo"},
        }
        """;
        File.WriteAllText(_path, original, new UTF8Encoding(true));
        var before = File.ReadAllBytes(_path);
        var store = Open();
        var saved = store.Save(new Dictionary<string, JsonNode?> {
            ["hotKey.modifiers"] = JsonValue.Create("Control, Alt"),
            ["theme"] = JsonValue.Create("light"),
            ["minLabelFontSize"] = JsonValue.Create(18),
        });
        Assert.Equal(HotKeyModifiers.Control | HotKeyModifiers.Alt, saved.HotKey.Modifiers);
        Assert.Equal("light", saved.Theme);
        var text = File.ReadAllText(_path);
        Assert.Contains("// user's overview", text);
        Assert.Contains("/* keep */", text);
        Assert.Contains("\"unknown\": {\"exact\": [1,  2,], \"note\": \"héllo\"}", text);
        Assert.Contains("\"future\": 1,", text);
        Assert.Contains("// favorite", text);
        Assert.Equal(before, File.ReadAllBytes(_path + ".settings.bak"));
        Assert.True(File.ReadAllBytes(_path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal(18, new SettingsConfigStore(_path).Open().Config.MinLabelFontSize);
    }

    [Theory]
    [InlineData("{\"configVersion\":7}")]
    [InlineData("{\"configVersion\":7, // trailing\n}")]
    [InlineData("{\"configVersion\":7,\"hotKey\":{/* empty */}}")]
    public void Save_InsertsMissingFieldsWithoutDroppingComments(string json) {
        File.WriteAllText(_path, json);
        var store = Open();
        store.Save(new Dictionary<string, JsonNode?> { ["hotKey.modifiers"] = JsonValue.Create("Control") });
        Assert.Equal(HotKeyModifiers.Control, new SettingsConfigStore(_path).Open().Config.HotKey.Modifiers);
        if (json.Contains("/* empty */")) {
            Assert.Contains("/* empty */", File.ReadAllText(_path));
        }
    }

    [Theory]
    [InlineData("{not json}")]
    [InlineData("null")]
    [InlineData("{\"configVersion\":8}")]
    [InlineData("{\"configVersion\":7,\"hotKey\":null}")]
    [InlineData("{\"configVersion\":7,\"theme\":\"dark\",\"Theme\":\"light\"}")]
    public void Open_RejectsMalformedNullFutureAndDuplicateInputs(string json) {
        File.WriteAllText(_path, json);
        var error = Record.Exception(() => Open());
        Assert.True(error is JsonException or InvalidDataException, error?.ToString());
        Assert.Equal(json, File.ReadAllText(_path));
        Assert.False(File.Exists(_path + ".settings.bak"));
    }

    [Fact]
    public void Save_InvalidBinding_RejectsWithoutWriting() {
        File.WriteAllText(_path, "{\"configVersion\":7}");
        var store = Open();
        var error = Assert.Throws<InvalidDataException>(() => store.Save(new Dictionary<string, JsonNode?> {
            ["hotKey.key"] = JsonValue.Create("A"),
        }));
        Assert.Contains("conflicts with a navigation key", error.Message);
        Assert.Equal("{\"configVersion\":7}", File.ReadAllText(_path));
        Assert.False(File.Exists(_path + ".settings.bak"));
    }

    [Fact]
    public void Save_ModeAndSizingRules_UseConfigLoaderValidation() {
        File.WriteAllText(_path, "{\"configVersion\":7}");
        var store = Open();
        var error = Assert.Throws<InvalidDataException>(() => store.Save(new Dictionary<string, JsonNode?> {
            ["modes.logGrid.twoKey"] = JsonValue.Create(false),
            ["modes.logCrosshair.logBaseSize"] = JsonValue.Create(90),
            ["minLabelFontSize"] = JsonValue.Create(-2),
        }));
        Assert.Contains("require twoKey", error.Message);
        Assert.Contains("between 2 and 50", error.Message);
        Assert.Contains("label size", error.Message);
    }

    [Fact]
    public void Save_DefaultModeAndUniformChord_ReopensThroughExistingLoader() {
        File.WriteAllText(_path, "{\"configVersion\":7}");
        var store = Open();
        store.Save(new Dictionary<string, JsonNode?> {
            ["modes.uniformGrid.default"] = JsonValue.Create(false),
            ["modes.uniformGrid.chordKey"] = JsonValue.Create("OemPlus"),
            ["modes.crosshair.default"] = JsonValue.Create(true),
        });
        var result = ConfigLoader.Load(_path);
        Assert.Empty(result.Violations);
        Assert.True(result.Config.Modes.Crosshair.Default);
        Assert.False(result.Config.Modes.UniformGrid.Default);
        Assert.Equal(VKey.OemPlus, result.Config.Modes.UniformGrid.ChordKey);
    }

    [Fact]
    public void Save_ExternalEditAndPreflightFailure_DoNotOverwrite() {
        File.WriteAllText(_path, "{\"configVersion\":7}");
        var store = Open();
        var change = new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") };
        Assert.Throws<InvalidDataException>(() => store.Save(change, _ => throw new InvalidDataException("Theme missing.")));
        Assert.Equal("{\"configVersion\":7}", File.ReadAllText(_path));
        File.AppendAllText(_path, "\n// external edit");
        var error = Assert.Throws<IOException>(() => store.Save(change));
        Assert.Contains("changed on disk", error.Message);
        Assert.EndsWith("// external edit", File.ReadAllText(_path));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void Save_NoOpDoesNotReplaceFileOrCreateBackup() {
        const string json = "{\"configVersion\":7,\"theme\":\"dark\"}";
        File.WriteAllText(_path, json);
        var before = File.ReadAllBytes(_path);
        var store = Open();

        store.Save(new Dictionary<string, JsonNode?>());

        Assert.Equal(before, File.ReadAllBytes(_path));
        Assert.False(File.Exists(_path + ".settings.bak"));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void Open_RejectsDuplicatePropertiesInsideArrays() {
        const string json = "{\"configVersion\":7,\"unknown\":[{\"value\":1,\"Value\":2}]}";
        File.WriteAllText(_path, json);

        var error = Assert.Throws<InvalidDataException>(() => Open());

        Assert.Contains("Duplicate config property 'Value'", error.Message);
        Assert.Equal(json, File.ReadAllText(_path));
    }

    [Fact]
    public void Save_AxisArrayMovesAddsAndDeletesKeepElementAndOrphanComments() {
        const string json = """
        {
          "configVersion": 7,
          "horizontalKeys": ["A", /* keep A */ "S", // orphan S
            "D",],
        }
        """;
        File.WriteAllText(_path, json);
        var store = Open();

        store.Save(new Dictionary<string, JsonNode?> {
            ["horizontalKeys"] = new JsonArray("D", "A", "F"),
        });

        var saved = File.ReadAllText(_path);
        var array = saved[saved.IndexOf('[')..(saved.IndexOf(']') + 1)];
        Assert.Contains("/* keep A */", array);
        Assert.Contains("// orphan S", array);
        Assert.Equal([VKey.D, VKey.A, VKey.F], new SettingsConfigStore(_path).Open().Config.HorizontalKeys);
    }

    [Fact]
    public void Save_ActionBindingDeleteKeepsMemberCommentsInsideObject() {
        const string json = """
        {
          "configVersion": 7,
          "actionBindings": {
            "OemOpenBrackets": "leftClick", /* preserve member context */
            "OemCloseBrackets": /* retain comment inside deleted member */ "rightClick", // orphan deleted-member comment
          },
        }
        """;
        File.WriteAllText(_path, json);
        var store = Open();

        store.Save(new Dictionary<string, JsonNode?> {
            ["actionBindings"] = new JsonObject {
                ["OemOpenBrackets"] = "leftClick",
                ["Z"] = "doubleClick",
            },
        });

        var saved = File.ReadAllText(_path);
        var map = saved[saved.IndexOf('{', saved.IndexOf("\"actionBindings\"", StringComparison.Ordinal))..];
        var close = map.IndexOf('}');
        Assert.Contains("/* preserve member context */", map[..(close + 1)]);
        Assert.Contains("/* retain comment inside deleted member */", map[..(close + 1)]);
        Assert.Contains("// orphan deleted-member comment", map[..(close + 1)]);
        var config = new SettingsConfigStore(_path).Open().Config;
        Assert.Equal(2, config.ActionBindings.Count);
        Assert.Equal(MouseAction.DoubleClick, config.ActionBindings["Z"]);
        Assert.False(config.ActionBindings.ContainsKey("OemCloseBrackets"));
    }

    [Fact]
    public void RestoreLastCommit_RestoresExactOldBytesWithoutReplacingBackup() {
        const string json = "{\"configVersion\":7,\"theme\":\"dark\"}";
        File.WriteAllText(_path, json, new UTF8Encoding(true));
        var original = File.ReadAllBytes(_path);
        var store = Open();
        store.Save(new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") });
        var backup = File.ReadAllBytes(_path + ".settings.bak");

        var result = store.RestoreLastCommit();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(original, File.ReadAllBytes(_path));
        Assert.Equal(original, backup);
        Assert.Equal(backup, File.ReadAllBytes(_path + ".settings.bak"));
        Assert.False(store.HasUnacceptedCommit);
    }

    [Fact]
    public void RestoreLastCommit_RefusesToOverwriteExternalBytes() {
        File.WriteAllText(_path, "{\"configVersion\":7,\"theme\":\"dark\"}");
        var store = Open();
        store.Save(new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") });
        var backup = File.ReadAllBytes(_path + ".settings.bak");
        File.AppendAllText(_path, "\n// external");
        var external = File.ReadAllBytes(_path);

        var result = store.RestoreLastCommit();

        Assert.False(result.Succeeded);
        Assert.Contains("Newer external bytes were kept", result.Message);
        Assert.Equal(external, File.ReadAllBytes(_path));
        Assert.Equal(backup, File.ReadAllBytes(_path + ".settings.bak"));
    }

    [Fact]
    public void Save_ExternalDeleteDoesNotRecreateConfig() {
        File.WriteAllText(_path, "{\"configVersion\":7}");
        var store = Open();
        File.Delete(_path);

        var error = Assert.Throws<IOException>(() => store.Save(new Dictionary<string, JsonNode?> {
            ["theme"] = JsonValue.Create("light"),
        }));

        Assert.Contains("changed on disk", error.Message);
        Assert.False(File.Exists(_path));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void Save_CompatibilityWarningDoesNotBlockUnrelatedEdit() {
        File.WriteAllText(_path, "{\"configVersion\":7,\"macros\":{\"slotKeys\":[\"F1\"]}}");
        var store = Open();

        store.Save(new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") });

        Assert.Equal("light", new SettingsConfigStore(_path).Open().Config.Theme);
        Assert.Contains(store.LastWarnings, warning => warning.Contains("fewer than 10 entries", StringComparison.Ordinal));
    }

    [Fact]
    public void Open_SeparatesSettingsErrorsFromAdvisoryWarnings() {
        File.WriteAllText(_path, "{\"configVersion\":7,\"logLevel\":\"verbose\",\"retainedLogFileCount\":0}");

        var result = new SettingsConfigStore(_path).Open();

        Assert.Contains(result.SettingsBlockingErrors, error => error.Contains("logLevel", StringComparison.Ordinal));
        Assert.Contains(result.SettingsBlockingErrors, error => error.Contains("retainedLogFileCount", StringComparison.Ordinal));
        Assert.Empty(result.SettingsWarnings);
    }

    [Fact]
    public void Save_PreservesUntouchedLegacyIndicatorFloorsButRejectsEditingBelowThem() {
        File.WriteAllText(_path, """
        {"configVersion":7,"macros":{"playbackIndicator":{"initialRadius":0.5,"finalRadius":0.75,"animationDurationMs":50}}}
        """);
        var store = Open();
        var opened = store.Open();
        Assert.Empty(opened.SettingsBlockingErrors);
        Assert.Equal(3, opened.SettingsWarnings.Count);

        store.Save(new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") });
        var beforeRejectedEdit = File.ReadAllBytes(_path);
        Assert.Throws<InvalidDataException>(() => store.Save(
            new Dictionary<string, JsonNode?> { ["macros.playbackIndicator.initialRadius"] = JsonValue.Create(0.5) }));
        Assert.Equal(beforeRejectedEdit, File.ReadAllBytes(_path));

        var corrected = store.Save(new Dictionary<string, JsonNode?> {
            ["macros.playbackIndicator.initialRadius"] = JsonValue.Create(1),
            ["macros.playbackIndicator.finalRadius"] = JsonValue.Create(1),
            ["macros.playbackIndicator.animationDurationMs"] = JsonValue.Create(100),
        });
        Assert.Equal(1, corrected.Macros.PlaybackIndicator.InitialRadius);
        Assert.Equal(100, corrected.Macros.PlaybackIndicator.AnimationDurationMs);
    }

    [Fact]
    public void Save_RejectsUnsafeIndicatorAndNegativeSpeedEvenWhenMacrosAreDisabled() {
        File.WriteAllText(_path, """
        {"configVersion":7,"macros":{"enabled":false,"speedModifier":-0.1,"playbackIndicator":{"fillColor":"not-a-color"}}}
        """);
        var store = Open();
        var opened = store.Open();
        Assert.Contains(opened.SettingsBlockingErrors, error => error.Contains("speedModifier", StringComparison.Ordinal));
        Assert.Contains(opened.SettingsBlockingErrors, error => error.Contains("fillColor", StringComparison.Ordinal));
        var original = File.ReadAllBytes(_path);

        Assert.Throws<InvalidDataException>(() => store.Save(
            new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") }));
        Assert.Equal(original, File.ReadAllBytes(_path));
    }

    private SettingsConfigStore Open() {
        var store = new SettingsConfigStore(_path);
        store.Open();
        return store;
    }

    public void Dispose() {
        foreach (var file in Directory.GetFiles(_folder)) {
            File.Delete(file);
        }
        Directory.Delete(_folder);
    }
}
