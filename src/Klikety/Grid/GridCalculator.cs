using System.Drawing;

namespace Klikety.Grid;

/// <summary>
/// Divides screen bounds into a grid of cells based on key set sizes.
/// All coordinates are in physical pixels.
/// </summary>
public static class GridCalculator
{
    /// <summary>
    /// Computes the level-1 grid cells for the given screen bounds and key set sizes.
    /// </summary>
    /// <param name="screenBounds">Primary screen bounds in physical pixels.</param>
    /// <param name="cols">Number of columns (firstKeys count).</param>
    /// <param name="rows">Number of rows (secondKeys count).</param>
    public static IReadOnlyList<GridCell> Calculate(Rectangle screenBounds, int cols, int rows)
    {
        if (cols <= 0) throw new ArgumentOutOfRangeException(nameof(cols));
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));

        var cells = new GridCell[cols * rows];
        double cellWidth = (double)screenBounds.Width / cols;
        double cellHeight = (double)screenBounds.Height / rows;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int x = screenBounds.X + (int)Math.Round(col * cellWidth);
                int y = screenBounds.Y + (int)Math.Round(row * cellHeight);
                int nextX = screenBounds.X + (int)Math.Round((col + 1) * cellWidth);
                int nextY = screenBounds.Y + (int)Math.Round((row + 1) * cellHeight);

                cells[row * cols + col] = new GridCell(
                    Row: row,
                    Col: col,
                    Bounds: new Rectangle(x, y, nextX - x, nextY - y));
            }
        }

        return cells;
    }

    /// <summary>
    /// Returns the center point of a cell in physical pixels.
    /// </summary>
    public static Point CenterOf(GridCell cell)
    {
        return new Point(
            cell.Bounds.X + cell.Bounds.Width / 2,
            cell.Bounds.Y + cell.Bounds.Height / 2);
    }
}
