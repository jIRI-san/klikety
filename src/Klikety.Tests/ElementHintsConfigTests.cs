using System.IO;
using System.Text.Json.Nodes;

using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public class ElementHintsConfigTests {
    [Fact]
    public void MigrationAddsDisabledModePreservesBindingsAndUnknownFieldsAndIsIdempotent() {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            File.WriteAllText(path, """{"configVersion":8,"modes":{},"actionBindings":{"Tab":"RightClick"},"unknown":42}""");
            Assert.True(ConfigMigrator.MigrateIfNeeded(path).WasMigrated);
            var node = JsonNode.Parse(File.ReadAllText(path))!;
            Assert.Equal(9, node["configVersion"]!.GetValue<int>());
            Assert.False(node["modes"]!["elementHints"]!["enabled"]!.GetValue<bool>());
            Assert.Equal("RightClick", node["actionBindings"]!["Tab"]!.GetValue<string>());
            Assert.Equal(42, node["unknown"]!.GetValue<int>());
            Assert.False(ConfigMigrator.MigrateIfNeeded(path).WasMigrated);
        } finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Theory]
    [InlineData("modes", "elementHints", "enabled", "twoKey")]
    [InlineData("modes", "ElementHints", "Enabled", "TwoKey")]
    public void PartialSettingsMergeDefaultsButExplicitFalseFailsClosed(string modes, string hints, string enabled, string twoKey) {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            var settings = new JsonObject { [enabled] = true };
            var document = new JsonObject { ["configVersion"] = 9, [modes] = new JsonObject { [hints] = settings } };
            File.WriteAllText(path, document.ToJsonString());
            var result = ConfigLoader.Load(path);
            Assert.Null(ElementHintsPolicy.GetInvalidReason(result.Config));
            Assert.True(new ModeSessionFactory(result.Config, new ActionMapper([]), null).IsElementHintsAvailable);
            settings[twoKey] = false;
            File.WriteAllText(path, document.ToJsonString());
            result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("twoKey and arrowKeys"));
            Assert.False(new ModeSessionFactory(result.Config, new ActionMapper([]), null).IsElementHintsAvailable);
        } finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Theory]
    [InlineData("""{"configVersion":9,"modes":[]}""", "could not be parsed")]
    [InlineData("""{"configVersion":9,"modes":{"elementHints":[]}}""", "could not be parsed")]
    [InlineData("""{"configVersion":9,"modes":{"elementHints":null}}""", "could not be parsed")]
    [InlineData("""{"configVersion":9,"modes":null}""", "could not be parsed")]
    [InlineData("""[]""", "Config file root is not a JSON object.")]
    public void MalformedShapesReportParseFailure(string json, string error) {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            File.WriteAllText(path, json);
            Assert.Contains(ConfigLoader.Load(path).Violations, v => v.Contains(error));
        } finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Theory]
    [InlineData(VKey.Space)]
    [InlineData(VKey.A)]
    [InlineData(VKey.N)]
    [InlineData(VKey.OemQuestion)]
    [InlineData(VKey.OemPipe)]
    [InlineData(VKey.D1)]
    [InlineData(VKey.F1)]
    public void ChordCollisionsDisableRuntimeDispatchWithoutRebinding(VKey chord) {
        var config = new ConfigModel {
            Modes = new() {
                ElementHints = new() {
                    Enabled = true, ChordKey = chord, TwoKey = true, ArrowKeys = true,
                }
            }
        };
        Assert.NotNull(ElementHintsPolicy.GetInvalidReason(config));
        Assert.False(new ModeSessionFactory(config, new ActionMapper([]), null).IsElementHintsAvailable);
        Assert.Equal(chord, config.Modes.ElementHints.ChordKey);
    }

    [Fact]
    public void EnabledModeRequiresGridFallback() {
        var config = new ConfigModel {
            Modes = new() {
                UniformGrid = new() { Enabled = false },
                ElementHints = new() { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            }
        };
        Assert.Contains("UniformGrid", ElementHintsPolicy.GetInvalidReason(config)!);
    }
}
