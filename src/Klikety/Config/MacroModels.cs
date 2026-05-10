using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klikety.Config;

/// <summary>
/// Action types recordable in a macro step. Mirrors <see cref="MouseAction"/> plus Scroll.
/// </summary>
public enum MacroActionType {
    LeftClick,
    RightClick,
    MiddleClick,
    DoubleClick,
    MoveOnly,
    DragDrop,
    Scroll,
}

/// <summary>
/// Position mode for a macro recording. Absolute uses screen coordinates;
/// WindowRelative uses offsets from the target window's top-left corner.
/// </summary>
public enum MacroPositionMode {
    Absolute = 0,
    WindowRelative = 1,
}

/// <summary>
/// A single recorded action within a macro.
/// </summary>
public sealed class MacroStep {
    public MacroActionType ActionType { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public ActionModifiers Modifiers { get; init; }
    public int RelativeTimeMs { get; init; }
    public int? EndX { get; init; }
    public int? EndY { get; init; }
    public int? ScrollDelta { get; init; }
    public MouseAction? DragButton { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool StartFromCursor { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// A named macro recording with screen context and ordered steps.
/// Slot is derived from array index in <see cref="MacrosFile.Macros"/> — no Slot property.
/// </summary>
public sealed class MacroDefinition {
    public string Name { get; set; } = string.Empty;
    public int ScreenWidth { get; init; }
    public int ScreenHeight { get; init; }
    public double DpiScale { get; init; }
    public double SpeedModifier { get; init; } = 1.0;
    public MacroPositionMode PositionMode { get; init; }
    public int WindowWidth { get; init; }
    public int WindowHeight { get; init; }
    public string WindowTitlePattern { get; init; } = string.Empty;
    public List<MacroStep> Steps { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Root model for macros.json. Contains version and 10-slot macro array.
/// Array index = slot number. Null = empty slot.
/// </summary>
public sealed class MacrosFile {
    public int Version { get; init; } = 2;
    public MacroDefinition?[] Macros { get; init; } = new MacroDefinition?[10];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
