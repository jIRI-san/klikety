using System.Drawing;
using Klikety.Grid;

namespace Klikety.Tests;

public class SubgridCalculatorTests
{
    [Fact]
    public void Calculate_SubgridWithinParentBounds()
    {
        var parent = new GridCell(0, 0, new Rectangle(0, 0, 213, 135));
        var cells = SubgridCalculator.Calculate(parent, 9, 8);

        Assert.Equal(72, cells.Count);

        // All subgrid cells must be within parent bounds
        foreach (var cell in cells)
        {
            Assert.True(cell.Bounds.X >= parent.Bounds.X);
            Assert.True(cell.Bounds.Y >= parent.Bounds.Y);
            Assert.True(cell.Bounds.Right <= parent.Bounds.Right);
            Assert.True(cell.Bounds.Bottom <= parent.Bounds.Bottom);
        }
    }

    [Theory]
    [InlineData(500, 400, 40000, true)]   // 200000 > 40000
    [InlineData(100, 100, 40000, false)]   // 10000 < 40000
    [InlineData(200, 200, 40000, false)]   // 40000 = 40000 (not >)
    [InlineData(201, 200, 40000, true)]    // 40200 > 40000
    public void ShouldActivateLevel3_ThresholdComparison(int w, int h, int threshold, bool expected)
    {
        var cell = new GridCell(0, 0, new Rectangle(0, 0, w, h));
        Assert.Equal(expected, SubgridCalculator.ShouldActivateLevel3(cell, threshold));
    }
}
