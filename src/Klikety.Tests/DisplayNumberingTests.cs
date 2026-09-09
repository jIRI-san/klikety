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

    [Fact]
    public void ReusesMapForKnownFingerprint() {
        var a = Row(@"\\?\DISPLAY#A", new Rectangle(0, 0, 1920, 1080));
        var b = Row(@"\\?\DISPLAY#B", new Rectangle(1920, 0, 1920, 1080));
        var stored = new Dictionary<string, int>(StringComparer.Ordinal) {
            [a.DevicePath] = 1,
            [b.DevicePath] = 2,
        };

        var aMoved = Row(a.DevicePath, new Rectangle(1920, 0, 1920, 1080));
        var bMoved = Row(b.DevicePath, new Rectangle(0, 0, 1920, 1080));
        var numbers = DisplayNumbering.Resolve(
            [aMoved, bMoved],
            [(DisplayNumbering.Fingerprint([a, b]), stored)],
            out bool isNew);

        Assert.False(isNew);
        Assert.Equal(1, numbers[a.DevicePath]);
        Assert.Equal(2, numbers[b.DevicePath]);
        Assert.Equal(1, DisplayNumbering.AssignSpatially([aMoved, bMoved])[b.DevicePath]);
    }

    [Fact]
    public void CapsAtNine() {
        var displays = Enumerable.Range(0, 10)
            .Select(i => Row($@"\\?\DISPLAY#{i:D2}", new Rectangle(i * 100, 0, 100, 100)))
            .ToArray();

        var numbers = DisplayNumbering.AssignSpatially(displays);

        Assert.Equal(9, numbers.Count);
        for (int i = 0; i < 9; i++) {
            Assert.Equal(i + 1, numbers[displays[i].DevicePath]);
        }

        Assert.False(numbers.ContainsKey(displays[9].DevicePath));
    }

    private static DisplayInfo Row(string devicePath, Rectangle bounds) =>
        new(bounds, 1.0, @"\\.\DISPLAY1", devicePath);
}
