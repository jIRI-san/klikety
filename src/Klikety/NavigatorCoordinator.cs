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

    private bool _deactivating;
    private bool _nonQwertyWarningShown;
    private bool _dragMode;
    private Point _dragStartPoint;

    // App-scope state
    private VKey? _appScopeChordKey;
    private nint _preOverlayHwnd;

    // Recording app-scope persistence
    private bool _recordingAppScoped;
    private Rectangle _recordingWindowBounds;

    private readonly SessionManager _sessionManager;
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
        _sessionManager = new SessionManager(sessionFactory, overlayWindow, logger);

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
        _sessionManager.ActionRequested += OnSessionActionRequested;
        _sessionManager.Cancelled += OnSessionCancelled;
        _sessionManager.CursorMoveRequested += OnSessionCursorMoveRequested;
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
        if (_sessionManager.IsActive) {
            return;
        }

        LogHotkeyActivated();

        var screenBounds = _platform.Screen.GetPrimaryScreenBounds();
        var origin = _platform.Cursor.GetCursorPosition();

        // Multi-monitor guardrail: cursor outside primary screen → suppress
        if (!screenBounds.Contains(origin)) {
            LogCursorOutsidePrimary(origin.X, origin.Y);
            return;
        }

        // Determine default mode, with non-QWERTY fallback
        var defaultModeName = GetDefaultModeName();

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

        // Create and activate default mode session
        try {
            _sessionManager.ActivateDefaultSession(screenBounds, origin, defaultModeName);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            LogActivationFailed(defaultModeName, ex.Message);
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
            && _sessionManager.IsActive
            && _macroRecorder is not null
            && _config.Macros is { Enabled: true }
            && e.Key == _config.Macros.RecordKey) {
            if (_sessionManager.AppScoped) {
                var windowTitle = _platform.ForegroundWindow.GetWindowTitle(_preOverlayHwnd);
                if (string.IsNullOrEmpty(windowTitle)) {
                    _overlayWindow.ShowStatusText("Window has no title — cannot record");
                    return;
                }

                var titlePattern = ExtractTitlePattern(windowTitle);
                var context = new MacroRecordingContext(
                    IsWindowRelative: true,
                    WindowWidth: _sessionManager.AppScopeBounds.Width,
                    WindowHeight: _sessionManager.AppScopeBounds.Height,
                    WindowTitlePattern: titlePattern,
                    DpiScale: _platform.Screen.GetDpiScale()
                );

                _macroState = MacroState.Recording;
                _recordingAppScoped = true;
                _recordingWindowBounds = _sessionManager.AppScopeBounds;
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
            && _sessionManager.IsActive
            && _macroPickerWindow is not null
            && _config.Macros is { Enabled: true }
            && e.Key == _config.Macros.HelperKey) {
            ShowMacroPicker();
            return;
        }

        // (6b) Slot key: direct playback when idle and overlay open
        if (_macroState == MacroState.Idle
            && _sessionManager.IsActive
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
        if (!_sessionManager.IsModeLocked && _appScopeChordKey is { } appScopeKey && e.Key == appScopeKey) {
            if (_sessionManager.AppScoped) {
                return; // Auto-repeat guard
            }

            var hwndBounds = _platform.ForegroundWindow.GetWindowBounds(_preOverlayHwnd);

            if (_preOverlayHwnd == 0 || hwndBounds.IsEmpty) {
                LogAppScopeRejected("invalid or minimized window");
                _overlayWindow.ShowStatusText("Invalid window");
                return;
            }

            // Clip to primary screen
            var clipped = Rectangle.Intersect(hwndBounds, _sessionManager.ScreenBounds);
            if (clipped.IsEmpty) {
                LogAppScopeRejected("window outside primary screen");
                _overlayWindow.ShowStatusText("Window outside screen");
                return;
            }

            // Clamp origin into clipped bounds
            var cursor = _platform.Cursor.GetCursorPosition();
            var origin = new Point(
                Math.Clamp(cursor.X, clipped.Left, clipped.Right - 1),
                Math.Clamp(cursor.Y, clipped.Top, clipped.Bottom - 1));

            LogAppScopeActivated(clipped.Width, clipped.Height);
            try {
                _sessionManager.SwitchToAppScope(clipped, origin);
            } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
                DeactivateOverlay();
            }
            return;
        }

        // (8) Chord dispatch: before mode lock, chord key switches mode
        if (!_sessionManager.IsModeLocked && _chordKeyMap.TryGetValue(e.Key, out var targetMode)) {
            // Non-QWERTY check for chord target
            if (targetMode != "UniformGrid" && !IsQwertyLayout()) {
                if (!_nonQwertyWarningShown) {
                    _nonQwertyWarningShown = true;
                    LogNonQwertyFallback(targetMode);
                }
                // Ignore chord — stay on current mode
                return;
            }

            try {
                _sessionManager.SwitchMode(targetMode);
            } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
                DeactivateOverlay();
            }
            return;
        }

        // Any key forwarded to session locks the mode
        _sessionManager.ForwardKey(e.Key);
    }

    private void OnFocusLost(object? sender, EventArgs e) {
        LogFocusLost();

        // Suppress deactivation during mode/app-scope switching (WPF fires Deactivated on Hide)
        if (_sessionManager.IsSwitching) {
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
        if (!_sessionManager.ActiveBounds.Contains(point)) {
            LogActionOutOfBounds(point.X, point.Y);
            _mouseService.MoveTo(_sessionManager.Origin);
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
        try {
            _sessionManager.ResetForDrag(GetDefaultModeName(), _dragStartPoint,
                _recordingAppScoped, _recordingWindowBounds);
            _overlayWindow.ShowStatusText("Select drag target");
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            _dragMode = false;
            _overlayWindow.ClearStatusText();
            DeactivateOverlay();
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
            _mouseService.MoveTo(_sessionManager.Origin);
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

            _sessionManager.DeactivateSession();

            // Drag-mode cleanup: restore cursor to origin on focus-loss or unexpected deactivation
            if (_dragMode) {
                _mouseService.MoveTo(_sessionManager.Origin);
                _dragMode = false;
            }

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Activation failed for mode {Mode}: {Error}")]
    private partial void LogActivationFailed(string mode, string error);

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
        _sessionManager.DeactivateSession();

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

        var origin = _platform.Cursor.GetCursorPosition();
        var screenBounds = _platform.Screen.GetPrimaryScreenBounds();
        _sessionManager.ScreenBounds = screenBounds;

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
            origin = new Point(
                Math.Clamp(origin.X, newBounds.Left, newBounds.Right - 1),
                Math.Clamp(origin.Y, newBounds.Top, newBounds.Bottom - 1)
            );
        }

        // Re-show overlay
        try {
            _overlayWindow.Show();
        } catch (InvalidOperationException) {
            CancelRecording();
            return;
        }

        // Create new default-mode session with current cursor as origin
        var defaultModeName = GetDefaultModeName();
        try {
            _sessionManager.ResumeForRecording(defaultModeName, screenBounds, origin,
                _recordingAppScoped, _recordingWindowBounds);

            _overlayWindow.SetRecordingBorder(true);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
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

        if (_playbackFromGlobalHotKey || !_sessionManager.IsActive) {
            DeactivateOverlay();
        } else {
            ResumeOverlayAfterPlayback();
        }
    }

    private void ResumeOverlayAfterPlayback() {
        var origin = _platform.Cursor.GetCursorPosition();
        var screenBounds = _platform.Screen.GetPrimaryScreenBounds();

        try {
            _overlayWindow.Show();
        } catch (InvalidOperationException) {
            return;
        }

        _hookService.Enable();

        var defaultModeName = GetDefaultModeName();
        try {
            _sessionManager.ResumeAfterPlayback(defaultModeName, screenBounds, origin);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            DeactivateOverlay();
        }
    }

    private void OnPickerClosed() {
        LogPickerClosed(_macroState);
        _macroState = MacroState.Idle;
        if (_sessionManager.IsActive) {
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
        _sessionManager.ActionRequested -= OnSessionActionRequested;
        _sessionManager.Cancelled -= OnSessionCancelled;
        _sessionManager.CursorMoveRequested -= OnSessionCursorMoveRequested;
        DeactivateOverlay();
        _debounce.Dispose();
        _sessionManager.Dispose();
        _hookService.Dispose();
        _overlayWindow.Close();
    }
}
