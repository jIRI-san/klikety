using Microsoft.Extensions.Logging;

namespace Klikety.Services;

internal static partial class InputResultObserver {
    internal static void Observe(InputResult result, ILogger logger) {
        if (!result.Succeeded) {
            LogInputFailed(logger, result.ToString());
        }
    }

    internal static async Task ObserveDragAsync(IMouseActionService mouse, System.Drawing.Point start,
        System.Drawing.Point end, Config.MouseAction button, Config.ActionModifiers modifiers, ILogger logger) {
        try {
            Observe(await mouse.SendDrag(start, end, button, modifiers).ConfigureAwait(false), logger);
        } catch (Exception ex) {
            LogDragFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Mouse input failed: {Details}")]
    private static partial void LogInputFailed(ILogger logger, string details);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mouse drag failed")]
    private static partial void LogDragFailed(ILogger logger, Exception exception);
}
