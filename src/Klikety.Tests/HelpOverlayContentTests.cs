using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Overlay;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class HelpBindingModelTests {
    [Fact]
    public void Build_UsesEffectiveActionsAndConfiguredDisplayBindings() {
        var config = new ConfigModel {
            ActionBindings = new Dictionary<string, MouseAction> {
                ["Space"] = MouseAction.RightClick,
                ["OemTilde"] = MouseAction.DoubleClick,
            },
            HelpBinding = new HelpBindingConfig { Key = VKey.OemQuestion },
        };
        var content = Build(config, displayNumbers: new Dictionary<string, int> {
            ["display-a"] = 1,
            ["display-b"] = 2,
        });

        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.Space && entry.Command == "Right click");
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.OemTilde && entry.Command == "Double click");
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.D1 && entry.Command == "Current 1");
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.D2 && entry.Command == "Switch 2");
        Assert.Contains(content.Entries, entry => entry.Key == VKey.Escape && entry.Command == "Close");
        Assert.Contains(VKey.A, content.NavigationAnchors);
    }

    [Fact]
    public void Build_MutesLockedOrUnavailableCommandsAndReportsMacroAndDragContext() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true },
                Crosshair = new ModeConfig { Enabled = true, ChordKey = VKey.N, TwoKey = true },
                LogGrid = new ModeConfig { Enabled = true, ChordKey = VKey.OemComma, TwoKey = true },
            },
            ActionBindings = new Dictionary<string, MouseAction> {
                ["X"] = MouseAction.LeftClick,
                ["C"] = MouseAction.RightClick,
                ["V"] = MouseAction.MiddleClick,
                ["B"] = MouseAction.DoubleClick,
                ["Z"] = MouseAction.MoveOnly,
                ["A"] = MouseAction.DragDrop,
            },
            Macros = new MacrosConfig {
                Enabled = true,
                SlotKeys = [VKey.F1, VKey.F2],
            },
        };
        var slots = new MacroDefinition?[] {
            new() { Name = "Quarterly review draft", Steps = [] },
            null,
        };
        var content = HelpOverlayContentBuilder.Build(
            config,
            new FakeKeyLabelResolver(),
            logGridAvailable: false,
            isDragMode: true,
            MacroState.Recording,
            MacroRecorderState.AwaitOverwrite,
            selectedMacroSlot: 0,
            recordedStepCount: 1,
            slots,
            new Dictionary<string, int>(),
            activeDisplayPath: null,
            macroPickerAvailable: true,
            isModeLocked: true,
            appScoped: false);

        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.N && !entry.IsAvailable && entry.UnavailableReason!.Contains("locked"));
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.OemComma && !entry.IsAvailable && entry.UnavailableReason!.Contains("unavailable"));
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.F1 && entry.Command == "Play 0");
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.F2 && entry.Command.Contains("Empty"));
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.Y && entry.Command == "Yes");
        Assert.Contains(content.Prompts, prompt => prompt.Contains("Overwrite slot 0"));
        Assert.Contains(content.Prompts, prompt => prompt.Contains("Quarterly review draft"));
        Assert.Contains(content.Prompts, prompt => prompt.Contains("Drag target"));
        Assert.Contains(content.Prompts, prompt => prompt.Contains("recording clock continues"));
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.X && entry.Command.Contains("Drag: left"));
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.C && entry.Command.Contains("Drag: right"));
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.V && entry.Command.Contains("Drag: middle"));
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.B && entry.Command.Contains("Drag: double"));
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.Z && entry.Command == "Invalid drag");
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.A && entry.Command == "Invalid drag");
    }

    [Fact]
    public void Build_ExplainsRecordingSlotAndStartFromCursorPrompts() {
        var config = new ConfigModel {
            Macros = new MacrosConfig {
                Enabled = true,
                SlotKeys = [VKey.F1, VKey.F2],
            },
        };
        var slots = new MacroDefinition?[] {
            new() { Name = "Saved macro", Steps = [] },
            null,
        };

        var awaitSlot = HelpOverlayContentBuilder.Build(
            config,
            new FakeKeyLabelResolver(),
            logGridAvailable: true,
            isDragMode: false,
            MacroState.Recording,
            MacroRecorderState.AwaitSlot,
            selectedMacroSlot: -1,
            recordedStepCount: 0,
            slots,
            new Dictionary<string, int>(),
            activeDisplayPath: null,
            macroPickerAvailable: true,
            isModeLocked: false,
            appScoped: false);

        Assert.Contains(awaitSlot.Entries, entry => entry.Key == VKey.F1 && entry.Command == "Select 0");
        Assert.Contains(awaitSlot.Entries, entry => entry.Key == VKey.F2 && entry.Command == "Record 1");
        Assert.Contains(awaitSlot.Prompts, prompt => prompt.Contains("choose a slot"));
        Assert.Contains(awaitSlot.Prompts, prompt => prompt.Contains("recording clock continues"));

        var awaitCursor = HelpOverlayContentBuilder.Build(
            config,
            new FakeKeyLabelResolver(),
            logGridAvailable: true,
            isDragMode: false,
            MacroState.Recording,
            MacroRecorderState.AwaitStartFromCursorConfirm,
            selectedMacroSlot: 1,
            recordedStepCount: 1,
            slots,
            new Dictionary<string, int>(),
            activeDisplayPath: null,
            macroPickerAvailable: true,
            isModeLocked: false,
            appScoped: false);

        Assert.Contains(awaitCursor.Entries, entry => entry.Key == VKey.Y && entry.Command == "Cursor start");
        Assert.Contains(awaitCursor.Entries, entry => entry.Key == VKey.N && entry.Command == "Recorded start");
        Assert.Contains(awaitCursor.Prompts, prompt => prompt.Contains("Drag from cursor"));
        Assert.Contains(awaitCursor.Prompts, prompt => prompt.Contains("recording clock continues"));
    }

    [Fact]
    public void Build_OmitsDisabledModesAndMacros() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true },
                Crosshair = new ModeConfig { Enabled = false, ChordKey = VKey.N },
            },
            Macros = new MacrosConfig { Enabled = false },
            HelpBinding = new HelpBindingConfig { Enabled = false },
        };
        var content = Build(config);

        Assert.DoesNotContain(content.Entries, entry => entry.Key == VKey.N);
        Assert.DoesNotContain(content.Entries, entry => entry.Key == config.Macros.RecordKey);
        Assert.DoesNotContain(content.Entries, entry => entry.Command == "Help");
        Assert.Contains(content.Entries, entry => entry.Key == VKey.Space && entry.Command == "Left click");
        Assert.Equal("Press Escape to close help.", content.CloseInstruction);
    }

    [Fact]
    public void Build_OmitsInvalidHelpBindingFromEffectiveCommands() {
        var config = new ConfigModel {
            ActionBindings = new Dictionary<string, MouseAction> {
                ["OemQuestion"] = MouseAction.LeftClick,
            },
            HelpBinding = new HelpBindingConfig { Key = VKey.OemQuestion },
        };
        var content = HelpOverlayContentBuilder.Build(
            config,
            new FakeKeyLabelResolver(),
            logGridAvailable: true,
            isDragMode: false,
            MacroState.Idle,
            recorderState: null,
            selectedMacroSlot: -1,
            recordedStepCount: 0,
            macroSlots: [],
            displayNumbers: new Dictionary<string, int>(),
            activeDisplayPath: null,
            macroPickerAvailable: true,
            isModeLocked: false,
            appScoped: false,
            helpBindingValid: false);

        Assert.DoesNotContain(content.Entries, entry =>
            entry.Key == VKey.OemQuestion && entry.Command == "Help");
        Assert.Equal("Press Escape to close help.", content.CloseInstruction);
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.OemQuestion && entry.Command == "Left click");
    }

    private static HelpOverlayContent Build(
        ConfigModel config,
        IReadOnlyDictionary<string, int>? displayNumbers = null) =>
        HelpOverlayContentBuilder.Build(
            config,
            new FakeKeyLabelResolver(),
            logGridAvailable: true,
            isDragMode: false,
            MacroState.Idle,
            recorderState: null,
            selectedMacroSlot: -1,
            recordedStepCount: 0,
            macroSlots: [],
            displayNumbers ?? new Dictionary<string, int>(),
            activeDisplayPath: "display-a",
            macroPickerAvailable: true,
            isModeLocked: false,
            appScoped: false);
}
