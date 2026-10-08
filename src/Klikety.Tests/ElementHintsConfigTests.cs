using System.IO;
using System.Text.Json.Nodes;

using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public class ElementHintsConfigTests {
    [Fact]
    public void DisabledArrowsKeepAdaptiveLabelsAndPagingAvailable() {
        var result = ConfigLoader.ReadSettings("""
            {"configVersion":9,"modes":{"elementHints":{"enabled":true,"twoKey":true,"arrowKeys":false,"chordKey":"Tab"}}}
            """);
        Assert.Empty(result.SettingsBlockingErrors);
        Assert.False(result.Config.Modes.ElementHints.ArrowKeys);
        Assert.True(new ModeSessionFactory(result.Config, new ActionMapper([]), null).IsElementHintsAvailable);
    }

    [Theory]
    [InlineData("horizontalKeys", "[\"Prior\"]")]
    [InlineData("verticalKeys", "[\"Next\"]")]
    [InlineData("actionBindings", "{\"Prior\":\"MoveOnly\"}")]
    [InlineData("helpBinding.key", "\"Next\"")]
    [InlineData("modes.elementHints.chordKey", "\"Prior\"")]
    [InlineData("modes.crosshair.chordKey", "\"Next\"")]
    [InlineData("appScope.chordKey", "\"Prior\"")]
    [InlineData("macros.recordKey", "\"Next\"")]
    [InlineData("macros.helperKey", "\"Prior\"")]
    [InlineData("macros.slotKeys", "[\"Next\"]")]
    public void PagingKeyConflictsBlockSaveWithoutRebinding(string path, string json) {
        var root = SettingsFieldCases.Serialize(new ConfigModel { ConfigVersion = ConfigMigrator.CurrentConfigVersion });
        SettingsValidationTests.Set(root, "modes.elementHints.enabled", JsonValue.Create(true));
        SettingsValidationTests.Set(root, path, JsonNode.Parse(json));
        var result = ConfigLoader.ReadSettings(root.ToJsonString(), [path]);
        Assert.NotEmpty(result.SettingsBlockingErrors);
        Assert.NotNull(ElementHintsPolicy.GetInvalidReason(result.Config));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), SettingsFieldCases.At(SettingsFieldCases.Serialize(result.Config), path)));
    }
    [Fact]
    public void SettingsAcceptDefaultHintsWithImplicitEnterGridFallback() {
        var result = ConfigLoader.ReadSettings("""
            {"configVersion":9,"modes":{
              "uniformGrid":{"enabled":true,"default":false,"twoKey":true,"arrowKeys":true},
              "elementHints":{"enabled":true,"default":true,"twoKey":true,"arrowKeys":true}
            }}
            """);
        Assert.Empty(result.Violations);
        var config = result.Config;
        Assert.True(config.Modes.ElementHints.Default);
        Assert.True(config.Modes.UniformGrid.Enabled);
        Assert.False(config.Modes.UniformGrid.Default);
    }

    [Fact]
    public void SettingsRejectExplicitNullHintMode() {
        Assert.Throws<InvalidDataException>(() =>
            ConfigLoader.ReadSettings("""{"configVersion":9,"modes":{"elementHints":null}}"""));
    }

    [Fact]
    public void MigrationAddsDisabledModePreservesBindingsAndUnknownFieldsAndIsIdempotent() {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            File.WriteAllText(path, """{"configVersion":8,"modes":{},"actionBindings":{"Tab":"RightClick"},"unknown":42}""");
            Assert.True(ConfigMigrator.MigrateIfNeeded(path).WasMigrated);
            var node = JsonNode.Parse(File.ReadAllText(path))!;
            Assert.Equal(9, node["configVersion"]!.GetValue<int>());
            Assert.False(node["modes"]!["elementHints"]!["enabled"]!.GetValue<bool>());
            Assert.Equal(1500, node["modes"]!["elementHints"]!["discoveryTimeoutMs"]!.GetValue<int>());
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
            Assert.Equal(1500, result.Config.Modes.ElementHints.DiscoveryTimeoutMs);
            Assert.True(new ModeSessionFactory(result.Config, new ActionMapper([]), null).IsElementHintsAvailable);
            settings[twoKey] = false;
            File.WriteAllText(path, document.ToJsonString());
            result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("twoKey must be true"));
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

    [Theory]
    [InlineData(100)]
    [InlineData(1500)]
    [InlineData(10000)]
    [InlineData(60000)]
    public void DiscoveryTimeoutIsPreservedByRuntimeAndSettingsWithoutMigratingVersionNine(int timeout) {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        string json = $$"""
            {"configVersion":9,"modes":{"elementHints":{"enabled":true,"twoKey":true,"arrowKeys":true,
                "chordKey":"Tab","discoveryTimeoutMs":{{timeout}}
            }
            }
            }
            """;
        try {
            File.WriteAllText(path, json);
            var runtime = ConfigLoader.Load(path);
            var settings = ConfigLoader.ReadSettings(json);
            Assert.Empty(runtime.Violations);
            Assert.Empty(settings.SettingsBlockingErrors);
            Assert.Equal(timeout, runtime.Config.Modes.ElementHints.DiscoveryTimeoutMs);
            Assert.Equal(timeout, settings.Config.Modes.ElementHints.DiscoveryTimeoutMs);
            Assert.Equal(json, File.ReadAllText(path));
            Assert.False(File.Exists(path + ".bak"));
        } finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(60001)]
    [InlineData(int.MaxValue)]
    public void InvalidDiscoveryTimeoutIsReportedNotClampedEvenWhenHintsAreDisabled(int timeout) {
        foreach (bool enabled in new[] { false, true }) {
            string json = $$"""
                {"configVersion":9,"modes":{"elementHints":{"enabled":{{enabled.ToString().ToLowerInvariant()}},
                    "twoKey":true,"arrowKeys":true,"chordKey":"Tab","discoveryTimeoutMs":{{timeout}}
                }
                }
                }
                """;
            var settings = ConfigLoader.ReadSettings(json);
            Assert.Contains(settings.SettingsBlockingErrors, error => error.Contains("discoveryTimeoutMs"));
            Assert.Equal(timeout, settings.Config.Modes.ElementHints.DiscoveryTimeoutMs);
            Assert.False(new ModeSessionFactory(settings.Config, new ActionMapper([]), null).IsElementHintsAvailable);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            try {
                File.WriteAllText(path, json);
                var runtime = ConfigLoader.Load(path);
                Assert.Contains(runtime.Violations, error => error.Contains("discoveryTimeoutMs"));
                Assert.Equal(timeout, runtime.Config.Modes.ElementHints.DiscoveryTimeoutMs);
                Assert.False(new ModeSessionFactory(runtime.Config, new ActionMapper([]), null).IsElementHintsAvailable);
            } finally { File.Delete(path); File.Delete(path + ".bak"); }
        }
    }
}
