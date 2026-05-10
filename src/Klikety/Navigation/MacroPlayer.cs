using System.Drawing;

using Klikety.Config;
using Klikety.Services;

namespace Klikety.Navigation;

public enum PlaybackResultKind {
    Completed,
    Cancelled,
    ScreenMismatch,
    WindowMismatch,
    CoordinateOutOfBounds,
    WindowDrift,
}

public sealed class PlaybackResult {
    public PlaybackResultKind Kind { get; init; }
    public string? Message { get; init; }

    public static PlaybackResult Completed => new() { Kind = PlaybackResultKind.Completed };
    public static PlaybackResult Cancelled => new() { Kind = PlaybackResultKind.Cancelled };
    public static PlaybackResult ScreenMismatch(string message) => new() {
        Kind = PlaybackResultKind.ScreenMismatch,
        Message = message,
    };
    public static PlaybackResult WindowMismatch(string message) => new() {
        Kind = PlaybackResultKind.WindowMismatch,
        Message = message,
    };
    public static PlaybackResult CoordinateOutOfBounds(string message) => new() {
        Kind = PlaybackResultKind.CoordinateOutOfBounds,
        Message = message,
    };
    public static PlaybackResult WindowDrift(string message) => new() {
        Kind = PlaybackResultKind.WindowDrift,
        Message = message,
    };
}

/// <summary>
/// Context for window-relative playback. Empty for absolute macros.
/// </summary>
public sealed record PlaybackContext(
    MacroPositionMode PositionMode,
    Rectangle WindowBounds,
    string WindowTitle,
    nint WindowHwnd,
    Point InitialCursorPosition
) {
    public static PlaybackContext Absolute => new(
        MacroPositionMode.Absolute, Rectangle.Empty, string.Empty, 0, Point.Empty);
}

public sealed class MacroPlayer {
    private readonly IMouseActionService _mouseService;
    private readonly IScreenBoundsProvider _screen;
    private readonly IForegroundWindowProvider? _foregroundWindow;
    private readonly IDelayProvider _delay;
    private readonly double _speedModifier;
    private readonly IClickIndicator? _clickIndicator;

    public event Action<int, int>? StepCompleted;
    public event Action<int, string>? DelayUpdate;

    public MacroPlayer(IMouseActionService mouseService, IScreenBoundsProvider screen,
          IDelayProvider delay, double speedModifier, IClickIndicator? clickIndicator = null,
          IForegroundWindowProvider? foregroundWindow = null) {
        _mouseService = mouseService;
        _screen = screen;
        _delay = delay;
        _speedModifier = speedModifier;
        _clickIndicator = clickIndicator;
        _foregroundWindow = foregroundWindow;
    }

    public Task<PlaybackResult> Play(MacroDefinition macro, CancellationToken ct) =>
        Play(macro, PlaybackContext.Absolute, ct);

    public async Task<PlaybackResult> Play(MacroDefinition macro, PlaybackContext context, CancellationToken ct) {
        // Window-relative validation
        if (macro.PositionMode == MacroPositionMode.WindowRelative) {
            if (string.IsNullOrEmpty(context.WindowTitle) ||
                !context.WindowTitle.Contains(macro.WindowTitlePattern, StringComparison.OrdinalIgnoreCase)) {
                return PlaybackResult.WindowMismatch(
                    $"Window title '{context.WindowTitle}' does not match pattern '{macro.WindowTitlePattern}'");
            }

            if (context.WindowBounds.Width != macro.WindowWidth || context.WindowBounds.Height != macro.WindowHeight) {
                return PlaybackResult.WindowMismatch(
                    $"Expected window {macro.WindowWidth}x{macro.WindowHeight}, " +
                    $"got {context.WindowBounds.Width}x{context.WindowBounds.Height}");
            }

            var dpi = _screen.GetDpiScale();
            if (Math.Abs(dpi - macro.DpiScale) > 0.01) {
                return PlaybackResult.WindowMismatch(
                    $"DPI mismatch: expected {macro.DpiScale:F2}, got {dpi:F2}");
            }
        } else {
            // Absolute screen validation
            var bounds = _screen.GetPrimaryScreenBounds();
            var dpi = _screen.GetDpiScale();

            if (bounds.Width != macro.ScreenWidth || bounds.Height != macro.ScreenHeight
                || Math.Abs(dpi - macro.DpiScale) > 0.001) {
                return PlaybackResult.ScreenMismatch(
                    $"Expected {macro.ScreenWidth}x{macro.ScreenHeight} @ {macro.DpiScale:F2} DPI, " +
                    $"got {bounds.Width}x{bounds.Height} @ {dpi:F2} DPI");
            }
        }

        var windowBounds = context.WindowBounds;
        var totalSteps = macro.Steps.Count;
        for (var i = 0; i < totalSteps; i++) {
            ct.ThrowIfCancellationRequested();

            var step = macro.Steps[i];
            var delayMs = ComputeDelay(step.RelativeTimeMs);
            await DelayWithUpdates(delayMs, step.ActionType.ToString(), ct);

            // Per-step drift check for window-relative mode
            if (macro.PositionMode == MacroPositionMode.WindowRelative && _foregroundWindow is not null) {
                var driftResult = CheckWindowDrift(context.WindowHwnd, ref windowBounds);
                if (driftResult is not null) {
                    return driftResult;
                }
            }

            var stepResult = ExecuteStep(step, macro.PositionMode, windowBounds, context.InitialCursorPosition);
            if (stepResult is not null) {
                return stepResult;
            }

            StepCompleted?.Invoke(i + 1, totalSteps);
        }

        return PlaybackResult.Completed;
    }

    private const int DelayTickMs = 50;

    private async Task DelayWithUpdates(int totalMs, string actionType, CancellationToken ct) {
        if (totalMs <= 0) {
            DelayUpdate?.Invoke(0, actionType);
            return;
        }

        var remaining = totalMs;
        DelayUpdate?.Invoke(remaining, actionType);

        while (remaining > 0) {
            var chunk = Math.Min(remaining, DelayTickMs);
            await _delay.Delay(chunk, ct);
            remaining -= chunk;
            DelayUpdate?.Invoke(remaining, actionType);
        }
    }

    private int ComputeDelay(int relativeTimeMs) {
        if (_speedModifier == 0) {
            return 100;
        }

        var scaled = (long)(relativeTimeMs * _speedModifier);
        var clamped = (int)Math.Min(scaled, int.MaxValue);
        return Math.Max(50, clamped);
    }

    private PlaybackResult? CheckWindowDrift(nint expectedHwnd, ref Rectangle windowBounds) {
        var currentHwnd = _foregroundWindow!.GetForegroundWindowHandle();
        if (currentHwnd != expectedHwnd) {
            return PlaybackResult.WindowDrift("Target window lost focus");
        }

        var currentBounds = _foregroundWindow.GetWindowBounds(currentHwnd);
        if (currentBounds.Width != windowBounds.Width || currentBounds.Height != windowBounds.Height) {
            return PlaybackResult.WindowDrift("Window resized during playback");
        }

        // Window moved — update bounds for coordinate resolution
        windowBounds = currentBounds;
        return null;
    }

    private static Point ResolvePoint(int x, int y, MacroPositionMode mode, Rectangle windowBounds) {
        if (mode == MacroPositionMode.WindowRelative) {
            return new Point(windowBounds.Left + x, windowBounds.Top + y);
        }
        return new Point(x, y);
    }

    private PlaybackResult? ExecuteStep(MacroStep step, MacroPositionMode mode, Rectangle windowBounds, Point initialCursor) {
        var point = ResolvePoint(step.X, step.Y, mode, windowBounds);

        if (mode == MacroPositionMode.WindowRelative) {
            if (point.X < windowBounds.Left || point.X >= windowBounds.Left + windowBounds.Width
                || point.Y < windowBounds.Top || point.Y >= windowBounds.Top + windowBounds.Height) {
                return PlaybackResult.CoordinateOutOfBounds(
                    $"Resolved coordinate ({point.X}, {point.Y}) outside window bounds");
            }
        }

        // Show click indicator for actions that interact (not MoveOnly)
        if (_clickIndicator != null && step.ActionType != MacroActionType.MoveOnly) {
            _clickIndicator.ShowAndWait(point.X, point.Y).GetAwaiter().GetResult();
        }

        switch (step.ActionType) {
            case MacroActionType.LeftClick:
                _mouseService.SendAction(point, MouseAction.LeftClick, step.Modifiers);
                break;
            case MacroActionType.RightClick:
                _mouseService.SendAction(point, MouseAction.RightClick, step.Modifiers);
                break;
            case MacroActionType.MiddleClick:
                _mouseService.SendAction(point, MouseAction.MiddleClick, step.Modifiers);
                break;
            case MacroActionType.DoubleClick:
                _mouseService.SendAction(point, MouseAction.DoubleClick, step.Modifiers);
                break;
            case MacroActionType.MoveOnly:
                _mouseService.SendAction(point, MouseAction.MoveOnly, ActionModifiers.None);
                break;
            case MacroActionType.DragDrop:
                var startPoint = step.StartFromCursor ? initialCursor : point;
                var end = ResolvePoint(step.EndX ?? step.X, step.EndY ?? step.Y, mode, windowBounds);
                _mouseService.SendDrag(startPoint, end, step.DragButton ?? MouseAction.LeftClick, step.Modifiers);
                break;
            case MacroActionType.Scroll:
                _mouseService.SendScroll(step.ScrollDelta ?? 0, step.Modifiers);
                break;
        }

        return null;
    }
}
