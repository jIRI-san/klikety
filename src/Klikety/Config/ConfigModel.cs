using Klikety.Input;

namespace Klikety.Config;

/// <summary>
/// Per-mode configuration. Each navigation mode has its own toggles, chord key,
/// and axis key arrays.
/// <para>
/// Bool properties default to <c>false</c> and arrays to <c>null</c> by design.
/// Usable defaults live in <see cref="ModesConfig"/> property initializers.
/// The config loader (JsonDocument pre-pass) is responsible for merging partial
/// user overrides onto those defaults — bare <c>new ModeConfig()</c> yields an
/// all-disabled instance.
/// </para>
/// </summary>
public sealed class ModeConfig {
    public bool Enabled { get; init; }
    public bool Default { get; init; }
    public VKey? ChordKey { get; init; }
    public bool ArrowKeys { get; init; }
    public bool TwoKey { get; init; }

    /// <summary>
    /// Base cell size in physical pixels for LogCrosshair center cell.
    /// Only meaningful for LogCrosshair mode. Default 5.
    /// </summary>
    public int LogBaseSize { get; init; } = 5;

    /// <summary>
    /// Horizontal axis keys for Crosshair/LogCrosshair modes.
    /// Null = use 10-key QWERTY defaults at load time.
    /// </summary>
    public VKey[]? HorizontalKeys { get; init; }

    /// <summary>
    /// Vertical axis keys for Crosshair/LogCrosshair modes.
    /// Null = use 10-key QWERTY defaults at load time.
    /// </summary>
    public VKey[]? VerticalKeys { get; init; }
}

/// <summary>
/// Container for all navigation mode configurations.
/// </summary>
public sealed class ModesConfig {
    public ModeConfig UniformGrid { get; init; } = new() {
        Enabled = true,
        Default = true,
        ArrowKeys = true,
        TwoKey = true,
    };

    public ModeConfig Crosshair { get; init; } = new() {
        Enabled = true,
        ChordKey = VKey.N,
        ArrowKeys = true,
        TwoKey = true,
        HorizontalKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon],
        VerticalKeys = [VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P],
    };

    public ModeConfig LogCrosshair { get; init; } = new() {
        Enabled = true,
        ChordKey = VKey.M,
        ArrowKeys = true,
        TwoKey = true,
        HorizontalKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon],
        VerticalKeys = [VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P],
    };
}

/// <summary>
/// Win32 modifier flags for RegisterHotKey. Values match MOD_* constants.
/// </summary>
[Flags]
public enum HotKeyModifiers {
    None = 0x0000,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

/// <summary>
/// Global hotkey configuration: modifier combination + trigger key.
/// </summary>
public sealed class HotKeyConfig {
    public HotKeyModifiers Modifiers { get; init; } = HotKeyModifiers.Alt;
    public VKey Key { get; init; } = VKey.Space;
}

/// <summary>
/// Root configuration model. Deserialized from %APPDATA%\Klikety\config.json (JSONC).
/// All properties have defaults so missing fields are handled gracefully.
/// </summary>
public sealed class ConfigModel {
    public HotKeyConfig HotKey { get; init; } = new();

    /// <summary>
    /// Per-mode configuration for all navigation modes.
    /// </summary>
    public ModesConfig Modes { get; init; } = new();

    /// <summary>
    /// Config schema version for migration detection.
    /// 0 = legacy (pre-modes), 1 = current.
    /// </summary>
    public int ConfigVersion { get; init; }

    /// <summary>
    /// Maps VKey names to mouse actions. Space → LeftClick is always the default
    /// even if omitted here.
    /// </summary>
    public Dictionary<string, MouseAction> ActionBindings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// First-key VKey list (selects grid column). Combined left+right hand keys for unified grid.
    /// </summary>
    public VKey[] FirstKeys { get; init; } = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon];

    /// <summary>
    /// Second-key VKey list (selects grid row). Combined left+right hand keys for unified grid.
    /// </summary>
    public VKey[] SecondKeys { get; init; } = [VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I, VKey.O];

    /// <summary>
    /// Level-3 auto-activation threshold in physical pixels² (cell area).
    /// When a level-2 cell's area exceeds this value, level-3 becomes available.
    /// Default 0 = always active. Set higher to disable L3 on small cells.
    /// </summary>
    public int Level3CellSizeThreshold { get; init; }

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

    /// <summary>
    /// Enables file logging to %APPDATA%\Klikety\logs\. Disabled by default
    /// to avoid polluting the user's filesystem.
    /// </summary>
    public bool FileLoggingEnabled { get; init; }

    /// <summary>
    /// Maximum number of rolling log files retained. Oldest files are deleted
    /// when this count is exceeded. Only applies when FileLoggingEnabled is true.
    /// </summary>
    public int RetainedLogFileCount { get; init; } = 7;

    /// <summary>
    /// Minimum label font size in DIP. When a subgrid cell is too small to fit
    /// labels at this size, labels render outside the grid with connector lines.
    /// </summary>
    public double MinLabelFontSize { get; init; } = 14.0;
}
