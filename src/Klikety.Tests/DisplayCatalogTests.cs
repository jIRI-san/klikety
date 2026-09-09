using System.Drawing;

using Klikety.Services;

namespace Klikety.Tests;

public class DisplayCatalogTests {
    private static readonly Rectangle VirtualScreen = new(-1920, 0, 3840, 1080);

    [Fact]
    public void TryMatch_EmptyDevicePath_Fails() {
        var monitors = new[] { Monitor(@"\\.\DISPLAY1", new Rectangle(0, 0, 1920, 1080)) };
        var ccd = new[] { new DisplayCatalog.CcdTarget(@"\\.\DISPLAY1", "") };

        var result = DisplayCatalog.TryMatch(monitors, ccd, VirtualScreen);

        Assert.False(result.Success);
        Assert.Null(result.Snapshot);
        Assert.Contains("empty", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryMatch_DuplicateDevicePath_Fails() {
        var monitors = new[] {
            Monitor(@"\\.\DISPLAY1", new Rectangle(0, 0, 1920, 1080)),
            Monitor(@"\\.\DISPLAY2", new Rectangle(1920, 0, 1920, 1080)),
        };
        var ccd = new[] {
            new DisplayCatalog.CcdTarget(@"\\.\DISPLAY1", @"\\?\DISPLAY#SAME"),
            new DisplayCatalog.CcdTarget(@"\\.\DISPLAY2", @"\\?\DISPLAY#SAME"),
        };

        var result = DisplayCatalog.TryMatch(monitors, ccd, VirtualScreen);

        Assert.False(result.Success);
        Assert.Null(result.Snapshot);
        Assert.Contains("duplicated", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryMatch_CountMismatch_Fails() {
        var monitors = new[] { Monitor(@"\\.\DISPLAY1", new Rectangle(0, 0, 1920, 1080)) };
        var ccd = new[] {
            new DisplayCatalog.CcdTarget(@"\\.\DISPLAY1", @"\\?\DISPLAY#A"),
            new DisplayCatalog.CcdTarget(@"\\.\DISPLAY2", @"\\?\DISPLAY#B"),
        };

        var result = DisplayCatalog.TryMatch(monitors, ccd, VirtualScreen);

        Assert.False(result.Success);
        Assert.Null(result.Snapshot);
        Assert.Contains("does not match", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryMatch_UnmatchedGdiName_Fails() {
        var monitors = new[] { Monitor(@"\\.\DISPLAY1", new Rectangle(0, 0, 1920, 1080)) };
        var ccd = new[] { new DisplayCatalog.CcdTarget(@"\\.\DISPLAY9", @"\\?\DISPLAY#A") };

        var result = DisplayCatalog.TryMatch(monitors, ccd, VirtualScreen);

        Assert.False(result.Success);
        Assert.Null(result.Snapshot);
        Assert.Contains("No CCD path", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMatch_ValidRows_ReturnsFrozenSnapshot() {
        var monitors = new[] {
            Monitor(@"\\.\DISPLAY2", new Rectangle(1920, 0, 1920, 1080), 1.5),
            Monitor(@"\\.\DISPLAY1", new Rectangle(-1920, 0, 1920, 1080), 1.0),
        };
        var ccd = new[] {
            new DisplayCatalog.CcdTarget(@"\\.\DISPLAY1", @"\\?\DISPLAY#LEFT"),
            new DisplayCatalog.CcdTarget(@"\\.\DISPLAY2", @"\\?\DISPLAY#RIGHT"),
        };

        var result = DisplayCatalog.TryMatch(monitors, ccd, VirtualScreen);

        Assert.True(result.Success);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(VirtualScreen, result.Snapshot.VirtualScreen);
        Assert.Equal(2, result.Snapshot.Displays.Count);
        Assert.Equal(@"\\?\DISPLAY#RIGHT", result.Snapshot.Displays[0].DevicePath);
        Assert.Equal(@"\\?\DISPLAY#LEFT", result.Snapshot.Displays[1].DevicePath);
        Assert.Equal(1.5, result.Snapshot.Displays[0].DpiScale);
        Assert.Throws<NotSupportedException>(() => ((IList<DisplayInfo>)result.Snapshot.Displays).Add(
            MonitorInfo(@"\\.\DISPLAY3", Rectangle.Empty, @"\\?\X")));
    }

    [Fact]
    public void FindContaining_PointOnSecondary_ReturnsThatDisplay() {
        var monitors = new[] {
            Monitor(@"\\.\DISPLAY1", new Rectangle(-1920, 0, 1920, 1080)),
            Monitor(@"\\.\DISPLAY2", new Rectangle(0, 0, 1920, 1080)),
        };
        var ccd = new[] {
            new DisplayCatalog.CcdTarget(@"\\.\DISPLAY1", @"\\?\DISPLAY#LEFT"),
            new DisplayCatalog.CcdTarget(@"\\.\DISPLAY2", @"\\?\DISPLAY#PRIMARY"),
        };

        var result = DisplayCatalog.TryMatch(monitors, ccd, VirtualScreen);
        Assert.True(result.Success);
        var found = result.Snapshot!.FindContaining(new Point(100, 100));
        Assert.NotNull(found);
        Assert.Equal(@"\\?\DISPLAY#PRIMARY", found.DevicePath);
        Assert.Null(result.Snapshot.FindContaining(new Point(-4000, 0)));
    }

    private static DisplayCatalog.EnumeratedMonitor Monitor(string gdiName, Rectangle bounds, double dpi = 1.0) =>
        new(gdiName, bounds, dpi);

    private static DisplayInfo MonitorInfo(string gdiName, Rectangle bounds, string devicePath) =>
        new(bounds, 1.0, gdiName, devicePath);
}
