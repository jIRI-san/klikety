using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;

using MouseAction = Klikety.Config.MouseAction;

namespace Klikety;

public partial class SettingsWindow : Window {
    private sealed record Field(string Original, Func<string> Current, Func<JsonNode?> Value);
    private sealed record KeyChoice(VKey? Key, string Label) {
        public override string ToString() => Label;
    }
    private readonly SettingsConfigStore _store;
    private readonly SettingsSaveTransaction _transaction;
    private readonly Action _completeRuntimeOperation;
    private readonly Func<string, bool> _confirmDiscard;
    private readonly Action<string> _openFolder;
    private readonly Func<string, string, string?> _chooseColor;
    private readonly bool _demo;
    private readonly Dictionary<string, Field> _fields = [];
    private readonly Dictionary<string, SettingsModifierPicker> _modifierChoices = [];
    private readonly List<StackPanel> _pages = [];
    private readonly Win32KeyLabelResolver _labels = new();
    private SettingsDraft _draft = new(new ConfigModel());
    private ConfigModel _loadedConfig = new();
    private bool _loading;
    private bool _dirty;
    private bool _pendingApply;
    private bool _requiresReload;
    internal string LoadedTheme => _loadedConfig.Theme;

    internal SettingsWindow(
        string path,
        bool demo,
        Action<ConfigModel> preflight,
        Func<SettingsRuntimeSnapshot> captureRuntime,
        Func<ConfigModel, SettingsApplyOutcome> apply,
        Func<SettingsRuntimeSnapshot, SettingsApplyOutcome> restoreRuntime,
        SettingsOperationGate? operationGate = null,
        Action? completeRuntimeOperation = null,
        Action<string>? fault = null,
        Func<string, bool>? confirmDiscard = null,
        Action<string>? openFolder = null,
        Func<string, string, string?>? chooseColor = null) {
        InitializeComponent();
        _store = new SettingsConfigStore(path, fault);
        _transaction = new SettingsSaveTransaction(_store, operationGate ?? new SettingsOperationGate(() => true),
            preflight, captureRuntime, apply, restoreRuntime);
        _completeRuntimeOperation = completeRuntimeOperation ?? (() => { });
        _confirmDiscard = confirmDiscard ?? (message => MessageBox.Show(this, message, "Klikety Settings",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        _openFolder = openFolder ?? OpenInExplorer;
        _chooseColor = chooseColor ?? ShowColorDialog;
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
        _loadedConfig = config;
        _requiresReload = false;
        _pendingApply = false;
        _draft = new SettingsDraft(config);
        _loading = true;
        _fields.Clear();
        _modifierChoices.Clear();
        _pages.Clear();

        var general = Page("General", "The shortcut that brings navigation to your screen.");
        var hotkey = Card(general, "Activation hotkey");
        Modifiers(hotkey, "hotKey.modifiers", config.HotKey.Modifiers);
        KeyPicker(hotkey, "Trigger key", "hotKey.key", config.HotKey.Key, nullable: false);
        Hint(hotkey, "Choose named keys from the picker. Collisions are checked before writing.");
        var generalAdvanced = Advanced(general);
        Choice(generalAdvanced, "Minimum log level", "logLevel", config.LogLevel,
            ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"]);
        Toggle(generalAdvanced, "Write logs to files", "fileLoggingEnabled", config.FileLoggingEnabled);
        Number(generalAdvanced, "Retained log files", "retainedLogFileCount", config.RetainedLogFileCount, integer: true);
        var metadata = Card(generalAdvanced, "Configuration metadata");
        ReadOnlyValue(metadata, "Config file", _store.FilePath, "metadata.configPath",
            () => OpenFolder(_store.FilePath, filePath: true));
        ReadOnlyValue(metadata, "Config version", config.ConfigVersion.ToString(CultureInfo.InvariantCulture), "metadata.configVersion");
        var schema = _store.SchemaReference;
        var schemaUri = Uri.TryCreate(schema, UriKind.Absolute, out var parsedSchema) ? parsedSchema : null;
        Action? openSchemaFolder = schema != "(not specified)" && (schemaUri is null || schemaUri.IsFile)
            ? () => OpenFolder(schemaUri?.LocalPath ?? schema, filePath: true)
            : null;
        ReadOnlyValue(metadata, "$schema", schema, "metadata.schemaReference", openSchemaFolder);
        Hint(generalAdvanced, "Start with Windows remains a registry toggle in the tray. It is not saved to JSON.");

        var navigation = Page("Navigation", "Pick a starting mode, then choose how each mode responds.");
        var defaultCard = Card(navigation, "Start in");
        var modeNames = new[] { "uniformGrid", "crosshair", "logCrosshair", "logGrid", "elementHints" };
        var modeLabels = new[] { "Uniform grid", "Crosshair", "Log crosshair", "Log grid", "Element hints" };
        var modes = new[] { config.Modes.UniformGrid, config.Modes.Crosshair, config.Modes.LogCrosshair, config.Modes.LogGrid, config.Modes.ElementHints };
        var defaultPicker = new ComboBox {
            ItemsSource = modeLabels,
            SelectedIndex = modes.Count(mode => mode.Default) == 1 ? Array.FindIndex(modes, mode => mode.Default) : -1,
        };
        // Preserve invalid persisted defaults until the user explicitly chooses one.
        bool defaultChanged = false;
        Row(defaultCard, "Default mode", defaultPicker);
        for (var i = 0; i < modes.Length; i++) {
            var index = i;
            var original = modes[i].Default;
            Track($"modes.{modeNames[i]}.default", () => (defaultChanged ? defaultPicker.SelectedIndex == index : original).ToString(),
                () => JsonValue.Create(defaultChanged ? defaultPicker.SelectedIndex == index : original), original.ToString());
        }
        defaultPicker.SelectionChanged += (_, _) => { defaultChanged = true; RefreshDirty(); };
        AutomationProperties.SetAutomationId(defaultPicker, "defaultMode");
        Hint(defaultCard, "Enabled non-default modes need a unique switch chord. With Element hints as default, Enter provides Uniform grid fallback without a separate grid chord.");
        for (var i = 0; i < modes.Length; i++) {
            var card = Card(navigation, modeLabels[i]);
            var prefix = $"modes.{modeNames[i]}";
            Toggle(card, "Enabled", prefix + ".enabled", modes[i].Enabled);
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            card.Children.Add(controls);
            Toggle(controls, modeNames[i] == "elementHints" ? "Arrow focus navigation" : "Arrow keys",
                prefix + ".arrowKeys", modes[i].ArrowKeys);
            Toggle(controls, modeNames[i] == "elementHints" ? "Adaptive one/two-key labels" : "Two-key selection",
                prefix + ".twoKey", modes[i].TwoKey);
            KeyPicker(card, "Switch chord", prefix + ".chordKey", modes[i].ChordKey, nullable: true);
            if (modeNames[i] == "elementHints") {
                Hint(card, "Keep adaptive labels and Uniform grid enabled. Completed small levels use one key; growing levels freeze two-key labels. + opens a nested control group. Arrows optionally focus controls, never switch pages. PgUp/PgDn page only when needed; Esc goes back; Enter opens the grid.");
                Number(card, "Discovery timeout (ms)", prefix + ".discoveryTimeoutMs", modes[i].DiscoveryTimeoutMs, integer: true);
                Hint(card, "100-60000 ms, including helper startup. Increase this for slow applications such as Word; Enter or Esc still exits while loading.");
                Hint(card, "Controls appear as discovery progresses. Child counts never stop discovery. Groups and pages are used when labels cannot fit; existing key labels stay unchanged.");
                Number(card, "Cached windows", prefix + ".cacheWindowCount", modes[i].CacheWindowCount, integer: true);
                Hint(card, "0-20 recently used windows; default 5. Each window counts separately. 0 disables reuse. Actions always validate fresh controls.");
            }
            var modeAdvanced = Advanced(card);
            Number(modeAdvanced, "Log crosshair center (px)", prefix + ".logBaseSize", modes[i].LogBaseSize, integer: true);
            Number(modeAdvanced, "Log grid center (px)", prefix + ".logGridBaseSize", modes[i].LogGridBaseSize, integer: true);
            Hint(modeAdvanced, "Only the matching logarithmic mode uses each value.");
            Hint(modeAdvanced, "Disabled modes keep their values. Size fields are inert outside the matching logarithmic mode.");
        }
        var scope = Card(navigation, "Current-window scope");
        KeyPicker(scope, "Scope chord", "appScope.chordKey", config.AppScope.ChordKey, nullable: true);
        Hint(scope, "None disables current-window targeting. This chord must not overlap other bindings.");
        var sizing = Advanced(navigation);
        Number(sizing, "Level-3 threshold (px²)", "level3CellSizeThreshold", config.Level3CellSizeThreshold, integer: true);
        Hint(sizing, "Center sizes: 2–50 px. Level-3 area threshold: 0 means always available.");

        var bindings = Page("Key bindings", "Assign physical keys to actions and ordered navigation axes.");
        var help = Card(bindings, "Keyboard help");
        Toggle(help, "Enable overlay help", "helpBinding.enabled", config.HelpBinding.Enabled);
        KeyPicker(help, "Help key", "helpBinding.key", config.HelpBinding.Key, nullable: false);
        Toggle(help, "Require Shift", "helpBinding.requireShift", config.HelpBinding.RequireShift);
        Hint(help, "Opens help while navigation is visible. Shift is optional unless required; Ctrl, Alt and Win do not match.");
        ActionBindingsEditor(bindings, config.ActionBindings);
        var axis = Card(bindings, "Navigation axes");
        KeyListEditor(axis, "Horizontal keys", "horizontalKeys", config.HorizontalKeys);
        KeyListEditor(axis, "Vertical keys", "verticalKeys", config.VerticalKeys);

        var appearance = Page("Appearance", "Change the overlay, not the Windows-native settings interface.");
        var appearanceCard = Card(appearance, "Overlay labels");
        TextEntry(appearanceCard, "Theme reference", "theme", config.Theme);
        Number(appearanceCard, "Minimum label size (DIP)", "minLabelFontSize", config.MinLabelFontSize, integer: false);
        Hint(appearanceCard, "Labels auto-scale. This size is the readability floor, not a fixed font size.");
        var appearanceAdvanced = Advanced(appearance);
        var themeFolder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_store.FilePath)!, "themes");
        ReadOnlyValue(appearanceAdvanced, "Theme folder", themeFolder, "metadata.themeFolder",
            () => OpenFolder(themeFolder));
        Hint(appearanceAdvanced, "Theme contents stay in their separate files. Unchanged loader fallback warnings remain advisory.");

        var scrolling = Page("Scrolling", "Configure scroll shortcuts and amount. Pause remains a runtime tray control.");
        Toggle(scrolling, "Enable scroll hotkeys", "scrollHotkeys.enabled", config.ScrollHotKeys.Enabled);
        HotKeyEditor(scrolling, "Scroll up", "scrollHotkeys.scrollUpKey", config.ScrollHotKeys.ScrollUpKey);
        HotKeyEditor(scrolling, "Scroll down", "scrollHotkeys.scrollDownKey", config.ScrollHotKeys.ScrollDownKey);
        Number(scrolling, "Scroll amount", "scrollHotkeys.scrollAmount", config.ScrollHotKeys.ScrollAmount, integer: true);
        Hint(scrolling, "Pause is kept while scrolling remains enabled. Disabling removes scroll registrations and pause state.");

        var macros = Page("Macros", "Edit macro hotkeys and playback visuals. Recorded macro data stays in macros.json.");
        Toggle(macros, "Enable macro controls", "macros.enabled", config.Macros.Enabled);
        NullableHotKeyEditor(macros, "Global macro hotkey", "macros.globalHotKey", config.Macros.GlobalHotKey);
        KeyPicker(macros, "Record key", "macros.recordKey", config.Macros.RecordKey, nullable: false);
        KeyPicker(macros, "Helper key", "macros.helperKey", config.Macros.HelperKey, nullable: false);
        KeyListEditor(macros, "Ordered slot keys", "macros.slotKeys", config.Macros.SlotKeys ?? []);
        Number(macros, "Playback speed modifier", "macros.speedModifier", config.Macros.SpeedModifier, integer: false);
        var playback = Advanced(macros);
        TextEntry(playback, "Indicator fill color", "macros.playbackIndicator.fillColor", config.Macros.PlaybackIndicator.FillColor, color: true);
        TextEntry(playback, "Indicator stroke color", "macros.playbackIndicator.strokeColor", config.Macros.PlaybackIndicator.StrokeColor, color: true);
        Number(playback, "Stroke thickness", "macros.playbackIndicator.strokeThickness", config.Macros.PlaybackIndicator.StrokeThickness, integer: false);
        Number(playback, "Initial radius", "macros.playbackIndicator.initialRadius", config.Macros.PlaybackIndicator.InitialRadius, integer: false);
        Number(playback, "Final radius", "macros.playbackIndicator.finalRadius", config.Macros.PlaybackIndicator.FinalRadius, integer: false);
        Number(playback, "Animation duration (ms)", "macros.playbackIndicator.animationDurationMs", config.Macros.PlaybackIndicator.AnimationDurationMs, integer: true);

        var hud = Page("Key-press HUD", "Configure the visualization appearance. Show Key Presses remains a runtime tray control.");
        var hudCommon = Card(hud, "Display");
        Number(hudCommon, "Font size", "keyPressVisualization.fontSize", config.KeyPressVisualization.FontSize, integer: false);
        TextEntry(hudCommon, "Font color", "keyPressVisualization.fontColor", config.KeyPressVisualization.FontColor, color: true);
        Choice(hudCommon, "Corner", "keyPressVisualization.corner", config.KeyPressVisualization.Corner,
            ["TopLeft", "TopRight", "BottomLeft", "BottomRight"]);
        Number(hudCommon, "Maximum visible keys", "keyPressVisualization.maxVisibleKeys", config.KeyPressVisualization.MaxVisibleKeys, integer: true);
        var hudAdvanced = Advanced(hud);
        TextEntry(hudAdvanced, "Outline color", "keyPressVisualization.outlineColor", config.KeyPressVisualization.OutlineColor, color: true);
        Number(hudAdvanced, "Outline thickness", "keyPressVisualization.outlineThickness", config.KeyPressVisualization.OutlineThickness, integer: false);
        Number(hudAdvanced, "Fade timeout (ms)", "keyPressVisualization.fadeTimeoutMs", config.KeyPressVisualization.FadeTimeoutMs, integer: true);
        Number(hudAdvanced, "Fade duration (ms)", "keyPressVisualization.fadeDurationMs", config.KeyPressVisualization.FadeDurationMs, integer: true);
        Number(hudAdvanced, "Margin", "keyPressVisualization.margin", config.KeyPressVisualization.Margin, integer: false);
        Number(hudAdvanced, "Repeat window (ms)", "keyPressVisualization.repeatWindowMs", config.KeyPressVisualization.RepeatWindowMs, integer: true);
        _loading = false;
        PageHost.Content = _pages[Math.Max(0, Categories.SelectedIndex)];
        RefreshDirty();
        if (result.SettingsBlockingErrors.Count > 0) {
            ShowStatus("Existing config errors (fix before saving):\n" +
                string.Join("\n", result.SettingsBlockingErrors), error: true);
        } else if (result.SettingsWarnings.Count > 0) {
            ShowStatus("Ready. Compatibility warnings do not block saving:\n" +
                string.Join("\n", result.SettingsWarnings));
        } else {
            ShowStatus("Ready. Only changed values are written. Save creates config.json.settings.bak.");
        }
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
        var border = new Border {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(16), Margin = new Thickness(0, 14, 0, 0), Child = content,
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        page.Children.Add(border);
        return content;
    }

    private static StackPanel Advanced(Panel page) {
        var content = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        page.Children.Add(new Expander { Header = "Advanced", Content = content });
        return content;
    }

    private static void Hint(Panel panel, string text) {
        var hint = new TextBlock {
            Text = text, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0), FontSize = 13,
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(hint);
    }

    private static void Row(Panel panel, string label, UIElement control, UIElement? labelTarget = null) {
        AutomationProperties.SetName(labelTarget ?? control, label);
        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(215) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var caption = new Label {
            Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
            Target = labelTarget ?? control, Padding = new Thickness(0, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(caption);
        System.Windows.Controls.Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        panel.Children.Add(grid);
    }

    private static void ReadOnlyValue(Panel panel, string label, string value, string automationId, Action? openFolder = null) {
        var box = new TextBox { Text = value, IsReadOnly = true, IsTabStop = false };
        AutomationProperties.SetAutomationId(box, automationId);
        if (openFolder is null) {
            Row(panel, label, box);
            return;
        }
        var button = new Button { Content = "Open folder", ToolTip = value };
        AutomationProperties.SetAutomationId(button, automationId + ".open");
        AutomationProperties.SetName(button, $"Open {label}");
        button.Click += (_, _) => openFolder();
        var input = new DockPanel();
        DockPanel.SetDock(button, Dock.Right);
        input.Children.Add(button);
        input.Children.Add(box);
        Row(panel, label, input, box);
    }

    private static void OpenInExplorer(string folder) {
        if (!Directory.Exists(folder)) { throw new DirectoryNotFoundException($"Folder is unavailable: {folder}"); }
        var start = new ProcessStartInfo("explorer.exe");
        start.ArgumentList.Add(folder);
        if (Process.Start(start) is null) { throw new InvalidOperationException("Windows Explorer did not start."); }
    }

    private void OpenFolder(string path, bool filePath = false) {
        try {
            var fullPath = System.IO.Path.GetFullPath(path, System.IO.Path.GetDirectoryName(_store.FilePath)!);
            _openFolder(filePath ? System.IO.Path.GetDirectoryName(fullPath)! : fullPath);
        } catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or
                                    InvalidOperationException or ArgumentException or NotSupportedException) {
            ShowStatus("Cannot open folder: " + ex.Message, error: true);
        }
    }

    private string? ShowColorDialog(string label, string value) {
        var dialog = new SettingsColorDialog(value, label) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.SelectedHex : null;
    }

    private void TextEntry(Panel panel, string label, string path, string value, bool color = false) {
        var box = new TextBox { Text = value };
        AutomationProperties.SetAutomationId(box, path);
        if (color) {
            var button = new Button { Content = "Choose color", ToolTip = "Choose RGB and opacity" };
            AutomationProperties.SetAutomationId(button, path + ".pick");
            AutomationProperties.SetName(button, $"Choose {label}");
            button.Click += (_, _) => {
                try {
                    if (!ConfigLoader.IsValidHexColor(box.Text)) {
                        throw new InvalidDataException("Enter #RRGGBB or #AARRGGBB before opening the color picker.");
                    }
                    if (_chooseColor(label, box.Text) is { } selected) {
                        if (!ConfigLoader.IsValidHexColor(selected)) {
                            throw new InvalidDataException("The color picker returned an invalid hex color.");
                        }
                        box.Text = selected;
                    }
                } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) {
                    ShowStatus($"{path}: {ex.Message}", error: true);
                    box.Focus();
                }
            };
            var input = new DockPanel();
            DockPanel.SetDock(button, Dock.Right);
            input.Children.Add(button);
            input.Children.Add(box);
            Row(panel, label, input, box);
        } else {
            Row(panel, label, box);
        }
        Track(path, () => box.Text, () => JsonValue.Create(box.Text));
        box.TextChanged += (_, _) => RefreshDirty();
    }

    private void HotKeyEditor(Panel panel, string label, string path, HotKeyConfig value) {
        var card = Card(panel, label);
        Modifiers(card, path + ".modifiers", value.Modifiers);
        KeyPicker(card, "Trigger key", path + ".key", value.Key, nullable: false);
    }

    private void NullableHotKeyEditor(Panel panel, string label, string path, HotKeyConfig? value) {
        var card = Card(panel, label);
        var enabled = new CheckBox { Content = "Enable global macro hotkey", IsChecked = value is not null };
        card.Children.Add(enabled);
        var modifiersValue = value?.Modifiers ?? (HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift);
        var keyValue = value?.Key ?? VKey.M;
        var modifiers = new SettingsModifierPicker(modifiersValue) {
            IsEnabled = value is not null,
        };
        var keyChoices = KeyChoices(keyValue);
        var key = new ComboBox {
            ItemsSource = keyChoices,
            DisplayMemberPath = nameof(KeyChoice.Label),
            SelectedValuePath = nameof(KeyChoice.Key),
            SelectedItem = keyChoices.First(choice => choice.Key == keyValue),
            IsEnabled = value is not null,
        };
        AutomationProperties.SetAutomationId(enabled, path);
        AutomationProperties.SetAutomationId(modifiers, path + ".modifiers");
        AutomationProperties.SetAutomationId(key, path + ".key");
        _modifierChoices[path + ".modifiers"] = modifiers;
        Row(card, "Modifiers", modifiers);
        Row(card, "Trigger key", key);
        var capture = CaptureButton(path + ".key.capture", key, selected => {
            key.SelectedItem = keyChoices.First(choice => choice.Key == selected);
            RefreshDirty();
        });
        card.Children.Add(capture);

        JsonNode? CurrentValue() => enabled.IsChecked == true
            ? new JsonObject {
                ["modifiers"] = JsonValue.Create(modifiers.Value.ToString()),
                ["key"] = JsonValue.Create(((KeyChoice)key.SelectedItem).Key!.Value.ToString()),
            }
            : null;
        string CurrentText() => CurrentValue()?.ToJsonString() ?? "null";
        Track(path, CurrentText, CurrentValue,
            value is null ? "null" : new JsonObject {
                ["modifiers"] = JsonValue.Create(value.Modifiers.ToString()),
                ["key"] = JsonValue.Create(value.Key.ToString()),
            }.ToJsonString());
        void Changed() {
            modifiers.IsEnabled = enabled.IsChecked == true;
            key.IsEnabled = enabled.IsChecked == true;
            RefreshDirty();
        }
        enabled.Checked += (_, _) => Changed();
        enabled.Unchecked += (_, _) => Changed();
        modifiers.ValueChanged += (_, _) => RefreshDirty();
        key.SelectionChanged += (_, _) => RefreshDirty();
        enabled.Checked += (_, _) => { key.IsEnabled = true; capture.IsEnabled = true; };
        enabled.Unchecked += (_, _) => { key.IsEnabled = false; capture.IsEnabled = false; };
        capture.IsEnabled = value is not null;
    }

    private void KeyListEditor(Panel panel, string label, string path, VKey[] initial) {
        var card = Card(panel, label);
        var rows = new StackPanel();
        card.Children.Add(rows);
        var values = initial.ToList();
        JsonArray CurrentValue() => new(values.Select(key => JsonValue.Create(key.ToString())).ToArray());
        Track(path, () => CurrentValue().ToJsonString(), () => CurrentValue());

        void Render() {
            rows.Children.Clear();
            for (var i = 0; i < values.Count; i++) {
                var index = i;
                var choices = KeyChoices(values[index]);
                var picker = new ComboBox {
                    ItemsSource = choices,
                    DisplayMemberPath = nameof(KeyChoice.Label),
                    SelectedValuePath = nameof(KeyChoice.Key),
                    SelectedItem = choices.First(choice => choice.Key == values[index]),
                    MinWidth = 190,
                };
                AutomationProperties.SetAutomationId(picker, $"{path}.item.{index}");
                AutomationProperties.SetName(picker, $"{label} item {index + 1}");
                var capture = CaptureButton($"{path}.item.{index}.capture", picker, selected => {
                    picker.SelectedItem = choices.First(choice => choice.Key == selected);
                    values[index] = selected;
                    RefreshDirty();
                });
                picker.SelectionChanged += (_, _) => {
                    if (picker.SelectedItem is KeyChoice { Key: { } selected }) {
                        values[index] = selected;
                        RefreshDirty();
                    }
                };
                var up = MoveButton(up: true, enabled: index > 0);
                AutomationProperties.SetAutomationId(up, $"{path}.item.{index}.up");
                AutomationProperties.SetName(up, $"Move {label} item {index + 1} up");
                up.Click += (_, _) => {
                    (values[index - 1], values[index]) = (values[index], values[index - 1]);
                    Render(); RefreshDirty(); FocusEditor(rows, $"{path}.item.{index - 1}");
                };
                var down = MoveButton(up: false, enabled: index + 1 < values.Count);
                AutomationProperties.SetAutomationId(down, $"{path}.item.{index}.down");
                AutomationProperties.SetName(down, $"Move {label} item {index + 1} down");
                down.Click += (_, _) => {
                    (values[index + 1], values[index]) = (values[index], values[index + 1]);
                    Render(); RefreshDirty(); FocusEditor(rows, $"{path}.item.{index + 1}");
                };
                var remove = new Button { Content = "Remove" };
                AutomationProperties.SetAutomationId(remove, $"{path}.item.{index}.remove");
                AutomationProperties.SetName(remove, $"Remove {label} item {index + 1}");
                remove.Click += (_, _) => {
                    values.RemoveAt(index); Render(); RefreshDirty();
                    FocusEditor(rows, values.Count == 0 ? path + ".add.key" : $"{path}.item.{Math.Min(index, values.Count - 1)}");
                };
                var row = new WrapPanel();
                row.Children.Add(picker);
                row.Children.Add(capture);
                row.Children.Add(up);
                row.Children.Add(down);
                row.Children.Add(remove);
                rows.Children.Add(row);
            }
            var addPicker = new ComboBox {
                ItemsSource = KeyChoices(),
                DisplayMemberPath = nameof(KeyChoice.Label),
                SelectedValuePath = nameof(KeyChoice.Key),
                SelectedIndex = 0,
                MinWidth = 190,
            };
            AutomationProperties.SetName(addPicker, $"Add {label} key");
            AutomationProperties.SetAutomationId(addPicker, $"{path}.add.key");
            var addCapture = CaptureButton($"{path}.add.capture", addPicker, selected =>
                addPicker.SelectedItem = ((IEnumerable<KeyChoice>)addPicker.ItemsSource!).First(choice => choice.Key == selected));
            var add = new Button { Content = "Add key" };
            AutomationProperties.SetAutomationId(add, $"{path}.add");
            AutomationProperties.SetName(add, $"Add {label} key");
            add.Click += (_, _) => {
                if (addPicker.SelectedItem is KeyChoice { Key: { } selected }) {
                    values.Add(selected);
                    Render();
                    RefreshDirty();
                    FocusEditor(rows, $"{path}.item.{values.Count - 1}");
                }
            };
            var addRow = new WrapPanel();
            addRow.Children.Add(addPicker);
            addRow.Children.Add(addCapture);
            addRow.Children.Add(add);
            rows.Children.Add(addRow);
        }

        Render();
    }

    private static Button MoveButton(bool up, bool enabled) {
        var icon = new System.Windows.Shapes.Path {
            Data = Geometry.Parse(up ? "M 2,6 L 6,2 L 10,6 M 6,2 L 6,12" : "M 2,8 L 6,12 L 10,8 M 6,2 L 6,12"),
            Width = 12, Height = 14, StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var button = new Button {
            Content = icon, ToolTip = up ? "Move up" : "Move down", IsEnabled = enabled,
            Width = 36, Padding = new Thickness(8),
        };
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { Source = button });
        return button;
    }

    private void ActionBindingsEditor(Panel panel, Dictionary<string, MouseAction> initial) {
        var card = Card(panel, "Action bindings");
        Hint(card, "Space always means LeftClick unless explicitly overridden. All six mouse actions are available.");
        var rows = new StackPanel();
        card.Children.Add(rows);
        var entries = initial.Select(pair => (pair.Key, pair.Value)).ToList();
        JsonObject CurrentValue() {
            var result = new JsonObject();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, action) in entries) {
                if (!keys.Add(key)) {
                    throw new InvalidDataException($"actionBindings: key '{key}' is assigned more than once.");
                }
                result[key] = JsonValue.Create(action.ToString());
            }
            return result;
        }
        string CurrentText() => new JsonArray(entries.Select(entry => (JsonNode)new JsonArray(entry.Key, entry.Value.ToString())).ToArray()).ToJsonString();
        Track("actionBindings", CurrentText, () => CurrentValue());

        void Render() {
            rows.Children.Clear();
            for (var i = 0; i < entries.Count; i++) {
                var index = i;
                var key = entries[index].Key;
                var line = new WrapPanel();
                if (Enum.TryParse<VKey>(key, ignoreCase: true, out var selectedKey) && Enum.IsDefined(selectedKey)) {
                    var choices = KeyChoices();
                    var picker = new ComboBox {
                        ItemsSource = choices,
                        DisplayMemberPath = nameof(KeyChoice.Label),
                        SelectedValuePath = nameof(KeyChoice.Key),
                        SelectedItem = choices.First(choice => choice.Key == selectedKey),
                        MinWidth = 190,
                    };
                    AutomationProperties.SetAutomationId(picker, $"actionBindings.item.{index}.key");
                    AutomationProperties.SetName(picker, $"Action binding {index + 1} key");
                    line.Children.Add(picker);
                    line.Children.Add(CaptureButton($"actionBindings.item.{index}.capture", picker, selected =>
                        picker.SelectedItem = choices.First(choice => choice.Key == selected)));
                    picker.SelectionChanged += (_, _) => {
                        if (picker.SelectedItem is KeyChoice { Key: { } selected }) {
                            entries[index] = (selected.ToString(), entries[index].Value);
                            RefreshDirty();
                        }
                    };
                } else {
                    var rawKey = new TextBox { Text = key, MinWidth = 190 };
                    AutomationProperties.SetName(rawKey, $"Action binding {index + 1} key (unrecognized)");
                    rawKey.TextChanged += (_, _) => { entries[index] = (rawKey.Text, entries[index].Value); RefreshDirty(); };
                    line.Children.Add(rawKey);
                }
                var actionPicker = new ComboBox {
                    ItemsSource = Enum.GetValues<MouseAction>(),
                    SelectedItem = entries[index].Value,
                    MinWidth = 170,
                };
                AutomationProperties.SetAutomationId(actionPicker, $"actionBindings.item.{index}.action");
                AutomationProperties.SetName(actionPicker, $"Action binding {index + 1} action");
                actionPicker.SelectionChanged += (_, _) => {
                    if (actionPicker.SelectedItem is MouseAction action) {
                        entries[index] = (entries[index].Key, action);
                        RefreshDirty();
                    }
                };
                line.Children.Add(actionPicker);
                var remove = new Button { Content = "Remove" };
                AutomationProperties.SetAutomationId(remove, $"actionBindings.item.{index}.remove");
                AutomationProperties.SetName(remove, $"Remove action binding {index + 1}");
                remove.Click += (_, _) => {
                    entries.RemoveAt(index); Render(); RefreshDirty();
                    FocusEditor(rows, entries.Count == 0 ? "actionBindings.add.key" : $"actionBindings.item.{Math.Min(index, entries.Count - 1)}.action");
                };
                line.Children.Add(remove);
                rows.Children.Add(line);
            }

            var addKey = new ComboBox {
                ItemsSource = KeyChoices(),
                DisplayMemberPath = nameof(KeyChoice.Label),
                SelectedValuePath = nameof(KeyChoice.Key),
                SelectedIndex = 0,
                MinWidth = 190,
            };
            AutomationProperties.SetAutomationId(addKey, "actionBindings.add.key");
            AutomationProperties.SetName(addKey, "Add action binding key");
            var capture = CaptureButton("actionBindings.add.capture", addKey, key => addKey.SelectedValue = key);
            var addAction = new ComboBox { ItemsSource = Enum.GetValues<MouseAction>(), SelectedItem = MouseAction.LeftClick };
            AutomationProperties.SetAutomationId(addAction, "actionBindings.add.action");
            AutomationProperties.SetName(addAction, "Add action binding action");
            var add = new Button { Content = "Add binding" };
            AutomationProperties.SetAutomationId(add, "actionBindings.add");
            add.Click += (_, _) => {
                if (addKey.SelectedItem is KeyChoice { Key: { } key } && addAction.SelectedItem is MouseAction action &&
                    !entries.Any(entry => string.Equals(entry.Key, key.ToString(), StringComparison.OrdinalIgnoreCase))) {
                    entries.Add((key.ToString(), action));
                    Render();
                    RefreshDirty();
                    FocusEditor(rows, $"actionBindings.item.{entries.Count - 1}.action");
                } else {
                    ShowStatus("Choose an action key that is not already assigned.", error: true);
                }
            };
            var addRow = new WrapPanel();
            addRow.Children.Add(addKey);
            addRow.Children.Add(capture);
            addRow.Children.Add(addAction);
            addRow.Children.Add(add);
            rows.Children.Add(addRow);
        }

        Render();
    }

    private List<KeyChoice> KeyChoices(VKey? existing = null) {
        var choices = Enum.GetValues<VKey>().Select(key => new KeyChoice(key, KeyLabel(key))).ToList();
        if (existing is { } value && !Enum.IsDefined(value)) {
            choices.Add(new KeyChoice(value, $"Unrecognized physical key ({(int)value})"));
        }
        return choices;
    }

    private Button CaptureButton(string automationId, ComboBox picker, Action<VKey> captured) {
        var button = new Button { Content = "Capture key", Margin = new Thickness(6, 0, 0, 0) };
        AutomationProperties.SetAutomationId(button, automationId);
        var capture = new SettingsKeyCapture();
        AutomationProperties.SetName(button, $"Capture {AutomationProperties.GetName(picker)}");
        void StopCapture(string message) {
            capture.Cancel();
            button.Content = "Capture key";
            if (message.Length > 0) { ShowStatus(message); }
        }
        button.Click += (_, _) => {
            capture.Begin();
            button.Content = "Press a key (Esc cancels)";
            ShowStatus("Focused capture is active. Press Esc to cancel or choose a key; moving focus cancels.");
            picker.Focus();
        };
        picker.PreviewKeyDown += (_, e) => {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var result = capture.Process(key, e.IsRepeat, Keyboard.Modifiers);
            if (result.Kind == SettingsCaptureKind.Inactive) { return; }
            if (result.Kind == SettingsCaptureKind.Cancelled) {
                StopCapture("Key capture cancelled.");
                e.Handled = true;
                return;
            }
            if (result.Kind == SettingsCaptureKind.Ignored) {
                e.Handled = true;
                return;
            }
            if (result.Kind == SettingsCaptureKind.Unsupported) {
                ShowStatus("This key is not supported by Klikety; choose a key from the picker.", error: true);
                e.Handled = true;
                return;
            }
            var modifiersPath = automationId.EndsWith(".key.capture", StringComparison.Ordinal)
                ? automationId[..^".key.capture".Length] + ".modifiers"
                : null;
            if (modifiersPath is not null && _modifierChoices.TryGetValue(modifiersPath, out var modifiers)) {
                modifiers.Value = result.Modifiers;
            }
            captured(result.Key!.Value);
            StopCapture("Key captured.");
            e.Handled = true;
        };
        picker.LostKeyboardFocus += (_, _) => {
            if (capture.IsActive) { StopCapture("Key capture cancelled because focus moved."); }
        };
        return button;
    }

    private void Track(string path, Func<string> current, Func<JsonNode?> value, string? original = null) =>
        _fields.Add(path, new Field(original ?? current(), current, value));

    private void Toggle(Panel panel, string label, string path, bool value) {
        var box = new CheckBox { Content = label, IsChecked = value };
        AutomationProperties.SetAutomationId(box, path);
        AutomationProperties.SetName(box, label);
        panel.Children.Add(box);
        Track(path, () => (box.IsChecked == true).ToString(), () => JsonValue.Create(box.IsChecked == true));
        box.Checked += (_, _) => RefreshDirty();
        box.Unchecked += (_, _) => RefreshDirty();
    }

    private void Choice(Panel panel, string label, string path, string value, string[] choices) {
        var box = new ComboBox { ItemsSource = choices.Append(value).Distinct().ToArray(), SelectedItem = value };
        AutomationProperties.SetAutomationId(box, path);
        Row(panel, label, box);
        Track(path, () => (string)box.SelectedItem, () => JsonValue.Create((string)box.SelectedItem));
        box.SelectionChanged += (_, _) => RefreshDirty();
    }

    private void Modifiers(Panel panel, string path, HotKeyModifiers value) {
        var picker = new SettingsModifierPicker(value);
        AutomationProperties.SetAutomationId(picker, path);
        _modifierChoices[path] = picker;
        Row(panel, "Modifiers", picker);
        Track(path, () => picker.Value.ToString(), () => JsonValue.Create(picker.Value.ToString()));
        picker.ValueChanged += (_, _) => RefreshDirty();
    }

    private void KeyPicker(Panel panel, string label, string path, VKey? value, bool nullable) {
        var choices = KeyChoices(value);
        if (nullable) {
            choices.Insert(0, new KeyChoice(null, "None"));
        }
        var box = new ComboBox {
            ItemsSource = choices, DisplayMemberPath = "Label",
            SelectedValuePath = nameof(KeyChoice.Key), SelectedItem = choices.First(c => c.Key == value)
        };
        AutomationProperties.SetAutomationId(box, path);
        var input = new WrapPanel();
        input.Children.Add(box);
        input.Children.Add(CaptureButton(path + ".capture", box, selected =>
            box.SelectedItem = choices.First(choice => choice.Key == selected)));
        Row(panel, label, input, box);
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
            throw new InvalidDataException($"{path}: {label} must be a valid {(integer ? "whole number" : "number")}.");
        });
        box.TextChanged += (_, _) => RefreshDirty();
    }

    private void RefreshDirty() {
        if (_loading) { return; }
        UpdateDraft(ignoreInvalid: true);
        _dirty = _fields.Values.Any(f => f.Current() != f.Original);
        DirtyLabel.Text = _dirty ? "Unsaved changes" : _pendingApply ? "Saved · apply pending" : "No unsaved changes";
        SaveButton.IsEnabled = !_requiresReload && (_dirty || _pendingApply);
        DiscardButton.IsEnabled = true; // Also reloads external edits.
    }

    private void UpdateDraft(bool ignoreInvalid) {
        foreach (var (path, field) in _fields) {
            if (field.Current() == field.Original) {
                _draft.Clear(path);
                continue;
            }
            try {
                _draft.Set(path, field.Value());
            } catch (Exception ex) when (ignoreInvalid && (ex is InvalidDataException or FormatException)) {
                _draft.Clear(path);
            }
        }
    }

    private void CategoryChanged(object sender, SelectionChangedEventArgs e) {
        if (PageHost is not null && Categories.SelectedIndex >= 0 && _pages.Count > Categories.SelectedIndex) {
            PageHost.Content = _pages[Categories.SelectedIndex];
        }
    }

    private void SaveClicked(object sender, RoutedEventArgs e) {
        try {
            if (_requiresReload) { throw new InvalidOperationException("Reload the external config before retrying."); }
            UpdateDraft(ignoreInvalid: false);
            var outcome = _transaction.Execute(_draft.Changes);
            if (outcome.Succeeded) {
                LoadDraft();
            } else {
                _requiresReload = outcome.RequiresReload;
                _pendingApply = true;
                ShowStatus(string.Join("\n", outcome.Issues), error: true);
            }
            if (outcome.Succeeded) {
                var status = _demo ? "Saved and reloaded demo config. Runtime activation is disabled."
                    : "Saved and applied. Reopening Settings reads these values from disk.";
                if (outcome.Issues.Count > 0) {
                    status += "\nNotices:\n" + string.Join("\n", outcome.Issues);
                }
                if (_store.LastWarnings.Count > 0) {
                    status += "\nCompatibility warnings:\n" + string.Join("\n", _store.LastWarnings);
                }
                ShowStatus(status);
            }
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
                                    InvalidOperationException or Win32Exception or FormatException) {
            _requiresReload = _store.RequiresReload;
            ShowStatus("Not saved: " + ex.Message, error: true);
            FocusReportedField(ex.Message);
        } finally {
            try { _completeRuntimeOperation(); } catch (InvalidOperationException ex) {
                _pendingApply = true;
                ShowStatus("Runtime cleanup failed: " + ex.Message, error: true);
            }
        }
        RefreshDirty();
    }

    private void DiscardClicked(object sender, RoutedEventArgs e) {
        if ((_dirty || _pendingApply) && !_confirmDiscard("Discard unsaved changes and reload from disk?")) {
            return;
        }
        try {
            LoadDraft();
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException) {
            ShowStatus("Cannot reload draft: " + ex.Message, error: true);
        }
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void ConfirmClose(object? sender, CancelEventArgs e) {
        if ((_dirty || _pendingApply) && !_confirmDiscard(
                _dirty ? "Discard unsaved changes and close?" : "Settings are saved, but apply has issues. Close anyway?")) {
            e.Cancel = true;
        }
    }

    private void ShowStatus(string text, bool error = false) {
        Status.Text = text;
        AutomationProperties.SetName(Status, text);
        Status.SetResourceReference(TextBlock.ForegroundProperty, error ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) {
            UIElementAutomationPeer.CreatePeerForElement(Status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void FocusReportedField(string message) {
        var path = _fields.Keys
            .Select(field => (Field: field, Index: message.IndexOf(field, StringComparison.OrdinalIgnoreCase)))
            .Where(item => item.Index >= 0)
            .OrderBy(item => item.Index)
            .Select(item => item.Field)
            .FirstOrDefault();
        if (path is null) {
            if (message.Contains("label size", StringComparison.OrdinalIgnoreCase)) { path = "minLabelFontSize"; } else if (message.Contains("Help key", StringComparison.OrdinalIgnoreCase)) { path = "helpBinding.key"; } else if (message.Contains("action binding", StringComparison.OrdinalIgnoreCase)) { path = "actionBindings"; } else if (message.Contains("hotkey", StringComparison.OrdinalIgnoreCase)) { path = "hotKey.key"; } else if (message.Contains("logcrosshair", StringComparison.OrdinalIgnoreCase)) { path = "modes.logCrosshair.logBaseSize"; } else if (message.Contains("loggrid", StringComparison.OrdinalIgnoreCase)) { path = "modes.logGrid.logGridBaseSize"; } else if (message.Contains("horizontal", StringComparison.OrdinalIgnoreCase)) { path = "horizontalKeys"; } else if (message.Contains("vertical", StringComparison.OrdinalIgnoreCase)) { path = "verticalKeys"; } else if (message.Contains("scroll", StringComparison.OrdinalIgnoreCase)) { path = "scrollHotkeys.enabled"; } else { return; }
        }

        Categories.SelectedIndex = path switch {
            var p when p.StartsWith("modes.", StringComparison.OrdinalIgnoreCase) ||
                       p.StartsWith("appScope.", StringComparison.OrdinalIgnoreCase) ||
                       p.StartsWith("level3", StringComparison.OrdinalIgnoreCase) => 1,
            "actionBindings" or "horizontalKeys" or "verticalKeys" => 2,
            var p when p.StartsWith("helpBinding.", StringComparison.OrdinalIgnoreCase) => 2,
            "theme" or "minLabelFontSize" => 3,
            var p when p.StartsWith("scrollHotkeys.", StringComparison.OrdinalIgnoreCase) => 4,
            var p when p.StartsWith("macros.", StringComparison.OrdinalIgnoreCase) => 5,
            var p when p.StartsWith("keyPressVisualization.", StringComparison.OrdinalIgnoreCase) => 6,
            _ => 0,
        };
        PageHost.UpdateLayout();
        var ids = path switch {
            "actionBindings" => new[] { "actionBindings.add.key", "actionBindings.add.action" },
            "macros.globalHotKey" => new[] { path + ".key", path + ".modifiers" },
            _ when path.EndsWith("Keys", StringComparison.Ordinal) => new[] { path + ".item.0", path + ".add.key" },
            _ when path.EndsWith(".default", StringComparison.Ordinal) => ["defaultMode"],
            _ => new[] { path },
        };
        foreach (var id in ids) {
            if (FindAutomationElement(PageHost.Content as DependencyObject, id) is { IsEnabled: true } element) {
                ExpandAdvancedAncestors(element);
                PageHost.UpdateLayout();
                if (element.Focus()) { return; }
            }
        }
    }

    private static void FocusEditor(FrameworkElement root, string id) {
        root.UpdateLayout();
        FindAutomationElement(root, id)?.Focus();
    }

    private static void ExpandAdvancedAncestors(FrameworkElement element) {
        for (var parent = LogicalTreeHelper.GetParent(element); parent is not null; parent = LogicalTreeHelper.GetParent(parent)) {
            if (parent is Expander expander) { expander.IsExpanded = true; }
        }
    }

    internal static FrameworkElement? FindAutomationElement(DependencyObject? root, string id) {
        if (root is null) { return null; }
        if (root is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id) {
            return element;
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) {
            if (FindAutomationElement(child, id) is { } found) { return found; }
        }
        if (root is Visual or System.Windows.Media.Media3D.Visual3D) {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
                if (FindAutomationElement(VisualTreeHelper.GetChild(root, i), id) is { } found) { return found; }
            }
        }
        return null;
    }
}
