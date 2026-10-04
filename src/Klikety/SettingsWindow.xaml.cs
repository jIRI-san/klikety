using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;

namespace Klikety;

public partial class SettingsWindow : Window {
    private sealed record Field(string Original, Func<string> Current, Func<JsonNode?> Value);
    private sealed record KeyChoice(VKey? Key, string Label) {
        public override string ToString() => Label;
    }
    private readonly SettingsConfigStore _store;
    private readonly Action<ConfigModel> _preflight;
    private readonly Func<IReadOnlyList<string>> _apply;
    private readonly bool _demo;
    private readonly Dictionary<string, Field> _fields = [];
    private readonly List<StackPanel> _pages = [];
    private readonly Win32KeyLabelResolver _labels = new();
    private bool _loading;
    private bool _dirty;
    private bool _pendingApply;

    internal SettingsWindow(string path, bool demo, Action<ConfigModel> preflight, Func<IReadOnlyList<string>> apply) {
        InitializeComponent();
        _store = new SettingsConfigStore(path);
        _preflight = preflight;
        _apply = apply;
        _demo = demo;
        if (demo) {
            Title = "Klikety Settings - ISOLATED DEMO (no global hooks)";
            ScopeLabel.Text = "ISOLATED DEMO · Real file editing; navigation/hotkey activation is disabled.";
        }
        ScopeLabel.ToolTip = path;
        LoadDraft();
        Categories.SelectedIndex = 0;
        Closing += ConfirmClose;
    }

    private void LoadDraft() {
        var result = _store.Open();
        var config = result.Config;
        _loading = true;
        _fields.Clear();
        _pages.Clear();

        var general = Page("General", "The shortcut that brings navigation to your screen.");
        var hotkey = Card(general, "Activation hotkey");
        Choice(hotkey, "Modifiers", "hotKey.modifiers", config.HotKey.Modifiers.ToString(),
            Enumerable.Range(0, 16).Select(n => ((HotKeyModifiers)n).ToString()).ToArray());
        KeyPicker(hotkey, "Trigger key", "hotKey.key", config.HotKey.Key, nullable: false);
        Hint(hotkey, "Choose named keys from the picker. Collisions are checked before writing.");
        var generalAdvanced = Advanced(general);
        Hint(generalAdvanced, "Start with Windows remains a registry toggle in the tray. It is not saved to JSON.");

        var navigation = Page("Navigation", "Pick a starting mode, then choose how each mode responds.");
        var defaultCard = Card(navigation, "Start in");
        var modeNames = new[] { "uniformGrid", "crosshair", "logCrosshair", "logGrid" };
        var modeLabels = new[] { "Uniform grid", "Crosshair", "Log crosshair", "Log grid" };
        var modes = new[] { config.Modes.UniformGrid, config.Modes.Crosshair, config.Modes.LogCrosshair, config.Modes.LogGrid };
        var defaultPicker = new ComboBox { ItemsSource = modeLabels, SelectedIndex = Array.FindIndex(modes, m => m.Default) };
        Row(defaultCard, "Default mode", defaultPicker);
        for (var i = 0; i < modes.Length; i++) {
            var index = i;
            var original = modes[i].Default;
            Track($"modes.{modeNames[i]}.default", () => (defaultPicker.SelectedIndex == index).ToString(),
                () => JsonValue.Create(defaultPicker.SelectedIndex == index), original.ToString());
        }
        defaultPicker.SelectionChanged += (_, _) => RefreshDirty();
        AutomationProperties.SetAutomationId(defaultPicker, "defaultMode");
        Hint(defaultCard, "Every enabled non-default mode needs a unique switch chord, including Uniform grid.");
        for (var i = 0; i < modes.Length; i++) {
            var card = Card(navigation, modeLabels[i]);
            var prefix = $"modes.{modeNames[i]}";
            Toggle(card, "Enabled", prefix + ".enabled", modes[i].Enabled);
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            card.Children.Add(controls);
            Toggle(controls, "Arrow keys", prefix + ".arrowKeys", modes[i].ArrowKeys);
            Toggle(controls, "Two-key selection", prefix + ".twoKey", modes[i].TwoKey);
            KeyPicker(card, "Switch chord", prefix + ".chordKey", modes[i].ChordKey, nullable: true);
        }
        var scope = Card(navigation, "Current-window scope");
        KeyPicker(scope, "Scope chord", "appScope.chordKey", config.AppScope.ChordKey, nullable: true);
        Hint(scope, "None disables current-window targeting. This chord must not overlap other bindings.");
        var sizing = Advanced(navigation);
        Number(sizing, "Log crosshair center (px)", "modes.logCrosshair.logBaseSize", config.Modes.LogCrosshair.LogBaseSize, integer: true);
        Number(sizing, "Log grid center (px)", "modes.logGrid.logGridBaseSize", config.Modes.LogGrid.LogGridBaseSize, integer: true);
        Number(sizing, "Level-3 threshold (px²)", "level3CellSizeThreshold", config.Level3CellSizeThreshold, integer: true);
        Hint(sizing, "Center sizes: 2–50 px. Level-3 area threshold: 0 means always available.");

        Page("Key bindings", "Unfinished in this prototype. Existing action and axis bindings are preserved.");
        var appearance = Page("Appearance", "Change the overlay, not the Windows-native settings interface.");
        var appearanceCard = Card(appearance, "Overlay labels");
        Choice(appearanceCard, "Theme", "theme", config.Theme, [config.Theme, "dark", "light"]);
        Number(appearanceCard, "Minimum label size (DIP)", "minLabelFontSize", config.MinLabelFontSize, integer: false);
        Hint(appearanceCard, "Labels auto-scale. This size is the readability floor, not a fixed font size.");
        Hint(Advanced(appearance), "Theme contents are out of scope. Existing custom theme names are kept.");
        Page("Scrolling", "Unfinished in this prototype. Existing scrolling values and the tray pause toggle are unchanged.");
        Page("Macros", "Unfinished in this prototype. Bindings, theme contents and macro recordings are not edited here.");
        Page("Key-press HUD", "Unfinished in this prototype. Show Key Presses is a runtime tray toggle, not a JSON enable flag.");
        _loading = false;
        PageHost.Content = _pages[Math.Max(0, Categories.SelectedIndex)];
        RefreshDirty();
        ShowStatus(result.Violations.Count == 0
            ? "Ready. Only changed values are written. Save creates config.json.settings.bak."
            : "Existing config issues (fix before saving):\n" + string.Join("\n", result.Violations), result.Violations.Count > 0);
    }

    private StackPanel Page(string title, string description) {
        var page = new StackPanel();
        page.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        Hint(page, description);
        _pages.Add(page);
        return page;
    }

    private static StackPanel Card(Panel page, string title) {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        page.Children.Add(new Border {
            Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(16), Margin = new Thickness(0, 14, 0, 0), Child = content,
        });
        return content;
    }

    private static StackPanel Advanced(Panel page) {
        var content = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        page.Children.Add(new Expander { Header = "Advanced", Content = content });
        return content;
    }

    private static void Hint(Panel panel, string text) => panel.Children.Add(new TextBlock {
        Text = text, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(85, 92, 102)),
        Margin = new Thickness(0, 8, 0, 0), FontSize = 13,
    });

    private static void Row(Panel panel, string label, Control control) {
        AutomationProperties.SetName(control, label);
        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(215) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var caption = new Label { Content = label, Target = control, Padding = new Thickness(0, 4, 8, 4) };
        grid.Children.Add(caption);
        System.Windows.Controls.Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        panel.Children.Add(grid);
    }

    private void Track(string path, Func<string> current, Func<JsonNode?> value, string? original = null) =>
        _fields.Add(path, new Field(original ?? current(), current, value));

    private void Toggle(Panel panel, string label, string path, bool value) {
        var box = new CheckBox { Content = label, IsChecked = value };
        AutomationProperties.SetAutomationId(box, path);
        panel.Children.Add(box);
        Track(path, () => (box.IsChecked == true).ToString(), () => JsonValue.Create(box.IsChecked == true));
        box.Checked += (_, _) => RefreshDirty();
        box.Unchecked += (_, _) => RefreshDirty();
    }

    private void Choice(Panel panel, string label, string path, string value, string[] choices) {
        var box = new ComboBox { ItemsSource = choices.Distinct().ToArray(), SelectedItem = value };
        AutomationProperties.SetAutomationId(box, path);
        Row(panel, label, box);
        Track(path, () => (string)box.SelectedItem, () => JsonValue.Create((string)box.SelectedItem));
        box.SelectionChanged += (_, _) => RefreshDirty();
    }

    private void KeyPicker(Panel panel, string label, string path, VKey? value, bool nullable) {
        var choices = Enum.GetValues<VKey>().Select(k => new KeyChoice(k, KeyLabel(k))).ToList();
        if (nullable) {
            choices.Insert(0, new KeyChoice(null, "None"));
        }
        var box = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label", SelectedItem = choices.First(c => c.Key == value) };
        AutomationProperties.SetAutomationId(box, path);
        Row(panel, label, box);
        Track(path, () => ((KeyChoice)box.SelectedItem).Key?.ToString() ?? "None",
            () => ((KeyChoice)box.SelectedItem).Key is { } key ? JsonValue.Create(key.ToString()) : null);
        box.SelectionChanged += (_, _) => RefreshDirty();
    }

    private string KeyLabel(VKey key) {
        var label = key switch {
            VKey.Menu => "Alt",
            VKey.Control => "Ctrl",
            VKey.Prior => "Page Up",
            VKey.Next => "Page Down",
            VKey.Return => "Enter",
            VKey.Back => "Backspace",
            VKey.Capital => "Caps Lock",
            VKey.Space => "Space",
            _ => _labels.Resolve(key),
        };
        return label == key.ToString() ? label : $"{label} ({key})";
    }

    private void Number(Panel panel, string label, string path, double value, bool integer) {
        var box = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture) };
        AutomationProperties.SetAutomationId(box, path);
        Row(panel, label, box);
        Track(path, () => box.Text, () => {
            if (integer && int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) {
                return JsonValue.Create(n);
            }
            if (!integer && double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)) {
                return JsonValue.Create(d);
            }
            throw new InvalidDataException($"{label}: enter a valid {(integer ? "whole number" : "number")}.");
        });
        box.TextChanged += (_, _) => RefreshDirty();
    }

    private void RefreshDirty() {
        if (_loading) { return; }
        _dirty = _fields.Values.Any(f => f.Current() != f.Original);
        DirtyLabel.Text = _dirty ? "Unsaved changes" : _pendingApply ? "Saved · apply pending" : "No unsaved changes";
        SaveButton.IsEnabled = _dirty || _pendingApply;
        DiscardButton.IsEnabled = true; // Also reloads external edits.
    }

    private void CategoryChanged(object sender, SelectionChangedEventArgs e) {
        if (PageHost is not null && Categories.SelectedIndex >= 0 && _pages.Count > Categories.SelectedIndex) {
            PageHost.Content = _pages[Categories.SelectedIndex];
        }
    }

    private void SaveClicked(object sender, RoutedEventArgs e) {
        var persisted = false;
        try {
            if (_dirty) {
                var changes = _fields.Where(p => p.Value.Current() != p.Value.Original)
                    .ToDictionary(p => p.Key, p => p.Value.Value());
                _store.Save(changes, _preflight);
                persisted = true;
                _pendingApply = true;
                LoadDraft();
            }
            var issues = _apply();
            _pendingApply = issues.Count > 0;
            ShowStatus(_pendingApply
                ? "Saved, but apply reported issues. Correct them or retry Save & apply:\n" + string.Join("\n", issues)
                : _demo ? "Saved and reloaded demo config. Runtime activation is disabled."
                : "Saved and applied. Reopening Settings reads these values from disk.", _pendingApply);
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
                                    InvalidOperationException or Win32Exception or FormatException) {
            ShowStatus((persisted || _pendingApply ? "Saved, but not fully applied: " : "Not saved: ") + ex.Message, error: true);
        }
        RefreshDirty();
    }

    private void DiscardClicked(object sender, RoutedEventArgs e) {
        if (_dirty && MessageBox.Show(this, "Discard unsaved changes and reload from disk?", "Klikety Settings",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
            return;
        }
        try {
            LoadDraft();
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException) {
            ShowStatus("Cannot reload draft: " + ex.Message, error: true);
        }
    }

    private void ConfirmClose(object? sender, CancelEventArgs e) {
        if ((_dirty || _pendingApply) && MessageBox.Show(this,
                _dirty ? "Discard unsaved changes and close?" : "Settings are saved, but apply has issues. Close anyway?",
                "Klikety Settings", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
            e.Cancel = true;
        }
    }

    private void ShowStatus(string text, bool error = false) {
        Status.Text = text;
        AutomationProperties.SetName(Status, text);
        Status.Foreground = error ? Brushes.DarkRed : new SolidColorBrush(Color.FromRgb(85, 92, 102));
    }
}
