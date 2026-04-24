using Klikety.Overlay;

namespace Klikety.Tests;

public class GridRendererTests {
    [Theory]
    [InlineData(17.9, 10.0, true)]   // 17.9 < 10 * 1.8 = 18 → external
    [InlineData(18.0, 10.0, false)]  // 18.0 = 10 * 1.8 → inline
    [InlineData(20.0, 10.0, false)]  // 20.0 > 18 → inline
    [InlineData(7.1, 4.0, true)]     // 7.1 < 4 * 1.8 = 7.2 → external
    [InlineData(7.2, 4.0, false)]    // 7.2 = 4 * 1.8 → inline
    [InlineData(0.0, 10.0, true)]    // zero height → external
    public void ShouldUseExternalLabels_ThresholdBehavior(double cellHeight, double minFontSize, bool expected) {
        Assert.Equal(expected, GridRenderer.ShouldUseExternalLabels(cellHeight, minFontSize));
    }
}
