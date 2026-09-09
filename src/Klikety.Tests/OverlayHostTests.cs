using System.Drawing;

using Klikety.Overlay;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class OverlayHostTests {
    [Fact]
    public void ShowsOneWindowPerDisplay() {
        var nav = new FakeOverlayWindow();
        var satellites = new List<FakeSatelliteOverlay>();
        using var host = new OverlayHost(nav, () => {
            var s = new FakeSatelliteOverlay();
            satellites.Add(s);
            return s;
        });

        var primary = new DisplayInfo(new Rectangle(0, 0, 1920, 1080), 1.0, @"\\.\DISPLAY1", @"\\?\A");
        var secondary = new DisplayInfo(new Rectangle(1920, 0, 1920, 1080), 1.0, @"\\.\DISPLAY2", @"\\?\B");
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal) {
            [primary.DevicePath] = 1,
            [secondary.DevicePath] = 2,
        };

        host.Show([primary, secondary], primary, numbers);

        Assert.Equal(2, host.WindowCount);
        Assert.Equal(1, nav.ShowCount);
        Assert.Equal(primary.MonitorBounds, nav.LastShowBounds);
        Assert.Single(satellites);
        Assert.Equal(1, satellites[0].ShowCount);
        Assert.Equal(secondary.MonitorBounds, satellites[0].LastBounds);
        Assert.Equal(2, satellites[0].LastNumber);
        Assert.Equal(1, nav.ShowCount);
    }

    [Fact]
    public void SingleDisplay_HasNoSatellites() {
        var nav = new FakeOverlayWindow();
        var satellites = new List<FakeSatelliteOverlay>();
        using var host = new OverlayHost(nav, () => {
            var s = new FakeSatelliteOverlay();
            satellites.Add(s);
            return s;
        });

        var primary = new DisplayInfo(new Rectangle(0, 0, 1920, 1080), 1.0, @"\\.\DISPLAY1", @"\\?\A");
        host.Show([primary], primary, new Dictionary<string, int> { [primary.DevicePath] = 1 });

        Assert.Equal(1, host.WindowCount);
        Assert.Empty(satellites);
        Assert.True(nav.IsVisible);
    }

    [Fact]
    public void SwitchDoesNotRenumber() {
        var nav = new FakeOverlayWindow();
        using var host = new OverlayHost(nav, () => new FakeSatelliteOverlay());
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal) {
            [@"\\?\A"] = 2,
            [@"\\?\B"] = 1,
        };
        var primary = new DisplayInfo(new Rectangle(0, 0, 1920, 1080), 1.0, @"\\.\DISPLAY1", @"\\?\A");
        var secondary = new DisplayInfo(new Rectangle(1920, 0, 1920, 1080), 1.0, @"\\.\DISPLAY2", @"\\?\B");

        host.Show([primary, secondary], primary, numbers);
        host.Show([primary, secondary], secondary, numbers);

        Assert.Equal(2, host.LastNumbers[primary.DevicePath]);
        Assert.Equal(1, host.LastNumbers[secondary.DevicePath]);
    }
}
