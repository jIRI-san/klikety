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

    /// <summary>Debounce timeout duration.</summary>
    private static readonly TimeSpan DebounceTimeout = TimeSpan.FromMilliseconds(500);

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

        BuildChordKeyMap();
        BuildSlotKeyMap();

        if (config.Macros is { Enabled: true }) {
            _macroRecorder = new MacroRecorder(platform.Screen, logger);
            _macroRecorder.RecordingComplete += OnRecordingComplete;
            _macroRecorder.RecordingCancelled += OnRecordingCancelled;
            _macroRecorder.SlotSelectionRequested += OnSlotSelectionRequested;
            _macroRecorder.OverwriteConfirmRequested += OnOverwriteConfirmRequested;
            _macroRecorder.RecordingStarted += OnRecordingStarted;
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

        // Populate debounce keys BEFORE hook enable (closes TOCTOU)
        PopulateDebounceKeys();

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
        StartDebounceTimer();

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

        // Trigger-key fast removal: on first keydown for a different key,
        // remove trigger only if no longer physically held
        if (_debounceKeys.Contains(_config.HotKey.Key)
            && !_platform.KeyState.IsKeyDown(_config.HotKey.Key)) {
            _debounceKeys.Remove(_config.HotKey.Key);
        }

        LogKeyPressed(e.Key);

        // --- Macro key dispatch priority ---

        // (2) Playing: only Escape cancels playback, all else ignored by hook
        if (_macroState == MacroState.Playing) {
            if (e.Key == VKey.Escape) {
                // TODO: cancel playback (Phase 5)
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
            _macroState = MacroState.Recording;
            _macroRecorder.StartRecording();
            return;
        }

        // (6) Helper key: open picker when idle and overlay open (Phase 4)
        // TODO: implement in Phase 4

        // (7) Chord dispatch: before mode lock, chord key switches mode
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
            session.Activate(_screenBounds, _origin);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            LogModeSwitchFailed(targetModeName, ex.Message);
            DeactivateOverlay();
        } finally {
            _switching = false;
        }
    }

    private void OnFocusLost(object? sender, EventArgs e) {
        LogFocusLost();

        // Suppress deactivation during recording or playback
        if (_macroState != MacroState.Idle) {
            return;
        }

        DeactivateOverlay();
    }

    private void OnSessionActionRequested(Point point, MouseAction action) {
        LogActionRequested(action, point.X, point.Y);

        // Bounds validation
        if (!_screenBounds.Contains(point)) {
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

            // Drag phase 2: completing a drag during recording
            if (_dragMode) {
                if (action is MouseAction.MoveOnly or MouseAction.DragDrop) {
                    LogDragInvalidAction(action);
                    return;
                }

                _macroRecorder.RecordAction(point, action, modifiers);
                _overlayWindow.ClearStatusText();
                _dragMode = false;
                SuspendOverlayForAction();
                _mouseService.SendDrag(_dragStartPoint, point, action, modifiers);
                ScheduleResumeAfterAction();
                return;
            }

            // Drag phase 1: starting a drag during recording
            if (action == MouseAction.DragDrop) {
                _macroRecorder.RecordAction(point, MouseAction.DragDrop, modifiers);
                _dragStartPoint = point;
                _dragMode = true;
                ResetOverlayForDrag();
                return;
            }

            // Normal action during recording
            _macroRecorder.RecordAction(point, action, modifiers);
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

        // Create and activate new default-mode session
        var defaultModeName = GetDefaultModeName();
        try {
            var session = _sessionFactory.Create(defaultModeName);

            session.ActionRequested += OnSessionActionRequested;
            session.Cancelled += OnSessionCancelled;
            session.CursorMoveRequested += OnSessionCursorMoveRequested;

            _activeSession = session;
            session.Activate(_screenBounds, _dragStartPoint);

            _overlayWindow.ShowStatusText("Select drag target");
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            LogModeSwitchFailed(defaultModeName, ex.Message);
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

            // Drag-mode cleanup: restore cursor to origin on focus-loss or unexpected deactivation
            if (_dragMode) {
                _mouseService.MoveTo(_origin);
                _dragMode = false;
            }

            _modeLocked = false;

            _overlayWindow.ClearCanvas();
            _overlayWindow.ClearStatusText();
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drag mode: invalid action {Action} — ignored")]
    private partial void LogDragInvalidAction(MouseAction action);

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
            session.Activate(_screenBounds, _origin);

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

    // --- End macro recording methods ---

    public void Dispose() {
        _resumeTimer?.Dispose();
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
        _hookService.Dispose();
        _overlayWindow.Close();
    }
}
