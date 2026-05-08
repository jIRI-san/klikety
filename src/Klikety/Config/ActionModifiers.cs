namespace Klikety.Config;

/// <summary>
/// Modifier keys captured at action time for injection into SendInput.
/// Separate from <see cref="HotKeyModifiers"/> (different values and semantics).
/// </summary>
[Flags]
public enum ActionModifiers {
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}
