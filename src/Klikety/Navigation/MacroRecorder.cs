using System.Diagnostics;
using System.Drawing;

using Klikety.Config;
using Klikety.Services;

using Microsoft.Extensions.Logging;

namespace Klikety.Navigation;

/// <summary>
/// State machine for macro recording. Manages slot selection, overwrite confirmation,
/// step capture with timing, and drag pairing. No UI dependency — testable via direct calls.
/// </summary>
public sealed partial class MacroRecorder {
    private readonly IScreenBoundsProvider _screen;
    private readonly ILogger _logger;

    private MacroRecorderState _state = MacroRecorderState.Idle;
    private int _selectedSlot;
    private readonly Stopwatch _stepTimer = new();
    private readonly List<MacroStep> _steps = [];
    private int _screenWidth;
    private int _screenHeight;
    private double _dpiScale;

    // Drag pairing: DragDrop action → next action resolves button
    private (Point Start, ActionModifiers Modifiers, int RelativeTimeMs)? _pendingDragStart;

    /// <summary>Current state of the recorder.</summary>
    public MacroRecorderState State => _state;

    /// <summary>Raised when recording completes with a finished macro.</summary>
    public event Action<int, MacroDefinition>? RecordingComplete;

    /// <summary>Raised when recording is cancelled from any state.</summary>
    public event Action? RecordingCancelled;

    /// <summary>Raised when the recorder enters AwaitSlot state and needs slot key input.</summary>
    public event Action? SlotSelectionRequested;

    /// <summary>Raised when the recorder enters AwaitOverwrite state.</summary>
    public event Action<int, string>? OverwriteConfirmRequested;

    /// <summary>Raised when the recorder enters Recording state (after slot confirmed).</summary>
    public event Action? RecordingStarted;

    public MacroRecorder(IScreenBoundsProvider screen, ILogger logger) {
        _screen = screen;
        _logger = logger;
    }

    /// <summary>
    /// Begin the recording flow: transition Idle → AwaitSlot.
    /// </summary>
    public void StartRecording() {
        if (_state != MacroRecorderState.Idle) {
            return;
        }

        _state = MacroRecorderState.AwaitSlot;
        SlotSelectionRequested?.Invoke();
        LogRecordingAwaitSlot();
    }

    /// <summary>
    /// Handle a slot key press during AwaitSlot state.
    /// </summary>
    public void OnSlotKey(int slot, MacroDefinition?[] currentMacros) {
        if (_state != MacroRecorderState.AwaitSlot) {
            return;
        }

        if (slot < 0 || slot >= currentMacros.Length) {
            return;
        }

        _selectedSlot = slot;

        if (currentMacros[slot] is { } existing) {
            _state = MacroRecorderState.AwaitOverwrite;
            OverwriteConfirmRequested?.Invoke(slot, existing.Name);
            LogRecordingAwaitOverwrite(slot, existing.Name);
        } else {
            BeginRecording();
        }
    }

    /// <summary>
    /// Handle overwrite confirmation response.
    /// </summary>
    public void OnOverwriteResponse(bool confirmed) {
        if (_state != MacroRecorderState.AwaitOverwrite) {
            return;
        }

        if (confirmed) {
            BeginRecording();
        } else {
            Cancel();
        }
    }

    /// <summary>
    /// Record an action step. Called when a session fires ActionRequested during recording.
    /// </summary>
    public void RecordAction(Point point, MouseAction action, ActionModifiers modifiers) {
        if (_state != MacroRecorderState.Recording) {
            return;
        }

        var elapsed = (int)_stepTimer.ElapsedMilliseconds;
        _stepTimer.Restart();

        // Drag phase 1: DragDrop starts a pending drag
        if (action == MouseAction.DragDrop) {
            _pendingDragStart = (point, modifiers, elapsed);
            return;
        }

        // Drag phase 2: resolve pending drag with button
        if (_pendingDragStart is { } drag) {
            if (action is MouseAction.LeftClick or MouseAction.RightClick or MouseAction.MiddleClick) {
                _steps.Add(new MacroStep {
                    ActionType = MacroActionType.DragDrop,
                    X = drag.Start.X,
                    Y = drag.Start.Y,
                    Modifiers = drag.Modifiers,
                    RelativeTimeMs = drag.RelativeTimeMs,
                    EndX = point.X,
                    EndY = point.Y,
                    DragButton = action,
                });
                _pendingDragStart = null;
            } else {
                // Invalid drag button (MoveOnly, DoubleClick, DragDrop) — discard drag
                LogDragPairInvalid(action);
                _pendingDragStart = null;
            }

            return;
        }

        // Normal action
        var actionType = action switch {
            MouseAction.LeftClick => MacroActionType.LeftClick,
            MouseAction.RightClick => MacroActionType.RightClick,
            MouseAction.MiddleClick => MacroActionType.MiddleClick,
            MouseAction.DoubleClick => MacroActionType.DoubleClick,
            MouseAction.MoveOnly => MacroActionType.MoveOnly,
            _ => MacroActionType.LeftClick,
        };

        _steps.Add(new MacroStep {
            ActionType = actionType,
            X = point.X,
            Y = point.Y,
            Modifiers = modifiers,
            RelativeTimeMs = elapsed,
        });
    }

    /// <summary>
    /// Record a scroll step.
    /// </summary>
    public void RecordScroll(Point point, int scrollDelta, ActionModifiers modifiers) {
        if (_state != MacroRecorderState.Recording) {
            return;
        }

        var elapsed = (int)_stepTimer.ElapsedMilliseconds;
        _stepTimer.Restart();

        _steps.Add(new MacroStep {
            ActionType = MacroActionType.Scroll,
            X = point.X,
            Y = point.Y,
            Modifiers = modifiers,
            RelativeTimeMs = elapsed,
            ScrollDelta = scrollDelta,
        });
    }

    /// <summary>
    /// Stop recording and emit the completed macro.
    /// </summary>
    public void StopRecording() {
        if (_state != MacroRecorderState.Recording) {
            return;
        }

        if (_pendingDragStart is not null) {
            LogPendingDragDiscarded(_selectedSlot);
            _pendingDragStart = null;
        }

        _stepTimer.Stop();
        _state = MacroRecorderState.Idle;

        var macro = new MacroDefinition {
            Name = $"Macro {_selectedSlot}",
            ScreenWidth = _screenWidth,
            ScreenHeight = _screenHeight,
            DpiScale = _dpiScale,
            Steps = [.. _steps],
        };

        LogRecordingStopped(_selectedSlot, _steps.Count);
        RecordingComplete?.Invoke(_selectedSlot, macro);
    }

    /// <summary>
    /// Cancel recording from any state.
    /// </summary>
    public void Cancel() {
        if (_state == MacroRecorderState.Idle) {
            return;
        }

        LogRecordingCancelled(_state);
        _pendingDragStart = null;
        _steps.Clear();
        _stepTimer.Stop();
        _state = MacroRecorderState.Idle;
        RecordingCancelled?.Invoke();
    }

    private void BeginRecording() {
        var bounds = _screen.GetPrimaryScreenBounds();
        _screenWidth = bounds.Width;
        _screenHeight = bounds.Height;
        _dpiScale = _screen.GetDpiScale();

        _steps.Clear();
        _pendingDragStart = null;
        _stepTimer.Restart();
        _state = MacroRecorderState.Recording;

        LogRecordingStarted(_selectedSlot);
        RecordingStarted?.Invoke();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recording: awaiting slot selection")]
    private partial void LogRecordingAwaitSlot();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recording: slot {Slot} occupied ('{Name}'), awaiting overwrite confirmation")]
    private partial void LogRecordingAwaitOverwrite(int slot, string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recording started for slot {Slot}")]
    private partial void LogRecordingStarted(int slot);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recording stopped for slot {Slot}: {StepCount} steps")]
    private partial void LogRecordingStopped(int slot, int stepCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recording cancelled from state {State}")]
    private partial void LogRecordingCancelled(MacroRecorderState state);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pending drag discarded on stop (slot {Slot})")]
    private partial void LogPendingDragDiscarded(int slot);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid drag-pair action: {Action} — drag discarded")]
    private partial void LogDragPairInvalid(MouseAction action);
}

/// <summary>
/// States of the macro recorder.
/// </summary>
public enum MacroRecorderState {
    Idle,
    AwaitSlot,
    AwaitOverwrite,
    Recording,
}
