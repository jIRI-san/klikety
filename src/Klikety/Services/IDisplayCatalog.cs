using System.Drawing;

namespace Klikety.Services;

/// <summary>
/// Frozen active-display identity: physical <c>rcMonitor</c>, DPI, GDI name, CCD DevicePath.
/// Identity key is DevicePath, not <c>HMONITOR</c> or <c>\\.\DISPLAYn</c>.
/// </summary>
public sealed record DisplayInfo(
    Rectangle MonitorBounds,
    double DpiScale,
    string GdiName,
    string DevicePath);

/// <summary>
/// Immutable catalog snapshot. No partial maps — either this or a hard failure.
/// </summary>
public sealed class DisplaySnapshot {
    public DisplaySnapshot(IReadOnlyList<DisplayInfo> displays, Rectangle virtualScreen) {
        Displays = Array.AsReadOnly(displays.ToArray());
        VirtualScreen = virtualScreen;
    }

    public IReadOnlyList<DisplayInfo> Displays { get; }
    public Rectangle VirtualScreen { get; }

    public DisplayInfo? FindContaining(Point physicalPoint) {
        foreach (var display in Displays) {
            if (display.MonitorBounds.Contains(physicalPoint)) {
                return display;
            }
        }

        return null;
    }
}

/// <summary>
/// Success with a frozen snapshot, or failure with no displays.
/// </summary>
public readonly struct DisplayCatalogResult {
    public bool Success { get; }
    public DisplaySnapshot? Snapshot { get; }
    public string? FailureReason { get; }

    private DisplayCatalogResult(bool success, DisplaySnapshot? snapshot, string? failureReason) {
        Success = success;
        Snapshot = snapshot;
        FailureReason = failureReason;
    }

    public static DisplayCatalogResult Ok(DisplaySnapshot snapshot) => new(true, snapshot, null);

    public static DisplayCatalogResult Fail(string reason) => new(false, null, reason);
}

/// <summary>
/// Enumerates active displays. Fail closed on empty/duplicate DevicePath or CCD/monitor count mismatch.
/// </summary>
public interface IDisplayCatalog {
    DisplayCatalogResult GetSnapshot();
}
