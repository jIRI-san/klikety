using Klikety.Input;
using Klikety.Services;

namespace Klikety.Config;

public static class HelpBindingPolicy {
    private static readonly HashSet<VKey> ReservedKeys = [
        VKey.Escape,
        VKey.Left, VKey.Right, VKey.Up, VKey.Down, VKey.Return,
        VKey.D1, VKey.D2, VKey.D3, VKey.D4, VKey.D5,
        VKey.D6, VKey.D7, VKey.D8, VKey.D9,
    ];

    public static string? GetInvalidReason(ConfigModel config) {
        var help = config.HelpBinding;
        if (help is null) {
            return "Help binding configuration is null.";
        }

        if (!help.Enabled) {
            return null;
        }

        if (!Enum.IsDefined(help.Key)) {
            return $"Help key value '{help.Key}' is not a recognized VKey.";
        }

        if (ReservedKeys.Contains(help.Key)) {
            return $"Help key '{help.Key}' is reserved.";
        }

        if (config.HorizontalKeys.Contains(help.Key) || config.VerticalKeys.Contains(help.Key)) {
            return $"Help key '{help.Key}' conflicts with a navigation key.";
        }

        foreach (var keyName in config.ActionBindings.Keys) {
            if (Enum.TryParse<VKey>(keyName, true, out var key) && key == help.Key) {
                return $"Help key '{help.Key}' conflicts with an action binding.";
            }
        }
        if (help.Key == VKey.Space && !config.ActionBindings.Keys.Any(keyName =>
                Enum.TryParse<VKey>(keyName, true, out var key) && key == VKey.Space)) {
            return "Help key 'Space' conflicts with the implicit left-click action binding.";
        }

        var modes = config.Modes;
        if (new[] { modes.UniformGrid, modes.Crosshair, modes.LogCrosshair, modes.LogGrid }
            .Any(mode => mode.Enabled && mode.ChordKey == help.Key)) {
            return $"Help key '{help.Key}' conflicts with a mode chord.";
        }

        if (config.AppScope.ChordKey == help.Key) {
            return $"Help key '{help.Key}' conflicts with the app-scope chord.";
        }

        if (config.Macros.Enabled &&
            (config.Macros.RecordKey == help.Key ||
             config.Macros.HelperKey == help.Key ||
             config.Macros.SlotKeys?.Contains(help.Key) == true)) {
            return $"Help key '{help.Key}' conflicts with a macro key.";
        }

        if (CanOverlapHelpBinding(config.HotKey, help.Key, help.RequireShift) ||
            (config.ScrollHotKeys.Enabled &&
             (CanOverlapHelpBinding(config.ScrollHotKeys.ScrollUpKey, help.Key, help.RequireShift) ||
              CanOverlapHelpBinding(config.ScrollHotKeys.ScrollDownKey, help.Key, help.RequireShift))) ||
            (config.Macros.Enabled &&
             config.Macros.GlobalHotKey is { } macroHotKey &&
             CanOverlapHelpBinding(macroHotKey, help.Key, help.RequireShift))) {
            return $"Help key '{help.Key}' conflicts with a global hotkey.";
        }

        return null;
    }

    public static bool Matches(HelpBindingConfig binding, VKey key, HookModifierFlags modifiers) {
        if (!binding.Enabled || binding.Key != key ||
            (modifiers & (HookModifierFlags.Control | HookModifierFlags.Alt | HookModifierFlags.Win)) != 0) {
            return false;
        }

        return !binding.RequireShift || (modifiers & HookModifierFlags.Shift) != 0;
    }

    private static bool CanOverlapHelpBinding(HotKeyConfig hotKey, VKey helpKey, bool requireShift) {
        if (hotKey.Key != helpKey) {
            return false;
        }

        var modifiers = hotKey.Modifiers;
        return (modifiers & (HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Win)) == 0 &&
            (!requireShift || (modifiers & HotKeyModifiers.Shift) != 0);
    }
}
