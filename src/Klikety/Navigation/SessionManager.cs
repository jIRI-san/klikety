using System.Drawing;

using Klikety.Config;
using Klikety.Input;
using Klikety.Services;

using Microsoft.Extensions.Logging;

namespace Klikety.Navigation;

/// <summary>
/// Manages mode session lifecycle: create → subscribe → activate → deactivate → unsubscribe.
/// Encapsulates the repeated session transition pattern and owns session-level scope state.
/// Forwards session events (ActionRequested, Cancelled, CursorMoveRequested) to subscribers.
/// Does NOT own overlay visibility (Show/Hide) — that stays with the coordinator.
/// </summary>
internal sealed partial class SessionManager : IDisposable {
    private readonly ModeSessionFactory _sessionFactory;
    private readonly IOverlayWindow _overlayWindow;
    private readonly ILogger _logger;

#pragma warning disable CA1859 // Will hold different session types (Crosshair, LogCrosshair, etc.)
    private IModeSession? _activeSession;
#pragma warning restore CA1859
    private bool _switching;
    private bool _modeLocked;
    private string _currentModeName = "UniformGrid";

    // Scope state
    private Point _origin;
    private Rectangle _screenBounds;
    private bool _appScoped;
    private Rectangle _appScopeBounds;

    public SessionManager(ModeSessionFactory sessionFactory, IOverlayWindow overlayWindow, ILogger logger) {
        _sessionFactory = sessionFactory;
        _overlayWindow = overlayWindow;
        _logger = logger;
    }

    // --- Properties ---

    public IModeSession? ActiveSession => _activeSession;
    public bool IsActive => _activeSession is not null;
    public bool IsSwitching => _switching;
    public bool IsModeLocked => _modeLocked;
    public string CurrentModeName => _currentModeName;
    public Point Origin { get => _origin; set => _origin = value; }
    public Rectangle ScreenBounds { get => _screenBounds; set => _screenBounds = value; }
    public bool AppScoped => _appScoped;
    public Rectangle AppScopeBounds => _appScopeBounds;
    public Rectangle ActiveBounds => _appScoped ? _appScopeBounds : _screenBounds;

    // --- Events (forwarded from active session) ---

    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action<Point>? CursorMoveRequested;

    // --- Session lifecycle methods ---

    /// <summary>
    /// Creates and activates the initial session for the given mode.
    /// Called during hotkey activation.
    /// </summary>
    public void ActivateDefaultSession(Rectangle bounds, Point origin, string modeName) {
        _screenBounds = bounds;
        _origin = origin;
        _currentModeName = modeName;
        _modeLocked = false;

        var session = _sessionFactory.Create(modeName);
        SubscribeSession(session);
        _activeSession = session;
        session.Activate(bounds, origin);
    }

    /// <summary>
    /// New L1 session on another display, same mode name. Clears app-scope.
    /// </summary>
    public void RestartOnDisplay(Rectangle bounds, Point origin) {
        if (_switching) {
            return;
        }

        _switching = true;
        try {
            UnsubscribeAndDeactivateSession();
            _overlayWindow.ClearCanvas();
            _overlayWindow.SetAppScopeBorder(false);
            _appScoped = false;
            _appScopeBounds = Rectangle.Empty;
            _modeLocked = false;
            _screenBounds = bounds;
            _origin = origin;
            var session = _sessionFactory.Create(_currentModeName);
            SubscribeSession(session);
            _activeSession = session;
            session.Activate(bounds, origin);
        } finally {
            _switching = false;
        }
    }

    /// <summary>
    /// Switches to a different navigation mode. Unsubscribes old session, clears canvas,
    /// creates new session, subscribes, and activates.
    /// </summary>
    public void SwitchMode(string targetModeName) {
        if (_switching) {
            return;
        }

        _switching = true;
        LogModeSwitching(targetModeName);

        try {
            UnsubscribeAndDeactivateSession();
            _overlayWindow.ClearCanvas();

            var session = _sessionFactory.Create(targetModeName);
            SubscribeSession(session);
            _activeSession = session;
            _currentModeName = targetModeName;
            session.Activate(ActiveBounds, _origin);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            LogModeSwitchFailed(targetModeName, ex.Message);
            throw;
        } finally {
            _switching = false;
        }
    }

    /// <summary>
    /// Switches to app-scoped mode: constrains the session to the given window bounds
    /// and shows the app-scope border.
    /// </summary>
    public void SwitchToAppScope(Rectangle bounds, Point origin) {
        if (_switching) {
            return;
        }

        _switching = true;

        try {
            UnsubscribeAndDeactivateSession();
            _overlayWindow.ClearCanvas();

            var session = _sessionFactory.Create(_currentModeName);
            SubscribeSession(session);
            _activeSession = session;
            _modeLocked = false;
            _appScoped = true;
            _appScopeBounds = bounds;
            _origin = origin;
            session.Activate(bounds, origin);

            _overlayWindow.SetAppScopeBorder(true, bounds);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            LogModeSwitchFailed(_currentModeName, ex.Message);
            _appScoped = false;
            _appScopeBounds = Rectangle.Empty;
            throw;
        } finally {
            _switching = false;
        }
    }

    /// <summary>
    /// Resets the overlay for drag target selection: deactivates current session,
    /// creates a new default-mode session at the drag start point.
    /// </summary>
    public void ResetForDrag(string defaultModeName, Point dragStartPoint,
        bool recordingAppScoped, Rectangle recordingWindowBounds) {
        if (_switching) {
            return;
        }

        _switching = true;

        try {
            UnsubscribeAndDeactivateSession();
            _overlayWindow.ClearCanvas();
            _modeLocked = false;

            // If app-scoped and not recording in app-scope, clear scope state
            if (_appScoped && !recordingAppScoped) {
                _appScoped = false;
                _appScopeBounds = Rectangle.Empty;
                _overlayWindow.SetAppScopeBorder(false);
            }

            var session = _sessionFactory.Create(defaultModeName);
            SubscribeSession(session);
            _activeSession = session;

            if (recordingAppScoped) {
                session.Activate(recordingWindowBounds, dragStartPoint);
                _appScoped = true;
                _appScopeBounds = recordingWindowBounds;
                _overlayWindow.SetAppScopeBorder(true, recordingWindowBounds);
            } else {
                session.Activate(_screenBounds, dragStartPoint);
            }
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            LogModeSwitchFailed(defaultModeName, ex.Message);
            throw;
        } finally {
            _switching = false;
        }
    }

    /// <summary>
    /// Creates a new session for recording resume. Sets scope state if app-scoped.
    /// Does NOT call overlay Show — that's the coordinator's responsibility.
    /// </summary>
    public void ResumeForRecording(string defaultModeName, Rectangle bounds, Point origin, bool appScoped,
        Rectangle appScopeBounds) {
        _modeLocked = false;
        _origin = origin;

        var session = _sessionFactory.Create(defaultModeName);
        SubscribeSession(session);
        _activeSession = session;

        if (appScoped) {
            session.Activate(appScopeBounds, origin);
            _appScoped = true;
            _appScopeBounds = appScopeBounds;
            _overlayWindow.SetAppScopeBorder(true, appScopeBounds);
        } else {
            session.Activate(bounds, origin);
        }
    }

    /// <summary>
    /// Creates a new session after playback completes.
    /// Does NOT call overlay Show/Hide — that's the coordinator's responsibility.
    /// </summary>
    public void ResumeAfterPlayback(string defaultModeName, Rectangle bounds, Point origin) {
        _modeLocked = false;
        _origin = origin;
        _screenBounds = bounds;

        var session = _sessionFactory.Create(defaultModeName);
        SubscribeSession(session);
        _activeSession = session;
        session.Activate(bounds, origin);
    }

    /// <summary>
    /// Deactivates the active session and unsubscribes its events.
    /// Does NOT call overlay Hide/ClearCanvas — the coordinator owns full DeactivateOverlay.
    /// Resets app-scope state.
    /// </summary>
    public void DeactivateSession() {
        UnsubscribeAndDeactivateSession();
        _modeLocked = false;
        _appScoped = false;
        _appScopeBounds = Rectangle.Empty;
    }

    public void LockMode() => _modeLocked = true;
    public void UnlockMode() => _modeLocked = false;

    /// <summary>
    /// Forwards a key to the active session and locks the mode.
    /// </summary>
    public void ForwardKey(VKey key) {
        if (_activeSession is not null) {
            _modeLocked = true;
            _activeSession.OnKey(key);
        }
    }

    public void Dispose() {
        UnsubscribeAndDeactivateSession();
    }

    // --- Private helpers ---

    private void SubscribeSession(IModeSession session) {
        session.ActionRequested += OnSessionActionRequested;
        session.Cancelled += OnSessionCancelled;
        session.CursorMoveRequested += OnSessionCursorMoveRequested;
    }

    private void UnsubscribeAndDeactivateSession() {
        if (_activeSession is not null) {
            _activeSession.ActionRequested -= OnSessionActionRequested;
            _activeSession.Cancelled -= OnSessionCancelled;
            _activeSession.CursorMoveRequested -= OnSessionCursorMoveRequested;
            _activeSession.Deactivate();
            _activeSession = null;
        }
    }

    private void OnSessionActionRequested(Point point, MouseAction action) =>
        ActionRequested?.Invoke(point, action);

    private void OnSessionCancelled() =>
        Cancelled?.Invoke();

    private void OnSessionCursorMoveRequested(Point point) =>
        CursorMoveRequested?.Invoke(point);

    // --- Log messages ---

    [LoggerMessage(Level = LogLevel.Debug, Message = "Switching to mode: {Mode}")]
    private partial void LogModeSwitching(string mode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mode switch to {Mode} failed: {Error}")]
    private partial void LogModeSwitchFailed(string mode, string error);
}
