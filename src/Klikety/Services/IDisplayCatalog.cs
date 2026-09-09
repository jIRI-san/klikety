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
        DisplayInfo? nearest = null;
        long nearestDist = long.MaxValue;
        foreach (var display in Displays) {
            var b = display.MonitorBounds;
            if (ContainsInclusive(b, physicalPoint)) {
                return display;
            }

            long dist = DistanceSquared(b, physicalPoint);
            if (dist < nearestDist) {
                nearestDist = dist;
                nearest = display;
            }
        }

        // Edge / rounding slop only — do not jump a 1000px gap between monitors.
        return nearestDist <= 4 ? nearest : null;
    }

    private static bool ContainsInclusive(Rectangle b, Point p) =>
        p.X >= b.Left && p.X <= b.Right && p.Y >= b.Top && p.Y <= b.Bottom;

    private static long DistanceSquared(Rectangle b, Point p) {
        int x = p.X < b.Left ? b.Left - p.X : p.X > b.Right ? p.X - b.Right : 0;
        int y = p.Y < b.Top ? b.Top - p.Y : p.Y > b.Bottom ? p.Y - b.Bottom : 0;
        return ((long)x * x) + ((long)y * y);
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
