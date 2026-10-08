using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

using Klikety;
using Klikety.Config;

namespace Klikety.Tests;

[Collection("Settings UI")]
public sealed class SettingsColorDialogTests {
    private static readonly string[] AlphaExamples = ["#44ABCDEF", "#FFABCDEF"];
    private static readonly string[] AccessibleControls = [
        "RedSlider", "GreenSlider", "BlueSlider", "AlphaSlider",
        "RedValue", "GreenValue", "BlueValue", "AlphaValue", "HexValue", "OkButton", "CancelButton",
    ];

    [Theory]
    [InlineData("#123456", 255, 18, 52, 86)]
    [InlineData("#44123456", 68, 18, 52, 86)]
    [InlineData("#00123456", 0, 18, 52, 86)]
    [InlineData("#ffabcdef", 255, 171, 205, 239)]
    public void OpeningPreservesExactHexAndBothPreviews(string hex, int alpha, int red, int green, int blue) => RunSta(() => {
        var dialog = new SettingsColorDialog(hex, "Fixture color");
        Assert.Equal(hex, dialog.SelectedHex);
        Assert.Equal(hex, Control<TextBox>(dialog, "HexValue").Text);
        Assert.Equal(alpha, Control<Slider>(dialog, "AlphaSlider").Value);
        Assert.Equal(red, Control<Slider>(dialog, "RedSlider").Value);
        Assert.Equal(green, Control<Slider>(dialog, "GreenSlider").Value);
        Assert.Equal(blue, Control<Slider>(dialog, "BlueSlider").Value);
        var original = Assert.IsType<SolidColorBrush>(Control<Border>(dialog, "OriginalSwatch").Background).Color;
        Assert.Equal(original, Assert.IsType<SolidColorBrush>(Control<Border>(dialog, "NewSwatch").Background).Color);
        var background = Assert.IsType<Border>(Control<Border>(dialog, "NewSwatch").Parent).Background;
        Assert.Equal(TileMode.Tile, Assert.IsType<DrawingBrush>(background).TileMode);
        Assert.True(Control<Button>(dialog, "OkButton").IsEnabled);
        dialog.Close();
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(254)]
    [InlineData(255)]
    public void OpacityAndRgbEditingKeepExactBytesAndRestoreOriginalDirtyRepresentation(int alpha) => RunSta(() => {
        var dialog = new SettingsColorDialog("#abcdef", "Fixture color");
        Control<Slider>(dialog, "AlphaSlider").Value = alpha;
        Control<Slider>(dialog, "RedSlider").Value = 0;
        Control<TextBox>(dialog, "GreenValue").Text = "255";
        Assert.Equal(alpha == 255 ? "#00FFEF" : $"#{alpha:X2}00FFEF", dialog.SelectedHex);
        Assert.Equal(alpha.ToString(CultureInfo.InvariantCulture), Control<TextBox>(dialog, "AlphaValue").Text);
        Control<TextBox>(dialog, "HexValue").Text = "#ABCDEF";
        Assert.Equal("#abcdef", dialog.SelectedHex);
        Assert.Equal(255, Control<Slider>(dialog, "AlphaSlider").Value);
        dialog.Close();
    });

    [Fact]
    public void RgbEditingPreservesExistingAlphaAndExplicitOpaqueEightDigitFormat() => RunSta(() => {
        foreach (var initial in AlphaExamples) {
            var dialog = new SettingsColorDialog(initial, "Fixture color");
            Control<Slider>(dialog, "BlueSlider").Value = 0;
            Assert.Equal(initial[..7] + "00", dialog.SelectedHex);
            Assert.Equal(initial[..3], dialog.SelectedHex[..3]);
            dialog.Close();
        }
    });

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("256")]
    [InlineData("1.5")]
    [InlineData("NaN")]
    [InlineData(" 12 ")]
    public void InvalidNumericInputBlocksAcceptWithoutCoercingAndCanBeRepaired(string value) => RunSta(() => {
        var dialog = new SettingsColorDialog("#44112233", "Fixture color");
        var input = Control<TextBox>(dialog, "AlphaValue");
        input.Text = value;
        Assert.Equal(value, input.Text);
        Assert.False(Control<Button>(dialog, "OkButton").IsEnabled);
        Assert.Equal("#44112233", dialog.SelectedHex);
        Assert.Contains("Opacity:", Control<TextBlock>(dialog, "Status").Text);
        input.Text = "0";
        Assert.True(Control<Button>(dialog, "OkButton").IsEnabled);
        Assert.Equal("#00112233", dialog.SelectedHex);
        dialog.Close();
    });

    [Theory]
    [InlineData("")]
    [InlineData("#123")]
    [InlineData("#12345")]
    [InlineData("#GG1122")]
    [InlineData("red")]
    [InlineData("#123456789")]
    public void InvalidHexRetainsInputAndPreviewUntilExplicitRepair(string hex) => RunSta(() => {
        var dialog = new SettingsColorDialog("#44112233", "Fixture color");
        var input = Control<TextBox>(dialog, "HexValue");
        input.Text = hex;
        Assert.Equal(hex, input.Text);
        Assert.False(Control<Button>(dialog, "OkButton").IsEnabled);
        Assert.Equal("#44112233", dialog.SelectedHex);
        Assert.Contains("last valid color", Control<TextBlock>(dialog, "Status").Text);
        Control<Slider>(dialog, "AlphaSlider").Value = 0;
        Assert.True(Control<Button>(dialog, "OkButton").IsEnabled);
        Assert.Equal("#00112233", input.Text);
        dialog.Close();
    });

    [Fact]
    public void HexEditsSynchronizeAllChannelsAndNamesLayoutAndKeyboardActionsRemainAccessible() => RunSta(() => {
        var dialog = new SettingsColorDialog("#112233", "Fixture color");
        Control<TextBox>(dialog, "HexValue").Text = "#10203040";
        Assert.Equal(16, Control<Slider>(dialog, "AlphaSlider").Value);
        Assert.Equal(32, Control<Slider>(dialog, "RedSlider").Value);
        Assert.Equal(48, Control<Slider>(dialog, "GreenSlider").Value);
        Assert.Equal(64, Control<Slider>(dialog, "BlueSlider").Value);
        Assert.Equal("#10203040", dialog.SelectedHex);
        var content = Assert.IsType<StackPanel>(dialog.Content);
        content.Measure(new Size(420, double.PositiveInfinity));
        content.Arrange(new Rect(new Point(), content.DesiredSize));
        content.UpdateLayout();
        Assert.True(content.ActualHeight < 640);
        foreach (var name in AccessibleControls) {
            var control = Control<Control>(dialog, name);
            Assert.True(control.IsTabStop);
            Assert.False(string.IsNullOrWhiteSpace(UIElementAutomationPeer.CreatePeerForElement(control)?.GetName()));
            var bounds = control.TransformToAncestor(content).TransformBounds(new Rect(control.RenderSize));
            Assert.True(bounds.Right <= content.ActualWidth + 0.1 && bounds.Bottom <= content.ActualHeight + 0.1);
        }
        Assert.True(Control<Button>(dialog, "OkButton").IsDefault);
        Assert.True(Control<Button>(dialog, "CancelButton").IsCancel);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Control<TextBlock>(dialog, "Status")));
        dialog.Close();
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ModalDialogReturnsAcceptanceOrCancellationOnlyAfterButtonAction(bool accept) => RunSta(() => {
        var dialog = new SettingsColorDialog("#44112233", "Fixture color");
        Exception? failure = null;
        dialog.Dispatcher.BeginInvoke(new Action(() => {
            try {
                Control<Slider>(dialog, "RedSlider").Value = 255;
                Assert.Equal("#44FF2233", dialog.SelectedHex);
                Control<Button>(dialog, accept ? "OkButton" : "CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            } catch (Exception ex) { failure = ex; } finally { if (dialog.IsVisible) { dialog.Close(); } }
        }));
        Assert.Equal(accept, dialog.ShowDialog());
        if (failure is not null) { throw failure; }
    });

    [Theory]
    [InlineData(5, "macros.playbackIndicator.fillColor", "#44FF0000")]
    [InlineData(5, "macros.playbackIndicator.strokeColor", "#CCFF0000")]
    [InlineData(6, "keyPressVisualization.fontColor", "#FFCC00")]
    [InlineData(6, "keyPressVisualization.outlineColor", "#000000")]
    public void EveryEditorPreservesCancelledInvalidAndUnchangedInputAndSavesOnlyAcceptedColors(int pageIndex, string path, string initial) {
        var folder = Path.Combine(Path.GetTempPath(), "Klikety-colors-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var configPath = Path.Combine(folder, "config.json");
        File.WriteAllText(configPath, "{\"configVersion\":9}");
        var original = File.ReadAllBytes(configPath);
        try {
            RunSta(() => {
                string? selected = null;
                var calls = 0;
                var window = new SettingsWindow(configPath, true, _ => { },
                    () => new(new ConfigModel(), false, false), _ => SettingsApplyOutcome.Success,
                    _ => SettingsApplyOutcome.Success, confirmDiscard: _ => true, chooseColor: (label, value) => {
                        Assert.False(string.IsNullOrWhiteSpace(label));
                        Assert.Equal(initial, value);
                        calls++;
                        return selected;
                    });
                Control<ListBox>(window, "Categories").SelectedIndex = pageIndex;
                var page = Control<ContentControl>(window, "PageHost").Content!;
                var text = FindId<TextBox>(page, path)!;
                var pick = FindId<Button>(page, path + ".pick")!;
                Assert.Equal(initial, text.Text);
                pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, calls);
                Assert.False(Control<Button>(window, "SaveButton").IsEnabled);
                Assert.Equal(initial, text.Text);
                selected = initial;
                pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(Control<Button>(window, "SaveButton").IsEnabled);
                text.Text = "#invalid";
                pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, calls);
                Assert.Equal("#invalid", text.Text);
                Assert.Contains(path, Control<TextBlock>(window, "Status").Text);
                text.Text = initial;
                selected = "#invalid";
                pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(3, calls);
                Assert.Equal(initial, text.Text);
                Assert.False(Control<Button>(window, "SaveButton").IsEnabled);
                Assert.Contains("invalid hex color", Control<TextBlock>(window, "Status").Text);
                selected = "#11223344";
                pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(4, calls);
                Assert.Equal(selected, text.Text);
                Assert.Equal(original, File.ReadAllBytes(configPath));
                Control<Button>(window, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var saved = SettingsFieldCases.Serialize(new SettingsConfigStore(configPath).Open().Config);
                Assert.Equal(selected, SettingsFieldCases.At(saved, path)!.GetValue<string>());
                Assert.False(Control<Button>(window, "SaveButton").IsEnabled);
                window.Close();
            });
        } finally {
            foreach (var file in Directory.GetFiles(folder)) { File.Delete(file); }
            Directory.Delete(folder);
        }
    }

    private static T Control<T>(Window window, string name) where T : FrameworkElement => Assert.IsAssignableFrom<T>(window.FindName(name));

    private static T? FindId<T>(object root, string id) where T : FrameworkElement {
        if (root is not DependencyObject node) { return null; }
        if (node is T element && AutomationProperties.GetAutomationId(element) == id) { return element; }
        foreach (var child in LogicalTreeHelper.GetChildren(node)) {
            if (FindId<T>(child, id) is { } found) { return found; }
        }
        return null;
    }

    private static void RunSta(Action action) {
        Exception? failure = null;
        var thread = new Thread(() => {
            try { action(); } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF color dialog check timed out.");
        if (failure is not null) { throw new Xunit.Sdk.XunitException(failure.ToString()); }
    }
}
