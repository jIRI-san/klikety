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

    // --- From center row (non-center col): only left/right ---

    [Fact]
    public void OnCenterRow_OffCenter_RightWorks() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Right, CenterRow, 7, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((CenterRow, 8), (r, c));
    }

    [Fact]
    public void OnCenterRow_OffCenter_UpBlocked() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Up, CenterRow, 7, CenterRow, CenterCol, TotalRows, TotalCols);
        // Not on center col → blocked
        Assert.Equal((CenterRow, 7), (r, c));
    }

    [Fact]
    public void OnCenterRow_OffCenter_DownBlocked() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Down, CenterRow, 7, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((CenterRow, 7), (r, c));
    }

    // --- From center col (non-center row): only up/down ---

    [Fact]
    public void OnCenterCol_OffCenter_DownWorks() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Down, 3, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((4, CenterCol), (r, c));
    }

    [Fact]
    public void OnCenterCol_OffCenter_LeftBlocked() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Left, 3, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        // Not on center row → blocked
        Assert.Equal((3, CenterCol), (r, c));
    }

    [Fact]
    public void OnCenterCol_OffCenter_RightBlocked() {
        var (r, c) = CrossArrowNavigator.Move(VKey.Right, 3, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((3, CenterCol), (r, c));
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

    // --- Non-cross cell: all directions blocked ---

    [Fact]
    public void OffCross_AllBlocked() {
        // Position (2, 8) — neither center row nor center col
        var pos = (Row: 2, Col: 8);

        Assert.Equal(pos, CrossArrowNavigator.Move(VKey.Left, pos.Row, pos.Col, CenterRow, CenterCol, TotalRows, TotalCols));
        Assert.Equal(pos, CrossArrowNavigator.Move(VKey.Right, pos.Row, pos.Col, CenterRow, CenterCol, TotalRows, TotalCols));
        Assert.Equal(pos, CrossArrowNavigator.Move(VKey.Up, pos.Row, pos.Col, CenterRow, CenterCol, TotalRows, TotalCols));
        Assert.Equal(pos, CrossArrowNavigator.Move(VKey.Down, pos.Row, pos.Col, CenterRow, CenterCol, TotalRows, TotalCols));
    }

    // --- Unknown key returns same position ---

    [Fact]
    public void UnknownKey_NoMove() {
        var (r, c) = CrossArrowNavigator.Move(VKey.A, CenterRow, CenterCol, CenterRow, CenterCol, TotalRows, TotalCols);
        Assert.Equal((CenterRow, CenterCol), (r, c));
    }
}
