using System.Drawing;

using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Services;

using Microsoft.Extensions.Logging;

namespace Klikety;

/// <summary>
/// Wires all services together: hotkey → overlay → hook → session → mouse action.
/// Single DeactivateOverlay() method covers all exit paths.
/// Delegates key input and rendering to the active <see cref="IModeSession"/>.
/// Handles chord dispatch, debounce, mode lock, and guardrails.
/// </summary>
public sealed partial class NavigatorCoordinator {
    private readonly IHotKeyService _hotKeyService;
    private readonly IKeyboardHookService _hookService;
    private readonly IMouseActionService _mouseService;
    private readonly IOverlayWindow _overlayWindow;
    private readonly ConfigModel _config;
    private readonly ILogger _logger;
    private readonly ModeSessionFactory _sessionFactory;
    private readonly IPlatformServices _platform;

    /// <summary>Debounce timeout duration.</summary>
    private static readonly TimeSpan DebounceTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Known QWERTY layout handles (low word = language ID 0x0409 = US English).</summary>
    private const int QwertyLanguageId = 0x0409;

#pragma warning disable CA1859 // Will hold different session types (Crosshair, LogCrosshair)
    private IModeSession? _activeSession;
#pragma warning restore CA1859
    private bool _deactivating;
    private bool _switching;
    private bool _modeLocked;
    private bool _nonQwertyWarningShown;
    private Point _origin;
    private Rectangle _screenBounds;
    private int _sessionGeneration;

    // Debounce state
    private readonly HashSet<VKey> _debounceKeys = [];
    private IDebounceTimer? _debounceTimer;

    // Chord key lookup: VKey → mode name
    private readonly Dictionary<VKey, string> _chordKeyMap = [];

    public NavigatorCoordinator(
        IHotKeyService hotKeyService,
        IKeyboardHookService hookService,
        IMouseActionService mouseService,
        IOverlayWindow overlayWindow,
        ModeSessionFactory sessionFactory,
        IPlatformServices platform,
        ConfigModel config,
        ILogger logger) {
        _hotKeyService = hotKeyService;
        _hookService = hookService;
        _mouseService = mouseService;
        _overlayWindow = overlayWindow;
        _sessionFactory = sessionFactory;
        _platform = platform;
        _config = config;
        _logger = logger;

        BuildChordKeyMap();

        _hotKeyService.Activated += OnHotKeyActivated;
        _hookService.KeyEvent += OnKeyEvent;
        _overlayWindow.FocusLost += OnFocusLost;
    }

    private void BuildChordKeyMap() {
        if (_config.Modes.Crosshair is { Enabled: true, ChordKey: { } crosshairChord }) {
            _chordKeyMap[crosshairChord] = "Crosshair";
        }

        if (_config.Modes.LogCrosshair is { Enabled: true, ChordKey: { } logChord }) {
            _chordKeyMap[logChord] = "LogCrosshair";
        }
    }

    private void OnHotKeyActivated(object? sender, EventArgs e) {
        // Re-entrant guard: ignore if already active
        if (_activeSession is not null) {
            return;
        }

        LogHotkeyActivated();

        _screenBounds = _platform.Screen.GetPrimaryScreenBounds();
        _origin = _platform.Cursor.GetCursorPosition();

        // Multi-monitor guardrail: cursor outside primary screen → suppress
        if (!_screenBounds.Contains(_origin)) {
            LogCursorOutsidePrimary(_origin.X, _origin.Y);
            return;
        }

        // Determine default mode, with non-QWERTY fallback
        var defaultModeName = GetDefaultModeName();

        // Populate debounce keys BEFORE hook enable (closes TOCTOU)
        PopulateDebounceKeys();

        _overlayWindow.Show();

        if (!_hookService.Enable()) {
            LogHookInstallFailed();
            DeactivateOverlay();
            return;
        }

        // Start debounce timer
        StartDebounceTimer();

        _modeLocked = false;

        // Create and activate default mode session
        var session = _sessionFactory.Create(defaultModeName);

        session.ActionRequested += OnSessionActionRequested;
        session.Cancelled += OnSessionCancelled;
        session.CursorMoveRequested += OnSessionCursorMoveRequested;

        _activeSession = session;
        _sessionGeneration++;
        session.Activate(_screenBounds, _origin);
    }

    private string GetDefaultModeName() {
        var modes = _config.Modes;

        // Identify the configured default mode
        string defaultMode;
        if (modes.Crosshair is { Default: true, Enabled: true }) {
            defaultMode = "Crosshair";
        } else if (modes.LogCrosshair is { Default: true, Enabled: true }) {
            defaultMode = "LogCrosshair";
        } else {
            defaultMode = "UniformGrid";
        }

        // Non-QWERTY fallback for non-UniformGrid defaults
        if (defaultMode != "UniformGrid" && !IsQwertyLayout()) {
            if (!_nonQwertyWarningShown) {
                _nonQwertyWarningShown = true;
                LogNonQwertyFallback(defaultMode);
            }

            return "UniformGrid";
        }

        return defaultMode;
    }

    private bool IsQwertyLayout() {
        var hkl = _platform.KeyboardLayout.GetActiveKeyboardLayout();
        // Low word of HKL is the language identifier
        return (hkl & 0xFFFF) == QwertyLanguageId;
    }

    private void PopulateDebounceKeys() {
        _debounceKeys.Clear();

        // Always add the trigger key unconditionally
        _debounceKeys.Add(_config.HotKey.Key);

        // Check modifier variants via actual key state
        var modifiers = _config.HotKey.Modifiers;

        if (modifiers.HasFlag(HotKeyModifiers.Alt)) {
            CheckAndAddDebounceKey(VKey.LMenu);
            CheckAndAddDebounceKey(VKey.RMenu);
            CheckAndAddDebounceKey(VKey.Menu);
        }

        if (modifiers.HasFlag(HotKeyModifiers.Control)) {
            CheckAndAddDebounceKey(VKey.LControl);
            CheckAndAddDebounceKey(VKey.RControl);
        }

        if (modifiers.HasFlag(HotKeyModifiers.Shift)) {
            CheckAndAddDebounceKey(VKey.LShift);
            CheckAndAddDebounceKey(VKey.RShift);
        }

        if (modifiers.HasFlag(HotKeyModifiers.Win)) {
            CheckAndAddDebounceKey(VKey.LWin);
            CheckAndAddDebounceKey(VKey.RWin);
        }
    }

    private void CheckAndAddDebounceKey(VKey key) {
        if (_platform.KeyState.IsKeyDown(key)) {
            _debounceKeys.Add(key);
        }
    }

    private void StartDebounceTimer() {
        _debounceTimer?.Dispose();
        _debounceTimer = _platform.Timers.Create();
        _debounceTimer.Elapsed += OnDebounceTimerElapsed;
        _debounceTimer.Start(DebounceTimeout);
    }

    private void OnDebounceTimerElapsed() {
        // Reconcile: remove keys that are no longer physically held
        var toRemove = new List<VKey>();
        foreach (var key in _debounceKeys) {
            if (!_platform.KeyState.IsKeyDown(key)) {
                toRemove.Add(key);
            }
        }

        foreach (var key in toRemove) {
            _debounceKeys.Remove(key);
        }

        _debounceTimer?.Stop();
    }

    private void OnKeyEvent(object? sender, KeyHookEventArgs e) {
        if (!e.IsDown) {
            // Key-up: debounce removal only
            _debounceKeys.Remove(e.Key);
            return;
        }

        // Debounce suppression: ignore keys still in debounce set
        if (_debounceKeys.Contains(e.Key)) {
            return;
        }

        // Trigger-key fast removal: on first keydown for a different key, remove trigger
        if (_debounceKeys.Contains(_config.HotKey.Key)) {
            _debounceKeys.Remove(_config.HotKey.Key);
        }

        LogKeyPressed(e.Key);

        // Chord dispatch: before mode lock, chord key switches mode
        if (!_modeLocked && _chordKeyMap.TryGetValue(e.Key, out var targetMode)) {
            // Non-QWERTY check for chord target
            if (targetMode != "UniformGrid" && !IsQwertyLayout()) {
                if (!_nonQwertyWarningShown) {
                    _nonQwertyWarningShown = true;
                    LogNonQwertyFallback(targetMode);
                }
                // Ignore chord — stay on current mode
                return;
            }

            SwitchMode(targetMode);
            return;
        }

        // Any nav/arrow/action key locks the mode
        _modeLocked = true;

        _activeSession?.OnKey(e.Key);
    }

    private void SwitchMode(string targetModeName) {
        if (_switching) {
            return;
        }

        _switching = true;
        LogModeSwitching(targetModeName);

        try {
            // Unsubscribe and deactivate old session
            if (_activeSession is not null) {
                _activeSession.ActionRequested -= OnSessionActionRequested;
                _activeSession.Cancelled -= OnSessionCancelled;
                _activeSession.CursorMoveRequested -= OnSessionCursorMoveRequested;
                _activeSession.Deactivate();
                _activeSession = null;
            }

            _overlayWindow.ClearCanvas();

            // Create and activate new session
            var session = _sessionFactory.Create(targetModeName);

            session.ActionRequested += OnSessionActionRequested;
            session.Cancelled += OnSessionCancelled;
            session.CursorMoveRequested += OnSessionCursorMoveRequested;

            _activeSession = session;
            _sessionGeneration++;
            session.Activate(_screenBounds, _origin);
        } catch (Exception ex) {
            LogModeSwitchFailed(targetModeName, ex.Message);
            DeactivateOverlay();
        } finally {
            _switching = false;
        }
    }

    private void OnFocusLost(object? sender, EventArgs e) {
        LogFocusLost();
        DeactivateOverlay();
    }

    private void OnSessionActionRequested(Point point, MouseAction action) {
        LogActionRequested(action, point.X, point.Y);

        // Bounds validation
        if (!_screenBounds.Contains(point)) {
            LogActionOutOfBounds(point.X, point.Y);
            DeactivateOverlay();
            return;
        }

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

            // Clear debounce state
            _debounceKeys.Clear();
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _debounceTimer = null;

            if (_activeSession is not null) {
                _activeSession.ActionRequested -= OnSessionActionRequested;
                _activeSession.Cancelled -= OnSessionCancelled;
                _activeSession.CursorMoveRequested -= OnSessionCursorMoveRequested;
                _activeSession.Deactivate();
                _activeSession = null;
            }

            _modeLocked = false;

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
    private partial void LogKeyPressed(VKey key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Overlay focus lost")]
    private partial void LogFocusLost();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Action requested: {Action} at ({X}, {Y})")]
    private partial void LogActionRequested(MouseAction action, int x, int y);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Navigation cancelled")]
    private partial void LogCancelled();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cursor at ({X}, {Y}) is outside primary screen — activation suppressed")]
    private partial void LogCursorOutsidePrimary(int x, int y);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Non-QWERTY layout detected — falling back to UniformGrid instead of {Mode}")]
    private partial void LogNonQwertyFallback(string mode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Switching to mode: {Mode}")]
    private partial void LogModeSwitching(string mode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mode switch to {Mode} failed: {Error}")]
    private partial void LogModeSwitchFailed(string mode, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action point ({X}, {Y}) out of screen bounds — suppressed")]
    private partial void LogActionOutOfBounds(int x, int y);
}
