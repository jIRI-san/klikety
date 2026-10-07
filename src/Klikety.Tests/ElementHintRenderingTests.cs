using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Klikety.Config;
using Klikety.Input;
using Klikety.Overlay;
using Klikety.Tests.Fakes;

using Path = System.Windows.Shapes.Path;

namespace Klikety.Tests;

public class ElementHintRenderingTests {
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void CrowdedHintsContainMeasuredTextAtFontFloorAcrossDpiAndNegativeOrigins(double dpi) {
        RunSta(() => {
            var canvas = new Canvas { Width = 800, Height = 600 };
            canvas.Measure(new Size(800, 600)); canvas.Arrange(new Rect(0, 0, 800, 600));
            var renderer = new ElementHintsRenderer(canvas, new ThemeModel(), 18,
                [VKey.A, VKey.OemSemicolon], [VKey.Q, VKey.OemOpenBrackets],
                new FakeKeyLabelResolver(), () => (new(-1920, -1080), new Matrix(1 / dpi, 0, 0, 1 / dpi, 0, 0)));
            int capacity = renderer.GetPageCapacity(new(-1920, -1080, 1600, 1200), 4);
            var targets = FakeElementHintService.Result(capacity).Targets.Select(t => t with {
                Bounds = new(-1900, -1000, 2, 2),
                VisibleBounds = new(-1900, -1000, 2, 2),
                Preview = new(-1899, -999),
            }).ToArray();
            renderer.Render(targets, 0, 2, null, null, "Partial controls");
            canvas.Measure(new Size(800, 600)); canvas.Arrange(new Rect(0, 0, 800, 600));
            var cards = canvas.Children.OfType<Border>().ToArray();
            var glyphs = canvas.Children.OfType<Path>().Where(p => p.Stroke is null).ToArray();
            Assert.Equal(capacity, cards.Length); Assert.Equal(capacity, glyphs.Length);
            for (int i = 0; i < capacity; i++) {
                var card = new Rect(Canvas.GetLeft(cards[i]), Canvas.GetTop(cards[i]), cards[i].Width, cards[i].Height);
                Assert.True(card.Contains(glyphs[i].Data.Bounds), $"Text {glyphs[i].Data.Bounds} is outside {card}");
                Assert.True(new Rect(0, 0, 800, 600).Contains(card));
                Assert.All(cards.Skip(i + 1), other => Assert.False(card.IntersectsWith(
                    new Rect(Canvas.GetLeft(other), Canvas.GetTop(other), other.Width, other.Height))));
            }
            Assert.All(targets, t => Assert.Equal(new Automation.HintPoint(-1899, -999), t.Preview));
        });
    }

    [Fact]
    public void ExtremeFontAndViewportUseContainedScrollingInsteadOfShrinkingOrDroppingTargets() {
        RunSta(() => {
            var canvas = new Canvas { Width = 300, Height = 150 };
            canvas.Measure(new Size(300, 150)); canvas.Arrange(new Rect(0, 0, 300, 150));
            var renderer = new ElementHintsRenderer(canvas, new ThemeModel(), 100,
                [VKey.OemSemicolon], [VKey.OemOpenBrackets], new FakeKeyLabelResolver());
            Assert.Equal(1, renderer.GetPageCapacity(new(0, 0, 300, 150), 1));
            renderer.Render(FakeElementHintService.Result(1).Targets, 0, 10, null, null, "Controls");
            var list = Assert.Single(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Canvas);
            Assert.True(list.Width <= 300 && list.Height <= 150);
            var content = Assert.IsType<Canvas>(list.Content);
            Assert.Single(content.Children.OfType<Border>());
            var path = Assert.Single(content.Children.OfType<Path>(), p => p.Stroke is null);
            Assert.True(path.Data.Bounds.Height > 50);
        });
    }

    private static void RunSta(Action action) {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) { ExceptionDispatchInfo.Capture(error).Throw(); }
    }
}
