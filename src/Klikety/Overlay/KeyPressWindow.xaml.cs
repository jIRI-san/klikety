using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

using Klikety.Config;

namespace Klikety.Overlay;

/// <summary>
/// Always-on-top, transparent, click-through, non-activating HUD window
/// that displays recent key presses with outlined text.
/// </summary>
public partial class KeyPressWindow : Window {
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    private readonly ObservableCollection<KeyPressDisplayItem> _items;
    private readonly Typeface _typeface;
    private Brush _fillBrush;
    private Brush _outlineBrush;
    private double _fontSize;
    private double _outlineThickness;

    public KeyPressWindow(KeyPressVisualizationConfig config) {
        InitializeComponent();

        _items = [];
        _typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        _fontSize = config.FontSize;
        _outlineThickness = config.OutlineThickness;
        _fillBrush = ParseBrush(config.FontColor, Brushes.Yellow);
        _outlineBrush = ParseBrush(config.OutlineColor, Brushes.Black);

        ItemsHost.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => RebuildVisuals();
    }

    public ObservableCollection<KeyPressDisplayItem> Items => _items;

    protected override void OnSourceInitialized(EventArgs e) {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var existing = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE,
            existing | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private void RebuildVisuals() {
        ItemsHost.Items.Clear();

        foreach (var item in _items) {
            var element = CreateOutlinedTextElement(item);
            ItemsHost.Items.Add(element);
        }
    }

    private System.Windows.Controls.Grid CreateOutlinedTextElement(KeyPressDisplayItem item) {
        var displayText = item.RepeatCount > 1 ? $"{item.Label} ×{item.RepeatCount}" : item.Label;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var ft = new FormattedText(displayText, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, _typeface, _fontSize, _fillBrush, dpi);
        var geometry = ft.BuildGeometry(new Point(0, 0));

        var outline = new Path {
            Data = geometry,
            Fill = Brushes.Transparent,
            Stroke = _outlineBrush,
            StrokeThickness = _outlineThickness * 2,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = item.Opacity,
        };

        var fill = new Path {
            Data = geometry,
            Fill = _fillBrush,
            Opacity = item.Opacity,
        };

        var grid = new System.Windows.Controls.Grid {
            Margin = new Thickness(4, 2, 4, 2),
        };
        grid.Children.Add(outline);
        grid.Children.Add(fill);

        // Subscribe to property changes for live opacity/repeat updates
        item.PropertyChanged += (_, args) => {
            if (args.PropertyName is nameof(KeyPressDisplayItem.Opacity)) {
                outline.Opacity = item.Opacity;
                fill.Opacity = item.Opacity;
            } else if (args.PropertyName is nameof(KeyPressDisplayItem.RepeatCount)) {
                RebuildVisuals();
            }
        };

        return grid;
    }

    private static SolidColorBrush ParseBrush(string hex, SolidColorBrush fallback) {
        try {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        } catch {
            return fallback;
        }
    }
}

/// <summary>
/// A single key press entry displayed in the HUD.
/// Implements INotifyPropertyChanged for live opacity/repeat updates.
/// </summary>
public sealed class KeyPressDisplayItem : INotifyPropertyChanged {
    private double _opacity = 1.0;
    private int _repeatCount = 1;

    public KeyPressDisplayItem(string label) {
        Label = label;
    }

    public string Label { get; }

    public double Opacity {
        get => _opacity;
        set {
            if (_opacity != value) {
                _opacity = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Opacity)));
            }
        }
    }

    public int RepeatCount {
        get => _repeatCount;
        set {
            if (_repeatCount != value) {
                _repeatCount = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RepeatCount)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
