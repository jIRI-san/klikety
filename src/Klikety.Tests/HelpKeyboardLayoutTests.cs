using Klikety.Input;
using Klikety.Overlay;

namespace Klikety.Tests;

public class HelpKeyboardLayoutTests {
    [Theory]
    [InlineData(800, 600)]
    [InlineData(1920, 1080)]
    public void Compute_PlacesKeyboardHalvesSlightlyBelowDisplayMidline(double width, double height) {
        var layout = HelpKeyboardLayout.Compute(width, height, [], []);
        var alphabetKeys = layout.Positions.Where(position =>
            position.Key is VKey.Q or VKey.M).ToArray();
        var top = alphabetKeys.Min(position => position.Y);
        var bottom = alphabetKeys.Max(position => position.Y + position.Height);
        var center = (top + bottom) / 2;

        Assert.Equal(height / 2 + 40, center, precision: 5);
    }

    [Theory]
    [InlineData(800, 600)]
    [InlineData(1920, 1080)]
    public void Compute_ContainedKeyboardWithMinimumReadableText(double width, double height) {
        var keys = new[] {
            VKey.F1, VKey.F2, VKey.F3, VKey.F4, VKey.F5, VKey.F6, VKey.F7, VKey.F8, VKey.F9, VKey.F10,
            VKey.D0, VKey.D1, VKey.D2, VKey.D3, VKey.D4, VKey.D5, VKey.D6, VKey.D7, VKey.D8, VKey.D9,
            VKey.A, VKey.B, VKey.C, VKey.D, VKey.E, VKey.F, VKey.G, VKey.H, VKey.I, VKey.J, VKey.K, VKey.L,
            VKey.M, VKey.N, VKey.O, VKey.P, VKey.Q, VKey.R, VKey.S, VKey.T, VKey.U, VKey.V, VKey.W, VKey.X,
            VKey.Y, VKey.Z, VKey.OemComma, VKey.OemPeriod, VKey.OemQuestion, VKey.OemSemicolon, VKey.OemTilde,
            VKey.OemPipe, VKey.Space, VKey.Escape,
        };
        var layout = HelpKeyboardLayout.Compute(width, height, keys, [VKey.A, VKey.W]);

        Assert.False(layout.ScrollViewport);
        Assert.Equal(12d, HelpKeyboardLayout.MinimumCommandFontSize);
        Assert.All(layout.Positions, position => {
            Assert.True(position.X >= 0);
            Assert.True(position.Y >= 0);
            Assert.True(position.X + position.Width <= layout.ContentWidth);
            Assert.True(position.Y + position.Height <= layout.ContentHeight);
        });

        var keyboardPositions = layout.Positions.ToArray();
        for (var i = 0; i < keyboardPositions.Length; i++) {
            for (var j = i + 1; j < keyboardPositions.Length; j++) {
                var left = keyboardPositions[i];
                var right = keyboardPositions[j];
                var overlaps = left.X < right.X + right.Width &&
                    left.X + left.Width > right.X &&
                    left.Y < right.Y + right.Height &&
                    left.Y + left.Height > right.Y;
                Assert.False(overlaps, $"{left.Key} overlaps {right.Key}");
            }
        }
    }

    [Theory]
    [InlineData(799, 599)]
    [InlineData(320, 240)]
    public void Compute_UsesContainedScrollingBelowConfirmedEnvelope(double width, double height) {
        var layout = HelpKeyboardLayout.Compute(
            width,
            height,
            [VKey.OemTilde, VKey.OemPipe, VKey.OemPlus],
            [VKey.A]);

        Assert.True(layout.ScrollViewport);
        Assert.All(layout.Positions, position => {
            Assert.True(position.X >= 0);
            Assert.True(position.Y >= 0);
            Assert.True(position.X + position.Width <= layout.ContentWidth);
            Assert.True(position.Y + position.Height <= layout.ContentHeight);
        });
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void Compute_ScalesKeyboardAndTextToThreeQuartersOfDisplayWidth(double width, double height) {
        var layout = HelpKeyboardLayout.Compute(
            width, height, [VKey.Escape, VKey.OemTilde, VKey.OemPipe], []);
        var positions = layout.Positions.Where(position => !position.IsAuxiliary).ToArray();
        var left = positions.Min(position => position.X);
        var right = positions.Max(position => position.X + position.Width);

        Assert.Equal(width * 0.75, right - left, precision: 5);
        Assert.Equal(width / 2, (left + right) / 2, precision: 5);
        Assert.True(layout.TextScale >= 2);
        Assert.False(layout.ScrollViewport);
        Assert.True(layout.Positions.Max(position => position.Y + position.Height) + 44 <= height);
    }

    [Theory]
    [InlineData(800, 600)]
    [InlineData(1920, 1080)]
    public void Compute_CentersSpaceAndEachAuxiliaryRow(double width, double height) {
        var layout = HelpKeyboardLayout.Compute(
            width, height, [VKey.Escape, VKey.OemTilde, VKey.OemPipe, VKey.OemPlus, VKey.Tab], []);
        var space = Assert.Single(layout.Positions, position => position.Key == VKey.Space);

        Assert.Equal(width / 2, space.X + space.Width / 2, precision: 5);
        Assert.Equal(layout.KeyUnit * 3.6, space.Width, precision: 5);
        foreach (var row in layout.Positions.Where(position => position.IsAuxiliary).GroupBy(position => position.Y)) {
            var left = row.Min(position => position.X);
            var right = row.Max(position => position.X + position.Width);
            Assert.Equal(width / 2, (left + right) / 2, precision: 5);
        }
    }

    [Fact]
    public void Compute_ShrinksForShortDisplaysWithoutReducingTextBelowMinimum() {
        var layout = HelpKeyboardLayout.Compute(
            2560, 600, [VKey.Escape, VKey.OemTilde, VKey.OemPipe], []);

        Assert.False(layout.ScrollViewport);
        Assert.InRange(layout.TextScale, 1, 2);
        Assert.All(layout.Positions, position => {
            Assert.True(position.X >= 0);
            Assert.True(position.Y >= 0);
            Assert.True(position.X + position.Width <= 2560);
            Assert.True(position.Y + position.Height + 44 <= 600);
        });
    }
}
