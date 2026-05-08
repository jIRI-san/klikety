using System.Windows;

using Klikety.Config;
using Klikety.Overlay;
using Klikety.Services;

namespace Klikety.Tests;

public sealed class KeyPressDisplayManagerTests {
    [Theory]
    [InlineData("BottomRight", 1900 - 200 - 20, 1080 - 100 - 20)]
    [InlineData("BottomLeft", 0 + 20, 1080 - 100 - 20)]
    [InlineData("TopRight", 1900 - 200 - 20, 0 + 20)]
    [InlineData("TopLeft", 0 + 20, 0 + 20)]
    public void CalculatePosition_CorrectCorner(string corner, double expectedLeft, double expectedTop) {
        var workArea = new Rect(0, 0, 1900, 1080);
        double windowWidth = 200;
        double windowHeight = 100;
        double margin = 20;

        var (left, top) = KeyPressDisplayManager.CalculatePosition(corner, workArea, windowWidth, windowHeight, margin);

        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedTop, top);
    }

    [Fact]
    public void CalculatePosition_WithOffset_IncludesWorkAreaOrigin() {
        // Simulates a second monitor offset at x=1920
        var workArea = new Rect(1920, 0, 1920, 1080);
        double windowWidth = 150;
        double windowHeight = 80;
        double margin = 10;

        var (left, top) = KeyPressDisplayManager.CalculatePosition("BottomRight", workArea, windowWidth, windowHeight, margin);

        Assert.Equal(1920 + 1920 - 150 - 10, left);
        Assert.Equal(1080 - 80 - 10, top);
    }

    [Fact]
    public void CalculatePosition_UnknownCorner_DefaultsToBottomRight() {
        var workArea = new Rect(0, 0, 1920, 1080);

        var (left, top) = KeyPressDisplayManager.CalculatePosition("Invalid", workArea, 200, 100, 20);

        // Same as BottomRight
        Assert.Equal(1920 - 200 - 20, left);
        Assert.Equal(1080 - 100 - 20, top);
    }

    [Fact]
    public void KeyPressDisplayItem_Opacity_RaisesPropertyChanged() {
        var item = new KeyPressDisplayItem("A");
        string? changedProperty = null;
        item.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        item.Opacity = 0.5;

        Assert.Equal(nameof(KeyPressDisplayItem.Opacity), changedProperty);
    }

    [Fact]
    public void KeyPressDisplayItem_RepeatCount_RaisesPropertyChanged() {
        var item = new KeyPressDisplayItem("A");
        string? changedProperty = null;
        item.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        item.RepeatCount = 3;

        Assert.Equal(nameof(KeyPressDisplayItem.RepeatCount), changedProperty);
    }

    [Fact]
    public void KeyPressDisplayItem_SameValue_DoesNotRaisePropertyChanged() {
        var item = new KeyPressDisplayItem("A");
        int changeCount = 0;
        item.PropertyChanged += (_, _) => changeCount++;

        item.Opacity = 1.0; // Same as default
        item.RepeatCount = 1; // Same as default

        Assert.Equal(0, changeCount);
    }
}
