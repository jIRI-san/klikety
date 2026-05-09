using Klikety.Grid;
using Klikety.Input;

namespace Klikety.Services;

/// <summary>
/// Processes raw key events into display-ready entries.
/// Handles modifier tracking, label resolution, repeat detection.
/// No WPF dependencies — fully testable.
/// </summary>
public sealed class KeyPressProcessor {
    private static readonly Dictionary<VKey, string> SpecialKeyLabels = new() {
        [VKey.Return] = "Enter",
        [VKey.Back] = "⌫",
        [VKey.Tab] = "Tab",
        [VKey.Escape] = "Esc",
        [VKey.Left] = "←",
        [VKey.Right] = "→",
        [VKey.Up] = "↑",
        [VKey.Down] = "↓",
        [VKey.Space] = "Space",
        [VKey.Prior] = "PgUp",
        [VKey.Next] = "PgDn",
        [VKey.Capital] = "Caps",
        [VKey.Pause] = "Pause",
        [(VKey)0x2D] = "Ins",
        [(VKey)0x2E] = "Del",
        [(VKey)0x24] = "Home",
        [(VKey)0x23] = "End",
        [(VKey)0x2C] = "PrtSc",
        [(VKey)0x70] = "F1",
        [(VKey)0x71] = "F2",
        [(VKey)0x72] = "F3",
        [(VKey)0x73] = "F4",
        [(VKey)0x74] = "F5",
        [(VKey)0x75] = "F6",
        [(VKey)0x76] = "F7",
        [(VKey)0x77] = "F8",
        [(VKey)0x78] = "F9",
        [(VKey)0x79] = "F10",
        [(VKey)0x7A] = "F11",
        [(VKey)0x7B] = "F12",
        [(VKey)0x7C] = "F13",
        [(VKey)0x7D] = "F14",
        [(VKey)0x7E] = "F15",
        [(VKey)0x7F] = "F16",
        [(VKey)0x80] = "F17",
        [(VKey)0x81] = "F18",
        [(VKey)0x82] = "F19",
        [(VKey)0x83] = "F20",
        [(VKey)0x84] = "F21",
        [(VKey)0x85] = "F22",
        [(VKey)0x86] = "F23",
        [(VKey)0x87] = "F24",
    };

    private static readonly HashSet<VKey> ModifierVKeys = [
        VKey.LControl, VKey.RControl,
        VKey.LShift, VKey.RShift,
        VKey.LMenu, VKey.RMenu,
        VKey.LWin, VKey.RWin,
        VKey.Shift, VKey.Control, VKey.Menu,
    ];

    private readonly Dictionary<VKey, string> _labelCache = new();
    private readonly HashSet<VKey> _heldModifiers = [];
    private readonly IKeyStateProvider _keyStateProvider;
    private readonly TimeProvider _timeProvider;

    public KeyPressProcessor(IKeyLabelResolver labelResolver, IKeyStateProvider keyStateProvider, TimeProvider timeProvider) {
        _keyStateProvider = keyStateProvider;
        _timeProvider = timeProvider;
        BuildLabelCache(labelResolver);
    }

    public KeyPressEntry? ProcessKeyDown(VKey vkey) {
        if (ModifierVKeys.Contains(vkey)) {
            _heldModifiers.Add(vkey);
            return null;
        }

        ReconcileModifiers();

        var prefix = BuildModifierPrefix();
        var label = _labelCache.TryGetValue(vkey, out var cached) ? cached : vkey.ToString();
        var fullLabel = $"{prefix}{label}";
        var ticks = _timeProvider.GetTimestamp();

        return new KeyPressEntry(fullLabel, ticks);
    }

    public void ProcessKeyUp(VKey vkey) {
        if (ModifierVKeys.Contains(vkey)) {
            _heldModifiers.Remove(vkey);
        }
    }

    public bool IsRepeat(KeyPressEntry current, KeyPressEntry? previous, int repeatWindowMs) {
        if (previous is null) return false;
        if (current.Label != previous.Label) return false;

        var elapsedMs = _timeProvider.GetElapsedTime(previous.TimestampTicks, current.TimestampTicks).TotalMilliseconds;
        return elapsedMs <= repeatWindowMs;
    }

    public void ResetModifierState() {
        _heldModifiers.Clear();
    }

    public void RebuildCache(IKeyLabelResolver labelResolver) {
        _labelCache.Clear();
        BuildLabelCache(labelResolver);
    }

    private void BuildLabelCache(IKeyLabelResolver labelResolver) {
        for (int i = 0; i <= 254; i++) {
            var vkey = (VKey)i;
            if (ModifierVKeys.Contains(vkey)) continue;

            if (SpecialKeyLabels.TryGetValue(vkey, out var special)) {
                _labelCache[vkey] = special;
            } else {
                var resolved = labelResolver.Resolve(vkey);
                if (!string.IsNullOrEmpty(resolved)) {
                    _labelCache[vkey] = resolved;
                }
            }
        }
    }

    private void ReconcileModifiers() {
        // Remove modifiers that are no longer physically held (RISK-7 mitigation)
        _heldModifiers.RemoveWhere(mod => !_keyStateProvider.IsKeyDown(mod));
    }

    private string BuildModifierPrefix() {
        if (_heldModifiers.Count == 0) return string.Empty;

        // Canonical order: Ctrl+Shift+Alt+Win (deduplicate L/R)
        bool ctrl = _heldModifiers.Contains(VKey.LControl) || _heldModifiers.Contains(VKey.RControl) || _heldModifiers.Contains(VKey.Control);
        bool shift = _heldModifiers.Contains(VKey.LShift) || _heldModifiers.Contains(VKey.RShift) || _heldModifiers.Contains(VKey.Shift);
        bool alt = _heldModifiers.Contains(VKey.LMenu) || _heldModifiers.Contains(VKey.RMenu) || _heldModifiers.Contains(VKey.Menu);
        bool win = _heldModifiers.Contains(VKey.LWin) || _heldModifiers.Contains(VKey.RWin);

        var parts = new List<string>(4);
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        if (win) parts.Add("Win");

        return parts.Count > 0 ? string.Join('+', parts) + "+" : string.Empty;
    }
}

/// <summary>
/// Represents a processed key press ready for display.
/// ToString is overridden to prevent accidental logging of key labels (REQ-18).
/// </summary>
public sealed record KeyPressEntry(string Label, long TimestampTicks) {
    public override string ToString() => "[KeyPressEntry]";
}
