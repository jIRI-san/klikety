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
        // Cell smaller than 1.0/0.7 DIP → fontSize < 1.0 → returns 1.0
        var dipRect = new Rect(0, 0, 1.0, 1.0);

        double result = LogCrosshairRenderer.ComputeGradualFontSize(dipRect, baseFontSize: 14.0);

        Assert.Equal(1.0, result);
    }

    [Fact]
    public void ComputeGradualFontSize_NeverExceedsCellExtent() {
        // Font should never exceed 70% of cell extent
        for (int size = 2; size <= 200; size += 10) {
            var dipRect = new Rect(0, 0, size, size);
            double maxExpected = size * 0.7;

            double result = LogCrosshairRenderer.ComputeGradualFontSize(dipRect, baseFontSize: 14.0);

            Assert.True(result <= maxExpected + 0.001,
                $"Font size {result} exceeds {maxExpected} for cell size {size}");
            Assert.True(result >= 1.0,
                $"Font size {result} below 1.0 for cell size {size}");
        }
    }

    [Fact]
    public void ComputeGradualFontSize_SmallCell_ScalesWithExtent() {
        // Small cell — font = cell * 0.7
        var dipRect = new Rect(0, 0, 6.0, 6.0);

        double result = LogCrosshairRenderer.ComputeGradualFontSize(dipRect, baseFontSize: 14.0);

        Assert.Equal(4.2, result, precision: 5); // 6 * 0.7 = 4.2
    }

    [Fact]
    public void ComputeGradualFontSize_LargeCell_ScalesWithExtent() {
        // Large cell — font = cell * 0.7
        var dipRect = new Rect(0, 0, 200, 200);

        double result = LogCrosshairRenderer.ComputeGradualFontSize(dipRect, baseFontSize: 14.0);

        Assert.Equal(140.0, result, precision: 5); // 200 * 0.7 = 140
    }
}

public class LogCrosshairRendererSmallCellTests {
    [Theory]
    [InlineData(20.0, 100.0, 10.0, false)]  // height ok, width ok → not small
    [InlineData(17.9, 100.0, 10.0, true)]   // height 17.9 < 10 * 1.8 = 18 → small
    [InlineData(18.0, 100.0, 10.0, false)]  // height 18.0 = threshold → not small
    [InlineData(20.0, 31.9, 10.0, true)]    // halfWidth 15.95 < 10 * 1.6 = 16 → small
    [InlineData(20.0, 32.0, 10.0, false)]   // halfWidth 16.0 = threshold → not small
    [InlineData(0.0, 100.0, 10.0, true)]    // zero height → small
    [InlineData(20.0, 0.0, 10.0, true)]     // zero width → small
    public void IsSmallCell_ThresholdBehavior(double height, double width, double minFontSize, bool expected) {
        var dipRect = new Rect(0, 0, width, height);
        Assert.Equal(expected, LogCrosshairRenderer.IsSmallCell(dipRect, minFontSize));
    }

    [Theory]
    [InlineData(31.9, 10.0, true)]   // halfWidth 15.95 < 10 * 1.6 = 16 → narrow
    [InlineData(32.0, 10.0, false)]  // halfWidth 16.0 = threshold → not narrow
    [InlineData(33.0, 10.0, false)]  // halfWidth 16.5 > threshold → not narrow
    [InlineData(0.0, 10.0, true)]    // zero width → narrow
    [InlineData(12.7, 4.0, true)]    // halfWidth 6.35 < 4 * 1.6 = 6.4 → narrow
    [InlineData(12.8, 4.0, false)]   // halfWidth 6.4 = threshold → not narrow
    public void IsNarrowColumn_ThresholdBehavior(double width, double minFontSize, bool expected) {
        var dipRect = new Rect(0, 0, width, 100);
        Assert.Equal(expected, LogCrosshairRenderer.IsNarrowColumn(dipRect, minFontSize));
    }

    [Theory]
    [InlineData(17.9, 10.0, true)]   // 17.9 < 10 * 1.8 = 18 → short
    [InlineData(18.0, 10.0, false)]  // 18.0 = threshold → not short
    [InlineData(19.0, 10.0, false)]  // above threshold → not short
    [InlineData(0.0, 10.0, true)]    // zero height → short
    [InlineData(7.1, 4.0, true)]     // 7.1 < 4 * 1.8 = 7.2 → short
    [InlineData(7.2, 4.0, false)]    // 7.2 = threshold → not short
    public void IsShortRow_ThresholdBehavior(double height, double minFontSize, bool expected) {
        var dipRect = new Rect(0, 0, 100, height);
        Assert.Equal(expected, LogCrosshairRenderer.IsShortRow(dipRect, minFontSize));
    }

    [Fact]
    public void IsSmallCell_BothDimensionsSmall_ReportsSmall() {
        // Both height and width below threshold
        var dipRect = new Rect(0, 0, 10, 10);
        Assert.True(LogCrosshairRenderer.IsSmallCell(dipRect, 10.0));
    }

    [Fact]
    public void IsSmallCell_OnlyHeightSmall_ReportsSmall() {
        // Height below threshold, width ok
        var dipRect = new Rect(0, 0, 100, 5);
        Assert.True(LogCrosshairRenderer.IsSmallCell(dipRect, 10.0));
        Assert.False(LogCrosshairRenderer.IsNarrowColumn(dipRect, 10.0));
        Assert.True(LogCrosshairRenderer.IsShortRow(dipRect, 10.0));
    }

    [Fact]
    public void IsSmallCell_OnlyWidthSmall_ReportsSmall() {
        // Width below threshold, height ok
        var dipRect = new Rect(0, 0, 10, 100);
        Assert.True(LogCrosshairRenderer.IsSmallCell(dipRect, 10.0));
        Assert.True(LogCrosshairRenderer.IsNarrowColumn(dipRect, 10.0));
        Assert.False(LogCrosshairRenderer.IsShortRow(dipRect, 10.0));
    }
}
