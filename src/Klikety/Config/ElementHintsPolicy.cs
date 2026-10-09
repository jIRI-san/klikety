using Klikety.Automation;
using Klikety.Input;

namespace Klikety.Config;

public static class ElementHintsPolicy {
    public static string? GetInvalidReason(ConfigModel config) {
        var mode = config.Modes.ElementHints;
        const string prefix = "ElementHints: ";
        if (mode.DiscoveryTimeoutMs is < ElementHintProtocol.MinDiscoveryMs or > ElementHintProtocol.MaxDiscoveryMs) {
            return prefix + $"modes.elementHints.discoveryTimeoutMs must be between {ElementHintProtocol.MinDiscoveryMs} and {ElementHintProtocol.MaxDiscoveryMs}.";
        }
        if (mode.CacheWindowCount is < 0 or > ElementHintProtocol.MaxCacheWindowCount) {
            return prefix + $"modes.elementHints.cacheWindowCount must be between 0 and {ElementHintProtocol.MaxCacheWindowCount}.";
        }
        if (!mode.Enabled) { return null; }
        if (!config.Modes.UniformGrid.Enabled) { return prefix + "enable UniformGrid for Enter fallback."; }
        if (!mode.TwoKey) { return prefix + "twoKey must be true for adaptive labels."; }
        if (new[] { config.Modes.UniformGrid, config.Modes.Crosshair, config.Modes.LogCrosshair,
                config.Modes.LogGrid, mode }.Count(m => m.Default) != 1) {
            return prefix + "exactly one mode must be default.";
        }
        if (!double.IsFinite(config.MinLabelFontSize) || config.MinLabelFontSize < 4) {
            return prefix + "minLabelFontSize must be finite and at least 4 DIP.";
        }
        var axes = config.HorizontalKeys.Concat(config.VerticalKeys).ToArray();
        var reserved = new[] { VKey.Escape, VKey.Return, VKey.Left, VKey.Right, VKey.Up, VKey.Down, VKey.Prior, VKey.Next,
            VKey.D1, VKey.D2, VKey.D3, VKey.D4, VKey.D5, VKey.D6, VKey.D7, VKey.D8, VKey.D9,
            VKey.Shift, VKey.LShift, VKey.RShift, VKey.Control, VKey.LControl, VKey.RControl,
            VKey.Menu, VKey.LMenu, VKey.RMenu, VKey.LWin, VKey.RWin };
        var actions = config.ActionBindings.Keys.Select(k => Enum.TryParse<VKey>(k, true, out var key) ? key : VKey.Space)
            .Append(VKey.Space).ToArray();
        if (config.HorizontalKeys.Length == 0 || config.VerticalKeys.Length == 0 ||
            config.HorizontalKeys.Length > 16 || config.VerticalKeys.Length > 16 ||
            axes.Distinct().Count() != axes.Length || axes.Any(k => !Enum.IsDefined(k) || reserved.Contains(k) || actions.Contains(k))) {
            return prefix + "label axes require 1-16 distinct, nonreserved, nonaction VKeys each.";
        }
        var commands = new List<VKey>(reserved) { config.HotKey.Key };
        commands.AddRange(actions);
        commands.AddRange(new[] { config.Modes.UniformGrid, config.Modes.Crosshair, config.Modes.LogCrosshair, config.Modes.LogGrid }
            .Where(m => m.Enabled && m.ChordKey.HasValue).Select(m => m.ChordKey!.Value));
        if (config.AppScope.ChordKey is { } scope) { commands.Add(scope); }
        if (config.HelpBinding.Enabled) { commands.Add(config.HelpBinding.Key); }
        if (config.ScrollHotKeys.Enabled) {
            commands.Add(config.ScrollHotKeys.ScrollUpKey.Key); commands.Add(config.ScrollHotKeys.ScrollDownKey.Key);
        }
        if (config.Macros.Enabled) {
            commands.Add(config.Macros.RecordKey); commands.Add(config.Macros.HelperKey);
            commands.AddRange(config.Macros.SlotKeys);
            if (config.Macros.GlobalHotKey is { } macro) { commands.Add(macro.Key); }
        }
        var localCommands = actions.Concat(new[] { config.Modes.UniformGrid, config.Modes.Crosshair,
            config.Modes.LogCrosshair, config.Modes.LogGrid, mode }
            .Where(m => m.Enabled && m.ChordKey.HasValue).Select(m => m.ChordKey!.Value)).ToList();
        if (config.AppScope.ChordKey is { } localScope) { localCommands.Add(localScope); }
        if (config.HelpBinding.Enabled) { localCommands.Add(config.HelpBinding.Key); }
        if (config.Macros.Enabled) {
            localCommands.Add(config.Macros.RecordKey); localCommands.Add(config.Macros.HelperKey);
            localCommands.AddRange(config.Macros.SlotKeys);
        }
        if (localCommands.Any(k => k is VKey.Prior or VKey.Next)) {
            return prefix + "PageUp/PageDown are reserved for hint paging; change the conflicting command.";
        }
        if (axes.Any(commands.Contains)) { return prefix + "label keys conflict with an enabled command."; }
        if (!mode.Default && mode.ChordKey is null) { return prefix + "nondefault mode requires chordKey."; }
        if (mode.ChordKey is { } chord && (!Enum.IsDefined(chord) || axes.Contains(chord) || commands.Contains(chord))) {
            return prefix + $"chord key '{chord}' conflicts with a configured command.";
        }
        return null;
    }
}
