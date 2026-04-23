using Klikety.Input;

namespace Klikety.Config;

/// <summary>
/// Win32 modifier flags for RegisterHotKey. Values match MOD_* constants.
/// </summary>
[Flags]
public enum HotKeyModifiers
{
    None    = 0x0000,
    Alt     = 0x0001,
    Control = 0x0002,
    Shift   = 0x0004,
    Win     = 0x0008,
}

/// <summary>
/// Global hotkey configuration: modifier combination + trigger key.
/// </summary>
public sealed class HotKeyConfig
{
    public HotKeyModifiers Modifiers { get; init; } = HotKeyModifiers.Alt;
    public VKey Key { get; init; } = VKey.Space;
}

/// <summary>
/// First-key and second-key sets used by the two-key grid navigation scheme.
/// </summary>
public sealed class KeySetsConfig
{
    /// <summary>
    /// First-key VKey list (selects grid column). Default: QWERTY home row.
    /// </summary>
    public VKey[] FirstKeys { get; init; } =
    [
        VKey.A, VKey.S, VKey.D, VKey.F, VKey.G,
        VKey.H, VKey.J, VKey.K, VKey.L,
    ];

    /// <summary>
    /// Second-key VKey list (selects grid row). Default: QWERTY top row (non-pinky).
    /// </summary>
    public VKey[] SecondKeys { get; init; } =
    [
        VKey.W, VKey.E, VKey.R, VKey.T,
        VKey.Y, VKey.U, VKey.I, VKey.O,
    ];
}

/// <summary>
/// Root configuration model. Deserialized from %APPDATA%\Klikety\config.json (JSONC).
/// All properties have defaults so missing fields are handled gracefully.
/// </summary>
public sealed class ConfigModel
{
    public HotKeyConfig HotKey { get; init; } = new();

    /// <summary>
    /// Maps VKey names to mouse actions. Space → LeftClick is always the default
    /// even if omitted here.
    /// </summary>
    public Dictionary<string, MouseAction> ActionBindings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public KeySetsConfig KeySets { get; init; } = new();

    /// <summary>
    /// Level-3 auto-activation threshold in physical pixels² (cell area).
    /// When a level-2 cell's area exceeds this value, level-3 becomes available.
    /// Default sized for ~4K displays.
    /// </summary>
    public int Level3CellSizeThreshold { get; init; } = 40_000;

    /// <summary>
    /// Minimum log level. Accepts Microsoft.Extensions.Logging level names:
    /// Trace, Debug, Information, Warning, Error, Critical, None.
    /// </summary>
    public string LogLevel { get; init; } = "Warning";

    /// <summary>
    /// Which navigation input schemes are active.
    /// </summary>
    public NavigationMode NavigationMode { get; init; } = NavigationMode.Both;

    /// <summary>
    /// Theme name (bare name resolves to themes/&lt;name&gt;.theme.json) or
    /// relative path from the config folder.
    /// </summary>
    public string Theme { get; init; } = "dark";
}
