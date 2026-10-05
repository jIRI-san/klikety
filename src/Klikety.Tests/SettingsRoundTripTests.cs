using System.Text;
using System.Text.Json.Nodes;

using Klikety.Config;

namespace Klikety.Tests;

public sealed class SettingsRoundTripTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Klikety-roundtrip-" + Guid.NewGuid());
    private readonly string _path;
    public SettingsRoundTripTests() {
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "config.json");
        File.WriteAllText(_path, """
        {
          "$schema" : "preserve-schema",
          "configVersion" : 7,
          "extension" : {"exact":[1.0,  2e0,], /* user's unknown */ "flag":true},
          // retain root note
        }
        """, new UTF8Encoding(true));
    }

    [Fact]
    public void EveryEditableFieldSavesAndReopensWithoutChangingMetadataOrExtensions() {
        var original = File.ReadAllBytes(_path);
        var store = new SettingsConfigStore(_path);
        store.Open();
        var changes = SettingsFieldCases.All.ToDictionary(field => field.Path, field => JsonNode.Parse(field.Json));
        var saved = store.Save(changes);
        var reopened = new SettingsConfigStore(_path).Open();
        Assert.Empty(reopened.SettingsBlockingErrors);
        var actual = SettingsFieldCases.Serialize(reopened.Config);
        foreach (var field in SettingsFieldCases.All) {
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(field.Json), SettingsFieldCases.At(actual, field.Path)), field.Path);
        }
        Assert.Equal("custom", saved.Theme);
        var text = File.ReadAllText(_path);
        Assert.Contains("\"$schema\" : \"preserve-schema\"", text);
        Assert.Contains("\"configVersion\" : 7", text);
        Assert.Contains("\"extension\" : {\"exact\":[1.0,  2e0,], /* user's unknown */ \"flag\":true}", text);
        Assert.Contains("// retain root note", text);
        Assert.True(File.ReadAllBytes(_path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal(original, File.ReadAllBytes(_path + ".settings.bak"));
    }

    [Theory]
    [InlineData("macros.globalHotKey")]
    [InlineData("appScope.chordKey")]
    [InlineData("modes.uniformGrid.chordKey")]
    public void NullableFieldsRoundTripWithoutReplacingOtherSections(string path) {
        var store = new SettingsConfigStore(_path);
        store.Open();
        store.Save(new Dictionary<string, JsonNode?> { [path] = null });
        var actual = SettingsFieldCases.Serialize(new SettingsConfigStore(_path).Open().Config);
        Assert.Null(SettingsFieldCases.At(actual, path));
    }

    [Fact]
    public void OrderedAxesAndSlotsAddMoveRemoveKeepOriginalScalarTextAndOrphanComments() {
        File.WriteAllText(_path, """
        {"configVersion":7,"horizontalKeys":["a",/* axis */"s","d"],"macros":{"slotKeys":["F1",/* slot */"F2","F3"]},
        "actionBindings":{"OemOpenBrackets":/* member */"LeftClick","Z":"MoveOnly"}}
        """);
        var store = new SettingsConfigStore(_path);
        store.Open();
        store.Save(new Dictionary<string, JsonNode?> {
            ["horizontalKeys"] = new JsonArray("D", "A", "F"),
            ["macros.slotKeys"] = new JsonArray("F3", "F1", "F4"),
            ["actionBindings"] = new JsonObject { ["Z"] = "RightClick", ["X"] = "DoubleClick" },
        });
        var text = File.ReadAllText(_path);
        Assert.Contains("\"a\"", text);
        Assert.Contains("/* axis */", text);
        Assert.Contains("/* slot */", text);
        Assert.Contains("/* member */", text);
        var reopened = new SettingsConfigStore(_path).Open();
        Assert.Empty(reopened.SettingsBlockingErrors);
        Assert.Contains(reopened.SettingsWarnings, warning => warning.Contains("missing slots", StringComparison.Ordinal));
        Assert.Equal(["D", "A", "F"], reopened.Config.HorizontalKeys.Select(key => key.ToString()));
        Assert.Equal(["F3", "F1", "F4"], reopened.Config.Macros.SlotKeys.Select(key => key.ToString()));
    }

    public void Dispose() {
        foreach (var file in Directory.GetFiles(_root)) { File.Delete(file); }
        Directory.Delete(_root);
    }
}
