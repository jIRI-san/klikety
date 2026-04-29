using Klikety.Input;

namespace Klikety.Navigation;

/// <summary>
/// Arrow navigator for crosshair grids. Traverses center row and center column only.
/// Axis switch only at center cell. Wrapping at arm ends.
/// </summary>
public static class CrossArrowNavigator {
    /// <summary>
    /// Moves along the cross arms. Returns new (row, col) position.
    /// From center: all directions work. From non-center: only along the current arm.
    /// </summary>
    public static (int Row, int Col) Move(
        VKey arrowKey, int currentRow, int currentCol,
        int centerRow, int centerCol, int totalRows, int totalCols) {
        bool atCenter = currentRow == centerRow && currentCol == centerCol;

        return arrowKey switch {
            VKey.Left => MoveLeft(currentRow, currentCol, centerRow, centerCol, totalCols, atCenter),
            VKey.Right => MoveRight(currentRow, currentCol, centerRow, centerCol, totalCols, atCenter),
            VKey.Up => MoveUp(currentRow, currentCol, centerRow, centerCol, totalRows, atCenter),
            VKey.Down => MoveDown(currentRow, currentCol, centerRow, centerCol, totalRows, atCenter),
            _ => (currentRow, currentCol),
        };
    }

    private static (int Row, int Col) MoveLeft(
        int row, int col, int centerRow, int centerCol, int totalCols, bool atCenter) {
        // Can only move left if on center row
        if (row != centerRow && !atCenter) {
            return (row, col);
        }

        // Move on center row
        int newCol = col == 0 ? totalCols - 1 : col - 1;
        return (centerRow, newCol);
    }

    private static (int Row, int Col) MoveRight(
        int row, int col, int centerRow, int centerCol, int totalCols, bool atCenter) {
        if (row != centerRow && !atCenter) {
            return (row, col);
        }

        int newCol = (col + 1) % totalCols;
        return (centerRow, newCol);
    }

    private static (int Row, int Col) MoveUp(
        int row, int col, int centerRow, int centerCol, int totalRows, bool atCenter) {
        // Can only move up if on center column
        if (col != centerCol && !atCenter) {
            return (row, col);
        }

        int newRow = row == 0 ? totalRows - 1 : row - 1;
        return (newRow, centerCol);
    }

    private static (int Row, int Col) MoveDown(
        int row, int col, int centerRow, int centerCol, int totalRows, bool atCenter) {
        if (col != centerCol && !atCenter) {
            return (row, col);
        }

        int newRow = (row + 1) % totalRows;
        return (newRow, centerCol);
    }
}
