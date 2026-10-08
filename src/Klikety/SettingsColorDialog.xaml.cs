using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

using Klikety.Config;

namespace Klikety;

public partial class SettingsColorDialog : Window {
    private readonly string _original;
    private readonly Color _initialColor;
    private readonly (Slider Slider, TextBox Input, string Name)[] _channels;
    private Color _color;
    private bool _ready;
    private bool _synchronizing;

    internal SettingsColorDialog(string original, string label) {
        if (!TryParse(original, out var color)) {
            throw new ArgumentException("Use #RRGGBB or #AARRGGBB to open the color picker.", nameof(original));
        }
        _original = original;
        _initialColor = color;
        InitializeComponent();
        Title = "Choose color - " + label;
        FieldName.Text = label;
        _channels = [
            (RedSlider, RedValue, "Red"), (GreenSlider, GreenValue, "Green"),
            (BlueSlider, BlueValue, "Blue"), (AlphaSlider, AlphaValue, "Opacity"),
        ];
        OriginalSwatch.Background = new SolidColorBrush(color);
        _ready = true;
        SetColor(color, updateHex: true);
        Loaded += (_, _) => { HexValue.Focus(); HexValue.SelectAll(); };
    }

    internal string SelectedHex => _color == _initialColor ? _original
        : _color.A == 255 && _original.Length == 7
            ? $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}"
            : $"#{_color.A:X2}{_color.R:X2}{_color.G:X2}{_color.B:X2}";

    private static bool TryParse(string value, out Color color) {
        color = default;
        if (!ConfigLoader.IsValidHexColor(value)) { return false; }
        color = (Color)ColorConverter.ConvertFromString(value)!;
        return true;
    }

    private void SetColor(Color color, bool updateHex) {
        _synchronizing = true;
        try {
            _color = color;
            byte[] values = [color.R, color.G, color.B, color.A];
            for (var index = 0; index < _channels.Length; index++) {
                _channels[index].Slider.Value = values[index];
                _channels[index].Input.Text = values[index].ToString(CultureInfo.InvariantCulture);
            }
            if (updateHex) { HexValue.Text = SelectedHex; }
            NewSwatch.Background = new SolidColorBrush(color);
            SetStatus("Preview includes opacity over a checkerboard.", error: false);
        } finally { _synchronizing = false; }
    }

    private void ChannelChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (!_ready || _synchronizing) { return; }
        var values = _channels.Select(channel => checked((byte)Math.Round(channel.Slider.Value))).ToArray();
        SetColor(Color.FromArgb(values[3], values[0], values[1], values[2]), updateHex: true);
    }

    private void ChannelTextChanged(object sender, TextChangedEventArgs e) {
        if (!_ready || _synchronizing) { return; }
        var values = new byte[_channels.Length];
        for (var index = 0; index < _channels.Length; index++) {
            if (!byte.TryParse(_channels[index].Input.Text, NumberStyles.None, CultureInfo.InvariantCulture, out values[index])) {
                SetStatus($"{_channels[index].Name}: enter a whole number from 0 to 255. Preview shows the last valid color.", error: true);
                return;
            }
        }
        SetColor(Color.FromArgb(values[3], values[0], values[1], values[2]), updateHex: true);
    }

    private void HexChanged(object sender, TextChangedEventArgs e) {
        if (!_ready || _synchronizing) { return; }
        if (!TryParse(HexValue.Text, out var color)) {
            SetStatus("Enter #RRGGBB or #AARRGGBB. Preview shows the last valid color.", error: true);
            return;
        }
        SetColor(color, updateHex: false);
    }

    private void SetStatus(string message, bool error) {
        OkButton.IsEnabled = !error;
        Status.Text = message;
        Status.SetResourceReference(TextBlock.ForegroundProperty, error ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
        AutomationProperties.SetName(Status, message);
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) {
            UIElementAutomationPeer.CreatePeerForElement(Status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void AcceptClicked(object sender, RoutedEventArgs e) {
        if (OkButton.IsEnabled) { DialogResult = true; }
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;
}
