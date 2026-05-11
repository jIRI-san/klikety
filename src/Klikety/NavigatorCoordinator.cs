using System.Drawing;

using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Services;

using Microsoft.Extensions.Logging;

namespace Klikety;

/// <summary>
/// Macro subsystem state. Enforces mutual exclusion between recording, playback, and picking.
/// </summary>
public enum MacroState {
    Idle,
    Recording,
    Playing,
    Picking,
}

/// <summary>
/// Wires all services together: hotkey → overlay → hook → session → mouse action.
/// Single DeactivateOverlay() method covers all exit paths.
/// Delegates key input and rendering to the active <see cref="IModeSession"/>.
/// Handles chord dispatch, debounce, mode lock, and guardrails.
/// </summary>
public sealed partial class NavigatorCoordinator : IDisposable {
    private readonly IHotKeyService _hotKeyService;
    private readonly IKeyboardHookService _hookService;
    private readonly IMouseActionService _mouseService;
    private readonly IOverlayWindow _overlayWindow;
    private readonly ConfigModel _config;
    private readonly ILogger _logger;
    private readonly ModeSessionFactory _sessionFactory;
    private readonly IPlatformServices _platform;
    private readonly IModifierDetector _modifierDetector;

    // Macro subsystem
    private readonly IMacroStore? _macroStore;
    private readonly MacroRecorder? _macroRecorder;
    private MacrosFile _macrosFile = new();
    private MacroState _macroState = MacroState.Idle;
    private readonly Dictionary<VKey, int> _slotKeyMap = [];
    private IDebounceTimer? _resumeTimer;

    // Macro picker + hotkey (set after construction by App.xaml.cs)
    public IMacroHotKeyService? MacroHotKeyService {
        get => _macroHotKeyService;
        set {
            if (_macroHotKeyService is not null) {
                _macroHotKeyService.Activated -= OnMacroHotKeyActivated;
            }
            _macroHotKeyService = value;
            if (_macroHotKeyService is not null) {
                _macroHotKeyService.Activated += OnMacroHotKeyActivated;
            }
        }
    }
    public IMacroPickerWindow? MacroPickerWindow {
        get => _macroPickerWindow;
        set {
            if (_macroPickerWindow is not null) {
                _macroPickerWindow.SlotSelected -= OnPickerSlotSelected;
                _macroPickerWindow.PickerClosed -= OnPickerClosed;
            }
            _macroPickerWindow = value;
            if (_macroPickerWindow is not null) {
                _macroPickerWindow.SlotSelected += OnPickerSlotSelected;
                _macroPickerWindow.PickerClosed += OnPickerClosed;
            }
        }
    }
    private IMacroHotKeyService? _macroHotKeyService;
    private IMacroPickerWindow? _macroPickerWindow;

    // Playback state
    private MacroPlayer? _macroPlayer;
    private CancellationTokenSource? _playbackCts;
    private Task? _playbackTask;
    private bool _playbackFromGlobalHotKey;
    private bool _disposed;
    public IMacroPlaybackWindow? MacroPlaybackWindow { get; set; }
    public IClickIndicator? ClickIndicator { get; set; }
    public IDelayProvider DelayProvider { get; set; } = new TaskDelayProvider();

#pragma warning disable CA1859 // Will hold different session types (Crosshair, LogCrosshair)
    private IModeSession? _activeSession;
#pragma warning restore CA1859
    private bool _deactivating;
    private bool _switching;
    private bool _modeLocked;
    private bool _nonQwertyWarningShown;
    private Point _origin;
    private Rectangle _screenBounds;
    private bool _dragMode;
    private Point _dragStartPoint;

    // App-scope state
    private bool _appScoped;
    private Rectangle _appScopeBounds;
    private VKey? _appScopeChordKey;
    private nint _preOverlayHwnd;
    private string _currentModeName = "UniformGrid";

    // Recording app-scope persistence
    private bool _recordingAppScoped;
    private Rectangle _recordingWindowBounds;

    private Rectangle ActiveBounds => _appScoped ? _appScopeBounds : _screenBounds;

    private readonly DebounceHandler _debounce;

    // Chord key lookup: VKey → mode name
    private readonly Dictionary<VKey, string> _chordKeyMap = [];

    public NavigatorCoordinator(
        IHotKeyService hotKeyService,
        IKeyboardHookService hookService,
        IMouseActionService mouseService,
        IOverlayWindow overlayWindow,
        ModeSessionFactory sessionFactory,
        IPlatformServices platform,
        IModifierDetector modifierDetector,
        ConfigModel config,
        ILogger logger,
        IMacroStore? macroStore = null,
        MacrosFile? macrosFile = null) {
        _hotKeyService = hotKeyService;
        _hookService = hookService;
        _mouseService = mouseService;
        _overlayWindow = overlayWindow;
        _sessionFactory = sessionFactory;
        _platform = platform;
        _modifierDetector = modifierDetector;
        _config = config;
        _logger = logger;
        _macroStore = macroStore;
        _macrosFile = macrosFile ?? new MacrosFile();

        _debounce = new DebounceHandler(platform, config);

        BuildChordKeyMap();
        BuildSlotKeyMap();

        _appScopeChordKey = config.AppScope.ChordKey;

        if (config.Macros is { Enabled: true }) {
            _macroRecorder = new MacroRecorder(platform.Screen, logger);
            _macroRecorder.RecordingComplete += OnRecordingComplete;
            _macroRecorder.RecordingCancelled += OnRecordingCancelled;
            _macroRecorder.SlotSelectionRequested += OnSlotSelectionRequested;
            _macroRecorder.OverwriteConfirmRequested += OnOverwriteConfirmRequested;
            _macroRecorder.RecordingStarted += OnRecordingStarted;
            _macroRecorder.StartFromCursorRequested += OnStartFromCursorRequested;
        }

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

        if (_config.Modes.LogGrid is { Enabled: true, ChordKey: { } logGridChord }
            && _sessionFactory.IsLogGridAvailable) {
            _chordKeyMap[logGridChord] = "LogGrid";
        }
    }

    private void BuildSlotKeyMap() {
        var macros = _config.Macros;
        if (macros is not { Enabled: true }) {
            return;
        }

        for (var i = 0; i < Math.Min(macros.SlotKeys.Length, 10); i++) {
            _slotKeyMap[macros.SlotKeys[i]] = i;
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
        _currentModeName = defaultModeName;

        // Populate debounce keys BEFORE hook enable (closes TOCTOU)
        _debounce.PopulateFromHotKey();

        // Capture foreground window HWND before Show() — overlay becomes foreground after Show()
        _preOverlayHwnd = _platform.ForegroundWindow.GetForegroundWindowHandle();

        try {
            _overlayWindow.Show();
        } catch (InvalidOperationException) {
            LogHookInstallFailed();
            DeactivateOverlay();
            return;
        }

        if (!_hookService.Enable()) {
            LogHookInstallFailed();
            DeactivateOverlay();
            return;
        }

        // Start debounce timer
        _debounce.StartTimer();

        _modeLocked = false;

        // Create and activate default mode session
        try {
            var session = _sessionFactory.Create(defaultModeName);

            session.ActionRequested += OnSessionActionRequested;
            session.Cancelled += OnSessionCancelled;
            session.CursorMoveRequested += OnSessionCursorMoveRequested;

            _activeSession = session;
            session.Activate(_screenBounds, _origin);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            LogModeSwitchFailed(defaultModeName, ex.Message);
            DeactivateOverlay();
        }
    }

    private string GetDefaultModeName() {
        var modes = _config.Modes;

        // Identify the configured default mode
        string defaultMode;
        if (modes.LogGrid is { Default: true, Enabled: true } && _sessionFactory.IsLogGridAvailable) {
            defaultMode = "LogGrid";
        } else if (modes.Crosshair is { Default: true, Enabled: true }) {
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
        return _platform.KeyboardLayout.IsQwertyCompatible();
    }

    private void OnKeyEvent(object? sender, KeyHookEventArgs e) {
        if (!e.IsDown) {
            // Key-up: debounce removal only
            _debounce.Remove(e.Key);
            return;
        }

        // Debounce suppression: ignore keys still in debounce set
        if (_debounce.Contains(e.Key)) {
            return;
        }

        // Trigger-key fast removal: on first keydown for a different key,
        // remove trigger only if no longer physically held
        _debounce.RemoveTriggerIfReleased();

        LogKeyPressed(e.Key);

        // --- Macro key dispatch priority ---

        // (2) Playing: only Escape cancels playback, all else ignored by hook
        if (_macroState == MacroState.Playing) {
            if (e.Key == VKey.Escape) {
                _playbackCts?.Cancel();
            }
            return;
        }

        // (4) Recording: recording control keys
        if (_macroState == MacroState.Recording && _macroRecorder is not null) {
            if (HandleRecordingKey(e.Key)) {
                return;
            }
            // During recording in AwaitSlot/AwaitOverwrite, keys are consumed
            if (_macroRecorder.State is MacroRecorderState.AwaitSlot or MacroRecorderState.AwaitOverwrite) {
                return;
            }
        }

        // (5) Record key: start recording when idle and overlay open
        if (_macroState == MacroState.Idle
            && _activeSession is not null
            && _macroRecorder is not null
            && _config.Macros is { Enabled: true }
            && e.Key == _config.Macros.RecordKey) {
            if (_appScoped) {
                var windowTitle = _platform.ForegroundWindow.GetWindowTitle(_preOverlayHwnd);
                if (string.IsNullOrEmpty(windowTitle)) {
                    _overlayWindow.ShowStatusText("Window has no title — cannot record");
                    return;
                }

                var titlePattern = ExtractTitlePattern(windowTitle);
                var context = new MacroRecordingContext(
                    IsWindowRelative: true,
                    WindowWidth: _appScopeBounds.Width,
                    WindowHeight: _appScopeBounds.Height,
                    WindowTitlePattern: titlePattern,
                    DpiScale: _platform.Screen.GetDpiScale()
                );

                _macroState = MacroState.Recording;
                _recordingAppScoped = true;
                _recordingWindowBounds = _appScopeBounds;
                _macroRecorder.StartRecording(context);
            } else {
                _macroState = MacroState.Recording;
                _recordingAppScoped = false;
                _recordingWindowBounds = Rectangle.Empty;
                _macroRecorder.StartRecording();
            }
            return;
        }

        // (6) Helper key: open picker when idle and overlay open
        if (_macroState == MacroState.Idle
            && _activeSession is not null
            && _macroPickerWindow is not null
            && _config.Macros is { Enabled: true }
            && e.Key == _config.Macros.HelperKey) {
            ShowMacroPicker();
            return;
        }

        // (6b) Slot key: direct playback when idle and overlay open
        if (_macroState == MacroState.Idle
            && _activeSession is not null
            && _config.Macros is { Enabled: true }
            && _slotKeyMap.TryGetValue(e.Key, out var directSlot)
            && _macrosFile.Macros.Length > directSlot
            && _macrosFile.Macros[directSlot] is { } directMacro) {
            _playbackFromGlobalHotKey = false;
            var savedHwnd = _preOverlayHwnd;
            _hookService.Disable();
            _overlayWindow.Hide();
            StartPlayback(directMacro, savedHwnd);
            return;
        }

        // (7) App-scope chord dispatch: before mode lock, chord key scopes to foreground window
        if (!_modeLocked && _appScopeChordKey is { } appScopeKey && e.Key == appScopeKey) {
            if (_appScoped) {
                return; // Auto-repeat guard
            }

            var hwndBounds = _platform.ForegroundWindow.GetWindowBounds(_preOverlayHwnd);

            if (_preOverlayHwnd == 0 || hwndBounds.IsEmpty) {
                LogAppScopeRejected("invalid or minimized window");
                _overlayWindow.ShowStatusText("Invalid window");
                return;
            }

            // Clip to primary screen
            var clipped = Rectangle.Intersect(hwndBounds, _screenBounds);
            if (clipped.IsEmpty) {
                LogAppScopeRejected("window outside primary screen");
                _overlayWindow.ShowStatusText("Window outside screen");
                return;
            }

            // Clamp origin into clipped bounds
            var cursor = _platform.Cursor.GetCursorPosition();
            _origin = new Point(
                Math.Clamp(cursor.X, clipped.Left, clipped.Right - 1),
                Math.Clamp(cursor.Y, clipped.Top, clipped.Bottom - 1));

            LogAppScopeActivated(clipped.Width, clipped.Height);
            SwitchToAppScope(clipped);
            return;
        }

        // (8) Chord dispatch: before mode lock, chord key switches mode
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

        // Any key forwarded to session locks the mode
        if (_activeSession is not null) {
            _modeLocked = true;
            _activeSession.OnKey(e.Key);
        }
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
            _currentModeName = targetModeName;
            session.Activate(ActiveBounds, _origin);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            LogModeSwitchFailed(targetModeName, ex.Message);
            DeactivateOverlay();
        } finally {
            _switching = false;
        }
    }

    private void SwitchToAppScope(Rectangle bounds) {
        if (_switching) {
            return;
        }

        _switching = true;

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

            // Keep overlay full-screen — renderers use screen-space coordinates that
            // match the full-screen canvas. Pass window bounds to session only.
            var session = _sessionFactory.Create(_currentModeName);

            session.ActionRequested += OnSessionActionRequested;
            session.Cancelled += OnSessionCancelled;
            session.CursorMoveRequested += OnSessionCursorMoveRequested;

            _activeSession = session;
            _modeLocked = false;
            _appScoped = true;
            _appScopeBounds = bounds;
            session.Activate(bounds, _origin);

            _overlayWindow.SetAppScopeBorder(true, bounds);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            LogModeSwitchFailed(_currentModeName, ex.Message);
            _appScoped = false;
            _appScopeBounds = Rectangle.Empty;
            DeactivateOverlay();
        } finally {
            _switching = false;
        }
    }

    private void OnFocusLost(object? sender, EventArgs e) {
        LogFocusLost();

        // Suppress deactivation during mode/app-scope switching (WPF fires Deactivated on Hide)
        if (_switching) {
            return;
        }

        // Suppress deactivation during recording or playback
        if (_macroState != MacroState.Idle) {
            return;
        }

        DeactivateOverlay();
    }

    private void OnSessionActionRequested(Point point, MouseAction action) {
        LogActionRequested(action, point.X, point.Y);

        // Bounds validation
        if (!ActiveBounds.Contains(point)) {
            LogActionOutOfBounds(point.X, point.Y);
            _mouseService.MoveTo(_origin);
            if (_macroState == MacroState.Recording) {
                // Stay in recording mode, resume overlay
                ResumeOverlayForRecording();
                return;
            }
            DeactivateOverlay();
            return;
        }

        // Recording: intercept action to record step, then suspend/resume
        if (_macroState == MacroState.Recording && _macroRecorder is not null) {
            var modifiers = action == MouseAction.MoveOnly
                ? ActionModifiers.None
                : _modifierDetector.GetCurrentModifiers();

            var recordPoint = _recordingAppScoped
                ? new Point(point.X - _recordingWindowBounds.Left, point.Y - _recordingWindowBounds.Top)
                : point;

            // Drag phase 2: completing a drag during recording
            if (_dragMode) {
                if (action is MouseAction.MoveOnly or MouseAction.DragDrop) {
                    LogDragInvalidAction(action);
                    return;
                }

                _macroRecorder.RecordAction(recordPoint, action, modifiers);

                // StartFromCursor prompt — don't clear status or suspend overlay
                if (_macroRecorder.State == MacroRecorderState.AwaitStartFromCursorConfirm) {
                    _dragMode = false;
                    _mouseService.SendDrag(_dragStartPoint, point, action, modifiers);
                    return;
                }

                _overlayWindow.ClearStatusText();
                _dragMode = false;
                SuspendOverlayForAction();
                _mouseService.SendDrag(_dragStartPoint, point, action, modifiers);
                ScheduleResumeAfterAction();
                return;
            }

            // Drag phase 1: starting a drag during recording
            if (action == MouseAction.DragDrop) {
                _macroRecorder.RecordAction(recordPoint, MouseAction.DragDrop, modifiers);
                _dragStartPoint = point;
                _dragMode = true;
                ResetOverlayForDrag();
                return;
            }

            // Normal action during recording
            _macroRecorder.RecordAction(recordPoint, action, modifiers);
            SuspendOverlayForAction();
            _mouseService.SendAction(point, action, modifiers);
            ScheduleResumeAfterAction();
            return;
        }

        // --- Normal (non-recording) action dispatch ---

        // Drag phase 2: completing a drag
        if (_dragMode) {
            // Invalid actions during drag
            if (action is MouseAction.MoveOnly or MouseAction.DragDrop) {
                LogDragInvalidAction(action);
                return;
            }

            var modifiers = _modifierDetector.GetCurrentModifiers();
            _overlayWindow.ClearStatusText();
            _dragMode = false;
            DeactivateOverlay();
            _mouseService.SendDrag(_dragStartPoint, point, action, modifiers);
            return;
        }

        // Drag phase 1: starting a drag
        if (action == MouseAction.DragDrop) {
            _dragStartPoint = point;
            _dragMode = true;
            ResetOverlayForDrag();
            return;
        }

        var actionModifiers = action == MouseAction.MoveOnly
            ? ActionModifiers.None
            : _modifierDetector.GetCurrentModifiers();

        DeactivateOverlay();
        _mouseService.SendAction(point, action, actionModifiers);
    }

    private void ResetOverlayForDrag() {
        if (_switching) {
            return;
        }

        _switching = true;

        try {
            // Unsubscribe and deactivate current session
            if (_activeSession is not null) {
                _activeSession.ActionRequested -= OnSessionActionRequested;
                _activeSession.Cancelled -= OnSessionCancelled;
                _activeSession.CursorMoveRequested -= OnSessionCursorMoveRequested;
                _activeSession.Deactivate();
                _activeSession = null;
            }

            _overlayWindow.ClearCanvas();
            _modeLocked = false;

            // If app-scoped and not recording in app-scope, clear scope state
            if (_appScoped && !_recordingAppScoped) {
                _appScoped = false;
                _appScopeBounds = Rectangle.Empty;
                _overlayWindow.SetAppScopeBorder(false);
            }

            // Create and activate new default-mode session
            var defaultModeName = GetDefaultModeName();
            try {
                var session = _sessionFactory.Create(defaultModeName);

                session.ActionRequested += OnSessionActionRequested;
                session.Cancelled += OnSessionCancelled;
                session.CursorMoveRequested += OnSessionCursorMoveRequested;

                _activeSession = session;

                if (_recordingAppScoped) {
                    session.Activate(_recordingWindowBounds, _dragStartPoint);
                    _appScoped = true;
                    _appScopeBounds = _recordingWindowBounds;
                    _overlayWindow.SetAppScopeBorder(true, _recordingWindowBounds);
                } else {
                    session.Activate(_screenBounds, _dragStartPoint);
                }

                _overlayWindow.ShowStatusText("Select drag target");
            } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
                LogModeSwitchFailed(defaultModeName, ex.Message);
                _dragMode = false;
                _overlayWindow.ClearStatusText();
                DeactivateOverlay();
            }
        } finally {
            _switching = false;
        }
    }

    private void OnSessionCancelled() {
        LogCancelled();

        // During recording, cancel just resets overlay for next action
        if (_macroState == MacroState.Recording && _macroRecorder is not null) {
            if (_dragMode) {
                _dragMode = false;
                _overlayWindow.ClearStatusText();
            }
            // Cancel the pending drag in recorder
            _macroRecorder.Cancel();
            _macroState = MacroState.Idle;
            _overlayWindow.SetRecordingBorder(false);
            return;
        }

        if (_dragMode) {
            _dragMode = false;
            _overlayWindow.ClearStatusText();
            _mouseService.MoveTo(_origin);
        }
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
            _hookService.DrainAndDisable();

            // Clear debounce state
            _debounce.StopAndDispose();

            if (_activeSession is not null) {
                _activeSession.ActionRequested -= OnSessionActionRequested;
                _activeSession.Cancelled -= OnSessionCancelled;
                _activeSession.CursorMoveRequested -= OnSessionCursorMoveRequested;
                _activeSession.Deactivate();
                _activeSession = null;
            }

            // Drag-mode cleanup: restore cursor to origin on focus-loss or unexpected deactivation
            if (_dragMode) {
                _mouseService.MoveTo(_origin);
                _dragMode = false;
            }

            _modeLocked = false;
            _appScoped = false;
            _appScopeBounds = Rectangle.Empty;

            // Restore focus to the window that was active before the overlay.
            // Critical for MoveOnly/Cancel where no click activates the target.
            var savedHwnd = _preOverlayHwnd;
            _preOverlayHwnd = 0;

            _overlayWindow.ClearCanvas();
            _overlayWindow.ClearStatusText();
            _overlayWindow.SetAppScopeBorder(false);
            _overlayWindow.Hide();

            if (savedHwnd != 0) {
                _platform.ForegroundWindow.SetForegroundWindow(savedHwnd);
            }

            // Clear modifier keys (Alt/Ctrl/Shift) that may be stuck in the target
            // window's thread — hotkey modifier keydown went to target before overlay
            // opened, but keyup was consumed by overlay.
            _mouseService.ClearStuckModifiers();
        } finally {
            _deactivating = false;
        }
    }

    private static string ExtractTitlePattern(string windowTitle) {
        var lastSep = windowTitle.LastIndexOf(" - ", StringComparison.Ordinal);
        return lastSep >= 0 ? windowTitle[(lastSep + 3)..] : windowTitle;
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drag mode: invalid action {Action} — ignored")]
    private partial void LogDragInvalidAction(MouseAction action);

    [LoggerMessage(Level = LogLevel.Debug, Message = "App-scope activated: {Width}×{Height}")]
    private partial void LogAppScopeActivated(int width, int height);

    [LoggerMessage(Level = LogLevel.Warning, Message = "App-scope rejected: {Reason}")]
    private partial void LogAppScopeRejected(string reason);

    // --- Macro recording methods ---

    private bool HandleRecordingKey(VKey key) {
        var macros = _config.Macros;
        if (macros is null || _macroRecorder is null) {
            return false;
        }

        // Escape cancels from any recording state
        if (key == VKey.Escape) {
            CancelRecording();
            return true;
        }

        // Record key toggles stop when actively recording
        if (key == macros.RecordKey && _macroRecorder.State == MacroRecorderState.Recording) {
            _macroRecorder.StopRecording();
            return true;
        }

        // Slot key during AwaitSlot
        if (_macroRecorder.State == MacroRecorderState.AwaitSlot && _slotKeyMap.TryGetValue(key, out var slot)) {
            _macroRecorder.OnSlotKey(slot, _macrosFile.Macros);
            return true;
        }

        // Y/N during AwaitOverwrite
        if (_macroRecorder.State == MacroRecorderState.AwaitOverwrite) {
            if (key == VKey.Y) {
                _macroRecorder.OnOverwriteResponse(true);
                return true;
            }

            if (key == VKey.N) {
                _macroRecorder.OnOverwriteResponse(false);
                return true;
            }
        }

        // Y/N during AwaitStartFromCursorConfirm
        if (_macroRecorder.State == MacroRecorderState.AwaitStartFromCursorConfirm) {
            if (key == VKey.Y) {
                _macroRecorder.OnStartFromCursorResponse(true);
                _overlayWindow.ClearStatusText();
                ResumeOverlayForRecording();
                return true;
            }

            if (key == VKey.N) {
                _macroRecorder.OnStartFromCursorResponse(false);
                _overlayWindow.ClearStatusText();
                ResumeOverlayForRecording();
                return true;
            }

            // All other keys consumed (ignored) during this state
            return true;
        }

        return false;
    }

    private void CancelRecording() {
        _macroRecorder?.Cancel();
        _macroState = MacroState.Idle;
        _overlayWindow.SetRecordingBorder(false);
        _overlayWindow.ClearStatusText();
    }

    private void SuspendOverlayForAction() {
        // Deactivate session but keep hook enabled (strict filtering via _macroState)
        if (_activeSession is not null) {
            _activeSession.ActionRequested -= OnSessionActionRequested;
            _activeSession.Cancelled -= OnSessionCancelled;
            _activeSession.CursorMoveRequested -= OnSessionCursorMoveRequested;
            _activeSession.Deactivate();
            _activeSession = null;
        }

        _overlayWindow.ClearCanvas();
        _overlayWindow.ClearStatusText();
        _overlayWindow.Hide();
    }

    private void ScheduleResumeAfterAction() {
        _resumeTimer?.Dispose();
        _resumeTimer = _platform.Timers.Create();
        _resumeTimer.Elapsed += OnResumeTimerElapsed;
        _resumeTimer.Start(TimeSpan.FromMilliseconds(200));
    }

    private void OnResumeTimerElapsed() {
        _resumeTimer?.Stop();
        _resumeTimer?.Dispose();
        _resumeTimer = null;
        ResumeOverlayForRecording();
    }

    private void ResumeOverlayForRecording() {
        if (_macroState != MacroState.Recording) {
            return;
        }

        _origin = _platform.Cursor.GetCursorPosition();
        _screenBounds = _platform.Screen.GetPrimaryScreenBounds();

        // Window-relative recording: re-query window bounds via stored HWND
        if (_recordingAppScoped) {
            var newBounds = _platform.ForegroundWindow.GetWindowBounds(_preOverlayHwnd);

            // HWND invalid (window closed) → auto-stop + save
            if (newBounds.IsEmpty) {
                _macroRecorder!.StopRecording();
                return;
            }

            // Window resized → cancel recording
            if (newBounds.Width != _recordingWindowBounds.Width || newBounds.Height != _recordingWindowBounds.Height) {
                _overlayWindow.ShowStatusText("Window resized during recording");
                CancelRecording();
                return;
            }

            // Window moved (same size) → update bounds, offsets are frame-independent
            _recordingWindowBounds = newBounds;

            // Clamp origin into window bounds
            _origin = new Point(
                Math.Clamp(_origin.X, newBounds.Left, newBounds.Right - 1),
                Math.Clamp(_origin.Y, newBounds.Top, newBounds.Bottom - 1)
            );
        }

        // Re-show overlay
        try {
            _overlayWindow.Show();
        } catch (InvalidOperationException) {
            CancelRecording();
            return;
        }

        _modeLocked = false;

        // Create new default-mode session with current cursor as origin
        var defaultModeName = GetDefaultModeName();
        try {
            var session = _sessionFactory.Create(defaultModeName);

            session.ActionRequested += OnSessionActionRequested;
            session.Cancelled += OnSessionCancelled;
            session.CursorMoveRequested += OnSessionCursorMoveRequested;

            _activeSession = session;

            if (_recordingAppScoped) {
                session.Activate(_recordingWindowBounds, _origin);
                _appScoped = true;
                _appScopeBounds = _recordingWindowBounds;
                _overlayWindow.SetAppScopeBorder(true, _recordingWindowBounds);
            } else {
                session.Activate(_screenBounds, _origin);
            }

            _overlayWindow.SetRecordingBorder(true);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            LogModeSwitchFailed(defaultModeName, ex.Message);
            CancelRecording();
        }
    }

    // Recorder event handlers

    private void OnSlotSelectionRequested() {
        _overlayWindow.ShowStatusText("Select slot (0-9):");
    }

    private void OnOverwriteConfirmRequested(int slot, string name) {
        _overlayWindow.ShowStatusText($"Slot {slot}: {name}. Overwrite? (Y/N)");
    }

    private void OnRecordingStarted() {
        _overlayWindow.ClearStatusText();
        _overlayWindow.SetRecordingBorder(true);
    }

    private void OnStartFromCursorRequested() {
        _overlayWindow.ShowStatusText("Drag from cursor? [Y/N]");
    }

    private void OnRecordingComplete(int slot, MacroDefinition macro) {
        _macroState = MacroState.Idle;
        _overlayWindow.SetRecordingBorder(false);

        // Save to store
        _macrosFile.Macros[slot] = macro;
        if (_macroStore is not null) {
            var result = _macroStore.Save(_macrosFile);
            if (!result.Success) {
                LogMacroSaveFailed(slot, result.Error ?? "unknown error");
                // Keep in-memory state (slot not emptied on save failure)
            }
        }
    }

    private void OnRecordingCancelled() {
        _macroState = MacroState.Idle;
        _overlayWindow.SetRecordingBorder(false);
        _overlayWindow.ClearStatusText();
    }

    private void OnSessionCancelledDuringRecording() {
        if (_macroState == MacroState.Recording && _macroRecorder is not null) {
            CancelRecording();
            return;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to save macro slot {Slot}: {Error}")]
    private partial void LogMacroSaveFailed(int slot, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Macro playback failed: {Error}")]
    private partial void LogPlaybackFailed(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Screen mismatch for macro playback: {Details}")]
    private partial void LogScreenMismatch(string details);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Macro picker opening (helper key)")]
    private partial void LogMacroPickerOpening();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Macro picker shown")]
    private partial void LogMacroPickerShown();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Picker slot selected: {Slot} (state={State})")]
    private partial void LogPickerSlotSelected(int slot, MacroState state);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Picker closed (state={State})")]
    private partial void LogPickerClosed(MacroState state);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Playback starting: '{Name}' ({StepCount} steps)")]
    private partial void LogPlaybackStarting(string name, int stepCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Playback finished: {Result}")]
    private partial void LogPlaybackFinished(PlaybackResultKind result);

    // --- Macro picker methods ---

    private void ShowMacroPicker() {
        LogMacroPickerOpening();
        _playbackFromGlobalHotKey = false;
        _macroState = MacroState.Picking;
        _hookService.Disable();
        _overlayWindow.Hide();
        _macroPickerWindow!.Show(_macrosFile.Macros, _config.Macros.SlotKeys);
        LogMacroPickerShown();
    }

    private void OnMacroHotKeyActivated() {
        if (_macroState != MacroState.Idle || _macroPickerWindow is null) {
            return;
        }

        _playbackFromGlobalHotKey = true;
        _preOverlayHwnd = _platform.ForegroundWindow.GetForegroundWindowHandle();
        _macroState = MacroState.Picking;
        _macroPickerWindow.Show(_macrosFile.Macros, _config.Macros.SlotKeys);
    }

    private void OnPickerSlotSelected(int slot) {
        LogPickerSlotSelected(slot, _macroState);
        if (_macroState != MacroState.Picking) {
            return;
        }

        var macro = _macrosFile.Macros.Length > slot ? _macrosFile.Macros[slot] : null;
        if (macro is null) {
            OnPickerClosed();
            return;
        }

        StartPlayback(macro, _preOverlayHwnd);
    }

    private void StartPlayback(MacroDefinition macro, nint targetHwnd) {
        LogPlaybackStarting(macro.Name, macro.Steps.Count);
        _macroState = MacroState.Playing;
        _hookService.Enable();

        _macroPlayer = new MacroPlayer(_mouseService, _platform.Screen, DelayProvider,
            macro.SpeedModifier != 1.0 ? macro.SpeedModifier : _config.Macros.SpeedModifier,
            ClickIndicator, _platform.ForegroundWindow);

        PlaybackContext context;
        if (macro.PositionMode == MacroPositionMode.WindowRelative) {
            var title = _platform.ForegroundWindow.GetWindowTitle(targetHwnd);
            var bounds = _platform.ForegroundWindow.GetWindowBounds(targetHwnd);
            var cursor = _platform.Cursor.GetCursorPosition();
            context = new PlaybackContext(MacroPositionMode.WindowRelative, bounds, title, targetHwnd, cursor);
        } else {
            context = PlaybackContext.Absolute;
        }

        var windowContext = macro.PositionMode == MacroPositionMode.WindowRelative
            ? macro.WindowTitlePattern : null;
        MacroPlaybackWindow?.Show(macro.Name, macro.Steps.Count, windowContext);
        _macroPlayer.StepCompleted += (completed, total) =>
            MacroPlaybackWindow?.UpdateProgress(completed, total);
        _macroPlayer.DelayUpdate += (remainingMs, actionType) =>
            MacroPlaybackWindow?.UpdateDelay(remainingMs, actionType);

        _playbackCts = new CancellationTokenSource();
        _playbackTask = RunPlaybackAsync(macro, context, _playbackCts.Token);
    }

    private async Task RunPlaybackAsync(MacroDefinition macro, PlaybackContext context, CancellationToken ct) {
        PlaybackResult? result = null;
        try {
            result = await _macroPlayer!.Play(macro, context, ct);
        } catch (OperationCanceledException) {
            result = PlaybackResult.Cancelled;
        } catch (Exception ex) {
            LogPlaybackFailed(ex.Message);
            result = PlaybackResult.Cancelled;
        } finally {
            if (!_disposed) {
                OnPlaybackFinished(result ?? PlaybackResult.Cancelled);
            }
        }
    }

    private void OnPlaybackFinished(PlaybackResult result) {
        LogPlaybackFinished(result.Kind);
        MacroPlaybackWindow?.Close();
        _hookService.Disable();
        _macroState = MacroState.Idle;
        _playbackCts?.Dispose();
        _playbackCts = null;
        _playbackTask = null;
        _macroPlayer = null;

        if (result.Kind is PlaybackResultKind.ScreenMismatch
                or PlaybackResultKind.WindowMismatch
                or PlaybackResultKind.CoordinateOutOfBounds
                or PlaybackResultKind.WindowDrift) {
            LogScreenMismatch(result.Message ?? "unknown");
        }

        if (_playbackFromGlobalHotKey || _activeSession is null) {
            DeactivateOverlay();
        } else {
            ResumeOverlayAfterPlayback();
        }
    }

    private void ResumeOverlayAfterPlayback() {
        _origin = _platform.Cursor.GetCursorPosition();
        _screenBounds = _platform.Screen.GetPrimaryScreenBounds();

        try {
            _overlayWindow.Show();
        } catch (InvalidOperationException) {
            return;
        }

        _hookService.Enable();
        _modeLocked = false;

        var defaultModeName = GetDefaultModeName();
        try {
            var session = _sessionFactory.Create(defaultModeName);
            session.ActionRequested += OnSessionActionRequested;
            session.Cancelled += OnSessionCancelled;
            session.CursorMoveRequested += OnSessionCursorMoveRequested;
            _activeSession = session;
            session.Activate(_screenBounds, _origin);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            LogModeSwitchFailed(defaultModeName, ex.Message);
            DeactivateOverlay();
        }
    }

    private void OnPickerClosed() {
        LogPickerClosed(_macroState);
        _macroState = MacroState.Idle;
        if (_activeSession is not null) {
            _overlayWindow.Show();
            _hookService.Enable();
        }
    }

    // --- End macro recording methods ---

    public void Dispose() {
        _disposed = true;
        _playbackCts?.Cancel();
        _playbackTask?.GetAwaiter().GetResult();
        _playbackCts?.Dispose();
        _resumeTimer?.Dispose();
        MacroHotKeyService = null;
        MacroPickerWindow = null;
        MacroPlaybackWindow?.Close();
        if (_macroRecorder is not null) {
            _macroRecorder.RecordingComplete -= OnRecordingComplete;
            _macroRecorder.RecordingCancelled -= OnRecordingCancelled;
            _macroRecorder.SlotSelectionRequested -= OnSlotSelectionRequested;
            _macroRecorder.OverwriteConfirmRequested -= OnOverwriteConfirmRequested;
            _macroRecorder.RecordingStarted -= OnRecordingStarted;
            _macroRecorder.Cancel();
        }
        _hotKeyService.Activated -= OnHotKeyActivated;
        _hookService.KeyEvent -= OnKeyEvent;
        _overlayWindow.FocusLost -= OnFocusLost;
        DeactivateOverlay();
        _debounce.Dispose();
        _hookService.Dispose();
        _overlayWindow.Close();
    }
}
