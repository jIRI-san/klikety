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

    public ModeSessionFactory(
        ConfigModel config, ActionMapper actionMapper,
        IGridRenderer? gridRenderer, ICrosshairRenderer? crosshairRenderer = null) {
        _config = config;
        _actionMapper = actionMapper;
        _gridRenderer = gridRenderer;
        _crosshairRenderer = crosshairRenderer;
    }

    /// <summary>
    /// Creates a session for the named mode.
    /// </summary>
    public IModeSession Create(string modeName) => modeName switch {
        "UniformGrid" => CreateUniformGrid(),
        "Crosshair" => CreateCrosshair(),
        "LogCrosshair" => throw new NotSupportedException("LogCrosshair mode not yet implemented."),
        _ => throw new ArgumentException($"Unknown mode: {modeName}", nameof(modeName)),
    };

    private UniformGridSession CreateUniformGrid() {
        return new UniformGridSession(
            _config.FirstKeys,
            _config.SecondKeys,
            _actionMapper,
            _config.Modes.UniformGrid,
            _config.Level3CellSizeThreshold,
            _gridRenderer);
    }

    private CrosshairSession CreateCrosshair() {
        var mode = _config.Modes.Crosshair;
        return new CrosshairSession(
            mode.HorizontalKeys ?? throw new InvalidOperationException("Crosshair mode requires HorizontalKeys."),
            mode.VerticalKeys ?? throw new InvalidOperationException("Crosshair mode requires VerticalKeys."),
            _actionMapper,
            mode,
            _crosshairRenderer);
    }
}
