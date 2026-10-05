using System.IO;

using Klikety.Input;

namespace Klikety.Config;

internal sealed record SettingsShortcut(string Name, HotKeyConfig HotKey);

internal static class SettingsShortcutInventory {
    public static IReadOnlyList<SettingsShortcut> From(ConfigModel config, bool scrollPaused = false) {
        var shortcuts = new List<SettingsShortcut> { new("hotKey", config.HotKey) };
        if (config.ScrollHotKeys.Enabled && !scrollPaused) {
            shortcuts.Add(new("scrollHotkeys.scrollUpKey", config.ScrollHotKeys.ScrollUpKey));
            shortcuts.Add(new("scrollHotkeys.scrollDownKey", config.ScrollHotKeys.ScrollDownKey));
        }
        if (config.Macros.Enabled && config.Macros.GlobalHotKey is { } macro) {
            shortcuts.Add(new("macros.globalHotKey", macro));
        }
        return shortcuts;
    }

    public static void Preflight(ConfigModel candidate, IReadOnlyList<SettingsShortcut> owned,
        Func<HotKeyConfig, string?> probe) {
        var requested = From(candidate);
        var unique = new HashSet<(HotKeyModifiers, VKey)>();
        foreach (var shortcut in requested) {
            var hotkey = shortcut.HotKey;
            if (!unique.Add((hotkey.Modifiers, hotkey.Key))) {
                throw new InvalidDataException($"{shortcut.Name}: duplicate global shortcut.");
            }
            if (owned.Any(entry => entry.HotKey.Modifiers == hotkey.Modifiers && entry.HotKey.Key == hotkey.Key)) {
                continue;
            }
            if (probe(hotkey) is { } error) {
                throw new InvalidDataException($"{shortcut.Name}: {error}");
            }
        }
    }
}
