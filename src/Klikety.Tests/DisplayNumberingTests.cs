using System.Drawing;

using Klikety.Services;

namespace Klikety.Tests;

public class DisplayNumberingTests {
    [Fact]
    public void AssignsSpatiallyForUnknownFingerprint() {
        var right = Row(@"\\?\DISPLAY#RIGHT", new Rectangle(1920, 0, 1920, 1080));
        var bottom = Row(@"\\?\DISPLAY#BOTTOM", new Rectangle(0, 1080, 1920, 1080));
        var left = Row(@"\\?\DISPLAY#LEFT", new Rectangle(0, 0, 1920, 1080));
        var sameOriginLaterPath = Row(@"\\?\DISPLAY#A-TIE", new Rectangle(0, 0, 800, 600));

        var numbers = DisplayNumbering.AssignSpatially([right, bottom, sameOriginLaterPath, left]);

        Assert.Equal(4, numbers.Count);
        Assert.Equal(1, numbers[sameOriginLaterPath.DevicePath]);
        Assert.Equal(2, numbers[left.DevicePath]);
        Assert.Equal(3, numbers[bottom.DevicePath]);
        Assert.Equal(4, numbers[right.DevicePath]);
    }

    private static DisplayInfo Row(string devicePath, Rectangle bounds) =>
        new(bounds, 1.0, @"\\.\DISPLAY1", devicePath);
}
