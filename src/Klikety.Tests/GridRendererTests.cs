using System.Windows;

using Klikety.Overlay;

namespace Klikety.Tests;

public class GridRendererTests {
    [Theory]
    [InlineData(20.0, 100.0, 10.0, false)]  // height ok, width ok → inline
    [InlineData(17.9, 100.0, 10.0, true)]   // 17.9 < 10 * 1.8 = 18 → external (height)
    [InlineData(18.0, 100.0, 10.0, false)]  // 18.0 = 10 * 1.8, width ok → inline
    [InlineData(20.0, 31.9, 10.0, true)]    // half-width 15.95 < 10 * 1.6 = 16 → external (width)
    [InlineData(20.0, 32.0, 10.0, false)]   // half-width 16.0 = 10 * 1.6 → inline
    [InlineData(7.1, 100.0, 4.0, true)]     // 7.1 < 4 * 1.8 = 7.2 → external (height)
    [InlineData(7.2, 100.0, 4.0, false)]    // 7.2 = 4 * 1.8 → inline
    [InlineData(0.0, 100.0, 10.0, true)]    // zero height → external
    [InlineData(20.0, 0.0, 10.0, true)]     // zero width → external
    public void ShouldUseExternalLabels_ThresholdBehavior(double cellHeight, double cellWidth, double minFontSize, bool expected) {
        Assert.Equal(expected, GridRenderer.ShouldUseExternalLabels(cellHeight, cellWidth, minFontSize));
    }

    [Fact]
    public void ComputeFanOut_Labelsfit_NoFanOut() {
        // 4 labels × 20px each + 2px gap = 88px needed, 100px available → no fan-out
        var (distance, extent) = GridRenderer.ComputeFanOut(4, 20.0, 100.0, 15.0);
        Assert.Equal(15.0, distance); // standard margin
        Assert.Equal(100.0, extent);  // grid extent unchanged
    }

    [Fact]
    public void ComputeFanOut_LabelsOverlap_FansOut() {
        // 8 labels × 20px each + 2px gap = 176px needed, 100px available → fan-out
        var (distance, extent) = GridRenderer.ComputeFanOut(8, 20.0, 100.0, 15.0);
        Assert.True(distance > 15.0); // distance increased beyond standard margin
        Assert.True(extent > 100.0);  // labels spread wider than grid
        Assert.Equal(176.0, extent);  // 8 * (20 + 2)
        Assert.Equal(15.0 + (176.0 - 100.0) / 2, distance); // standardMargin + half spread
    }

    [Fact]
    public void ComputeFanOut_ExactFit_NoFanOut() {
        // 4 labels × 23px each + 2px gap = 100px needed, 100px available → exact fit
        var (distance, extent) = GridRenderer.ComputeFanOut(4, 23.0, 100.0, 15.0);
        Assert.Equal(15.0, distance);
        Assert.Equal(100.0, extent);
    }
}

public class LogCrosshairRendererFontTests {
    [Fact]
    public void ComputeGradualFontSize_TinyCell_Returns1() {
        // Cell smaller than 1.0/0.8 DIP → cellFitSize < 1.0 → returns 1.0
        var dipRect = new Rect(0, 0, 1.0, 1.0); // cellFitSize = 0.8
        var outerDip = new Rect(0, 0, 100, 100);

        double result = LogCrosshairRenderer.ComputeGradualFontSize(
            dipRect, row: 0, col: 0, centerRow: 5, centerCol: 5,
            totalRows: 11, totalCols: 11, outerDip, baseFontSize: 14.0);

        Assert.Equal(1.0, result);
    }

    [Fact]
    public void ComputeGradualFontSize_NeverExceedsCellFitSize() {
        // Test across all positions of an 11×11 grid with tiny cells (3×3 DIP)
        var outerDip = new Rect(0, 0, 100, 100);
        int rows = 11, cols = 11;
        int centerRow = 5, centerCol = 5;

        for (int r = 0; r < rows; r++) {
            for (int c = 0; c < cols; c++) {
                var dipRect = new Rect(0, 0, 3.0, 3.0);
                double cellFitSize = 3.0 * 0.8; // 2.4

                double result = LogCrosshairRenderer.ComputeGradualFontSize(
                    dipRect, r, c, centerRow, centerCol, rows, cols, outerDip, baseFontSize: 14.0);

                Assert.True(result <= cellFitSize,
                    $"Font size {result} exceeds cellFitSize {cellFitSize} at ({r},{c})");
                Assert.True(result >= 1.0,
                    $"Font size {result} below 1.0 at ({r},{c})");
            }
        }
    }

    [Fact]
    public void ComputeGradualFontSize_CenterCell_CapsToCell() {
        // Center cell with small extent — should cap baseFontSize to cellFitSize
        var dipRect = new Rect(0, 0, 6.0, 6.0); // cellFitSize = 4.8
        var outerDip = new Rect(0, 0, 100, 100);

        double result = LogCrosshairRenderer.ComputeGradualFontSize(
            dipRect, row: 5, col: 5, centerRow: 5, centerCol: 5,
            totalRows: 11, totalCols: 11, outerDip, baseFontSize: 14.0);

        Assert.Equal(4.8, result, precision: 5);
    }

    [Fact]
    public void ComputeGradualFontSize_NormalCell_NoThrow() {
        // Normal-sized cells should return a value without throwing
        var outerDip = new Rect(0, 0, 200, 200);
        var dipRect = new Rect(0, 0, 50, 50);

        double result = LogCrosshairRenderer.ComputeGradualFontSize(
            dipRect, row: 2, col: 3, centerRow: 5, centerCol: 5,
            totalRows: 11, totalCols: 11, outerDip, baseFontSize: 14.0);

        Assert.True(result >= 1.0);
        Assert.True(result <= 50.0 * 0.8);
    }
}
