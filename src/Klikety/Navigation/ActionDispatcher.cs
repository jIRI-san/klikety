using System.Drawing;

using Klikety.Config;
using Klikety.Services;

using Microsoft.Extensions.Logging;

namespace Klikety.Navigation;

/// <summary>
/// Handles action dispatch and drag-drop lifecycle.
/// Reads session bounds/origin from <see cref="SessionManager"/>.
/// </summary>
internal sealed partial class ActionDispatcher {
    private readonly IMouseActionService _mouseService;
    private readonly IModifierDetector _modifierDetector;
    private readonly IOverlayWindow _overlayWindow;
    private readonly SessionManager _sessionManager;
    private readonly Action _deactivateOverlay;
    private readonly ILogger _logger;

    private bool _dragMode;
    private Point _dragStartPoint;

    public bool IsDragMode => _dragMode;
    public Point DragStartPoint => _dragStartPoint;

    public ActionDispatcher(
        IMouseActionService mouseService,
        IModifierDetector modifierDetector,
        IOverlayWindow overlayWindow,
        SessionManager sessionManager,
        Action deactivateOverlay,
        ILogger logger) {
        _mouseService = mouseService;
        _modifierDetector = modifierDetector;
        _overlayWindow = overlayWindow;
        _sessionManager = sessionManager;
        _deactivateOverlay = deactivateOverlay;
        _logger = logger;
    }

    /// <summary>
    /// Handles normal (non-recording) action dispatch including drag lifecycle.
    /// Returns true if the action was handled.
    /// </summary>
    public bool HandleAction(Point point, MouseAction action) {
        LogActionRequested(action, point.X, point.Y);

        // Bounds validation
        if (!_sessionManager.ActiveBounds.Contains(point)) {
            LogActionOutOfBounds(point.X, point.Y);
            _mouseService.MoveTo(_sessionManager.Origin);
            _deactivateOverlay();
            return true;
        }

        // Drag phase 2: completing a drag
        if (_dragMode) {
            if (action is MouseAction.MoveOnly or MouseAction.DragDrop) {
                LogDragInvalidAction(action);
                return true;
            }

            var modifiers = _modifierDetector.GetCurrentModifiers();
            _overlayWindow.ClearStatusText();
            _dragMode = false;
            _deactivateOverlay();
            _mouseService.SendDrag(_dragStartPoint, point, action, modifiers);
            return true;
        }

        // Drag phase 1: starting a drag
        if (action == MouseAction.DragDrop) {
            _dragStartPoint = point;
            _dragMode = true;
            return false; // Caller handles overlay reset
        }

        var actionModifiers = action == MouseAction.MoveOnly
            ? ActionModifiers.None
            : _modifierDetector.GetCurrentModifiers();

        _deactivateOverlay();
        _mouseService.SendAction(point, action, actionModifiers);
        return true;
    }

    /// <summary>
    /// Handles action dispatch during recording.
    /// Returns a <see cref="RecordingActionResult"/> indicating what the coordinator should do next.
    /// </summary>
    public RecordingActionResult HandleRecordingAction(
        Point point,
        MouseAction action,
        MacroRecorder recorder,
        bool recordingAppScoped,
        Rectangle recordingWindowBounds) {
        LogActionRequested(action, point.X, point.Y);

        // Bounds validation during recording
        if (!_sessionManager.ActiveBounds.Contains(point)) {
            LogActionOutOfBounds(point.X, point.Y);
            _mouseService.MoveTo(_sessionManager.Origin);
            return RecordingActionResult.ResumeRecording;
        }

        var modifiers = action == MouseAction.MoveOnly
            ? ActionModifiers.None
            : _modifierDetector.GetCurrentModifiers();

        var recordPoint = recordingAppScoped
            ? new Point(point.X - recordingWindowBounds.Left, point.Y - recordingWindowBounds.Top)
            : point;

        // Drag phase 2: completing a drag during recording
        if (_dragMode) {
            if (action is MouseAction.MoveOnly or MouseAction.DragDrop) {
                LogDragInvalidAction(action);
                return RecordingActionResult.Consumed;
            }

            recorder.RecordAction(recordPoint, action, modifiers);

            // StartFromCursor prompt — don't clear status or suspend overlay
            if (recorder.State == MacroRecorderState.AwaitStartFromCursorConfirm) {
                _dragMode = false;
                _mouseService.SendDrag(_dragStartPoint, point, action, modifiers);
                return RecordingActionResult.Consumed;
            }

            _overlayWindow.ClearStatusText();
            _dragMode = false;
            _mouseService.SendDrag(_dragStartPoint, point, action, modifiers);
            return RecordingActionResult.SuspendAndResume;
        }

        // Drag phase 1: starting a drag during recording
        if (action == MouseAction.DragDrop) {
            recorder.RecordAction(recordPoint, MouseAction.DragDrop, modifiers);
            _dragStartPoint = point;
            _dragMode = true;
            return RecordingActionResult.ResetForDrag;
        }

        // Normal action during recording
        recorder.RecordAction(recordPoint, action, modifiers);
        _mouseService.SendAction(point, action, modifiers);
        return RecordingActionResult.SuspendAndResume;
    }

    /// <summary>
    /// Cancels drag mode and restores cursor to origin.
    /// </summary>
    public void CancelDrag() {
        if (!_dragMode) {
            return;
        }

        _dragMode = false;
        _overlayWindow.ClearStatusText();
        _mouseService.MoveTo(_sessionManager.Origin);
    }

    /// <summary>
    /// Clears drag mode without cursor restore (used during recording cancel).
    /// </summary>
    public void ClearDragMode() {
        if (!_dragMode) {
            return;
        }

        _dragMode = false;
        _overlayWindow.ClearStatusText();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Action requested: {Action} at ({X}, {Y})")]
    private partial void LogActionRequested(MouseAction action, int x, int y);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action point ({X}, {Y}) out of screen bounds — suppressed")]
    private partial void LogActionOutOfBounds(int x, int y);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drag mode: invalid action {Action} — ignored")]
    private partial void LogDragInvalidAction(MouseAction action);
}

/// <summary>
/// Result of a recording action dispatch, telling the coordinator what to do next.
/// </summary>
internal enum RecordingActionResult {
    /// <summary>Key was consumed, no further action needed.</summary>
    Consumed,
    /// <summary>Suspend overlay and schedule resume after action delay.</summary>
    SuspendAndResume,
    /// <summary>Reset overlay for drag target selection.</summary>
    ResetForDrag,
    /// <summary>Out of bounds during recording — resume recording overlay.</summary>
    ResumeRecording,
}
