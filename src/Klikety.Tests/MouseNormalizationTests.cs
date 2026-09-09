using System.Drawing;

using Klikety.Services;

namespace Klikety.Tests;

public class MouseNormalizationTests {
    [Fact]
    public void NegativeVirtualOrigin() {
        var virtualScreen = new Rectangle(-1920, 0, 3840, 1080);

        var origin = MouseActionService.NormalizeAbsolute(new Point(-1920, 0), virtualScreen);
        Assert.Equal(0, origin.X);
        Assert.Equal(0, origin.Y);

        var farCorner = MouseActionService.NormalizeAbsolute(new Point(1919, 1079), virtualScreen);
        Assert.Equal(65535, farCorner.X);
        Assert.Equal(65535, farCorner.Y);

        var primaryOrigin = MouseActionService.NormalizeAbsolute(new Point(0, 0), virtualScreen);
        Assert.InRange(primaryOrigin.X, 1, 65534);
        Assert.Equal(0, primaryOrigin.Y);
    }
}
