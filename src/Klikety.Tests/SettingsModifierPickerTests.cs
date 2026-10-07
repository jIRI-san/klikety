using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Klikety;
using Klikety.Config;

namespace Klikety.Tests;

[Collection("Settings UI")]
public sealed class SettingsModifierPickerTests {
    [Fact]
    public void AllSixteenCombinationsAreIndividuallyToggleableAndExposeReadableSummaryWithoutSpuriousChanges() => RunSta(() => {
        var picker = new SettingsModifierPicker(HotKeyModifiers.None);
        var boxes = Options(picker);
        Assert.Equal(["Ctrl", "Alt", "Shift", "Win"], boxes.Select(box => (string)box.Content));
        var peer = UIElementAutomationPeer.CreatePeerForElement(picker);
        var value = Assert.IsAssignableFrom<IValueProvider>(peer.GetPattern(PatternInterface.Value));
        Assert.Equal(AutomationControlType.ComboBox, peer.GetAutomationControlType());
        for (var mask = 0; mask < 16; mask++) {
            picker.Value = (HotKeyModifiers)mask;
            var events = 0;
            EventHandler changed = (_, _) => events++;
            picker.ValueChanged += changed;
            picker.Value = (HotKeyModifiers)mask;
            Assert.Equal(0, events);
            foreach (var box in boxes) {
                var flag = Flag(box);
                Assert.Equal(((HotKeyModifiers)mask).HasFlag(flag), box.IsChecked);
                box.IsChecked = box.IsChecked != true;
                Assert.Equal((HotKeyModifiers)mask ^ flag, picker.Value);
                box.IsChecked = box.IsChecked != true;
                Assert.Equal((HotKeyModifiers)mask, picker.Value);
            }
            Assert.Equal(8, events);
            var summary = mask == 0 ? "None"
                : string.Join(", ", boxes.Where(box => box.IsChecked == true).Select(box => (string)box.Content));
            Assert.Equal(summary, value.Value);
            picker.ValueChanged -= changed;
        }
        picker.Value = (HotKeyModifiers)32;
        Assert.Equal((HotKeyModifiers)32, picker.Value);
        Assert.Contains("Invalid modifiers", value.Value);
        boxes[0].IsChecked = true;
        Assert.Equal(HotKeyModifiers.Control, picker.Value);
    });

    [Fact]
    public void DropdownStaysOpenForMultipleSelectionsAndSupportsArrowsEscapeTabAndDisabledCleanup() => RunSta(() => {
        var picker = new SettingsModifierPicker(HotKeyModifiers.Alt);
        var following = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(picker);
        panel.Children.Add(following);
        var window = new Window { Content = panel, Width = 420, Height = 260 };
        try {
            window.Show();
            window.UpdateLayout();
            var boxes = Options(picker);
            var peer = UIElementAutomationPeer.CreatePeerForElement(picker);
            var expansion = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
            expansion.Expand();
            Drain(picker);
            Assert.Equal(ExpandCollapseState.Expanded, expansion.ExpandCollapseState);
            Assert.True(boxes[1].IsKeyboardFocused);
            var popup = Popup(picker);
            var border = Assert.IsType<Border>(popup.Child);
            border.UpdateLayout();
            Assert.True(border.ActualWidth > 0 && Math.Abs(border.ActualWidth - picker.ActualWidth) <= 0.1);
            boxes[0].IsChecked = true;
            boxes[2].IsChecked = true;
            Assert.True(popup.IsOpen);
            Assert.Equal(HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift, picker.Value);
            boxes[0].Focus();
            Key(boxes[0], System.Windows.Input.Key.Space);
            boxes[0].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(boxes[0]), 0,
                System.Windows.Input.Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
            Assert.Equal(HotKeyModifiers.Alt | HotKeyModifiers.Shift, picker.Value);
            Assert.True(popup.IsOpen);
            boxes[1].Focus();
            Key(boxes[1], System.Windows.Input.Key.Down);
            Assert.True(boxes[2].IsKeyboardFocused);
            Key(boxes[2], System.Windows.Input.Key.Home);
            Assert.True(boxes[0].IsKeyboardFocused);
            Key(boxes[0], System.Windows.Input.Key.End);
            Assert.True(boxes[3].IsKeyboardFocused);
            Key(boxes[3], System.Windows.Input.Key.Escape);
            Assert.Equal(ExpandCollapseState.Collapsed, expansion.ExpandCollapseState);
            var button = Assert.IsType<ToggleButton>(Assert.IsType<System.Windows.Controls.Grid>(picker.Content).Children[0]);
            Assert.True(button.IsKeyboardFocused);
            Key(button, System.Windows.Input.Key.F4);
            Drain(picker);
            Assert.True(popup.IsOpen);
            Key(boxes[1], System.Windows.Input.Key.Tab);
            Assert.False(popup.IsOpen);
            Assert.True(following.IsKeyboardFocused);
            expansion.Expand();
            Drain(picker);
            picker.IsEnabled = false;
            Assert.False(popup.IsOpen);
            Assert.Throws<ElementNotEnabledException>(() => expansion.Expand());
            picker.IsEnabled = true;
            expansion.Expand();
            Drain(picker);
            panel.Children.Remove(picker);
            Drain(picker);
            Assert.False(popup.IsOpen);
        } finally { window.Close(); }
    });

    [Fact]
    public void EveryModifierCombinationSavesAndReopensWithoutTouchingOverlayThemeOrUnknownText() {
        var folder = Path.Combine(Path.GetTempPath(), "Klikety-modifiers-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "config.json");
        File.WriteAllText(path, "{ \"configVersion\":8, \"theme\":\"dark\", /* keep */ \"future\":1.2300, \"hotKey\":{\"key\":\"F11\"} }");
        try {
            RunSta(() => {
                var window = new SettingsWindow(path, true, _ => { },
                    () => new(new ConfigModel(), false, false), _ => SettingsApplyOutcome.Success,
                    _ => SettingsApplyOutcome.Success, confirmDiscard: _ => true);
                for (var mask = 0; mask < 16; mask++) {
                    var page = Assert.IsType<ContentControl>(window.FindName("PageHost")).Content!;
                    var picker = FindPicker(page)!;
                    picker.Value = (HotKeyModifiers)mask;
                    Assert.IsType<Button>(window.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var snapshot = new SettingsConfigStore(path).Open();
                    Assert.Empty(snapshot.SettingsBlockingErrors);
                    Assert.Equal((HotKeyModifiers)mask, snapshot.Config.HotKey.Modifiers);
                    Assert.Equal("dark", snapshot.Config.Theme);
                    Assert.Contains("/* keep */ \"future\":1.2300", File.ReadAllText(path));
                    Assert.False(Assert.IsType<Button>(window.FindName("SaveButton")).IsEnabled);
                }
                window.Close();
            });
        } finally {
            foreach (var file in Directory.GetFiles(folder)) { File.Delete(file); }
            Directory.Delete(folder);
        }
    }

    private static SettingsModifierPicker? FindPicker(object root) {
        if (root is SettingsModifierPicker picker) { return picker; }
        if (root is not DependencyObject node) { return null; }
        foreach (var child in LogicalTreeHelper.GetChildren(node)) {
            if (FindPicker(child) is { } found) { return found; }
        }
        return null;
    }

    private static Popup Popup(SettingsModifierPicker picker) =>
        Assert.IsType<Popup>(Assert.IsType<System.Windows.Controls.Grid>(picker.Content).Children[1]);

    private static CheckBox[] Options(SettingsModifierPicker picker) =>
        Assert.IsType<StackPanel>(Assert.IsType<Border>(Popup(picker).Child).Child).Children.Cast<CheckBox>().ToArray();

    private static HotKeyModifiers Flag(CheckBox box) =>
        Enum.Parse<HotKeyModifiers>(AutomationProperties.GetAutomationId(box)["modifier.".Length..]);

    private static void Drain(DispatcherObject target) => target.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Key(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static void RunSta(Action action) {
        Exception? failure = null;
        var thread = new Thread(() => {
            try { action(); } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF modifier picker check timed out.");
        if (failure is not null) { throw new Xunit.Sdk.XunitException(failure.ToString()); }
    }
}
