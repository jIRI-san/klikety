using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

using Klikety.Config;
using Klikety.Interop;

namespace Klikety.Overlay;

/// <summary>
/// Always-on-top, transparent, click-through, non-activating HUD window
/// that displays recent key presses with outlined text.
/// </summary>
public partial class KeyPressWindow : Window {
    private readonly ObservableCollection<KeyPressDisplayItem> _items;
    private readonly Dictionary<KeyPressDisplayItem, PropertyChangedEventHandler> _handlers = [];
    private readonly Typeface _typeface;
    private readonly Brush _fillBrush;
    private readonly Brush _outlineBrush;
    private readonly double _fontSize;
    private readonly double _outlineThickness;
    private bool _isSourceInitialized;

    public KeyPressWindow(KeyPressVisualizationConfig config) {
        InitializeComponent();

        _items = [];
        _typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        _fontSize = config.FontSize;
        _outlineThickness = config.OutlineThickness;
        _fillBrush = ParseBrush(config.FontColor, Brushes.Yellow);
        _outlineBrush = ParseBrush(config.OutlineColor, Brushes.Black);

        _items.CollectionChanged += (_, _) => RebuildVisuals();
    }

    public ObservableCollection<KeyPressDisplayItem> Items => _items;

    protected override void OnSourceInitialized(EventArgs e) {
        base.OnSourceInitialized(e);
        _isSourceInitialized = true;

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetClickThroughExStyle(hwnd);

        RebuildVisuals();
    }

    private void RebuildVisuals() {
        if (!_isSourceInitialized) {
            return;
        }

        // Unsubscribe previous handlers
        foreach (var (item, handler) in _handlers) {
            item.PropertyChanged -= handler;
        }
        _handlers.Clear();

        ItemsHost.Items.Clear();

        foreach (var item in _items) {
            var element = CreateOutlinedTextElement(item);
            ItemsHost.Items.Add(element);
        }
    }

    private System.Windows.Controls.Grid CreateOutlinedTextElement(KeyPressDisplayItem item) {
        var displayText = item.RepeatCount > 1 ? $"{item.Label} \u00d7{item.RepeatCount}" : item.Label;
        var dpi = PresentationSource.FromVisual(this) is PresentationSource src
            ? src.CompositionTarget.TransformToDevice.M11
            : 1.0;

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

        PropertyChangedEventHandler handler = (_, args) => {
            if (args.PropertyName is nameof(KeyPressDisplayItem.Opacity)) {
                outline.Opacity = item.Opacity;
                fill.Opacity = item.Opacity;
            } else if (args.PropertyName is nameof(KeyPressDisplayItem.RepeatCount)) {
                RebuildVisuals();
            }
        };
        item.PropertyChanged += handler;
        _handlers[item] = handler;

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
