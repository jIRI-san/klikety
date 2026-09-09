using System.Windows;
using System.Windows.Media;

namespace Klikety.Overlay;

/// <summary>
/// Physical desktop pixels → overlay-canvas DIP. Canvas origin is the overlay
/// window, not the virtual desktop — monitors with negative <c>rcMonitor</c>
/// must subtract the window's physical origin or cells draw off-canvas.
/// </summary>
internal static class OverlayDip {
    public static Matrix ScaleOf(Visual visual) {
        var dpi = VisualTreeHelper.GetDpi(visual);
        double sx = dpi.DpiScaleX > 0 ? 1.0 / dpi.DpiScaleX : 1.0;
        double sy = dpi.DpiScaleY > 0 ? 1.0 / dpi.DpiScaleY : 1.0;
        return new Matrix(sx, 0, 0, sy, 0, 0);
    }

    public static System.Drawing.Point OriginOf(IReadOnlyList<Grid.GridCell> cells) {
        int x = int.MaxValue, y = int.MaxValue;
        foreach (var cell in cells) {
            if (cell.Bounds.X < x) {
                x = cell.Bounds.X;
            }

            if (cell.Bounds.Y < y) {
                y = cell.Bounds.Y;
            }
        }

        return x == int.MaxValue ? System.Drawing.Point.Empty : new System.Drawing.Point(x, y);
    }

    public static Rect ToCanvas(
        System.Drawing.Rectangle physical,
        System.Drawing.Point origin,
        Matrix scale) {
        return new Rect(
            (physical.X - origin.X) * scale.M11,
            (physical.Y - origin.Y) * scale.M22,
            physical.Width * scale.M11,
            physical.Height * scale.M22);
    }

    public static Point ToCanvasPoint(double physicalX, double physicalY, System.Drawing.Point origin, Matrix scale) =>
        new((physicalX - origin.X) * scale.M11, (physicalY - origin.Y) * scale.M22);
}
