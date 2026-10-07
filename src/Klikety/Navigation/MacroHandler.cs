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
    private sealed class PlaybackOperation(MacroPlayer player) {
        private readonly object _gate = new();
        private bool _released;
        public MacroPlayer Player { get; } = player;
        public CancellationTokenSource Cancellation { get; } = new();
        public SynchronizationContext? Context { get; } = SynchronizationContext.Current;
        public Task? Task { get; set; }

        public void Cancel() {
            lock (_gate) {
                if (!_released) {
                    Cancellation.Cancel();
                }
            }
        }

        public void Release() {
            lock (_gate) {
                _released = true;
                Cancellation.Dispose();
            }
        }

        public void OnContext(Action action) {
            if (Context is null || ReferenceEquals(Context, SynchronizationContext.Current)) {
                action();
            } else {
                Context.Post(_ => action(), null);
            }
        }
    }

    private PlaybackOperation? _playback;
    internal Task? PlaybackTask => _playback?.Task;
    private bool _playbackFromGlobalHotKey;
    private volatile bool _disposed;

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
    public IReadOnlyList<MacroDefinition?> MacroSlots => _macrosFile.Macros;

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
                _playback?.Cancel();
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

        bool navigationVisible = _sessionManager.IsActive;
        _playbackFromGlobalHotKey = true;
        if (!navigationVisible) {
            _targetHwnd = _platform.ForegroundWindow.GetForegroundWindowHandle();
        }
        _macroState = MacroState.Picking;
        if (navigationVisible) {
            _hookService.Disable();
            SuspendOverlayRequested?.Invoke();
        }
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

        var player = new MacroPlayer(_mouseService, _platform.Screen, DelayProvider,
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
        var operation = new PlaybackOperation(player);
        _playback = operation;
        player.StepCompleted += (completed, total) => operation.OnContext(() => {
            if (!_disposed && ReferenceEquals(_playback, operation)) {
                MacroPlaybackWindow?.UpdateProgress(completed, total);
            }
        });
        player.DelayUpdate += (remainingMs, actionType) => operation.OnContext(() => {
            if (!_disposed && ReferenceEquals(_playback, operation)) {
                MacroPlaybackWindow?.UpdateDelay(remainingMs, actionType);
            }
        });
        operation.Task = RunPlaybackAsync(operation, macro, context);
    }

    private async Task RunPlaybackAsync(PlaybackOperation operation, MacroDefinition macro, PlaybackContext context) {
        PlaybackResult? result = null;
        try {
            result = await operation.Player.Play(macro, context, operation.Cancellation.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (operation.Cancellation.IsCancellationRequested) {
            result = PlaybackResult.Cancelled;
        } catch (Exception ex) {
            result = new PlaybackResult { Kind = PlaybackResultKind.InputFailed, Message = ex.Message };
        } finally {
            if (result?.Kind == PlaybackResultKind.InputFailed) {
                LogPlaybackFailed(result.Message ?? "Native input failed without diagnostics");
            }
            operation.Release();
            if (_disposed) {
                Interlocked.CompareExchange(ref _playback, null, operation);
            } else {
                operation.OnContext(() => {
                    if (ReferenceEquals(_playback, operation)) {
                        _playback = null;
                        if (!_disposed) {
                            OnPlaybackFinished(result ?? PlaybackResult.Cancelled);
                        }
                    }
                });
            }
        }
    }

    private void OnPlaybackFinished(PlaybackResult result) {
        LogPlaybackFinished(result.Kind);
        MacroPlaybackWindow?.Close();
        _hookService.Disable();
        _macroState = MacroState.Idle;

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
        if (_disposed) {
            return;
        }
        _disposed = true;
        var operation = Interlocked.Exchange(ref _playback, null);
        operation?.Cancel();
        ClickIndicator?.Dispose();
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
