using System.Drawing;

namespace Klikety.Grid;

/// <summary>
/// Computes a logarithmic crosshair grid centered on a given point.
/// Cell sizes grow geometrically from the center outward: the center cell is
/// the smallest (<paramref name="logBaseSize"/> pixels) and each successive
/// cell outward is <c>ratio</c> times wider/taller than the previous.
/// The growth ratio is found via binary search per axis so cells fill the
/// screen bounds exactly. The shorter half of each axis constrains the ratio;
/// the outermost cell on the longer half absorbs remaining space.
/// </summary>
public static class LogGridCalculator {
    /// <summary>
    /// Computes the log-crosshair grid.
    /// Grid has (horizKeyCount + 1) columns and (vertKeyCount + 1) rows.
    /// Center cell is at column horizKeyCount/2, row vertKeyCount/2.
    /// </summary>
    /// <param name="center">Cursor position in physical pixels.</param>
    /// <param name="bounds">Screen bounding rectangle in physical pixels.</param>
    /// <param name="logBaseSize">Center cell size in physical pixels.</param>
    /// <param name="horizKeyCount">Number of horizontal axis keys.</param>
    /// <param name="vertKeyCount">Number of vertical axis keys.</param>
    public static LogCrosshairGrid Calculate(
        Point center, Rectangle bounds, int logBaseSize,
        int horizKeyCount, int vertKeyCount) {

        if (bounds.Width <= 0) {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Width must be positive.");
        }

        if (bounds.Height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Height must be positive.");
        }

        if (horizKeyCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(horizKeyCount));
        }

        if (vertKeyCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(vertKeyCount));
        }

        if (logBaseSize < 1) {
            throw new ArgumentOutOfRangeException(nameof(logBaseSize));
        }

        int cols = horizKeyCount + 1;
        int rows = vertKeyCount + 1;
        int centerCol = horizKeyCount / 2;
        int centerRow = vertKeyCount / 2;

        // Clamp center to bounds
        int cx = Math.Clamp(center.X, bounds.X, bounds.Right);
        int cy = Math.Clamp(center.Y, bounds.Y, bounds.Bottom);

        double[] colEdges = ComputeAxisEdges(cx, bounds.X, bounds.Right, logBaseSize, cols, centerCol);
        double[] rowEdges = ComputeAxisEdges(cy, bounds.Y, bounds.Bottom, logBaseSize, rows, centerRow);

        var cells = new GridCell[cols * rows];
        var degenerateIndices = new HashSet<int>();

        for (int row = 0; row < rows; row++) {
            int y = (int)Math.Round(rowEdges[row]);
            int nextY = (int)Math.Round(rowEdges[row + 1]);
            for (int col = 0; col < cols; col++) {
                int x = (int)Math.Round(colEdges[col]);
                int nextX = (int)Math.Round(colEdges[col + 1]);
                int idx = row * cols + col;
                cells[idx] = new GridCell(row, col, new Rectangle(x, y, nextX - x, nextY - y));
                if (nextX - x < 1 || nextY - y < 1) {
                    degenerateIndices.Add(idx);
                }
            }
        }

        return new LogCrosshairGrid(cells, cols, rows, centerCol, centerRow, degenerateIndices);
    }

    /// <summary>Returns the center point of a cell in physical pixels.</summary>
    public static Point CenterOf(GridCell cell) =>
        new(cell.Bounds.X + cell.Bounds.Width / 2, cell.Bounds.Y + cell.Bounds.Height / 2);

    /// <summary>
    /// Computes edge positions for one axis. Edges grow geometrically from center
    /// and are clamped to [minEdge, maxEdge] for last-cell absorption / overflow.
    /// </summary>
    static double[] ComputeAxisEdges(
        int centerPos, int minEdge, int maxEdge, int baseSize, int cellCount, int centerIdx) {

        double halfBase = baseSize / 2.0;
        double dLeft = centerPos - minEdge;
        double dRight = maxEdge - centerPos;

        int leftCells = centerIdx;
        int rightCells = cellCount - centerIdx - 1;

        double ratio = FindAxisRatio(dLeft, dRight, halfBase, baseSize, leftCells, rightCells);

        var edges = new double[cellCount + 1];

        // Center cell edges
        edges[centerIdx] = centerPos - halfBase;
        edges[centerIdx + 1] = centerPos + halfBase;

        // Build leftward from center
        double pos = edges[centerIdx];
        for (int i = 1; i <= leftCells; i++) {
            double width = baseSize * Math.Pow(ratio, i);
            pos -= width;
            edges[centerIdx - i] = pos;
        }

        // Build rightward from center
        pos = edges[centerIdx + 1];
        for (int i = 1; i <= rightCells; i++) {
            double width = baseSize * Math.Pow(ratio, i);
            pos += width;
            edges[centerIdx + 1 + i] = pos;
        }

        // Last-cell absorption: outermost edges always at bounds
        edges[0] = minEdge;
        edges[cellCount] = maxEdge;

        // Clamp intermediate edges to bounds (degenerate overflow protection)
        for (int i = 1; i < cellCount; i++) {
            edges[i] = Math.Clamp(edges[i], minEdge, maxEdge);
        }

        return edges;
    }

    /// <summary>
    /// Finds the single growth ratio for an axis by taking the tighter constraint
    /// of the two half-axis distances.
    /// </summary>
    static double FindAxisRatio(
        double dLeft, double dRight, double halfBase, int baseSize,
        int leftCells, int rightCells) {

        double rLeft = leftCells > 0 && dLeft > halfBase
            ? FindSideRatio(dLeft - halfBase, baseSize, leftCells)
            : double.MaxValue;

        double rRight = rightCells > 0 && dRight > halfBase
            ? FindSideRatio(dRight - halfBase, baseSize, rightCells)
            : double.MaxValue;

        double r = Math.Min(rLeft, rRight);
        return r == double.MaxValue ? 1.0 : r;
    }

    /// <summary>
    /// Binary search for ratio r such that
    /// baseSize * (r + r² + … + r^cellCount) = available.
    /// Returns 1.0 when available ≤ uniform sum (ratio would be ≤ 1).
    /// </summary>
    static double FindSideRatio(double available, int baseSize, int cellCount) {
        if (cellCount == 0 || baseSize <= 0) {
            return 1.0;
        }

        double uniformSum = (double)baseSize * cellCount;
        if (available <= uniformSum) {
            return 1.0;
        }

        // Binary search for ratio > 1
        double lo = 1.0;
        double hi = Math.Max(2.0, available / baseSize);

        for (int i = 0; i < 100; i++) {
            double mid = (lo + hi) / 2.0;
            double sum = GeometricSum(baseSize, mid, cellCount);
            if (sum < available) {
                lo = mid;
            } else {
                hi = mid;
            }
        }

        return (lo + hi) / 2.0;
    }

    /// <summary>
    /// Computes baseSize · (r + r² + … + r^count) = baseSize · r · (r^count − 1) / (r − 1).
    /// </summary>
    static double GeometricSum(double baseSize, double ratio, int count) {
        if (Math.Abs(ratio - 1.0) < 1e-10) {
            return baseSize * count;
        }

        return baseSize * ratio * (Math.Pow(ratio, count) - 1.0) / (ratio - 1.0);
    }
}

/// <summary>
/// Result of logarithmic crosshair grid calculation.
/// Same structure as <see cref="CrosshairGrid"/> plus degenerate cell tracking.
/// </summary>
public sealed class LogCrosshairGrid {
    public IReadOnlyList<GridCell> Cells { get; }
    public int Cols { get; }
    public int Rows { get; }
    public int CenterCol { get; }
    public int CenterRow { get; }

    readonly HashSet<int> _degenerateIndices;

    public LogCrosshairGrid(
        GridCell[] cells, int cols, int rows, int centerCol, int centerRow,
        HashSet<int> degenerateIndices) {
        Cells = cells;
        Cols = cols;
        Rows = rows;
        CenterCol = centerCol;
        CenterRow = centerRow;
        _degenerateIndices = degenerateIndices;
    }

    public GridCell CellAt(int row, int col) => Cells[row * Cols + col];
    public GridCell CenterCell => CellAt(CenterRow, CenterCol);
    public bool IsOnCross(int row, int col) => row == CenterRow || col == CenterCol;
    public bool IsDegenerate(int row, int col) => _degenerateIndices.Contains(row * Cols + col);
}
