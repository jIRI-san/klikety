using System.Drawing;

using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class CoordinatorDisplaySwitchTests {
    [Fact]
    public void ActivatesWhenCursorOnSecondary() {
        var (_, hotKey, hook, _, overlay, _, platform, _) = CoordinatorTestHelper.CreateCoordinator();
        var primary = new DisplayInfo(new Rectangle(0, 0, 1920, 1080), 1.0, @"\\.\DISPLAY1", @"\\?\A");
        var secondary = new DisplayInfo(new Rectangle(1920, 0, 1920, 1080), 1.0, @"\\.\DISPLAY2", @"\\?\B");
        platform.DisplayCatalog.Result = DisplayCatalogResult.Ok(
            new DisplaySnapshot([primary, secondary], new Rectangle(0, 0, 3840, 1080)));
        platform.Cursor.Position = new Point(2500, 500);

        hotKey.SimulateActivation();

        Assert.True(overlay.IsVisible);
        Assert.True(hook.IsEnabled);
        Assert.Equal(secondary.MonitorBounds, overlay.LastShowBounds);
    }
}
