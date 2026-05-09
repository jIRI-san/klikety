using System.Drawing;

using Klikety.Config;
using Klikety.Services;

namespace Klikety.Navigation;

public enum PlaybackResultKind {
    Completed,
    Cancelled,
    ScreenMismatch,
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
}

public sealed class MacroPlayer {
    private readonly IMouseActionService _mouseService;
    private readonly IScreenBoundsProvider _screen;
    private readonly IDelayProvider _delay;
    private readonly double _speedModifier;

    public event Action<int, int>? StepCompleted;

    public MacroPlayer(IMouseActionService mouseService, IScreenBoundsProvider screen,
        IDelayProvider delay, double speedModifier) {
        _mouseService = mouseService;
        _screen = screen;
        _delay = delay;
        _speedModifier = speedModifier;
    }

    public async Task<PlaybackResult> Play(MacroDefinition macro, CancellationToken ct) {
        // Screen validation
        var bounds = _screen.GetPrimaryScreenBounds();
        var dpi = _screen.GetDpiScale();

        if (bounds.Width != macro.ScreenWidth || bounds.Height != macro.ScreenHeight
            || Math.Abs(dpi - macro.DpiScale) > 0.001) {
            return PlaybackResult.ScreenMismatch(
                $"Expected {macro.ScreenWidth}x{macro.ScreenHeight} @ {macro.DpiScale:F2} DPI, " +
                $"got {bounds.Width}x{bounds.Height} @ {dpi:F2} DPI");
        }

        var totalSteps = macro.Steps.Count;
        for (var i = 0; i < totalSteps; i++) {
            ct.ThrowIfCancellationRequested();

            var step = macro.Steps[i];
            var delayMs = ComputeDelay(step.RelativeTimeMs);
            await _delay.Delay(delayMs, ct);

            ExecuteStep(step);
            StepCompleted?.Invoke(i + 1, totalSteps);
        }

        return PlaybackResult.Completed;
    }

    private int ComputeDelay(int relativeTimeMs) {
        if (_speedModifier == 0) {
            return 100;
        }

        var scaled = (long)(relativeTimeMs * _speedModifier);
        var clamped = (int)Math.Min(scaled, int.MaxValue);
        return Math.Max(50, clamped);
    }

    private void ExecuteStep(MacroStep step) {
        var point = new Point(step.X, step.Y);

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
                var end = new Point(step.EndX ?? step.X, step.EndY ?? step.Y);
                _mouseService.SendDrag(point, end, step.DragButton ?? MouseAction.LeftClick, step.Modifiers);
                break;
            case MacroActionType.Scroll:
                _mouseService.SendScroll(step.ScrollDelta ?? 0, step.Modifiers);
                break;
        }
    }
}
