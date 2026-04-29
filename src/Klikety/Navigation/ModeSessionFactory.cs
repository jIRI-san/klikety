using Klikety.Config;
using Klikety.Services;

namespace Klikety.Navigation;

/// <summary>
/// Creates <see cref="IModeSession"/> instances by mode name.
/// UniformGrid is fully implemented; Crosshair and LogCrosshair are stubs
/// until Phases 3/4.
/// </summary>
public sealed class ModeSessionFactory {
    private readonly ConfigModel _config;
    private readonly ActionMapper _actionMapper;
    private readonly IGridRenderer? _gridRenderer;

    public ModeSessionFactory(ConfigModel config, ActionMapper actionMapper, IGridRenderer? gridRenderer) {
        _config = config;
        _actionMapper = actionMapper;
        _gridRenderer = gridRenderer;
    }

    /// <summary>
    /// Creates a session for the default enabled mode.
    /// </summary>
    public IModeSession CreateDefault() {
        // For now, always UniformGrid. Multi-mode dispatch added in Phase 3/4.
        return CreateUniformGrid();
    }

    /// <summary>
    /// Creates a session for the named mode.
    /// </summary>
    public IModeSession Create(string modeName) => modeName switch {
        "UniformGrid" => CreateUniformGrid(),
        "Crosshair" => throw new NotSupportedException("Crosshair mode not yet implemented."),
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
}
