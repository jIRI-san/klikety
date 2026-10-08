using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

using Klikety.Config;
using Klikety.Input;

namespace Klikety.Tests;

[Collection("Settings UI")]
public sealed class ElementHintsSettingsTests {
    private const string Document = """
        {
          "configVersion":9,
          // keep user guidance
          "extension":{"untouched":42},
          "modes":{
            "uniformGrid":{"enabled":true,"default":true,"twoKey":true,"arrowKeys":true},
            "elementHints":{"enabled":false,"default":false,"chordKey":"Tab",
              "twoKey":true,"arrowKeys":true,"providerExtension":{"untouched":7}}
          },
          "horizontalKeys":["A","S"],"verticalKeys":["Q","W"],
          "actionBindings":{"Space":"RightClick","OemCloseBrackets":"MoveOnly"},
          "helpBinding":{"enabled":true,"key":"OemQuestion","requireShift":true}
        }
        """;

    [Fact]
    public void DialogLoadsEditsSavesAndReloadsHintDefaultAxesActionsAndHelpWithoutDroppingExtensions() =>
        WithDialog(Document, (path, window) => {
            var categories = Assert.IsType<ListBox>(window.FindName("Categories"));
            var host = Assert.IsType<ContentControl>(window.FindName("PageHost"));
            var save = Assert.IsType<Button>(window.FindName("SaveButton"));
            categories.SelectedIndex = 1;
            var defaults = Find<ComboBox>(host, "defaultMode");
            Assert.Equal("Element hints", defaults.Items[4]);
            Assert.Equal(0, defaults.SelectedIndex);
            Find<CheckBox>(host, "modes.elementHints.enabled").IsChecked = true;
            Assert.True(Find<CheckBox>(host, "modes.elementHints.twoKey").IsChecked);
            Assert.True(Find<CheckBox>(host, "modes.elementHints.arrowKeys").IsChecked);
            Assert.Equal(VKey.Tab, Find<ComboBox>(host, "modes.elementHints.chordKey").SelectedValue);
            Assert.Equal("1500", Find<TextBox>(host, "modes.elementHints.discoveryTimeoutMs").Text);
            Find<TextBox>(host, "modes.elementHints.discoveryTimeoutMs").Text = "10000";
            defaults.SelectedIndex = 4;
            categories.SelectedIndex = 2;
            Find<ComboBox>(host, "horizontalKeys.item.1").SelectedValue = VKey.D;
            Find<ComboBox>(host, "verticalKeys.item.1").SelectedValue = VKey.E;
            Find<ComboBox>(host, "actionBindings.item.0.action").SelectedItem = MouseAction.LeftClick;
            Find<ComboBox>(host, "actionBindings.item.1.key").SelectedValue = VKey.F11;
            Find<ComboBox>(host, "helpBinding.key").SelectedValue = VKey.OemOpenBrackets;
            Find<CheckBox>(host, "helpBinding.requireShift").IsChecked = false;
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(save.IsEnabled);
            var result = new SettingsConfigStore(path).Open();
            Assert.Empty(result.SettingsBlockingErrors);
            Assert.True(result.Config.Modes.ElementHints.Enabled);
            Assert.True(result.Config.Modes.ElementHints.Default);
            Assert.True(result.Config.Modes.ElementHints.TwoKey);
            Assert.True(result.Config.Modes.ElementHints.ArrowKeys);
            Assert.Equal(10000, result.Config.Modes.ElementHints.DiscoveryTimeoutMs);
            Assert.True(result.Config.Modes.UniformGrid.Enabled);
            Assert.False(result.Config.Modes.UniformGrid.Default);
            Assert.Null(result.Config.Modes.UniformGrid.ChordKey);
            Assert.Equal([VKey.A, VKey.D], result.Config.HorizontalKeys);
            Assert.Equal([VKey.Q, VKey.E], result.Config.VerticalKeys);
            Assert.Equal(MouseAction.LeftClick, result.Config.ActionBindings["Space"]);
            Assert.Equal(MouseAction.MoveOnly, result.Config.ActionBindings["F11"]);
            Assert.Equal(VKey.OemOpenBrackets, result.Config.HelpBinding.Key);
            Assert.True(result.Config.HelpBinding.Enabled);
            Assert.False(result.Config.HelpBinding.RequireShift);
            categories.SelectedIndex = 1;
            Assert.Equal(4, Find<ComboBox>(host, "defaultMode").SelectedIndex);
            Assert.True(Find<CheckBox>(host, "modes.elementHints.enabled").IsChecked);
            Assert.Equal("10000", Find<TextBox>(host, "modes.elementHints.discoveryTimeoutMs").Text);
            var text = File.ReadAllText(path);
            Assert.Contains("// keep user guidance", text);
            Assert.Contains("\"extension\":{\"untouched\":42}", text);
            Assert.Contains("\"providerExtension\":{\"untouched\":7}", text);
        });

    [Fact]
    public void InvalidHintRequirementIsVisibleAndDoesNotForceControlsOrWriteConfig() =>
        WithDialog(Document, (path, window) => {
            var original = File.ReadAllBytes(path);
            Assert.IsType<ListBox>(window.FindName("Categories")).SelectedIndex = 1;
            var host = Assert.IsType<ContentControl>(window.FindName("PageHost"));
            Find<CheckBox>(host, "modes.elementHints.enabled").IsChecked = true;
            var twoKey = Find<CheckBox>(host, "modes.elementHints.twoKey");
            twoKey.IsChecked = false;
            Assert.IsType<Button>(window.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("twoKey must be true",
                Assert.IsType<TextBlock>(window.FindName("Status")).Text);
            Assert.False(twoKey.IsChecked);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".settings.bak"));
        });

    [Fact]
    public void NativeEditorSavesHintsWithArrowNavigationDisabled() =>
        WithDialog(Document, (path, window) => {
            Assert.IsType<ListBox>(window.FindName("Categories")).SelectedIndex = 1;
            var host = Assert.IsType<ContentControl>(window.FindName("PageHost"));
            Find<CheckBox>(host, "modes.elementHints.enabled").IsChecked = true;
            Find<CheckBox>(host, "modes.elementHints.arrowKeys").IsChecked = false;
            Assert.IsType<Button>(window.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var result = new SettingsConfigStore(path).Open();
            Assert.Empty(result.SettingsBlockingErrors);
            Assert.True(result.Config.Modes.ElementHints.Enabled);
            Assert.False(result.Config.Modes.ElementHints.ArrowKeys);
            Assert.True(result.Config.Modes.ElementHints.TwoKey);
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidDefaultFlagsRemainUnchangedUntilUserChoosesADefault(bool multiple) {
        var json = multiple
            ? Document.Replace("\"enabled\":false,\"default\":false", "\"enabled\":true,\"default\":true")
            : Document.Replace("\"default\":true", "\"default\":false");
        WithDialog(json, (path, window) => {
            var original = File.ReadAllBytes(path);
            Assert.Contains("Existing config errors", Assert.IsType<TextBlock>(window.FindName("Status")).Text);
            var save = Assert.IsType<Button>(window.FindName("SaveButton"));
            Assert.False(save.IsEnabled);
            var categories = Assert.IsType<ListBox>(window.FindName("Categories"));
            var host = Assert.IsType<ContentControl>(window.FindName("PageHost"));
            categories.SelectedIndex = 1;
            Assert.Equal(-1, Find<ComboBox>(host, "defaultMode").SelectedIndex);
            categories.SelectedIndex = 0;
            Find<TextBox>(host, "retainedLogFileCount").Text = "7";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(original, File.ReadAllBytes(path));
            categories.SelectedIndex = 1;
            Find<ComboBox>(host, "defaultMode").SelectedIndex = 0;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var config = new SettingsConfigStore(path).Open().Config;
            Assert.True(config.Modes.UniformGrid.Default);
            Assert.False(config.Modes.ElementHints.Default);
            Assert.Equal(7, config.RetainedLogFileCount);
        });
    }

    [Theory]
    [InlineData("modes.uniformGrid.enabled", "false", "UniformGrid")]
    [InlineData("modes.elementHints.twoKey", "false", "twoKey must be true")]
    [InlineData("modes.elementHints.discoveryTimeoutMs", "0", "discoveryTimeoutMs")]
    [InlineData("modes.elementHints.discoveryTimeoutMs", "99", "discoveryTimeoutMs")]
    [InlineData("modes.elementHints.discoveryTimeoutMs", "60001", "discoveryTimeoutMs")]
    [InlineData("modes.elementHints.default", "true", "exactly one")]
    [InlineData("modes.elementHints.chordKey", "\"A\"", "chord")]
    [InlineData("modes.elementHints.chordKey", "\"Space\"", "chord")]
    [InlineData("modes.elementHints.chordKey", "\"OemQuestion\"", "chord")]
    [InlineData("modes.elementHints.chordKey", "\"N\"", "chord")]
    [InlineData("modes.elementHints.chordKey", "\"F1\"", "chord")]
    [InlineData("modes.elementHints.chordKey", "\"D1\"", "chord")]
    [InlineData("verticalKeys", "[\"A\"]", "label axes")]
    [InlineData("horizontalKeys", "[]", "label axes")]
    [InlineData("minLabelFontSize", "3", "minLabelFontSize")]
    [InlineData("helpBinding.key", "\"Tab\"", "Help key")]
    [InlineData("actionBindings", "{\"Tab\":\"RightClick\"}", "chord")]
    public void InvalidHintCombinationsBlockSettingsSaveWithoutRebinding(string path, string json, string error) {
        var root = SettingsFieldCases.Serialize(new ConfigModel { ConfigVersion = ConfigMigrator.CurrentConfigVersion });
        SettingsValidationTests.Set(root, "modes.elementHints.enabled", JsonValue.Create(true));
        var original = root.ToJsonString();
        SettingsValidationTests.Set(root, path, JsonNode.Parse(json));
        var result = ConfigLoader.ReadSettings(root.ToJsonString(), [path]);
        Assert.Contains(result.SettingsBlockingErrors, message => message.Contains(error, StringComparison.OrdinalIgnoreCase));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), SettingsFieldCases.At(SettingsFieldCases.Serialize(result.Config), path)));
        var file = Path.Combine(Path.GetTempPath(), "Klikety-element-rejection-" + Guid.NewGuid() + ".json");
        try {
            File.WriteAllText(file, original);
            var store = new SettingsConfigStore(file);
            store.Open();
            Assert.Throws<InvalidDataException>(() =>
                store.Save(new Dictionary<string, JsonNode?> { [path] = JsonNode.Parse(json) }));
            Assert.Equal(original, File.ReadAllText(file));
            Assert.False(File.Exists(file + ".settings.bak"));
        } finally {
            File.Delete(file);
            File.Delete(file + ".settings.bak");
        }
    }

    private static T Find<T>(ContentControl host, string id) where T : FrameworkElement =>
        Assert.IsType<T>(SettingsWindow.FindAutomationElement(host.Content as DependencyObject, id));

    private static void WithDialog(string json, Action<string, SettingsWindow> check) {
        var folder = Path.Combine(Path.GetTempPath(), "Klikety-element-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "config.json");
        File.WriteAllText(path, json);
        Exception? failure = null;
        var thread = new Thread(() => {
            SettingsWindow? window = null;
            try {
                window = new SettingsWindow(path, _ => { },
                    () => new SettingsRuntimeSnapshot(new ConfigModel(), false, false),
                    _ => SettingsApplyOutcome.Success, _ => SettingsApplyOutcome.Success, confirmDiscard: _ => true);
                check(path, window);
            } catch (Exception error) {
                failure = error;
            } finally {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try {
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Hidden ElementHints settings check timed out.");
            if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
        } finally {
            foreach (var file in Directory.GetFiles(folder)) { File.Delete(file); }
            Directory.Delete(folder);
        }
    }
}
