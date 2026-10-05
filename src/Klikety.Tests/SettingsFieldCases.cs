using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Klikety.Config;

namespace Klikety.Tests;

internal static class SettingsFieldCases {
    internal sealed record Field(int Page, string Path, string Json);
    public static readonly Field[] All = [
        new(0, "hotKey.modifiers", "\"Alt, Control, Shift\""), new(0, "hotKey.key", "\"F11\""),
        new(0, "logLevel", "\"Information\""), new(0, "fileLoggingEnabled", "true"), new(0, "retainedLogFileCount", "3"),
        new(1, "modes.uniformGrid.enabled", "true"), new(1, "modes.uniformGrid.default", "false"),
        new(1, "modes.uniformGrid.arrowKeys", "true"), new(1, "modes.uniformGrid.twoKey", "false"),
        new(1, "modes.uniformGrid.chordKey", "\"OemPlus\""),
        new(1, "modes.uniformGrid.logBaseSize", "11"), new(1, "modes.uniformGrid.logGridBaseSize", "12"),
        new(1, "modes.crosshair.enabled", "true"), new(1, "modes.crosshair.default", "true"),
        new(1, "modes.crosshair.arrowKeys", "false"), new(1, "modes.crosshair.twoKey", "true"),
        new(1, "modes.crosshair.chordKey", "null"),
        new(1, "modes.crosshair.logBaseSize", "13"), new(1, "modes.crosshair.logGridBaseSize", "14"),
        new(1, "modes.logCrosshair.enabled", "false"), new(1, "modes.logCrosshair.default", "false"),
        new(1, "modes.logCrosshair.arrowKeys", "false"), new(1, "modes.logCrosshair.twoKey", "false"),
        new(1, "modes.logCrosshair.chordKey", "null"),
        new(1, "modes.logCrosshair.logBaseSize", "15"), new(1, "modes.logCrosshair.logGridBaseSize", "16"),
        new(1, "modes.logGrid.enabled", "false"), new(1, "modes.logGrid.default", "false"),
        new(1, "modes.logGrid.arrowKeys", "false"), new(1, "modes.logGrid.twoKey", "false"),
        new(1, "modes.logGrid.chordKey", "null"),
        new(1, "modes.logGrid.logBaseSize", "17"), new(1, "modes.logGrid.logGridBaseSize", "18"),
        new(1, "appScope.chordKey", "null"), new(1, "level3CellSizeThreshold", "400"),
        new(2, "actionBindings", "{\"Z\":\"LeftClick\",\"X\":\"RightClick\",\"C\":\"DoubleClick\",\"V\":\"MiddleClick\",\"B\":\"MoveOnly\",\"OemOpenBrackets\":\"DragDrop\"}"),
        new(2, "horizontalKeys", "[\"OemSemicolon\",\"L\",\"K\",\"J\",\"H\",\"G\",\"F\",\"D\",\"S\",\"A\"]"),
        new(2, "verticalKeys", "[\"P\",\"O\",\"I\",\"U\",\"Y\",\"T\",\"R\",\"E\",\"W\",\"Q\"]"),
        new(3, "theme", "\"custom\""), new(3, "minLabelFontSize", "19.5"),
        new(4, "scrollHotkeys.enabled", "true"), new(4, "scrollHotkeys.scrollAmount", "7"),
        new(4, "scrollHotkeys.scrollUpKey.modifiers", "\"Control, Shift\""), new(4, "scrollHotkeys.scrollUpKey.key", "\"Next\""),
        new(4, "scrollHotkeys.scrollDownKey.modifiers", "\"Control, Shift\""), new(4, "scrollHotkeys.scrollDownKey.key", "\"Prior\""),
        new(5, "macros.enabled", "true"), new(5, "macros.globalHotKey.modifiers", "\"Alt, Control, Shift\""),
        new(5, "macros.globalHotKey.key", "\"Pause\""), new(5, "macros.recordKey", "\"OemQuestion\""),
        new(5, "macros.helperKey", "\"OemTilde\""), new(5, "macros.slotKeys", "[\"F10\",\"F9\",\"F8\",\"F7\",\"F6\",\"F5\",\"F4\",\"F3\",\"F2\",\"F1\"]"),
        new(5, "macros.speedModifier", "0"),
        new(5, "macros.playbackIndicator.fillColor", "\"#11ABCDEF\""), new(5, "macros.playbackIndicator.strokeColor", "\"#AA123456\""),
        new(5, "macros.playbackIndicator.strokeThickness", "0"), new(5, "macros.playbackIndicator.initialRadius", "25"),
        new(5, "macros.playbackIndicator.finalRadius", "5"), new(5, "macros.playbackIndicator.animationDurationMs", "100"),
        new(6, "keyPressVisualization.fontSize", "55"), new(6, "keyPressVisualization.fontColor", "\"#ABCDEF\""),
        new(6, "keyPressVisualization.outlineColor", "\"#654321\""), new(6, "keyPressVisualization.outlineThickness", "1.5"),
        new(6, "keyPressVisualization.corner", "\"TopLeft\""), new(6, "keyPressVisualization.fadeTimeoutMs", "1200"),
        new(6, "keyPressVisualization.fadeDurationMs", "300"), new(6, "keyPressVisualization.maxVisibleKeys", "5"),
        new(6, "keyPressVisualization.margin", "12"), new(6, "keyPressVisualization.repeatWindowMs", "150"),
    ];

    private static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
    public static JsonObject Serialize(ConfigModel config) => JsonSerializer.SerializeToNode(config, Options)!.AsObject();

    public static JsonNode? At(JsonNode root, string path) {
        JsonNode? value = root;
        foreach (var part in path.Split('.')) { value = value![part]; }
        return value;
    }
}
