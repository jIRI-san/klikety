using System.Drawing;

using Klikety.Config;
using Klikety.Input;
using Klikety.Overlay;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class CoordinatorDisplaySwitchTests {
    private static readonly DisplayInfo Primary =
        new(new Rectangle(0, 0, 1920, 1080), 1.0, @"\\.\DISPLAY1", @"\\?\A");
    private static readonly DisplayInfo Secondary =
        new(new Rectangle(1920, 0, 1920, 1080), 1.0, @"\\.\DISPLAY2", @"\\?\B");

    private static void ConfigureTwoDisplays(FakePlatformServices platform, Point cursor) {
        platform.DisplayCatalog.Result = DisplayCatalogResult.Ok(
            new DisplaySnapshot([Primary, Secondary], new Rectangle(0, 0, 3840, 1080)));
        platform.Cursor.Position = cursor;
    }

    [Fact]
    public void ActivatesWhenCursorOnSecondary() {
        var (_, hotKey, hook, _, overlay, _, platform, _) = CoordinatorTestHelper.CreateCoordinator();
        ConfigureTwoDisplays(platform, new Point(2500, 500));

        hotKey.SimulateActivation();

        Assert.True(overlay.IsVisible);
        Assert.True(hook.IsEnabled);
        Assert.Equal(Secondary.MonitorBounds, overlay.LastShowBounds);
    }

    [Fact]
    public void DigitMovesNavOverlay() {
        var (_, hotKey, hook, mouse, overlay, _, platform, _) = CoordinatorTestHelper.CreateCoordinator();
        ConfigureTwoDisplays(platform, new Point(100, 100));
        hotKey.SimulateActivation();
        int showBefore = overlay.ShowCount;

        hook.SimulateKey(VKey.D2);

        Assert.True(overlay.IsVisible);
        Assert.Equal(Secondary.MonitorBounds, overlay.LastShowBounds);
        Assert.True(overlay.ShowCount > showBefore);
        Assert.Contains(mouse.Calls, c => c.Action is null && c.Point == new Point(2880, 540));
    }

    [Fact]
    public void OwnDigitIsNoOp() {
        var (_, hotKey, hook, _, overlay, _, platform, _) = CoordinatorTestHelper.CreateCoordinator();
        ConfigureTwoDisplays(platform, new Point(100, 100));
        hotKey.SimulateActivation();
        int showBefore = overlay.ShowCount;

        hook.SimulateKey(VKey.D1);

        Assert.True(overlay.IsVisible);
        Assert.Equal(Primary.MonitorBounds, overlay.LastShowBounds);
        Assert.Equal(showBefore, overlay.ShowCount);
    }

    [Fact]
    public void SwitchResetsToL1SameMode() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
                Crosshair = new ModeConfig { Enabled = true, ChordKey = VKey.N, TwoKey = true, ArrowKeys = true },
            },
        };
        var (_, hotKey, hook, _, overlay, _, platform, _) = CoordinatorTestHelper.CreateCoordinator(configOverride: config);
        ConfigureTwoDisplays(platform, new Point(100, 100));
        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);

        hook.SimulateKey(VKey.D2);
        hook.SimulateKey(VKey.N);

        Assert.True(overlay.IsVisible);
        Assert.Equal(Secondary.MonitorBounds, overlay.LastShowBounds);
    }

    [Fact]
    public void SwitchCancelsAppScopeAndDrag() {
        var (_, hotKey, hook, _, overlay, _, platform, _) = CoordinatorTestHelper.CreateCoordinator();
        ConfigureTwoDisplays(platform, new Point(100, 100));
        platform.ForegroundWindow.Handle = 0x1234;
        platform.ForegroundWindow.Bounds = new Rectangle(100, 100, 800, 600);
        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPeriod);
        Assert.True(overlay.AppScopeBorderVisible);

        hook.SimulateKey(VKey.D2);

        Assert.False(overlay.AppScopeBorderVisible);
        Assert.Equal(Secondary.MonitorBounds, overlay.LastShowBounds);
    }

    [Fact]
    public void DisplayChangeDeactivates() {
        var (_, hotKey, hook, _, overlay, _, platform, _) = CoordinatorTestHelper.CreateCoordinator();
        ConfigureTwoDisplays(platform, new Point(100, 100));
        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);

        overlay.SimulateDisplayChange();

        Assert.False(overlay.IsVisible);
        Assert.False(hook.IsEnabled);
    }
}
