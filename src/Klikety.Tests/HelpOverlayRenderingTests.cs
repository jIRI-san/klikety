using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;

using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Overlay;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class HelpOverlayRenderingTests {
    [Fact]
    public void HelpView_RendersOnlyCommandCardsWithoutEmptyNavigationRectangles() {
        RunOnSta(() => {
            HelpOverlayEntry[] entries = [
                new(VKey.Space, "Space", "Left click", HelpEntryCategory.Action),
                new(VKey.Escape, "Esc", "Close", HelpEntryCategory.Help),
                new(VKey.OemTilde, "`", "Macro picker", HelpEntryCategory.Macro),
            ];
            var window = new OverlayWindow { Width = 800, Height = 600 };
            try {
                ((IOverlayWindow)window).ShowHelp(new HelpOverlayContent(
                    entries, [VKey.A, VKey.W, VKey.OemPlus], [], "Press Escape to close help."));
                var helpCanvas = Assert.IsType<Canvas>(window.FindName("HelpCanvas"));
                var viewport = Assert.Single(helpCanvas.Children.OfType<ScrollViewer>());
                var content = Assert.IsType<Canvas>(viewport.Content);
                var cards = content.Children.OfType<Border>().ToArray();

                Assert.Equal(entries.Length, cards.Length);
                Assert.All(cards, card => {
                    var stack = Assert.IsType<StackPanel>(card.Child);
                    var keycap = Assert.IsType<Border>(stack.Children[0]);
                    var label = Assert.IsType<TextBlock>(keycap.Child);
                    Assert.Contains(entries, entry => entry.KeyLabel == label.Text);
                });
            } finally {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(800, 600, "Segoe UI")]
    [InlineData(1920, 1080, "Segoe UI")]
    [InlineData(2560, 1440, "Consolas")]
    public void WrappedCommands_HaveFullRenderedHeightAndDoNotOverlapSpace(
        double width, double height, string fontFamily) {
        RunOnSta(() => {
            HelpOverlayEntry[] entries = [
                new(VKey.B, "B", "Move only / Start drag / Drag: double", HelpEntryCategory.Action),
                new(VKey.N, "N", "Crosshair / Confirm drag", HelpEntryCategory.Mode),
                new(VKey.Space, "Space", "Left click", HelpEntryCategory.Action),
                new(VKey.Escape, "Esc", "Close", HelpEntryCategory.Help),
                new(VKey.OemTilde, "`", "Macro picker", HelpEntryCategory.Macro),
                new(VKey.OemPipe, "\\", "Stop recording", HelpEntryCategory.Macro),
            ];
            var layout = HelpKeyboardLayout.Compute(width, height, entries.Select(entry => entry.Key), []);
            var theme = new ThemeModel { LabelFontFamily = fontFamily };
            var cards = MeasureCards(layout, entries, theme);
            var originalB = Assert.Single(layout.Positions, position => position.Key == VKey.B);
            Assert.True(cards[VKey.B].DesiredSize.Height > originalB.Height);

            layout = layout.WithMeasuredHeights(
                height, cards.ToDictionary(pair => pair.Key, pair => pair.Value.DesiredSize.Height), 40);

            foreach (var position in layout.Positions.Where(position => cards.ContainsKey(position.Key))) {
                var card = cards[position.Key];
                Assert.True(card.DesiredSize.Height <= position.Height + 0.01);
                card.Arrange(new Rect(0, 0, card.Width, card.DesiredSize.Height));
                var stack = Assert.IsType<StackPanel>(card.Child);
                var label = Assert.IsType<TextBlock>(stack.Children[1]);
                Assert.True(double.IsNaN(card.Height));
                Assert.True(double.IsNaN(stack.Height));
                Assert.Equal(double.PositiveInfinity, label.MaxHeight);
                var expected = new TextBlock {
                    Text = label.Text,
                    FontFamily = label.FontFamily,
                    FontSize = label.FontSize,
                    FontWeight = label.FontWeight,
                    TextWrapping = TextWrapping.Wrap,
                };
                expected.Measure(new Size(card.Width, double.PositiveInfinity));
                Assert.Equal(expected.DesiredSize.Height, label.ActualHeight, precision: 5);
            }

            var space = Assert.Single(layout.Positions, position => position.Key == VKey.Space);
            var bottomLetters = layout.Positions.Where(position => position.Key is VKey.B or VKey.N);
            Assert.All(bottomLetters, position =>
                Assert.True(space.Y >= position.Y + position.Height + layout.RowGap - 0.01));
            AssertSeparatedRows(layout);
            Assert.True(layout.ContentHeight >= layout.Positions.Max(position => position.Y + position.Height) + 40);
        });
    }

    [Theory]
    [InlineData(800, 600)]
    [InlineData(1920, 1080)]
    public void DefaultCommands_FitAfterMeasuringActualWpfCards(double width, double height) {
        RunOnSta(() => {
            var content = HelpOverlayContentBuilder.Build(
                new ConfigModel(),
                new FakeKeyLabelResolver(),
                logGridAvailable: true,
                isDragMode: false,
                MacroState.Idle,
                recorderState: null,
                selectedMacroSlot: -1,
                recordedStepCount: 0,
                macroSlots: [],
                displayNumbers: new Dictionary<string, int> { ["display"] = 1 },
                activeDisplayPath: "display",
                macroPickerAvailable: true,
                isModeLocked: false,
                appScoped: false);
            var layout = HelpKeyboardLayout.Compute(
                width, height, content.Entries.Select(entry => entry.Key), []);
            var cards = MeasureCards(layout, content.Entries, new ThemeModel());

            layout = layout.WithMeasuredHeights(
                height, cards.ToDictionary(pair => pair.Key, pair => pair.Value.DesiredSize.Height), 40);

            Assert.False(layout.ScrollViewport);
            Assert.All(layout.Positions, position => {
                Assert.True(position.Y >= 0);
                Assert.True(position.Y + position.Height + 40 <= height);
            });
            AssertSeparatedRows(layout);
        });
    }

    private static Dictionary<VKey, Border> MeasureCards(
        HelpKeyboardLayout layout, IReadOnlyList<HelpOverlayEntry> entries, ThemeModel theme) {
        var grouped = entries.GroupBy(entry => entry.Key)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var cards = new Dictionary<VKey, Border>();
        foreach (var position in layout.Positions) {
            if (grouped.TryGetValue(position.Key, out var commands)) {
                var card = OverlayWindow.CreateHelpEntry(position, commands, theme, layout.TextScale);
                card.Measure(new Size(position.Width, double.PositiveInfinity));
                cards.Add(position.Key, card);
            }
        }
        return cards;
    }

    private static void AssertSeparatedRows(HelpKeyboardLayout layout) {
        var rows = layout.Positions.GroupBy(position => position.Y).OrderBy(row => row.Key).ToArray();
        for (var index = 1; index < rows.Length; index++) {
            var previousBottom = rows[index - 1].Max(position => position.Y + position.Height);
            Assert.True(rows[index].Key >= previousBottom + layout.RowGap - 0.01);
        }
    }

    private static void RunOnSta(Action action) {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                action();
            } catch (Exception ex) {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
