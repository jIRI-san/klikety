using System.Text.Json;
using System.Text.Json.Serialization;

using Klikety.Config;

namespace Klikety.Tests;

public class MacroModelTests {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public void RoundTrip_ClickStep_PreservesAllFields() {
        var step = new MacroStep {
            ActionType = MacroActionType.LeftClick,
            X = 100, Y = 200,
            Modifiers = ActionModifiers.Ctrl | ActionModifiers.Shift,
            RelativeTimeMs = 500,
        };
        var json = JsonSerializer.Serialize(step, JsonOptions);
        var result = JsonSerializer.Deserialize<MacroStep>(json, JsonOptions)!;

        Assert.Equal(MacroActionType.LeftClick, result.ActionType);
        Assert.Equal(100, result.X);
        Assert.Equal(200, result.Y);
        Assert.Equal(ActionModifiers.Ctrl | ActionModifiers.Shift, result.Modifiers);
        Assert.Equal(500, result.RelativeTimeMs);
        Assert.Null(result.EndX);
        Assert.Null(result.EndY);
        Assert.Null(result.ScrollDelta);
        Assert.Null(result.DragButton);
    }

    [Fact]
    public void RoundTrip_DragStep_PreservesDragFields() {
        var step = new MacroStep {
            ActionType = MacroActionType.DragDrop,
            X = 10, Y = 20,
            EndX = 300, EndY = 400,
            DragButton = MouseAction.RightClick,
            Modifiers = ActionModifiers.Alt,
            RelativeTimeMs = 100,
        };
        var json = JsonSerializer.Serialize(step, JsonOptions);
        var result = JsonSerializer.Deserialize<MacroStep>(json, JsonOptions)!;

        Assert.Equal(MacroActionType.DragDrop, result.ActionType);
        Assert.Equal(300, result.EndX);
        Assert.Equal(400, result.EndY);
        Assert.Equal(MouseAction.RightClick, result.DragButton);
        Assert.Equal(ActionModifiers.Alt, result.Modifiers);
    }

    [Fact]
    public void RoundTrip_ScrollStep_PreservesScrollDelta() {
        var step = new MacroStep {
            ActionType = MacroActionType.Scroll,
            X = 50, Y = 60,
            ScrollDelta = -120,
            Modifiers = ActionModifiers.Ctrl,
            RelativeTimeMs = 250,
        };
        var json = JsonSerializer.Serialize(step, JsonOptions);
        var result = JsonSerializer.Deserialize<MacroStep>(json, JsonOptions)!;

        Assert.Equal(MacroActionType.Scroll, result.ActionType);
        Assert.Equal(-120, result.ScrollDelta);
        Assert.Equal(ActionModifiers.Ctrl, result.Modifiers);
    }

    [Fact]
    public void RoundTrip_MacroDefinition_PreservesAllFields() {
        var def = new MacroDefinition {
            Name = "Test Macro",
            ScreenWidth = 1920,
            ScreenHeight = 1080,
            DpiScale = 1.5,
            Steps = {
                new MacroStep { ActionType = MacroActionType.LeftClick, X = 10, Y = 20 },
                new MacroStep { ActionType = MacroActionType.RightClick, X = 30, Y = 40, RelativeTimeMs = 200 },
            },
        };
        var json = JsonSerializer.Serialize(def, JsonOptions);
        var result = JsonSerializer.Deserialize<MacroDefinition>(json, JsonOptions)!;

        Assert.Equal("Test Macro", result.Name);
        Assert.Equal(1920, result.ScreenWidth);
        Assert.Equal(1080, result.ScreenHeight);
        Assert.Equal(1.5, result.DpiScale);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(MacroActionType.RightClick, result.Steps[1].ActionType);
        Assert.Equal(200, result.Steps[1].RelativeTimeMs);
    }

    [Fact]
    public void RoundTrip_MacrosFile_PreservesNullSlots() {
        var file = new MacrosFile {
            Macros = new MacroDefinition?[10],
        };
        file.Macros[3] = new MacroDefinition {
            Name = "Slot 3",
            ScreenWidth = 1920, ScreenHeight = 1080, DpiScale = 1.0,
        };
        var json = JsonSerializer.Serialize(file, JsonOptions);
        var result = JsonSerializer.Deserialize<MacrosFile>(json, JsonOptions)!;

        Assert.Equal(2, result.Version);
        Assert.Equal(10, result.Macros.Length);
        Assert.Null(result.Macros[0]);
        Assert.NotNull(result.Macros[3]);
        Assert.Equal("Slot 3", result.Macros[3]!.Name);
        Assert.Null(result.Macros[9]);
    }

    [Fact]
    public void RoundTrip_UnknownFields_PreservedOnMacroStep() {
        var json = """
        {
            "actionType": "leftClick",
            "x": 10, "y": 20,
            "modifiers": "none",
            "relativeTimeMs": 0,
            "futureField": "preserved"
        }
        """;
        var step = JsonSerializer.Deserialize<MacroStep>(json, JsonOptions)!;
        Assert.NotNull(step.ExtensionData);
        Assert.True(step.ExtensionData!.ContainsKey("futureField"));

        var reJson = JsonSerializer.Serialize(step, JsonOptions);
        Assert.Contains("futureField", reJson);
        Assert.Contains("preserved", reJson);
    }

    [Fact]
    public void RoundTrip_UnknownFields_PreservedOnMacroDefinition() {
        var json = """
        {
            "name": "Test",
            "screenWidth": 1920, "screenHeight": 1080,
            "dpiScale": 1.0,
            "steps": [],
            "newProperty": 42
        }
        """;
        var def = JsonSerializer.Deserialize<MacroDefinition>(json, JsonOptions)!;
        Assert.NotNull(def.ExtensionData);
        Assert.True(def.ExtensionData!.ContainsKey("newProperty"));

        var reJson = JsonSerializer.Serialize(def, JsonOptions);
        Assert.Contains("newProperty", reJson);
    }

    [Fact]
    public void RoundTrip_UnknownFields_PreservedOnMacrosFile() {
        var json = """
        {
            "version": 1,
            "macros": [null, null, null, null, null, null, null, null, null, null],
            "metadata": "extra"
        }
        """;
        var file = JsonSerializer.Deserialize<MacrosFile>(json, JsonOptions)!;
        Assert.NotNull(file.ExtensionData);
        Assert.True(file.ExtensionData!.ContainsKey("metadata"));

        var reJson = JsonSerializer.Serialize(file, JsonOptions);
        Assert.Contains("metadata", reJson);
    }

    [Fact]
    public void Deserialize_ArrayShorterThan10_ProducesShortArray() {
        var json = """
        {
            "version": 1,
            "macros": [null, null, null]
        }
        """;
        var file = JsonSerializer.Deserialize<MacrosFile>(json, JsonOptions)!;
        Assert.Equal(3, file.Macros.Length);
    }

    [Fact]
    public void Deserialize_ArrayLongerThan10_PreservesFullArray() {
        var macros = string.Join(", ", Enumerable.Repeat("null", 15));
        var json = $$"""
        {
            "version": 1,
            "macros": [{{macros}}]
        }
        """;
        var file = JsonSerializer.Deserialize<MacrosFile>(json, JsonOptions)!;
        Assert.Equal(15, file.Macros.Length);
    }

    [Fact]
    public void RoundTrip_AllActionTypes_SerializeCorrectly() {
        foreach (var actionType in Enum.GetValues<MacroActionType>()) {
            var step = new MacroStep { ActionType = actionType, X = 1, Y = 2 };
            var json = JsonSerializer.Serialize(step, JsonOptions);
            var result = JsonSerializer.Deserialize<MacroStep>(json, JsonOptions)!;
            Assert.Equal(actionType, result.ActionType);
        }
    }

    [Fact]
    public void MacroActionType_CoversAllMouseActions() {
        // Every MouseAction value should have a corresponding MacroActionType
        foreach (var mouseAction in Enum.GetValues<MouseAction>()) {
            var name = mouseAction.ToString();
            Assert.True(
                Enum.TryParse<MacroActionType>(name, out _),
                $"MouseAction.{name} has no corresponding MacroActionType");
        }
    }

    [Fact]
    public void RoundTrip_AllModifierCombinations_Preserved() {
        var all = ActionModifiers.Shift | ActionModifiers.Ctrl | ActionModifiers.Alt;
        var step = new MacroStep {
            ActionType = MacroActionType.LeftClick,
            X = 1, Y = 2,
            Modifiers = all,
        };
        var json = JsonSerializer.Serialize(step, JsonOptions);
        var result = JsonSerializer.Deserialize<MacroStep>(json, JsonOptions)!;
        Assert.Equal(all, result.Modifiers);
    }
}
