using System.Drawing;

namespace Klikety.Grid;

/// <summary>
/// Result of a log-scale grid calculation.
/// All coordinates are in physical pixels.
/// </summary>
public sealed class LogGrid {
    public GridCell[] Cells { get; }
    public int Cols { get; }
    public int Rows { get; }

    /// <summary>Cursor position used when computing the grid (center dividing edge).</summary>
    public Point CenterPoint { get; }

    /// <summary>Column edge positions in physical pixels. Length = Cols + 1.</summary>
    public double[] ColEdges { get; }

    /// <summary>Row edge positions in physical pixels. Length = Rows + 1.</summary>
    public double[] RowEdges { get; }

    public LogGrid(
        GridCell[] cells, int cols, int rows,
        Point centerPoint, double[] colEdges, double[] rowEdges) {
        Cells = cells;
        Cols = cols;
        Rows = rows;
        CenterPoint = centerPoint;
        ColEdges = colEdges;
        RowEdges = rowEdges;
    }

    public GridCell CellAt(int row, int col) => Cells[row * Cols + col];
}
