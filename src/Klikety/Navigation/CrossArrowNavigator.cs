using Klikety.Input;

namespace Klikety.Navigation;

/// <summary>
/// Arrow navigator for crosshair grids. Free 2D movement:
/// Left/Right change column (wrapping), Up/Down change row (wrapping), independently.
/// </summary>
public static class CrossArrowNavigator {
    /// <summary>
    /// Moves to a new (row, col) position. Left/Right change column,
    /// Up/Down change row, each with wrapping. Movement is unrestricted —
    /// works from any position, not just center row/col.
    /// </summary>
    public static (int Row, int Col) Move(
        VKey arrowKey, int currentRow, int currentCol,
        int centerRow, int centerCol, // Retained for API compatibility; unused after free-2D rewrite
        int totalRows, int totalCols) {
        return arrowKey switch {
            VKey.Left => (currentRow, (currentCol - 1 + totalCols) % totalCols),
            VKey.Right => (currentRow, (currentCol + 1) % totalCols),
            VKey.Up => ((currentRow - 1 + totalRows) % totalRows, currentCol),
            VKey.Down => ((currentRow + 1) % totalRows, currentCol),
            _ => (currentRow, currentCol),
        };
    }
}
