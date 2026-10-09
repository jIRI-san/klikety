using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using Klikety;
using Klikety.Config;

namespace Klikety.Tests;

[Collection("Settings UI")]
public sealed class SettingsThemeTests {
    [Fact]
    public void SettingsAndColorDialogUseSystemThemeAndWindowLocalLightDarkChangesRetainDraftAndConfig() {
        var folder = Path.Combine(Path.GetTempPath(), "Klikety-settings-theme-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "config.json");
        File.WriteAllText(path, "{\"configVersion\":9,\"theme\":\"custom\"}");
        var original = File.ReadAllBytes(path);
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var window = new SettingsWindow(path, _ => { },
                    () => new(new ConfigModel(), false, false), _ => SettingsApplyOutcome.Success,
                    _ => SettingsApplyOutcome.Success, confirmDiscard: _ => true);
                var dialog = new SettingsColorDialog("#44112233", "Fixture color");
#pragma warning disable WPF0001
                Assert.Equal(ThemeMode.System, window.ThemeMode);
                Assert.Equal(ThemeMode.System, dialog.ThemeMode);
                window.Show();
                dialog.Show();
                var content = Assert.IsType<System.Windows.Controls.Grid>(window.Content);
                content.Measure(new Size(1080, 700));
                content.Arrange(new Rect(0, 0, 1080, 700));
                content.UpdateLayout();
                var page = Assert.IsType<StackPanel>(Assert.IsType<ContentControl>(window.FindName("PageHost")).Content);
                var picker = Descendants(page).OfType<SettingsModifierPicker>().Single();
                picker.Value = HotKeyModifiers.Control;
                var save = Assert.IsType<Button>(window.FindName("SaveButton"));
                Assert.True(save.IsEnabled);
                for (var index = 0; index < 3; index++) {
                    var mode = index == 1 ? ThemeMode.Dark : ThemeMode.Light;
                    window.ThemeMode = mode;
                    dialog.ThemeMode = mode;
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    content.UpdateLayout();
                    Assert.Equal(mode, window.ThemeMode);
                    Assert.Equal(mode, dialog.ThemeMode);
                    Assert.Equal(HotKeyModifiers.Control, picker.Value);
                    Assert.True(save.IsEnabled);
                    var dark = mode == ThemeMode.Dark;
                    var background = Assert.IsType<SolidColorBrush>(window.Background).Color;
                    Assert.Equal(dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(250, 250, 250), background);
                    Assert.Equal(background, Assert.IsType<SolidColorBrush>(dialog.Background).Color);
                    Assert.Equal(dark ? Colors.White : Colors.Black, Opaque(Assert.IsType<SolidColorBrush>(window.Foreground).Color));
                    AssertReadableControls(window, dialog, dark);
                    var card = Descendants(page).OfType<Border>().First(border => border.Child is StackPanel && border.Padding.Left == 16);
                    Assert.Same(window.FindResource("CardBackgroundFillColorDefaultBrush"), card.Background);
                    Assert.Same(window.FindResource("CardStrokeColorDefaultBrush"), card.BorderBrush);
                    Assert.Same(window.FindResource("TextFillColorSecondaryBrush"), Assert.IsType<TextBlock>(window.FindName("Status")).Foreground);
                    var hex = Assert.IsType<TextBox>(dialog.FindName("HexValue"));
                    hex.Text = "#invalid";
                    Assert.Same(dialog.FindResource("SystemFillColorCriticalBrush"), Assert.IsType<TextBlock>(dialog.FindName("Status")).Foreground);
                    hex.Text = "#44112233";
                    Assert.Equal("#44112233", dialog.SelectedHex);
                    Assert.Same(dialog.FindResource("TextFillColorSecondaryBrush"), Assert.IsType<TextBlock>(dialog.FindName("Status")).Foreground);
                    Assert.Equal("custom", window.LoadedTheme);
                    Assert.Equal(original, File.ReadAllBytes(path));
                }
                window.ThemeMode = ThemeMode.System;
                dialog.ThemeMode = ThemeMode.System;
                var unrelated = new Window();
                Assert.Equal(ThemeMode.None, unrelated.ThemeMode);
                unrelated.Close();
#pragma warning restore WPF0001
                dialog.Close();
                window.Close();
            } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF theme check timed out.");
        try {
            if (failure is not null) { throw new Xunit.Sdk.XunitException(failure.ToString()); }
        } finally {
            File.Delete(path);
            Directory.Delete(folder);
        }
    }

    private static Color Opaque(Color color) => Color.FromRgb(color.R, color.G, color.B);

    private static void AssertReadableControls(SettingsWindow window, SettingsColorDialog dialog, bool dark) {
        var categories = Assert.IsType<ListBox>(window.FindName("Categories"));
        var previous = categories.SelectedIndex;
        var host = Assert.IsType<ContentControl>(window.FindName("PageHost"));
        var expected = dark ? Colors.White : Colors.Black;
        for (var page = 0; page < categories.Items.Count; page++) {
            categories.SelectedIndex = page;
            window.UpdateLayout();
            foreach (var control in Descendants((DependencyObject)host.Content).OfType<Control>()
                         .Where(control => control.IsEnabled && control is CheckBox or Expander or Button or ComboBox or TextBox)) {
                control.ApplyTemplate();
                var color = Assert.IsType<SolidColorBrush>(control.Foreground).Color;
                Assert.True(Opaque(color) == expected,
                    $"{(dark ? "Dark" : "Light")} page {page} {control.GetType().Name} '{AutomationProperties.GetName(control)}': {color} text");
                AssertContrast(control);
                if (control is Button) {
                    Assert.Equal(Assert.IsType<SolidColorBrush>(window.FindResource("ButtonBackground")).Color,
                        Assert.IsType<SolidColorBrush>(control.Background).Color);
                }
            }
        }
        categories.SelectedIndex = previous;
        foreach (var control in Descendants(dialog).OfType<Control>()
                     .Where(control => control.IsEnabled && control is Label or TextBox or Button)) {
            Assert.Equal(expected, Opaque(Assert.IsType<SolidColorBrush>(control.Foreground).Color));
            AssertContrast(control);
        }
        foreach (var button in Descendants(dialog).OfType<Button>()) {
            Assert.Equal(expected, Opaque(Assert.IsType<SolidColorBrush>(button.Foreground).Color));
            Assert.Equal(Assert.IsType<SolidColorBrush>(dialog.FindResource("ButtonBackground")).Color,
                Assert.IsType<SolidColorBrush>(button.Background).Color);
        }
    }

    private static void AssertContrast(Control control) {
        var layers = new List<Color>();
        for (FrameworkElement? element = control; element is not null; element = element.Parent as FrameworkElement) {
            if (ReferenceEquals(element, control) && control is CheckBox or Expander) { continue; }
            var brush = element switch {
                Control input => input.Background,
                Border border => border.Background,
                Panel panel => panel.Background,
                _ => null,
            };
            if (brush is SolidColorBrush solid) { layers.Add(solid.Color); }
        }
        var background = Colors.White;
        for (var index = layers.Count - 1; index >= 0; index--) { background = Composite(layers[index], background); }
        var foreground = Composite(Assert.IsType<SolidColorBrush>(control.Foreground).Color, background);
        var first = Luminance(foreground);
        var second = Luminance(background);
        var contrast = (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
        Assert.True(contrast >= 4.5,
            $"{control.GetType().Name} '{AutomationProperties.GetName(control)}': text {foreground} on {background}, contrast {contrast:F2}");
    }

    private static Color Composite(Color foreground, Color background) {
        var alpha = foreground.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
            (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
            (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
    }

    private static double Luminance(Color color) {
        static double Channel(byte channel) {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node) {
        yield return node;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) {
            foreach (var descendant in Descendants(child)) { yield return descendant; }
        }
    }
}
