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
        File.WriteAllText(path, "{\"configVersion\":7,\"theme\":\"custom\"}");
        var original = File.ReadAllBytes(path);
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var window = new SettingsWindow(path, true, _ => { },
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

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node) {
        yield return node;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) {
            foreach (var descendant in Descendants(child)) { yield return descendant; }
        }
    }
}
