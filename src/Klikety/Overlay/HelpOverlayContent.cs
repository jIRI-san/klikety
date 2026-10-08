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
        bool helpBindingValid = true,
        bool elementHintsAvailable = false,
        ElementHintsHelpState? elementHints = null) {
        var helpBinding = config.HelpBinding;
        helpBindingValid &= helpBinding?.Enabled == true;
        var activeHelpBinding = helpBindingValid ? helpBinding : null;
        var entries = new List<HelpOverlayEntry>();
        var mapper = new ActionMapper(config.ActionBindings);
        var recording = config.Macros.Enabled && macroState == MacroState.Recording;
        var macroBlocksNavigation = recording && recorderState is
            MacroRecorderState.AwaitSlot or MacroRecorderState.AwaitOverwrite or MacroRecorderState.AwaitStartFromCursorConfirm;

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

            var requiresSelection = elementHints is { HasSelection: false };
            var invalidDrag = elementHints is not null && isDragMode &&
                action is MouseAction.MoveOnly or MouseAction.DragDrop;
            entries.Add(NewEntry(key, command, HelpEntryCategory.Action, labels,
                !requiresSelection && !invalidDrag && !(elementHints is not null && macroBlocksNavigation),
                elementHints is not null && macroBlocksNavigation ? "Finish macro setup first" :
                requiresSelection ? "Select a hint first" : invalidDrag ? "Choose a click action to finish the drag" : null));
        }

        AddMode(config.Modes.UniformGrid, "UniformGrid", "Uniform grid", labels, entries, true, isModeLocked);
        AddMode(config.Modes.Crosshair, "Crosshair", "Crosshair", labels, entries, true, isModeLocked);
        AddMode(config.Modes.ElementHints, "ElementHints", "Element hints", labels, entries, elementHintsAvailable, isModeLocked);
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
        if (elementHints is { } hints) {
            entries.Add(NewEntry(VKey.Return, "Grid fallback", HelpEntryCategory.Mode, labels, !macroBlocksNavigation));
            var hasTargets = hints.TargetCount > 0;
            foreach (var arrow in new[] { VKey.Left, VKey.Right, VKey.Up, VKey.Down }) {
                entries.Add(NewEntry(arrow, "Focus control/group", HelpEntryCategory.Mode, labels,
                    hasTargets && hints.ArrowKeys && !macroBlocksNavigation));
            }
            if (hints.PageCount > 1) {
                entries.Add(NewEntry(VKey.Prior, "Previous page", HelpEntryCategory.Mode, labels, !macroBlocksNavigation));
                entries.Add(NewEntry(VKey.Next, "Next page", HelpEntryCategory.Mode, labels, !macroBlocksNavigation));
            }
            if (!string.IsNullOrWhiteSpace(hints.Status)) {
                prompts.Add(hints.Status);
            }
            if (hasTargets) {
                prompts.Add($"Level {hints.Depth}: {(hints.SingleKey ? "one-key" : "two-key")} labels. + marks a navigation-only group.");
                if (hints.PageCount > 1) {
                    prompts.Add($"Page {hints.Page + 1} of {hints.PageCount}. Other controls may be on another page.");
                }
            } else if (!macroBlocksNavigation) {
                prompts.Add(hints.Outcome is null
                    ? "Wait for controls, or press Enter for the grid. No action is available while finding controls."
                    : "No selectable controls. Press Enter for the grid.");
            }
            prompts.Add(hints.SingleKey
                ? $"Label keys: {string.Join(", ", config.HorizontalKeys.Select(labels.Resolve))}."
                : $"First label key: {string.Join(", ", config.HorizontalKeys.Select(labels.Resolve))}. Second label key: {string.Join(", ", config.VerticalKeys.Select(labels.Resolve))}.");
            if (macroBlocksNavigation) {
                prompts.Add("Finish macro setup first: label, page, action and Enter keys are consumed until the macro slot/confirmation is complete.");
            } else {
                prompts.Add("Type the label printed on a hint to select without clicking, or open a group. Only displayed labels are valid.");
                prompts.Add(hints.HasSelection
                    ? "Target selected. Use an action key to click, move only, or start a drag; targets are checked again before acting."
                    : hints.Prefix is { } prefix
                        ? $"First key {labels.Resolve(config.HorizontalKeys[prefix])} entered. Type a second label key to select; action keys do nothing until selection."
                        : hints.FocusedGroup
                            ? "Group focused. Type its label to open; action keys cannot act on groups."
                            : "No target selected. Action keys do nothing until a label or arrow selects a control.");
                if (hints.ArrowKeys) { prompts.Add("Arrows focus controls or groups within this level, never switch pages."); }
                if (hints.PageCount > 1) { prompts.Add("PgUp/PgDn wrap pages and clear the first key and selection, independently of arrow navigation."); }
                prompts.Add("Enter switches to the grid while loading, after a failure, or with a first key/selection, even when mode changes are locked.");
            }
            prompts.Add("Hold Ctrl, Alt or Shift with a click to modify it. Move only ignores modifiers. Start drag selects its source; then select a destination and use a click action to finish.");
            prompts.Add(recording
                ? "Outside help: Escape cancels macro recording first. Once idle, Escape clears a first key, otherwise returns to the previous level or cancels at Level 1."
                : "Outside help: Escape clears a first key, otherwise returns to the previous level; at Level 1 it cancels immediately.");
        }
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

        if (elementHints is not null && macroBlocksNavigation) {
            for (int index = 0; index < entries.Count; index++) {
                if (entries[index].Category is HelpEntryCategory.Mode or HelpEntryCategory.Scope) {
                    entries[index] = entries[index] with { IsAvailable = false, UnavailableReason = "Finish macro setup first" };
                }
            }
        }

        prompts.AddRange(entries
            .Where(entry => !entry.IsAvailable && !string.IsNullOrWhiteSpace(entry.UnavailableReason) &&
                !(elementHints is not null && (entry.Category == HelpEntryCategory.Action || macroBlocksNavigation)))
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
                ? modeName == "ElementHints" ? "Element hints are unavailable" : $"{modeName} renderer is unavailable"
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
                VKey.Prior => "PgUp",
                VKey.Next => "PgDn",
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
