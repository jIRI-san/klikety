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
            VKey.OemPipe, VKey.Space,
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

        var keyboardPositions = layout.Positions.Where(position => !position.IsAuxiliary).ToArray();
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

    [Fact]
    public void Compute_UsesContainedScrollingBelowConfirmedEnvelope() {
        var layout = HelpKeyboardLayout.Compute(
            799,
            599,
            [VKey.OemTilde, VKey.OemPipe, VKey.OemPlus],
            [VKey.A]);

        Assert.True(layout.ScrollViewport);
        Assert.All(layout.Positions.Where(position => position.IsAuxiliary), position => {
            Assert.True(position.X + position.Width <= layout.ContentWidth);
            Assert.True(position.Y + position.Height <= layout.ContentHeight);
        });
    }
}
