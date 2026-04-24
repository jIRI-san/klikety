using System.Drawing;

namespace Klikety.Grid;

/// <summary>
/// Subdivides a parent cell into a subgrid for level-2/level-3 navigation.
/// </summary>
public static class SubgridCalculator {
    /// <summary>
    /// Computes subgrid cells within the parent cell's bounds.
    /// </summary>
    public static IReadOnlyList<GridCell> Calculate(GridCell parent, int cols, int rows) {
        return GridCalculator.Calculate(parent.Bounds, cols, rows);
    }

    /// <summary>
    /// Determines whether level-3 navigation should be available
    /// based on the cell's physical-pixel area vs. the threshold.
    /// </summary>
    public static bool ShouldActivateLevel3(GridCell level2Cell, int thresholdPixelsSq) {
        long area = (long)level2Cell.Bounds.Width * level2Cell.Bounds.Height;
        return area > thresholdPixelsSq;
    }
}
