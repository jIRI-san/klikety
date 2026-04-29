using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public class CrossArrowNavigatorTests {
    // 11×11 grid, center at (5, 5)
    private const int CenterRow = 5;
    private const int CenterCol = 5;
    private const int TotalRows = 11;
    private const int TotalCols = 11;

    // --- From center: all directions work ---

    [Fact]
    public void FromCenter_Right() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Right, CenterRow, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((CenterRow, 6), (r, c));
    }

    [Fact]
    public void FromCenter_Left() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Left, CenterRow, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((CenterRow, 4), (r, c));
    }

    [Fact]
    public void FromCenter_Down() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Down, CenterRow, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((6, CenterCol), (r, c));
    }

    [Fact]
    public void FromCenter_Up() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Up, CenterRow, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((4, CenterCol), (r, c));
    }

    // --- From center row (non-center col): all directions work ---

    [Fact]
    public void OnCenterRow_OffCenter_RightWorks() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Right, CenterRow, 7, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((CenterRow, 8), (r, c));
    }

    [Fact]
    public void OnCenterRow_OffCenter_UpMoves() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Up, CenterRow, 7, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((CenterRow - 1, 7), (r, c));
    }

    [Fact]
    public void OnCenterRow_OffCenter_DownMoves() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Down, CenterRow, 7, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((CenterRow + 1, 7), (r, c));
    }

    // --- From center col (non-center row): all directions work ---

    [Fact]
    public void OnCenterCol_OffCenter_DownWorks() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Down, 3, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((4, CenterCol), (r, c));
    }

    [Fact]
    public void OnCenterCol_OffCenter_LeftMoves() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Left, 3, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((3, CenterCol - 1), (r, c));
    }

    [Fact]
    public void OnCenterCol_OffCenter_RightMoves() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Right, 3, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((3, CenterCol + 1), (r, c));
    }

    // --- Wrapping at edges ---

    [Fact]
    public void Right_WrapsAtEnd() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Right, CenterRow, TotalCols - 1, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((CenterRow, 0), (r, c));
    }

    [Fact]
    public void Left_WrapsAtStart() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Left, CenterRow, 0, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((CenterRow, TotalCols - 1), (r, c));
    }

    [Fact]
    public void Down_WrapsAtBottom() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Down, TotalRows - 1, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((0, CenterCol), (r, c));
    }

    [Fact]
    public void Up_WrapsAtTop() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Up, 0, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((TotalRows - 1, CenterCol), (r, c));
    }

    // --- Off-cross cell: all directions work (free 2D) ---

    [Fact]
    public void OffCross_AllDirectionsWork() {
        // Position (2, 8) — neither center row nor center col — still moves freely
        Assert.Equal((2, 7), CrossArrowNavigator.Move(VKey.Left, 2, 8, CenterRow, CenterCol, TotalRows, TotalCols));
        Assert.Equal((2, 9), CrossArrowNavigator.Move(VKey.Right, 2, 8, CenterRow, CenterCol, TotalRows, TotalCols));
        Assert.Equal((1, 8), CrossArrowNavigator.Move(VKey.Up, 2, 8, CenterRow, CenterCol, TotalRows, TotalCols));
        Assert.Equal((3, 8), CrossArrowNavigator.Move(VKey.Down, 2, 8, CenterRow, CenterCol, TotalRows, TotalCols));
    }

    // --- Wrapping from off-cross positions ---

    [Fact]
    public void OffCross_Left_WrapsAtCol0() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Left, 2, 0, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((2, TotalCols - 1), (r, c));
    }

    [Fact]
    public void OffCross_Right_WrapsAtLastCol() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Right, 2, TotalCols - 1, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((2, 0), (r, c));
    }

    [Fact]
    public void OffCross_Up_WrapsAtRow0() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Up, 0, 8, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((TotalRows - 1, 8), (r, c));
    }

    [Fact]
    public void OffCross_Down_WrapsAtLastRow() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Down, TotalRows - 1, 8, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((0, 8), (r, c));
    }

    // --- Unknown key returns same position ---

    [Fact]
    public void UnknownKey_NoMove() {
        var (r, c) = CrossArrowNavigator.Move(VKey.A, CenterRow, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((CenterRow, CenterCol), (r, c));
    }
}
