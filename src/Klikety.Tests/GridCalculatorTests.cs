using System.Drawing;
using Klikety.Grid;

namespace Klikety.Tests;

public class GridCalculatorTests
{
    [Fact]
    public void Calculate_StandardGrid_ReturnsCorrectCellCount()
    {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        var cells = GridCalculator.Calculate(bounds, 9, 8);
        Assert.Equal(72, cells.Count);
    }

    [Fact]
    public void Calculate_CellsCoverFullScreen()
    {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        var cells = GridCalculator.Calculate(bounds, 9, 8);

        // First cell starts at screen origin
        Assert.Equal(0, cells[0].Bounds.X);
        Assert.Equal(0, cells[0].Bounds.Y);

        // Last cell ends at screen edge
        var lastCell = cells[^1];
        Assert.Equal(bounds.Width, lastCell.Bounds.X + lastCell.Bounds.Width);
        Assert.Equal(bounds.Height, lastCell.Bounds.Y + lastCell.Bounds.Height);
    }

    [Theory]
    [InlineData(1920, 1080, 9, 8)]   // 1080p
    [InlineData(2560, 1440, 9, 8)]   // 1440p
    [InlineData(3840, 2160, 9, 8)]   // 4K
    [InlineData(1920, 1200, 9, 8)]   // 1920x1200
    public void Calculate_VariousResolutions_NoCellGaps(int w, int h, int cols, int rows)
    {
        var bounds = new Rectangle(0, 0, w, h);
        var cells = GridCalculator.Calculate(bounds, cols, rows);

        // Verify no gaps between adjacent cells in same row
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols - 1; col++)
            {
                var current = cells[row * cols + col];
                var next = cells[row * cols + col + 1];
                Assert.Equal(current.Bounds.Right, next.Bounds.X);
            }
        }

        // Verify no gaps between adjacent rows
        for (int col = 0; col < cols; col++)
        {
            for (int row = 0; row < rows - 1; row++)
            {
                var current = cells[row * cols + col];
                var next = cells[(row + 1) * cols + col];
                Assert.Equal(current.Bounds.Bottom, next.Bounds.Y);
            }
        }
    }

    [Fact]
    public void Calculate_RowColIndicesCorrect()
    {
        var cells = GridCalculator.Calculate(new Rectangle(0, 0, 1920, 1080), 9, 8);
        var cell = cells[2 * 9 + 3]; // row 2, col 3
        Assert.Equal(2, cell.Row);
        Assert.Equal(3, cell.Col);
    }

    [Fact]
    public void CenterOf_ReturnsCellCenter()
    {
        var cell = new GridCell(0, 0, new Rectangle(100, 200, 50, 30));
        var center = GridCalculator.CenterOf(cell);
        Assert.Equal(125, center.X);
        Assert.Equal(215, center.Y);
    }

    [Fact]
    public void Calculate_ThrowsOnZeroCols()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridCalculator.Calculate(new Rectangle(0, 0, 1920, 1080), 0, 8));
    }

    [Fact]
    public void Calculate_ThrowsOnZeroRows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridCalculator.Calculate(new Rectangle(0, 0, 1920, 1080), 9, 0));
    }
}
