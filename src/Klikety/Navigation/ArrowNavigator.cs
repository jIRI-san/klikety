namespace Klikety.Navigation;

/// <summary>
/// Stateless helper for arrow-key cell navigation within a grid level.
/// Pure functions — fully unit-testable.
/// </summary>
public static class ArrowNavigator
{
    public static int MoveLeft(int currentIndex, int cols, int total)
    {
        int row = currentIndex / cols;
        int col = currentIndex % cols;
        col = col == 0 ? cols - 1 : col - 1;
        return row * cols + col;
    }

    public static int MoveRight(int currentIndex, int cols, int total)
    {
        int row = currentIndex / cols;
        int col = currentIndex % cols;
        col = (col + 1) % cols;
        return row * cols + col;
    }

    public static int MoveUp(int currentIndex, int cols, int total)
    {
        int rows = (total + cols - 1) / cols;
        int row = currentIndex / cols;
        int col = currentIndex % cols;
        row = row == 0 ? rows - 1 : row - 1;
        int index = row * cols + col;
        return index < total ? index : currentIndex;
    }

    public static int MoveDown(int currentIndex, int cols, int total)
    {
        int rows = (total + cols - 1) / cols;
        int row = currentIndex / cols;
        int col = currentIndex % cols;
        row = (row + 1) % rows;
        int index = row * cols + col;
        return index < total ? index : currentIndex;
    }
}
