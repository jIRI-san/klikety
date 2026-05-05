using System.Drawing;

namespace Klikety.Grid;

/// <summary>
/// Determines the screen corner position for the first-key indicator.
/// The indicator is placed in the corner opposite to the quadrant that
/// contains the current grid center point.
/// <para>
/// Quadrant mapping (center quadrant → indicator corner):
/// <list type="bullet">
/// <item>Top-left (-1,-1) → bottom-right (1,1)</item>
/// <item>Top-right (1,-1) → bottom-left (-1,1)</item>
/// <item>Bottom-left (-1,1) → top-right (1,-1)</item>
/// <item>Bottom-right (1,1) → top-left (-1,-1)</item>
/// <item>Exact center → bottom-right (fallback)</item>
/// </list>
/// </para>
/// </summary>
public static class QuadrantCornerHelper {
    /// <summary>
    /// Returns the screen corner position (top-left of the indicator region)
    /// for a given grid center point within the screen bounds.
    /// </summary>
    /// <param name="gridCenter">The grid center in physical pixels.</param>
    /// <param name="screenBounds">The screen rectangle in physical pixels.</param>
    /// <param name="indicatorSize">The indicator dimensions (width, height) in physical pixels.</param>
    /// <returns>The top-left point of the indicator placement area.</returns>
    public static Point GetIndicatorPosition(Point gridCenter, Rectangle screenBounds, Size indicatorSize) {
        double screenMidX = screenBounds.X + screenBounds.Width / 2.0;
        double screenMidY = screenBounds.Y + screenBounds.Height / 2.0;

        // Determine which quadrant the grid center is in
        // Exact center (on midpoint) → fallback to bottom-right
        bool centerIsLeft = gridCenter.X < screenMidX;
        bool centerIsTop = gridCenter.Y < screenMidY;
        bool isExactCenterX = gridCenter.X == (int)screenMidX;
        bool isExactCenterY = gridCenter.Y == (int)screenMidY;

        // Opposite corner (with exact-center fallback to bottom-right)
        bool indicatorRight;
        bool indicatorBottom;

        if (isExactCenterX && isExactCenterY) {
            // Exact center → bottom-right
            indicatorRight = true;
            indicatorBottom = true;
        } else {
            indicatorRight = centerIsLeft;
            indicatorBottom = centerIsTop;
        }

        int margin = 20;
        int x = indicatorRight
            ? screenBounds.Right - indicatorSize.Width - margin
            : screenBounds.X + margin;
        int y = indicatorBottom
            ? screenBounds.Bottom - indicatorSize.Height - margin
            : screenBounds.Y + margin;

        return new Point(x, y);
    }
}
