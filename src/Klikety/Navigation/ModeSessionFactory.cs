using Klikety.Config;
using Klikety.Grid;
using Klikety.Services;

namespace Klikety.Navigation;

/// <summary>
/// Creates <see cref="IModeSession"/> instances by mode name.
/// </summary>
public sealed class ModeSessionFactory {
    private readonly ConfigModel _config;
    private readonly ActionMapper _actionMapper;
    private readonly IGridRenderer? _gridRenderer;
    private readonly ICrosshairRenderer? _crosshairRenderer;
    private readonly ILogCrosshairRenderer? _logCrosshairRenderer;

    public ModeSessionFactory(
        ConfigModel config, ActionMapper actionMapper,
        IGridRenderer? gridRenderer, ICrosshairRenderer? crosshairRenderer = null,
        ILogCrosshairRenderer? logCrosshairRenderer = null) {
        _config = config;
        _actionMapper = actionMapper;
        _gridRenderer = gridRenderer;
        _crosshairRenderer = crosshairRenderer;
        _logCrosshairRenderer = logCrosshairRenderer;
    }

    /// <summary>
    /// Creates a session for the named mode.
    /// </summary>
    public IModeSession Create(string modeName) => modeName switch {
        "UniformGrid" => CreateUniformGrid(),
        "Crosshair" => CreateCrosshair(),
        "LogCrosshair" => CreateLogCrosshair(),
        _ => throw new ArgumentException($"Unknown mode: {modeName}", nameof(modeName)),
    };

    private UniformGridSession CreateUniformGrid() {
        return new UniformGridSession(
            _config.HorizontalKeys,
            _config.VerticalKeys,
            _actionMapper,
            _config.Modes.UniformGrid,
            _config.Level3CellSizeThreshold,
            _gridRenderer,
            minCellPx: 10);
    }

    private CrosshairSession CreateCrosshair() {
        var mode = _config.Modes.Crosshair;
        return new CrosshairSession(
            _config.HorizontalKeys,
            _config.VerticalKeys,
            _actionMapper,
            mode,
            _crosshairRenderer,
            gridRenderer: _gridRenderer);
    }

    private LogCrosshairSession CreateLogCrosshair() {
        var mode = _config.Modes.LogCrosshair;
        return new LogCrosshairSession(
            _config.HorizontalKeys,
            _config.VerticalKeys,
            _actionMapper,
            mode,
            _logCrosshairRenderer);
    }
}
