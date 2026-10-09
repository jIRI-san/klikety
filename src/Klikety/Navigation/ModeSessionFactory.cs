using Klikety.Automation;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Services;

using Microsoft.Extensions.Logging;

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
    private readonly ILogGridRenderer? _logGridRenderer;
    private readonly LogGridKeyPolicyResult _logGridKeyPolicy;
    private IElementHintService? _elementHintsService;
    private readonly IElementHintsRenderer? _elementHintsRenderer;
    private readonly ILogger? _logger;
    private readonly ElementHintAssignmentCache _elementHintAssignments;

    public ModeSessionFactory(
        ConfigModel config, ActionMapper actionMapper,
        IGridRenderer? gridRenderer, ICrosshairRenderer? crosshairRenderer = null,
        ILogCrosshairRenderer? logCrosshairRenderer = null,
        ILogGridRenderer? logGridRenderer = null,
        IElementHintsRenderer? elementHintsRenderer = null,
        IElementHintService? elementHintsService = null, ILogger? logger = null) {
        _config = config;
        _actionMapper = actionMapper;
        _gridRenderer = gridRenderer;
        _crosshairRenderer = crosshairRenderer;
        _logCrosshairRenderer = logCrosshairRenderer;
        _logGridRenderer = logGridRenderer;
        _elementHintsRenderer = elementHintsRenderer;
        _elementHintsService = elementHintsService;
        _logger = logger;
        _elementHintAssignments = new(config.Modes.ElementHints.CacheWindowCount);
        _logGridKeyPolicy = LogGridKeyPolicy.Evaluate(config.HorizontalKeys, config.VerticalKeys);
    }

    /// <summary>
    /// Whether LogGrid mode is available (enough keys on both axes).
    /// </summary>
    public bool IsLogGridAvailable => _logGridKeyPolicy.IsAvailable;
    public bool IsElementHintsAvailable => _config.Modes.ElementHints.Enabled &&
        ElementHintsPolicy.GetInvalidReason(_config) is null;

    /// <summary>
    /// Warning from LogGrid key policy evaluation (trim or unavailability reason).
    /// </summary>
    public string? LogGridKeyPolicyWarning => _logGridKeyPolicy.Warning;

    /// <summary>
    /// Rebuilds display labels on all configured renderers from a fresh layout resolver.
    /// </summary>
    public void RebuildLabels(IKeyLabelResolver resolver) {
        _gridRenderer?.RebuildLabels(resolver);
        _crosshairRenderer?.RebuildLabels(resolver);
        _logCrosshairRenderer?.RebuildLabels(resolver);
        _logGridRenderer?.RebuildLabels(resolver);
        _elementHintsRenderer?.RebuildLabels(resolver);
    }

    /// <summary>
    /// Creates a session for the named mode.
    /// </summary>
    public IModeSession Create(string modeName, ElementTargetContext? targetContext = null) => modeName switch {
        "UniformGrid" => CreateUniformGrid(),
        "Crosshair" => CreateCrosshair(),
        "LogCrosshair" => CreateLogCrosshair(),
        "LogGrid" => CreateLogGrid(),
        "ElementHints" when IsElementHintsAvailable && targetContext is not null =>
            new ElementHintsSession(_config.HorizontalKeys, _config.VerticalKeys, _actionMapper,
                targetContext, _elementHintsService ??= new UiaWorkerSupervisor(_config.Modes.ElementHints.DiscoveryTimeoutMs,
                    _config.Modes.ElementHints.CacheWindowCount),
                _elementHintsRenderer, _logger, _config.Modes.ElementHints.ArrowKeys) {
                AssignmentCache = _elementHintAssignments
            },
        "ElementHints" => throw new NotSupportedException("ElementHints requires valid configuration and an explicit target application."),
        _ => throw new ArgumentException($"Unknown mode: {modeName}", nameof(modeName)),
    };

    internal Task<bool> RetireElementHintsAsync() {
        _elementHintAssignments.Retire();
        return _elementHintsService?.ShutdownAsync() ?? Task.FromResult(true);
    }

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

    private LogGridSession CreateLogGrid() {
        if (!_logGridKeyPolicy.IsAvailable) {
            throw new NotSupportedException(
                _logGridKeyPolicy.Warning ?? "LogGrid mode is unavailable.");
        }

        var mode = _config.Modes.LogGrid;
        return new LogGridSession(
            _logGridKeyPolicy.EffectiveHorizontalKeys,
            _logGridKeyPolicy.EffectiveVerticalKeys,
            _actionMapper,
            mode,
            _logGridRenderer);
    }
}
