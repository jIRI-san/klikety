using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Overlay;
using Klikety.Tests.Fakes;

using Path = System.Windows.Shapes.Path;

namespace Klikety.Tests;

public class ElementHintRenderingTests {
    private sealed class LongKeyLabelResolver : IKeyLabelResolver {
        public string Resolve(VKey key) => $"LONG-FALLBACK-GLYPH-{key}";
    }

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
            var list = canvas.Children.OfType<ScrollViewer>().SingleOrDefault(s => s.Content is Canvas);
            var cardCanvas = list?.Content as Canvas ?? canvas;
            var cards = cardCanvas.Children.OfType<Border>().ToArray();
            var glyphs = cardCanvas.Children.OfType<Path>().Where(p => p.Stroke is null).ToArray();
            Assert.Equal(capacity, cards.Length); Assert.Equal(capacity, glyphs.Length);
            for (int i = 0; i < capacity; i++) {
                var card = new Rect(Canvas.GetLeft(cards[i]), Canvas.GetTop(cards[i]), cards[i].Width, cards[i].Height);
                Assert.True(card.Contains(glyphs[i].Data.Bounds), $"Text {glyphs[i].Data.Bounds} is outside {card}");
                Assert.True(new Rect(0, 0, list is null ? 800 : cardCanvas.Width,
                    list is null ? 600 : cardCanvas.Height).Contains(card));
                Assert.All(cards.Skip(i + 1), other => Assert.False(card.IntersectsWith(
                    new Rect(Canvas.GetLeft(other), Canvas.GetTop(other), other.Width, other.Height))));
            }
            if (list is not null) {
                Assert.True(new Rect(0, 0, 800, 600).Contains(
                    new Rect(Canvas.GetLeft(list), Canvas.GetTop(list), list.Width, list.Height)));
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

    [Theory]
    [InlineData(7, 20)]
    [InlineData(1, 1)]
    [InlineData(100, 60)]
    public void TinyViewportKeepsStatusAndLabelScrollRegionsInsideCanvas(double width, double height) {
        RunSta(() => {
            var canvas = new Canvas { Width = width, Height = height };
            canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height));
            var renderer = new ElementHintsRenderer(canvas, new ThemeModel(), 100,
                [VKey.OemSemicolon], [VKey.OemOpenBrackets], new FakeKeyLabelResolver());
            Assert.Equal(1, renderer.GetPageCapacity(new(0, 0, (int)width, (int)height), 1));
            renderer.Render(FakeElementHintService.Result(1).Targets, 0, 10, null, null, "Controls");
            var viewport = new Rect(0, 0, width, height);
            Assert.All(canvas.Children.OfType<ScrollViewer>(), scroll =>
                Assert.True(viewport.Contains(new Rect(Canvas.GetLeft(scroll), Canvas.GetTop(scroll), scroll.Width, scroll.Height))));
            var list = Assert.Single(canvas.Children.OfType<ScrollViewer>(), scroll => scroll.Content is Canvas);
            var content = Assert.IsType<Canvas>(list.Content);
            Assert.Single(content.Children.OfType<Border>());
            Assert.True(Assert.Single(content.Children.OfType<Path>(), p => p.Stroke is null).Data.Bounds.Height > 50);
        });
    }

    [Fact]
    public void GlyphOnlyRedrawKeepsPagePrefixSelectionAndAssignmentsWhenTextNoLongerFits() {
        RunSta(() => {
            var canvas = new Canvas { Width = 300, Height = 150 };
            canvas.Measure(new Size(300, 150)); canvas.Arrange(new Rect(0, 0, 300, 150));
            var renderer = new ElementHintsRenderer(canvas, new ThemeModel(), 18,
                [VKey.A, VKey.S], [VKey.Q, VKey.W], new FakeKeyLabelResolver());
            var service = new FakeElementHintService { Response = FakeElementHintService.Result(11) };
            var session = new ElementHintsSession([VKey.A, VKey.S], [VKey.Q, VKey.W],
                new ActionMapper([]), new(1, 1), service, renderer);
            int moves = 0;
            session.CursorMoveRequested += _ => moves++;
            try {
                session.Activate(new(0, 0, 300, 150), default);
                session.OnKey(VKey.Right); session.OnKey(VKey.A); session.OnKey(VKey.W);
                Assert.Equal(6, session.Selected!.Token);
                renderer.RebuildLabels(new LongKeyLabelResolver());
                session.Redraw();
                Assert.Equal(1, session.Page);
                Assert.Equal(3, session.PageCount);
                Assert.Equal(6, session.Selected.Token);
                Assert.Equal(1, moves);
                var list = Assert.Single(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Canvas);
                Assert.Equal(4, Assert.IsType<Canvas>(list.Content).Children.OfType<Border>().Count());
                canvas.Measure(new Size(300, 150)); canvas.Arrange(new Rect(0, 0, 300, 150)); canvas.UpdateLayout();
                Assert.True(list.VerticalOffset > 0);
                var selectedCard = Assert.IsType<Canvas>(list.Content).Children.OfType<Border>().ElementAt(1);
                var connector = Assert.Single(canvas.Children.OfType<Line>());
                Assert.Equal(Canvas.GetTop(list) + Canvas.GetTop(selectedCard) - list.VerticalOffset +
                    selectedCard.Height / 2, connector.Y1, 3);
                session.OnKey(VKey.A); session.Redraw();
                Assert.Equal(0, session.Prefix);
                Assert.Equal(1, session.Page);
                session.OnKey(VKey.W);
                Assert.Equal(6, session.Selected!.Token);
                Assert.Single(service.Scans);
                session.Relayout();
                Assert.Equal(0, session.Page);
                int capacity = renderer.GetPageCapacity(new(0, 0, 300, 150), 4);
                Assert.Equal((11 + capacity - 1) / capacity, session.PageCount);
                Assert.True(session.PageCount > 3);
                Assert.Null(session.Selected);
                Assert.Null(session.Prefix);
            } finally { session.Deactivate(); }
        });
    }

    [Fact]
    public void TopEdgeOrSingleCollisionDoesNotMoveEveryLabelToAListOrDrawConnectorSoup() {
        RunSta(() => {
            var canvas = new Canvas { Width = 800, Height = 600 };
            canvas.Measure(new Size(800, 600)); canvas.Arrange(new Rect(0, 0, 800, 600));
            var renderer = new ElementHintsRenderer(canvas, new ThemeModel(), 15,
                [VKey.A, VKey.S], [VKey.Q, VKey.W], new FakeKeyLabelResolver(),
                () => (new(0, 0), Matrix.Identity));
            Assert.Equal(4, renderer.GetPageCapacity(new(0, 0, 800, 600), 4));
            var targets = FakeElementHintService.Result(4).Targets.Select((t, i) => t with {
                Bounds = new(i < 2 ? 100 : 200 + i * 130, i < 2 ? 5 : 250, 30, 20),
                VisibleBounds = new(i < 2 ? 100 : 200 + i * 130, i < 2 ? 5 : 250, 30, 20),
                Preview = new(i < 2 ? 115 : 215 + i * 130, i < 2 ? 15 : 260),
            }).ToArray();
            renderer.Render(targets, 0, 1, null, null, "");
            Assert.DoesNotContain(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Canvas);
            var cards = canvas.Children.OfType<Border>().ToArray();
            Assert.Equal(4, cards.Length);
            Assert.All(cards.Skip(2), card => Assert.InRange(Canvas.GetTop(card), 230, 270));
            Assert.Empty(canvas.Children.OfType<Line>());
            Assert.Empty(canvas.Children.OfType<Rectangle>());
            var footer = Assert.Single(canvas.Children.OfType<ScrollViewer>());
            Assert.True(footer.Width < 800);
            Assert.True(Canvas.GetTop(footer) > 500);
            var text = Assert.IsType<TextBlock>(Assert.IsType<Border>(footer.Content).Child);
            Assert.DoesNotContain("Page", text.Text);
            Assert.Contains("Enter: grid", text.Text);
            renderer.Render(targets, 0, 1, null, targets[1].Token, "");
            Assert.Single(canvas.Children.OfType<Rectangle>());
            Assert.Single(canvas.Children.OfType<Line>());
            Assert.All(targets, t => Assert.Equal(t.Bounds.Center, t.Preview));
        });
    }

    [Fact]
    public void PathologicalCrowdingKeepsEveryLabelInBoundedListWithoutUnselectedConnectors() {
        RunSta(() => {
            var canvas = new Canvas { Width = 800, Height = 600 };
            canvas.Measure(new Size(800, 600)); canvas.Arrange(new Rect(0, 0, 800, 600));
            var keys = new ConfigModel();
            var renderer = new ElementHintsRenderer(canvas, new ThemeModel(), 15,
                keys.HorizontalKeys, keys.VerticalKeys, new FakeKeyLabelResolver());
            Assert.Equal(50, renderer.GetPageCapacity(new(0, 0, 800, 600), 50));
            renderer.Render(FakeElementHintService.Result(50).Targets, 0, 2, null, null, "Some controls unavailable");
            var list = Assert.Single(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Canvas);
            Assert.True(list.Width <= 360);
            Assert.Equal(50, Assert.IsType<Canvas>(list.Content).Children.OfType<Border>().Count());
            Assert.Empty(canvas.Children.OfType<Line>());
            var footer = Assert.Single(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Border);
            var text = Assert.IsType<TextBlock>(Assert.IsType<Border>(footer.Content).Child);
            Assert.Contains("Some controls unavailable", text.Text);
            Assert.Contains("Page 1/2", text.Text);
            renderer.Render(FakeElementHintService.Result(50).Targets, 1, 2, null, 40, "");
            Assert.Single(canvas.Children.OfType<Line>());
            Assert.Single(canvas.Children.OfType<Rectangle>());
        });
    }

    [Fact]
    public void PageCapacityFitsTheNarrowFallbackListWithoutHidingLabelRows() {
        RunSta(() => {
            var canvas = new Canvas { Width = 800, Height = 600 };
            canvas.Measure(new Size(800, 600)); canvas.Arrange(new Rect(0, 0, 800, 600));
            var keys = new ConfigModel();
            var renderer = new ElementHintsRenderer(canvas, new ThemeModel(), 40,
                keys.HorizontalKeys, keys.VerticalKeys, new FakeKeyLabelResolver(),
                () => (new(0, 0), Matrix.Identity));
            int keyCapacity = keys.HorizontalKeys.Length * keys.VerticalKeys.Length;
            int capacity = renderer.GetPageCapacity(new(0, 0, 800, 600), keyCapacity);
            Assert.InRange(capacity, 1, keyCapacity - 1);
            renderer.Render(FakeElementHintService.Result(capacity).Targets, 0, 2, null, null, "");
            var list = Assert.Single(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Canvas);
            var content = Assert.IsType<Canvas>(list.Content);
            Assert.Equal(capacity, content.Children.OfType<Border>().Count());
            Assert.True(content.Height <= list.Height);
            Assert.True(content.Width <= list.Width);
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
