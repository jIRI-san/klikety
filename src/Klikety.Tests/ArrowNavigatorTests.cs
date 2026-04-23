using Klikety.Navigation;

namespace Klikety.Tests;

public class ArrowNavigatorTests
{
    [Theory]
    [InlineData(0, 9, 72, 8)]    // col 0 → wraps to col 8
    [InlineData(5, 9, 72, 4)]    // col 5 → col 4
    [InlineData(9, 9, 72, 17)]   // row 1 col 0 → row 1 col 8
    public void MoveLeft_WrapsCorrectly(int current, int cols, int total, int expected)
    {
        Assert.Equal(expected, ArrowNavigator.MoveLeft(current, cols, total));
    }

    [Theory]
    [InlineData(8, 9, 72, 0)]    // col 8 → wraps to col 0
    [InlineData(5, 9, 72, 6)]    // col 5 → col 6
    [InlineData(17, 9, 72, 9)]   // row 1 col 8 → row 1 col 0
    public void MoveRight_WrapsCorrectly(int current, int cols, int total, int expected)
    {
        Assert.Equal(expected, ArrowNavigator.MoveRight(current, cols, total));
    }

    [Theory]
    [InlineData(0, 9, 72, 63)]    // row 0 → wraps to row 7
    [InlineData(18, 9, 72, 9)]    // row 2 → row 1
    public void MoveUp_WrapsCorrectly(int current, int cols, int total, int expected)
    {
        Assert.Equal(expected, ArrowNavigator.MoveUp(current, cols, total));
    }

    [Theory]
    [InlineData(63, 9, 72, 0)]    // row 7 → wraps to row 0
    [InlineData(9, 9, 72, 18)]    // row 1 → row 2
    public void MoveDown_WrapsCorrectly(int current, int cols, int total, int expected)
    {
        Assert.Equal(expected, ArrowNavigator.MoveDown(current, cols, total));
    }

    [Fact]
    public void SingleCellGrid_AllMovesReturnSameIndex()
    {
        Assert.Equal(0, ArrowNavigator.MoveLeft(0, 1, 1));
        Assert.Equal(0, ArrowNavigator.MoveRight(0, 1, 1));
        Assert.Equal(0, ArrowNavigator.MoveUp(0, 1, 1));
        Assert.Equal(0, ArrowNavigator.MoveDown(0, 1, 1));
    }

    [Fact]
    public void SingleRow_UpDownReturnSame()
    {
        // 1 row × 5 cols
        Assert.Equal(2, ArrowNavigator.MoveUp(2, 5, 5));
        Assert.Equal(2, ArrowNavigator.MoveDown(2, 5, 5));
    }

    [Fact]
    public void SingleColumn_LeftRightWrap()
    {
        // 5 rows × 1 col — all indices 0
        Assert.Equal(0, ArrowNavigator.MoveLeft(0, 1, 5));
        Assert.Equal(0, ArrowNavigator.MoveRight(0, 1, 5));
    }
}
