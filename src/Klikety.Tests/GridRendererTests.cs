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
