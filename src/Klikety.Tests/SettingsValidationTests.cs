using System.Text.Json.Nodes;

using Klikety.Config;

namespace Klikety.Tests;

public sealed class SettingsValidationTests {
    [Theory]
    [InlineData("logLevel", "\"4\"", "logLevel")]
    [InlineData("logLevel", "\"verbose\"", "logLevel")]
    [InlineData("retainedLogFileCount", "0", "retainedLogFileCount")]
    [InlineData("minLabelFontSize", "-1", "label size")]
    [InlineData("level3CellSizeThreshold", "-1", "threshold")]
    [InlineData("hotKey.key", "9999", "hotKey.key")]
    [InlineData("helpBinding.key", "9999", "helpBinding.key")]
    [InlineData("hotKey.modifiers", "32", "hotKey.modifiers")]
    [InlineData("scrollHotkeys.scrollAmount", "0", "scrollAmount")]
    [InlineData("macros.speedModifier", "-0.1", "speedModifier")]
    [InlineData("macros.playbackIndicator.fillColor", "\"invalid\"", "fillColor")]
    [InlineData("macros.playbackIndicator.strokeColor", "\"invalid\"", "strokeColor")]
    [InlineData("macros.playbackIndicator.strokeThickness", "-1", "strokeThickness")]
    [InlineData("macros.playbackIndicator.initialRadius", "0.5", "initialRadius")]
    [InlineData("macros.playbackIndicator.finalRadius", "0", "finalRadius")]
    [InlineData("macros.playbackIndicator.animationDurationMs", "50", "animationDurationMs")]
    [InlineData("keyPressVisualization.fontSize", "0", "fontSize")]
    [InlineData("keyPressVisualization.fontColor", "\"invalid\"", "fontColor")]
    [InlineData("keyPressVisualization.outlineColor", "\"invalid\"", "outlineColor")]
    [InlineData("keyPressVisualization.outlineThickness", "-1", "outlineThickness")]
    [InlineData("keyPressVisualization.corner", "\"Middle\"", "corner")]
    [InlineData("keyPressVisualization.fadeTimeoutMs", "-1", "fadeTimeoutMs")]
    [InlineData("keyPressVisualization.fadeDurationMs", "-1", "fadeDurationMs")]
    [InlineData("keyPressVisualization.maxVisibleKeys", "0", "maxVisibleKeys")]
    [InlineData("keyPressVisualization.margin", "-1", "margin")]
    [InlineData("keyPressVisualization.repeatWindowMs", "-1", "repeatWindowMs")]
    [InlineData("modes.uniformGrid.logBaseSize", "1", "logBaseSize")]
    [InlineData("modes.crosshair.logGridBaseSize", "51", "logGridBaseSize")]
    public void InvalidEditedValuesHaveBlockingFieldErrors(string path, string value, string expected) {
        var root = SettingsFieldCases.Serialize(new ConfigModel { ConfigVersion = ConfigMigrator.CurrentConfigVersion });
        Set(root, path, JsonNode.Parse(value));
        var result = ConfigLoader.ReadSettings(root.ToJsonString(), [path]);
        Assert.Contains(result.SettingsBlockingErrors, error => error.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("uniformGrid", false, false)]
    [InlineData("crosshair", false, true)]
    [InlineData("logCrosshair", false, true)]
    [InlineData("logGrid", false, true)]
    public void ImpossibleEnabledModeCombinationsBlock(string mode, bool twoKey, bool arrows) {
        var root = SettingsFieldCases.Serialize(new ConfigModel { ConfigVersion = ConfigMigrator.CurrentConfigVersion });
        Set(root, $"modes.{mode}.twoKey", JsonValue.Create(twoKey));
        Set(root, $"modes.{mode}.arrowKeys", JsonValue.Create(arrows));
        Assert.NotEmpty(ConfigLoader.ReadSettings(root.ToJsonString()).SettingsBlockingErrors);
        Set(root, $"modes.{mode}.enabled", JsonValue.Create(false));
        if (mode == "uniformGrid") {
            Set(root, "modes.uniformGrid.default", JsonValue.Create(false));
            Set(root, "modes.crosshair.default", JsonValue.Create(true));
        }
        Assert.Empty(ConfigLoader.ReadSettings(root.ToJsonString()).SettingsBlockingErrors);
    }

    [Theory]
    [InlineData("horizontalKeys", "[\"Escape\"]")]
    [InlineData("verticalKeys", "[\"A\"]")]
    [InlineData("actionBindings", "{\"Escape\":\"LeftClick\"}")]
    [InlineData("actionBindings", "{\"A\":\"LeftClick\"}")]
    [InlineData("horizontalKeys", "[\"A\",\"A\"]")]
    [InlineData("modes.crosshair.chordKey", "\"M\"")]
    [InlineData("appScope.chordKey", "\"N\"")]
    [InlineData("macros.recordKey", "\"A\"")]
    [InlineData("macros.helperKey", "\"OemPipe\"")]
    [InlineData("macros.slotKeys", "[\"D1\"]")]
    [InlineData("macros.slotKeys", "[\"F1\",\"F1\"]")]
    [InlineData("helpBinding.key", "\"Escape\"")]
    [InlineData("helpBinding.key", "\"Space\"")]
    [InlineData("helpBinding.key", "\"A\"")]
    [InlineData("helpBinding.key", "\"N\"")]
    [InlineData("helpBinding.key", "\"OemPeriod\"")]
    [InlineData("helpBinding.key", "\"OemPipe\"")]
    [InlineData("helpBinding.key", "\"F1\"")]
    public void ReservedDuplicateAndCrossSubsystemKeysBlock(string path, string json) {
        var root = SettingsFieldCases.Serialize(new ConfigModel { ConfigVersion = ConfigMigrator.CurrentConfigVersion });
        Set(root, path, JsonNode.Parse(json));
        Assert.NotEmpty(ConfigLoader.ReadSettings(root.ToJsonString(), [path]).SettingsBlockingErrors);
    }

    [Fact]
    public void HelpBindingChecksUniformChordAndDisabledValuesWithoutNullSlotFailure() {
        var root = SettingsFieldCases.Serialize(new ConfigModel { ConfigVersion = ConfigMigrator.CurrentConfigVersion });
        Set(root, "modes.uniformGrid.default", JsonValue.Create(false));
        Set(root, "modes.crosshair.default", JsonValue.Create(true));
        Set(root, "modes.uniformGrid.chordKey", JsonValue.Create("OemQuestion"));
        Assert.Contains(ConfigLoader.ReadSettings(root.ToJsonString()).SettingsBlockingErrors,
            error => error.Contains("Help key", StringComparison.Ordinal) && error.Contains("mode chord", StringComparison.Ordinal));
        Set(root, "helpBinding.enabled", JsonValue.Create(false));
        Assert.Empty(ConfigLoader.ReadSettings(root.ToJsonString()).SettingsBlockingErrors);
        Set(root, "helpBinding.key", JsonValue.Create(9999));
        Assert.Contains(ConfigLoader.ReadSettings(root.ToJsonString()).SettingsBlockingErrors,
            error => error.Contains("helpBinding.key", StringComparison.Ordinal));
        Set(root, "helpBinding.enabled", JsonValue.Create(true));
        Set(root, "helpBinding.key", JsonValue.Create("OemCloseBrackets"));
        Set(root, "macros.slotKeys", null);
        Assert.Contains(ConfigLoader.ReadSettings(root.ToJsonString()).SettingsBlockingErrors,
            error => error.Contains("slotKeys", StringComparison.Ordinal));
    }

    [Fact]
    public void HelpBindingRejectsConfiguredActionsAndOverlappingGlobalHotkeys() {
        var root = SettingsFieldCases.Serialize(new ConfigModel {
            ConfigVersion = ConfigMigrator.CurrentConfigVersion,
            ActionBindings = new() { ["Z"] = MouseAction.LeftClick },
        });
        Set(root, "helpBinding.key", JsonValue.Create("Z"));
        Assert.Contains(ConfigLoader.ReadSettings(root.ToJsonString()).SettingsBlockingErrors,
            error => error.Contains("Help key", StringComparison.Ordinal) && error.Contains("action binding", StringComparison.Ordinal));
        Set(root, "helpBinding.key", JsonValue.Create("Tab"));
        Set(root, "hotKey.key", JsonValue.Create("Tab"));
        Set(root, "hotKey.modifiers", JsonValue.Create("None"));
        Assert.Contains(ConfigLoader.ReadSettings(root.ToJsonString()).SettingsBlockingErrors,
            error => error.Contains("Help key", StringComparison.Ordinal) && error.Contains("global hotkey", StringComparison.Ordinal));
        Set(root, "helpBinding.requireShift", JsonValue.Create(true));
        Assert.Empty(ConfigLoader.ReadSettings(root.ToJsonString()).SettingsBlockingErrors);
    }

    [Fact]
    public void DisabledMacrosStillRejectNullSlotsUnsafeVisualsAndNegativeSpeed() {
        var result = ConfigLoader.ReadSettings("""
        {"configVersion":8,"macros":{"enabled":false,"slotKeys":null,"speedModifier":-1,"playbackIndicator":{"strokeColor":"invalid"}}}
        """);
        Assert.Contains(result.SettingsBlockingErrors, error => error.Contains("slotKeys", StringComparison.Ordinal));
        Assert.Contains(result.SettingsBlockingErrors, error => error.Contains("speedModifier", StringComparison.Ordinal));
        Assert.Contains(result.SettingsBlockingErrors, error => error.Contains("strokeColor", StringComparison.Ordinal));
    }

    [Fact]
    public void ZeroSpeedAndLengthCompatibilityRemainValidWithExplicitWarnings() {
        var result = ConfigLoader.ReadSettings("""
        {"configVersion":8,"horizontalKeys":["A","S"],"macros":{"slotKeys":["F1"],"speedModifier":0}}
        """);
        Assert.Empty(result.SettingsBlockingErrors);
        Assert.Equal(0, result.Config.Macros.SpeedModifier);
        Assert.Contains(result.SettingsWarnings, warning => warning.Contains("missing slots", StringComparison.Ordinal));
        Assert.True(result.SettingsWarnings.Count >= 2);
    }

    internal static void Set(JsonObject root, string path, JsonNode? value) {
        var parts = path.Split('.');
        var parent = root;
        foreach (var part in parts[..^1]) { parent = parent[part]!.AsObject(); }
        parent[parts[^1]] = value;
    }
}
