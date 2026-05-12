using System.Drawing;

using Klikety.Config;
using Klikety.Input;
using Klikety.Services;

using Microsoft.Extensions.Logging;

namespace Klikety.Navigation;

/// <summary>
/// Manages macro recording, playback, and picker lifecycle.
/// Fires intent events for overlay control — coordinator owns the overlay.
/// </summary>
internal sealed partial class MacroHandler : IDisposable {
    private readonly ConfigModel _config;
    private readonly IPlatformServices _platform;
    private readonly IKeyboardHookService _hookService;
    private readonly IMouseActionService _mouseService;
    private readonly SessionManager _sessionManager;
    private readonly IMacroStore? _macroStore;
    private readonly MacroRecorder? _macroRecorder;
    private readonly ILogger _logger;
    private readonly Dictionary<VKey, int> _slotKeyMap = [];

    private MacrosFile _macrosFile;
    private MacroState _macroState = MacroState.Idle;
    private IDebounceTimer? _resumeTimer;

    // Playback state
    private MacroPlayer? _macroPlayer;
    private CancellationTokenSource? _playbackCts;
    private Task? _playbackTask;
    private bool _playbackFromGlobalHotKey;
    private bool _disposed;

    // Recording app-scope persistence
    private bool _recordingAppScoped;
    private Rectangle _recordingWindowBounds;

    // Target HWND — own copy, set by coordinator before each use
    private nint _targetHwnd;

    // Macro picker + hotkey (set after construction)
    private IMacroHotKeyService? _macroHotKeyService;
    private IMacroPickerWindow? _macroPickerWindow;

    public MacroState State => _macroState;
    public bool RecordingAppScoped => _recordingAppScoped;
    public Rectangle RecordingWindowBounds => _recordingWindowBounds;
    public MacroRecorder? Recorder => _macroRecorder;

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

    public IMacroPlaybackWindow? MacroPlaybackWindow { get; set; }
    public IClickIndicator? ClickIndicator { get; set; }
    public IDelayProvider DelayProvider { get; set; } = new TaskDelayProvider();

    // --- Events for overlay control (coordinator subscribes) ---
    public event Action? SuspendOverlayRequested;
    public event Action? ResumeOverlayRequested;
    public event Action? DeactivateRequested;
    public event Action<string>? ShowStatusTextRequested;
    public event Action? ClearStatusTextRequested;
    public event Action<bool>? SetRecordingBorderRequested;

    public MacroHandler(
        ConfigModel config,
        IPlatformServices platform,
        IKeyboardHookService hookService,
        IMouseActionService mouseService,
        SessionManager sessionManager,
        ILogger logger,
        IMacroStore? macroStore,
        MacrosFile? macrosFile) {
        _config = config;
        _platform = platform;
        _hookService = hookService;
        _mouseService = mouseService;
        _sessionManager = sessionManager;
        _logger = logger;
        _macroStore = macroStore;
        _macrosFile = macrosFile ?? new MacrosFile();

        BuildSlotKeyMap();

        if (config.Macros is { Enabled: true }) {
            _macroRecorder = new MacroRecorder(platform.Screen, logger);
            _macroRecorder.RecordingComplete += OnRecordingComplete;
            _macroRecorder.RecordingCancelled += OnRecordingCancelled;
            _macroRecorder.SlotSelectionRequested += OnSlotSelectionRequested;
            _macroRecorder.OverwriteConfirmRequested += OnOverwriteConfirmRequested;
            _macroRecorder.RecordingStarted += OnRecordingStarted;
            _macroRecorder.StartFromCursorRequested += OnStartFromCursorRequested;
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

    /// <summary>
    /// Sets the target HWND for macro operations. Called by coordinator before activation.
    /// </summary>
    public void SetTargetHwnd(nint hwnd) => _targetHwnd = hwnd;

    /// <summary>
    /// Whether macro subsystem is in a state that should suppress focus-loss deactivation.
    /// </summary>
    public bool IsPlayingOrRecording() => _macroState != MacroState.Idle;

    /// <summary>
    /// Tries to handle a key press in the macro subsystem.
    /// Returns true if the key was consumed.
    /// </summary>
    public bool TryHandleKey(VKey key) {
        // Playing: only Escape cancels playback, all else ignored by hook
        if (_macroState == MacroState.Playing) {
            if (key == VKey.Escape) {
                _playbackCts?.Cancel();
            }
            return true;
        }

        // Recording: recording control keys
        if (_macroState == MacroState.Recording && _macroRecorder is not null) {
            if (HandleRecordingKey(key)) {
                return true;
            }
            // During recording in AwaitSlot/AwaitOverwrite, keys are consumed
            if (_macroRecorder.State is MacroRecorderState.AwaitSlot or MacroRecorderState.AwaitOverwrite) {
                return true;
            }
        }

        // Record key: start recording when idle and overlay open
        if (_macroState == MacroState.Idle
            && _sessionManager.IsActive
            && _macroRecorder is not null
            && _config.Macros is { Enabled: true }
            && key == _config.Macros.RecordKey) {
            StartRecording();
            return true;
        }

        // Helper key: open picker when idle and overlay open
        if (_macroState == MacroState.Idle
            && _sessionManager.IsActive
            && _macroPickerWindow is not null
            && _config.Macros is { Enabled: true }
            && key == _config.Macros.HelperKey) {
            ShowMacroPicker();
            return true;
        }

        // Slot key: direct playback when idle and overlay open
        if (_macroState == MacroState.Idle
            && _sessionManager.IsActive
            && _config.Macros is { Enabled: true }
            && _slotKeyMap.TryGetValue(key, out var directSlot)
            && _macrosFile.Macros.Length > directSlot
            && _macrosFile.Macros[directSlot] is { } directMacro) {
            _playbackFromGlobalHotKey = false;
            _hookService.Disable();
            SuspendOverlayRequested?.Invoke();
            StartPlayback(directMacro, _targetHwnd);
            return true;
        }

        return false;
    }

    private void StartRecording() {
        if (_sessionManager.AppScoped) {
            var windowTitle = _platform.ForegroundWindow.GetWindowTitle(_targetHwnd);
            if (string.IsNullOrEmpty(windowTitle)) {
                ShowStatusTextRequested?.Invoke("Window has no title — cannot record");
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
            _macroRecorder!.StartRecording(context);
        } else {
            _macroState = MacroState.Recording;
            _recordingAppScoped = false;
            _recordingWindowBounds = Rectangle.Empty;
            _macroRecorder!.StartRecording();
        }
    }

    public void CancelRecording() {
        _macroRecorder?.Cancel();
        _macroState = MacroState.Idle;
        SetRecordingBorderRequested?.Invoke(false);
        ClearStatusTextRequested?.Invoke();
    }

    private bool HandleRecordingKey(VKey key) {
        var macros = _config.Macros;
        if (macros is null || _macroRecorder is null) {
            return false;
        }

        if (key == VKey.Escape) {
            CancelRecording();
            return true;
        }

        if (key == macros.RecordKey && _macroRecorder.State == MacroRecorderState.Recording) {
            _macroRecorder.StopRecording();
            return true;
        }

        if (_macroRecorder.State == MacroRecorderState.AwaitSlot && _slotKeyMap.TryGetValue(key, out var slot)) {
            _macroRecorder.OnSlotKey(slot, _macrosFile.Macros);
            return true;
        }

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

        if (_macroRecorder.State == MacroRecorderState.AwaitStartFromCursorConfirm) {
            if (key == VKey.Y) {
                _macroRecorder.OnStartFromCursorResponse(true);
                ClearStatusTextRequested?.Invoke();
                ResumeOverlayForRecording();
                return true;
            }
            if (key == VKey.N) {
                _macroRecorder.OnStartFromCursorResponse(false);
                ClearStatusTextRequested?.Invoke();
                ResumeOverlayForRecording();
                return true;
            }
            return true;
        }

        return false;
    }

    public void SuspendOverlayForAction() {
        _sessionManager.DeactivateSession();
        SuspendOverlayRequested?.Invoke();
    }

    public void ScheduleResumeAfterAction() {
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

    public void ResumeOverlayForRecording() {
        if (_macroState != MacroState.Recording) {
            return;
        }

        var origin = _platform.Cursor.GetCursorPosition();
        var screenBounds = _platform.Screen.GetPrimaryScreenBounds();
        _sessionManager.ScreenBounds = screenBounds;

        if (_recordingAppScoped) {
            var newBounds = _platform.ForegroundWindow.GetWindowBounds(_targetHwnd);

            if (newBounds.IsEmpty) {
                _macroRecorder!.StopRecording();
                return;
            }

            if (newBounds.Width != _recordingWindowBounds.Width || newBounds.Height != _recordingWindowBounds.Height) {
                ShowStatusTextRequested?.Invoke("Window resized during recording");
                CancelRecording();
                return;
            }

            _recordingWindowBounds = newBounds;
            origin = new Point(
                Math.Clamp(origin.X, newBounds.Left, newBounds.Right - 1),
                Math.Clamp(origin.Y, newBounds.Top, newBounds.Bottom - 1)
            );
        }

        // Request coordinator to re-show overlay
        ResumeOverlayRequested?.Invoke();

        // Create new session in the same mode that was active before recording
        try {
            _sessionManager.ResumeForRecording(_sessionManager.CurrentModeName, screenBounds, origin,
                _recordingAppScoped, _recordingWindowBounds);

            SetRecordingBorderRequested?.Invoke(true);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            CancelRecording();
        }
    }

    // --- Recorder event handlers ---

    private void OnSlotSelectionRequested() {
        ShowStatusTextRequested?.Invoke("Select slot (0-9):");
    }

    private void OnOverwriteConfirmRequested(int slot, string name) {
        ShowStatusTextRequested?.Invoke($"Slot {slot}: {name}. Overwrite? (Y/N)");
    }

    private void OnRecordingStarted() {
        ClearStatusTextRequested?.Invoke();
        SetRecordingBorderRequested?.Invoke(true);
    }

    private void OnStartFromCursorRequested() {
        ShowStatusTextRequested?.Invoke("Drag from cursor? [Y/N]");
    }

    private void OnRecordingComplete(int slot, MacroDefinition macro) {
        _macroState = MacroState.Idle;
        SetRecordingBorderRequested?.Invoke(false);

        _macrosFile.Macros[slot] = macro;
        if (_macroStore is not null) {
            var result = _macroStore.Save(_macrosFile);
            if (!result.Success) {
                LogMacroSaveFailed(slot, result.Error ?? "unknown error");
            }
        }
    }

    private void OnRecordingCancelled() {
        _macroState = MacroState.Idle;
        SetRecordingBorderRequested?.Invoke(false);
        ClearStatusTextRequested?.Invoke();
    }

    // --- Macro picker methods ---

    private void ShowMacroPicker() {
        LogMacroPickerOpening();
        _playbackFromGlobalHotKey = false;
        _macroState = MacroState.Picking;
        _hookService.Disable();
        SuspendOverlayRequested?.Invoke();
        _macroPickerWindow!.Show(_macrosFile.Macros, _config.Macros.SlotKeys);
        LogMacroPickerShown();
    }

    private void OnMacroHotKeyActivated() {
        if (_macroState != MacroState.Idle || _macroPickerWindow is null) {
            return;
        }

        _playbackFromGlobalHotKey = true;
        _targetHwnd = _platform.ForegroundWindow.GetForegroundWindowHandle();
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

        StartPlayback(macro, _targetHwnd);
    }

    private void OnPickerClosed() {
        LogPickerClosed(_macroState);
        _macroState = MacroState.Idle;
        if (_sessionManager.IsActive) {
            ResumeOverlayRequested?.Invoke();
            _hookService.Enable();
        }
    }

    // --- Playback methods ---

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
            DeactivateRequested?.Invoke();
        } else {
            ResumeOverlayAfterPlayback();
        }
    }

    private void ResumeOverlayAfterPlayback() {
        var origin = _platform.Cursor.GetCursorPosition();
        var screenBounds = _platform.Screen.GetPrimaryScreenBounds();

        ResumeOverlayRequested?.Invoke();
        _hookService.Enable();

        try {
            _sessionManager.ResumeAfterPlayback(_sessionManager.CurrentModeName, screenBounds, origin);
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            DeactivateRequested?.Invoke();
        }
    }

    private static string ExtractTitlePattern(string windowTitle) {
        var lastSep = windowTitle.LastIndexOf(" - ", StringComparison.Ordinal);
        return lastSep >= 0 ? windowTitle[(lastSep + 3)..] : windowTitle;
    }

    // --- Log declarations ---

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
    }
}
