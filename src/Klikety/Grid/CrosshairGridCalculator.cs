using System.Drawing;

namespace Klikety.Grid;

/// <summary>
/// Computes a crosshair-style grid: (N+1) × (M+1) uniform cells where N = horizontal
/// key count and M = vertical key count. The center cell is at the intersection of the
/// center row and center column (the "cross").
/// </summary>
public static class CrosshairGridCalculator {
    /// <summary>
    /// Computes the crosshair grid cells for the given bounds and axis key counts.
    /// Grid has (horizKeys + 1) columns and (vertKeys + 1) rows.
    /// Center cell is at column horizKeys/2, row vertKeys/2.
    /// </summary>
    /// <param name="bounds">Bounding rectangle in physical pixels.</param>
    /// <param name="horizKeyCount">Number of horizontal axis keys.</param>
    /// <param name="vertKeyCount">Number of vertical axis keys.</param>
    public static CrosshairGrid Calculate(Rectangle bounds, int horizKeyCount, int vertKeyCount) {
        if (horizKeyCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(horizKeyCount));
        }

        if (vertKeyCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(vertKeyCount));
        }

        int cols = horizKeyCount + 1;
        int rows = vertKeyCount + 1;

        var cells = new GridCell[cols * rows];
        double cellWidth = (double)bounds.Width / cols;
        double cellHeight = (double)bounds.Height / rows;

        for (int row = 0; row < rows; row++) {
            for (int col = 0; col < cols; col++) {
                int x = bounds.X + (int)Math.Round(col * cellWidth);
                int y = bounds.Y + (int)Math.Round(row * cellHeight);
                int nextX = bounds.X + (int)Math.Round((col + 1) * cellWidth);
                int nextY = bounds.Y + (int)Math.Round((row + 1) * cellHeight);

                cells[row * cols + col] = new GridCell(
                    Row: row,
                    Col: col,
                    Bounds: new Rectangle(x, y, nextX - x, nextY - y));
            }
        }

        int centerCol = horizKeyCount / 2;
        int centerRow = vertKeyCount / 2;

        return new CrosshairGrid(cells, cols, rows, centerCol, centerRow);
    }

    /// <summary>
    /// Returns the center point of a cell in physical pixels.
    /// </summary>
    public static Point CenterOf(GridCell cell) {
        return new Point(
            cell.Bounds.X + cell.Bounds.Width / 2,
            cell.Bounds.Y + cell.Bounds.Height / 2);
    }
}

/// <summary>
/// Result of crosshair grid calculation. Contains all cells plus metadata
/// about the grid dimensions and center position.
/// </summary>
public sealed class CrosshairGrid {
    public IReadOnlyList<GridCell> Cells { get; }
    public int Cols { get; }
    public int Rows { get; }
    public int CenterCol { get; }
    public int CenterRow { get; }

    public CrosshairGrid(GridCell[] cells, int cols, int rows, int centerCol, int centerRow) {
        Cells = cells;
        Cols = cols;
        Rows = rows;
        CenterCol = centerCol;
        CenterRow = centerRow;
    }

    /// <summary>Gets the cell at the given row and column.</summary>
    public GridCell CellAt(int row, int col) => Cells[row * Cols + col];

    /// <summary>Gets the center cell.</summary>
    public GridCell CenterCell => CellAt(CenterRow, CenterCol);

    /// <summary>True if the cell is on the center row or center column (the cross).</summary>
    public bool IsOnCross(int row, int col) => row == CenterRow || col == CenterCol;
}
