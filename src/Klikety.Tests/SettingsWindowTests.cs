using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Klikety;
using Klikety.Config;
using Klikety.Input;
using MouseAction = Klikety.Config.MouseAction;

namespace Klikety.Tests;

public sealed class SettingsWindowTests {
    private static readonly string[][] EditableIds = [
        ["hotKey.modifiers", "hotKey.key", "logLevel", "fileLoggingEnabled", "retainedLogFileCount", "metadata.configPath", "metadata.configVersion", "metadata.schemaReference"],
        [
            "defaultMode", "level3CellSizeThreshold", "appScope.chordKey",
            "modes.uniformGrid.enabled", "modes.uniformGrid.arrowKeys", "modes.uniformGrid.twoKey", "modes.uniformGrid.chordKey", "modes.uniformGrid.logBaseSize", "modes.uniformGrid.logGridBaseSize",
            "modes.crosshair.enabled", "modes.crosshair.arrowKeys", "modes.crosshair.twoKey", "modes.crosshair.chordKey", "modes.crosshair.logBaseSize", "modes.crosshair.logGridBaseSize",
            "modes.logCrosshair.enabled", "modes.logCrosshair.arrowKeys", "modes.logCrosshair.twoKey", "modes.logCrosshair.chordKey", "modes.logCrosshair.logBaseSize", "modes.logCrosshair.logGridBaseSize",
            "modes.logGrid.enabled", "modes.logGrid.arrowKeys", "modes.logGrid.twoKey", "modes.logGrid.chordKey", "modes.logGrid.logBaseSize", "modes.logGrid.logGridBaseSize",
        ],
        ["actionBindings.add.key", "actionBindings.add.action", "horizontalKeys.item.0", "verticalKeys.item.0"],
        ["theme", "minLabelFontSize", "metadata.themeFolder"],
        ["scrollHotkeys.enabled", "scrollHotkeys.scrollUpKey.modifiers", "scrollHotkeys.scrollUpKey.key", "scrollHotkeys.scrollDownKey.modifiers", "scrollHotkeys.scrollDownKey.key", "scrollHotkeys.scrollAmount"],
        [
            "macros.enabled", "macros.globalHotKey", "macros.globalHotKey.modifiers", "macros.globalHotKey.key",
            "macros.recordKey", "macros.helperKey", "macros.slotKeys.item.0", "macros.speedModifier",
            "macros.playbackIndicator.fillColor", "macros.playbackIndicator.strokeColor",
            "macros.playbackIndicator.strokeThickness", "macros.playbackIndicator.initialRadius",
            "macros.playbackIndicator.finalRadius", "macros.playbackIndicator.animationDurationMs",
        ],
        [
            "keyPressVisualization.fontSize", "keyPressVisualization.fontColor", "keyPressVisualization.corner",
            "keyPressVisualization.maxVisibleKeys", "keyPressVisualization.outlineColor",
            "keyPressVisualization.outlineThickness", "keyPressVisualization.fadeTimeoutMs",
            "keyPressVisualization.fadeDurationMs", "keyPressVisualization.margin",
            "keyPressVisualization.repeatWindowMs",
        ],
    ];

    [Fact]
    public void WindowProvidesReusableSevenPageDraftAndRetainsInvalidInput() {
        var folder = Path.Combine(Path.GetTempPath(), "Klikety-settings-window-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "config.json");
        File.WriteAllText(path, "{\"configVersion\":7}");
        var original = File.ReadAllBytes(path);

        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var snapshot = new SettingsRuntimeSnapshot(new ConfigModel(), false, false);
                var applySucceeds = false;
                var window = new SettingsWindow(path, true, _ => { }, () => snapshot,
                    _ => applySucceeds
                        ? SettingsApplyOutcome.Success
                        : new SettingsApplyOutcome(false, ["Injected runtime activation failure."]),
                    _ => SettingsApplyOutcome.Success);
                window.Show();

                var categories = Assert.IsType<ListBox>(window.FindName("Categories"));
                Assert.Equal([
                    "General", "Navigation", "Key bindings", "Appearance", "Scrolling", "Macros", "Key-press HUD",
                ], categories.Items.Cast<ListBoxItem>().Select(item => item.Content).Cast<string>());

                var pageHost = Assert.IsType<ContentControl>(window.FindName("PageHost"));
                for (var pageIndex = 0; pageIndex < EditableIds.Length; pageIndex++) {
                    categories.SelectedIndex = pageIndex;
                    foreach (var id in EditableIds[pageIndex]) {
                        Assert.NotNull(FindByAutomationId<FrameworkElement>(pageHost.Content!, id));
                    }
                }
                categories.SelectedIndex = 0;
                var modifiers = Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, "hotKey.modifiers"));
                Assert.True(Assert.IsType<TextBox>(FindByAutomationId<TextBox>(pageHost.Content!, "metadata.configPath")).IsReadOnly);
                var trigger = Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, "hotKey.key"));
                var capture = Assert.IsType<Button>(FindByAutomationId<Button>(pageHost.Content!, "hotKey.key.capture"));
                var save = Assert.IsType<Button>(window.FindName("SaveButton"));
                capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(window));
                var keyEvent = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.K) {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                };
                trigger.RaiseEvent(keyEvent);
                Assert.Equal("K", trigger.SelectedItem?.ToString());
                Assert.True(save.IsEnabled);
                trigger.SelectedItem = trigger.Items.Cast<object>().Single(item => item.ToString() == "Space");
                modifiers.SelectedItem = "Alt";
                Assert.False(save.IsEnabled);
                capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var cancelEvent = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                };
                trigger.RaiseEvent(cancelEvent);
                Assert.Equal("Space", trigger.SelectedItem?.ToString());
                Assert.Contains("cancelled", Assert.IsType<TextBlock>(window.FindName("Status")).Text);
                modifiers.SelectedItem = "Control";
                Assert.True(save.IsEnabled);
                categories.SelectedIndex = 1;
                categories.SelectedIndex = 0;
                Assert.True(save.IsEnabled);
                modifiers.SelectedItem = "Alt";
                Assert.False(save.IsEnabled);

                var generalPage = pageHost.Content;
                var retainedCount = Assert.IsType<TextBox>(FindByAutomationId<TextBox>(generalPage!, "retainedLogFileCount"));
                retainedCount.Text = "invalid";
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("invalid", retainedCount.Text);
                Assert.True(retainedCount.IsKeyboardFocused);
                Assert.Contains("valid whole number", Assert.IsType<TextBlock>(window.FindName("Status")).Text);

                retainedCount.Text = "7";
                modifiers.SelectedItem = "Control";
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(original, File.ReadAllBytes(path));
                Assert.Contains("previous runtime restored", Assert.IsType<TextBlock>(window.FindName("Status")).Text);
                Assert.True(save.IsEnabled);

                applySucceeds = true;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(HotKeyModifiers.Control, new SettingsConfigStore(path).Open().Config.HotKey.Modifiers);
                Assert.False(save.IsEnabled);

                categories.SelectedIndex = 2;
                var addKey = Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, "actionBindings.add.key"));
                addKey.SelectedValue = VKey.OemOpenBrackets;
                var addAction = Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, "actionBindings.add.action"));
                addAction.SelectedItem = MouseAction.DragDrop;
                var addBinding = Assert.IsType<Button>(FindByAutomationId<Button>(pageHost.Content!, "actionBindings.add"));
                addBinding.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(save.IsEnabled);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(MouseAction.DragDrop, new SettingsConfigStore(path).Open().Config.ActionBindings["OemOpenBrackets"]);
                window.Close();
            } catch (Exception ex) {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        try {
            if (failure is not null) {
                throw new Xunit.Sdk.XunitException(failure.ToString());
            }
        } finally {
            foreach (var file in Directory.GetFiles(folder)) {
                File.Delete(file);
            }
            Directory.Delete(folder);
        }
    }

    private static T? FindByAutomationId<T>(object root, string id) where T : FrameworkElement {
        if (root is not DependencyObject dependencyObject) {
            return null;
        }
        if (dependencyObject is T element && System.Windows.Automation.AutomationProperties.GetAutomationId(element) == id) {
            return element;
        }
        foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject)) {
            var found = FindByAutomationId<T>(child, id);
            if (found is not null) {
                return found;
            }
        }
        if (dependencyObject is Visual or System.Windows.Media.Media3D.Visual3D) {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(dependencyObject); i++) {
                var found = FindByAutomationId<T>(VisualTreeHelper.GetChild(dependencyObject, i), id);
                if (found is not null) {
                    return found;
                }
            }
        }
        return null;
    }
}
