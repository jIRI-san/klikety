using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Overlay;

public enum HelpEntryCategory {
    Action,
    Mode,
    Scope,
    Macro,
    Display,
    Help,
}

public sealed record HelpOverlayEntry(
    VKey Key,
    string KeyLabel,
    string Command,
    HelpEntryCategory Category,
    bool IsAvailable = true,
    string? UnavailableReason = null);

public sealed record HelpOverlayContent(
    IReadOnlyList<HelpOverlayEntry> Entries,
    IReadOnlyList<VKey> NavigationAnchors,
    IReadOnlyList<string> Prompts,
    string CloseInstruction);

public static class HelpOverlayContentBuilder {
    public static HelpOverlayContent Build(
        ConfigModel config,
        IKeyLabelResolver labels,
        bool logGridAvailable,
        bool isDragMode,
        MacroState macroState,
        MacroRecorderState? recorderState,
        int selectedMacroSlot,
        int recordedStepCount,
        IReadOnlyList<MacroDefinition?> macroSlots,
        IReadOnlyDictionary<string, int> displayNumbers,
        string? activeDisplayPath,
        bool macroPickerAvailable,
        bool isModeLocked,
        bool appScoped,
        bool helpBindingValid = true) {
        var helpBinding = config.HelpBinding;
        helpBindingValid &= helpBinding?.Enabled == true;
        var activeHelpBinding = helpBindingValid ? helpBinding : null;
        var entries = new List<HelpOverlayEntry>();
        var mapper = new ActionMapper(config.ActionBindings);

        foreach (var (key, action) in mapper.Bindings.OrderBy(binding => (int)binding.Key)) {
            var command = ActionName(action);
            if (isDragMode) {
                command = action switch {
                    MouseAction.LeftClick => "Drag: left",
                    MouseAction.RightClick => "Drag: right",
                    MouseAction.MiddleClick => "Drag: middle",
                    MouseAction.DoubleClick => "Drag: double",
                    MouseAction.MoveOnly or MouseAction.DragDrop => "Invalid drag",
                    _ => command,
                };
            }

            entries.Add(NewEntry(key, command, HelpEntryCategory.Action, labels));
        }

        AddMode(config.Modes.Crosshair, "Crosshair", "Crosshair", labels, entries, true, isModeLocked);
        AddMode(config.Modes.LogCrosshair, "LogCrosshair", "LogCrosshair", labels, entries, true, isModeLocked);
        AddMode(
            config.Modes.LogGrid,
            "LogGrid",
            "LogGrid",
            labels,
            entries,
            logGridAvailable,
            isModeLocked);

        if (config.AppScope.ChordKey is { } scopeKey) {
            var available = !isModeLocked && !appScoped;
            entries.Add(NewEntry(
                scopeKey,
                appScoped ? "Scoped" : "App scope",
                HelpEntryCategory.Scope,
                labels,
                available,
                available ? null : appScoped ? "Application scope is already active" : "Navigation input has locked scope changes"));
        }

        foreach (var (devicePath, number) in displayNumbers.OrderBy(pair => pair.Value)) {
            if (number is < 1 or > 9) {
                continue;
            }

            var key = (VKey)((int)VKey.D1 + number - 1);
            var isActive = string.Equals(devicePath, activeDisplayPath, StringComparison.Ordinal);
            entries.Add(NewEntry(
                key,
                isActive ? $"Current {number}" : $"Switch {number}",
                HelpEntryCategory.Display,
                labels));
        }

        var prompts = new List<string>();
        var macrosEnabled = config.Macros.Enabled;
        if (macrosEnabled) {
            var recordCommand = recorderState switch {
                MacroRecorderState.Recording => "Stop recording",
                MacroRecorderState.AwaitSlot => "Choose slot",
                MacroRecorderState.AwaitOverwrite => "Overwrite?",
                MacroRecorderState.AwaitStartFromCursorConfirm => "Confirm drag",
                _ => "Record macro",
            };
            entries.Add(NewEntry(config.Macros.RecordKey, recordCommand, HelpEntryCategory.Macro, labels));
            entries.Add(NewEntry(
                config.Macros.HelperKey,
                "Macro picker",
                HelpEntryCategory.Macro,
                labels,
                macroPickerAvailable,
                macroPickerAvailable ? null : "Macro picker is not available"));

            for (var slot = 0; slot < Math.Min(config.Macros.SlotKeys.Length, 10); slot++) {
                var macro = slot < macroSlots.Count ? macroSlots[slot] : null;
                if (!string.IsNullOrWhiteSpace(macro?.Name)) {
                    prompts.Add($"{labels.Resolve(config.Macros.SlotKeys[slot])}: {macro.Name}");
                }
                if (recorderState == MacroRecorderState.AwaitSlot) {
                    entries.Add(NewEntry(
                        config.Macros.SlotKeys[slot],
                        macro is null ? $"Record {slot}" : $"Select {slot}",
                        HelpEntryCategory.Macro,
                        labels));
                } else if (macro is not null) {
                    entries.Add(NewEntry(
                        config.Macros.SlotKeys[slot],
                        $"Play {slot}",
                        HelpEntryCategory.Macro,
                        labels));
                } else {
                    entries.Add(NewEntry(
                        config.Macros.SlotKeys[slot],
                        $"Empty {slot}",
                        HelpEntryCategory.Macro,
                        labels,
                        false));
                }
            }
        }

        if (activeHelpBinding is { } activeBinding) {
            entries.Add(NewEntry(activeBinding.Key, "Help", HelpEntryCategory.Help, labels));
        }
        entries.Add(NewEntry(VKey.Escape, "Close", HelpEntryCategory.Help, labels));

        if (recorderState is MacroRecorderState.AwaitOverwrite) {
            entries.Add(NewEntry(VKey.Y, "Yes", HelpEntryCategory.Macro, labels));
            entries.Add(NewEntry(VKey.N, "No", HelpEntryCategory.Macro, labels));
        } else if (recorderState is MacroRecorderState.AwaitStartFromCursorConfirm) {
            entries.Add(NewEntry(VKey.Y, "Cursor start", HelpEntryCategory.Macro, labels));
            entries.Add(NewEntry(VKey.N, "Recorded start", HelpEntryCategory.Macro, labels));
        }

        if (isDragMode) {
            prompts.Add("Drag target: choose a click action to finish, or Escape to cancel.");
        }

        if (macroState == MacroState.Recording) {
            if (recorderState == MacroRecorderState.AwaitSlot) {
                prompts.Add("Recording setup: choose a slot. Existing macros require overwrite confirmation.");
            } else if (recorderState == MacroRecorderState.AwaitOverwrite) {
                prompts.Add($"Overwrite slot {selectedMacroSlot}? Press Y or N to close help and answer.");
            } else if (recorderState == MacroRecorderState.AwaitStartFromCursorConfirm) {
                prompts.Add("Drag from cursor? Press Y or N to close help and answer.");
            } else {
                prompts.Add($"Recording macro ({recordedStepCount} steps).");
            }
            prompts.Add("The recording clock continues while help is open.");
        }

        prompts.AddRange(entries
            .Where(entry => !entry.IsAvailable && !string.IsNullOrWhiteSpace(entry.UnavailableReason))
            .Select(entry => $"{entry.KeyLabel}: {entry.Command} — {entry.UnavailableReason}"));

        var closeInstruction = activeHelpBinding is { } closeBinding
            ? closeBinding.RequireShift
                ? $"Press Shift+{labels.Resolve(closeBinding.Key)} or Escape to close help."
                : $"Press {labels.Resolve(closeBinding.Key)} (Shift optional) or Escape to close help."
            : "Press Escape to close help.";
        closeInstruction += " Other non-modifier keys close help and run normally.";

        return new HelpOverlayContent(
            entries,
            config.HorizontalKeys.Concat(config.VerticalKeys).Distinct().ToArray(),
            prompts,
            closeInstruction);
    }

    private static void AddMode(
        ModeConfig mode,
        string modeName,
        string command,
        IKeyLabelResolver labels,
        List<HelpOverlayEntry> entries,
        bool isAvailable,
        bool isModeLocked) {
        if (!mode.Enabled || mode.ChordKey is not { } key) {
            return;
        }

        entries.Add(NewEntry(
            key,
            command,
            HelpEntryCategory.Mode,
            labels,
            isAvailable && !isModeLocked,
            !isAvailable
                ? $"{modeName} renderer is unavailable"
                : isModeLocked ? "Navigation input has locked mode changes" : null));
    }

    private static HelpOverlayEntry NewEntry(
        VKey key,
        string command,
        HelpEntryCategory category,
        IKeyLabelResolver labels,
        bool isAvailable = true,
        string? reason = null) =>
        new(
            key,
            key switch {
                VKey.Escape => "Esc",
                VKey.Space => "Space",
                _ => labels.Resolve(key),
            },
            command,
            category,
            isAvailable,
            reason);

    private static string ActionName(MouseAction action) => action switch {
        MouseAction.LeftClick => "Left click",
        MouseAction.RightClick => "Right click",
        MouseAction.MiddleClick => "Middle click",
        MouseAction.DoubleClick => "Double click",
        MouseAction.MoveOnly => "Move only",
        MouseAction.DragDrop => "Start drag",
        _ => action.ToString(),
    };
}
