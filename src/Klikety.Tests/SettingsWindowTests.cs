using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

using Klikety;
using Klikety.Config;
using Klikety.Input;
using MouseAction = Klikety.Config.MouseAction;

namespace Klikety.Tests;

[Collection("Settings UI")]
public sealed class SettingsWindowTests {
    private static readonly string[][] EditableIds = [
        ["hotKey.modifiers", "hotKey.key", "logLevel", "fileLoggingEnabled", "retainedLogFileCount", "metadata.configPath", "metadata.configPath.open", "metadata.configVersion", "metadata.schemaReference"],
        [
            "defaultMode", "level3CellSizeThreshold", "appScope.chordKey",
            "modes.uniformGrid.enabled", "modes.uniformGrid.arrowKeys", "modes.uniformGrid.twoKey", "modes.uniformGrid.chordKey", "modes.uniformGrid.logBaseSize", "modes.uniformGrid.logGridBaseSize",
            "modes.crosshair.enabled", "modes.crosshair.arrowKeys", "modes.crosshair.twoKey", "modes.crosshair.chordKey", "modes.crosshair.logBaseSize", "modes.crosshair.logGridBaseSize",
            "modes.logCrosshair.enabled", "modes.logCrosshair.arrowKeys", "modes.logCrosshair.twoKey", "modes.logCrosshair.chordKey", "modes.logCrosshair.logBaseSize", "modes.logCrosshair.logGridBaseSize",
            "modes.logGrid.enabled", "modes.logGrid.arrowKeys", "modes.logGrid.twoKey", "modes.logGrid.chordKey", "modes.logGrid.logBaseSize", "modes.logGrid.logGridBaseSize",
        ],
        ["helpBinding.enabled", "helpBinding.key", "helpBinding.requireShift", "actionBindings.add.key", "actionBindings.add.action", "horizontalKeys.item.0", "verticalKeys.item.0"],
        ["theme", "minLabelFontSize", "metadata.themeFolder", "metadata.themeFolder.open"],
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
        File.WriteAllText(path, "{\"configVersion\":8,\"$schema\":\"schemas/config schema.json\"}");
        var original = File.ReadAllBytes(path);

        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var snapshot = new SettingsRuntimeSnapshot(new ConfigModel(), false, false);
                var applySucceeds = false;
                var confirmDiscard = false;
                var openedFolders = new List<string>();
                var openFolderFails = false;
                var window = new SettingsWindow(path, true, _ => { }, () => snapshot,
                    _ => applySucceeds
                        ? SettingsApplyOutcome.Success
                        : new SettingsApplyOutcome(false, ["Injected runtime activation failure."]),
                    _ => SettingsApplyOutcome.Success, confirmDiscard: _ => confirmDiscard,
                    openFolder: folder => {
                        if (openFolderFails) { throw new System.ComponentModel.Win32Exception(5); }
                        openedFolders.Add(folder);
                    });
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
                categories.SelectedIndex = 2;
                var helpKey = Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, "helpBinding.key"));
                helpKey.SelectedValue = VKey.Escape;
                Assert.True(Assert.IsType<Button>(window.FindName("SaveButton")).IsEnabled);
                Assert.IsType<Button>(window.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, categories.SelectedIndex);
                Assert.True(helpKey.IsKeyboardFocused);
                Assert.Contains("Help key", Assert.IsType<TextBlock>(window.FindName("Status")).Text);
                Assert.Equal(original, File.ReadAllBytes(path));
                helpKey.SelectedValue = VKey.OemQuestion;
                Assert.False(Assert.IsType<Button>(window.FindName("SaveButton")).IsEnabled);
                categories.SelectedIndex = 3;
                var openFolder = Assert.IsType<Button>(FindByAutomationId<Button>(pageHost.Content!, "metadata.themeFolder.open"));
                Assert.Equal("Open folder", openFolder.Content);
                Assert.Equal("Open Theme folder", AutomationProperties.GetName(openFolder));
                openFolder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal([Path.Combine(folder, "themes")], openedFolders);
                Assert.Equal(original, File.ReadAllBytes(path));
                Assert.False(Assert.IsType<Button>(window.FindName("SaveButton")).IsEnabled);
                openFolderFails = true;
                openFolder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.StartsWith("Cannot open folder:", Assert.IsType<TextBlock>(window.FindName("Status")).Text);
                Assert.Single(openedFolders);
                Assert.Equal(original, File.ReadAllBytes(path));
                categories.SelectedIndex = 0;
                openFolderFails = false;
                Assert.IsType<Button>(FindByAutomationId<Button>(pageHost.Content!, "metadata.configPath.open"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsType<Button>(FindByAutomationId<Button>(pageHost.Content!, "metadata.schemaReference.open"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal([Path.Combine(folder, "themes"), folder, Path.Combine(folder, "schemas")], openedFolders);
                Assert.False(Assert.IsType<Button>(window.FindName("SaveButton")).IsEnabled);
                Assert.Equal(original, File.ReadAllBytes(path));
                var modifiers = Assert.IsType<SettingsModifierPicker>(FindByAutomationId<SettingsModifierPicker>(pageHost.Content!, "hotKey.modifiers"));
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
                modifiers.Value = HotKeyModifiers.Alt;
                Assert.False(save.IsEnabled);
                capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var cancelEvent = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                };
                trigger.RaiseEvent(cancelEvent);
                Assert.Equal("Space", trigger.SelectedItem?.ToString());
                Assert.Contains("cancelled", Assert.IsType<TextBlock>(window.FindName("Status")).Text);
                window.UpdateLayout();
                trigger.Focus();
                capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                modifiers.Focus();
                Assert.Contains("focus moved", Assert.IsType<TextBlock>(window.FindName("Status")).Text);
                Assert.Equal("Space", trigger.SelectedValue?.ToString());
                var selectedCategory = Assert.IsType<ListBoxItem>(categories.SelectedItem);
                selectedCategory.Focus();
                var selectedPage = categories.SelectedIndex;
                selectedCategory.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Down) {
                    RoutedEvent = Keyboard.KeyDownEvent,
                });
                Assert.Equal(selectedPage + 1, categories.SelectedIndex);
                Assert.IsType<ListBoxItem>(categories.SelectedItem).RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Up) {
                    RoutedEvent = Keyboard.KeyDownEvent,
                });
                Assert.Equal(selectedPage, categories.SelectedIndex);
                window.UpdateLayout();
                Assert.True(trigger.Focus());
                Assert.True(trigger.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.True(capture.IsKeyboardFocused);
                Assert.True(capture.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)));
                Assert.True(trigger.IsKeyboardFocused);
                modifiers.Value = HotKeyModifiers.Control;
                Assert.True(save.IsEnabled);
                categories.SelectedIndex = 1;
                categories.SelectedIndex = 0;
                Assert.True(save.IsEnabled);
                modifiers.Value = HotKeyModifiers.Alt;
                Assert.False(save.IsEnabled);

                var generalPage = pageHost.Content;
                var retainedCount = Assert.IsType<TextBox>(FindByAutomationId<TextBox>(generalPage!, "retainedLogFileCount"));
                retainedCount.Text = "invalid";
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("invalid", retainedCount.Text);
                Assert.True(retainedCount.IsKeyboardFocused);
                Assert.Contains("valid whole number", Assert.IsType<TextBlock>(window.FindName("Status")).Text);

                retainedCount.Text = "7";
                modifiers.Value = HotKeyModifiers.Control;
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

                foreach (var field in SettingsFieldCases.All) {
                    categories.SelectedIndex = field.Page;
                    if (field.Path.EndsWith(".default", StringComparison.Ordinal)) {
                        Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, "defaultMode")).SelectedIndex = 1;
                        continue;
                    }
                    var desired = JsonNode.Parse(field.Json);
                    if (field.Path == "actionBindings") {
                        while (FindByAutomationId<Button>(pageHost.Content!, "actionBindings.item.0.remove") is { } remove) {
                            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        foreach (var (key, action) in desired!.AsObject()) {
                            Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, "actionBindings.add.key"))
                                .SelectedValue = Enum.Parse<VKey>(key);
                            Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, "actionBindings.add.action"))
                                .SelectedItem = Enum.Parse<MouseAction>(action!.GetValue<string>());
                            Assert.IsType<Button>(FindByAutomationId<Button>(pageHost.Content!, "actionBindings.add"))
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        continue;
                    }
                    if (desired is JsonArray desiredKeys) {
                        while (FindByAutomationId<Button>(pageHost.Content!, field.Path + ".item.0.remove") is { } remove) {
                            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        foreach (var key in desiredKeys) {
                            Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, field.Path + ".add.key"))
                                .SelectedValue = Enum.Parse<VKey>(key!.GetValue<string>());
                            Assert.IsType<Button>(FindByAutomationId<Button>(pageHost.Content!, field.Path + ".add"))
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        var first = Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, field.Path + ".item.0"));
                        var firstValue = first.SelectedValue;
                        Assert.IsType<Button>(FindByAutomationId<Button>(pageHost.Content!, field.Path + ".item.0.down"))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.IsType<Button>(FindByAutomationId<Button>(pageHost.Content!, field.Path + ".item.1.up"))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.Equal(firstValue, Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, field.Path + ".item.0")).SelectedValue);
                        continue;
                    }
                    var control = Assert.IsAssignableFrom<FrameworkElement>(FindByAutomationId<FrameworkElement>(pageHost.Content!, field.Path));
                    if (control is TextBox text) {
                        text.Text = desired is JsonValue value && value.TryGetValue<string>(out var stringValue)
                            ? stringValue : desired!.ToJsonString();
                    } else if (control is CheckBox toggle) {
                        toggle.IsChecked = desired!.GetValue<bool>();
                    } else if (control is SettingsModifierPicker modifierPicker) {
                        modifierPicker.Value = Enum.Parse<HotKeyModifiers>(desired!.GetValue<string>());
                    } else if (control is ComboBox choice) {
                        if (choice.SelectedValuePath.Length > 0) {
                            choice.SelectedValue = desired is null ? null : Enum.Parse<VKey>(desired.GetValue<string>());
                        } else { choice.SelectedItem = desired!.GetValue<string>(); }
                    }
                    Assert.False(string.IsNullOrWhiteSpace(UIElementAutomationPeer.CreatePeerForElement(control)?.GetName()), field.Path);
                }
                categories.SelectedIndex = 2;
                var collisionKey = Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(pageHost.Content!, "actionBindings.item.0.key"));
                collisionKey.SelectedValue = VKey.X;
                var beforeDuplicateSave = File.ReadAllBytes(path);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("assigned more than once", Assert.IsType<TextBlock>(window.FindName("Status")).Text);
                Assert.Equal(beforeDuplicateSave, File.ReadAllBytes(path));
                collisionKey.SelectedValue = VKey.Z;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("Saved", Assert.IsType<TextBlock>(window.FindName("Status")).Text);
                Assert.False(save.IsEnabled);
                var savedAll = SettingsFieldCases.Serialize(new SettingsConfigStore(path).Open().Config);
                foreach (var field in SettingsFieldCases.All) {
                    Assert.True(JsonNode.DeepEquals(JsonNode.Parse(field.Json), SettingsFieldCases.At(savedAll, field.Path)), field.Path);
                }

                categories.SelectedIndex = 6;
                window.UpdateLayout();
                var fade = Assert.IsType<TextBox>(FindByAutomationId<TextBox>(pageHost.Content!, "keyPressVisualization.fadeDurationMs"));
                fade.Text = "-1";
                var beforeInvalidHudSave = File.ReadAllBytes(path);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(fade.IsKeyboardFocused);
                Assert.Equal("-1", fade.Text);
                Assert.Equal(beforeInvalidHudSave, File.ReadAllBytes(path));
                fade.Text = "300";
                Assert.False(save.IsEnabled);

                categories.SelectedIndex = 3;
                var theme = Assert.IsType<TextBox>(FindByAutomationId<TextBox>(pageHost.Content!, "theme"));
                theme.Text = "unsaved";
                Assert.IsType<Button>(window.FindName("DiscardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("unsaved", theme.Text);
                window.Close();
                Assert.True(window.IsVisible);
                confirmDiscard = true;
                Assert.IsType<Button>(window.FindName("DiscardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("custom", Assert.IsType<TextBox>(FindByAutomationId<TextBox>(pageHost.Content!, "theme")).Text);
                Assert.False(save.IsEnabled);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Assert.IsType<TextBlock>(window.FindName("Status"))));
                window.Close();

                File.WriteAllText(path, "{\"configVersion\":8,\"hotKey\":{\"key\":9999},\"macros\":{\"slotKeys\":null}}");
                var invalidBefore = File.ReadAllBytes(path);
                var repairWindow = new SettingsWindow(path, true, _ => { }, () => snapshot,
                    _ => SettingsApplyOutcome.Success, _ => SettingsApplyOutcome.Success, confirmDiscard: _ => true);
                Assert.Equal(invalidBefore, File.ReadAllBytes(path));
                var repairCategories = Assert.IsType<ListBox>(repairWindow.FindName("Categories"));
                var repairPage = Assert.IsType<ContentControl>(repairWindow.FindName("PageHost"));
                Assert.Contains("Existing config errors", Assert.IsType<TextBlock>(repairWindow.FindName("Status")).Text);
                Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(repairPage.Content!, "hotKey.key")).SelectedValue = VKey.Space;
                repairCategories.SelectedIndex = 5;
                Assert.IsType<ComboBox>(FindByAutomationId<ComboBox>(repairPage.Content!, "macros.slotKeys.add.key")).SelectedValue = VKey.F1;
                Assert.IsType<Button>(FindByAutomationId<Button>(repairPage.Content!, "macros.slotKeys.add")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsType<Button>(repairWindow.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(new SettingsConfigStore(path).Open().SettingsBlockingErrors);
                repairWindow.Close();
            } catch (Exception ex) {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF editor check timed out.");

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

    [Theory]
    [InlineData("relative", true)]
    [InlineData("absolute", true)]
    [InlineData("file-uri", true)]
    [InlineData("web", false)]
    [InlineData("absent", false)]
    [InlineData("invalid", true)]
    public void SchemaFolderActionsResolveOnlyLocalReferencesAndRetainMetadata(string kind, bool hasAction) {
        var folder = Path.Combine(Path.GetTempPath(), "Klikety settings paths-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var schemaPath = Path.Combine(folder, "schemas", "config schema.json");
        var schema = kind switch {
            "relative" => "schemas/config schema.json",
            "absolute" => schemaPath,
            "file-uri" => new Uri(schemaPath).AbsoluteUri,
            "web" => "https://example.invalid/config.schema.json",
            "invalid" => "invalid\0schema.json",
            _ => null,
        };
        var path = Path.Combine(folder, "config.json");
        var document = new JsonObject { ["configVersion"] = ConfigMigrator.CurrentConfigVersion };
        if (schema is not null) { document["$schema"] = schema; }
        File.WriteAllText(path, document.ToJsonString());
        var original = File.ReadAllBytes(path);
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var opened = new List<string>();
                var window = new SettingsWindow(path, true, _ => { },
                    () => new(new ConfigModel(), false, false), _ => SettingsApplyOutcome.Success,
                    _ => SettingsApplyOutcome.Success, openFolder: opened.Add);
                var page = Assert.IsType<ContentControl>(window.FindName("PageHost"));
                var button = FindByAutomationId<Button>(page.Content!, "metadata.schemaReference.open");
                Assert.Equal(hasAction, button is not null);
                if (button is not null) {
                    Assert.Equal("Open $schema", AutomationProperties.GetName(button));
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (kind == "invalid") {
                        Assert.Empty(opened);
                        Assert.StartsWith("Cannot open folder:", Assert.IsType<TextBlock>(window.FindName("Status")).Text);
                    } else {
                        Assert.Equal([Path.GetDirectoryName(schemaPath)!], opened);
                    }
                }
                Assert.True(Assert.IsType<TextBox>(FindByAutomationId<TextBox>(page.Content!, "metadata.schemaReference")).IsReadOnly);
                Assert.False(Assert.IsType<Button>(window.FindName("SaveButton")).IsEnabled);
                Assert.Equal(original, File.ReadAllBytes(path));
                window.Close();
            } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF folder action check timed out.");
        try {
            if (failure is not null) { throw new Xunit.Sdk.XunitException(failure.ToString()); }
        } finally {
            File.Delete(path);
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
