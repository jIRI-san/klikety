using System.Drawing;
using System.Windows.Threading;

using Klikety.Automation;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Overlay;
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
    private readonly OverlayHost _overlayHost;
    private readonly DisplayTopologyStore? _topologyStore;
    private readonly ConfigModel _config;
    private readonly ILogger _logger;
    private readonly ModeSessionFactory _sessionFactory;
    private readonly IPlatformServices _platform;
    private readonly Func<nint, IKeyLabelResolver> _keyLabelResolverFactory;
    private readonly IElementPointGuard _elementPointGuard;
    private CancellationTokenSource? _pendingElementAction;
    private ActionModifiers _eventModifiers;
    public event Action<string>? RuntimeNotification;
    public event Action<bool>? ElementValidationChanged;
    public bool IsElementValidationPending => _pendingElementAction is not null;

    private bool _deactivating;
    private bool _hostBusy;
    private bool _nonQwertyWarningShown;
    private bool _helpVisible;
    private readonly bool _helpBindingValid;
    private readonly HashSet<VKey> _helpLatchedKeys = [];
    private nint _lastKeyboardLayout;
    private IReadOnlyList<DisplayInfo> _displays = [];
    private DisplayInfo? _navDisplay;
    private IReadOnlyDictionary<string, int> _displayNumbers =
        new Dictionary<string, int>(StringComparer.Ordinal);

    // App-scope state
    private VKey? _appScopeChordKey;
    private nint _preOverlayHwnd;

    private readonly SessionManager _sessionManager;
    private readonly DebounceHandler _debounce;
    private readonly ActionDispatcher _actionDispatcher;
    private readonly MacroHandler _macroHandler;

    // Chord key lookup: VKey → mode name
    private readonly Dictionary<VKey, string> _chordKeyMap = [];

    // Proxy properties for macro subsystem (set by App.xaml.cs after construction)
    public IMacroHotKeyService? MacroHotKeyService {
        get => _macroHandler.MacroHotKeyService;
        set => _macroHandler.MacroHotKeyService = value;
    }
    public IMacroPickerWindow? MacroPickerWindow {
        get => _macroHandler.MacroPickerWindow;
        set => _macroHandler.MacroPickerWindow = value;
    }
    public IMacroPlaybackWindow? MacroPlaybackWindow {
        get => _macroHandler.MacroPlaybackWindow;
        set => _macroHandler.MacroPlaybackWindow = value;
    }
    public IClickIndicator? ClickIndicator {
        get => _macroHandler.ClickIndicator;
        set => _macroHandler.ClickIndicator = value;
    }
    public IDelayProvider DelayProvider {
        get => _macroHandler.DelayProvider;
        set => _macroHandler.DelayProvider = value;
    }
    public bool IsIdle => !_hostBusy && !_sessionManager.IsActive && !_macroHandler.IsPlayingOrRecording();

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
        MacrosFile? macrosFile = null,
        Func<ISatelliteOverlay>? satelliteFactory = null,
        DisplayTopologyStore? topologyStore = null,
        Func<nint, IKeyLabelResolver>? keyLabelResolverFactory = null,
        IElementPointGuard? elementPointGuard = null) {
        _hotKeyService = hotKeyService;
        _hookService = hookService;
        _mouseService = mouseService;
        _overlayWindow = overlayWindow;
        _overlayHost = new OverlayHost(
            overlayWindow,
            satelliteFactory ?? (() => new NullSatelliteOverlay()),
            logger);
        _topologyStore = topologyStore;
        _sessionFactory = sessionFactory;
        _platform = platform;
        _elementPointGuard = elementPointGuard ?? new ElementPointGuard();
        _config = config;
        _logger = logger;
        _keyLabelResolverFactory = keyLabelResolverFactory ?? (layout => new Win32KeyLabelResolver(layout));
        var helpBindingError = HelpBindingPolicy.GetInvalidReason(config);
        _helpBindingValid = config.HelpBinding?.Enabled == true && helpBindingError is null;
        if (helpBindingError is not null) {
            LogInvalidHelpBinding(helpBindingError);
        }

        _debounce = new DebounceHandler(platform, config);
        _sessionManager = new SessionManager(sessionFactory, overlayWindow, logger);
        _actionDispatcher = new ActionDispatcher(mouseService, modifierDetector, overlayWindow, _sessionManager, DeactivateOverlay, logger);
        _macroHandler = new MacroHandler(config, platform, hookService, mouseService, _sessionManager, logger, macroStore, macrosFile);

        // Subscribe to macro handler overlay-control events
        _macroHandler.SuspendOverlayRequested += OnMacroSuspendOverlay;
        _macroHandler.ResumeOverlayRequested += OnMacroResumeOverlay;
        _macroHandler.DeactivateRequested += DeactivateOverlay;
        _macroHandler.ShowStatusTextRequested += _overlayWindow.ShowStatusText;
        _macroHandler.ClearStatusTextRequested += OnMacroClearStatusText;
        _macroHandler.SetRecordingBorderRequested += _overlayWindow.SetRecordingBorder;

        BuildChordKeyMap();

        _appScopeChordKey = config.AppScope.ChordKey;

        _hotKeyService.Activated += OnHotKeyActivated;
        _hookService.KeyEvent += OnKeyEvent;
        _overlayWindow.FocusLost += OnFocusLost;
        _overlayWindow.DisplayChanged += OnDisplayChanged;
        _overlayWindow.KeyboardLayoutChanged += OnKeyboardLayoutChanged;
        _overlayWindow.ViewportChanged += OnViewportChanged;
        _sessionManager.ActionRequested += OnSessionActionRequested;
        _sessionManager.Cancelled += OnSessionCancelled;
        _sessionManager.CursorMoveRequested += OnSessionCursorMoveRequested;
        _sessionManager.GridFallbackRequested += OnElementGridFallback;
        _sessionManager.FailureReported += NotifyElementFailure;
        _sessionManager.ElementHintsStateChanged += OnElementHintsStateChanged;
    }

    private void BuildChordKeyMap() {
        if (_sessionFactory.IsElementHintsAvailable && _config.Modes.ElementHints.ChordKey is { } hintsChord) {
            _chordKeyMap[hintsChord] = "ElementHints";
        }
        if (_config.Modes.UniformGrid is { Enabled: true, ChordKey: { } uniformChord }) {
            _chordKeyMap[uniformChord] = "UniformGrid";
        }
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

    private void OnHotKeyActivated(object? sender, EventArgs e) {
        // Toggle: visible overlay → dismiss. Stale session (nav gone, satellite leftover) →
        // clean up then activate on the cursor display.
        if (_sessionManager.IsActive) {
            bool visible = _overlayWindow.IsVisible;
            DeactivateOverlay();
            if (visible) {
                return;
            }
        }

        LogHotkeyActivated();

        var catalog = _platform.DisplayCatalog.GetSnapshot();
        if (!catalog.Success || catalog.Snapshot is null) {
            LogDisplayCatalogFailed(catalog.FailureReason ?? "unknown");
            return;
        }

        var origin = _platform.Cursor.GetCursorPosition();
        var display = catalog.Snapshot.FindContaining(origin);
        LogActivationTarget(
            origin.X, origin.Y,
            display?.GdiName ?? "(none)",
            display?.MonitorBounds.X ?? 0,
            display?.MonitorBounds.Y ?? 0,
            display?.MonitorBounds.Width ?? 0,
            display?.MonitorBounds.Height ?? 0,
            catalog.Snapshot.Displays.Count);
        if (display is null) {
            LogCursorOutsideDisplays(origin.X, origin.Y);
            return;
        }

        var screenBounds = display.MonitorBounds;

        // Determine default mode, with non-QWERTY fallback
        var defaultModeName = GetDefaultModeName();
        RefreshKeyboardLayoutIfChanged(redraw: false);

        // Populate debounce keys BEFORE hook enable (closes TOCTOU)
        _debounce.PopulateFromHotKey();

        // Capture foreground window HWND before Show() — overlay becomes foreground after Show()
        _preOverlayHwnd = _platform.ForegroundWindow.GetForegroundWindowHandle();
        _sessionManager.TargetContext = _elementPointGuard.Capture(_preOverlayHwnd, Environment.ProcessId);
        _macroHandler.SetTargetHwnd(_preOverlayHwnd);

        var layoutPending = false;
        try {
            _hostBusy = true;
            _displays = catalog.Snapshot.Displays;
            _navDisplay = display;
            _displayNumbers = _topologyStore?.Resolve(_displays)
                ?? DisplayNumbering.AssignSpatially(_displays);
            _overlayHost.Show(_displays, display, _displayNumbers, defaultModeName != "ElementHints");

            if (!_overlayWindow.IsVisible) {
                DeactivateOverlay();
                return;
            }

            if (!_hookService.Enable()) {
                LogHookInstallFailed();
                DeactivateOverlay();
                return;
            }

            _debounce.StartTimer();
            layoutPending = true;
            AfterHostLayout(() => {
                try {
                    _sessionManager.ActivateDefaultSession(screenBounds, origin, defaultModeName);
                } catch (Exception ex) when (
                    ex is NotSupportedException or ArgumentException or InvalidOperationException) {
                    LogActivationFailed(defaultModeName, ex.Message);
                    DeactivateOverlay();
                }
            });
        } catch (InvalidOperationException) {
            LogHookInstallFailed();
            DeactivateOverlay();
        } finally {
            if (!layoutPending) {
                _hostBusy = false;
            }
        }
    }

    private string GetDefaultModeName() {
        var modes = _config.Modes;

        // Identify the configured default mode
        string defaultMode;
        if (modes.ElementHints is { Default: true, Enabled: true } && _sessionFactory.IsElementHintsAvailable) {
            defaultMode = "ElementHints";
        } else if (modes.LogGrid is { Default: true, Enabled: true } && _sessionFactory.IsLogGridAvailable) {
            defaultMode = "LogGrid";
        } else if (modes.Crosshair is { Default: true, Enabled: true }) {
            defaultMode = "Crosshair";
        } else if (modes.LogCrosshair is { Default: true, Enabled: true }) {
            defaultMode = "LogCrosshair";
        } else {
            defaultMode = "UniformGrid";
        }

        // Non-QWERTY fallback for non-UniformGrid defaults
        if (defaultMode is not ("UniformGrid" or "ElementHints") && !IsQwertyLayout()) {
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
            _debounce.Remove(e.Key);
            _helpLatchedKeys.Remove(e.Key);
            return;
        }
        LogHintDigit(e.Key, _pendingElementAction is not null ? "pending-action" :
            _helpLatchedKeys.Contains(e.Key) ? "help-latched" : _debounce.Contains(e.Key) ? "debounced" : "received");
        if (_pendingElementAction is not null) { return; }
        _eventModifiers = (ActionModifiers)((int)e.Modifiers & 7);

        if (_helpLatchedKeys.Contains(e.Key)) {
            return;
        }

        // Debounce suppression: ignore keys still in debounce set
        if (_debounce.Contains(e.Key)) {
            return;
        }

        // Trigger-key fast removal: on first keydown for a different key,
        // remove trigger only if no longer physically held
        _debounce.RemoveTriggerIfReleased();

        if (_sessionManager.IsActive) {
            RefreshKeyboardLayoutIfChanged(redraw: true);
        }

        LogKeyPressed(e.Key);

        if (_helpBindingValid && _overlayWindow.IsVisible && _sessionManager.IsActive &&
            HelpBindingPolicy.Matches(_config.HelpBinding, e.Key, e.Modifiers)) {
            _helpLatchedKeys.Add(e.Key);
            ToggleHelp();
            return;
        }

        if (_helpVisible) {
            if (e.Key == VKey.Escape) {
                _helpLatchedKeys.Add(e.Key);
                CloseHelp();
                return;
            }
            if (e.Key is VKey.Shift or VKey.LShift or VKey.RShift or
                VKey.Control or VKey.LControl or VKey.RControl or
                VKey.Menu or VKey.LMenu or VKey.RMenu or VKey.LWin or VKey.RWin) {
                return;
            }
            CloseHelp();
        }

        if (_sessionManager.IsActive && _sessionManager.ActiveSession is not ElementHintsSession &&
            TryHandleDisplayDigit(e.Key)) {
            return;
        }

        // Macro subsystem gets first priority
        if (_macroHandler.TryHandleKey(e.Key)) {
            LogHintDigit(e.Key, "macro-consumed");
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
            if (targetMode is not ("UniformGrid" or "ElementHints") && !IsQwertyLayout()) {
                if (!_nonQwertyWarningShown) {
                    _nonQwertyWarningShown = true;
                    LogNonQwertyFallback(targetMode);
                }
                // Ignore chord — stay on current mode
                return;
            }

            try {
                _sessionManager.SwitchMode(targetMode);
                _overlayHost.SetDisplayNumbersEnabled(_sessionManager.ActiveSession is not ElementHintsSession);
            } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
                DeactivateOverlay();
            }
            return;
        }

        // Any key forwarded to session locks the mode
        LogHintDigit(e.Key, "forward-session");
        _sessionManager.ForwardKey(e.Key);
    }

    private void LogHintDigit(VKey key, string route) {
        if (key is >= VKey.D1 and <= VKey.D9 && _sessionManager.ActiveSession is ElementHintsSession hints) {
            LogHintDigitRoute(hints.DiagnosticId, key, route, _helpVisible, _macroHandler.State);
        }
    }
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} digit-route key={Key} route={Route} helpVisible={HelpVisible} macroState={MacroState}")]
    private partial void LogHintDigitRoute(Guid activation, VKey key, string route, bool helpVisible, MacroState macroState);

    private static int? DisplayNumberFromKey(VKey key) {
        int n = key - VKey.D1 + 1;
        return n is >= 1 and <= 9 ? n : null;
    }

    private bool TryHandleDisplayDigit(VKey key) {
        if (DisplayNumberFromKey(key) is not int digit) {
            return false;
        }

        DisplayInfo? target = null;
        foreach (var display in _displays) {
            if (_displayNumbers.TryGetValue(display.DevicePath, out int number) && number == digit) {
                target = display;
                break;
            }
        }

        if (target is null || _navDisplay is null ||
            string.Equals(target.DevicePath, _navDisplay.DevicePath, StringComparison.Ordinal)) {
            return true;
        }

        SwitchNavigationDisplay(target);
        return true;
    }

    private void SwitchNavigationDisplay(DisplayInfo target) {
        CancelElementAction();
        var center = new Point(
            target.MonitorBounds.X + target.MonitorBounds.Width / 2,
            target.MonitorBounds.Y + target.MonitorBounds.Height / 2);
        _hostBusy = true;
        try {
            _actionDispatcher.CancelDrag();
            _navDisplay = target;
            _overlayWindow.ClearCanvas();
            _overlayHost.Show(_displays, target, _displayNumbers);
            InputResultObserver.Observe(_mouseService.MoveTo(center), _logger);
            AfterHostLayout(() => {
                if (!_overlayWindow.IsVisible) {
                    DeactivateOverlay();
                    return;
                }

                _sessionManager.RestartOnDisplay(target.MonitorBounds, center);
            });
        } catch {
            _hostBusy = false;
            throw;
        }
    }

    private void AfterHostLayout(Action action) {
        void Run() {
            try {
                action();
            } finally {
                _hostBusy = false;
            }
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) {
            Run();
            return;
        }

        dispatcher.InvokeAsync(Run, DispatcherPriority.Loaded);
    }

    private void OnDisplayChanged(object? sender, EventArgs e) {
        if (_sessionManager.IsActive) {
            DeactivateOverlay();
        }
    }

    private void OnKeyboardLayoutChanged(object? sender, EventArgs e) =>
        RefreshKeyboardLayoutIfChanged(redraw: true);

    private void OnViewportChanged(object? sender, OverlayViewportChangedEventArgs e) {
        _overlayWindow.RelayoutHelp();
        if (!_hostBusy && _overlayWindow.IsVisible && _sessionManager.ActiveSession is ElementHintsSession hints) {
            hints.Relayout();
        }
    }

    private void RefreshKeyboardLayoutIfChanged(bool redraw) {
        var keyboardLayout = _platform.KeyboardLayout.GetActiveKeyboardLayout();
        if (keyboardLayout == _lastKeyboardLayout) {
            return;
        }

        _sessionFactory.RebuildLabels(_keyLabelResolverFactory(keyboardLayout));
        _lastKeyboardLayout = keyboardLayout;

        if (redraw) {
            _sessionManager.RedrawActiveSession();
        }

        if (_helpVisible && (!redraw || _sessionManager.ActiveSession is not ElementHintsSession)) {
            _overlayWindow.UpdateHelp(BuildHelpContent());
        }
    }

    private void OnElementHintsStateChanged() {
        _overlayHost.SetDisplayNumbersEnabled(false);
        if (_helpVisible) {
            _overlayWindow.UpdateHelp(BuildHelpContent());
        }
    }

    private HelpOverlayContent BuildHelpContent() =>
        HelpOverlayContentBuilder.Build(
            _config,
            _keyLabelResolverFactory(_lastKeyboardLayout),
            _sessionFactory.IsLogGridAvailable,
            _actionDispatcher.IsDragMode,
            _macroHandler.State,
            _macroHandler.Recorder?.State,
            _macroHandler.Recorder?.SelectedSlot ?? -1,
            _macroHandler.Recorder?.RecordedStepCount ?? 0,
            _macroHandler.MacroSlots,
            _displayNumbers,
            _navDisplay?.DevicePath,
            _macroHandler.MacroPickerWindow is not null,
            _sessionManager.IsModeLocked,
            _sessionManager.AppScoped,
            _helpBindingValid,
            _sessionFactory.IsElementHintsAvailable,
            _sessionManager.ActiveSession is ElementHintsSession hints ? hints.HelpState : null);

    private void ToggleHelp() {
        if (_helpVisible) {
            CloseHelp();
            return;
        }

        _helpVisible = true;
        _overlayWindow.ShowHelp(BuildHelpContent());
    }

    private void CloseHelp() {
        _helpVisible = false;
        _overlayWindow.HideHelp();
    }

    private void ClearHelpState() {
        _helpVisible = false;
        _helpLatchedKeys.Clear();
        _overlayWindow.HideHelp();
    }

    private void OnFocusLost(object? sender, EventArgs e) {
        LogFocusLostDetail(
            _hostBusy,
            _sessionManager.IsSwitching,
            _overlayWindow.IsVisible,
            _sessionManager.IsActive,
            _macroHandler.State != MacroState.Idle);

        // Suppress deactivation during mode/app-scope switching (WPF fires Deactivated on Hide)
        if (_sessionManager.IsSwitching || _hostBusy) {
            LogFocusLostSuppressed();
            return;
        }

        // Suppress deactivation during recording or playback
        if (_macroHandler.IsPlayingOrRecording()) {
            return;
        }

        DeactivateOverlay();
    }

    private void OnSessionActionRequested(Point point, MouseAction action) {
        if (_sessionManager.ActiveSession is ElementHintsSession hints) {
            if (_pendingElementAction is null) {
                _ = ValidateAndDispatchElementActionAsync(hints, action, _eventModifiers);
            }
            return;
        }
        DispatchSessionAction(point, action);
    }

    private void DispatchSessionAction(Point point, MouseAction action, ActionModifiers? modifiers = null) {
        // Recording path
        if (_macroHandler.State == MacroState.Recording && _macroHandler.Recorder is not null) {
            var result = _actionDispatcher.HandleRecordingAction(point, action,
                _macroHandler.Recorder, _macroHandler.RecordingAppScoped, _macroHandler.RecordingWindowBounds, modifiers);
            switch (result) {
                case RecordingActionResult.SuspendAndResume:
                    _macroHandler.SuspendOverlayForAction();
                    _macroHandler.ScheduleResumeAfterAction();
                    break;
                case RecordingActionResult.ResetForDrag:
                    ResetOverlayForDrag();
                    break;
                case RecordingActionResult.ResumeRecording:
                    _macroHandler.ResumeOverlayForRecording();
                    break;
            }
            return;
        }

        // Normal path
        if (!_actionDispatcher.HandleAction(point, action, modifiers)) {
            // DragDrop phase 1 — reset overlay for drag target selection
            ResetOverlayForDrag();
        }
    }

    private async Task ValidateAndDispatchElementActionAsync(ElementHintsSession hints,
        MouseAction action, ActionModifiers modifiers) {
        var intent = new CancellationTokenSource();
        _pendingElementAction = intent;
        try {
            ElementValidationChanged?.Invoke(true);
            _hostBusy = true;
            ClearHelpState();
            _hookService.DrainAndDisable();
            _overlayHost.Hide();
            _platform.ForegroundWindow.SetForegroundWindow(hints.Context.Hwnd);
            _hostBusy = false;
            var response = await hints.ValidateAsync(intent.Token, moveOnly: action == MouseAction.MoveOnly);
            if (intent.IsCancellationRequested || _pendingElementAction != intent ||
                _sessionManager.ActiveSession != hints) { return; }
            if (response.Outcome != HintOutcome.Success || response.Point is not { } point ||
                !hints.Selected!.VisibleBounds.Contains(point) ||
                !_elementPointGuard.IsCurrent(hints.Context, hints.RootProcessId, point)) {
                if (_macroHandler.State == MacroState.Recording) { _macroHandler.CancelRecording(); }
                _mouseService.MoveTo(_sessionManager.Origin);
                DeactivateOverlay();
                NotifyElementFailure($"Element action rejected: {response.Reason ?? response.Outcome.ToString()}. Reopen or use grid.");
                return;
            }
            _pendingElementAction = null;
            DispatchSessionAction(new(point.X, point.Y), action, modifiers);
            // Drag-start and recorder-confirmation paths keep a navigation session alive.
            if (_sessionManager.IsActive && !_overlayWindow.IsVisible &&
                _macroHandler.State != MacroState.Playing) {
                _hostBusy = true;
                _overlayHost.ShowLast();
                if (!_hookService.Enable()) { DeactivateOverlay(); } else { _sessionManager.RedrawActiveSession(); }
                _hostBusy = false;
            }
        } catch (OperationCanceledException) {
            // Activation cancellation owns teardown; no late input or retry.
        } finally {
            _hostBusy = false;
            if (_pendingElementAction == intent) { _pendingElementAction = null; }
            ElementValidationChanged?.Invoke(IsElementValidationPending);
            intent.Dispose();
        }
    }

    private void CancelElementAction() {
        _pendingElementAction?.Cancel();
        _pendingElementAction = null;
    }
    private void NotifyElementFailure(string reason) {
        LogElementFailure(reason);
        RuntimeNotification?.Invoke(reason);
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Element hints: {Reason}")]
    private partial void LogElementFailure(string reason);

    private void OnElementGridFallback() {
        var cursor = _platform.Cursor.GetCursorPosition();
        var bounds = _sessionManager.ActiveBounds;
        var point = new Point(Math.Clamp(cursor.X, bounds.Left, bounds.Right - 1),
            Math.Clamp(cursor.Y, bounds.Top, bounds.Bottom - 1));
        _sessionManager.SwitchMode("UniformGrid", point);
        _overlayHost.SetDisplayNumbersEnabled(true);
        _sessionManager.LockMode();
    }
    private void ResetOverlayForDrag() {
        try {
            CancelElementAction();
            _sessionManager.ResetForDrag(GetDefaultModeName(), _actionDispatcher.DragStartPoint,
                _macroHandler.RecordingAppScoped, _macroHandler.RecordingWindowBounds);
            _overlayHost.SetDisplayNumbersEnabled(_sessionManager.ActiveSession is not ElementHintsSession);
            _overlayWindow.ShowStatusText("Select drag target");
        } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) {
            _actionDispatcher.ClearDragMode();
            DeactivateOverlay();
        }
    }

    private void OnSessionCancelled() {
        LogCancelled();

        // During recording, cancel just resets overlay for next action
        if (_macroHandler.State == MacroState.Recording) {
            _actionDispatcher.ClearDragMode();
            _macroHandler.CancelRecording();
            return;
        }

        _actionDispatcher.CancelDrag();
        DeactivateOverlay();
    }

    private void OnSessionCursorMoveRequested(Point point) {
        InputResultObserver.Observe(_mouseService.MoveTo(point), _logger);
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
            CancelElementAction();
            ClearHelpState();
            _hookService.DrainAndDisable();

            // Clear debounce state
            _debounce.StopAndDispose();

            _sessionManager.DeactivateSession();

            // Drag-mode cleanup: restore cursor to origin on focus-loss or unexpected deactivation
            _actionDispatcher.CancelDrag();

            // Restore focus to the window that was active before the overlay.
            // Critical for MoveOnly/Cancel where no click activates the target.
            var savedHwnd = _preOverlayHwnd;
            _preOverlayHwnd = 0;

            _overlayHost.Hide();

            if (savedHwnd != 0) {
                _platform.ForegroundWindow.SetForegroundWindow(savedHwnd);
            }

            // Clear modifier keys (Alt/Ctrl/Shift) that may be stuck in the target
            // window's thread — hotkey modifier keydown went to target before overlay
            // opened, but keyup was consumed by overlay.
            InputResultObserver.Observe(_mouseService.ClearStuckModifiers(), _logger);
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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "FocusLost busy={Busy} switching={Switching} visible={Visible} session={Session} rec={Recording}")]
    private partial void LogFocusLostDetail(
        bool busy, bool switching, bool visible, bool session, bool recording);

    [LoggerMessage(Level = LogLevel.Information, Message = "FocusLost suppressed (host busy or switching)")]
    private partial void LogFocusLostSuppressed();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Activate cursor=({X},{Y}) display={Display} bounds={BX},{BY} {BW}x{BH} count={Count}")]
    private partial void LogActivationTarget(
        int x, int y, string display, int bx, int by, int bw, int bh, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Navigation cancelled")]
    private partial void LogCancelled();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Display catalog failed: {Reason}")]
    private partial void LogDisplayCatalogFailed(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cursor at ({X}, {Y}) is outside any active display — activation suppressed")]
    private partial void LogCursorOutsideDisplays(int x, int y);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Non-QWERTY layout detected — falling back to UniformGrid instead of {Mode}")]
    private partial void LogNonQwertyFallback(string mode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Activation failed for mode {Mode}: {Error}")]
    private partial void LogActivationFailed(string mode, string error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "App-scope activated: {Width}×{Height}")]
    private partial void LogAppScopeActivated(int width, int height);

    [LoggerMessage(Level = LogLevel.Warning, Message = "App-scope rejected: {Reason}")]
    private partial void LogAppScopeRejected(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Help binding is ineffective: {Reason}")]
    private partial void LogInvalidHelpBinding(string reason);

    // --- Macro handler overlay-control event handlers ---

    private void OnMacroSuspendOverlay() {
        CancelElementAction();
        _sessionManager.SuspendElementHints();
        ClearHelpState();
        _overlayHost.Hide();
    }

    private void OnMacroResumeOverlay() {
        try {
            _hostBusy = true;
            _overlayHost.ShowLast();
        } catch (InvalidOperationException) {
            _macroHandler.CancelRecording();
        } finally {
            _hostBusy = false;
        }
    }

    private void OnMacroClearStatusText() {
        _overlayWindow.ClearStatusText();
    }

    // --- End macro handler event handlers ---

    public void Dispose() {
        // Unsubscribe macro handler events
        _macroHandler.SuspendOverlayRequested -= OnMacroSuspendOverlay;
        _macroHandler.ResumeOverlayRequested -= OnMacroResumeOverlay;
        _macroHandler.DeactivateRequested -= DeactivateOverlay;
        _macroHandler.ShowStatusTextRequested -= _overlayWindow.ShowStatusText;
        _macroHandler.ClearStatusTextRequested -= OnMacroClearStatusText;
        _macroHandler.SetRecordingBorderRequested -= _overlayWindow.SetRecordingBorder;
        _macroHandler.Dispose();

        _hotKeyService.Activated -= OnHotKeyActivated;
        _hookService.KeyEvent -= OnKeyEvent;
        _overlayWindow.FocusLost -= OnFocusLost;
        _overlayWindow.DisplayChanged -= OnDisplayChanged;
        _overlayWindow.KeyboardLayoutChanged -= OnKeyboardLayoutChanged;
        _overlayWindow.ViewportChanged -= OnViewportChanged;
        _sessionManager.ActionRequested -= OnSessionActionRequested;
        _sessionManager.Cancelled -= OnSessionCancelled;
        _sessionManager.CursorMoveRequested -= OnSessionCursorMoveRequested;
        _sessionManager.GridFallbackRequested -= OnElementGridFallback;
        _sessionManager.FailureReported -= NotifyElementFailure;
        DeactivateOverlay();
        _overlayHost.Dispose();
        _debounce.Dispose();
        _sessionManager.Dispose();
        _hookService.Dispose();
        _overlayWindow.Close();
    }
}
