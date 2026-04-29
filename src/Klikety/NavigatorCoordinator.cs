using System.Drawing;

using Klikety.Config;
using Klikety.Interop;
using Klikety.Navigation;
using Klikety.Services;

using Microsoft.Extensions.Logging;

namespace Klikety;

/// <summary>
/// Wires all services together: hotkey → overlay → hook → session → mouse action.
/// Single DeactivateOverlay() method covers all exit paths.
/// Delegates key input and rendering to the active <see cref="IModeSession"/>.
/// </summary>
public sealed partial class NavigatorCoordinator {
    private readonly IHotKeyService _hotKeyService;
    private readonly IKeyboardHookService _hookService;
    private readonly IMouseActionService _mouseService;
    private readonly IOverlayWindow _overlayWindow;
    private readonly IGridRenderer? _gridRenderer;
    private readonly ConfigModel _config;
    private readonly ILogger _logger;
    private readonly ActionMapper _actionMapper;

#pragma warning disable CA1859 // Will hold different session types (Crosshair, LogCrosshair)
    private IModeSession? _activeSession;
#pragma warning restore CA1859
    private bool _deactivating;
    private Point _origin;

    public NavigatorCoordinator(
        IHotKeyService hotKeyService,
        IKeyboardHookService hookService,
        IMouseActionService mouseService,
        IOverlayWindow overlayWindow,
        IGridRenderer? gridRenderer,
        ConfigModel config,
        ActionMapper actionMapper,
        ILogger logger) {
        _hotKeyService = hotKeyService;
        _hookService = hookService;
        _mouseService = mouseService;
        _overlayWindow = overlayWindow;
        _gridRenderer = gridRenderer;
        _config = config;
        _actionMapper = actionMapper;
        _logger = logger;

        _hotKeyService.Activated += OnHotKeyActivated;
        _hookService.KeyEvent += OnKeyEvent;
        _overlayWindow.FocusLost += OnFocusLost;
    }

    private void OnHotKeyActivated(object? sender, EventArgs e) {
        LogHotkeyActivated();

        var screenBounds = NativeMethods.GetPrimaryScreenBounds();
        _origin = NativeMethods.GetCursorPosition();

        _overlayWindow.Show();

        if (!_hookService.Enable()) {
            LogHookInstallFailed();
            DeactivateOverlay();
            return;
        }

        // Create and activate UniformGrid session
        var modeConfig = _config.Modes.UniformGrid;
        var session = new UniformGridSession(
            _config.FirstKeys, _config.SecondKeys,
            _actionMapper, modeConfig,
            _config.Level3CellSizeThreshold, _gridRenderer);

        session.ActionRequested += OnSessionActionRequested;
        session.Cancelled += OnSessionCancelled;
        session.CursorMoveRequested += OnSessionCursorMoveRequested;

        _activeSession = session;
        session.Activate(screenBounds, _origin);
    }

    private void OnKeyEvent(object? sender, KeyHookEventArgs e) {
        if (!e.IsDown) {
            return; // key-up: no action yet (debounce removal will be added in step 2.3)
        }

        LogKeyPressed(e.Key);
        _activeSession?.OnKey(e.Key);
    }

    private void OnFocusLost(object? sender, EventArgs e) {
        LogFocusLost();
        DeactivateOverlay();
    }

    private void OnSessionActionRequested(Point point, MouseAction action) {
        LogActionRequested(action, point.X, point.Y);
        DeactivateOverlay();
        _mouseService.SendAction(point, action);
    }

    private void OnSessionCancelled() {
        LogCancelled();
        DeactivateOverlay();
    }

    private void OnSessionCursorMoveRequested(Point point) {
        _mouseService.MoveTo(point);
    }

    /// <summary>
    /// Single idempotent exit method. Called from action, cancel, focus-loss, exception, quit.
    /// </summary>
    public void DeactivateOverlay() {
        if (_deactivating) {
            return;
        }

        _deactivating = true;

        try {
            _hookService.Disable();

            if (_activeSession is not null) {
                _activeSession.ActionRequested -= OnSessionActionRequested;
                _activeSession.Cancelled -= OnSessionCancelled;
                _activeSession.CursorMoveRequested -= OnSessionCursorMoveRequested;
                _activeSession.Deactivate();
                _activeSession = null;
            }

            _overlayWindow.ClearCanvas();
            _overlayWindow.Hide();
        } finally {
            _deactivating = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hotkey activated")]
    private partial void LogHotkeyActivated();

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to install keyboard hook")]
    private partial void LogHookInstallFailed();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Key: {Key}")]
    private partial void LogKeyPressed(Input.VKey key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Overlay focus lost")]
    private partial void LogFocusLost();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Action requested: {Action} at ({X}, {Y})")]
    private partial void LogActionRequested(MouseAction action, int x, int y);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Navigation cancelled")]
    private partial void LogCancelled();
}
