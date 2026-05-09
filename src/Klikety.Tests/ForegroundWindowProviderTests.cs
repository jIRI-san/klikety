using System.Drawing;

using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class ForegroundWindowProviderTests {
    [Fact]
    public void GetForegroundWindowHandle_ReturnsConfiguredHandle() {
        var fake = new FakeForegroundWindowProvider { Handle = 0x1234 };
        Assert.Equal((nint)0x1234, fake.GetForegroundWindowHandle());
    }

    [Fact]
    public void GetWindowBounds_ValidHandle_ReturnsConfiguredBounds() {
        var bounds = new Rectangle(100, 200, 800, 600);
        var fake = new FakeForegroundWindowProvider { Handle = 0x1234, Bounds = bounds };
        Assert.Equal(bounds, fake.GetWindowBounds(0x1234));
    }

    [Fact]
    public void GetWindowBounds_ZeroHandle_ReturnsEmpty() {
        var fake = new FakeForegroundWindowProvider { Handle = 0, Bounds = new Rectangle(0, 0, 800, 600) };
        Assert.Equal(Rectangle.Empty, fake.GetWindowBounds(0));
    }

    [Fact]
    public void GetWindowBounds_MismatchedHandle_ReturnsEmpty() {
        var fake = new FakeForegroundWindowProvider { Handle = 0x1234, Bounds = new Rectangle(0, 0, 800, 600) };
        Assert.Equal(Rectangle.Empty, fake.GetWindowBounds(0x5678));
    }

    [Fact]
    public void ShowBounds_RecordsBoundsInFakeOverlay() {
        var overlay = new FakeOverlayWindow();
        var bounds = new Rectangle(100, 200, 800, 600);
        overlay.Show(bounds);

        Assert.Equal(bounds, overlay.LastShowBounds);
        Assert.True(overlay.IsVisible);
        Assert.Equal(1, overlay.ShowCount);
    }

    [Fact]
    public void ShowParameterless_RecordsDefaultBoundsInFakeOverlay() {
        var overlay = new FakeOverlayWindow();
        overlay.Show();

        Assert.Equal(new Rectangle(0, 0, 1920, 1080), overlay.LastShowBounds);
    }

    [Fact]
    public void RaiseFocusLostOnHide_WhenEnabled_FiresFocusLost() {
        var overlay = new FakeOverlayWindow { RaiseFocusLostOnHide = true };
        overlay.Show();

        bool focusLostFired = false;
        overlay.FocusLost += (_, _) => focusLostFired = true;
        overlay.Hide();

        Assert.True(focusLostFired);
    }

    [Fact]
    public void RaiseFocusLostOnHide_WhenDisabled_DoesNotFireFocusLost() {
        var overlay = new FakeOverlayWindow();
        overlay.Show();

        bool focusLostFired = false;
        overlay.FocusLost += (_, _) => focusLostFired = true;
        overlay.Hide();

        Assert.False(focusLostFired);
    }
}
