using Klikety.Overlay;

namespace Klikety.Tests;

public class SatelliteWindowTests {
    [Fact]
    public void FontSizeIsFortyPercentClamped() {
        Assert.Equal(192, SatelliteWindow.DigitFontSize(480, 800));
        Assert.Equal(96, SatelliteWindow.DigitFontSize(100, 100));
        Assert.Equal(400, SatelliteWindow.DigitFontSize(2000, 2000));
    }
}
