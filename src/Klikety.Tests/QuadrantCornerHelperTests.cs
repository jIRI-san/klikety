using System.Drawing;

using Klikety.Grid;

namespace Klikety.Tests;

public class QuadrantCornerHelperTests {
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);
    private static readonly Size IndicatorSize = new(80, 60);

    [Fact]
    public void CenterTopLeft_IndicatorGoesBottomRight() {
        // Grid center in top-left quadrant
        var center = new Point(400, 200);
        var pos = QuadrantCornerHelper.GetIndicatorPosition(center, Screen, IndicatorSize);

        // Should be in bottom-right area
        Assert.True(pos.X > Screen.Width / 2, $"Expected X > {Screen.Width / 2}, got {pos.X}");
        Assert.True(pos.Y > Screen.Height / 2, $"Expected Y > {Screen.Height / 2}, got {pos.Y}");
    }

    [Fact]
    public void CenterTopRight_IndicatorGoesBottomLeft() {
        var center = new Point(1400, 200);
        var pos = QuadrantCornerHelper.GetIndicatorPosition(center, Screen, IndicatorSize);

        Assert.True(pos.X < Screen.Width / 2);
        Assert.True(pos.Y > Screen.Height / 2);
    }

    [Fact]
    public void CenterBottomLeft_IndicatorGoesTopRight() {
        var center = new Point(400, 800);
        var pos = QuadrantCornerHelper.GetIndicatorPosition(center, Screen, IndicatorSize);

        Assert.True(pos.X > Screen.Width / 2);
        Assert.True(pos.Y < Screen.Height / 2);
    }

    [Fact]
    public void CenterBottomRight_IndicatorGoesTopLeft() {
        var center = new Point(1400, 800);
        var pos = QuadrantCornerHelper.GetIndicatorPosition(center, Screen, IndicatorSize);

        Assert.True(pos.X < Screen.Width / 2);
        Assert.True(pos.Y < Screen.Height / 2);
    }

    [Fact]
    public void ExactCenter_FallsBackToBottomRight() {
        // Exact screen center
        var center = new Point(Screen.Width / 2, Screen.Height / 2);
        var pos = QuadrantCornerHelper.GetIndicatorPosition(center, Screen, IndicatorSize);

        Assert.True(pos.X > Screen.Width / 2);
        Assert.True(pos.Y > Screen.Height / 2);
    }

    [Fact]
    public void IndicatorStaysWithinScreenBounds() {
        var center = new Point(100, 100);
        var pos = QuadrantCornerHelper.GetIndicatorPosition(center, Screen, IndicatorSize);

        Assert.True(pos.X >= Screen.X);
        Assert.True(pos.Y >= Screen.Y);
        Assert.True(pos.X + IndicatorSize.Width <= Screen.Right);
        Assert.True(pos.Y + IndicatorSize.Height <= Screen.Bottom);
    }

    [Fact]
    public void NonZeroOriginScreen_StillWorksCorrectly() {
        // Multi-monitor scenario: screen offset at (1920, 0)
        var screen = new Rectangle(1920, 0, 1920, 1080);
        var center = new Point(2000, 200); // top-left of this screen
        var pos = QuadrantCornerHelper.GetIndicatorPosition(center, screen, IndicatorSize);

        // Should go to bottom-right of this screen
        Assert.True(pos.X > screen.X + screen.Width / 2);
        Assert.True(pos.Y > screen.Y + screen.Height / 2);
    }
}
