using System.Drawing;

namespace Klikety.Grid;

/// <summary>
/// Computes a log-scale N×M grid (default 10×10) centered on a given point.
/// Cell sizes grow geometrically from the center outward, with independent growth
/// ratios per half-axis (left/right and up/down each solve their own binary search).
/// The cursor position is the shared edge between the two innermost cells on each axis.
/// </summary>
public static class LogScaleGridCalculator {
    /// <summary>
    /// Computes the log-scale grid.
    /// </summary>
    /// <param name="center">Cursor position in physical pixels. Becomes the center dividing edge.</param>
    /// <param name="bounds">Screen bounding rectangle in physical pixels.</param>
    /// <param name="logBaseSize">Innermost cell extent in physical pixels.</param>
    /// <param name="cols">Number of columns. Must be a positive even number.</param>
    /// <param name="rows">Number of rows. Must be a positive even number.</param>
    public static LogGrid Calculate(
        Point center, Rectangle bounds, int logBaseSize, int cols, int rows) {

        if (bounds.Width <= 0) {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Width must be positive.");
        }

        if (bounds.Height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Height must be positive.");
        }

        if (cols <= 0 || cols % 2 != 0) {
            throw new ArgumentOutOfRangeException(nameof(cols), "cols must be a positive even number.");
        }

        if (rows <= 0 || rows % 2 != 0) {
            throw new ArgumentOutOfRangeException(nameof(rows), "rows must be a positive even number.");
        }

        if (logBaseSize < 1) {
            throw new ArgumentOutOfRangeException(nameof(logBaseSize));
        }

        // Clamp center to bounds
        int cx = Math.Clamp(center.X, bounds.Left, bounds.Right);
        int cy = Math.Clamp(center.Y, bounds.Top, bounds.Bottom);

        double[] colEdges = ComputeAxisEdges(cx, bounds.Left, bounds.Right, logBaseSize, cols);
        double[] rowEdges = ComputeAxisEdges(cy, bounds.Top, bounds.Bottom, logBaseSize, rows);

        var cells = new GridCell[cols * rows];
        for (int row = 0; row < rows; row++) {
            int y = (int)Math.Round(rowEdges[row]);
            int nextY = (int)Math.Round(rowEdges[row + 1]);
            for (int col = 0; col < cols; col++) {
                int x = (int)Math.Round(colEdges[col]);
                int nextX = (int)Math.Round(colEdges[col + 1]);
                cells[row * cols + col] = new GridCell(row, col, new Rectangle(x, y, nextX - x, nextY - y));
            }
        }

        return new LogGrid(cells, cols, rows, new Point(cx, cy), colEdges, rowEdges);
    }

    /// <summary>
    /// Computes edge positions for one axis. The center position becomes the dividing
    /// edge between the two innermost half-axis cells. Growth ratios are solved
    /// independently per half (left/right or up/down).
    /// </summary>
    static double[] ComputeAxisEdges(
        int centerPos, int minEdge, int maxEdge, int baseSize, int cellCount) {

        int cellsPerHalf = cellCount / 2;
        double halfLeft = centerPos - minEdge;
        double halfRight = maxEdge - centerPos;

        double rLeft = FindHalfRatio(halfLeft, baseSize, cellsPerHalf);
        double rRight = FindHalfRatio(halfRight, baseSize, cellsPerHalf);

        var edges = new double[cellCount + 1];
        edges[cellsPerHalf] = centerPos;

        // Build left side: innermost cell (k=0) has size baseSize, grows outward
        double pos = centerPos;
        for (int k = 0; k < cellsPerHalf; k++) {
            double size = rLeft < 0
                ? halfLeft / cellsPerHalf
                : baseSize * Math.Pow(rLeft, k);
            pos -= size;
            edges[cellsPerHalf - 1 - k] = pos;
        }

        // Build right side: innermost cell (k=0) has size baseSize, grows outward
        pos = centerPos;
        for (int k = 0; k < cellsPerHalf; k++) {
            double size = rRight < 0
                ? halfRight / cellsPerHalf
                : baseSize * Math.Pow(rRight, k);
            pos += size;
            edges[cellsPerHalf + 1 + k] = pos;
        }

        // Outermost edges are always pinned to bounds (last-cell absorption)
        edges[0] = minEdge;
        edges[cellCount] = maxEdge;

        // Clamp intermediate edges to bounds (degenerate overflow protection)
        for (int i = 1; i < cellCount; i++) {
            edges[i] = Math.Clamp(edges[i], minEdge, maxEdge);
        }

        return edges;
    }

    /// <summary>
    /// Finds ratio r ≥ 1 for one half-axis such that:
    ///   baseSize · (1 + r + r² + … + r^(n−1)) = halfDistance
    /// Returns −1 to signal uniform fallback when halfDistance &lt; n · baseSize.
    /// </summary>
    static double FindHalfRatio(double halfDistance, int baseSize, int cellsPerHalf) {
        if (cellsPerHalf == 0) {
            return -1.0;
        }

        if (halfDistance < (double)cellsPerHalf * baseSize) {
            return -1.0; // uniform fallback: each cell gets halfDistance / cellsPerHalf
        }

        if (halfDistance == (double)cellsPerHalf * baseSize) {
            return 1.0;
        }

        // Binary search for r > 1
        double lo = 1.0;
        double hi = Math.Max(2.0, halfDistance / baseSize);

        for (int i = 0; i < 100; i++) {
            double mid = (lo + hi) / 2.0;
            double sum = GeometricSum(baseSize, mid, cellsPerHalf);
            if (sum < halfDistance) {
                lo = mid;
            } else {
                hi = mid;
            }
        }

        return (lo + hi) / 2.0;
    }

    /// <summary>
    /// Computes baseSize · (1 + r + r² + … + r^(n−1)).
    /// </summary>
    internal static double GeometricSum(double baseSize, double r, int n) {
        if (Math.Abs(r - 1.0) < 1e-10) {
            return baseSize * n;
        }

        return baseSize * (Math.Pow(r, n) - 1.0) / (r - 1.0);
    }
}
