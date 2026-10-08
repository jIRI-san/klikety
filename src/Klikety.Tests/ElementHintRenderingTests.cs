using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Klikety.Config;
using Klikety.Automation;
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
    [InlineData(800, 600, 1.0)]
    [InlineData(300, 150, 1.5)]
    [InlineData(1, 1, 2.0)]
    public void ExtremeNestedLayoutKeepsRolesInContainedScrollingFallback(double width, double height, double dpi) {
        RunSta(() => {
            var canvas = new Canvas { Width = width, Height = height };
            canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height));
            var renderer = new ElementHintsRenderer(canvas, new ThemeModel(), 100,
                [VKey.A, VKey.S], [VKey.Q, VKey.W], new LongKeyLabelResolver(),
                () => (new(-1920, -1080), new Matrix(1 / dpi, 0, 0, 1 / dpi, 0, 0)));
            renderer.GetPageCapacity(new(-1920, -1080, (int)width, (int)height), 2, singleKey: true);
            var targets = FakeElementHintService.Result(2).Targets.Select(t => t with {
                Bounds = new(-1900, -1050, 100, 40),
                VisibleBounds = new(-1900, -1050, 100, 40),
                Preview = new(-1850, -1030)
            }).ToArray();
            var labels = targets.Select((target, i) => new HintLabel(new(target.Token, target,
                target.VisibleBounds, target.Preview, i == 0 ? "Tree item - select/expand" : "Button - invoke", []),
                i == 0 ? VKey.A : VKey.S, null)).ToArray();
            renderer.Render(new(labels, 2, 0, 1, null, 2, 2, "", true, true, true));
            canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
            var list = Assert.Single(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Canvas);
            Assert.True(new Rect(0, 0, width, height).Contains(
                new Rect(Canvas.GetLeft(list), Canvas.GetTop(list), list.Width, list.Height)));
            var content = Assert.IsType<Canvas>(list.Content);
            var cards = content.Children.OfType<Border>().ToArray();
            var glyphs = content.Children.OfType<Path>().Where(p => p.Stroke is null).ToArray();
            Assert.Equal(2, cards.Length);
            for (int i = 0; i < cards.Length; i++) {
                Assert.True(new Rect(Canvas.GetLeft(cards[i]), Canvas.GetTop(cards[i]),
                    cards[i].Width, cards[i].Height).Contains(glyphs[i].Data.Bounds));
            }
            Assert.Single(canvas.Children.OfType<Line>());
            var footer = Assert.Single(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Border);
            var text = Assert.IsType<TextBlock>(Assert.IsType<Border>(footer.Content).Child).Text;
            Assert.Contains("L2", text);
            Assert.Contains("Esc: back", text);
            Assert.DoesNotContain("PgUp", text);
        });
    }

    [Theory]
    [InlineData(1.0, "#000000", "#FFFFFF")]
    [InlineData(1.5, "#FFFFFF", "#1A1A1A")]
    [InlineData(2.0, "#000000", "#FFFFFF")]
    public void NestedBadgesAssociateControlsWithoutAListOrColorOnlyEncoding(double dpi, string background, string foreground) {
        RunSta(() => {
            var canvas = new Canvas { Width = 800, Height = 600 };
            canvas.Measure(new Size(800, 600)); canvas.Arrange(new Rect(0, 0, 800, 600));
            VKey[] keys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H];
            var renderer = new ElementHintsRenderer(canvas,
                new ThemeModel { LabelOutlineColor = background, LabelColor = foreground, LabelOutlineThickness = 0 },
                18, keys, [VKey.Q, VKey.W], new FakeKeyLabelResolver(),
                () => (new(-1920, -1080), new Matrix(1 / dpi, 0, 0, 1 / dpi, 0, 0)));
            renderer.GetPageCapacity(new(-1920, -1080, 1600, 1200), keys.Length, singleKey: true);
            var targets = FakeElementHintService.Result(keys.Length).Targets.Select(t => t with {
                Bounds = new(-1720, -880, 400, 120),
                VisibleBounds = new(-1720, -880, 400, 120),
                Preview = new(-1520, -820)
            }).ToArray();
            var labels = targets.Select((target, index) => new HintLabel(
                new(target.Token, target, target.VisibleBounds, target.Preview, "Button - invoke", []),
                keys[index], null)).ToArray();
            var view = new HintLevelView(labels, 2, 0, 1, null, null, null, "", true, true, true);
            renderer.Render(view);
            Assert.DoesNotContain(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Canvas);
            var cards = canvas.Children.OfType<Border>().ToArray();
            var glyphs = canvas.Children.OfType<Path>().Where(path => path.Stroke is null).ToArray();
            var contours = canvas.Children.OfType<Rectangle>().Where(r => r.Tag is string).ToArray();
            Assert.Equal(keys.Length, cards.Length);
            Assert.Equal(keys.Length, glyphs.Length);
            var patterns = new HashSet<string>();
            var colors = new List<Color>();
            for (int index = 0; index < keys.Length; index++) {
                var control = Assert.Single(contours, r => Equals(r.Tag, $"control:{targets[index].Token}"));
                var badge = Assert.Single(contours, r => Equals(r.Tag, $"badge:{targets[index].Token}"));
                var color = Assert.IsType<SolidColorBrush>(glyphs[index].Fill).Color;
                colors.Add(color);
                Assert.Equal(color, Assert.IsType<SolidColorBrush>(control.Stroke).Color);
                Assert.Equal(color, Assert.IsType<SolidColorBrush>(badge.Stroke).Color);
                Assert.Equal(control.StrokeDashArray, badge.StrokeDashArray);
                foreach (var outline in new[] { control, badge }) {
                    Assert.Contains(canvas.Children.OfType<Rectangle>(), halo => halo.Tag is null &&
                        halo.Stroke == Brushes.Black && halo.StrokeThickness == outline.StrokeThickness + 2 &&
                        Canvas.GetLeft(halo) == Canvas.GetLeft(outline) && Canvas.GetTop(halo) == Canvas.GetTop(outline) &&
                        halo.Width == outline.Width && halo.Height == outline.Height);
                }
                Assert.True(patterns.Add(string.Join(",", control.StrokeDashArray)));
                Assert.True((Luminance(color) + .05) / .05 >= 4.5);
                var card = new Rect(Canvas.GetLeft(cards[index]), Canvas.GetTop(cards[index]), cards[index].Width, cards[index].Height);
                Assert.True(card.Contains(glyphs[index].Data.Bounds));
                Assert.True(new Rect(0, 0, 800, 600).Contains(card));
                Assert.All(cards.Skip(index + 1), other => Assert.False(card.IntersectsWith(
                    new Rect(Canvas.GetLeft(other), Canvas.GetTop(other), other.Width, other.Height))));
                Assert.Equal(.4, cards[index].Background.Opacity);
                Assert.False(control.IsHitTestVisible);
                Assert.Equal(new HintPoint(-1520, -820), targets[index].Preview);
            }
            var links = canvas.Children.OfType<Line>().Where(line => line.Tag is string).ToArray();
            Assert.NotEmpty(links);
            foreach (var link in links) {
                var control = Assert.Single(contours, r => Equals(r.Tag,
                    Assert.IsType<string>(link.Tag).Replace("link:", "control:", StringComparison.Ordinal)));
                Assert.Equal(Assert.IsType<SolidColorBrush>(control.Stroke).Color,
                    Assert.IsType<SolidColorBrush>(link.Stroke).Color);
                Assert.Equal(control.StrokeDashArray, link.StrokeDashArray);
                var bounds = new Rect(Canvas.GetLeft(control), Canvas.GetTop(control), control.Width, control.Height);
                Assert.True(bounds.Contains(new Point(link.X2, link.Y2)));
                Assert.True(link.X2 == bounds.Left || link.X2 == bounds.Right ||
                    link.Y2 == bounds.Top || link.Y2 == bounds.Bottom);
            }
            Assert.All(canvas.Children.OfType<Path>().Where(path => path.Stroke is not null), path => {
                Assert.Equal(Brushes.Black, path.Stroke);
                Assert.True(path.StrokeThickness >= 3);
            });
            var first = Assert.Single(contours, r => Equals(r.Tag, "control:1"));
            var second = Assert.Single(contours, r => Equals(r.Tag, "control:2"));
            Assert.NotEqual(Canvas.GetLeft(first), Canvas.GetLeft(second));
            renderer.Render(view with { FocusedId = 1, SelectedToken = 1 });
            var focused = Assert.Single(canvas.Children.OfType<Rectangle>(), r => Equals(r.Tag, "control:1"));
            Assert.Equal(3, focused.StrokeThickness);
            Assert.Equal(colors[0], Assert.IsType<SolidColorBrush>(focused.Stroke).Color);
            var footer = Assert.Single(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Border);
            Assert.Contains("A: Button - invoke", Assert.IsType<TextBlock>(Assert.IsType<Border>(footer.Content).Child).Text);
            renderer.RebuildLabels(new LongKeyLabelResolver());
            renderer.GetPageCapacity(new(-1920, -1080, 1600, 1200), keys.Length, singleKey: true);
            renderer.Render(view with { FocusedId = 1, SelectedToken = 1 });
            var list = canvas.Children.OfType<ScrollViewer>().SingleOrDefault(s => s.Content is Canvas);
            var cardCanvas = list?.Content as Canvas ?? canvas;
            Assert.Equal(colors, cardCanvas.Children.OfType<Path>().Where(path => path.Stroke is null)
                .Select(path => Assert.IsType<SolidColorBrush>(path.Fill).Color));
        });
    }

    [Fact]
    public void NestedPairBadgesPreservePrefixDimmingAndGroupDistinction() {
        RunSta(() => {
            var canvas = new Canvas { Width = 800, Height = 600 };
            canvas.Measure(new Size(800, 600)); canvas.Arrange(new Rect(0, 0, 800, 600));
            var renderer = new ElementHintsRenderer(canvas, new ThemeModel(), 18,
                [VKey.A, VKey.S], [VKey.Q, VKey.W], new FakeKeyLabelResolver(),
                () => (new(0, 0), Matrix.Identity));
            renderer.GetPageCapacity(new(0, 0, 800, 600), 4);
            var targets = FakeElementHintService.Result(3).Targets;
            var entries = targets.Select(target => new HintEntry(target.Token, target,
                new(200, 200, 200, 80), new(300, 240), "Button - invoke", [])).ToList();
            entries.Add(new(-1, null, new(200, 200, 200, 80), new(300, 240), "Controls (2 controls)", []));
            var labels = entries.Select((entry, index) => new HintLabel(entry,
                index < 2 ? VKey.A : VKey.S, index % 2 == 0 ? VKey.Q : VKey.W)).ToArray();
            renderer.Render(new(labels, 2, 0, 1, 0, null, null, "", false, true, true));
            Assert.DoesNotContain(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Canvas);
            var cards = canvas.Children.OfType<Border>().ToArray();
            Assert.Equal(4, cards.Length);
            Assert.Equal(new CornerRadius(5), cards[3].CornerRadius);
            var group = Assert.Single(canvas.Children.OfType<Rectangle>(), r => Equals(r.Tag, "badge:-1"));
            Assert.Equal(5, group.RadiusX);
            var glyphs = canvas.Children.OfType<Path>().Where(path => path.Stroke is null).ToArray();
            Assert.Equal(4, glyphs.Length);
            for (int index = 0; index < cards.Length; index++) {
                Assert.Equal(index < 2 ? 1 : .2, cards[index].Opacity);
                Assert.True(new Rect(Canvas.GetLeft(cards[index]), Canvas.GetTop(cards[index]),
                    cards[index].Width, cards[index].Height).Contains(glyphs[index].Data.Bounds));
            }
            renderer.Render(new(labels, 2, 0, 1, null, -1, null, "", false, true, true));
            Assert.DoesNotContain(canvas.Children.OfType<Line>(), line => line.Tag is null && line.StrokeThickness == 2);
            var footer = Assert.Single(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Border);
            Assert.Contains("Group focused", Assert.IsType<TextBlock>(Assert.IsType<Border>(footer.Content).Child).Text);
        });
    }

    [Fact]
    public void GroupBadgeIsDistinctAndFocusNeverDrawsAnActionConnector() {
        RunSta(() => {
            var canvas = new Canvas { Width = 800, Height = 600 };
            canvas.Measure(new Size(800, 600)); canvas.Arrange(new Rect(0, 0, 800, 600));
            var renderer = new ElementHintsRenderer(canvas, new ThemeModel(), 18,
                [VKey.A], [VKey.Q], new FakeKeyLabelResolver(), () => (new(0, 0), Matrix.Identity));
            renderer.GetPageCapacity(new(0, 0, 800, 600), 1, singleKey: true);
            var entry = new HintEntry(-1, null, new(100, 100, 100, 40), new(150, 120), "Tree item (2 controls)",
                [], Compact: true);
            renderer.Render(new([new(entry, VKey.A, null)], 1, 0, 1, null, -1, null, "", true, true, false));
            var card = Assert.Single(canvas.Children.OfType<Border>());
            Assert.Equal(new CornerRadius(5), card.CornerRadius);
            Assert.Equal(new Thickness(3), card.BorderThickness);
            Assert.Equal(.4, card.Background.Opacity);
            Assert.Empty(canvas.Children.OfType<Line>());
            var footer = Assert.Single(canvas.Children.OfType<ScrollViewer>(), s => s.Content is Border);
            Assert.Contains("Group focused", Assert.IsType<TextBlock>(Assert.IsType<Border>(footer.Content).Child).Text);
        });
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF", false)]
    [InlineData("#FFFFFF", "#1A1A1A", false)]
    [InlineData("#80000000", "#FFFFFF", false)]
    [InlineData("#000000", "#FFFFFF", true)]
    [InlineData("#FFFFFF", "#1A1A1A", true)]
    public void BadgeBackgroundIsTranslucentWithoutDimmingTextBordersOrSelection(string background, string foreground, bool crowded) {
        RunSta(() => {
            int width = crowded ? 300 : 800;
            var canvas = new Canvas { Width = width, Height = 600 };
            canvas.Measure(new Size(width, 600)); canvas.Arrange(new Rect(0, 0, width, 600));
            var renderer = new ElementHintsRenderer(canvas,
                new ThemeModel { LabelOutlineColor = background, LabelColor = foreground }, 15,
                [VKey.A, VKey.S], [VKey.Q, VKey.W], new FakeKeyLabelResolver(),
                () => (new(0, 0), Matrix.Identity));
            renderer.GetPageCapacity(new(0, 0, width, 600), 4);
            var targets = FakeElementHintService.Result(4).Targets.Select((t, i) => t with {
                Preview = new(100 + i * 150, 100),
            }).ToArray();
            if (crowded) { renderer.RebuildLabels(new LongKeyLabelResolver()); renderer.GetPageCapacity(new(0, 0, width, 600), 4); }
            foreach (var (prefix, selected) in new (int? Prefix, int? Selected)[] { (null, null), (0, null), (null, 1) }) {
                renderer.Render(targets, 0, 1, prefix, selected, "");
                var list = canvas.Children.OfType<ScrollViewer>().SingleOrDefault(s => s.Content is Canvas);
                Assert.Equal(crowded, list is not null);
                var cardsCanvas = list?.Content as Canvas ?? canvas;
                var cards = cardsCanvas.Children.OfType<Border>().ToArray();
                var glyphs = cardsCanvas.Children.OfType<Path>().Where(p => p.Stroke is null).ToArray();
                Assert.Equal(4, cards.Length);
                for (int i = 0; i < cards.Length; i++) {
                    double opacity = prefix is not null ? i < 2 ? 1 : .2 : selected is null || i == 0 ? 1 : .35;
                    var brush = Assert.IsType<SolidColorBrush>(cards[i].Background);
                    Assert.Equal(.4, brush.Opacity);
                    Assert.Equal((Color)ColorConverter.ConvertFromString(background), brush.Color);
                    Assert.Equal(opacity, cards[i].Opacity);
                    Assert.Equal(opacity, glyphs[i].Opacity);
                    Assert.Equal(1, cards[i].BorderBrush.Opacity);
                    Assert.Equal(new Thickness(selected == 1 && i == 0 ? 3 : 1), cards[i].BorderThickness);
                }
            }
        });
    }

    [Theory]
    [InlineData(800, 600)]
    [InlineData(100, 60)]
    [InlineData(1, 1)]
    public void DiscoverySpinnerIsCenteredContainedAndStopsOnResultOrUnload(double width, double height) {
        RunSta(() => {
            var canvas = new Canvas { Width = width, Height = height };
            canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height));
            var renderer = new ElementHintsRenderer(canvas, new ThemeModel(), 15,
                [VKey.A], [VKey.Q], new FakeKeyLabelResolver(), () => (new(0, 0), Matrix.Identity));
            renderer.GetPageCapacity(new(0, 0, (int)width, (int)height), 1);
            renderer.Render([], 0, 1, null, null, "Finding controls...", isDiscovering: true);
            var spinner = Assert.Single(canvas.Children.OfType<Viewbox>());
            Assert.Equal(width / 2, Canvas.GetLeft(spinner) + spinner.Width / 2);
            Assert.Equal(height / 2, Canvas.GetTop(spinner) + spinner.Height / 2);
            Assert.True(new Rect(0, 0, width, height).Contains(
                new Rect(Canvas.GetLeft(spinner), Canvas.GetTop(spinner), spinner.Width, spinner.Height)));
            Assert.False(spinner.IsHitTestVisible);
            var arc = Assert.Single(Assert.IsType<Canvas>(spinner.Child).Children.OfType<Path>());
            var rotation = Assert.IsType<RotateTransform>(arc.RenderTransform);
            Assert.True(rotation.HasAnimatedProperties);
            renderer.Render([], 0, 1, null, null, "Control discovery timed out");
            Assert.Empty(canvas.Children.OfType<Viewbox>());
            Assert.False(rotation.HasAnimatedProperties);
            renderer.Render([], 0, 1, null, null, "Finding controls...", isDiscovering: true);
            spinner = Assert.Single(canvas.Children.OfType<Viewbox>());
            arc = Assert.Single(Assert.IsType<Canvas>(spinner.Child).Children.OfType<Path>());
            rotation = Assert.IsType<RotateTransform>(arc.RenderTransform);
            spinner.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.False(rotation.HasAnimatedProperties);
        });
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
                session.OnKey(VKey.A); session.OnKey(VKey.W);
                session.OnKey(VKey.A); session.OnKey(VKey.W);
                Assert.Equal(6, session.Selected!.Token);
                renderer.RebuildLabels(new LongKeyLabelResolver());
                session.Redraw();
                Assert.Equal(0, session.Page);
                Assert.Equal(1, session.PageCount);
                Assert.Equal(2, session.Depth);
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
                Assert.Equal(0, session.Page);
                session.OnKey(VKey.W);
                Assert.Equal(6, session.Selected!.Token);
                Assert.Single(service.Scans);
                session.Relayout();
                Assert.Equal(0, session.Page);
                int capacity = renderer.GetPageCapacity(new(0, 0, 300, 150), 4);
                Assert.Equal(1, session.PageCount);
                Assert.InRange(session.Labels.Count, 1, capacity);
                Assert.Equal(1, session.Depth);
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
    private static double Luminance(Color color) {
        static double Linear(byte value) {
            double channel = value / 255.0;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
}

internal static class ElementHintRenderTestExtensions {
    public static void Render(this ElementHintsRenderer renderer, IReadOnlyList<HintTarget> targets,
        int page, int pageCount, int? prefix, int? selectedToken, string status, bool isDiscovering = false) {
        var config = new ConfigModel();
        int rows = targets.Count <= 4 ? 2 : config.VerticalKeys.Length;
        var labels = targets.Select((target, i) => new HintLabel(
            new(target.Token, target, target.VisibleBounds, target.Preview, "Button", []),
            config.HorizontalKeys[i / rows], config.VerticalKeys[i % rows])).ToArray();
        renderer.Render(new(labels, 1, page, pageCount, prefix, null, selectedToken,
            status, false, true, false, isDiscovering));
    }
}
