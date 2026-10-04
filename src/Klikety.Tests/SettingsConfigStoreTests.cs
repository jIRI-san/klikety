using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Klikety.Config;
using Klikety.Input;

namespace Klikety.Tests;

public sealed class SettingsConfigStoreTests : IDisposable {
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
    public void Save_ExternalDeleteDoesNotRecreateConfig() {
        File.WriteAllText(_path, "{\"configVersion\":7}");
        var store = Open();
        File.Delete(_path);

        var error = Assert.Throws<IOException>(() => store.Save(new Dictionary<string, JsonNode?> {
            ["theme"] = JsonValue.Create("light"),
        }));

        Assert.Contains("changed on disk", error.Message);
        Assert.False(File.Exists(_path));
        Assert.Single(Directory.GetFiles(_folder));
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
