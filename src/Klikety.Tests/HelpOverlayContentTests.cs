using Klikety.Automation;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Overlay;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class HelpBindingModelTests {
    [Fact]
    public void SingleKeyGroupHelpMutesActionsAndPagingStaysIndependentOfArrows() {
        var config = new ConfigModel { Modes = new() { ElementHints = new() { Enabled = true, ArrowKeys = false, TwoKey = true } } };
        var content = Build(config, hints: new(HintOutcome.Success, "", 1, 3, 250, null, false,
            Depth: 2, SingleKey: true, FocusedGroup: true, ArrowKeys: false));
        Assert.Contains(content.Prompts, p => p.StartsWith("Level 2: one-key"));
        Assert.Contains(content.Prompts, p => p.StartsWith("Group focused."));
        Assert.Contains(content.Prompts, p => p.StartsWith("Label keys:"));
        Assert.DoesNotContain(content.Prompts, p => p.StartsWith("First label key:"));
        Assert.All(content.Entries.Where(e => e.Category == HelpEntryCategory.Action), e => Assert.False(e.IsAvailable));
        Assert.All(content.Entries.Where(e => e.Key is VKey.Left or VKey.Right or VKey.Up or VKey.Down),
            e => Assert.False(e.IsAvailable));
        Assert.Contains(content.Entries, e => e.Key == VKey.Next && e.KeyLabel == "PgDn" && e.IsAvailable);
        Assert.Contains(content.Entries, e => e.Key == VKey.Prior && e.KeyLabel == "PgUp" && e.IsAvailable);
        Assert.Contains(content.Entries, e => e.Key == VKey.Return && e.IsAvailable);
    }
    [Theory]
    [InlineData(MacroRecorderState.AwaitSlot)]
    [InlineData(MacroRecorderState.AwaitOverwrite)]
    [InlineData(MacroRecorderState.AwaitStartFromCursorConfirm)]
    public void Build_HintGuidanceRespectsMacroSetupPriority(MacroRecorderState recorderState) {
        var help = HelpOverlayContentBuilder.Build(new ConfigModel(), new FakeKeyLabelResolver(),
            true, false, MacroState.Recording, recorderState, 0, 1, [], new Dictionary<string, int>(),
            null, true, false, false,
            elementHints: new(HintOutcome.Success, "", 0, 2, 117, null, true));
        Assert.All(help.Entries.Where(entry => entry.Category is
            HelpEntryCategory.Action or HelpEntryCategory.Mode or HelpEntryCategory.Scope),
            entry => Assert.False(entry.IsAvailable));
        Assert.Contains(help.Prompts, prompt => prompt.Contains("Finish macro setup first"));
        Assert.Contains(help.Prompts, prompt => prompt.Contains("Escape cancels macro recording first"));
        Assert.DoesNotContain(help.Prompts, prompt => prompt.StartsWith("Target selected."));
        Assert.Contains("Escape to close help", help.CloseInstruction);
    }

    [Theory]
    [InlineData(null, "Finding controls...")]
    [InlineData(HintOutcome.NoTargets, "No controls found")]
    [InlineData(HintOutcome.Timeout, "Control discovery timed out")]
    [InlineData(HintOutcome.AccessDenied, "Application access denied")]
    [InlineData(HintOutcome.Unavailable, "Element hints unavailable")]
    [InlineData(HintOutcome.InvalidRoot, "Application unavailable")]
    [InlineData(HintOutcome.CleanupFailed, "Helper cleanup pending; reopen hints to retry")]
    [InlineData(HintOutcome.ProviderError, "Control discovery failed")]
    public void Build_HintLoadingAndFailuresMuteActionsButNeverLockGridFallback(HintOutcome? outcome, string status) {
        var content = Build(new ConfigModel(),
            hints: new(outcome, status, 0, 1, 0, null, false), locked: true);

        Assert.Contains(status, content.Prompts);
        Assert.All(content.Entries.Where(entry => entry.Category == HelpEntryCategory.Action), entry => {
            Assert.False(entry.IsAvailable);
            Assert.Equal("Select a hint first", entry.UnavailableReason);
        });
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.Return && entry.Command == "Grid fallback" && entry.IsAvailable);
        Assert.Contains(content.Entries, entry => entry.Key == VKey.Left && !entry.IsAvailable);
        Assert.Contains(content.Entries, entry => entry.Key == VKey.Right && !entry.IsAvailable);
        Assert.Contains(content.Prompts, prompt => prompt.Contains("Enter") && prompt.Contains("grid"));
        Assert.DoesNotContain(content.Prompts, string.IsNullOrWhiteSpace);
        Assert.DoesNotContain(content.Prompts, prompt => prompt.Contains("retained=") || prompt.Contains("visited="));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_HintsShowPagePrefixSelectionEffectiveActionsAndEscapeStages(bool selected) {
        var config = new ConfigModel {
            ActionBindings = new() {
                ["Space"] = MouseAction.RightClick, ["F11"] = MouseAction.MoveOnly, ["F10"] = MouseAction.DragDrop,
            },
            Modes = new() {
                UniformGrid = new() { Enabled = true, ChordKey = VKey.Back, TwoKey = true },
                ElementHints = new() { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
            Macros = new() { Enabled = false },
        };
        var content = Build(config,
            hints: new(HintOutcome.Partial, "Some controls unavailable", 2, 4, 117, selected ? null : 1, selected),
            locked: true);

        Assert.Contains(content.Prompts, prompt => prompt.StartsWith("Page 3 of 4."));
        Assert.Contains(content.Prompts, prompt => prompt.Contains("wrap pages") && prompt.Contains("clear"));
        Assert.Contains(content.Prompts, prompt => prompt.Contains("select without clicking"));
        Assert.Contains(content.Prompts, prompt => prompt.Contains("Ctrl, Alt or Shift"));
        Assert.Contains(content.Prompts, prompt => prompt.Contains("Move only ignores modifiers"));
        Assert.Contains(content.Prompts, prompt => prompt.Contains("Start drag") && prompt.Contains("destination"));
        Assert.Contains(content.Prompts, prompt => prompt.Contains("Outside help: Escape clears"));
        Assert.Contains(content.Prompts, prompt => prompt.Contains("at Level 1 it cancels immediately"));
        Assert.Contains(content.Prompts, prompt => prompt.Contains("Some controls unavailable"));
        Assert.Contains(content.Prompts, prompt =>
            selected ? prompt.Contains("Target selected") : prompt.Contains("First key S entered"));
        Assert.Contains(content.Entries, entry => entry.Key == VKey.Space &&
            entry.Command == "Right click" && entry.IsAvailable == selected);
        Assert.Contains(content.Entries, entry => entry.Key == VKey.F11 &&
            entry.Command == "Move only" && entry.IsAvailable == selected);
        Assert.Contains(content.Entries, entry => entry.Key == VKey.F10 &&
            entry.Command == "Start drag" && entry.IsAvailable == selected);
        Assert.Contains(content.Entries, entry => entry.Key == VKey.Back &&
            entry.Command == "Uniform grid" && !entry.IsAvailable);
        Assert.Contains(content.Entries, entry => entry.Key == VKey.Return && entry.IsAvailable);
        Assert.Contains(content.Entries, entry => entry.Key == VKey.Escape && entry.Command == "Close");
        Assert.Contains("Escape to close help", content.CloseInstruction);
        Assert.Contains("Other non-modifier keys close help and run normally", content.CloseInstruction);
    }

    [Fact]
    public void Build_SelectedHintInDragPhaseRequiresAClickDestinationAction() {
        var content = Build(new ConfigModel {
            ActionBindings = new() { ["X"] = MouseAction.MoveOnly, ["Z"] = MouseAction.DragDrop },
        }, hints: new(HintOutcome.Success, "", 0, 1, 1, null, true), drag: true);
        Assert.Contains(content.Entries, entry => entry.Key == VKey.Space &&
            entry.Command == "Drag: left" && entry.IsAvailable);
        Assert.Contains(content.Entries, entry => entry.Key == VKey.X &&
            entry.Command == "Invalid drag" && !entry.IsAvailable);
        Assert.Contains(content.Entries, entry => entry.Key == VKey.Z &&
            entry.Command == "Invalid drag" && !entry.IsAvailable);
    }

    [Fact]
    public void Build_GridModeHasNoHintSpecificPromptsAndShowsConfiguredGridChord() {
        var content = Build(new ConfigModel {
            Modes = new() { UniformGrid = new() { Enabled = true, ChordKey = VKey.Back, TwoKey = true } },
        });
        Assert.Contains(content.Entries, entry => entry.Key == VKey.Back &&
            entry.Command == "Uniform grid" && entry.IsAvailable);
        Assert.DoesNotContain(content.Prompts, prompt => prompt.Contains("label key") || prompt.StartsWith("Page "));
        Assert.DoesNotContain(content.Entries, entry => entry.Key == VKey.Return);
        Assert.All(content.Entries.Where(entry => entry.Category == HelpEntryCategory.Action),
            entry => Assert.True(entry.IsAvailable));
    }

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
        Assert.Equal("Press Escape to close help. Other non-modifier keys close help and run normally.", content.CloseInstruction);
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
        Assert.Equal("Press Escape to close help. Other non-modifier keys close help and run normally.", content.CloseInstruction);
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.OemQuestion && entry.Command == "Left click");
    }

    private static HelpOverlayContent Build(
        ConfigModel config,
        IReadOnlyDictionary<string, int>? displayNumbers = null,
        ElementHintsHelpState? hints = null,
        bool locked = false,
        bool drag = false) =>
        HelpOverlayContentBuilder.Build(
            config,
            new FakeKeyLabelResolver(),
            logGridAvailable: true,
            isDragMode: drag,
            MacroState.Idle,
            recorderState: null,
            selectedMacroSlot: -1,
            recordedStepCount: 0,
            macroSlots: [],
            displayNumbers ?? new Dictionary<string, int>(),
            activeDisplayPath: "display-a",
            macroPickerAvailable: true,
            isModeLocked: locked,
            appScoped: false,
            elementHintsAvailable: true,
            elementHints: hints);
}
