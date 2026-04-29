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
        return arrowKey switch {
            VKey.Left => MoveHorizontal(currentRow, currentCol, centerRow, totalCols, -1),
            VKey.Right => MoveHorizontal(currentRow, currentCol, centerRow, totalCols, +1),
            VKey.Up => MoveVertical(currentRow, currentCol, centerCol, totalRows, -1),
            VKey.Down => MoveVertical(currentRow, currentCol, centerCol, totalRows, +1),
            _ => (currentRow, currentCol),
        };
    }

    private static (int Row, int Col) MoveHorizontal(
        int row, int col, int centerRow, int totalCols, int direction) {
        // Can only move horizontally if on center row
        if (row != centerRow) {
            return (row, col);
        }

        int newCol = (col + direction + totalCols) % totalCols;
        return (centerRow, newCol);
    }

    private static (int Row, int Col) MoveVertical(
        int row, int col, int centerCol, int totalRows, int direction) {
        // Can only move vertically if on center column
        if (col != centerCol) {
            return (row, col);
        }

        int newRow = (row + direction + totalRows) % totalRows;
        return (newRow, centerCol);
    }
}
